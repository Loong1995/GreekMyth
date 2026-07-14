import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.hero_files import Zeus
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import EventType, HeroRole
from _output_helper import format_battle_result, print_and_save_output


def build_thunder_oracle_input(seed: int = 1) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_thunder_oracle",
        seed=seed,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            Zeus(
                ["gorgon_gaze", "delphi_revelation", "asclepius_oracle"],
                hero_id="a_main",
                team_id="team_a",
            ),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 10000, 80, 70, 80, 70, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 10000, 80, 70, 80, 50, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 50, 80, ["basic_attack"]),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 10000, 80, 70, 50, 60, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 10000, 80, 70, 50, 40, ["basic_attack"]),
        ],
    )


def test_thunder_oracle_triggers_lightning_without_recursion() -> None:
    result = run_battle(build_thunder_oracle_input())
    logs = "\n".join(result.human_logs)

    print_and_save_output(
        "test_thunder_oracle_triggers_lightning_without_recursion",
        format_battle_result("Thunder Oracle", result),
    )

    thunder_states = [
        state
        for state in result.summary.state_summaries
        if state["config_id"] == "thunder_state" and str(state["owner"]).startswith("a_")
    ]
    lightning_damage_events = [
        event
        for event in result.event_stream
        if event.event_type == EventType.DAMAGE_SETTLED and event.source_id == "thunder_state"
    ]
    thunder_trigger_events = [
        event
        for event in result.event_stream
        if event.source_type == "STATE" and event.source_id == "thunder_state"
    ]

    assert len(thunder_states) == 3
    assert thunder_trigger_events
    assert lightning_damage_events
    assert "雷霆神谕" in logs
    assert "雷霆" in logs
    assert "落雷" in logs
    assert "base=7000" in logs
    assert "触发者=" in logs
    assert {event.actor_id for event in lightning_damage_events} <= {"a_main", "a_d1", "a_d2"}
    deputy_thunder_keys = [
        line
        for line in logs.splitlines()
        if "A-D1 触发 雷霆" in line and "key=" in line
    ]
    assert deputy_thunder_keys
    assert all("|a_d1|thunder_oracle|" in line for line in deputy_thunder_keys)
    assert all("|a_main|thunder_oracle|" not in line for line in deputy_thunder_keys)


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
