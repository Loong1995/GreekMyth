import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.domain.enums import EventType
from battlecore.sample.sample_battle import build_step1_basic_attack_input
from _output_helper import format_battle_result, print_and_save_output


def test_basic_attack_battle_is_deterministic() -> None:
    input_data = build_step1_basic_attack_input()
    first = run_battle(input_data)
    second = run_battle(input_data)
    print_and_save_output(
        "test_basic_attack_battle_is_deterministic",
        format_battle_result("Basic Attack Deterministic Run", first),
    )

    assert [event.to_dict() for event in first.event_stream] == [
        event.to_dict() for event in second.event_stream
    ]
    assert first.summary.to_dict() == second.summary.to_dict()


def test_basic_attack_generates_damage_and_exit_events() -> None:
    result = run_battle(build_step1_basic_attack_input())
    event_types = [event.event_type for event in result.event_stream]
    print_and_save_output(
        "test_basic_attack_generates_damage_and_exit_events",
        format_battle_result("Basic Attack Damage And Exit Events", result),
    )

    assert EventType.TRIGGER_SUCCESS in event_types
    assert EventType.TARGET_SELECTED in event_types
    assert EventType.DAMAGE_APPLIED in event_types
    assert EventType.HERO_EXITED in event_types


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
