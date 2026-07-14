import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import EventType, HeroRole
from _output_helper import format_battle_result, print_and_save_output


def build_gorgon_gaze_input(seed: int = 1) -> BattleInput:
    db = build_demo_config_db()
    skill_ids = ["basic_attack", "gorgon_gaze"]
    return BattleInput(
        battle_id="battle_gorgon_gaze",
        seed=seed,
        max_rounds=3,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 10000, 80, 70, 40, 90, skill_ids),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 3000, 75, 60, 40, 70, skill_ids),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 3000, 70, 60, 40, 50, skill_ids),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 40, 80, skill_ids),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 3000, 75, 60, 40, 60, skill_ids),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 3000, 70, 60, 40, 40, skill_ids),
        ],
    )


def test_gorgon_gaze_effect_records_control_and_duration() -> None:
    result = run_battle(build_gorgon_gaze_input())
    event_types = [event.event_type for event in result.event_stream]
    logs = "\n".join(result.human_logs)
    gorgon_summaries = [
        summary for summary in result.summary.skill_summaries if summary["skill_id"] == "gorgon_gaze"
    ]
    gorgon_records = [
        record for summary in gorgon_summaries for record in summary["effect_execution_records"]
    ]
    executed_records = [record for record in gorgon_records if record["status"] == "EXECUTED"]

    print_and_save_output(
        "test_gorgon_gaze_effect_records_control_and_duration",
        format_battle_result("Gorgon Gaze Skill", result),
    )

    assert EventType.CONTROL_STATE_APPLIED in event_types
    assert EventType.STATE_ADDED not in event_types or all(
        event.payload.get("state_type") != "CONTROL"
        for event in result.event_stream
        if event.event_type == EventType.STATE_ADDED
    )
    assert EventType.STATE_DURATION_TICKED in event_types
    assert EventType.STATE_REMOVED in event_types
    assert EventType.BEFORE_ACTIVE_SIGNAL in event_types
    assert EventType.ACTIVE_SIGNAL in event_types
    assert EventType.AFTER_ACTIVE_SIGNAL in event_types
    assert EventType.BEFORE_BASIC_SIGNAL in event_types
    assert EventType.BASIC_SIGNAL in event_types
    assert EventType.AFTER_BASIC_SIGNAL in event_types
    assert "CONTROL_FORBID_BASIC" in logs
    assert "CONTROL_FORBID_ACTIVE" in logs
    assert any("reason=ALWAYS_TRIGGER" in line and "roll=" not in line for line in result.human_logs)
    assert all("roll=" not in line for line in result.human_logs if "CONTROL_FORBID_" in line)
    assert "roll=" in logs and "current=" in logs

    successful_gorgon_count = sum(
        1
        for event in result.event_stream
        if event.event_type == EventType.TRIGGER_SUCCESS and event.skill_id == "gorgon_gaze"
    )
    before_gorgon_count = sum(
        1
        for event in result.event_stream
        if event.event_type == EventType.BEFORE_ACTIVE_SIGNAL and event.skill_id == "gorgon_gaze"
    )
    failed_gorgon_count = sum(
        1
        for event in result.event_stream
        if event.event_type == EventType.TRIGGER_FAIL and event.skill_id == "gorgon_gaze"
    )
    assert before_gorgon_count == successful_gorgon_count
    assert failed_gorgon_count > 0
    assert any(
        event.payload.get("failure_kind") == "CONTROL"
        for event in result.event_stream
        if event.event_type == EventType.TRIGGER_FAIL
    )
    assert any(
        event.payload.get("failure_kind") == "PROBABILITY"
        for event in result.event_stream
        if event.event_type == EventType.TRIGGER_FAIL
    )

    assert len(gorgon_records) >= 4
    assert any(record["effect_id"] == "gorgon_gaze_damage_1" for record in executed_records)
    assert any(record["effect_id"] == "gorgon_gaze_ming_lock_1" for record in executed_records)

    for summary in gorgon_summaries:
        records = summary["effect_execution_records"]
        for index, record in enumerate(records):
            if record["effect_id"] != "gorgon_gaze_ming_lock_1" or not record["selected_target_ids"]:
                continue
            previous = records[index - 1]
            assert previous["effect_id"] == "gorgon_gaze_damage_1"
            assert record["selected_target_ids"] == previous["selected_target_ids"]
            break
        else:
            continue
        break
    else:
        raise AssertionError("no Gorgon lock effect reused the damage target")


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
