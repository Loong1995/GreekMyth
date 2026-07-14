import _path_bootstrap  # noqa: F401

from battlecore.config.chain_reaction_config import (
    BEFORE_ACTION_REGULAR,
    DEFAULT_REGULAR_GROUPS,
    DEFAULT_SPY_GROUPS,
    DEFAULT_UNCONFIGURED_STATE_SORT,
    DAMAGE_SETTLED_SPY,
)
from battlecore.config.schema import StateConfig
from battlecore.domain.enums import EventType, HeroRole, StateType, Timing, TriggerMode
from battlecore.domain.hero import Hero
from battlecore.domain.skill import State
from battlecore.engine.chain_reaction import (
    sort_regular_states_for_dispatch,
    sort_spy_states_for_dispatch,
)


def make_hero(hero_id: str, *, position: int) -> Hero:
    return Hero(
        instance_id=hero_id,
        config_id=hero_id,
        name=hero_id,
        team_id="team_a",
        role=HeroRole.MAIN,
        position=position,
        max_troops=10000,
        troops=10000,
        force=80,
        intelligence=70,
        command=70,
        speed=70,
    )


def _make_state(
    instance_id: str,
    tag: str,
    position: int,
    *,
    owner_id: str | None = None,
    trigger_mode: TriggerMode = TriggerMode.SPY,
    trigger_timings: list[Timing] | None = None,
) -> State:
    owner = make_hero(owner_id or f"owner_{instance_id}", position=position)
    cfg = StateConfig(
        state_config_id=f"{tag}_state",
        name=tag,
        state_type=StateType.SPECIAL,
        trigger_mode=trigger_mode,
        listen_event_types=[EventType.DAMAGE_SETTLED],
        trigger_timings=list(trigger_timings or []),
        tags=[tag],
    )
    return State.from_config(instance_id, cfg, owner)


def test_sort_spy_states_follows_damage_settled_spy_order() -> None:
    styx = _make_state("s_styx", "styx_blood_oath", 1)
    snake = _make_state("s_snake", "snake_staff_protection", 2)
    thunder = _make_state("s_thunder", "thunder_oracle", 3)
    shuffled = [thunder, snake, styx]
    ordered = sort_spy_states_for_dispatch(
        shuffled,
        primary_group=DAMAGE_SETTLED_SPY,
        spy_groups=DEFAULT_SPY_GROUPS,
        unconfigured_sort=DEFAULT_UNCONFIGURED_STATE_SORT,
    )
    assert [s.instance_id for s in ordered] == ["s_styx", "s_snake", "s_thunder"]


def test_unconfigured_spy_runs_after_configured_steps() -> None:
    styx = _make_state("s_styx", "styx_blood_oath", 1)
    other = _make_state("s_other", "unknown_tag", 2)
    ordered = sort_spy_states_for_dispatch(
        [other, styx],
        primary_group=DAMAGE_SETTLED_SPY,
        spy_groups=DEFAULT_SPY_GROUPS,
        unconfigured_sort=DEFAULT_UNCONFIGURED_STATE_SORT,
    )
    assert [s.instance_id for s in ordered] == ["s_styx", "s_other"]


def test_dispatch_without_spy_group_uses_owner_position_then_ids() -> None:
    low_pos = _make_state("s_low", "unknown_tag", 1, owner_id="hero_b")
    high_pos = _make_state("s_high", "unknown_tag", 3, owner_id="hero_a")
    ordered = sort_spy_states_for_dispatch(
        [high_pos, low_pos],
        primary_group=None,
        spy_groups=DEFAULT_SPY_GROUPS,
        unconfigured_sort=DEFAULT_UNCONFIGURED_STATE_SORT,
    )
    assert [s.instance_id for s in ordered] == ["s_low", "s_high"]


def test_sort_regular_states_follows_before_action_order() -> None:
    shadow = _make_state(
        "s_shadow",
        "shadow_veil",
        1,
        trigger_mode=TriggerMode.REGULAR,
        trigger_timings=[Timing.BEFORE_ACTION],
    )
    hades = _make_state(
        "s_hades",
        "hades_command_drain",
        1,
        trigger_mode=TriggerMode.REGULAR,
        trigger_timings=[Timing.BEFORE_ACTION],
    )
    ordered = sort_regular_states_for_dispatch(
        [hades, shadow],
        primary_group=BEFORE_ACTION_REGULAR,
        regular_groups=DEFAULT_REGULAR_GROUPS,
        unconfigured_sort=DEFAULT_UNCONFIGURED_STATE_SORT,
    )
    assert [s.instance_id for s in ordered] == ["s_shadow", "s_hades"]


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
