from __future__ import annotations

from typing import TYPE_CHECKING

from battlecore.engine.action_order import build_round_action_order, sort_team_action_order
from battlecore.engine.damage_calculator import get_effective_attr

if TYPE_CHECKING:
    from battlecore.domain.hero import Hero
    from battlecore.engine.battle_context import BattleContext


def build_speed_order(heroes: dict[str, Hero]) -> list[str]:
    """Legacy helper: deterministic global order by effective speed only."""
    return sorted(
        heroes,
        key=lambda hero_id: (
            -get_effective_attr(heroes[hero_id], "speed"),
            heroes[hero_id].team_id,
            heroes[hero_id].position,
            heroes[hero_id].instance_id,
        ),
    )


def build_global_speed_order(context: BattleContext, round_no: int) -> list[str]:
    return build_round_action_order(context, round_no).action_order


def build_team_internal_orders(heroes: dict[str, Hero], team_ids: list[str]) -> dict[str, list[str]]:
    heroes_by_team: dict[str, list[str]] = {}
    for hero_id, hero in heroes.items():
        heroes_by_team.setdefault(hero.team_id, []).append(hero_id)
    return {
        team_id: sort_team_action_order(team_id, heroes_by_team.get(team_id, []), heroes)
        for team_id in sorted(team_ids)
    }
