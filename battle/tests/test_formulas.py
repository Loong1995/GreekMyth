"""公式单测（Phase 3 标定版，docs/mechanics/damage.md）。

期望值为手工推导的标定锚点，任何一条失败 = 公式实现失真，禁止改期望值迁就实现。
直接运行：python battle/tests/test_formulas.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import formulas
from battle.formulas import (
    BPS,
    calc_core_magic,
    calc_core_physical,
    calc_damage,
    calc_heal,
    calc_heal_attr_coef_bps,
    calc_hit_points_bps,
    calc_speed_first_probability_bps,
    calc_troop_coef_bps,
    constrain_heal,
    split_damage,
    wounded_decay,
)


# ---------------------------------------------------------------- 核心项

def test_core_physical():
    assert calc_core_physical(120, 100) == 380   # 360 + 120 - 100
    assert calc_core_physical(100, 100) == 360
    assert calc_core_physical(1, 1000) == 1      # min=1 安全截断


def test_core_magic():
    # 360 + 智力 - ½统率 - ½智力（半值合并后下取整）
    assert calc_core_magic(100, 80, 60) == 390   # 360+100-70
    assert calc_core_magic(100, 81, 60) == 390   # (81+60)//2 = 70（下取整）
    assert calc_core_magic(10, 500, 500) == 1    # min=1


# ---------------------------------------------------------------- 兵力系数

def test_troop_coef_anchors():
    """0.5 + 0.5×(troops/10000)：任务书四锚点精确对齐。"""
    assert calc_troop_coef_bps(10000) == 10000
    assert calc_troop_coef_bps(8000) == 9000
    assert calc_troop_coef_bps(6000) == 8000
    assert calc_troop_coef_bps(4000) == 7000
    assert calc_troop_coef_bps(30000) == 20000  # 超编不截断（D-05）
    assert calc_troop_coef_bps(0) == 5000


# ---------------------------------------------------------------- 伤害主公式

def test_damage_calibration_example():
    """core380 兵8000 系数200% 增伤20% 减伤10% → 739（一次舍入）。"""
    damage = calc_damage(
        core_damage=calc_core_physical(120, 100),
        attacker_current_troops=8000,
        target_current_troops=100000,
        skill_rate_bps=20000,
        damage_up_bps=2000,
        damage_reduce_bps=1000,
        random_coef_bps=10000,
    )
    assert damage == 739  # 380×0.9×1.2×0.9×2 = 738.72 → 739
    dead, wounded = split_damage(damage)
    assert (dead, wounded) == (221, 518)


def test_extra_damage_up_is_independent_zone():
    base = calc_damage(
        core_damage=360, attacker_current_troops=10000,
        target_current_troops=100000, skill_rate_bps=10000,
        random_coef_bps=10000,
    )
    boosted = calc_damage(
        core_damage=360, attacker_current_troops=10000,
        target_current_troops=100000, skill_rate_bps=10000,
        extra_damage_up_bps=5000, random_coef_bps=10000,
    )
    assert base == 360
    assert boosted == 540  # ×1.5 独立乘区


def test_crit_multiplier_in_single_rounding():
    base = calc_damage(
        core_damage=380, attacker_current_troops=8000,
        target_current_troops=100000, skill_rate_bps=20000,
        damage_up_bps=2000, damage_reduce_bps=1000, random_coef_bps=10000,
    )
    crit = calc_damage(
        core_damage=380, attacker_current_troops=8000,
        target_current_troops=100000, skill_rate_bps=20000,
        damage_up_bps=2000, damage_reduce_bps=1000, random_coef_bps=10000,
        crit_multiplier_bps=formulas.CRIT_DAMAGE_MULTIPLIER_BPS,
    )
    assert base == 739
    assert crit == 1477  # 738.72×2 = 1477.44 → 1477（≠ 739×2，一次舍入）


def test_damage_bounds():
    assert calc_damage(
        core_damage=1, attacker_current_troops=1,
        target_current_troops=10000, skill_rate_bps=100,
        random_coef_bps=10000,
    ) == formulas.MIN_DAMAGE  # 保底 1
    assert calc_damage(
        core_damage=360, attacker_current_troops=10000,
        target_current_troops=0, skill_rate_bps=10000,
    ) == 0  # 目标无兵
    assert calc_damage(
        core_damage=100000, attacker_current_troops=10000,
        target_current_troops=5000, skill_rate_bps=100000,
        random_coef_bps=10000,
    ) == 5000  # 目标当前兵力截断


def test_damage_reduce_cap():
    """减伤上限 80%。"""
    damage = calc_damage(
        core_damage=360, attacker_current_troops=10000,
        target_current_troops=100000, skill_rate_bps=10000,
        damage_reduce_bps=99999, random_coef_bps=10000,
    )
    assert damage == 72  # 360 × 0.2


# ---------------------------------------------------------------- 兵力三池

def test_wounded_decay_thirty_percent_floor():
    assert wounded_decay(1000) == 300
    assert wounded_decay(1) == 0
    assert wounded_decay(0) == 0


# ---------------------------------------------------------------- 治疗公式（不变）

def test_heal_model_example_1073():
    heal = calc_heal(
        healer_max_troops=10000,
        heal_attr=130,
        heal_rate_bps=15000,
        heal_up_bps=1000,
        random_coef_bps=10000,
    )
    assert heal == 1073


def test_heal_calibration_targets():
    assert calc_heal(healer_max_troops=10000, heal_attr=100,
                     heal_rate_bps=10000, random_coef_bps=10000) == 500
    assert calc_heal(healer_max_troops=10000, heal_attr=80,
                     heal_rate_bps=10000, random_coef_bps=10000) == 400


def test_heal_attr_coef_clamped():
    assert calc_heal_attr_coef_bps(100) == BPS
    assert calc_heal_attr_coef_bps(10) == formulas.HEAL_ATTR_COEF_MIN_BPS
    assert calc_heal_attr_coef_bps(300) == formulas.HEAL_ATTR_COEF_MAX_BPS


def test_heal_crit_doubles():
    base = calc_heal(healer_max_troops=10000, heal_attr=100,
                     heal_rate_bps=10000, random_coef_bps=10000)
    crit = calc_heal(healer_max_troops=10000, heal_attr=100,
                     heal_rate_bps=10000, random_coef_bps=10000,
                     crit_multiplier_bps=formulas.CRIT_HEAL_MULTIPLIER_BPS)
    assert crit == base * 2 == 1000


def test_constrain_heal_only_restores_wounded():
    assert constrain_heal(1073, wounded_troop=1200, max_troops=10000, current_troops=8000) == 1073
    assert constrain_heal(9999, wounded_troop=127, max_troops=10000, current_troops=9073) == 127
    assert constrain_heal(500, wounded_troop=800, max_troops=10000, current_troops=9900) == 100
    assert constrain_heal(500, wounded_troop=0, max_troops=10000, current_troops=5000) == 0


# ---------------------------------------------------------------- 先手与受击率（不变）

def test_speed_first_probability_anchors():
    expectations = {0: 5000, 1: 5500, 5: 7000, 10: 8000, 20: 10000, 25: 10000,
                    -1: 4500, -5: 3000, -10: 2000, -20: 0}
    for diff, prob in expectations.items():
        assert calc_speed_first_probability_bps(diff) == prob, diff
    assert calc_speed_first_probability_bps(3) == 6250


def test_hit_points_recomputed_from_initial():
    assert calc_hit_points_bps(initial_hit_points_bps=5000, max_troops=10000,
                               current_troops=10000) == 5000
    assert calc_hit_points_bps(initial_hit_points_bps=5000, max_troops=10000,
                               current_troops=5000) == 3500
    assert calc_hit_points_bps(initial_hit_points_bps=5000, max_troops=10000,
                               current_troops=0) == 2000


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
