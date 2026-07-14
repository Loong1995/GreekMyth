from __future__ import annotations

from battlecore.config.schema import EffectConfig, SkillConfig, StateConfig
from battlecore.domain.enums import (
    DamageType,
    EffectType,
    EventType,
    SkillCategory,
    StateType,
    TargetPolicy,
    Timing,
    TriggerMode,
)


# =============================================================================
# 技能配置文件书写约定
# =============================================================================
#
# 1. 每个技能配置前必须先写自然语言描述。
#    目的不是给程序执行，而是给策划、数值和工程后续维护时快速理解：
#    - 这个技能在什么 timing 触发。
#    - 施加哪些 Effect。
#    - 产生哪些 State。
#    - State 是纯数值常驻、事件监听，还是控制 / DOT / HOT。
#
# 2. SkillConfig 只表达“技能何时触发、触发率多少、按什么顺序执行 effects”。
#    例如【德尔斐启示】在 PREPARE 触发，只有一个施加状态的 Effect。
#
# 3. EffectConfig 是原子动作。
#    例如“对己方全体施加神示”或“对目标造成 MAGIC 伤害”。
#    多段技能不要写成一个巨大的硬编码逻辑，而是拆成多个顺序 Effect。
#
# 4. StateConfig 表达状态本身的运行方式（详见 STATE_RESPONSE_REFERENCE.md）：
#    - StateType.ATTR + TriggerMode.NONE：
#      属性型状态，供四维与增伤/易伤等 ATTR 模型读取；不主动触发时可不进 timing 索引。
#      也可配置 REGULAR/SPY 监听并动态更新 payload。例如【神示】、【被汲取统率】。
#    - StateType.DAMAGE_REDUCE：
#      减伤乘区；payload 提供 damage_reduce_bps。可 REGULAR 在 BEFORE_ACTION 刷新（【幽影蔽体】）。
#    - StateType.SPECIAL + TriggerMode.SPY：
#      监听型；listen_event_types 订阅事件，dispatch_events 时按 SpyGroupConfig 顺序触发。
#      例如【蛇杖庇护】监听 DAMAGE_SETTLED。
#    - StateType.SPECIAL + TriggerMode.REGULAR：
#      定时主动触发；trigger_timings 指定 timing，按 RegularGroupConfig 顺序执行。
#      例如【冥祭献统】在 BEFORE_ACTION 献祭统率。
#    - StateType.CONTROL + TriggerMode.NONE：
#      控制状态；payload 的 forbid_basic / forbid_active 影响 Skill.can_trigger_at。
#
# 5. 技能描述只写「基础值」，结算修正由 BattleContext 统一处理：
#    - 技能/Effect/State payload 提供系数、固定分量等基础参数。
#    - apply_damage / apply_heal 结算时再叠加暴击、随机系数、增减伤/增减疗等。
#    - 例如【蛇杖庇护】描述「1% 最大兵力 + 1×智力」为基础量；
#      治疗暴击与治疗增减在 apply_heal 结算层处理，不在基础公式里重复。
#    - 除非技能描述或 payload 明确说明 skip_heal_modifiers 等例外，均遵循此原则。
#

ARES_DESCRIPTION = """
【战神怒火】

神谕技能，战斗准备阶段触发。

全场英雄进入血战状态，持续整场，该状态导致受到物理易伤提升30%，物理类暴击率提升20%。
我方最高武力的英雄武力提升5点，速度提升5点。

"""

HELAKEKLEOS_DESCRIPTION = """
【十二试炼】

被动技能，战斗准备阶段触发。

自身每次收到攻击时，有70概率触发一次试炼：武力提升2点，物理吸血提升2%，并对随即两名敌方英雄造成60%伤害，持续整场,，最多触发12次。

"""

MEDUSA_DESCRIPTION = """
【石化凝视】

被动技能，战斗准备阶段触发。

自身每次收到攻击时，有70概率触发一次凝视：自身吸取伤害来源的2点智力持续整场，并石化伤害来源的敌方英雄持续一回合。

"""



DELPHI_REVELATION_DESCRIPTION = """
【德尔斐启示】

神谕技能，战斗准备阶段触发。

英雄聆听来自德尔斐圣所的阿波罗神谕，为己方全体队友揭示胜利征兆。
己方全体单位获得【神示】效果，四维属性各提高 10 点，持续整场战斗。

技能效果：
- PREPARE 阶段发动。
- 对己方全体单位施加【神示】。
- 【神示】是 ATTR 型状态，不监听、不主动触发，只在数值模型中持续生效。
- 武力 +10、智力 +10、统率 +10、敏捷 +10。
- 持续至战斗结束。

技能描述：
德尔斐的圣火映照战场，阿波罗的预言穿透迷雾，
受神谕庇护者将在命运的指引下展现更强的力量、谋略、统御与身法。
"""


ASCLEPIUS_ORACLE_DESCRIPTION = """
【阿斯克勒庇俄斯圣谕】

神谕技能，战斗准备阶段触发。

英雄向医神阿斯克勒庇俄斯献上祈愿，使己方全体队友获得长期治疗效果【蛇杖庇护】。
携带【蛇杖庇护】的单位每次受到实际伤害（damage>0）结算后，会监听 DAMAGE_SETTLED 信号，
并有 40% 概率恢复生命值。

技能效果：
- PREPARE 阶段发动。
- 对己方全体单位施加【蛇杖庇护】。
- 【蛇杖庇护】监听 DAMAGE_SETTLED。
- 每次携带者受到实际伤害（payload.damage>0）后，40% 概率触发治疗。
- 基础治疗量 = 目标最大兵力的 1% + 【阿斯克勒庇俄斯圣谕】战法持有者智力的 1 倍。
- 治疗只能恢复伤兵，不能复活阵亡。
- 持续至战斗结束。

技能描述：
医神的蛇杖在战场阴影中低鸣，阿斯克勒庇俄斯的圣谕守护着濒危之人；
每当受庇护者承受创伤，神圣医术便有机会回应伤痛，使血肉复苏、生命回返。
"""


THUNDER_ORACLE_DESCRIPTION = """
【雷霆神谕】

神谕技能，战斗准备阶段触发。

英雄呼唤宙斯的雷霆神力，为己方全体单位施加【雷霆】。
携带【雷霆】的单位在造成任意非落雷伤害后，有 70% 概率对本次受击目标追加一次【落雷】，每名英雄每回合最多触发三次落雷。

技能效果：
- PREPARE 阶段发动。
- 对己方全体单位施加【雷霆】。
- 【雷霆】是 SPY 型状态，监听 DAMAGE_SETTLED。
- 当携带者造成伤害结算后（含 damage=0 的结算信号），70% 概率对本次受击目标追加一次 MAGIC 落雷伤害。
- 落雷伤害系数默认 100%，可通过 damage_coefficient_bps 调整。
- 落雷伤害使用触发者自己的 intelligence。
- 落雷本身不会再次触发【雷霆】，避免递归连锁。
- 持续至战斗结束。

技能描述：
宙斯的雷云笼罩战场，受雷霆庇护者每次击中敌人时，
皆有机会引来天罚之雷，对敌军追加谋略打击。
"""

HADES_UNDERWORLD_DOMINION_DESCRIPTION = """
【冥域君临】

哈迪斯自带神谕战法，战斗准备阶段（PREPARE）触发，整场战斗各生效一次。

概览：
冥王以冥域权威献祭友军——全军受血誓与幽影庇佑；哈迪斯每次行动前从队友身上抽走统率，
并将抽到的统率点数 **1:1 转为自身武力**，愈献祭愈凶猛；队友阵亡后，已转化的武力永久保留。

技能效果（SkillConfig）：
- skill_id: hades_underworld_dominion
- category: COMMAND（神谕）
- trigger_timings: [PREPARE]
- probability_bps: 10000（必发）
- max_trigger_per_battle: 1
- effect_ids 按顺序执行：
  1. hades_grant_styx_blood_oath — 对己方全体施加【冥河血誓】
  2. hades_grant_shadow_veil — 对己方全体施加【幽影蔽体】
  3. hades_grant_command_drain — 仅对哈迪斯自身施加【冥祭献统】

Effect 说明：
- hades_grant_styx_blood_oath
  - EffectType.SPECIAL_STATE_GRANT
  - TargetPolicy.ALLY_ALL，state_config_id=styx_blood_oath_state
- hades_grant_shadow_veil
  - EffectType.SPECIAL_STATE_GRANT
  - TargetPolicy.ALLY_ALL，state_config_id=shadow_veil_state
- hades_grant_command_drain
  - EffectType.SPECIAL_STATE_GRANT
  - TargetPolicy.SELF，state_config_id=hades_command_drain_state

【冥河血誓】styx_blood_oath_state：
- StateType.SPECIAL + TriggerMode.SPY
- listen_event_types: [DAMAGE_SETTLED]
- tags: styx_blood_oath
- 触发条件：事件 actor_id 等于状态持有者（携带者「造成伤害结算」时）
- 且 payload.damage > 0
- 效果：治疗量 = damage × heal_damage_bps / 10000（payload heal_damage_bps=1000，即 10%）
- 对持有者自身 apply_heal；skip_heal_modifiers=true，不走治疗增减乘区
- 只能恢复伤兵，不能复活阵亡
- 持续整场（duration_rounds=999）

【幽影蔽体】shadow_veil_state：
- StateType.DAMAGE_REDUCE（减伤乘区专用，与 ATTR 分离便于查询）
- TriggerMode.REGULAR + trigger_timings: [BEFORE_ACTION]
- tags: shadow_veil, damage_reduce_zone
- 每次持有者自己行动前（BEFORE_ACTION）刷新 payload：
  - entry_troops：PREPARE 施加时记录为当时兵力（add_state 写入）
  - loss_ratio_bps = (entry_troops - current_troops) × 10000 / entry_troops
  - damage_reduce_bps = min(max_damage_reduce_bps, loss_ratio_bps × max_damage_reduce_bps / 10000)
  - max_damage_reduce_bps=5000，即最高 50% 减伤
- 兵力损失越多，减伤越高；满编时减伤为 0
- 减伤值由伤害模型 _damage_reduce_bps 乘区读取，不参与四维 ATTR 汇总
- 持续整场

【冥祭献统】hades_command_drain_state：
- StateType.SPECIAL + TriggerMode.REGULAR + trigger_timings: [BEFORE_ACTION]
- tags: hades_command_drain
- 仅 PREPARE 时授予哈迪斯本人；仅在其自己行动前触发
- payload drain_command_delta=5
- 执行逻辑（State._execute_hades_command_drain）：
  - 遍历己方存活武将（不含自身）
  - 每名友军实际汲取 = min(drain_command_delta, 当前有效统率)，有效统率不会低于 0
  - 友军累加【统率削减】hades_command_loss_state 的 command_delta -= 实际汲取量
  - 哈迪斯累加【献祭武力】hades_force_gain_state 的 force_delta += 实际汲取量（1 点统率 → 1 点武力）
  - 汇总日志读取 hades_force_gain_state 的 force_delta，不按被献祭者列表统计
  - 每回合行动重复执行，持续至战斗结束

【统率削减】hades_command_loss_state：
- StateType.ATTR + TriggerMode.NONE（被动属性修正，不进 timing 索引）
- tags: hades_command_loss, attr, attr_decrease
- 不由 PREPARE 直接授予；首次献祭时由 BattleContext.accumulate_attr_state_payload 创建
- command_delta 随每轮献祭累加（如 -5、-10、-15…）
- 参与 get_effective_attr("command") 统率统计
- 队友阵亡时随其自身状态一并移除

【献祭武力】hades_force_gain_state：
- StateType.ATTR + TriggerMode.NONE
- tags: hades_force_gain, attr, attr_increase
- 挂在哈迪斯身上，force_delta 随献祭累加（与当次汲取统率等量）
- 参与 get_effective_attr("force") 武力统计
- 队友阵亡时 **不** 移除、不回滚；仅随哈迪斯自身退场而消失
  （因为该 state 的 owner/source 均为哈迪斯，自然兼容通用阵亡清理）

配置拆分原则：
- Skill 只表达 PREPARE 时机与 3 个顺序 Effect，不写死业务分支。
- 冥河血誓 / 幽影蔽体 / 冥祭献统的运行时行为在 State.execute、
  should_trigger_by_event 中按 tags 分发。
- 减伤走 DAMAGE_REDUCE 乘区 state；统率削减与武力献祭走 ATTR state，二者职责分离。

技能描述：
冥王双目微阖，战场便坠入永恒的暗影。
全军受斯堤克斯河誓言加护，每次挥刃都牵出一条亡者的悲鸣，令敌魂碎裂为冥土的活气，滋养伤创；
战士们越是逼近死亡，身躯就越如薄雾般难以触及，凡俗的锋刃与神咒皆被幽冥稀释。
而哈迪斯以冥祭之名，从队友骨髓中抽走统御意志，将每一丝统率都熔铸为自身的毁灭之力——
献祭愈深，冥王愈猛；即便祭品陨落，已夺来的武力亦永不归还冥府。
"""

GORGON_GAZE_DESCRIPTION = """
【戈耳工凝视】

主动战法，发动率 35%。

效果说明：
- 对敌军两名单体分别造成一次 MAGIC（谋略）伤害。
- 每个目标的伤害系数为 100%，使用施法者 intelligence 作为进攻属性。
- 每个受伤目标会独立进行一次 45% 概率判定。
- 判定成功时，为该目标施加【冥锁】1 回合。

【冥锁】效果：
- 控制状态。
- 持有者无法触发 ACTIVE。
- 持有者无法触发 BASIC。
- 状态持续时间按目标自己的 BEFORE_ACTION 计数：
  目标获得状态后，每次轮到该目标行动前计数 +1；
  当计数大于配置持续回合数时，状态移除。

配置拆分：
- 该战法不是一个硬编码技能，而是由 4 个顺序 Effect 组成。
- effect1：选择敌方目标 1 并造成 MAGIC 伤害，同时把目标保存为 gorgon_target_1。
- effect2：复用 gorgon_target_1，对同一目标尝试施加【冥锁】。
- effect3：排除 gorgon_target_1 后选择敌方目标 2，并造成 MAGIC 伤害，同时保存为 gorgon_target_2。
- effect4：复用 gorgon_target_2，对同一目标尝试施加【冥锁】。
"""


def build_skill_preparing_state(
    *,
    state_config_id: str,
    name: str,
    source_skill_id: str,
) -> StateConfig:
    """为单个准备型主动战法构建独立准备 state（payload.source_skill_id 用于区分）。"""
    return StateConfig(
        state_config_id=state_config_id,
        name=name,
        state_type=StateType.SPECIAL,
        trigger_mode=TriggerMode.NONE,
        duration_rounds=999,
        max_stack=1,
        tags=["active_preparing"],
        payload={
            "prepare_ticks": 0,
            "prepare_rounds": 1,
            "source_skill_id": source_skill_id,
        },
    )


def build_delphi_charged_oracle_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    skill = SkillConfig(
        skill_id="delphi_charged_oracle",
        name="德尔斐蓄谕",
        category=SkillCategory.ACTIVE,
        level=1,
        trigger_timings=[Timing.ACTIVE],
        probability_bps=5000,
        effect_ids=["delphi_charged_prepare_grant"],
        params={
            "prepare_rounds": 1,
            "prepare_state_config_id": "delphi_charged_preparing_state",
            "prepare_effect_ids": ["delphi_charged_prepare_grant"],
            "release_effect_ids": ["delphi_charged_release_damage"],
            "pseudo_random": {
                "bonus_per_fail_bps": 1000,
                "penalty_per_success_bps": 800,
                "min_rate_bps": 2000,
                "max_rate_bps": 8000,
                "guarantee_count": 5,
            },
        },
    )
    effects = {
        "delphi_charged_prepare_grant": EffectConfig(
            effect_id="delphi_charged_prepare_grant",
            name="进入神谕吟诵",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.SELF,
            target_count=1,
            state_config_id="delphi_charged_preparing_state",
            duration_rounds=999,
        ),
        "delphi_charged_release_damage": EffectConfig(
            effect_id="delphi_charged_release_damage",
            name="德尔斐蓄谕落咒",
            effect_type=EffectType.DAMAGE,
            probability_bps=10000,
            target_policy=TargetPolicy.RANDOM_ENEMY,
            target_count=1,
            coefficient_bps=30000,
            based_on_attr="intelligence",
            damage_type=DamageType.MAGIC,
        ),
    }
    states = {
        "delphi_charged_preparing_state": build_skill_preparing_state(
            state_config_id="delphi_charged_preparing_state",
            name="神谕吟诵",
            source_skill_id="delphi_charged_oracle",
        ),
    }
    return skill, effects, states


def build_pythia_woven_scheme_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    skill = SkillConfig(
        skill_id="pythia_woven_scheme",
        name="皮提亚筹谋",
        category=SkillCategory.ACTIVE,
        level=1,
        trigger_timings=[Timing.ACTIVE],
        probability_bps=5000,
        effect_ids=["pythia_woven_prepare_grant"],
        params={
            "prepare_rounds": 1,
            "prepare_state_config_id": "pythia_woven_preparing_state",
            "prepare_effect_ids": ["pythia_woven_prepare_grant"],
            "release_effect_ids": ["pythia_woven_release_damage"],
            "pseudo_random": {
                "bonus_per_fail_bps": 1000,
                "penalty_per_success_bps": 800,
                "min_rate_bps": 2000,
                "max_rate_bps": 8000,
                "guarantee_count": 5,
            },
        },
    )
    effects = {
        "pythia_woven_prepare_grant": EffectConfig(
            effect_id="pythia_woven_prepare_grant",
            name="进入筹谋酝酿",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.SELF,
            target_count=1,
            state_config_id="pythia_woven_preparing_state",
            duration_rounds=999,
        ),
        "pythia_woven_release_damage": EffectConfig(
            effect_id="pythia_woven_release_damage",
            name="皮提亚筹谋落策",
            effect_type=EffectType.DAMAGE,
            probability_bps=10000,
            target_policy=TargetPolicy.RANDOM_ENEMY,
            target_count=1,
            coefficient_bps=25000,
            based_on_attr="intelligence",
            damage_type=DamageType.MAGIC,
        ),
    }
    states = {
        "pythia_woven_preparing_state": build_skill_preparing_state(
            state_config_id="pythia_woven_preparing_state",
            name="筹谋酝酿",
            source_skill_id="pythia_woven_scheme",
        ),
    }
    return skill, effects, states



def build_delphi_revelation_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    skill = SkillConfig(
        skill_id="delphi_revelation",
        name="德尔斐启示",
        category=SkillCategory.COMMAND,
        level=1,
        trigger_timings=[Timing.PREPARE],
        probability_bps=10000,
        effect_ids=["delphi_revelation_grant"],
        max_trigger_per_battle=1,
        valid_round_start=0,
        params={"description": DELPHI_REVELATION_DESCRIPTION.strip()},
    )
    effects = {
        "delphi_revelation_grant": EffectConfig(
            effect_id="delphi_revelation_grant",
            name="施加神示",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.ALLY_ALL,
            target_count=3,
            state_config_id="divine_revelation_state",
            duration_rounds=999,
        )
    }
    states = {
        "divine_revelation_state": StateConfig(
            state_config_id="divine_revelation_state",
            name="神示",
            state_type=StateType.ATTR,
            trigger_mode=TriggerMode.NONE,
            duration_rounds=999,
            max_stack=1,
            tags=["divine_revelation", "attr"],
            payload={
                "force_delta": 10,
                "intelligence_delta": 10,
                "command_delta": 10,
                "speed_delta": 10,
            },
        )
    }
    return skill, effects, states


def build_thunder_oracle_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    skill = SkillConfig(
        skill_id="thunder_oracle",
        name="雷霆神谕",
        category=SkillCategory.COMMAND,
        level=1,
        trigger_timings=[Timing.PREPARE],
        probability_bps=10000,
        effect_ids=["thunder_oracle_grant"],
        max_trigger_per_battle=1,
        valid_round_start=0,
        params={"description": THUNDER_ORACLE_DESCRIPTION.strip()},
    )
    effects = {
        "thunder_oracle_grant": EffectConfig(
            effect_id="thunder_oracle_grant",
            name="施加雷霆",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.ALLY_ALL,
            target_count=3,
            state_config_id="thunder_state",
            duration_rounds=999,
        )
    }
    states = {
        "thunder_state": StateConfig(
            state_config_id="thunder_state",
            name="雷霆",
            state_type=StateType.SPECIAL,
            trigger_mode=TriggerMode.SPY,
            listen_event_types=[EventType.DAMAGE_SETTLED],
            duration_rounds=999,
            max_stack=1,
            tags=["thunder_oracle", "lightning_follow_up"],
            payload={
                "probability_bps": 7000,
                "damage_coefficient_bps": 10000,
                "based_on_attr": "intelligence",
                "damage_type": DamageType.MAGIC.value,
                "ignore_lightning_damage": True,
                "max_trigger_per_round": 3,
                "pseudo_random": {
                    "bonus_per_fail_bps": 900,
                    "penalty_per_success_bps": 700,
                    "min_rate_bps": 3000,
                    "max_rate_bps": 8500,
                    "guarantee_count": 4,
                },
            },
        )
    }
    return skill, effects, states


def build_asclepius_oracle_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    skill = SkillConfig(
        skill_id="asclepius_oracle",
        name="阿斯克勒庇俄斯圣谕",
        category=SkillCategory.COMMAND,
        level=1,
        trigger_timings=[Timing.PREPARE],
        probability_bps=10000,
        effect_ids=["asclepius_oracle_grant"],
        max_trigger_per_battle=1,
        valid_round_start=0,
        params={"description": ASCLEPIUS_ORACLE_DESCRIPTION.strip()},
    )
    effects = {
        "asclepius_oracle_grant": EffectConfig(
            effect_id="asclepius_oracle_grant",
            name="施加蛇杖庇护",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.ALLY_ALL,
            target_count=3,
            state_config_id="snake_staff_protection_state",
            duration_rounds=999,
        )
    }
    states = {
        "snake_staff_protection_state": StateConfig(
            state_config_id="snake_staff_protection_state",
            name="蛇杖庇护",
            state_type=StateType.SPECIAL,
            trigger_mode=TriggerMode.SPY,
            listen_event_types=[EventType.DAMAGE_SETTLED],
            duration_rounds=999,
            max_stack=1,
            tags=["snake_staff_protection"],  # SPY 顺序：chain_reaction_config.DAMAGE_SETTLED_SPY 第 2 步
            payload={
                "probability_bps": 4000,
                "heal_max_troop_bps": 100,
                "heal_source_intelligence_bps": 10000,
                "pseudo_random": {
                    "bonus_per_fail_bps": 800,
                    "penalty_per_success_bps": 600,
                    "min_rate_bps": 2000,
                    "max_rate_bps": 7000,
                    "guarantee_count": 5,
                },
            },
        )
    }
    return skill, effects, states


def build_hades_underworld_dominion_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    """构建【冥域君临】配置。

    该函数顶部的 HADES_UNDERWORLD_DOMINION_DESCRIPTION 是战法自然语言说明；
    下方 SkillConfig / EffectConfig / StateConfig 是同一设计的结构化配置。

    关联运行时：battlecore.domain.skill.State（styx_blood_oath / shadow_veil / hades_command_drain tags）
    与 battlecore.engine.damage_calculator（ATTR 四维、DAMAGE_REDUCE 减伤乘区）。
    """
    # --- Skill：PREPARE 神谕，3 个顺序施加状态 Effect ---
    skill = SkillConfig(
        skill_id="hades_underworld_dominion",
        name="冥域君临",
        category=SkillCategory.COMMAND,
        level=1,
        trigger_timings=[Timing.PREPARE],
        probability_bps=10000,
        effect_ids=[
            "hades_grant_styx_blood_oath",
            "hades_grant_shadow_veil",
            "hades_grant_command_drain",
        ],
        max_trigger_per_battle=1,
        valid_round_start=0,
        params={"description": HADES_UNDERWORLD_DOMINION_DESCRIPTION.strip()},
    )
    effects = {
        # Effect 1：全军【冥河血誓】— SPY 监听造成伤害
        "hades_grant_styx_blood_oath": EffectConfig(
            effect_id="hades_grant_styx_blood_oath",
            name="施加冥河血誓",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.ALLY_ALL,
            target_count=3,
            state_config_id="styx_blood_oath_state",
            duration_rounds=999,
        ),
        # Effect 2：全军【幽影蔽体】— BEFORE_ACTION 刷新减伤乘区
        "hades_grant_shadow_veil": EffectConfig(
            effect_id="hades_grant_shadow_veil",
            name="施加幽影蔽体",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.ALLY_ALL,
            target_count=3,
            state_config_id="shadow_veil_state",
            duration_rounds=999,
        ),
        # Effect 3：仅哈迪斯【冥祭献统】— BEFORE_ACTION 献祭友军统率并转为自身武力
        "hades_grant_command_drain": EffectConfig(
            effect_id="hades_grant_command_drain",
            name="施加冥祭献统",
            effect_type=EffectType.SPECIAL_STATE_GRANT,
            probability_bps=10000,
            target_policy=TargetPolicy.SELF,
            target_count=1,
            state_config_id="hades_command_drain_state",
            duration_rounds=999,
        ),
    }
    states = {
        # SPY：携带者造成伤害结算后，按伤害 10% 治疗自身伤兵
        "styx_blood_oath_state": StateConfig(
            state_config_id="styx_blood_oath_state",
            name="冥河血誓",
            state_type=StateType.SPECIAL,
            trigger_mode=TriggerMode.SPY,
            listen_event_types=[EventType.DAMAGE_SETTLED],
            duration_rounds=999,
            max_stack=1,
            tags=["styx_blood_oath"],  # SPY 顺序：chain_reaction_config.DAMAGE_SETTLED_SPY 第 1 步
            payload={
                "heal_damage_bps": 1000,
                "skip_heal_modifiers": True,
            },
        ),
        # DAMAGE_REDUCE：行动前按已损失兵力比例写入 damage_reduce_bps
        "shadow_veil_state": StateConfig(
            state_config_id="shadow_veil_state",
            name="幽影蔽体",
            state_type=StateType.DAMAGE_REDUCE,
            trigger_mode=TriggerMode.REGULAR,
            trigger_timings=[Timing.BEFORE_ACTION],
            duration_rounds=999,
            max_stack=1,
            tags=["shadow_veil", "damage_reduce_zone"],
            payload={
                "damage_reduce_bps": 0,
                "max_damage_reduce_bps": 5000,
            },
        ),
        # REGULAR：哈迪斯行动前献祭友军统率，并以两类 ATTR 状态表达结果：
        # - 队友挂“统率削减” state（source=哈迪斯）
        # - 自己挂“献祭武力” state（source=哈迪斯）
        "hades_command_drain_state": StateConfig(
            state_config_id="hades_command_drain_state",
            name="冥祭献统",
            state_type=StateType.SPECIAL,
            trigger_mode=TriggerMode.REGULAR,
            trigger_timings=[Timing.BEFORE_ACTION],
            duration_rounds=999,
            max_stack=1,
            tags=["hades_command_drain"],
            payload={"drain_command_delta": 5},
        ),
        # ATTR(-)：友军被献祭统率；运行时首次献祭时施加并持续累加 command_delta<0
        "hades_command_loss_state": StateConfig(
            state_config_id="hades_command_loss_state",
            name="统率削减",
            state_type=StateType.ATTR,
            trigger_mode=TriggerMode.NONE,
            duration_rounds=999,
            max_stack=1,
            tags=["hades_command_loss", "attr", "attr_decrease"],
            payload={"command_delta": 0},
        ),
        # ATTR(+): 哈迪斯献祭获得的武力；运行时首次献祭时施加并持续累加 force_delta>0
        "hades_force_gain_state": StateConfig(
            state_config_id="hades_force_gain_state",
            name="献祭武力",
            state_type=StateType.ATTR,
            trigger_mode=TriggerMode.NONE,
            duration_rounds=999,
            max_stack=1,
            tags=["hades_force_gain", "attr", "attr_increase"],
            payload={"force_delta": 0},
        ),
    }
    return skill, effects, states


def build_gorgon_gaze_skill() -> tuple[SkillConfig, dict[str, EffectConfig], dict[str, StateConfig]]:
    """构建【戈耳工凝视】配置。

    该函数顶部的 GORGON_GAZE_DESCRIPTION 是战法自然语言说明；
    下方 SkillConfig / EffectConfig / StateConfig 是同一设计的结构化配置。
    """
    skill = SkillConfig(
        skill_id="gorgon_gaze",
        name="戈耳工凝视",
        category=SkillCategory.ACTIVE,
        level=1,
        trigger_timings=[Timing.ACTIVE],
        probability_bps=3500,
        effect_ids=[
            "gorgon_gaze_damage_1",
            "gorgon_gaze_ming_lock_1",
            "gorgon_gaze_damage_2",
            "gorgon_gaze_ming_lock_2",
        ],
        params={
            "description": GORGON_GAZE_DESCRIPTION.strip(),
            "pseudo_random": {
                "bonus_per_fail_bps": 1200,
                "penalty_per_success_bps": 800,
                "min_rate_bps": 1500,
                "max_rate_bps": 7500,
                "guarantee_count": 4,
            },
        },
    )
    effects = {
        "gorgon_gaze_damage_1": EffectConfig(
            effect_id="gorgon_gaze_damage_1",
            name="戈耳工凝视伤害1",
            effect_type=EffectType.DAMAGE,
            probability_bps=10000,
            target_policy=TargetPolicy.RANDOM_ENEMY,
            target_count=1,
            coefficient_bps=10000,
            based_on_attr="intelligence",
            damage_type=DamageType.MAGIC,
            params={"store_targets_as": "gorgon_target_1"},
        ),
        "gorgon_gaze_ming_lock_1": EffectConfig(
            effect_id="gorgon_gaze_ming_lock_1",
            name="戈耳工凝视冥锁1",
            effect_type=EffectType.CONTROL_APPLY,
            probability_bps=4500,
            target_policy=TargetPolicy.SAME_AS_PREVIOUS_EFFECT,
            target_count=1,
            state_config_id="ming_lock_state",
            duration_rounds=1,
            params={
                "target_from_effect_alias": "gorgon_target_1",
                "pseudo_random": {
                    "bonus_per_fail_bps": 700,
                    "penalty_per_success_bps": 1200,
                    "min_rate_bps": 1000,
                    "max_rate_bps": 6500,
                    "guarantee_count": 6,
                },
            },
        ),
        "gorgon_gaze_damage_2": EffectConfig(
            effect_id="gorgon_gaze_damage_2",
            name="戈耳工凝视伤害2",
            effect_type=EffectType.DAMAGE,
            probability_bps=10000,
            target_policy=TargetPolicy.RANDOM_ENEMY,
            target_count=1,
            coefficient_bps=10000,
            based_on_attr="intelligence",
            damage_type=DamageType.MAGIC,
            params={"store_targets_as": "gorgon_target_2", "exclude_effect_aliases": ["gorgon_target_1"]},
        ),
        "gorgon_gaze_ming_lock_2": EffectConfig(
            effect_id="gorgon_gaze_ming_lock_2",
            name="戈耳工凝视冥锁2",
            effect_type=EffectType.CONTROL_APPLY,
            probability_bps=4500,
            target_policy=TargetPolicy.SAME_AS_PREVIOUS_EFFECT,
            target_count=1,
            state_config_id="ming_lock_state",
            duration_rounds=1,
            params={
                "target_from_effect_alias": "gorgon_target_2",
                "pseudo_random": {
                    "bonus_per_fail_bps": 700,
                    "penalty_per_success_bps": 1200,
                    "min_rate_bps": 1000,
                    "max_rate_bps": 6500,
                    "guarantee_count": 6,
                },
            },
        ),
    }
    states = {
        "ming_lock_state": StateConfig(
            state_config_id="ming_lock_state",
            name="冥锁",
            state_type=StateType.CONTROL,
            trigger_mode=TriggerMode.NONE,
            duration_rounds=1,
            max_stack=1,
            tags=["ming_lock", "control"],
            payload={
                "forbid_basic": True,
                "forbid_active": True,
                "forbid_pursuit": False,
            },
        )
    }
    return skill, effects, states
