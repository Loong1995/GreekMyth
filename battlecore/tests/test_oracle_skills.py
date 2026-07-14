import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.hero_files import Apollo, Zeus
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import EventType, HeroRole
from _output_helper import format_battle_result, print_and_save_output


def build_oracle_input(seed: int = 1) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_oracle_skills",
        seed=seed,
        max_rounds=8,
        config_version=db.version,
        team_a_heroes=[
            Apollo(
                ["gorgon_gaze", "delphi_charged_oracle", "pythia_woven_scheme"],
                hero_id="a_main",
                team_id="team_a",
            ),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 10000, 100, 90, 50, 70, ["basic_attack", "gorgon_gaze"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 10000, 100, 90, 50, 50, ["basic_attack"]),
        ],
        team_b_heroes=[
            Zeus(
                ["delphi_charged_oracle", "gorgon_gaze", "pythia_woven_scheme"],
                hero_id="b_main",
                team_id="team_b",
            ),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 10000, 100, 90, 50, 70, ["basic_attack", "gorgon_gaze"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 10000, 100, 90, 50, 40, ["basic_attack"]),
        ],
    )


def test_oracle_prepare_const_and_damage_settled_heal() -> None:
    result = run_battle(build_oracle_input())
    event_types = [event.event_type for event in result.event_stream]
    logs = "\n".join(result.human_logs)
    a_heroes = [hero for hero in result.summary.hero_summaries if hero["team_id"] == "team_a"]

    print_and_save_output(
        "test_oracle_prepare_const_and_damage_settled_heal",
        format_battle_result("Oracle Skills", result),
    )

    assert EventType.DAMAGE_SETTLED in event_types
    if EventType.HEAL_SETTLED in event_types:
        assert EventType.HEAL_APPLIED in event_types
    assert "德尔斐启示" in logs
    assert "阿斯克勒庇俄斯圣谕" in logs
    assert "神示" in logs
    assert "蛇杖庇护" in logs
    assert "failCount=" in logs
    assert "successStreak=" in logs

    for hero in a_heroes:
        assert any("state:" in state_id for state_id in hero["remaining_states"])

    divine_states = [
        state
        for state in result.summary.state_summaries
        if state["config_id"] == "divine_revelation_state" and str(state["owner"]).startswith("a_")
    ]
    snake_states = [
        state
        for state in result.summary.state_summaries
        if state["config_id"] == "snake_staff_protection_state" and str(state["owner"]).startswith("a_")
    ]
    assert len(divine_states) == 3
    assert len(snake_states) == 3
    assert sum(state["success_count"] + state["fail_count"] for state in snake_states) >= 2
    assert any(state["success_count"] > 0 for state in snake_states)

    deputy_snake_keys = [
        line
        for line in logs.splitlines()
        if "A-D1 触发 蛇杖庇护" in line and "key=" in line
    ]
    assert deputy_snake_keys
    assert all("|a_d1|asclepius_oracle|" in line for line in deputy_snake_keys)
    assert all("|a_main|asclepius_oracle|" not in line for line in deputy_snake_keys)


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
