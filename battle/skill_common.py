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


def emit_highlight_trigger(engine, hero, skill_id: str, targets: list, parent_seq: int) -> int:
    """专属高光释放：skill_trigger(kind="highlight") 作组根 + hint.cut_in 取景注记。

    高光 id 不是装配战法（不进 skill_catalog），客户端按 kind="highlight" 判定为
    主动形演出、按 hint.cut_in 走标准 cut-in（契约 §3.2 分叉组 / §7 hint）。
    """
    return engine.writer.emit(
        "skill_trigger",
        {
            "actor_id": hero.hero_id,
            "skill_id": skill_id,
            "kind": "highlight",
            "target_ids": [t.hero_id for t in targets],
        },
        parent_seq=parent_seq,
        new_group=True,
        hint={"cut_in": "highlight"},
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


def lowest_troops_enemies(engine, actor, n: int) -> list:
    """敌方**绝对兵力**最低的 n 人（升序；并列取遍历序靠前）。

    与 lowest_ratio_enemies 的比例口径不同：宙斯神罚按「兵力最低」取，
    即绝对值最小者（残血小兵优先于半血大兵）。
    """
    enemies = engine.alive_enemies(actor)
    enemies = sorted(
        enemies,
        key=lambda h: (h.troops, engine.hero_order.index(h.hero_id)),
    )
    return enemies[:n]


def highest_attr_unit(engine, actor, attr: str, *, allies: bool):
    """有效属性最高单位；并列取站位更小（与战神之勇口径一致）。无存活则 None。"""
    pool = engine.alive_allies(actor) if allies else engine.alive_enemies(actor)
    if not pool:
        return None
    best = pool[0]
    for unit in pool[1:]:
        ua = engine.effective_attr(unit, attr)
        ba = engine.effective_attr(best, attr)
        if ua > ba or (ua == ba and unit.position < best.position):
            best = unit
    return best
