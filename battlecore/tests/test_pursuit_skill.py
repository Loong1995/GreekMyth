import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig, StateConfig
from battlecore.domain.enums import EventType, HeroRole, StateType, Timing, TriggerMode
from battlecore.engine.battle_context import BattleContext
from _output_helper import format_battle_result, print_and_save_output


def build_pursuit_input(seed: int = 1) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_pursuit_strike",
        seed=seed,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig(
                "a_main",
                "A-Main",
                "team_a",
                HeroRole.MAIN,
                1,
                10000,
                120,
                80,
                80,
                90,
                ["basic_attack", "pursuit_strike"],
            ),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 10000, 80, 70, 70, 70, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 10000, 80, 70, 70, 50, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 70, 80, ["basic_attack"]),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 10000, 80, 70, 70, 60, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 10000, 80, 70, 70, 40, ["basic_attack"]),
        ],
    )


def _event_index(events: list, event) -> int:
    return next(i for i, item in enumerate(events) if item.event_id == event.event_id)


def test_pursuit_triggers_after_basic_damage_settled_without_pursuit_timing() -> None:
    result = run_battle(build_pursuit_input())

    print_and_save_output(
        "test_pursuit_triggers_after_basic_damage_settled_without_pursuit_timing",
        format_battle_result("Pursuit Strike", result),
    )

    timing_started = [
        event
        for event in result.event_stream
        if event.event_type == EventType.TIMING_STARTED and event.timing == Timing.PURSUIT
    ]
    assert not timing_started

    basic_damage_settled = [
        event
        for event in result.event_stream
        if event.event_type == EventType.DAMAGE_SETTLED and event.skill_id == "basic_attack"
    ]
    pursuit_triggers = [
        event
        for event in result.event_stream
        if event.skill_id == "pursuit_strike" and event.event_type == EventType.TRIGGER_SUCCESS
    ]
    pursuit_signals = [
        event
        for event in result.event_stream
        if event.skill_id == "pursuit_strike" and event.event_type == EventType.PURSUIT_SIGNAL
    ]
    basic_post_effects = [
        event
        for event in result.event_stream
        if event.event_type == EventType.POST_EFFECT_EXECUTE
        and event.skill_id == "basic_attack"
        and event.effect_id == "basic_attack_damage"
    ]

    assert basic_damage_settled
    assert all(event.payload.get("damage", 0) > 0 for event in basic_damage_settled)
    assert pursuit_triggers
    assert pursuit_signals
    assert basic_post_effects
    assert all(event.timing == Timing.BASIC for event in pursuit_signals)

    first_basic_settled = basic_damage_settled[0]
    first_pursuit_damage = next(
        event
        for event in result.event_stream
        if event.skill_id == "pursuit_strike" and event.event_type == EventType.DAMAGE_APPLIED
    )
    assert first_pursuit_damage.target_ids == first_basic_settled.target_ids

    first_basic_post = basic_post_effects[0]
    first_pursuit_trigger = pursuit_triggers[0]
    assert _event_index(result.event_stream, first_basic_settled) < _event_index(
        result.event_stream, first_pursuit_trigger
    )
    assert _event_index(result.event_stream, first_pursuit_trigger) < _event_index(
        result.event_stream, first_basic_post
    )


def test_pursuit_not_triggered_when_basic_blocked_by_ming_lock() -> None:
    db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="battle_pursuit_ming_lock",
        seed=1,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig(
                "a_main",
                "A-Main",
                "team_a",
                HeroRole.MAIN,
                1,
                10000,
                120,
                80,
                80,
                90,
                ["basic_attack", "pursuit_strike"],
            ),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 70, 80, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    context.rebuild_indexes()
    actor = context.heroes["a_main"]
    context.add_state(actor, actor, "ming_lock_state", duration_override=1)
    context.current_timing = Timing.BASIC
    context.run_battle()

    basic_fails = [
        event
        for event in context.event_stream
        if event.actor_id == "a_main"
        and event.skill_id == "basic_attack"
        and event.event_type == EventType.TRIGGER_FAIL
        and event.payload.get("reason") == "CONTROL_FORBID_BASIC"
    ]
    basic_damage_settled = [
        event
        for event in context.event_stream
        if event.actor_id == "a_main"
        and event.event_type == EventType.DAMAGE_SETTLED
        and event.skill_id == "basic_attack"
    ]
    pursuit_events = [
        event
        for event in context.event_stream
        if event.actor_id == "a_main"
        and event.skill_id == "pursuit_strike"
        and event.event_type in (EventType.TRIGGER_SUCCESS, EventType.PURSUIT_SIGNAL)
    ]

    assert basic_fails
    assert not basic_damage_settled
    assert not pursuit_events


def test_pursuit_blocked_by_forbid_pursuit_control() -> None:
    db = build_demo_config_db()
    db.state_configs["forbid_pursuit_state"] = StateConfig(
        state_config_id="forbid_pursuit_state",
        name="禁追",
        state_type=StateType.CONTROL,
        trigger_mode=TriggerMode.NONE,
        duration_rounds=1,
        payload={"forbid_pursuit": True},
    )
    battle_input = BattleInput(
        battle_id="battle_pursuit_forbid",
        seed=1,
        max_rounds=1,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig(
                "a_main",
                "A-Main",
                "team_a",
                HeroRole.MAIN,
                1,
                10000,
                120,
                80,
                80,
                90,
                ["basic_attack", "pursuit_strike"],
            ),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 70, 80, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    context.rebuild_indexes()
    actor = context.heroes["a_main"]
    context.add_state(actor, actor, "forbid_pursuit_state", duration_override=1)

    context.run_battle()

    pursuit_fails = [
        event
        for event in context.event_stream
        if event.skill_id == "pursuit_strike"
        and event.event_type == EventType.TRIGGER_FAIL
        and event.payload.get("reason") == "CONTROL_FORBID_PURSUIT"
    ]
    assert pursuit_fails


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
