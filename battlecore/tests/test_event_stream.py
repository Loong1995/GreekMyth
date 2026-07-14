import json

import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.domain.enums import EventType
from battlecore.event.event_codec import dumps_event_stream
from battlecore.sample.sample_battle import build_step1_basic_attack_input
from _output_helper import format_battle_result, print_and_save_output


def test_event_stream_is_json_serializable_and_ordered() -> None:
    result = run_battle(build_step1_basic_attack_input())
    event_ids = [event.event_id for event in result.event_stream]

    assert event_ids == list(range(1, len(event_ids) + 1))
    assert result.event_stream[0].event_type == EventType.BATTLE_STARTED
    assert result.event_stream[-1].event_type == EventType.BATTLE_FINISHED

    encoded = dumps_event_stream(result.event_stream)
    decoded = json.loads(encoded)
    print_and_save_output(
        "test_event_stream_is_json_serializable_and_ordered",
        format_battle_result("Event Stream Serialization", result),
    )

    assert decoded[-1]["event_type"] == "BATTLE_FINISHED"
    assert "payload" in decoded[-1]


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
