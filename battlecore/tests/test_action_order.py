import _path_bootstrap  # noqa: F401

import pytest

from battlecore.engine.action_order import (
    RoundActionOrderResult,
    build_round_action_order,
    calc_speed_first_probability_bps,
    get_effective_speed,
    merge_team_orders_into_global,
    sort_team_action_order,
)


@pytest.mark.parametrize(
    ("speed_diff", "expected_bps"),
    [
        (0, 5000),
        (1, 5500),
        (5, 7000),
        (10, 8000),
        (20, 10000),
        (25, 10000),
        (-1, 4500),
        (-5, 3000),
        (-10, 2000),
        (-20, 0),
    ],
)
def test_speed_first_probability_curve(speed_diff: int, expected_bps: int) -> None:
    assert calc_speed_first_probability_bps(speed_diff) == expected_bps


def test_sort_team_action_order_uses_speed_within_team() -> None:
    from battlecore.domain.enums import HeroRole
    from battlecore.domain.hero import Hero

    heroes = {
        "fast": Hero("fast", "fast", "Fast", "team_a", HeroRole.MAIN, 1, 1000, 1000, 100, 100, 100, 90),
        "slow": Hero("slow", "slow", "Slow", "team_a", HeroRole.DEPUTY, 2, 1000, 1000, 100, 100, 100, 50),
    }
    order = sort_team_action_order("team_a", ["slow", "fast"], heroes)
    assert order == ["fast", "slow"]


def _assert_team_internal_order(action_order: list[str], hero_ids: list[str]) -> None:
    indices = [action_order.index(hero_id) for hero_id in hero_ids]
    assert indices == sorted(indices)


def test_build_round_action_order_interleaves_teams_by_speed_contest() -> None:
    from battlecore.config.config_db import build_demo_config_db
    from battlecore.config.schema import BattleInput, HeroConfig
    from battlecore.domain.enums import HeroRole
    from battlecore.engine.battle_context import BattleContext

    db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="action_order_test",
        seed=1,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 1000, 100, 100, 100, 90, ["basic_attack"]),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 1000, 100, 100, 100, 70, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 1000, 100, 100, 100, 50, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 1000, 100, 100, 100, 80, ["basic_attack"]),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 1000, 100, 100, 100, 60, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 1000, 100, 100, 100, 40, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    context.rebuild_indexes()

    result = build_round_action_order(context, 1)
    assert isinstance(result, RoundActionOrderResult)
    assert result.action_order == ["a_main", "b_main", "a_d1", "a_d2", "b_d1", "b_d2"]
    _assert_team_internal_order(result.action_order, ["a_main", "a_d1", "a_d2"])
    _assert_team_internal_order(result.action_order, ["b_main", "b_d1", "b_d2"])
    assert result.action_order != ["a_main", "a_d1", "a_d2", "b_main", "b_d1", "b_d2"]
    assert len(result.merge_decisions) == 4
    assert result.merge_decisions[0].winner_id == "a_main"
    assert result.merge_decisions[0].speed_diff == 10
    assert result.merge_decisions[3].speed_diff == -10
    assert result.merge_decisions[3].winner_id == "a_d2"


def test_merge_uses_pseudo_random_when_speed_is_equal() -> None:
    from battlecore.config.config_db import build_demo_config_db
    from battlecore.config.schema import BattleInput, HeroConfig
    from battlecore.domain.enums import HeroRole
    from battlecore.engine.battle_context import BattleContext

    db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="action_order_equal_speed",
        seed=1,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 1000, 100, 100, 100, 80, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 1000, 100, 100, 100, 80, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    result = merge_team_orders_into_global(
        context,
        round_no=1,
        team_orders={
            "team_a": ["a_main"],
            "team_b": ["b_main"],
        },
    )
    assert result.merge_decisions[0].first_prob_bps == 5000
    assert result.action_order[0] in {"a_main", "b_main"}


def test_prepare_round_action_order_logs_table() -> None:
    from battlecore.config.config_db import build_demo_config_db
    from battlecore.config.schema import BattleInput, HeroConfig
    from battlecore.domain.enums import HeroRole
    from battlecore.engine.battle_context import BattleContext

    db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="action_order_log_test",
        seed=7,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 1000, 100, 100, 100, 100, ["basic_attack"]),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 1000, 100, 100, 100, 90, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 1000, 100, 100, 100, 80, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 1000, 100, 100, 100, 95, ["basic_attack"]),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 1000, 100, 100, 100, 85, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 1000, 100, 100, 100, 75, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    context.prepare_round_action_order(1)

    assert context.round_action_orders[1]
    assert any("Round 1 Action Order" in log for log in context.human_logs)
    assert any("MergeDecisions" in log for log in context.human_logs)
    assert get_effective_speed(context.heroes["a_main"]) == 100
