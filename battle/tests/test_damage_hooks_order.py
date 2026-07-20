"""伤害响应钩子序：

1. 先守方 on_damage_taken，再攻方 on_damage_dealt（即便攻方 priority 更小）。
2. 同持有者内：他人施加的触发状态整段先于自身施加的，再按 priority。
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION, PHASE_GAME_START
from battle.setup import BattleSetup, TeamSetup
from battle.statuses import PERMANENT, SPECIAL, StatusDef
from battle.tests.helpers import make_hero

_ORDER: list[str] = []


def _mark_taken(engine, status, ctx):
    _ORDER.append(f"taken:{status.status_id}")


def _mark_dealt(engine, status, ctx):
    _ORDER.append(f"dealt:{status.status_id}")


# 攻方 priority=10（更小），守方 priority=90（更大）——旧合并序会先 dealt
DEALT_LOW_PRIO = StatusDef(
    status_id="t_dealt_low", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=10, on_damage_dealt=_mark_dealt,
)
TAKEN_HIGH_PRIO = StatusDef(
    status_id="t_taken_high", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=90, on_damage_taken=_mark_taken,
)
DEALT_MID = StatusDef(
    status_id="t_dealt_mid", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=40, on_damage_dealt=_mark_dealt,
)
TAKEN_MID = StatusDef(
    status_id="t_taken_mid", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=20, on_damage_taken=_mark_taken,
)
# 他人施加但 priority 更大（旧序会排在自身低 priority 之后）
DEALT_FROM_ALLY = StatusDef(
    status_id="t_dealt_ally", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=80, on_damage_dealt=_mark_dealt,
)
DEALT_SELF_LOW = StatusDef(
    status_id="t_dealt_self", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=5, on_damage_dealt=_mark_dealt,
)


def _engine(*, with_a2: bool = False) -> SeriesEngine:
    a_heroes = [make_hero("a1", 0, force=100, command=50, speed=90)]
    if with_a2:
        a_heroes.append(make_hero("a2", 1, force=80, command=50, speed=80))
    setup = BattleSetup(
        battle_id="t_hook_order",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=tuple(a_heroes)),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, force=50, command=50, speed=80),
            )),
        ),
    )
    engine = SeriesEngine(setup, seed=1)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    engine.writer.emit("game_start", {"game_no": 1, "troops": []})
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    return engine


def test_taken_before_dealt_even_when_dealt_has_lower_priority():
    """攻方 priority 更小也不能插到守方前面。"""
    global _ORDER
    _ORDER = []
    engine = _engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "t_anchor", "kind": "cast", "target_ids": ["b1"],
    })
    engine.apply_status(a1, a1, DEALT_LOW_PRIO, parent_seq=anchor)
    engine.apply_status(b1, b1, TAKEN_HIGH_PRIO, parent_seq=anchor)
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=10000, parent_seq=anchor)
    assert _ORDER == ["taken:t_taken_high", "dealt:t_dealt_low"], _ORDER


def test_within_side_priority_still_applies():
    """同侧、同源（皆自身）仍按 response_priority 升序。"""
    global _ORDER
    _ORDER = []
    engine = _engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "t_anchor", "kind": "cast", "target_ids": ["b1"],
    })
    engine.apply_status(a1, a1, DEALT_LOW_PRIO, parent_seq=anchor)
    engine.apply_status(a1, a1, DEALT_MID, parent_seq=anchor)
    engine.apply_status(b1, b1, TAKEN_HIGH_PRIO, parent_seq=anchor)
    engine.apply_status(b1, b1, TAKEN_MID, parent_seq=anchor)
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=10000, parent_seq=anchor)
    assert _ORDER == [
        "taken:t_taken_mid",
        "taken:t_taken_high",
        "dealt:t_dealt_low",
        "dealt:t_dealt_mid",
    ], _ORDER


def test_dealt_external_before_self_even_when_self_has_lower_priority():
    """A 造成伤害时：他人挂到 A 的触发整段先于 A 自身触发（即便自身 priority 更小）。"""
    global _ORDER
    _ORDER = []
    engine = _engine(with_a2=True)
    a1, a2, b1 = engine.hero_by_id("a1"), engine.hero_by_id("a2"), engine.hero_by_id("b1")
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "t_anchor", "kind": "cast", "target_ids": ["b1"],
    })
    engine.apply_status(a1, a1, DEALT_SELF_LOW, parent_seq=anchor)   # 自身 priority=5
    engine.apply_status(a2, a1, DEALT_FROM_ALLY, parent_seq=anchor)  # 他人 priority=80
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=10000, parent_seq=anchor)
    assert _ORDER == ["dealt:t_dealt_ally", "dealt:t_dealt_self"], _ORDER


def test_action_start_external_before_self():
    """行动开始钩子同样：他人施加优先于自身。"""
    global _ORDER
    _ORDER = []

    def _mark_action(engine, status, action_seq):
        _ORDER.append(f"action:{status.status_id}")

    external = StatusDef(
        status_id="t_act_ally", kind=SPECIAL, duration_rounds=PERMANENT,
        response_priority=90, on_action_start=_mark_action,
    )
    own = StatusDef(
        status_id="t_act_self", kind=SPECIAL, duration_rounds=PERMANENT,
        response_priority=10, on_action_start=_mark_action,
    )
    engine = _engine(with_a2=True)
    a1, a2 = engine.hero_by_id("a1"), engine.hero_by_id("a2")
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "t_anchor", "kind": "cast", "target_ids": [],
    })
    engine.apply_status(a1, a1, own, parent_seq=anchor)
    engine.apply_status(a2, a1, external, parent_seq=anchor)
    engine._dispatch_action_start(a1, anchor)
    assert _ORDER == ["action:t_act_ally", "action:t_act_self"], _ORDER


if __name__ == "__main__":
    import pytest
    raise SystemExit(pytest.main([__file__, "-v"]))
