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
  crit_damage_up_bps                                                会心/奇谋伤害加成（在 ×2 基础上加）
  forbid_basic / forbid_active / forbid_pursuit（bool，不乘层数）  控制禁制

响应钩子（B3，事件驱动状态）：
  on_damage_dealt(engine, status, ctx)   持有者造成伤害结算后（雷霆/血誓/三叉戟…）
  on_damage_taken(engine, status, ctx)   持有者受到伤害结算后（蛇杖/试炼/凝视…）
  on_action_start(engine, status, action_seq)  持有者行动窗口开始（幽影蔽体刷新/
                                          冥祭献统/赫尔墨斯扰心标记…）
  同一结算点多个状态的响应顺序 = (response_priority, 持有者 hero_order 序, instance_id)，
  全局确定（任务书 4.4），优先级登记见 docs/mechanics/effects.md。
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


@dataclass(frozen=True, slots=True)
class StatusDef:
    status_id: str
    kind: str  # buff / debuff / control / special
    duration_rounds: int = 1  # -1=整局
    max_stacks: int = 1
    refreshable: bool | None = None  # None=按默认规则（负面否，正面/特殊是）
    modifiers: dict[str, Any] = field(default_factory=dict)
    # DoT/HoT：每回合开始 tick 一次。rate_bps 为对应主公式的技能系数。
    dot_rate_bps: int = 0   # >0 = 每回合按来源谋略结算一次魔法伤害（中毒/燃烧）
    hot_rate_bps: int = 0   # >0 = 每回合按来源结算一次治疗
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
    on_pre_damage_dealt: Callable | None = None  # 持有者造成伤害结算前（觅踵/死亡凝望/
                                                 # 致命一矢…可改写 ctx 的增伤/必暴）
    mitigation_gate: Callable | None = None  # (engine, status) -> bool：本实例的减免能力
                                             # （evade/block/reflect）是否生效（圣盾受
                                             # 匠心旁骛压制时返回 False）；None=恒生效
    payload: dict[str, Any] = field(default_factory=dict)  # 响应处理器的参数

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
    """石化：禁主动 + 普攻，受到伤害 +10%（决策 D-01：入易伤乘区，加法叠加）。"""
    return StatusDef(
        status_id="petrify", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"forbid_basic": True, "forbid_active": True, "vulnerable_bps": 1000},
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
    """魅惑（Phase 3 塞壬拆解）：普攻和主动战法选目标敌我不分（引擎选人联动）。"""
    return StatusDef(
        status_id="charm", kind=CONTROL, duration_rounds=duration_rounds,
        modifiers={"charm_targeting": True},
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
