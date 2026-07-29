from __future__ import annotations

"""状态系统：一等公民建模（任务书 4.4/5.3）。

- StatusDef：状态的静态定义（类别、叠加/刷新规则、持续、数值修正、DoT/HoT）。
- StatusInstance：运行时实例（来源、层数、剩余持续、实例 id）。
- 默认规则：负面状态不可刷新、不可叠加（任务书 5.3）；正面/特殊按各自定义。
- 持续计数：目标自己的行动窗口开始时计次（「持续 1 回合」= 至少覆盖目标下一次
  行动窗口），与旧 core BEFORE_ACTION 语义一致。DoT/HoT 在回合开始相位 tick。
- 战时状态不跨局：每局开始整体清空（game_end 语义覆盖，不逐条发事件）。

数值修正键（modifiers，全部可选，聚合方式=同键求和×层数）：
  force_delta / intelligence_delta / command_delta / speed_delta   四维平加
  force_bps / intelligence_bps / command_bps / speed_bps           四维百分比
  damage_up_bps / damage_reduce_bps / vulnerable_bps               伤害乘区
  physical_damage_up_bps / magic_damage_up_bps                     类型专属增伤
  physical_damage_reduce_bps / magic_damage_reduce_bps             类型专属减伤
  physical_vulnerable_bps / magic_vulnerable_bps                   类型专属易伤
  crit_rate_bps / heal_crit_rate_bps                               暴击率
  physical_crit_rate_bps / magic_crit_rate_bps / true_crit_rate_bps 类型专属暴击率
  heal_up_bps / heal_received_up_bps / heal_reduce_bps             治疗乘区
  lifesteal_bps / physical_lifesteal_bps                           吸血（造成伤害转自疗）
  combo_rate_bps                                                    连击率（≥100% 普攻两次）
  evade_bps                                                         闪避率（Phase 3：伤害前置 roll，0 结算）
  block_rate_bps                                                    几率型格挡（次数型走 counters.block_charges）
  reflect_rate_bps                                                  反弹率（受伤归零并将本应受伤害反弹给攻击者，特殊伤害不连锁）
  extra_damage_up_bps                                               额外增伤独立乘区（Phase 3 §二）
  crit_damage_up_bps                                                会心/奇谋伤害加成（在 ×1.5 基础上加）
  burst_rate_up_bps                                                 连发率加成（Phase 4，作用于持有者全部主动战法）
  hit_weight_up_bps                                                 受击权重偏置（Phase 4 集火战术：受击点数×(1+bias)，仍加权随机非锁定）
  forbid_basic / forbid_active / forbid_pursuit（bool，不乘层数）  控制禁制
  charm_targeting（bool）                                           魅惑：选敌初步备选池改为除自身外全体；技能内部规则仍在池上执行
  control_immune（bool）                                            清醒：免疫硬控（CONTROL 施加静默拒绝）
  注：数值修正键允许负值（如恐惧 damage_up_bps=-1500 表示造成伤害 -15%）

响应钩子（B3，事件驱动状态）：
  on_damage_dealt(engine, status, ctx)   持有者造成伤害结算后（雷霆/血誓/三叉戟…）
  on_damage_taken(engine, status, ctx)   持有者受到伤害结算后（蛇杖/试炼/凝视…）
  on_action_start(engine, status, action_seq)  持有者行动窗口开始（幽影蔽体刷新/
                                          冥祭献统/赫尔墨斯扰心标记…）
  伤害结算点：先守方 on_damage_taken 整段，再攻方 on_damage_dealt 整段；
  各段内他人施加优先于自身施加，再 (response_priority, instance_id)
  （determinism.md §2）。优先级登记见 docs/mechanics/effects.md。
"""

from dataclasses import dataclass, field
from typing import Any, Callable

BUFF = "buff"
DEBUFF = "debuff"
CONTROL = "control"
SPECIAL = "special"

ATTR_FLAT_KEYS = ("force_delta", "intelligence_delta", "command_delta", "speed_delta")
ATTR_BPS_KEYS = ("force_bps", "intelligence_bps", "command_bps", "speed_bps")
FORBID_KEYS = ("forbid_basic", "forbid_active", "forbid_pursuit")

PERMANENT = -1  # duration_rounds=-1：整局有效

# 状态触发的播放语义标签（定义期声明 → 战报头 status_catalog → 客户端编译层）：
#   simultaneous  与持有者无关的齐发型触发（雷霆落雷：目标头顶落雷，施法者不动）。
#                 同一因果批次内**跨持有者**也并成一个播放单元。
#   sequential    必须逐次单独成单元（圣盾反制、代战借刀：演出是持有者突进，
#                 并组会让一个人替所有人挥刀）。
# 都不声明＝默认：同批次**同持有者**的多次触发并成一发，跨持有者不并。
SIMULTANEOUS = "simultaneous"
SEQUENTIAL = "sequential"
PLAYBACK_TAGS = frozenset({SIMULTANEOUS, SEQUENTIAL})

# 定义期自注册表（StatusDef.__post_init__ 写入）：status_id → StatusDef。
# 只服务 status_catalog 导出，禁止结算侧依赖（结算一律走实例 definition）。
STATUS_DEFS: dict[str, "StatusDef"] = {}


@dataclass(frozen=True, slots=True)
class StatusDef:
    status_id: str
    kind: str  # buff / debuff / control / special
    duration_rounds: int = 1  # -1=整局
    max_stacks: int = 1
    refreshable: bool | None = None  # None=按默认规则（负面否，正面/特殊是）
    modifiers: dict[str, Any] = field(default_factory=dict)
    # DoT/HoT：每回合开始 tick 一次。rate_bps 为对应主公式的技能系数。
    # 实际系数 = rate_bps × stacks；dot_can_crit 默认 False（冥火等可显式打开）。
    dot_rate_bps: int = 0   # >0 = 每回合按来源谋略结算一次魔法伤害（中毒/燃烧）
    hot_rate_bps: int = 0   # >0 = 每回合按来源结算一次治疗
    dot_can_crit: bool = False
    # ---- B3：事件驱动响应（见文件头说明） ----
    response_priority: int = 100
    on_apply: Callable | None = None         # 新实例创建后（刷新/叠层不触发）
    on_damage_dealt: Callable | None = None
    on_damage_taken: Callable | None = None
    on_action_start: Callable | None = None
    # ---- Phase 3 新钩子 ----
    on_round_start: Callable | None = None   # 回合开始（木马奇谋/胜利羽翼/疾走…）
    on_round_end: Callable | None = None     # 回合结束（冬春轮转/蛇杖收尾治疗…）
    on_hero_defeated: Callable | None = None # 任意武将阵亡后（渡魂船费/胜利羽翼击杀）
    on_control_taken: Callable | None = None # 持有者被施加控制后（圣盾反制控制）
    on_ally_basic_attack: Callable | None = None  # 队友普攻结算后（Phase 4 协击：
                                                  # 卡斯托耳双子协战；ctx 含 attacker/
                                                  # target/strike_no/damage_seq）
    on_status_inflicted: Callable | None = None   # 任意状态成功施加/刷新后（Phase 4
                                                  # 死亡凝望盯诅咒；ctx 含 source/target/
                                                  # status_id/is_refresh/apply_seq）
    on_pre_damage_dealt: Callable | None = None  # 持有者造成伤害结算前（觅踵/死亡凝望/
                                                 # 致命一矢/十二试炼…可改写 ctx 的增伤/
                                                 # 必暴/rate_bonus_bps）
    mitigation_gate: Callable | None = None  # (engine, status) -> bool：本实例的减免能力
                                             # （evade/block/reflect）是否生效（圣盾受
                                             # 匠心旁骛压制时返回 False）；None=恒生效
    on_reflect: Callable | None = None       # 伤害反弹已选定 bounce、即将 deal 反伤前：
                                             # (engine, status, ctx) -> int|None
                                             # ctx=reflected_amount/bounce/tick_seq/
                                             # damage_seq/damage_type；返回值覆盖反伤
                                             # parent_seq（雅典娜高光等）；None=沿用 tick_seq
    # ---- 施加扩展点（2026-07-20 注册表化，替代 engine 内按 status_id 的特例） ----
    immune_when_forbid: str | None = None    # 目标持有该 forbid 键时本状态静默拒绝
                                             # （石化→"petrify_immune" 珀尔修斯镜盾）
    on_applied_to_other: Callable | None = None  # 对他人施加/刷新成功后回调
                                                 # (engine, source, target, parent_seq)
                                                 # （美杜莎孤怨照影；对自己施加不回调防递归）
    payload: dict[str, Any] = field(default_factory=dict)  # 响应处理器的参数
    # ---- 播放标签（schema 1.5.2 status_catalog；定义期声明，客户端编译层直读）----
    # 本状态的触发（status_tick）在客户端如何组播放单元，取值见 PLAYBACK_TAGS。
    # 不声明＝默认：同一因果批次内、同一持有者的多次触发并成一个播放单元。
    playback_tags: tuple[str, ...] = ()

    def __post_init__(self) -> None:
        for tag in self.playback_tags:
            if tag not in PLAYBACK_TAGS:
                raise ValueError(
                    f"{self.status_id}: playback_tags 非法 {tag!r}，"
                    f"必须取自 {sorted(PLAYBACK_TAGS)}")
        # 定义期自注册：status_catalog 从此表导出（同 id 重复定义以首个为准）
        STATUS_DEFS.setdefault(self.status_id, self)

    def is_negative(self) -> bool:
        return self.kind in (DEBUFF, CONTROL)

    def allows_refresh(self) -> bool:
        if self.refreshable is not None:
            return self.refreshable
        return not self.is_negative()  # 默认：负面不可刷新不可叠加

    def allows_stack(self) -> bool:
        return self.max_stacks > 1


@dataclass(slots=True)
class StatusInstance:
    instance_id: int
    definition: StatusDef
    owner_id: str
    source_id: str          # 施加来源武将（来源阵亡时清理）
    stacks: int = 1
    action_tick_count: int = 0  # 目标行动窗口计次
    counters: dict[str, int] = field(default_factory=dict)        # 局内计数（试炼次数…）
    round_counters: dict[str, int] = field(default_factory=dict)  # 回合计数（每回合开始清零）
    dynamic_modifiers: dict[str, int] = field(default_factory=dict)  # 运行时修正（幽影蔽体…），不乘层数

    @property
    def status_id(self) -> str:
        return self.definition.status_id

    def remaining_rounds(self) -> int:
        if self.definition.duration_rounds == PERMANENT:
            return PERMANENT
        return max(0, self.definition.duration_rounds - self.action_tick_count)

    def ref(self) -> dict[str, Any]:
        """契约 StatusRef 结构。"""
        return {
            "instance_id": self.instance_id,
            "status_id": self.status_id,
            "owner_id": self.owner_id,
        }


def sum_modifier(statuses: list[StatusInstance], key: str) -> int:
    """聚合某数值修正键：静态修正同键求和×层数；动态修正（dynamic_modifiers）不乘层数。"""
    total = 0
    for status in statuses:
        value = status.definition.modifiers.get(key, 0)
        if value:
            total += value * status.stacks
        dynamic = status.dynamic_modifiers.get(key, 0)
        if dynamic:
            total += dynamic
    return total


def instance_modifier(status: StatusInstance, key: str) -> int:
    """单个实例的某数值修正键：静态修正×层数 + 动态修正（减免逐实例判定用）。"""
    return (
        status.definition.modifiers.get(key, 0) * status.stacks
        + status.dynamic_modifiers.get(key, 0)
    )


def any_forbid(statuses: list[StatusInstance], key: str) -> bool:
    return any(status.definition.modifiers.get(key, False) for status in statuses)


def has_first_strike(statuses: list[StatusInstance]) -> bool:
    """先攻是否仍可用于本回合行动序。

    持续 N 回合的先攻只覆盖持有者接下来的 N 次行动窗；排序在行动窗开始计次
    **之前**读取，故以 `action_tick_count < duration` 判定「尚未消费完」——
    避免 duration=1 在第一次行动后仍带着 tick=1 多吃下一回合排序（神使戏言/
    神使印记常见坑）。
    """
    for status in statuses:
        if not status.definition.modifiers.get("first_strike"):
            continue
        dur = status.definition.duration_rounds
        if dur == PERMANENT:
            return True
        if status.action_tick_count < dur:
            return True
    return False


# =============================================================================
# 标准控制/特殊状态（任务书 5.3 状态清单）。builder 形式：施加方可配持续/参数。
# 同 status_id 的多个 def 视为同一状态（存在性判断按 status_id）。
# =============================================================================

def silence(duration_rounds: int = 1) -> StatusDef:
    """缄默：禁主动战法；施加时若目标准备中则额外产生打断（引擎 apply_status 联动）。"""
    return StatusDef(
        status_id="silence", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_active": True},
    )


def disarm(duration_rounds: int = 1) -> StatusDef:
    """缴械：禁普攻（追击挂普攻命中后，自然无追击）。"""
    return StatusDef(
        status_id="disarm", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_basic": True},
    )


def ming_lock(duration_rounds: int = 1) -> StatusDef:
    """冥锁：禁主动 + 普攻。"""
    return StatusDef(
        status_id="ming_lock", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_basic": True, "forbid_active": True},
    )


def petrify(duration_rounds: int = 1) -> StatusDef:
    """石化：禁主动 + 普攻，受到伤害 +10%（决策 D-01：入易伤乘区，加法叠加）。
    免疫键与施加回调走 StatusDef 扩展点（镜盾免疫 / 美杜莎孤怨照影）。"""
    return StatusDef(
        status_id="petrify", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_basic": True, "forbid_active": True, "vulnerable_bps": 1000},
        immune_when_forbid="petrify_immune",
        on_applied_to_other=lambda engine, source, target, parent_seq:
            engine.notify_petrify_out(source, parent_seq),
    )


def freeze(duration_rounds: int = 1) -> StatusDef:
    """冰锢（卡吕普索）：禁主动 + 普攻，无石化易伤；清醒可免。"""
    return StatusDef(
        status_id="freeze", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_basic": True, "forbid_active": True},
    )


def underworld_burn(duration_rounds: int = 2) -> StatusDef:
    """冥火（赫卡忒）：可叠最多 3 层、可刷新；每层 60% 谋略 DoT（可暴击）。"""
    return StatusDef(
        status_id="underworld_burn", kind=DEBUFF, duration_rounds=duration_rounds,
        max_stacks=3, refreshable=True,
        dot_rate_bps=6000, dot_can_crit=True,
    )


def block(duration_rounds: int = PERMANENT) -> StatusDef:
    """格挡（次数型载体，Phase 3）：instance.counters["block_charges"] 记次数，
    受到伤害时消耗 1 次并将伤害置 0（0 结算事件化 mitigation=block）。
    默认整局有效（次数耗尽即失效但保留图标语义由客户端处理）；可刷新叠计数。"""
    return StatusDef(
        status_id="block", kind=BUFF, duration_rounds=duration_rounds,
        refreshable=True,
        payload={"remove_when_exhausted": True},
    )


def charm(duration_rounds: int = 1) -> StatusDef:
    """魅惑（Phase 3 塞壬）：选敌初步备选池改为除自身外全体存活（敌我不分）；
    互斥/指名/受击率等技能规则仍在该池上执行。随机选人在池内等概率。"""
    return StatusDef(
        status_id="charm", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"charm_targeting": True},
    )


def fear(duration_rounds: int = 1) -> StatusDef:
    """恐惧（Phase 4 刻耳柏洛斯三首噬咬；口径为临时定案，见 phase4_manual_tasks §一）：
    硬控轻量版——禁普攻+禁追击，且持有者造成伤害 -15%（damage_up 负值入增伤乘区）。"""
    return StatusDef(
        status_id="fear", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_basic": True, "forbid_pursuit": True, "damage_up_bps": -1500},
    )


def curse(duration_rounds: int = 2) -> StatusDef:
    """诅咒（Phase 4 卡戎摆渡）：智力 -20、受到伤害 +10%。负面例外**可刷新**
    （任务书：同一施放者不能叠加只能刷新持续；全局单实例、任意来源刷新为
    简化口径，A3 校准时若需按来源分实例再扩）。"""
    return StatusDef(
        status_id="curse", kind=DEBUFF, duration_rounds=duration_rounds,
        refreshable=True,
        modifiers={"intelligence_delta": -20, "vulnerable_bps": 1000},
    )


def certain_crit() -> StatusDef:
    """必胜（Phase 4 尼刻胜利羽翼族共用）：counters["forced_crit_charges"] 记次数，
    下一次造成伤害或治疗时必定暴击并消耗 1 次（引擎 _consume_forced_crit 联动，
    最早实例先消耗）。整局有效，可刷新叠计数。"""
    return StatusDef(
        status_id="certain_crit", kind=BUFF, duration_rounds=PERMANENT,
        refreshable=True,
        payload={"remove_when_exhausted": True},
    )


def clear_mind(duration_rounds: int) -> StatusDef:
    """清醒（Phase 4 伊阿宋英雄远征）：免疫各类硬控（kind=CONTROL 的状态施加
    一律静默拒绝，引擎 apply_status 联动）。犹豫为 SPECIAL，不在免疫范围。"""
    return StatusDef(
        status_id="clear_mind", kind=SPECIAL, duration_rounds=duration_rounds,
        modifiers={"control_immune": True},
    )


def hesitation(delay_rate_bps: int = 5000, duration_rounds: int = 2) -> StatusDef:
    """犹豫（特殊，Phase 3 修订）：行动时按 delay_rate 判定是否延后，
    延后固定 1 回合（N → N+1 回合窗口最前释放）；重复施加为**刷新不叠层**，
    已登记的延迟行动不受刷新影响。计次与其他状态统一：行动窗口开始时计次
    （Phase 3 §二——本回合开始即到期时，寄存的延迟行动仍照常释放，
    仅新行动不再进入犹豫判定）。
    多来源刷新时以**首个实例**的 delay_rate 为准（instance 绑定首个 def）。"""
    return StatusDef(
        status_id="hesitation", kind=SPECIAL, duration_rounds=duration_rounds,
        refreshable=True,
        payload={"delay_rate_bps": delay_rate_bps},
    )
