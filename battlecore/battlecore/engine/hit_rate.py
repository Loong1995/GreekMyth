from __future__ import annotations

"""受击点数与实时受击率计算。

完整机制见 DESIGN_V2.md「受击率模型」一节。
"""

from battlecore.domain.hero import Hero
DEFAULT_HIT_POINTS_BPS = 5000
MAX_HIT_POINTS_DECAY_BPS = 3000
BPS_SCALE = 10000


def calc_troop_hit_points_offset(hero: Hero) -> int:
    """按当前损失兵力比例计算的扣减量：((最高兵力-当前兵力)/最高兵力)*3000，区间 [0, 3000]。"""
    if hero.max_troops <= 0:
        return 0
    lost_troops = max(0, hero.max_troops - hero.troops)
    return lost_troops * MAX_HIT_POINTS_DECAY_BPS // hero.max_troops


def calc_hit_points_from_troops(hero: Hero) -> int:
    """受击点数 = 开局初始受击点数 - 损失兵力比例扣减量（每次从初始值重算，非累扣）。"""
    offset = calc_troop_hit_points_offset(hero)
    return max(0, hero.initial_hit_points_bps - offset)


def recalc_hit_points_from_troops(hero: Hero) -> tuple[int, int, int, int]:
    """按初始受击点数与当前兵力重算受击点数。

    返回 (旧点数, 初始点数, 兵力比例扣减量, 新点数)。
    """
    old_points = hero.hit_points_bps
    initial_points = hero.initial_hit_points_bps
    offset = calc_troop_hit_points_offset(hero)
    new_points = max(0, initial_points - offset)
    hero.hit_points_bps = new_points
    return old_points, initial_points, offset, new_points


def calc_realtime_hit_rate_bps(hit_points: int, team_hit_points_sum: int) -> int:
    """归一法：自身受击点数 / 我方受击点数总和 * 10000。"""
    if team_hit_points_sum <= 0:
        return 0
    return hit_points * BPS_SCALE // team_hit_points_sum


def format_realtime_hit_rate_formula(hit_points: int, team_sum: int, rate_bps: int) -> str:
    return f"{rate_bps}={hit_points}/{team_sum}*10000"


def format_hit_points_recalc_formula(initial_points: int, offset: int, new_points: int) -> str:
    return f"初始{initial_points}-({offset})={new_points}"


def format_target_pool_hit_rate_weights(heroes: list[Hero]) -> str:
    """格式化候选池内各武将已维护的实时受击率权重，用于选人日志。"""
    if not heroes:
        return "<无候选>"
    return " | ".join(f"{hero.name}={hero.realtime_hit_rate_bps}" for hero in heroes)
