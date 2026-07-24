from __future__ import annotations

"""阵型注册表（formations）。

阵型 = 队伍级配置：规定合法站位集合，并按站位赋予
① 初始受击点数（initial_hit_points_bps，配将时写入 HeroState）
② 整场被动状态（每局 game_start 后重挂，duration=PERMANENT，整局有效；
   逐局重挂即达成「整场」语义，与战时状态随局清空的边界规则一致）。

加法式演进：新增阵型只在 FORMATION_REGISTRY 加一项；TeamSetup.formation
默认空字符串 = 无阵型，行为与历史逐字节一致（golden 保障）。

数值来源（雁行阵，2026-07-23 求解）：
满兵受击率 40/40/20 → 点数比 2:2:1；6 号位兵力趋近 0 时受击率趋近 10%
→ (a-3000)/(5a-3000)=0.1 → a=5400。1/2 号位残兵受击率 7800/24000=32.5%。
"""

from dataclasses import dataclass, field

from battle import statuses as st

# 阵型被动状态 id（客户端表现登记见 StatusPresentationRegistry / names.py）
YANXING_GUARD = "formation_yanxing_guard"  # 1/2 号位：整场减伤 5%
YANXING_EDGE = "formation_yanxing_edge"    # 6 号位：整场增伤 8%


def _yanxing_guard() -> st.StatusDef:
    """雁行阵·雁翼（1/2 号位）：受到伤害 -5%，整场。"""
    return st.StatusDef(
        status_id=YANXING_GUARD, kind=st.BUFF, duration_rounds=st.PERMANENT,
        modifiers={"damage_reduce_bps": 500},
    )


def _yanxing_edge() -> st.StatusDef:
    """雁行阵·雁喙（6 号位）：造成伤害 +8%，整场。"""
    return st.StatusDef(
        status_id=YANXING_EDGE, kind=st.BUFF, duration_rounds=st.PERMANENT,
        modifiers={"damage_up_bps": 800},
    )


@dataclass(frozen=True, slots=True)
class FormationDef:
    formation_id: str
    name: str
    positions: frozenset[int]                      # 合法站位集合（配将校验）
    hit_points_bps: dict[int, int] = field(default_factory=dict)   # 站位→初始受击点数
    buffs: dict[int, object] = field(default_factory=dict)         # 站位→StatusDef 工厂


FORMATION_REGISTRY: dict[str, FormationDef] = {
    "yanxing": FormationDef(
        formation_id="yanxing",
        name="雁行阵",
        positions=frozenset({1, 2, 6}),
        hit_points_bps={1: 10800, 2: 10800, 6: 5400},
        buffs={1: _yanxing_guard, 2: _yanxing_guard, 6: _yanxing_edge},
    ),
}


def get_formation(formation_id: str) -> FormationDef | None:
    if not formation_id:
        return None
    return FORMATION_REGISTRY.get(formation_id)
