from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

from battlecore.domain.hero_attrs import ATTR_BATTLE_LOG_HEADER, ATTR_STAT_HEADER
from battlecore.engine.damage_calculator import get_effective_attr

if TYPE_CHECKING:
    from battlecore.domain.hero import Hero
    from battlecore.engine.battle_context import BattleContext

SPEED_FIRST_STRIKE_GUARANTEE_DIFF = 20
CRIT_DAMAGE_MULTIPLIER_BPS = 20000
CRIT_HEAL_MULTIPLIER_BPS = 20000

_SPEED_FIRST_BREAKPOINTS: tuple[tuple[int, int], ...] = (
    (0, 5000),
    (1, 5500),
    (5, 7000),
    (10, 8000),
    (20, 10000),
)


def calc_speed_first_probability_bps(speed_diff: int) -> int:
    """Return bps probability that the hero with positive speed_diff acts first."""
    if speed_diff >= SPEED_FIRST_STRIKE_GUARANTEE_DIFF:
        return 10000
    if speed_diff <= -SPEED_FIRST_STRIKE_GUARANTEE_DIFF:
        return 0
    if speed_diff == 0:
        return 5000

    positive = speed_diff > 0
    magnitude = abs(speed_diff)
    magnitude_prob = _interpolate_speed_first_bps(magnitude)
    return magnitude_prob if positive else 10000 - magnitude_prob


def _interpolate_speed_first_bps(speed_diff: int) -> int:
    clamped = max(0, min(speed_diff, SPEED_FIRST_STRIKE_GUARANTEE_DIFF))
    for (left_diff, left_prob), (right_diff, right_prob) in zip(
        _SPEED_FIRST_BREAKPOINTS,
        _SPEED_FIRST_BREAKPOINTS[1:],
    ):
        if left_diff <= clamped <= right_diff:
            if left_diff == right_diff:
                return left_prob
            span = right_diff - left_diff
            weight = clamped - left_diff
            return left_prob + (right_prob - left_prob) * weight // span
    return _SPEED_FIRST_BREAKPOINTS[-1][1]


@dataclass(slots=True)
class SpeedMergeDecision:
    slot: int
    hero_a_id: str
    hero_b_id: str
    speed_diff: int
    first_prob_bps: int
    winner_id: str


@dataclass(slots=True)
class RoundActionOrderResult:
    action_order: list[str]
    merge_decisions: list[SpeedMergeDecision] = field(default_factory=list)


def get_effective_speed(hero: Hero) -> int:
    return get_effective_attr(hero, "speed")


def sort_team_action_order(team_id: str, hero_ids: list[str], heroes: dict[str, Hero]) -> list[str]:
    """Within a team, alive heroes always act from highest speed to lowest."""
    return sorted(
        (hero_id for hero_id in hero_ids if heroes[hero_id].is_alive()),
        key=lambda hero_id: (
            -get_effective_speed(heroes[hero_id]),
            heroes[hero_id].position,
            hero_id,
        ),
    )


def decide_first_hero_id(
    context: BattleContext,
    *,
    round_no: int,
    slot: int,
    hero_a_id: str,
    hero_b_id: str,
) -> tuple[str, SpeedMergeDecision]:
    speed_a = get_effective_speed(context.heroes[hero_a_id])
    speed_b = get_effective_speed(context.heroes[hero_b_id])
    speed_diff = speed_a - speed_b
    first_prob_bps = calc_speed_first_probability_bps(speed_diff)

    if first_prob_bps >= 10000:
        winner_id = hero_a_id
    elif first_prob_bps <= 0:
        winner_id = hero_b_id
    else:
        result = context.roll_pseudo_random_probability(
            caster_id=hero_a_id,
            skill_id="round_action_order",
            effect_id=f"{round_no}:{slot}",
            target_id=hero_b_id,
            trigger_type="SPEED_FIRST_STRIKE",
            base_rate_bps=first_prob_bps,
            params={},
        )
        winner_id = hero_a_id if result.allowed else hero_b_id

    decision = SpeedMergeDecision(
        slot=slot,
        hero_a_id=hero_a_id,
        hero_b_id=hero_b_id,
        speed_diff=speed_diff,
        first_prob_bps=first_prob_bps,
        winner_id=winner_id,
    )
    return winner_id, decision


def merge_team_orders_into_global(
    context: BattleContext,
    *,
    round_no: int,
    team_orders: dict[str, list[str]],
) -> RoundActionOrderResult:
    """Merge per-team speed queues into one global order.

    Team-internal order is fixed. Cross-team ordering compares queue heads with
    pseudo-random speed contest at each slot.
    """
    team_ids = sorted(team_orders)
    queues = {team_id: list(team_orders[team_id]) for team_id in team_ids}
    action_order: list[str] = []
    merge_decisions: list[SpeedMergeDecision] = []
    slot = 0

    while True:
        active_teams = [team_id for team_id in team_ids if queues[team_id]]
        if not active_teams:
            break
        if len(active_teams) == 1:
            action_order.extend(queues[active_teams[0]])
            break

        team_a_id, team_b_id = active_teams[0], active_teams[1]
        hero_a_id = queues[team_a_id][0]
        hero_b_id = queues[team_b_id][0]
        winner_id, decision = decide_first_hero_id(
            context,
            round_no=round_no,
            slot=slot,
            hero_a_id=hero_a_id,
            hero_b_id=hero_b_id,
        )
        merge_decisions.append(decision)
        winner_team_id = context.heroes[winner_id].team_id
        action_order.append(queues[winner_team_id].pop(0))
        slot += 1

    return RoundActionOrderResult(action_order=action_order, merge_decisions=merge_decisions)


def build_round_action_order(context: BattleContext, round_no: int) -> RoundActionOrderResult:
    """Build a round-local global action order before ROUND_START."""
    team_ids = sorted(context.teams)
    if len(team_ids) != 2:
        raise ValueError("round action order requires exactly two teams")

    team_orders = {
        team_id: sort_team_action_order(team_id, context.teams[team_id], context.heroes)
        for team_id in team_ids
    }
    if sum(len(order) for order in team_orders.values()) <= 1:
        flat_order = [hero_id for team_id in team_ids for hero_id in team_orders[team_id]]
        return RoundActionOrderResult(action_order=flat_order)

    return merge_team_orders_into_global(context, round_no=round_no, team_orders=team_orders)


def format_action_order_table(
    context: BattleContext,
    round_no: int,
    action_order: list[str],
    *,
    merge_decisions: list[SpeedMergeDecision] | None = None,
) -> str:
    lines = [f"Round {round_no} Action Order"]
    if merge_decisions:
        lines.append("MergeDecisions\tHeroA\tHeroB\tSpeedDiff\tFirstProb\tWinner")
        for decision in merge_decisions:
            hero_a = context.heroes[decision.hero_a_id].name
            hero_b = context.heroes[decision.hero_b_id].name
            winner = context.heroes[decision.winner_id].name
            lines.append(
                f"{decision.slot}\t{hero_a}\t{hero_b}\t{decision.speed_diff}\t"
                f"{decision.first_prob_bps / 100:.2f}%\t{winner}"
            )
    lines.append(f"Order\tTeam\tHero\t{ATTR_BATTLE_LOG_HEADER}")
    for index, hero_id in enumerate(action_order, start=1):
        hero = context.heroes[hero_id]
        lines.append(
            f"{index}\t{hero.team_id}\t{hero.name}\t{get_effective_speed(hero)}\t"
            f"{get_effective_attr(hero, 'force')}\t{get_effective_attr(hero, 'intelligence')}\t"
            f"{get_effective_attr(hero, 'command')}"
        )
    return "\n".join(lines)


def format_round_effective_attrs_table(context: BattleContext) -> str:
    """本回合在场武将的有效四维（含 ATTR 状态修正）。"""
    lines = [f"EffectiveAttrs\tTeam\tHero\t{ATTR_BATTLE_LOG_HEADER}"]
    heroes = sorted(
        (hero for hero in context.heroes.values() if not hero.exited),
        key=lambda hero: (hero.team_id, hero.position, hero.instance_id),
    )
    for index, hero in enumerate(heroes, start=1):
        lines.append(
            f"{index}\t{hero.team_id}\t{hero.name}\t{get_effective_speed(hero)}\t"
            f"{get_effective_attr(hero, 'force')}\t{get_effective_attr(hero, 'intelligence')}\t"
            f"{get_effective_attr(hero, 'command')}"
        )
    return "\n".join(lines)


def build_action_order_payload(
    context: BattleContext,
    round_no: int,
    action_order: list[str],
    *,
    merge_decisions: list[SpeedMergeDecision] | None = None,
) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    for index, hero_id in enumerate(action_order, start=1):
        hero = context.heroes[hero_id]
        rows.append(
            {
                "order": index,
                "hero_id": hero_id,
                "team_id": hero.team_id,
                "name": hero.name,
                "speed": get_effective_speed(hero),
            }
        )
    payload: dict[str, Any] = {
        "round_no": round_no,
        "action_order": action_order,
        "action_order_table": rows,
    }
    if merge_decisions:
        payload["merge_decisions"] = [
            {
                "slot": decision.slot,
                "hero_a_id": decision.hero_a_id,
                "hero_b_id": decision.hero_b_id,
                "speed_diff": decision.speed_diff,
                "first_prob_bps": decision.first_prob_bps,
                "winner_id": decision.winner_id,
            }
            for decision in merge_decisions
        ]
    return payload
