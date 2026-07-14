from __future__ import annotations

from dataclasses import dataclass, field

from battlecore.config.chain_reaction_config import (
    DEFAULT_REGULAR_GROUPS,
    DEFAULT_SPY_GROUPS,
    DEFAULT_UNCONFIGURED_STATE_SORT,
    RegularGroupConfig,
    SpyGroupConfig,
    TriggerStepConfig,
    UnconfiguredStateSortConfig,
)
from battlecore.config.schema import EffectConfig, SkillConfig, StateConfig
from battlecore.config.hero_files import HERO_TEMPLATES, HeroTemplateConfig
from battlecore.config.basic_test_damage_skills import build_basic_test_damage_bundle
from battlecore.config.skill_files import (
    build_asclepius_oracle_skill,
    build_delphi_charged_oracle_skill,
    build_delphi_revelation_skill,
    build_gorgon_gaze_skill,
    build_hades_underworld_dominion_skill,
    build_pythia_woven_scheme_skill,
    build_thunder_oracle_skill,
)
from battlecore.domain.enums import (
    DamageType,
    EffectType,
    SkillCategory,
    TargetPolicy,
    Timing,
)


@dataclass(slots=True)
class ConfigDB:
    version: str
    skill_configs: dict[str, SkillConfig] = field(default_factory=dict)
    effect_configs: dict[str, EffectConfig] = field(default_factory=dict)
    state_configs: dict[str, StateConfig] = field(default_factory=dict)
    hero_templates: dict[str, HeroTemplateConfig] = field(default_factory=dict)
    spy_groups: tuple[SpyGroupConfig, ...] = field(default_factory=lambda: DEFAULT_SPY_GROUPS)
    # REGULAR 状态按 timing 的响应顺序；见 STATE_RESPONSE_REFERENCE.md
    regular_groups: tuple[RegularGroupConfig, ...] = field(default_factory=lambda: DEFAULT_REGULAR_GROUPS)
    # 未列入 spy_groups / regular_groups steps 的 State 稳定排序键
    state_unconfigured_sort: UnconfiguredStateSortConfig = field(
        default_factory=lambda: DEFAULT_UNCONFIGURED_STATE_SORT
    )


def build_demo_config_db() -> ConfigDB:
    """MVP config table: only basic attack is active, with future tables kept."""
    basic_skill = SkillConfig(
        skill_id="basic_attack",
        name="Basic Attack",
        category=SkillCategory.BASIC,
        level=1,
        trigger_timings=[Timing.BASIC],
        probability_bps=10000,
        effect_ids=["basic_attack_damage"],
    )
    basic_effect = EffectConfig(
        effect_id="basic_attack_damage",
        name="Basic Attack Damage",
        effect_type=EffectType.DAMAGE,
        damage_type=DamageType.PHYSICAL,
        probability_bps=10000,
        target_policy=TargetPolicy.RANDOM_ENEMY,
        target_count=1,
        coefficient_bps=10000,
        based_on_attr="force",
    )
    pursuit_skill = SkillConfig(
        skill_id="pursuit_strike",
        name="突击",
        category=SkillCategory.PURSUIT,
        level=1,
        trigger_timings=[],
        probability_bps=10000,
        effect_ids=["pursuit_strike_damage"],
    )
    pursuit_effect = EffectConfig(
        effect_id="pursuit_strike_damage",
        name="突击伤害",
        effect_type=EffectType.DAMAGE,
        damage_type=DamageType.PHYSICAL,
        probability_bps=10000,
        target_policy=TargetPolicy.SAME_AS_SOURCE_EVENT,
        target_count=1,
        coefficient_bps=5000,
        based_on_attr="force",
    )
    gorgon_skill, gorgon_effects, gorgon_states = build_gorgon_gaze_skill()
    delphi_skill, delphi_effects, delphi_states = build_delphi_revelation_skill()
    asclepius_skill, asclepius_effects, asclepius_states = build_asclepius_oracle_skill()
    thunder_skill, thunder_effects, thunder_states = build_thunder_oracle_skill()
    delphi_charged_skill, delphi_charged_effects, delphi_charged_states = build_delphi_charged_oracle_skill()
    pythia_skill, pythia_effects, pythia_states = build_pythia_woven_scheme_skill()
    hades_skill, hades_effects, hades_states = build_hades_underworld_dominion_skill()
    return ConfigDB(
        version="demo-basic-v1",
        hero_templates=dict(HERO_TEMPLATES),
        skill_configs={
            basic_skill.skill_id: basic_skill,
            pursuit_skill.skill_id: pursuit_skill,
            gorgon_skill.skill_id: gorgon_skill,
            delphi_skill.skill_id: delphi_skill,
            asclepius_skill.skill_id: asclepius_skill,
            thunder_skill.skill_id: thunder_skill,
            delphi_charged_skill.skill_id: delphi_charged_skill,
            pythia_skill.skill_id: pythia_skill,
            hades_skill.skill_id: hades_skill,
        },
        effect_configs={
            basic_effect.effect_id: basic_effect,
            pursuit_effect.effect_id: pursuit_effect,
            **gorgon_effects,
            **delphi_effects,
            **asclepius_effects,
            **thunder_effects,
            **delphi_charged_effects,
            **pythia_effects,
            **hades_effects,
        },
        state_configs={
            **gorgon_states,
            **delphi_states,
            **asclepius_states,
            **thunder_states,
            **delphi_charged_states,
            **pythia_states,
            **hades_states,
        },
    )


def build_basic_test_damage_config_db() -> ConfigDB:
    """Demo config + BASIC_TEST_DAMAGE 标定技能（临时，将来删除）。"""
    base = build_demo_config_db()
    skills, effects, states = build_basic_test_damage_bundle()
    return ConfigDB(
        version="basic-test-damage-v1",
        hero_templates=base.hero_templates,
        skill_configs={**base.skill_configs, **skills},
        effect_configs={**base.effect_configs, **effects},
        state_configs={**base.state_configs, **states},
    )
