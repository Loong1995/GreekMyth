import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import BattleResultType, EventType, HeroRole
from _output_helper import format_battle_result, print_and_save_output


def test_main_hero_exit_finishes_battle_immediately() -> None:
    db = build_demo_config_db()
    input_data = BattleInput(
        battle_id="battle_victory_main_exit",
        seed=1,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 1000, 1000, 50, 1, 100, ["basic_attack"]),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 1000, 1000, 50, 1, 90, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 1000, 1000, 50, 1, 80, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 1, 1, 1, 1, 1, ["basic_attack"]),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 1, 1, 1, 1, 1, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 1, 1, 1, 1, 1, ["basic_attack"]),
        ],
    )

    result = run_battle(input_data, db)
    event_types = [event.event_type for event in result.event_stream]
    print_and_save_output(
        "test_main_hero_exit_finishes_battle_immediately",
        format_battle_result("Main Hero Exit Victory", result),
    )

    assert EventType.MAIN_HERO_EXITED in event_types
    assert result.summary.result == BattleResultType.TEAM_A_WIN.value
    assert result.summary.winner_team_id == "team_a"
    assert result.summary.finish_reason == "MAIN_HERO_EXITED"


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
