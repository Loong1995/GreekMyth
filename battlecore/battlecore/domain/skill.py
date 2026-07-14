from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

from battlecore.config.schema import SkillConfig, StateConfig, TriggerCheckResult
from battlecore.domain.effect import Effect
from battlecore.engine.damage_calculator import calc_snake_staff_base_heal, get_effective_attr
from battlecore.domain.enums import (
    DamageType,
    EventType,
    SkillCategory,
    StateType,
    Timing,
    TriggerableType,
    TriggerMode,
)

if TYPE_CHECKING:
    from battlecore.domain.hero import Hero
    from battlecore.engine.battle_context import BattleContext
    from battlecore.event.battle_event import BattleEvent


@dataclass(slots=True)
class Triggerable:
    instance_id: str
    config_id: str
    name: str
    owner: Hero
    triggerable_type: TriggerableType
    trigger_timings: list[Timing]
    probability_bps: int
    enabled_flag: bool = True
    max_trigger_per_round: int | None = None
    max_trigger_per_battle: int | None = None
    trigger_count_round: int = 0
    trigger_count_battle: int = 0
    success_count: int = 0
    fail_count: int = 0
    history: list[dict[str, Any]] = field(default_factory=list)

    def trigger_phase(self) -> str:
        return self.triggerable_type.value

    def set_enabled(self, value: bool) -> None:
        self.enabled_flag = value

    def enabled(self, context: BattleContext) -> TriggerCheckResult:
        if not self.enabled_flag:
            return TriggerCheckResult(False, "DISABLED")
        if context.battle_finished:
            return TriggerCheckResult(False, "BATTLE_FINISHED")
        if self.owner.exited and not getattr(self, "params", {}).get("allow_after_exit", False):
            return TriggerCheckResult(False, "OWNER_EXITED")
        if self.max_trigger_per_round is not None and self.trigger_count_round >= self.max_trigger_per_round:
            return TriggerCheckResult(False, "MAX_TRIGGER_PER_ROUND")
        if self.max_trigger_per_battle is not None and self.trigger_count_battle >= self.max_trigger_per_battle:
            return TriggerCheckResult(False, "MAX_TRIGGER_PER_BATTLE")
        return TriggerCheckResult(True, "OK")

    def can_trigger_at(
        self, context: BattleContext, timing: Timing, source_event: BattleEvent | None = None
    ) -> TriggerCheckResult:
        base = self.enabled(context)
        if not base.allowed:
            return base
        if timing not in self.trigger_timings:
            return TriggerCheckResult(False, "TIMING_NOT_MATCH")
        return TriggerCheckResult(True, "OK")

    def roll_probability(
        self,
        context: BattleContext,
        source_event: BattleEvent | None = None,
    ) -> TriggerCheckResult:
        return context.roll_trigger_probability(self, source_event=source_event)

    def record_trigger_success(
        self, context: BattleContext, timing: Timing, source_event: BattleEvent | None = None
    ) -> None:
        self.trigger_count_round += 1
        self.trigger_count_battle += 1
        self.success_count += 1
        if self.triggerable_type == TriggerableType.SKILL:
            self.owner.skill_trigger_success += 1
        else:
            self.owner.state_trigger_success += 1
        self.history.append({"round": context.round_no, "timing": timing.value, "result": "success"})

    def record_trigger_fail(
        self,
        context: BattleContext,
        timing: Timing,
        result: TriggerCheckResult,
        source_event: BattleEvent | None = None,
    ) -> None:
        self.fail_count += 1
        if self.triggerable_type == TriggerableType.SKILL:
            self.owner.skill_trigger_fail += 1
        else:
            self.owner.state_trigger_fail += 1
        self.history.append(
            {
                "round": context.round_no,
                "timing": timing.value,
                "result": "fail",
                "reason": result.reason,
            }
        )

    def reset_round_counters(self) -> None:
        self.trigger_count_round = 0

    def summary(self) -> dict[str, Any]:
        return {
            "instance_id": self.instance_id,
            "config_id": self.config_id,
            "name": self.name,
            "owner": self.owner.instance_id,
            "success_count": self.success_count,
            "fail_count": self.fail_count,
            "trigger_count_battle": self.trigger_count_battle,
        }


@dataclass(slots=True)
class Skill(Triggerable):
    category: SkillCategory = SkillCategory.BASIC
    level: int = 1
    effects: list[Effect] = field(default_factory=list)
    cooldown_rounds: int = 0
    current_cooldown: int = 0
    valid_round_start: int = 1
    valid_round_end: int = 999
    tags: list[str] = field(default_factory=list)
    params: dict[str, Any] = field(default_factory=dict)
    effect_execution_records: list[dict[str, Any]] = field(default_factory=list)
    execution_seq: int = 0

    @classmethod
    def from_config(cls, instance_id: str, config: SkillConfig, owner: Hero) -> Skill:
        return cls(
            instance_id=instance_id,
            config_id=config.skill_id,
            name=config.name,
            owner=owner,
            triggerable_type=TriggerableType.SKILL,
            trigger_timings=list(config.trigger_timings),
            probability_bps=config.probability_bps,
            max_trigger_per_round=config.max_trigger_per_round,
            max_trigger_per_battle=config.max_trigger_per_battle,
            category=config.category,
            level=config.level,
            cooldown_rounds=config.cooldown_rounds,
            valid_round_start=config.valid_round_start,
            valid_round_end=config.valid_round_end,
            tags=list(config.tags),
            params=dict(config.params),
        )

    def can_trigger_at(
        self, context: BattleContext, timing: Timing, source_event: BattleEvent | None = None
    ) -> TriggerCheckResult:
        if self.category == SkillCategory.PURSUIT:
            base = self.enabled(context)
            if not base.allowed:
                return base
            if not (self.valid_round_start <= context.round_no <= self.valid_round_end):
                return TriggerCheckResult(False, "ROUND_NOT_VALID")
            if self.current_cooldown > 0:
                return TriggerCheckResult(False, "COOLDOWN")
            if self._is_forbidden("forbid_pursuit"):
                return TriggerCheckResult(False, "CONTROL_FORBID_PURSUIT")
            if not context.is_basic_damage_settled_signal(source_event):
                return TriggerCheckResult(False, "PURSUIT_REQUIRES_BASIC_DAMAGE_SETTLED")
            return TriggerCheckResult(True, "OK")
        base = Triggerable.can_trigger_at(self, context, timing, source_event)
        if not base.allowed:
            return base
        if not (self.valid_round_start <= context.round_no <= self.valid_round_end):
            return TriggerCheckResult(False, "ROUND_NOT_VALID")
        if self.current_cooldown > 0:
            return TriggerCheckResult(False, "COOLDOWN")
        if self.category == SkillCategory.BASIC and self._is_forbidden("forbid_basic"):
            return TriggerCheckResult(False, "CONTROL_FORBID_BASIC")
        if self.category == SkillCategory.ACTIVE and self._is_forbidden("forbid_active"):
            return TriggerCheckResult(False, "CONTROL_FORBID_ACTIVE")
        if (
            self.category == SkillCategory.ACTIVE
            and self.is_preparation_active()
            and Skill.find_active_preparing_state(self.owner, self.config_id) is not None
        ):
            return TriggerCheckResult(False, "ACTIVE_PREPARING")
        return TriggerCheckResult(True, "OK")

    def _is_forbidden(self, payload_key: str) -> bool:
        return any(bool(state.payload.get(payload_key, False)) for state in self.owner.states)

    def choose_actor(self, context: BattleContext) -> Hero:
        return self.owner

    def is_preparation_active(self) -> bool:
        return self.category == SkillCategory.ACTIVE and int(self.params.get("prepare_rounds", 0)) > 0

    def prepare_effect_ids(self) -> list[str]:
        return list(self.params.get("prepare_effect_ids", []))

    def release_effect_ids(self) -> list[str]:
        configured = self.params.get("release_effect_ids")
        if configured:
            return list(configured)
        return [effect.config_id for effect in self.effects]

    def prepare_state_config_id(self) -> str:
        return str(self.params.get("prepare_state_config_id", ""))

    def execute(self, context: BattleContext, source_event: BattleEvent | None = None) -> None:
        context.execute_skill(self, source_event=source_event)

    @staticmethod
    def iter_active_preparing_states(owner: Hero):
        for state in owner.states:
            if "active_preparing" in state.tags:
                yield state

    @staticmethod
    def find_active_preparing_state(owner: Hero, source_skill_id: str) -> State | None:
        for state in Skill.iter_active_preparing_states(owner):
            if str(state.payload.get("source_skill_id", "")) == source_skill_id:
                return state
        return None

    def summary(self) -> dict[str, Any]:
        data = Triggerable.summary(self)
        data.update(
            {
                "skill_id": self.config_id,
                "category": self.category.value,
                "effect_execution_records": list(self.effect_execution_records),
            }
        )
        return data


@dataclass(slots=True)
class State(Triggerable):
    """运行时状态实例。

    分类与配置建议见 STATE_RESPONSE_REFERENCE.md。概要：

    - ATTR / DAMAGE_REDUCE + trigger_mode=NONE：被动数值，不进触发索引
    - trigger_mode=REGULAR：在 trigger_timings 由 run_timing 主动 execute
    - trigger_mode=SPY：在 listen_event_types 由 dispatch_events 响应 source_event

    State 是 Triggerable 但不是 Skill；复杂流程应拆成 Effect + 简单 State。
    """

    state_config_id: str = ""
    state_type: StateType = StateType.BUFF
    trigger_mode: TriggerMode = TriggerMode.NONE
    listen_event_types: list[EventType] = field(default_factory=list)
    duration_rounds: int = 1
    remaining_rounds: int = 1
    action_tick_count: int = 0
    stack: int = 1
    max_stack: int = 1
    dispellable: bool = True
    purifiable: bool = True
    tags: list[str] = field(default_factory=list)
    payload: dict[str, Any] = field(default_factory=dict)
    effects: list[Effect] = field(default_factory=list)
    responded_event_ids: set[int] = field(default_factory=set)
    source_actor_id: str | None = None
    source_skill_id: str | None = None

    @classmethod
    def from_config(cls, instance_id: str, config: StateConfig, owner: Hero) -> State:
        return cls(
            instance_id=instance_id,
            config_id=config.state_config_id,
            name=config.name,
            owner=owner,
            triggerable_type=TriggerableType.STATE,
            trigger_timings=list(config.trigger_timings),
            probability_bps=int(config.payload.get("probability_bps", 10000)),
            max_trigger_per_round=config.payload.get("max_trigger_per_round"),
            max_trigger_per_battle=config.payload.get("max_trigger_per_battle"),
            state_config_id=config.state_config_id,
            state_type=config.state_type,
            trigger_mode=config.trigger_mode,
            listen_event_types=list(config.listen_event_types),
            duration_rounds=config.duration_rounds,
            remaining_rounds=config.duration_rounds,
            max_stack=config.max_stack,
            dispellable=config.dispellable,
            purifiable=config.purifiable,
            tags=list(config.tags),
            payload=dict(config.payload),
        )

    def enabled(self, context: BattleContext) -> TriggerCheckResult:
        base = Triggerable.enabled(self, context)
        if not base.allowed:
            return base
        if self.source_actor_id:
            source = context.heroes.get(self.source_actor_id)
            if source is not None and source.exited:
                return TriggerCheckResult(False, "SOURCE_EXITED")
        return TriggerCheckResult(True, "OK")

    def should_trigger_by_event(self, context: BattleContext, event: BattleEvent) -> bool:
        listen = [event_type.value for event_type in self.listen_event_types]
        if not listen:
            return True
        if "snake_staff_protection" in self.tags:
            if self.owner.instance_id not in event.target_ids:
                return False
            if int(event.payload.get("damage", 0)) <= 0:
                return False
        if "thunder_oracle" in self.tags:
            if event.actor_id != self.owner.instance_id:
                return False
            if event.state_instance_id:
                source_state = context.state_instances.get(event.state_instance_id)
                if source_state and "thunder_oracle" in source_state.tags:
                    return False
        if "styx_blood_oath" in self.tags and event.event_type == EventType.DAMAGE_SETTLED:
            if event.actor_id != self.owner.instance_id:
                return False
            if int(event.payload.get("damage", 0)) <= 0:
                return False
        return event.event_type.value in listen

    def should_trigger_by_timing(self, context: BattleContext, timing: Timing) -> bool:
        return timing in self.trigger_timings

    def can_trigger_at(
        self, context: BattleContext, timing: Timing, source_event: BattleEvent | None = None
    ) -> TriggerCheckResult:
        base = self.enabled(context)
        if not base.allowed:
            return base
        if self.trigger_mode == TriggerMode.SPY and not self.trigger_timings:
            return TriggerCheckResult(True, "OK")
        if timing not in self.trigger_timings:
            return TriggerCheckResult(False, "TIMING_NOT_MATCH")
        return TriggerCheckResult(True, "OK")

    def invalid_target_reason(
        self, context: BattleContext, source_event: BattleEvent
    ) -> dict[str, str] | None:
        if source_event.event_type == EventType.DAMAGE_SETTLED and "thunder_oracle" in self.tags:
            if source_event.actor_id != self.owner.instance_id or not source_event.target_ids:
                return None
            target_id = source_event.target_ids[0]
            target, reason = context.resolve_hero_for_state_effect(target_id)
            if reason is None:
                return None
            return {"reason": reason, "target_name": target.name if target is not None else target_id}
        if source_event.event_type == EventType.DAMAGE_SETTLED and "snake_staff_protection" in self.tags:
            if self.owner.instance_id not in source_event.target_ids:
                return None
            target, reason = context.resolve_hero_for_state_effect(self.owner.instance_id)
            if reason is None:
                return None
            return {"reason": reason, "target_name": self.owner.name}
        return None

    def execute(self, context: BattleContext, source_event: BattleEvent | None = None) -> None:
        if source_event is None:
            if "shadow_veil" in self.tags:
                self._execute_shadow_veil(context)
                return None
            if "hades_command_drain" in self.tags:
                self._execute_hades_command_drain(context)
                return None
            return None
        skip = self.invalid_target_reason(context, source_event)
        if skip is not None:
            context.log_state_skipped_invalid_target(self, **skip)
            return None
        if source_event.event_type == EventType.DAMAGE_SETTLED and "styx_blood_oath" in self.tags:
            if source_event.actor_id != self.owner.instance_id:
                return None
            damage = int(source_event.payload.get("damage", 0))
            if damage <= 0:
                return None
            heal_bps = int(self.payload.get("heal_damage_bps", 1000))
            heal_amount = damage * heal_bps // 10000
            if heal_amount <= 0:
                return None
            context.log(
                f"{self.owner.name} 的 {self.name} 将伤害 {damage} 的 {heal_bps / 100:.0f}% 转化为治疗 {heal_amount}"
            )
            context.apply_heal(
                actor=self.owner,
                target=self.owner,
                amount=heal_amount,
                state=self,
            )
            return None
        if source_event.event_type == EventType.DAMAGE_SETTLED and "snake_staff_protection" in self.tags:
            if self.owner.instance_id not in source_event.target_ids:
                return None
            if int(source_event.payload.get("damage", 0)) <= 0:
                return None
            oracle_holder = context.heroes.get(self.source_actor_id or self.owner.instance_id, self.owner)
            heal_amount = calc_snake_staff_base_heal(
                self.owner,
                oracle_holder,
                heal_max_troop_bps=int(self.payload.get("heal_max_troop_bps", 0)),
                heal_source_intelligence_bps=int(self.payload.get("heal_source_intelligence_bps", 0)),
            )
            context.apply_heal(
                actor=oracle_holder,
                target=self.owner,
                amount=heal_amount,
                state=self,
            )
        if source_event.event_type == EventType.DAMAGE_SETTLED and "thunder_oracle" in self.tags:
            if source_event.actor_id != self.owner.instance_id or not source_event.target_ids:
                return None
            target = context.heroes[source_event.target_ids[0]]
            triggerer = self.owner
            context.log(f"{triggerer.name} 的 {self.name} 召来落雷，触发者={triggerer.name}，目标={target.name}")
            context.apply_damage(
                actor=triggerer,
                target=target,
                amount=0,
                damage_type=DamageType.MAGIC,
                state=self,
            )
        return None

    def _execute_shadow_veil(self, context: BattleContext) -> None:
        entry_troops = int(self.payload.get("entry_troops", self.owner.max_troops))
        current_troops = max(0, int(self.owner.troops))
        if entry_troops <= 0:
            loss_ratio_bps = 0
        else:
            lost_troops = max(0, entry_troops - current_troops)
            loss_ratio_bps = lost_troops * 10000 // entry_troops
        max_reduce_bps = int(self.payload.get("max_damage_reduce_bps", 5000))
        reduce_bps = min(max_reduce_bps, loss_ratio_bps * max_reduce_bps // 10000)
        old_reduce_bps = int(self.payload.get("damage_reduce_bps", 0))
        self.payload["damage_reduce_bps"] = reduce_bps
        if old_reduce_bps != reduce_bps:
            context.log(
                f"{self.owner.name} 的 {self.name} 更新减伤："
                f"损失比例={loss_ratio_bps / 100:.2f}%，减伤={reduce_bps / 100:.2f}%"
            )

    def _execute_hades_command_drain(self, context: BattleContext) -> None:
        drain_delta = int(self.payload.get("drain_command_delta", 5))
        allies = [
            hero
            for hero in context.get_alive_heroes(self.owner.team_id)
            if hero.instance_id != self.owner.instance_id
        ]
        if not allies:
            return
        total_absorbed = 0
        for ally in allies:
            available_command = get_effective_attr(ally, "command")
            actual_drain = min(drain_delta, max(0, available_command))
            if actual_drain <= 0:
                continue
            context.accumulate_attr_state_payload(
                self.owner,
                ally,
                "hades_command_loss_state",
                {"command_delta": -actual_drain},
                source_state=self,
            )
            total_absorbed += actual_drain
        if total_absorbed > 0:
            context.accumulate_attr_state_payload(
                self.owner,
                self.owner,
                "hades_force_gain_state",
                {"force_delta": total_absorbed},
                source_state=self,
            )
        sacrifice_state = context._find_state_by_config_id(self.owner, "hades_force_gain_state")
        force_total = int(sacrifice_state.payload.get("force_delta", 0)) if sacrifice_state else 0
        context.log(
            f"{self.owner.name} 的 {self.name} 冥祭献统："
            f"献祭武力 state force_delta={force_total}"
        )

    def tick_duration(self, context: BattleContext, timing: Timing) -> None:
        if (
            timing == Timing.ROUND_END
            and self.payload.get("duration_tick_mode") == "ROUND_END"
            and self.duration_rounds < 999
        ):
            self.remaining_rounds -= 1

    def is_expired(self) -> bool:
        return self.duration_rounds < 999 and self.remaining_rounds <= 0

    def summary(self) -> dict[str, Any]:
        data = Triggerable.summary(self)
        data.update(
            {
                "state_instance_id": self.instance_id,
                "owner": self.owner.instance_id,
                "source": self.source_actor_id,
                "remaining_rounds": self.remaining_rounds,
                "action_tick_count": self.action_tick_count,
                "listen_event_types": [event_type.value for event_type in self.listen_event_types],
                "stack": self.stack,
                "expired_or_active": "expired" if self.is_expired() else "active",
            }
        )
        return data
