"""BASIC_TEST_DAMAGE 系列技能配置（伤害标定用，将来删除）。"""

from __future__ import annotations

from dataclasses import dataclass

from battlecore.config.basic_test_damage_descriptions import DESCRIPTIONS
from battlecore.config.schema import EffectConfig, SkillConfig, StateConfig
from battlecore.config.skill_files import build_skill_preparing_state
from battlecore.domain.enums import (
    DamageType,
    EffectType,
    SkillCategory,
    TargetPolicy,
    Timing,
)

_SERIES_TAG = "basic_test_damage"
_PREPARE_PSEUDO_RANDOM = {
    "bonus_per_fail_bps": 1000,
    "penalty_per_success_bps": 800,
    "min_rate_bps": 2000,
    "max_rate_bps": 8000,
    "guarantee_count": 5,
}


@dataclass(frozen=True, slots=True)
class _InstantActiveSpec:
    skill_id: str
    name: str
    probability_bps: int
    coefficient_bps: int
    based_on_attr: str
    damage_type: DamageType


@dataclass(frozen=True, slots=True)
class _PursuitSpec:
    skill_id: str
    name: str
    probability_bps: int
    coefficient_bps: int
    based_on_attr: str
    damage_type: DamageType


@dataclass(frozen=True, slots=True)
class _PrepareSpec:
    skill_id: str
    name: str
    state_name: str
    probability_bps: int
    coefficient_bps: int
    based_on_attr: str
    damage_type: DamageType


_INSTANT_ACTIVE_SPECS: tuple[_InstantActiveSpec, ...] = (
    _InstantActiveSpec("achilles_spear_rush", "阿喀琉斯枪袭", 6000, 25000, "force", DamageType.PHYSICAL),
    _InstantActiveSpec("ares_blood_axe", "阿瑞斯血斧", 5000, 30000, "force", DamageType.PHYSICAL),
    _InstantActiveSpec("heracles_lion_crush", "赫拉克勒斯狮摧", 5000, 40000, "force", DamageType.PHYSICAL),
    _InstantActiveSpec("perseus_gorgon_slash", "珀尔修斯斩首", 4000, 50000, "force", DamageType.PHYSICAL),
    _InstantActiveSpec("athena_tactical_decree", "雅典娜战术敕令", 6000, 25000, "intelligence", DamageType.MAGIC),
    _InstantActiveSpec("apollo_solar_arrow", "阿波罗日冕箭", 5000, 30000, "intelligence", DamageType.MAGIC),
    _InstantActiveSpec("zeus_thunder_judgment", "宙斯雷霆裁决", 5000, 40000, "intelligence", DamageType.MAGIC),
    _InstantActiveSpec("hades_styx_sentence", "哈迪斯冥河判罚", 4000, 50000, "intelligence", DamageType.MAGIC),
)

_PURSUIT_SPECS: tuple[_PursuitSpec, ...] = (
    _PursuitSpec("atalanta_hunting_arrow", "阿塔兰忒猎矢", 6000, 25000, "force", DamageType.PHYSICAL),
    _PursuitSpec("odysseus_hidden_dagger", "奥德修斯藏刃", 5000, 30000, "force", DamageType.PHYSICAL),
    _PursuitSpec("diomedes_god_wound", "狄俄墨得斯伤神", 5000, 40000, "force", DamageType.PHYSICAL),
    _PursuitSpec("hector_last_stand", "赫克托尔城门反击", 4000, 50000, "force", DamageType.PHYSICAL),
    _PursuitSpec("hermes_shadow_message", "赫尔墨斯影信", 6000, 25000, "intelligence", DamageType.MAGIC),
    _PursuitSpec("circe_transfiguring_curse", "喀耳刻变形咒", 5000, 30000, "intelligence", DamageType.MAGIC),
    _PursuitSpec("medea_black_flame", "美狄亚黑焰", 5000, 40000, "intelligence", DamageType.MAGIC),
    _PursuitSpec("erinyes_vengeance_whisper", "厄里倪厄斯复仇低语", 4000, 50000, "intelligence", DamageType.MAGIC),
)

_PREPARE_SPECS: tuple[_PrepareSpec, ...] = (
    _PrepareSpec("theseus_labyrinth_charge", "忒修斯迷宫突进", "迷宫蓄势", 5000, 30000, "force", DamageType.PHYSICAL),
    _PrepareSpec("bellerophon_pegasus_dive", "柏勒洛丰天马坠击", "天马升空", 5000, 40000, "force", DamageType.PHYSICAL),
    _PrepareSpec("delphi_oracle_chant", "德尔斐蓄谕", "神谕吟诵", 5000, 30000, "intelligence", DamageType.MAGIC),
    _PrepareSpec("poseidon_abyssal_tide", "波塞冬深渊潮汐", "深渊蓄潮", 5000, 40000, "intelligence", DamageType.MAGIC),
)


def _series_params(skill_id: str) -> dict:
    return {
        "series": _SERIES_TAG,
        "description": DESCRIPTIONS[skill_id].strip(),
    }


def _build_instant_active(spec: _InstantActiveSpec) -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    effect_id = f"{spec.skill_id}_damage"
    skill = SkillConfig(
        skill_id=spec.skill_id,
        name=spec.name,
        category=SkillCategory.ACTIVE,
        level=1,
        trigger_timings=[Timing.ACTIVE],
        probability_bps=spec.probability_bps,
        effect_ids=[effect_id],
        tags=[_SERIES_TAG],
        params=_series_params(spec.skill_id),
    )
    effects = {
        effect_id: EffectConfig(
            effect_id=effect_id,
            name=f"{spec.name}伤害",
            effect_type=EffectType.DAMAGE,
            damage_type=spec.damage_type,
            probability_bps=10000,
            target_policy=TargetPolicy.RANDOM_ENEMY,
            target_count=1,
            coefficient_bps=spec.coefficient_bps,
            based_on_attr=spec.based_on_attr,
        ),
    }
    return skill, effects, {}


def _build_pursuit(spec: _PursuitSpec) -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    effect_id = f"{spec.skill_id}_damage"
    skill = SkillConfig(
        skill_id=spec.skill_id,
        name=spec.name,
        category=SkillCategory.PURSUIT,
        level=1,
        trigger_timings=[],
        probability_bps=spec.probability_bps,
        effect_ids=[effect_id],
        tags=[_SERIES_TAG],
        params=_series_params(spec.skill_id),
    )
    effects = {
        effect_id: EffectConfig(
            effect_id=effect_id,
            name=f"{spec.name}伤害",
            effect_type=EffectType.DAMAGE,
            damage_type=spec.damage_type,
            probability_bps=10000,
            target_policy=TargetPolicy.SAME_AS_SOURCE_EVENT,
            target_count=1,
            coefficient_bps=spec.coefficient_bps,
            based_on_attr=spec.based_on_attr,
        ),
    }
    return skill, effects, {}


def _build_prepare(spec: _PrepareSpec) -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    state_id = f"{spec.skill_id}_preparing_state"
    prepare_effect_id = f"{spec.skill_id}_prepare_grant"
    release_effect_id = f"{spec.skill_id}_release_damage"
    skill = SkillConfig(
        skill_id=spec.skill_id,
        name=spec.name,
        category=SkillCategory.ACTIVE,
        level=1,
        trigger_timings=[Timing.ACTIVE],
        probability_bps=spec.probability_bps,
        effect_ids=[prepare_effect_id],
        tags=[_SERIES_TAG],
        params={
            **_series_params(spec.skill_id),
            "prepare_rounds": 1,
            "prepare_state_config_id": state_id,
            "prepare_effect_ids": [prepare_effect_id],
            "release_effect_ids": [release_effect_id],
            "pseudo_random": dict(_PREPARE_PSEUDO_RANDOM),
        },
    )
    effects = {
        prepare_effect_id: EffectConfig(
            effect_id=prepare_effect_id,
            name=f"进入{spec.state_name}",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.SELF,
            target_count=1,
            state_config_id=state_id,
            duration_rounds=999,
        ),
        release_effect_id: EffectConfig(
            effect_id=release_effect_id,
            name=f"{spec.name}释放",
            effect_type=EffectType.DAMAGE,
            probability_bps=10000,
            target_policy=TargetPolicy.RANDOM_ENEMY,
            target_count=1,
            coefficient_bps=spec.coefficient_bps,
            based_on_attr=spec.based_on_attr,
            damage_type=spec.damage_type,
        ),
    }
    states = {
        state_id: build_skill_preparing_state(
            state_config_id=state_id,
            name=spec.state_name,
            source_skill_id=spec.skill_id,
        ),
    }
    return skill, effects, states


def build_basic_test_damage_bundle() -> tuple[dict[str, SkillConfig], dict[str, EffectConfig], dict[str, StateConfig]]:
    skills: dict[str, SkillConfig] = {}
    effects: dict[str, EffectConfig] = {}
    states: dict[str, StateConfig] = {}
    for spec in _INSTANT_ACTIVE_SPECS:
        skill, spec_effects, spec_states = _build_instant_active(spec)
        skills[skill.skill_id] = skill
        effects.update(spec_effects)
        states.update(spec_states)
    for spec in _PURSUIT_SPECS:
        skill, spec_effects, spec_states = _build_pursuit(spec)
        skills[skill.skill_id] = skill
        effects.update(spec_effects)
        states.update(spec_states)
    for spec in _PREPARE_SPECS:
        skill, spec_effects, spec_states = _build_prepare(spec)
        skills[skill.skill_id] = skill
        effects.update(spec_effects)
        states.update(spec_states)
    return skills, effects, states


BASIC_TEST_DAMAGE_SKILL_IDS: tuple[str, ...] = tuple(
    spec.skill_id for spec in (*_INSTANT_ACTIVE_SPECS, *_PURSUIT_SPECS, *_PREPARE_SPECS)
)
