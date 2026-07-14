from __future__ import annotations

"""State 响应排序：REGULAR / SPY 分组内按 chain_reaction_config 的 steps 稳定排序。

详见 STATE_RESPONSE_REFERENCE.md。
"""

from battlecore.config.chain_reaction_config import (
    RegularGroupConfig,
    SpyGroupConfig,
    StateSortKey,
    TriggerStepConfig,
    UnconfiguredStateSortConfig,
)
from battlecore.domain.enums import EventType, SkillCategory, Timing
from battlecore.domain.skill import Skill, State


def find_spy_group(
    groups: tuple[SpyGroupConfig, ...], event_type: EventType
) -> SpyGroupConfig | None:
    for group in groups:
        if event_type in group.listen_event_types:
            return group
    return None


def find_regular_group(
    groups: tuple[RegularGroupConfig, ...], timing: Timing
) -> RegularGroupConfig | None:
    for group in groups:
        if group.timing == timing:
            return group
    return None


def state_step_index_in_steps(state: State, steps: tuple[TriggerStepConfig, ...]) -> int | None:
    state_tags = set(state.tags)
    for index, step in enumerate(steps):
        if step.kind != "STATE":
            continue
        if any(tag in state_tags for tag in step.state_tags):
            return index
    return None


def _spy_state_step_indices(state: State, spy_groups: tuple[SpyGroupConfig, ...]) -> list[tuple[int, int]]:
    ranks: list[tuple[int, int]] = []
    for group_index, group in enumerate(spy_groups):
        step_index = state_step_index_in_steps(state, group.steps)
        if step_index is not None:
            ranks.append((group_index, step_index))
    return ranks


def _regular_state_step_indices(
    state: State, regular_groups: tuple[RegularGroupConfig, ...]
) -> list[tuple[int, int]]:
    ranks: list[tuple[int, int]] = []
    for group_index, group in enumerate(regular_groups):
        if group.timing not in state.trigger_timings:
            continue
        step_index = state_step_index_in_steps(state, group.steps)
        if step_index is not None:
            ranks.append((group_index, step_index))
    return ranks


def _state_sort_key(state: State, keys: tuple[StateSortKey, ...]) -> tuple:
    parts: list[int | str] = []
    for key in keys:
        if key == "owner_position":
            parts.append(state.owner.position)
        elif key == "owner_instance_id":
            parts.append(state.owner.instance_id)
        elif key == "state_instance_id":
            parts.append(state.instance_id)
    return tuple(parts)


def sort_states_by_unconfigured_rule(
    states: list[State], sort_config: UnconfiguredStateSortConfig
) -> list[State]:
    keyed = [(_state_sort_key(state, sort_config.keys), state) for state in states]
    keyed.sort(key=lambda item: item[0])
    return [state for _, state in keyed]


def _configured_rank_for_spy(
    state: State,
    *,
    primary_group: SpyGroupConfig | None,
    spy_groups: tuple[SpyGroupConfig, ...],
) -> tuple[int, tuple] | None:
    if primary_group is not None:
        step_index = state_step_index_in_steps(state, primary_group.steps)
        if step_index is not None:
            return (0, (step_index,))
        return None
    indices = _spy_state_step_indices(state, spy_groups)
    if indices:
        return (0, min(indices))
    return None


def _configured_rank_for_regular(
    state: State,
    *,
    primary_group: RegularGroupConfig | None,
    regular_groups: tuple[RegularGroupConfig, ...],
) -> tuple[int, tuple] | None:
    if primary_group is not None:
        step_index = state_step_index_in_steps(state, primary_group.steps)
        if step_index is not None:
            return (0, (step_index,))
        return None
    indices = _regular_state_step_indices(state, regular_groups)
    if indices:
        return (0, min(indices))
    return None


def _sort_states_by_configured_ranks(
    states: list[State],
    *,
    rank_for_state,
    unconfigured_sort: UnconfiguredStateSortConfig,
) -> list[State]:
    configured: list[tuple[tuple[int, tuple], tuple, State]] = []
    unconfigured: list[State] = []
    for state in states:
        rank = rank_for_state(state)
        if rank is None:
            unconfigured.append(state)
        else:
            tie_break = _state_sort_key(state, unconfigured_sort.keys)
            configured.append((rank, tie_break, state))
    configured.sort(key=lambda item: (item[0], item[1]))
    ordered = [state for _, _, state in configured]
    ordered.extend(sort_states_by_unconfigured_rule(unconfigured, unconfigured_sort))
    return ordered


def sort_spy_states_for_dispatch(
    states: list[State],
    *,
    primary_group: SpyGroupConfig | None,
    spy_groups: tuple[SpyGroupConfig, ...],
    unconfigured_sort: UnconfiguredStateSortConfig,
) -> list[State]:
    return _sort_states_by_configured_ranks(
        states,
        rank_for_state=lambda state: _configured_rank_for_spy(
            state,
            primary_group=primary_group,
            spy_groups=spy_groups,
        ),
        unconfigured_sort=unconfigured_sort,
    )


def sort_regular_states_for_dispatch(
    states: list[State],
    *,
    primary_group: RegularGroupConfig | None,
    regular_groups: tuple[RegularGroupConfig, ...],
    unconfigured_sort: UnconfiguredStateSortConfig,
) -> list[State]:
    return _sort_states_by_configured_ranks(
        states,
        rank_for_state=lambda state: _configured_rank_for_regular(
            state,
            primary_group=primary_group,
            regular_groups=regular_groups,
        ),
        unconfigured_sort=unconfigured_sort,
    )


def skill_steps_for_spy_group(group: SpyGroupConfig) -> list[TriggerStepConfig]:
    return [step for step in group.steps if step.kind == "SKILL"]


def skills_for_chain_step(actor_skills: list[Skill], step: TriggerStepConfig) -> list[Skill]:
    if step.kind != "SKILL" or step.skill_category is None:
        return []
    return [skill for skill in actor_skills if skill.category == step.skill_category]


# 兼容旧名
find_chain_group = find_spy_group
find_active_group = find_regular_group
chain_step_index_for_state = lambda state, group: state_step_index_in_steps(state, group.steps)
sort_spy_states_by_unconfigured_rule = sort_states_by_unconfigured_rule
sort_spy_states_for_chain = sort_spy_states_for_dispatch
sort_active_states_for_dispatch = sort_regular_states_for_dispatch
skill_steps_for_chain = skill_steps_for_spy_group
