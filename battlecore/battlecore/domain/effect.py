from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

from battlecore.config.schema import EffectConfig, TriggerCheckResult
from battlecore.domain.enums import DamageType, EffectType, TargetPolicy

if TYPE_CHECKING:
    from battlecore.domain.hero import Hero
    from battlecore.domain.skill import Skill, State
    from battlecore.engine.battle_context import BattleContext


@dataclass(slots=True)
class Effect:
    instance_id: str
    config_id: str
    name: str
    effect_type: EffectType
    owner_hero: Hero
    owner_skill: Skill | None = None
    owner_state: State | None = None
    probability_bps: int = 10000
    target_policy: TargetPolicy = TargetPolicy.RANDOM_ENEMY
    target_count: int = 1
    coefficient_bps: int = 0
    based_on_attr: str = "force"
    damage_type: DamageType | None = None
    state_config_id: str | None = None
    duration_rounds: int = 0
    tags: list[str] = field(default_factory=list)
    params: dict[str, Any] = field(default_factory=dict)
    success_count: int = 0
    fail_count: int = 0
    total_damage: int = 0
    total_heal: int = 0
    applied_state_count: int = 0
    history: list[dict[str, Any]] = field(default_factory=list)

    @classmethod
    def from_config(
        cls,
        instance_id: str,
        config: EffectConfig,
        owner_hero: Hero,
        owner_skill: Skill | None = None,
        owner_state: State | None = None,
    ) -> Effect:
        return cls(
            instance_id=instance_id,
            config_id=config.effect_id,
            name=config.name,
            effect_type=config.effect_type,
            owner_hero=owner_hero,
            owner_skill=owner_skill,
            owner_state=owner_state,
            probability_bps=config.probability_bps,
            target_policy=config.target_policy,
            target_count=config.target_count,
            coefficient_bps=config.coefficient_bps,
            based_on_attr=config.based_on_attr,
            damage_type=config.damage_type,
            state_config_id=config.state_config_id,
            duration_rounds=config.duration_rounds,
            tags=list(config.tags),
            params=dict(config.params),
        )

    def enabled(self, context: BattleContext) -> TriggerCheckResult:
        if context.battle_finished:
            return TriggerCheckResult(False, "BATTLE_FINISHED")
        if self.owner_hero.exited:
            return TriggerCheckResult(False, "OWNER_EXITED")
        return TriggerCheckResult(True, "OK")

    def roll_probability(self, context: BattleContext, actor: Hero, targets: list[Hero]) -> TriggerCheckResult:
        return context.roll_effect_probability(self, actor, targets)

    def select_actor(self, context: BattleContext) -> Hero:
        return self.owner_hero

    def select_targets(self, context: BattleContext) -> list[Hero]:
        return context.select_targets(self.owner_hero, self.target_policy, self.target_count)

    def execute(self, context: BattleContext, actor: Hero, targets: list[Hero]) -> None:
        for target in targets:
            if self.effect_type in (EffectType.DAMAGE, EffectType.TRUE_DAMAGE):
                context.apply_damage(
                    actor=actor,
                    target=target,
                    amount=0,
                    damage_type=self.damage_type or DamageType.TRUE,
                    skill=self.owner_skill,
                    effect=self,
                    state=self.owner_state,
                )
            elif self.effect_type == EffectType.HEAL:
                context.apply_heal(actor=actor, target=target, amount=0, skill=self.owner_skill, effect=self)
            elif self.state_config_id:
                context.add_state(
                    actor=actor,
                    target=target,
                    state_config_id=self.state_config_id,
                    source_skill=self.owner_skill,
                    source_effect=self,
                    source_state=self.owner_state,
                    duration_override=self.duration_rounds or None,
                )

    def record_success(self) -> None:
        self.success_count += 1

    def record_fail(self, reason: str) -> None:
        self.fail_count += 1
        self.history.append({"result": "fail", "reason": reason})

    def summary(self) -> dict[str, Any]:
        return {
            "effect_id": self.config_id,
            "instance_id": self.instance_id,
            "name": self.name,
            "owner_skill": self.owner_skill.config_id if self.owner_skill else None,
            "owner": self.owner_hero.instance_id,
            "success_count": self.success_count,
            "fail_count": self.fail_count,
            "total_damage": self.total_damage,
            "total_heal": self.total_heal,
            "applied_state_count": self.applied_state_count,
        }
