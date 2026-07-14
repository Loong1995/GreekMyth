from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any

from battlecore.domain.enums import (
    DamageType,
    EffectType,
    EventType,
    HeroRole,
    SkillCategory,
    StateType,
    TargetPolicy,
    Timing,
    TriggerMode,
)


def to_jsonable(value: Any) -> Any:
    if hasattr(value, "value"):
        return value.value
    if isinstance(value, list):
        return [to_jsonable(v) for v in value]
    if isinstance(value, dict):
        return {k: to_jsonable(v) for k, v in value.items()}
    return value


@dataclass(slots=True)
class HeroConfig:
    hero_id: str
    name: str
    team_id: str
    role: HeroRole
    position: int
    max_troops: int
    force: int
    intelligence: int
    command: int
    speed: int
    skill_ids: list[str]
    crit_rate_bps: int = 0
    heal_crit_rate_bps: int = 0
    template_id: str | None = None
    portrait: str | None = None
    innate_skill_id: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return to_jsonable(asdict(self))


@dataclass(slots=True)
class BattleInput:
    battle_id: str
    seed: int
    max_rounds: int
    team_a_heroes: list[HeroConfig]
    team_b_heroes: list[HeroConfig]
    config_version: str

    def to_dict(self) -> dict[str, Any]:
        return to_jsonable(asdict(self))


@dataclass(slots=True)
class SkillConfig:
    skill_id: str
    name: str
    category: SkillCategory
    level: int
    trigger_timings: list[Timing]
    probability_bps: int
    effect_ids: list[str]
    max_trigger_per_round: int | None = None
    max_trigger_per_battle: int | None = None
    valid_round_start: int = 1
    valid_round_end: int = 999
    cooldown_rounds: int = 0
    tags: list[str] = field(default_factory=list)
    params: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class EffectConfig:
    effect_id: str
    name: str
    effect_type: EffectType
    probability_bps: int
    target_policy: TargetPolicy
    target_count: int
    coefficient_bps: int = 0
    based_on_attr: str = "force"
    damage_type: DamageType | None = None
    state_config_id: str | None = None
    duration_rounds: int = 0
    tags: list[str] = field(default_factory=list)
    params: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class StateConfig:
    state_config_id: str
    name: str
    state_type: StateType
    trigger_mode: TriggerMode
    trigger_timings: list[Timing] = field(default_factory=list)
    listen_event_types: list[EventType] = field(default_factory=list)
    duration_rounds: int = 1
    max_stack: int = 1
    dispellable: bool = True
    purifiable: bool = True
    tags: list[str] = field(default_factory=list)
    payload: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class TriggerCheckResult:
    allowed: bool
    reason: str
    roll_bps: int | None = None
    threshold_bps: int | None = None
    rng_index: int | None = None
    pseudo_random_key: str | None = None
    base_rate_bps: int | None = None
    current_rate_bps: int | None = None
    fail_count: int = 0
    success_streak: int = 0
    guarantee_triggered: bool = False


@dataclass(slots=True)
class BattleSummary:
    battle_id: str
    result: str
    winner_team_id: str | None
    rounds: int
    finish_reason: str | None
    hero_summaries: list[dict[str, Any]]
    skill_summaries: list[dict[str, Any]]
    state_summaries: list[dict[str, Any]]
    effect_summaries: list[dict[str, Any]]
    event_count: int
    rng_count: int

    def to_dict(self) -> dict[str, Any]:
        return to_jsonable(asdict(self))
