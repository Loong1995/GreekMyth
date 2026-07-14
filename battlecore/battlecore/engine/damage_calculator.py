from __future__ import annotations

from typing import Any

from battlecore.domain.enums import DamageType, StateType
from battlecore.domain.hero import Hero


BPS = 10000
# 万分比基准。
# 为了让 Python 原型未来迁移到 C# / C++ / Rust / Go 时结果稳定，本文件用整数万分比表示小数。
# 例如：1.0 = 10000，0.95 = 9500，1.05 = 10500。

BASE_DAMAGE = 390
# 100% 技能率下 BaseDamage 恒为 BASE_DAMAGE（390），与兵力无关。
# skill_rate（coefficient_bps/10000）作为最终乘区之一，不再缩放 BaseDamage。

BASE_HEAL_RATIO_BPS = 500
# 基础治疗系数，等价于 0.05。
# 当前按“100 智力、100% 治疗系数 = 500 回复量”标定。
# 80 智力时治疗属性系数为 0.8，因此 100% 治疗系数约 400 回复量。
# 调大该值会让治疗更强、战斗更拖，调小会让治疗更弱、战斗更快结束。

MAX_TROOPS = 10000
# 全局兵力基准：TroopCoef 的 rate = current_troops / MAX_TROOPS（与武将进场 max_troops 无关）。
# 不同初始兵力的武将，只要 current 不同，普攻伤害就会不同。

TROOP_COEF_BASE_BPS = 4000
# TroopCoef 公式常数项 0.4。

TROOP_COEF_RATE_BPS = 6000
# TroopCoef 公式系数 0.6；TroopCoef = 0.4 + 0.6 × (current / MAX_TROOPS)，不做上下限截断。

ATTR_DIFF_COEF = 8
# 属性差值固定伤害系数。
# AttrBonus = AttrDiff × ATTR_DIFF_COEF，与 BaseDamage 相加后再乘兵力系数与其余倍率。
# 例：AttrDiff=+50 → 基础层额外 +400；AttrDiff=-20 → 基础层 -160。
# 调大该值会放大武力/智力对伤害的绝对贡献；调小则让技能率与兵力层更主导。

ATTR_DIFF_MAX = 1000
# 正属性差安全上限（仅防极端溢出）。raw < 1000 时正差 1:1 计入，不做属性压制截顶。

ATTR_DIFF_FLOOR = -45
# 负属性差映射下限：raw≤-200 时固定为 -45。

# 负属性差分段锚点 (raw, mapped)。raw≥-30 时 1:1；更负时按锚点线性插值，堆统率边际递减。
_ATTR_DIFF_NEG_ANCHORS: tuple[tuple[int, int], ...] = (
    (-30, -30),
    (-40, -33),
    (-50, -36),
    (-100, -41),
    (-200, -45),
)

DAMAGE_UP_MAX_BPS = 10000
DAMAGE_REDUCE_MAX_BPS = 8000
VULNERABLE_MAX_BPS = 10000
# 增伤、减伤、易伤上限。
# 造成伤害提升最多 +100%，受到伤害降低最多 80%，不管什么类型的伤害易伤最多 +100%。
# clamp 的目的，是防止多个 buff 叠加后让伤害爆炸或接近 0。

HEAL_ATTR_BASE = 100
HEAL_ATTR_STEP = 10
HEAL_ATTR_COEF_PER_STEP_BPS = 1000
HEAL_ATTR_COEF_MIN_BPS = 6000
HEAL_ATTR_COEF_MAX_BPS = 15000
# 治疗属性修正。
# 当前默认使用智力，与 100 做差值比较：每高 10 点治疗提高 10%，每低 10 点降低 10%。
# 调大 HEAL_ATTR_COEF_PER_STEP_BPS 会让治疗更依赖属性；调小会让治疗更依赖技能率。

HEAL_UP_MAX_BPS = 10000
HEAL_RECEIVED_UP_MAX_BPS = 10000
HEAL_REDUCE_MAX_BPS = 10000
CRIT_RATE_MAX_BPS = 10000
HEAL_CRIT_RATE_MAX_BPS = 10000
CRIT_DAMAGE_MULTIPLIER_BPS = 20000
CRIT_HEAL_MULTIPLIER_BPS = 20000
# 治疗提升、受到治疗提升、治疗降低的上限，均按 0 到 100% 处理。

RANDOM_COEF_MIN_BPS = 9500
RANDOM_COEF_MAX_BPS = 10500
# 伤害 / 治疗随机浮动范围：0.95 到 1.05。
# BattleContext 会从 DeterministicRNG 传入该值，保证同 seed 可回放。
# 如果单独调用 calc_damage / calc_heal 而不传 random_coef_bps，则默认使用 1.0，不引入随机。

DEAD_RATIO_BPS = 3000
WOUNDED_RATIO_BPS = BPS - DEAD_RATIO_BPS
# 伤兵 / 阵亡拆分。
# 默认 30% 直接阵亡，70% 进入伤兵池。
# 调大 DEAD_RATIO_BPS 会让战斗后恢复成本变高、连续作战压力更大；
# 调小 DEAD_RATIO_BPS 会让治疗和战后恢复更重要，战斗节奏更宽松。

WOUNDED_TO_DEAD_RATIO_BPS = 3000
# 每回合 ROUND_START 时，全体武将将当前伤兵池的 30% 转为死兵（不改变 current_troop）。
# 与 DEAD_RATIO_BPS 独立：前者是「回合开始」伤兵池自然损耗，后者是「受击瞬间」的阵亡/伤兵拆分。

MIN_DAMAGE = 1


# =============================================================================
# 伤害公式各项 Clamp 一览（与 calc_damage 实现一致）
# =============================================================================
#
# 完整公式：
#   Damage =
#       round(
#           (BaseDamage + AttrDiff × AttrDiffCoef)
#           × TroopCoef
#           × DamageUpCoef
#           × DamageReduceCoef
#           × VulnerableCoef
#           × RestrainCoef
#           × RandomCoef
#           × CritCoef
#           × skill_rate
#       )
#       + FixedExtraDamage
#
#   BaseDamage = BASE_DAMAGE（默认 390，不随 skill_rate 缩放）
#   skill_rate = coefficient_bps / 10000
# | skill_rate | coefficient_bps / 10000 | 无（整数万分比，由配置给定） |
# | BaseDamage | BASE_DAMAGE（默认 390） | 无 |
# | AttackAttr / DefenseAttr | get_effective_attr | 有效属性 ≥ 0（max(0, …)） |
# | raw AttrDiff | AttackAttr − DefenseAttr（真伤 DefenseAttr=0） | 无（映射前） |
# | AttrDiff | _map_attr_diff(raw) | 正差：raw≥1000 → +1000；负差：≥−30 为 1:1，(−200,−30] 分段插值，≤−200 → −45 |
# | AttrDiffCoef | 常量 ATTR_DIFF_COEF=8 | 无 |
# | AttrBonus | AttrDiff × AttrDiffCoef | 无 |
# | CoreDamage | BaseDamage + AttrBonus | ≥ 0（max(0, …)） |
# | current_troops | 施法者当前兵力 | ≥ 0（max(0, …)） |
# | TroopCoef | 0.4 + 0.6 × current / MAX_TROOPS | 无上下限截断；ignore_troop_coef 时固定 1.0 |
# | DamageUpCoef | 1 + damage_up_bps/10000 | damage_up_bps ∈ [0, 10000]（+100% 上限） |
# | DamageReduceCoef | 1 − damage_reduce_bps/10000 | damage_reduce_bps ∈ [0, 8000]（−80% 上限） |
# | VulnerableCoef | 1 + vulnerable_bps/10000 | vulnerable_bps ∈ [0, 10000]（+100% 上限） |
# | RestrainCoef | 外部传入 restrain_coef_bps | ≥ 0（max(0, …)），无上限 |
# | RandomCoef | BattleContext RNG 或默认 1.0 | ∈ [0.95, 1.05]（9500~10500 bps） |
# | CritCoef | 默认 1.0，暴击 2.0 | crit_multiplier_bps ≥ 0（max(0, …)），无上限 |
# | FixedExtraDamage | effect/state payload | 无 |
# | 理论伤害取整 | 万分比连乘后 round | 无额外 clamp |
# | 最终伤害 | min(max(1, 理论伤害), target.current) | ≥ MIN_DAMAGE(1)；≤ 目标当前兵力；目标兵力≤0 时整段返回 0 |
#
# apply_damage 拆分（结算层，非公式倍率）：
# | actual_damage | min(理论伤害, target.current) | ≥ 0 |
# | dead_ratio | DEAD_RATIO_BPS 默认 3000 | ∈ [0, 10000] |
#
# apply_wounded_to_dead（回合开始伤兵池损耗）：
# | converted | floor(wounded_troop × WOUNDED_TO_DEAD_RATIO_BPS / 10000) | wounded_troop 减少，dead_troop 增加，current_troop 不变 |
#
def clamp(value: int, min_value: int, max_value: int) -> int:
    """把 value 限制在 [min_value, max_value] 范围内。

    本模型中倍率 clamp：
    - 属性差值映射有上下限设计。
    - 增伤 / 减伤 / 易伤需要 clamp，避免多个状态叠加后破坏数值边界。
    - TroopCoef 不截断，随 current / MAX_TROOPS 线性变化。
    """
    return max(min_value, min(value, max_value))


def _iter_attr_states(hero: Hero) -> list[Any]:
    """筛选参与四维 / 暴击 / 增伤 / 易伤等 ATTR 模型读取的状态。"""
    return [
        state
        for state in hero.states
        if getattr(state, "state_type", None) == StateType.ATTR and not hero.exited
    ]


def _iter_damage_reduce_states(hero: Hero) -> list[Any]:
    """筛选参与减伤乘区计算的状态。"""
    return [
        state
        for state in hero.states
        if getattr(state, "state_type", None) == StateType.DAMAGE_REDUCE and not hero.exited
    ]


def _get_attr_state_payload_sum(hero: Hero, key: str) -> int:
    """读取 ATTR 状态 payload 的整数累计值。"""
    return sum(int(state.payload.get(key, 0)) for state in _iter_attr_states(hero))


def _get_damage_reduce_state_payload_sum(hero: Hero, key: str) -> int:
    """读取减伤乘区状态 payload 的整数累计值。"""
    return sum(int(state.payload.get(key, 0)) for state in _iter_damage_reduce_states(hero))


def _get_attr_bps(hero: Hero, key: str) -> int:
    """读取 hero 身上 ATTR 状态 payload 的万分比累计值。"""
    return _get_attr_state_payload_sum(hero, key)


def _get_damage_reduce_bps(hero: Hero, key: str) -> int:
    """读取 hero 身上减伤乘区状态的万分比累计值。"""
    return _get_damage_reduce_state_payload_sum(hero, key)


def get_effective_attr(hero: Hero, attr_name: str) -> int:
    """读取武将的战斗有效属性。

    基础属性来自 Hero 本体，例如：
    - force
    - intelligence
    - command

    但战斗中可能存在一些 StateType.ATTR 的状态，
    这些状态挂在武将身上并持续生效（也可监听信号动态更新 payload）。
    典型例子：
    - 武力提高 20
    - 智力降低 15
    - 统率提高 10

    因此伤害 / 治疗计算不能只读 hero.force / hero.intelligence / hero.command，
    还需要读取当前持有 states 中的属性修正。

    当前支持的 payload 字段：
    - force_delta / intelligence_delta / command_delta：直接加减固定属性值。
    - force_bps / intelligence_bps / command_bps：按万分比修正属性。

    例子：
    - {"force_delta": 20} 表示武力 +20。
    - {"force_delta": -10} 表示武力 -10。
    - {"force_bps": 2000} 表示武力额外 +20%。

    注意：
    - 固定值和百分比都只从当前 ATTR states 汇总。
    - 最终属性不低于 0，避免负属性导致公式异常。
    - 这只是数值层读取，不负责状态触发或持续时间。
    """
    base_value = int(getattr(hero, attr_name))
    flat_delta = _get_attr_state_payload_sum(hero, f"{attr_name}_delta")
    percent_delta_bps = _get_attr_state_payload_sum(hero, f"{attr_name}_bps")
    after_flat = base_value + flat_delta
    after_percent = after_flat * (BPS + percent_delta_bps) // BPS
    return max(0, after_percent)


def get_effective_crit_rate_bps(hero: Hero) -> int:
    base_rate = int(getattr(hero, "crit_rate_bps", 0))
    return clamp(base_rate + _get_attr_bps(hero, "crit_rate_bps"), 0, CRIT_RATE_MAX_BPS)


def get_effective_heal_crit_rate_bps(hero: Hero) -> int:
    base_rate = int(getattr(hero, "heal_crit_rate_bps", 0))
    return clamp(base_rate + _get_attr_bps(hero, "heal_crit_rate_bps"), 0, HEAL_CRIT_RATE_MAX_BPS)


def _get_max_troop(hero: Hero) -> int:
    return int(getattr(hero, "max_troop", getattr(hero, "max_troops")))


def _get_current_troop(hero: Hero) -> int:
    return int(getattr(hero, "current_troop", getattr(hero, "troops")))


def _set_current_troop(hero: Hero, value: int) -> None:
    value = max(0, min(value, _get_max_troop(hero)))
    if hasattr(type(hero), "current_troop"):
        hero.current_troop = value
    else:
        hero.troops = value


def calc_troop_coef(caster: Hero, ignore_troop_coef: bool = False) -> int:
    """计算兵力系数 TroopCoef（万分比）。

    公式：
        rate = caster.current_troops / MAX_TROOPS
        TroopCoef = 0.4 + 0.6 × rate

    注意：
    - 分母是全局 MAX_TROOPS，不是武将自身的 max_troops。
    - 不做 [0.4, 1.0] 截断；兵力高于 MAX_TROOPS 时 Co 可 > 1.0。
    - ignore_troop_coef=True 时返回 1.0（10000 bps）。
    """
    if ignore_troop_coef:
        return BPS
    current_troop = max(0, _get_current_troop(caster))
    rate_bps = current_troop * BPS // MAX_TROOPS
    return TROOP_COEF_BASE_BPS + TROOP_COEF_RATE_BPS * rate_bps // BPS


def _get_attack_defense_attrs(caster: Hero, target: Hero, damage_type: DamageType) -> tuple[int, int]:
    """按伤害类型读取攻防有效属性。

    兵刃 PHYSICAL：武力 vs 统率。
    谋略 MAGIC：智力 vs 智力。
    真伤 TRUE：武力 vs 0（计算 AttrDiff 时视对方统率为 0，不读取实际统率）。
    """
    if damage_type == DamageType.PHYSICAL:
        attack_attr = get_effective_attr(caster, "force")
        defense_attr = get_effective_attr(target, "command")
    elif damage_type == DamageType.MAGIC:
        attack_attr = get_effective_attr(caster, "intelligence")
        defense_attr = get_effective_attr(target, "intelligence")
    elif damage_type == DamageType.TRUE:
        attack_attr = get_effective_attr(caster, "force")
        defense_attr = 0
    else:
        attack_attr = get_effective_attr(caster, "force")
        defense_attr = get_effective_attr(target, "command")
    return attack_attr, defense_attr


def calc_base_damage() -> int:
    """固定基础伤害层 BaseDamage，不随技能系数缩放。"""
    return BASE_DAMAGE


def _map_attr_diff(raw_diff: int) -> int:
    """将原始攻防差映射为有效 AttrDiff。

    正差：raw < ATTR_DIFF_MAX（1000）时 1:1；raw ≥ 1000 时截顶为 +1000（仅安全兜底）。
    负差：
    - raw ≥ -30：1:1（含 -30/-40/-50 在阈值内时仍按原值，仅更负才压缩）。
    - (-200, -30]：按 _ATTR_DIFF_NEG_ANCHORS 分段线性插值，堆统率收益递减。
    - raw ≤ -200：固定为 ATTR_DIFF_FLOOR（-45）。
    """
    if raw_diff >= ATTR_DIFF_MAX:
        return ATTR_DIFF_MAX
    if raw_diff >= _ATTR_DIFF_NEG_ANCHORS[0][0]:
        return raw_diff
    if raw_diff <= _ATTR_DIFF_NEG_ANCHORS[-1][0]:
        return ATTR_DIFF_FLOOR
    for (x_hi, y_hi), (x_lo, y_lo) in zip(_ATTR_DIFF_NEG_ANCHORS, _ATTR_DIFF_NEG_ANCHORS[1:]):
        if x_lo < raw_diff <= x_hi:
            span = x_hi - x_lo
            progress_num = x_hi - raw_diff
            return (y_hi * span + (y_lo - y_hi) * progress_num + span // 2) // span
    return ATTR_DIFF_FLOOR


def calc_attr_diff(caster: Hero, target: Hero, damage_type: DamageType) -> int:
    """计算有效 AttrDiff（含正负分段映射）。"""
    attack_attr, defense_attr = _get_attack_defense_attrs(caster, target, damage_type)
    return _map_attr_diff(attack_attr - defense_attr)


def calc_attr_diff_bonus(caster: Hero, target: Hero, damage_type: DamageType) -> int:
    """计算属性差固定加成 AttrDiff × ATTR_DIFF_COEF（加入 BaseDamage）。"""
    return calc_attr_diff(caster, target, damage_type) * ATTR_DIFF_COEF


def _damage_up_bps(caster: Hero, damage_type: DamageType) -> int:
    general = _get_attr_bps(caster, "damage_up_bps") + _get_attr_bps(caster, "damage_bonus_bps")
    if damage_type == DamageType.PHYSICAL:
        typed = _get_attr_bps(caster, "physical_damage_up_bps")
    else:
        typed = _get_attr_bps(caster, "magic_damage_up_bps")
    return clamp(general + typed, 0, DAMAGE_UP_MAX_BPS)


def _damage_reduce_bps(target: Hero, damage_type: DamageType) -> int:
    general = _get_damage_reduce_bps(target, "damage_reduce_bps") + _get_damage_reduce_bps(
        target, "damage_reduction_bps"
    )
    if damage_type == DamageType.PHYSICAL:
        typed = _get_damage_reduce_bps(target, "physical_damage_reduce_bps")
    else:
        typed = _get_damage_reduce_bps(target, "magic_damage_reduce_bps")
    return clamp(general + typed, 0, DAMAGE_REDUCE_MAX_BPS)


def _vulnerable_bps(target: Hero) -> int:
    return clamp(
        _get_attr_bps(target, "vulnerable_bps") + _get_attr_bps(target, "damage_taken_bonus_bps"),
        0,
        VULNERABLE_MAX_BPS,
    )


def calc_damage(
    caster: Hero,
    target: Hero,
    damage_type: DamageType,
    skill_rate_bps: int,
    *,
    ignore_troop_coef: bool = False,
    restrain_coef_bps: int = BPS,
    random_coef_bps: int | None = None,
    fixed_extra_damage: int = 0,
    crit_multiplier_bps: int = BPS,
) -> int:
    """计算最终伤害，但不直接修改兵力。

    完整公式：
        Damage =
            round(
                (BaseDamage + AttrDiff × AttrDiffCoef)
                × TroopCoef
                × DamageUpCoef
                × DamageReduceCoef
                × VulnerableCoef
                × RestrainCoef
                × RandomCoef
                × CritCoef
                × skill_rate
            )
            + FixedExtraDamage

    BaseDamage = BASE_DAMAGE（默认 390，与兵力无关）。
    skill_rate = skill_rate_bps / 10000（Effect.coefficient_bps）。
    各项 Clamp（详见本文件顶部「伤害公式各项 Clamp 一览」）：
    - BaseDamage：固定 BASE_DAMAGE；skill_rate 为乘区，无 clamp。
    - 有效攻防属性：≥ 0。
    - AttrDiff 映射：正差 raw≥1000 → +1000；负差 ≥−30 为 1:1，(−200,−30] 分段压缩，≤−200 → −45。
    - CoreDamage = BaseDamage + AttrBonus：≥ 0。
    - TroopCoef：不截断；ignore_troop_coef 时固定 1.0。
    - DamageUpCoef：增伤 bps ∈ [0, 10000]。
    - DamageReduceCoef：减伤 bps ∈ [0, 8000]。
    - VulnerableCoef：易伤 bps ∈ [0, 10000]。
    - RestrainCoef：≥ 0，无上限。
    - RandomCoef：∈ [0.95, 1.05]。
    - CritCoef：≥ 0，无上限（默认 1.0，暴击 2.0）。
    - 最终伤害：≥ MIN_DAMAGE(1)，≤ 目标当前兵力；目标已阵亡返回 0。

    各部分含义：
    - BaseDamage 恒为 BASE_DAMAGE（默认 390）；skill_rate 在乘区末尾缩放整段 Core×倍率。
      想让全局战斗更快，调大 BASE_DAMAGE；想更慢，调小它。
    - AttrDiff 由 raw=AttackAttr-DefenseAttr 分段映射；AttrDiffCoef = ATTR_DIFF_COEF（当前为 8）。
      正差 1:1（仅 raw≥1000 时安全截顶）；raw≥-30 时负差 1:1，更负时按锚点压缩至最低 -45。
      兵刃：AttackAttr=武力，DefenseAttr=统率；谋略：双方均取智力；
      真伤：AttackAttr=武力，DefenseAttr 固定为 0（无视对方统率）。
    - TroopCoef = 0.4 + 0.6 × (current_troops / MAX_TROOPS)，全局 MAX_TROOPS=10000，不截断。
    - DamageUpCoef / DamageReduceCoef / VulnerableCoef 来自状态 payload。
      支持通用增伤减伤，也支持 physical / magic 专属增伤减伤。
    - RestrainCoef 只接收外部传入，不在这里判断兵种克制。
    - RandomCoef 默认 1.0；BattleContext 可传入确定性 RNG 生成的 0.95 到 1.05。
    - CritCoef 默认 1.0；暴击时为 2.0。
    - FixedExtraDamage 用于以后实现固定额外伤害。

    返回：
    - 已取整的实际伤害。
    - 最小为 1。
    - 不超过目标当前兵力。
    - 若目标当前兵力 <= 0，返回 0。
    """
    target_current = _get_current_troop(target)
    if target_current <= 0:
        return 0

    attr_bonus = calc_attr_diff_bonus(caster, target, damage_type)
    if damage_type == DamageType.TRUE:
        troop_coef_bps = BPS if ignore_troop_coef else calc_troop_coef(caster, False)
    else:
        troop_coef_bps = calc_troop_coef(caster, ignore_troop_coef)

    random_coef_bps = random_coef_bps if random_coef_bps is not None else BPS
    random_coef_bps = clamp(random_coef_bps, RANDOM_COEF_MIN_BPS, RANDOM_COEF_MAX_BPS)
    restrain_coef_bps = max(0, restrain_coef_bps)
    crit_multiplier_bps = max(0, crit_multiplier_bps)

    # BaseDamage 与属性差固定加成先合并为基础层；skill_rate 与其余万分比倍率统一 round。
    base_damage = calc_base_damage()
    core_damage = max(0, base_damage + attr_bonus)

    multiplier_num = (
        troop_coef_bps
        * (BPS + _damage_up_bps(caster, damage_type))
        * (BPS - _damage_reduce_bps(target, damage_type))
        * (BPS + _vulnerable_bps(target))
        * restrain_coef_bps
        * random_coef_bps
        * crit_multiplier_bps
        * skill_rate_bps
    )
    multiplier_den = BPS**8
    damage_num = core_damage * multiplier_num
    damage = (damage_num + multiplier_den // 2) // multiplier_den + fixed_extra_damage

    return min(max(MIN_DAMAGE, int(damage)), target_current)


def apply_damage(target: Hero, damage: int, *, dead_ratio_bps: int = DEAD_RATIO_BPS) -> dict[str, int]:
    """应用伤害，并拆分伤兵 / 阵亡。

    规则：
        ActualDamage = min(damage, target.current_troop)
        Dead = floor(ActualDamage x DEAD_RATIO)
        Wounded = ActualDamage - Dead

    为什么要拆分：
    - current_troop 是战斗中还能继续作战的兵力。
    - wounded_troop 是伤兵，只能被治疗恢复。
    - dead_troop 是阵亡，治疗不能复活，只能战后或外部系统处理。

    调参说明：
    - dead_ratio_bps 越高，战斗损失越不可逆，连续作战压力越大。
    - dead_ratio_bps 越低，治疗价值越高，战斗节奏更偏消耗和拉扯。

    返回：
        {
            "actual_damage": 实际造成的兵力损失,
            "dead": 阵亡,
            "wounded": 伤兵
        }
    """
    current_troop = _get_current_troop(target)
    actual_damage = min(max(0, int(damage)), current_troop)
    dead_ratio_bps = clamp(dead_ratio_bps, 0, BPS)
    dead = actual_damage * dead_ratio_bps // BPS
    wounded = actual_damage - dead

    _set_current_troop(target, current_troop - actual_damage)
    target.dead_troop = max(0, target.dead_troop + dead)
    target.wounded_troop = max(0, target.wounded_troop + wounded)

    return {"actual_damage": actual_damage, "dead": dead, "wounded": wounded}


def apply_wounded_to_dead(
    hero: Hero,
    *,
    ratio_bps: int = WOUNDED_TO_DEAD_RATIO_BPS,
) -> dict[str, int]:
    """回合开始时将伤兵池的一部分转为死兵。

    规则：
        Converted = floor(wounded_troop × ratio_bps / 10000)
        wounded_troop -= Converted
        dead_troop += Converted
        current_troop 不变

    时机：
    - 由 BattleContext 在 ROUND_START 对仍在场（未阵亡）的全体武将统一调用。

    返回：
        {
            "converted": 本次由伤兵转为死兵的数量,
            "old_wounded_troop": 转换前伤兵池,
            "new_wounded_troop": 转换后伤兵池,
            "old_dead_troop": 转换前死兵池,
            "new_dead_troop": 转换后死兵池,
        }
    """
    ratio_bps = clamp(ratio_bps, 0, BPS)
    old_wounded_troop = max(0, hero.wounded_troop)
    old_dead_troop = max(0, hero.dead_troop)
    converted = old_wounded_troop * ratio_bps // BPS
    if converted <= 0:
        return {
            "converted": 0,
            "old_wounded_troop": old_wounded_troop,
            "new_wounded_troop": old_wounded_troop,
            "old_dead_troop": old_dead_troop,
            "new_dead_troop": old_dead_troop,
        }

    hero.wounded_troop = old_wounded_troop - converted
    hero.dead_troop = old_dead_troop + converted
    return {
        "converted": converted,
        "old_wounded_troop": old_wounded_troop,
        "new_wounded_troop": hero.wounded_troop,
        "old_dead_troop": old_dead_troop,
        "new_dead_troop": hero.dead_troop,
    }


def calc_heal_attr(healer: Hero) -> int:
    """计算治疗属性 HealAttr。

    当前默认：
        HealAttr = healer.intelligence

    为什么单独封装：
    - 现在治疗默认由智力修正，逻辑简单。
    - 以后可以不改治疗主公式，只替换这里的治疗属性来源。

    未来可扩展方案：
    - 方案 A：智力主导，统率辅助
        HealAttr = intelligence x 0.8 + command x 0.2
    - 方案 B：取智力和统率中的较高值
        HealAttr = max(intelligence, command)
    - 方案 C：多属性平均
        HealAttr = intelligence x 0.6 + command x 0.3 + force x 0.1
    """
    return get_effective_attr(healer, "intelligence")


def _heal_attr_coef_bps(healer: Hero) -> int:
    heal_attr = calc_heal_attr(healer)
    heal_attr_diff = heal_attr - HEAL_ATTR_BASE
    heal_attr_delta_bps = heal_attr_diff * HEAL_ATTR_COEF_PER_STEP_BPS // HEAL_ATTR_STEP
    return clamp(BPS + heal_attr_delta_bps, HEAL_ATTR_COEF_MIN_BPS, HEAL_ATTR_COEF_MAX_BPS)


def _heal_up_bps(healer: Hero) -> int:
    return clamp(_get_attr_bps(healer, "heal_up_bps") + _get_attr_bps(healer, "heal_bonus_bps"), 0, HEAL_UP_MAX_BPS)


def _heal_received_up_bps(target: Hero) -> int:
    return clamp(
        _get_attr_bps(target, "heal_received_up_bps") + _get_attr_bps(target, "heal_taken_bonus_bps"),
        0,
        HEAL_RECEIVED_UP_MAX_BPS,
    )


def _heal_reduce_bps(target: Hero) -> int:
    return clamp(_get_attr_bps(target, "heal_reduce_bps"), 0, HEAL_REDUCE_MAX_BPS)


def apply_heal_settlement_adjustments(
    healer: Hero,
    target: Hero,
    base_heal: int,
    *,
    crit_multiplier_bps: int = BPS,
    random_coef_bps: int | None = None,
    apply_modifiers: bool = True,
) -> int:
    """在技能基础治疗量之上叠加结算层修正。

    技能/状态只提供 base_heal；暴击、治疗增减、随机系数等在 BattleContext.apply_heal 结算时调用。
    """
    if base_heal <= 0:
        return 0
    random_coef_bps = random_coef_bps if random_coef_bps is not None else BPS
    random_coef_bps = clamp(random_coef_bps, RANDOM_COEF_MIN_BPS, RANDOM_COEF_MAX_BPS)
    crit_multiplier_bps = max(0, crit_multiplier_bps)

    if not apply_modifiers:
        return base_heal * crit_multiplier_bps // BPS

    multiplier_num = (
        (BPS + _heal_up_bps(healer))
        * (BPS + _heal_received_up_bps(target))
        * (BPS - _heal_reduce_bps(target))
        * random_coef_bps
        * crit_multiplier_bps
    )
    multiplier_den = BPS**5
    heal_num = base_heal * multiplier_num
    heal = (heal_num + multiplier_den // 2) // multiplier_den
    return max(0, int(heal))


def calc_snake_staff_base_heal(
    wounded: Hero,
    oracle_holder: Hero,
    *,
    heal_max_troop_bps: int,
    heal_source_intelligence_bps: int,
) -> int:
    """【蛇杖庇护】基础治疗量（技能层，不含结算修正）。

    公式：受击者 max_troop × heal_max_troop_bps + 神谕持有者智力 × heal_source_intelligence_bps。
    暴击与治疗增减在 apply_heal 结算层统一处理。
    """
    heal_from_max = wounded.max_troop * heal_max_troop_bps // BPS
    heal_from_int = get_effective_attr(oracle_holder, "intelligence") * heal_source_intelligence_bps // BPS
    return heal_from_max + heal_from_int


def calc_heal(
    healer: Hero,
    target: Hero,
    heal_rate_bps: int,
    *,
    random_coef_bps: int | None = None,
    fixed_extra_heal: int = 0,
    crit_multiplier_bps: int = BPS,
) -> int:
    """计算理论治疗量，但不直接修改兵力。

    完整公式：
        Heal =
            BaseHeal
            x HealAttrCoef
            x HealUpCoef
            x HealReceivedCoef
            x HealReduceCoef
            x RandomCoef
            x CritCoef
            + FixedExtraHeal

    关键规则：
    - BaseHeal = healer.max_troop x 0.03 x heal_rate。
    - HealAttrCoef 默认由 healer.intelligence 修正，并 clamp 到 0.6 到 1.5。
    - HealUpCoef 是治疗者造成治疗提升。
    - HealReceivedCoef 是目标受到治疗提升。
    - HealReduceCoef 是目标受到治疗降低。
    - RandomCoef 默认 1.0；BattleContext 可传入确定性 RNG 生成的 0.95 到 1.05。
    - CritCoef 默认 1.0；治疗暴击时为 2.0。
    - 经过公式取整后的理论治疗量。
    - 不在这里修改兵力。
    - 实际能恢复多少，由 apply_heal 根据 wounded_troop 和缺兵量限制。
    """
    random_coef_bps = random_coef_bps if random_coef_bps is not None else BPS
    random_coef_bps = clamp(random_coef_bps, RANDOM_COEF_MIN_BPS, RANDOM_COEF_MAX_BPS)
    crit_multiplier_bps = max(0, crit_multiplier_bps)

    # 治疗同样采用最终统一 round，避免逐步 floor 导致治疗量长期偏低。
    base_heal_num = _get_max_troop(healer) * BASE_HEAL_RATIO_BPS * heal_rate_bps
    base_heal_den = BPS * BPS
    multiplier_num = (
        _heal_attr_coef_bps(healer)
        * (BPS + _heal_up_bps(healer))
        * (BPS + _heal_received_up_bps(target))
        * (BPS - _heal_reduce_bps(target))
        * random_coef_bps
        * crit_multiplier_bps
    )
    multiplier_den = BPS**6
    heal_num = base_heal_num * multiplier_num
    heal_den = base_heal_den * multiplier_den
    heal = (heal_num + heal_den // 2) // heal_den + fixed_extra_heal
    return max(0, int(heal))


def apply_heal(target: Hero, heal: int) -> int:
    """应用治疗，只能治疗伤兵，不能复活阵亡。

    实际治疗限制：
        ActualHeal = min(
            heal,
            target.wounded_troop,
            target.max_troop - target.current_troop
        )

    为什么不能治疗 dead_troop：
    - dead_troop 表示阵亡，是不可逆损失。
    - wounded_troop 表示伤兵，是治疗系统可以恢复的兵力池。
    - 这样能把“战中治疗”和“战后补兵 / 复活”分成两个清晰系统。

    更新：
    - current_troop 增加 ActualHeal，但不超过 max_troop。
    - wounded_troop 减少 ActualHeal，但不低于 0。
    - dead_troop 不变。
    """
    current_troop = _get_current_troop(target)
    missing_troop = max(0, _get_max_troop(target) - current_troop)
    actual_heal = min(max(0, int(heal)), max(0, target.wounded_troop), missing_troop)
    _set_current_troop(target, current_troop + actual_heal)
    target.wounded_troop = max(0, target.wounded_troop - actual_heal)
    return actual_heal


def calculate_damage(
    actor: Hero,
    target: Hero,
    coefficient_bps: int,
    based_on_attr: str,
    damage_type: DamageType,
) -> int:
    """兼容旧调用的伤害入口。

    based_on_attr 保留在签名中是为了兼容旧 EffectConfig；
    新模型会根据 damage_type 自动选择武力 / 智力，不再依赖 based_on_attr。
    """
    return calc_damage(actor, target, damage_type, coefficient_bps)


def calculate_heal(actor: Hero, target: Hero, coefficient_bps: int, based_on_attr: str) -> int:
    """兼容旧调用的治疗入口。

    based_on_attr 保留在签名中是为了兼容旧 EffectConfig；
    当前治疗属性由 calc_heal_attr() 统一决定，默认使用 intelligence。
    """
    return calc_heal(actor, target, coefficient_bps)
