from __future__ import annotations

"""战法架构基座：战法 = 类 + 注册（任务书 4.4）。

- 每个战法是一个 `Skill` 子类实例，注册进全局 REGISTRY（skill_id → 实例，无状态）。
- 引擎在行动窗口按**装配顺序**（HeroSetup.skills 下标）逐个做触发判定；
  判定用伪随机补偿（battle/pseudo_random.py），key=(actor_id, skill_id) 一局内真累计。
- 战法只通过引擎暴露的效果原语（deal_damage / heal / apply_status / remove_status /
  modify_attr）产生作用，自身不触碰兵力池与事件流细节。

触发时机（timing，任务书 4.4 事件驱动 + 优先级）：
  active   行动窗口主动（可配 prepare_rounds>0 → 准备型：prepare→release 两段协议）
  prepare  准备回合（r=0）施放：神谕与被动入场战法（is_oracle=True 触发副将连携）
  pursuit  己方普攻命中后（引擎在每次普攻 damage 结算后按装配顺序分发）
  持续型被动效果（雷霆/血誓/试炼…）由 prepare 施放的状态经响应钩子表达
  （statuses.StatusDef.on_damage_dealt / on_damage_taken / on_action_start）。

- 本文件含基座与 **测试用战法**（test_ 前缀，覆盖效果原语/暴击乘区/连击/追击，
  验证架构表达力）；正式标杆战法（skill_files.py 清单 + 阿喀琉斯）在
  battle/standard_skills.py。
"""

from dataclasses import dataclass
from typing import TYPE_CHECKING

from battle.pseudo_random import PLAIN, PseudoRandomParams
from battle.statuses import BUFF, CONTROL, DEBUFF, StatusDef

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState

TIMING_ACTIVE = "active"
TIMING_PREPARE = "prepare"
TIMING_PURSUIT = "pursuit"

# 战法伤害标签取值（定义期声明，进战报 skill_catalog，客户端拿来即用）：
#   physical / magic  本战法（含其挂出的状态钩子）产生的伤害类型
#   mixed             同一战法会打出两种类型（代战借手、海神潮涌回响等）
#   none              纯增益/治疗/控制，不产生伤害
DAMAGE_TYPE_PHYSICAL = "physical"
DAMAGE_TYPE_MAGIC = "magic"
DAMAGE_TYPE_MIXED = "mixed"
DAMAGE_TYPE_NONE = "none"
_DAMAGE_TYPES = {DAMAGE_TYPE_PHYSICAL, DAMAGE_TYPE_MAGIC,
                 DAMAGE_TYPE_MIXED, DAMAGE_TYPE_NONE}


@dataclass(frozen=True, slots=True)
class Skill:
    """战法基类。子类实现 select_targets / execute；execute 即战法的结算载荷
    （准备型战法的 prepare 登记由引擎负责，execute 只写 release 段效果）。

    【标签义务（2026-07-27 起）】新战法注册时必须声明 `damage_type`：
    会造成伤害（含状态钩子产生的归因伤害）写 physical/magic/mixed，
    纯增益/治疗/控制写 "none"。标签随战报头 `skill_catalog` 下发，
    客户端播放层直读、不再逐事件推断（docs/schema/battle_events.md §skill_catalog）。
    `tags` 为自由标签位（加法演进，客户端未知标签必须忽略）。"""

    skill_id: str
    trigger_rate_bps: int = 10000
    pseudo_random: PseudoRandomParams = PLAIN
    hint_intensity: str | None = None
    timing: str = TIMING_ACTIVE
    is_oracle: bool = False    # 神谕：主将准备回合释放后触发副将连携（D-04）
    prepare_rounds: int = 0    # >0 = 准备型主动：触发时进入准备，N 回合后释放
    # ---- Phase 4：连发（仅 active 有效）----
    # >0 = 成功释放后按此概率 roll 连发（走伪随机补偿，key=(actor, skill, "burst")），
    # 可连续；同一行动窗内该战法总释放次数硬上限 7（首发 + 至多 6 次连发）。
    # 准备型连发不重新准备（释放段直接连发）。
    burst_rate_bps: int = 0
    burst_pseudo_random: PseudoRandomParams = PLAIN
    # ---- 播放标签（schema 1.5.0 skill_catalog）----
    damage_type: str = DAMAGE_TYPE_NONE
    tags: tuple[str, ...] = ()

    @property
    def category(self) -> str:
        """播放分类（由既有字段推导，禁止另立字段造成双真源）：
        oracle / passive（prepare 时机非神谕）/ pursuit /
        prepare_active（准备型主动）/ active。"""
        if self.timing == TIMING_PREPARE:
            return "oracle" if self.is_oracle else "passive"
        if self.timing == TIMING_PURSUIT:
            return "pursuit"
        return "prepare_active" if self.prepare_rounds > 0 else "active"

    def trigger_rate_for(self, engine: "SeriesEngine", actor: "HeroState") -> int:
        """有效触发率（Phase 3：海嗣号角等每次释放递减的动态率可覆盖）。"""
        return self.trigger_rate_bps

    def select_targets(self, engine: "SeriesEngine", actor: "HeroState") -> list["HeroState"]:
        """宣告目标（写进 skill_trigger.target_ids）。默认无目标。"""
        return []

    def execute(
        self,
        engine: "SeriesEngine",
        actor: "HeroState",
        targets: list["HeroState"],
        trigger_seq: int,
    ) -> None:
        """触发判定通过后调用。trigger_seq 为 skill_trigger 组根事件的 seq。"""
        raise NotImplementedError


REGISTRY: dict[str, Skill] = {}


def register(skill: Skill) -> Skill:
    if skill.skill_id in REGISTRY:
        raise ValueError(f"skill_id 重复注册: {skill.skill_id}")
    if skill.damage_type not in _DAMAGE_TYPES:
        raise ValueError(
            f"{skill.skill_id}: damage_type 非法 {skill.damage_type!r}，"
            f"必须是 {sorted(_DAMAGE_TYPES)} 之一（纯增益/治疗写 'none'）")
    REGISTRY[skill.skill_id] = skill
    return skill


# =============================================================================
# B2 测试用战法（test_ 前缀；正式战法 Step B3 落位）
# =============================================================================

@dataclass(frozen=True, slots=True)
class _TestBlast(Skill):
    """瞬发谋略伤害：随机敌方单体，300% 魔法伤害。验证伤害原语 + 暴击乘区。"""

    rate_bps: int = 30000

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="magic", rate_bps=self.rate_bps,
                    parent_seq=trigger_seq,
                )


@dataclass(frozen=True, slots=True)
class _TestMend(Skill):
    """治疗：己方兵力比例最低者，150% 治疗。验证治疗原语。"""

    rate_bps: int = 15000

    def select_targets(self, engine, actor):
        return [engine.select_ally_lowest_troops(actor)]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.heal(actor, target, rate_bps=self.rate_bps, parent_seq=trigger_seq)


TEST_POISON_STATUS = StatusDef(
    status_id="test_poison_status",
    kind=DEBUFF,
    duration_rounds=2,
    dot_rate_bps=5000,  # 每回合开始按来源谋略结算 50% 魔法伤害
)


@dataclass(frozen=True, slots=True)
class _TestPoison(Skill):
    """施毒：随机敌方单体挂 2 回合 DoT。验证施加状态 + DoT tick + 来源阵亡清理。"""

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.apply_status(actor, target, TEST_POISON_STATUS, parent_seq=trigger_seq)


TEST_WAR_CRY_STATUS = StatusDef(
    status_id="test_war_cry_status",
    kind=BUFF,
    duration_rounds=2,
    max_stacks=3,
    modifiers={"damage_up_bps": 2000, "crit_rate_bps": 1500},
)


@dataclass(frozen=True, slots=True)
class _TestWarCry(Skill):
    """战吼：给自己叠增伤+暴击率 buff（可叠 3 层）。验证正面状态叠层与修正聚合。"""

    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, TEST_WAR_CRY_STATUS, parent_seq=trigger_seq)


TEST_DISARM_STATUS = StatusDef(
    status_id="test_disarm_status",
    kind=CONTROL,
    duration_rounds=1,
    modifiers={"forbid_basic": True},
)


@dataclass(frozen=True, slots=True)
class _TestDisarm(Skill):
    """缴械：禁敌方随机单体普攻 1 回合。验证控制状态 + 负面不可刷新。"""

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.apply_status(actor, target, TEST_DISARM_STATUS, parent_seq=trigger_seq)


@dataclass(frozen=True, slots=True)
class _TestSap(Skill):
    """削弱：敌方随机单体统率 -10（本局）。验证属性修改原语与 scope=game 回滚。"""

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.modify_attr(
                    target, [("command", -10)], scope="game", parent_seq=trigger_seq
                )


@dataclass(frozen=True, slots=True)
class _TestPursuit(Skill):
    """追击（B3）：己方普攻命中后 50% 概率对同一目标追加 50% 兵刃伤害。
    验证 pursuit 时机 + 连锁跨组（组根 parent 指回普攻 damage）。"""

    rate_bps: int = 5000

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=self.rate_bps,
                    parent_seq=trigger_seq, kind="pursuit",
                )


TEST_COMBO_STATUS = StatusDef(
    status_id="test_combo_status",
    kind=BUFF,
    duration_rounds=2,
    modifiers={"combo_rate_bps": 10000},  # 100% 连击：普攻必打两次
)


@dataclass(frozen=True, slots=True)
class _TestComboDrill(Skill):
    """疾风连打（B3）：给自己挂 2 回合 100% 连击 buff。验证连击 + 每击独立追击。"""

    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, TEST_COMBO_STATUS, parent_seq=trigger_seq)


@dataclass(frozen=True, slots=True)
class _TestChargedNova(Skill):
    """蓄力新星（B3）：准备型主动（准备 1 回合），释放时对随机敌方单体 400% 魔法伤害。
    验证 prepare/release/interrupted 协议。"""

    rate_bps: int = 40000

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="magic", rate_bps=self.rate_bps,
                    parent_seq=trigger_seq,
                )


@dataclass(frozen=True, slots=True)
class _TestSilence(Skill):
    """缄默：敌方随机单体禁主动 1 回合（可打断准备）。验证缄默×准备中交互。"""

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        from battle.statuses import silence
        for target in targets:
            if target.is_alive():
                engine.apply_status(actor, target, silence(1), parent_seq=trigger_seq)


@dataclass(frozen=True, slots=True)
class _TestHesitate(Skill):
    """扰心：敌方随机单体叠 1 层犹豫（持续 2 回合，50% 延迟率）。验证犹豫系统。"""

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        from battle.statuses import hesitation
        for target in targets:
            if target.is_alive():
                engine.apply_status(actor, target, hesitation(), parent_seq=trigger_seq)


register(_TestBlast(skill_id="test_blast", trigger_rate_bps=5000,
                    pseudo_random=PseudoRandomParams(bonus_per_fail_bps=1200,
                                                     penalty_per_success_bps=800,
                                                     guarantee_fail_count=4),
                    hint_intensity="strong", damage_type="magic"))
register(_TestMend(skill_id="test_mend", trigger_rate_bps=5000))
register(_TestPoison(skill_id="test_poison", trigger_rate_bps=6000,
                     damage_type="magic"))
register(_TestWarCry(skill_id="test_war_cry", trigger_rate_bps=10000))
register(_TestDisarm(skill_id="test_disarm", trigger_rate_bps=4000))
register(_TestSap(skill_id="test_sap", trigger_rate_bps=4000))
register(_TestPursuit(skill_id="test_pursuit", trigger_rate_bps=5000, timing=TIMING_PURSUIT,
                      damage_type="physical"))
register(_TestComboDrill(skill_id="test_combo_drill", trigger_rate_bps=10000))
register(_TestChargedNova(skill_id="test_charged_nova", trigger_rate_bps=5000,
                          prepare_rounds=1, hint_intensity="ultimate",
                          damage_type="magic"))
register(_TestSilence(skill_id="test_silence", trigger_rate_bps=5000))
register(_TestHesitate(skill_id="test_hesitate", trigger_rate_bps=6000))
