"""状态系统单测：施加/刷新/叠层默认规则、行动窗口计次到期、修正聚合、
DoT tick、attr_change 回滚。直接在引擎实例上驱动原语（不经 simulate 全流程）。

直接运行：python battle/tests/test_statuses.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.statuses import BUFF, CONTROL, DEBUFF, PERMANENT, StatusDef
from battle.tests.helpers import full_3v3_setup


def bare_engine(seed: int = 1) -> tuple[SeriesEngine, int]:
    """开局并提供一个组根事件作挂载锚点，返回 (engine, anchor_seq)。"""
    engine = SeriesEngine(full_3v3_setup(), seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "test_anchor", "kind": "cast", "target_ids": [],
    })
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


BUFF_2R = StatusDef(status_id="buff_2r", kind=BUFF, duration_rounds=2,
                    modifiers={"damage_up_bps": 1500})
DEBUFF_1R = StatusDef(status_id="debuff_1r", kind=DEBUFF, duration_rounds=1,
                      modifiers={"vulnerable_bps": 2000})
CONTROL_1R = StatusDef(status_id="control_1r", kind=CONTROL, duration_rounds=1,
                       modifiers={"forbid_basic": True, "forbid_active": True})
STACKING = StatusDef(status_id="stacking", kind=BUFF, duration_rounds=3, max_stacks=3,
                     modifiers={"force_delta": 10})
PERM = StatusDef(status_id="perm", kind=BUFF, duration_rounds=PERMANENT,
                 modifiers={"speed_bps": 1000})


def test_apply_and_negative_default_no_refresh_no_stack():
    engine, anchor = bare_engine()
    source = engine.hero_by_id("a1")
    target = engine.hero_by_id("b1")

    first = engine.apply_status(source, target, DEBUFF_1R, parent_seq=anchor)
    assert first is not None and first.stacks == 1
    assert len(events_of(engine, "status_apply")) == 1

    # 负面默认不可刷新不可叠加：静默拒绝，无事件
    second = engine.apply_status(source, target, DEBUFF_1R, parent_seq=anchor)
    assert second is None
    assert len(events_of(engine, "status_apply")) == 1
    assert len(events_of(engine, "status_refresh")) == 0
    assert len(engine.hero_statuses("b1")) == 1


def test_buff_refresh_resets_duration():
    engine, anchor = bare_engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    instance = engine.apply_status(a1, b1, BUFF_2R, parent_seq=anchor)
    instance.action_tick_count = 2  # 模拟已消耗殆尽

    refreshed = engine.apply_status(a1, b1, BUFF_2R, parent_seq=anchor)
    assert refreshed is instance
    assert instance.action_tick_count == 0  # 计次重置
    assert instance.stacks == 1  # max_stacks=1 不叠层
    assert len(events_of(engine, "status_refresh")) == 1


def test_stacking_caps_at_max_stacks():
    engine, anchor = bare_engine()
    a1 = engine.hero_by_id("a1")
    for _ in range(5):
        engine.apply_status(a1, a1, STACKING, parent_seq=anchor)
    owned = engine.hero_statuses("a1")
    assert len(owned) == 1
    assert owned[0].stacks == 3  # 封顶
    # 修正聚合按层数放大：force 95 + 10×3 = 125
    assert engine.effective_attr(a1, "force") == 125


def test_modifier_aggregation_flat_then_percent():
    engine, anchor = bare_engine()
    b1 = engine.hero_by_id("b1")
    engine.apply_status(b1, b1, STACKING, parent_seq=anchor)          # force +10
    engine.apply_status(b1, b1, PERM, parent_seq=anchor)              # speed +10%
    assert engine.effective_attr(b1, "force") == 102   # 92+10
    assert engine.effective_attr(b1, "speed") == 99    # 90×1.1
    # 分层顺序：先平加后百分比
    boost = StatusDef(status_id="boost", kind=BUFF, duration_rounds=1,
                      modifiers={"force_delta": 8, "force_bps": 2000})
    engine.apply_status(b1, b1, boost, parent_seq=anchor)
    assert engine.effective_attr(b1, "force") == 132   # (92+10+8)×1.2


def test_action_window_duration_semantics():
    """「持续 1 回合」至少覆盖目标下一次行动窗口：第 1 次计次存活，第 2 次到期。"""
    engine, anchor = bare_engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(a1, b1, CONTROL_1R, parent_seq=anchor)

    expired = engine._tick_action_durations(b1)
    assert expired == []  # 计次 1 ≤ 1：本窗口仍被控
    assert engine.is_forbidden(b1, "forbid_basic")

    expired = engine._tick_action_durations(b1)
    assert [s.status_id for s in expired] == ["control_1r"]  # 计次 2 > 1：到期
    assert not engine.is_forbidden(b1, "forbid_basic")
    assert engine.hero_statuses("b1") == []


def test_permanent_status_never_expires():
    engine, anchor = bare_engine()
    a1 = engine.hero_by_id("a1")
    engine.apply_status(a1, a1, PERM, parent_seq=anchor)
    for _ in range(20):
        assert engine._tick_action_durations(a1) == []
    assert len(engine.hero_statuses("a1")) == 1


def test_dot_ticks_and_damage_uses_source_intelligence():
    engine, anchor = bare_engine()
    a3, b2 = engine.hero_by_id("a3"), engine.hero_by_id("b2")
    dot = StatusDef(status_id="poison", kind=DEBUFF, duration_rounds=2, dot_rate_bps=5000)
    engine.apply_status(a3, b2, dot, parent_seq=anchor)

    troops_before = b2.troops
    engine._tick_periodic_statuses(anchor)

    ticks = events_of(engine, "status_tick")
    damages = events_of(engine, "damage")
    assert len(ticks) == 1 and ticks[0]["payload"]["source_id"] == "a3"
    assert len(damages) == 1
    payload = damages[0]["payload"]
    assert payload["damage_type"] == "magic"
    assert payload["source_id"] == "a3" and payload["target_id"] == "b2"
    assert payload["is_crit"] is False  # DoT 不暴击
    assert b2.troops == troops_before - payload["amount"]
    assert damages[0]["parent_seq"] == ticks[0]["seq"]  # 数值挂在 tick 之下


def test_attr_change_scope_game_reverts_on_game_reset():
    engine, anchor = bare_engine()
    b1 = engine.hero_by_id("b1")
    assert b1.command == 88
    engine.modify_attr(b1, [("command", -10)], scope="game", parent_seq=anchor)
    assert b1.command == 78
    changes = events_of(engine, "attr_change")
    assert changes[0]["payload"]["changes"] == [{"attr": "command", "before": 88, "after": 78}]

    engine._reset_game_state()
    assert b1.command == 88  # 局末回滚


def test_attr_change_scope_series_persists():
    engine, anchor = bare_engine()
    b1 = engine.hero_by_id("b1")
    engine.modify_attr(b1, [("force", -5)], scope="series", parent_seq=anchor)
    engine._reset_game_state()
    assert b1.force == 87  # 系列级不回滚


def test_attr_change_floors_at_zero():
    engine, anchor = bare_engine()
    b1 = engine.hero_by_id("b1")
    engine.modify_attr(b1, [("intelligence", -999)], scope="game", parent_seq=anchor)
    assert b1.intelligence == 0
    engine._reset_game_state()
    assert b1.intelligence == 70  # 回滚按实际生效量


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
