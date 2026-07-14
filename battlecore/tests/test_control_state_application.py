import _path_bootstrap  # noqa: F401

from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import EventType, HeroRole, Timing
from battlecore.engine.battle_context import BattleContext


def build_control_state_context() -> BattleContext:
    db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="control_state_test",
        seed=1,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 10000, 80, 70, 40, 90, ["gorgon_gaze"]),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 10000, 75, 60, 40, 70, ["gorgon_gaze"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 40, 80, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    context.rebuild_indexes()
    context.current_timing = Timing.ACTIVE
    return context


def test_control_state_applied_signal_on_first_apply() -> None:
    context = build_control_state_context()
    actor = context.heroes["a_main"]
    target = context.heroes["b_main"]

    state = context.add_state(actor, target, "ming_lock_state", duration_override=1)
    control_events = [event for event in context.event_stream if event.event_type == EventType.CONTROL_STATE_APPLIED]
    state_added_events = [event for event in context.event_stream if event.event_type == EventType.STATE_ADDED]

    assert state is not None
    assert len(control_events) == 1
    assert not state_added_events
    assert control_events[0].payload["refreshed"] is False
    assert control_events[0].payload["action_tick_count"] == 0
    assert len(target.states) == 1


def test_control_state_refresh_reuses_single_instance_and_resets_ticks() -> None:
    context = build_control_state_context()
    first_actor = context.heroes["a_main"]
    second_actor = context.heroes["a_d1"]
    target = context.heroes["b_main"]

    first_state = context.add_state(first_actor, target, "ming_lock_state", duration_override=1)
    target.states[0].action_tick_count = 1
    target.states[0].remaining_rounds = 0

    refreshed_state = context.add_state(second_actor, target, "ming_lock_state", duration_override=1)
    control_events = [event for event in context.event_stream if event.event_type == EventType.CONTROL_STATE_APPLIED]

    assert refreshed_state is first_state
    assert len(target.states) == 1
    assert refreshed_state.action_tick_count == 0
    assert refreshed_state.remaining_rounds == 1
    assert refreshed_state.stack == 1
    assert refreshed_state.source_actor_id == second_actor.instance_id
    assert len(control_events) == 2
    assert control_events[0].payload["refreshed"] is False
    assert control_events[1].payload["refreshed"] is True
    assert control_events[1].event_type == control_events[0].event_type


def test_control_state_refresh_does_not_interrupt_preparation() -> None:
    context = build_control_state_context()
    first_actor = context.heroes["a_main"]
    second_actor = context.heroes["a_d1"]
    target = context.heroes["b_main"]

    context.add_state(first_actor, target, "ming_lock_state", duration_override=1)
    target.states[0].action_tick_count = 1

    context.add_state(target, target, "delphi_charged_preparing_state", duration_override=999)
    preparing = next(state for state in target.states if state.state_config_id == "delphi_charged_preparing_state")
    assert "active_preparing" in preparing.tags

    context.add_state(second_actor, target, "ming_lock_state", duration_override=1)

    removed_reasons = [
        event.payload.get("reason")
        for event in context.event_stream
        if event.event_type == EventType.STATE_REMOVED and event.state_instance_id == preparing.instance_id
    ]
    assert removed_reasons == []
    assert any(state.instance_id == preparing.instance_id for state in target.states)
    assert len([state for state in target.states if state.state_config_id == "ming_lock_state"]) == 1


def test_control_state_first_apply_still_interrupts_preparation() -> None:
    context = build_control_state_context()
    actor = context.heroes["a_main"]
    target = context.heroes["b_main"]

    context.add_state(target, target, "delphi_charged_preparing_state", duration_override=999)
    preparing_id = target.states[0].instance_id

    context.add_state(actor, target, "ming_lock_state", duration_override=1)

    removed_reasons = [
        event.payload.get("reason")
        for event in context.event_stream
        if event.event_type == EventType.STATE_REMOVED and event.state_instance_id == preparing_id
    ]
    assert removed_reasons == ["CONTROL_INTERRUPT"]


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
