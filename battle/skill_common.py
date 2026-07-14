from __future__ import annotations

"""战法实现共用小件（Phase 3 v3.1 战法池）。"""

from battle.rng import BPS  # noqa: F401  (re-export 供各阵营战法文件使用)

ATTR_DELTA_KEYS = ("force_delta", "intelligence_delta", "command_delta", "speed_delta")
ATTR_NAMES = ("force", "intelligence", "command", "speed")


def emit_status_trigger(engine, status, parent_seq: int) -> int:
    """事件驱动状态发动：status_tick 作组根（契约 §11 情形②），parent 指因果事件。"""
    return engine.writer.emit(
        "status_tick",
        {"status": status.ref(), "source_id": status.source_id},
        parent_seq=parent_seq,
        new_group=True,
    )


def pick_distinct_enemies(engine, actor, n: int, reason_prefix: str) -> list:
    """按受击率连续选取至多 n 个互异敌方目标。"""
    picked = []
    ids: list[str] = []
    for i in range(n):
        target = engine.select_enemy_by_hit_rate(
            actor, reason=f"{reason_prefix}:{i}", exclude_ids=tuple(ids)
        )
        if target is None:
            break
        picked.append(target)
        ids.append(target.hero_id)
    return picked


def lowest_ratio_allies(engine, actor, n: int) -> list:
    """己方兵力比例最低的 n 人（升序；并列取遍历序靠前）。"""
    allies = engine.alive_allies(actor)
    allies = sorted(
        allies,
        key=lambda h: (h.troops * BPS // h.max_troops,
                       engine.hero_order.index(h.hero_id)),
    )
    return allies[:n]


def lowest_ratio_enemies(engine, actor, n: int) -> list:
    enemies = engine.alive_enemies(actor)
    enemies = sorted(
        enemies,
        key=lambda h: (h.troops * BPS // h.max_troops,
                       engine.hero_order.index(h.hero_id)),
    )
    return enemies[:n]
