"""阵型注册表（formations）。

阵型 = 队伍占用站位集合命中预设后，按站位赋予
① 初始受击点数（initial_hit_points_bps，配将时写入 HeroState）
② 整场被动状态（每局 game_start 后重挂，duration=PERMANENT）

识别：`detect_formation(positions)` 对占用站位做**精确集合相等**匹配；
未命中返回 ""（无阵型加成）。配将**禁止**传入 formation 字符串——
只改 `HeroSetup.position`，由 `TeamSetup.formation` 只读属性自动识别。

加法式演进：新增阵型只在 FORMATION_REGISTRY 加一项。

数值来源（雁行阵，2026-07-23 求解）：
满兵受击率 40/40/20 → 点数比 2:2:1；6 号位兵力趋近 0 时受击率趋近 10%
→ (a-3000)/(5a-3000)=0.1 → a=5400。其余五阵暂无加成骨架（后续填）。
"""

from __future__ import annotations

from collections.abc import Iterable
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
    positions: frozenset[int]                      # 预设站位集合（精确匹配）
    hit_points_bps: dict[int, int] = field(default_factory=dict)   # 站位→初始受击点数
    buffs: dict[int, object] = field(default_factory=dict)         # 站位→StatusDef 工厂


def _skeleton(fid: str, name: str, positions: frozenset[int]) -> FormationDef:
    """无加成骨架：识别用；hit/buff 后续填。"""
    return FormationDef(formation_id=fid, name=name, positions=positions)


FORMATION_REGISTRY: dict[str, FormationDef] = {
    "yizi": _skeleton("yizi", "一字阵", frozenset({1, 2, 3})),
    "zhui": _skeleton("zhui", "锥形阵", frozenset({2, 4, 6})),
    "ji": _skeleton("ji", "箕形阵", frozenset({1, 5, 6})),
    "fangyuan": _skeleton("fangyuan", "方圆阵", frozenset({3, 4, 5})),
    "yanyue": _skeleton("yanyue", "偃月阵", frozenset({1, 3, 5})),
    "yanxing": FormationDef(
        formation_id="yanxing",
        name="雁行阵",
        positions=frozenset({1, 2, 6}),
        hit_points_bps={1: 10800, 2: 10800, 6: 5400},
        buffs={1: _yanxing_guard, 2: _yanxing_guard, 6: _yanxing_edge},
    ),
}

# 精确匹配表：frozenset → id
_DETECT_MAP: dict[frozenset[int], str] = {
    frozenset(f.positions): fid for fid, f in FORMATION_REGISTRY.items()
}


def get_formation(formation_id: str) -> FormationDef | None:
    if not formation_id:
        return None
    return FORMATION_REGISTRY.get(formation_id)


def detect_formation(positions: Iterable[int]) -> str:
    """占用站位精确集合相等 → 阵型 id；否则 ""。"""
    occupied = frozenset(int(p) for p in positions if int(p) >= 1)
    return _DETECT_MAP.get(occupied, "")


def resolve_formation(positions: Iterable[int]) -> FormationDef | None:
    """按站位自动识别阵型定义；未命中返回 None。"""
    return get_formation(detect_formation(positions))
