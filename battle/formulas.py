from __future__ import annotations

"""伤害/兵力数值公式（Phase 3 标定版，见 docs/mechanics/damage.md，禁止擅改标定值）。

本文件只做纯整数计算，不触碰事件流与 RNG。全部系数为 bps 万分比。

Phase 3 公式（docs/prompts/phase3_battlecomplete.md §二）：
- 兵刃核心 = max(1, 360 + 武力 - 统率)
- 谋略核心 = max(1, 360 + 智力 - (统率+智力)//2)   （½统率+½智力 取整数下取整）
- Damage = round(核心 × 兵力系数 × (1+基础增伤) × (1-减伤) × (1+额外增伤)
           × (1+易伤) × 随机 × 会心/奇谋 × 技能系数) + 固定追加
- 兵力系数 = 0.5 + 0.5×(兵/10000)，精确过 10000→100%/8000→90%/6000→80%/4000→70% 锚点。

舍入约定：乘区连乘保持一次舍入语义——分子连乘后对 10000^8 做一次四舍五入
（`(num + den//2) // den`）。Python 整数无溢出；跨语言迁移约定见
docs/mechanics/determinism.md。
"""

BPS = 10000

BASE_DAMAGE = 360
CORE_DAMAGE_MIN = 1  # 技能系数前的核心项安全截断（Phase 3 任务书）

GLOBAL_TROOPS_BASE = 10000  # TroopCoef 分母：全局基准，与武将自身上限无关（决策 D-05：不截断）
TROOP_COEF_BASE_BPS = 5000
TROOP_COEF_RATE_BPS = 5000

DAMAGE_UP_MAX_BPS = 10000
DAMAGE_REDUCE_MAX_BPS = 8000
VULNERABLE_MAX_BPS = 10000
RANDOM_COEF_MIN_BPS = 9500
RANDOM_COEF_MAX_BPS = 10500
CRIT_DAMAGE_MULTIPLIER_BPS = 15000  # 默认暴击伤害 ×1.5
CRIT_HEAL_MULTIPLIER_BPS = 20000
CRIT_RATE_MAX_BPS = 10000
MIN_DAMAGE = 1

BASE_HEAL_RATIO_BPS = 500        # 基础治疗 = 治疗者 max_troops × 5% × heal_rate
HEAL_ATTR_BASE = 100             # 治疗属性基准：智力 100 = 1.0
HEAL_ATTR_STEP = 10              # 每 10 点智力
HEAL_ATTR_COEF_PER_STEP_BPS = 1000  # 修正 ±10%
HEAL_ATTR_COEF_MIN_BPS = 6000
HEAL_ATTR_COEF_MAX_BPS = 15000
HEAL_UP_MAX_BPS = 10000
HEAL_RECEIVED_UP_MAX_BPS = 10000
HEAL_REDUCE_MAX_BPS = 10000

DEAD_RATIO_BPS = 3000            # 受击瞬间：30% 阵亡 / 70% 伤兵
WOUNDED_TO_DEAD_RATIO_BPS = 3000  # 回合开始：伤兵池 30% 转阵亡

SPEED_FIRST_GUARANTEE_DIFF = 20
_SPEED_FIRST_BREAKPOINTS: tuple[tuple[int, int], ...] = (
    (0, 5000),
    (1, 5500),
    (5, 7000),
    (10, 8000),
    (20, 10000),
)

DEFAULT_HIT_POINTS_BPS = 5000
MAX_HIT_POINTS_DECAY_BPS = 3000


def clamp(value: int, low: int, high: int) -> int:
    return max(low, min(value, high))


def calc_core_physical(attack_force: int, target_command: int) -> int:
    """兵刃核心项 = max(1, 360 + 武力 - 统率)。"""
    return max(CORE_DAMAGE_MIN, BASE_DAMAGE + attack_force - target_command)


def calc_core_magic(attack_intelligence: int, target_command: int, target_intelligence: int) -> int:
    """谋略核心项 = max(1, 360 + 智力 - ½统率 - ½智力)；半值合并后整数下取整。"""
    return max(
        CORE_DAMAGE_MIN,
        BASE_DAMAGE + attack_intelligence - (target_command + target_intelligence) // 2,
    )


def calc_troop_coef_bps(current_troops: int) -> int:
    """TroopCoef = 0.5 + 0.5 × (current / 10000)，不截断（超编 NPC 系数 >1 是设计意图）。
    锚点：10000→100%、8000→90%、6000→80%、4000→70%。"""
    current = max(0, current_troops)
    rate_bps = current * BPS // GLOBAL_TROOPS_BASE
    return TROOP_COEF_BASE_BPS + TROOP_COEF_RATE_BPS * rate_bps // BPS


def calc_damage(
    *,
    core_damage: int,
    attacker_current_troops: int,
    target_current_troops: int,
    skill_rate_bps: int,
    damage_up_bps: int = 0,
    damage_reduce_bps: int = 0,
    extra_damage_up_bps: int = 0,
    vulnerable_bps: int = 0,
    random_coef_bps: int = BPS,
    crit_multiplier_bps: int = BPS,
    fixed_extra_damage: int = 0,
    ignore_troop_coef: bool = False,
) -> int:
    """伤害主公式（Phase 3）。返回最终伤害：≥1、≤目标当前兵力；目标已无兵返回 0。

    Damage = round(核心 × TroopCoef × (1+基础增伤) × (1-减伤) × (1+额外增伤)
             × (1+易伤) × 随机 × 会心/奇谋 × skill_rate) + 固定追加

    核心项由 calc_core_physical / calc_core_magic 预先计算（已做 min=1 截断）。
    额外增伤（extra_damage_up_bps）为独立乘区：主动/追击战法单独加成、兵种、
    武将特殊加成等来源（Phase 3 §二，预留扩展）。
    """
    if target_current_troops <= 0:
        return 0

    core = max(CORE_DAMAGE_MIN, core_damage)
    troop_coef_bps = BPS if ignore_troop_coef else calc_troop_coef_bps(attacker_current_troops)
    damage_up = clamp(damage_up_bps, 0, DAMAGE_UP_MAX_BPS)
    damage_reduce = clamp(damage_reduce_bps, 0, DAMAGE_REDUCE_MAX_BPS)
    extra_up = clamp(extra_damage_up_bps, 0, DAMAGE_UP_MAX_BPS)
    vulnerable = clamp(vulnerable_bps, 0, VULNERABLE_MAX_BPS)
    random_coef = clamp(random_coef_bps, RANDOM_COEF_MIN_BPS, RANDOM_COEF_MAX_BPS)
    crit = max(0, crit_multiplier_bps)

    multiplier_num = (
        troop_coef_bps
        * (BPS + damage_up)
        * (BPS - damage_reduce)
        * (BPS + extra_up)
        * (BPS + vulnerable)
        * random_coef
        * crit
        * skill_rate_bps
    )
    multiplier_den = BPS**8
    damage = (core * multiplier_num + multiplier_den // 2) // multiplier_den
    damage += fixed_extra_damage
    return min(max(MIN_DAMAGE, damage), target_current_troops)


def split_damage(actual_damage: int, *, dead_ratio_bps: int = DEAD_RATIO_BPS) -> tuple[int, int]:
    """受击伤害拆分 (dead, wounded)：dead = floor(damage × 30%)，其余进伤兵池。"""
    ratio = clamp(dead_ratio_bps, 0, BPS)
    dead = actual_damage * ratio // BPS
    return dead, actual_damage - dead


def wounded_decay(wounded_troop: int, *, ratio_bps: int = WOUNDED_TO_DEAD_RATIO_BPS) -> int:
    """回合开始伤兵自然损耗：返回本次转为阵亡的数量（伤兵池 × 30% 向下取整）。"""
    if wounded_troop <= 0:
        return 0
    return wounded_troop * clamp(ratio_bps, 0, BPS) // BPS


def calc_speed_first_probability_bps(speed_diff: int) -> int:
    """速度差 → 先手概率（正差方视角）。锚点 0→50%、1→55%、5→70%、10→80%、≥20→100%。"""
    if speed_diff >= SPEED_FIRST_GUARANTEE_DIFF:
        return 10000
    if speed_diff <= -SPEED_FIRST_GUARANTEE_DIFF:
        return 0
    if speed_diff == 0:
        return 5000
    positive = speed_diff > 0
    magnitude = min(abs(speed_diff), SPEED_FIRST_GUARANTEE_DIFF)
    prob = _SPEED_FIRST_BREAKPOINTS[-1][1]
    for (left_diff, left_prob), (right_diff, right_prob) in zip(
        _SPEED_FIRST_BREAKPOINTS, _SPEED_FIRST_BREAKPOINTS[1:]
    ):
        if left_diff <= magnitude <= right_diff:
            span = right_diff - left_diff
            weight = magnitude - left_diff
            prob = left_prob + (right_prob - left_prob) * weight // span
            break
    return prob if positive else 10000 - prob


def calc_hit_points_bps(*, initial_hit_points_bps: int, max_troops: int, current_troops: int) -> int:
    """受击点数 = 初始点数 - 损失兵力比例 × 3000（每次从初始值重算，非累扣）。"""
    if max_troops <= 0:
        return 0
    lost = max(0, max_troops - current_troops)
    offset = lost * MAX_HIT_POINTS_DECAY_BPS // max_troops
    return max(0, initial_hit_points_bps - offset)


def calc_heal_attr_coef_bps(heal_attr: int) -> int:
    """治疗属性系数：智力与 100 比较，每差 10 点 ±10%，clamp [0.6, 1.5]。"""
    delta_bps = (heal_attr - HEAL_ATTR_BASE) * HEAL_ATTR_COEF_PER_STEP_BPS // HEAL_ATTR_STEP
    return clamp(BPS + delta_bps, HEAL_ATTR_COEF_MIN_BPS, HEAL_ATTR_COEF_MAX_BPS)


def calc_heal(
    *,
    healer_max_troops: int,
    heal_attr: int,
    heal_rate_bps: int,
    heal_up_bps: int = 0,
    heal_received_up_bps: int = 0,
    heal_reduce_bps: int = 0,
    random_coef_bps: int = BPS,
    crit_multiplier_bps: int = BPS,
    fixed_extra_heal: int = 0,
) -> int:
    """治疗主公式（理论量，不落池）。

    Heal = round(治疗者max_troops × 5% × heal_rate × HealAttrCoef × 治疗提升
           × 受疗提升 × 治疗降低 × 随机 × 暴击) + 固定追加
    与旧 core 一致：整段一次四舍五入，避免逐步 floor 使治疗长期偏低。
    """
    random_coef = clamp(random_coef_bps, RANDOM_COEF_MIN_BPS, RANDOM_COEF_MAX_BPS)
    crit = max(0, crit_multiplier_bps)
    base_heal_num = healer_max_troops * BASE_HEAL_RATIO_BPS * heal_rate_bps
    base_heal_den = BPS * BPS
    multiplier_num = (
        calc_heal_attr_coef_bps(heal_attr)
        * (BPS + clamp(heal_up_bps, 0, HEAL_UP_MAX_BPS))
        * (BPS + clamp(heal_received_up_bps, 0, HEAL_RECEIVED_UP_MAX_BPS))
        * (BPS - clamp(heal_reduce_bps, 0, HEAL_REDUCE_MAX_BPS))
        * random_coef
        * crit
    )
    multiplier_den = BPS**6
    heal = (base_heal_num * multiplier_num + base_heal_den * multiplier_den // 2) // (
        base_heal_den * multiplier_den
    )
    return max(0, heal + fixed_extra_heal)


def apply_heal_modifiers(
    base_heal: int,
    *,
    heal_up_bps: int = 0,
    heal_received_up_bps: int = 0,
    heal_reduce_bps: int = 0,
    random_coef_bps: int = BPS,
    crit_multiplier_bps: int = BPS,
) -> int:
    """固定基数治疗的乘区链（蛇杖庇护「1% 上限 + 1×智力」等基数不走 calc_heal 主公式，
    但增减疗/随机/暴击乘区与主公式同规）。整段一次四舍五入。"""
    random_coef = clamp(random_coef_bps, RANDOM_COEF_MIN_BPS, RANDOM_COEF_MAX_BPS)
    crit = max(0, crit_multiplier_bps)
    multiplier_num = (
        (BPS + clamp(heal_up_bps, 0, HEAL_UP_MAX_BPS))
        * (BPS + clamp(heal_received_up_bps, 0, HEAL_RECEIVED_UP_MAX_BPS))
        * (BPS - clamp(heal_reduce_bps, 0, HEAL_REDUCE_MAX_BPS))
        * random_coef
        * crit
    )
    multiplier_den = BPS**5
    return max(0, (base_heal * multiplier_num + multiplier_den // 2) // multiplier_den)


def constrain_heal(heal: int, *, wounded_troop: int, max_troops: int, current_troops: int) -> int:
    """实际治疗量 = min(理论量, 伤兵池, 缺兵量)。只回伤兵、不复活、不超上限。"""
    missing = max(0, max_troops - current_troops)
    return min(max(0, heal), max(0, wounded_troop), missing)
