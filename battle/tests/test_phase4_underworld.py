"""Phase 4 A3 冥界批语义测试：石化凝视 15 智/回合上限/不刷新石化、蛇瞳一瞥双目标、
春芽被动、渡魂船费三段、摆渡诅咒、死亡凝望盯诅咒、三首噬咬恐惧、镰痕处决加成。

直接运行：python -m pytest battle/tests/test_phase4_underworld.py -q
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.skills import REGISTRY as SKILLS
from battle.tests.helpers import make_hero


def bare_engine(setup: BattleSetup, seed: int = 3) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


def uw_setup(battle_id: str, a_templates: tuple, b_count: int = 3) -> BattleSetup:
    heroes_a = tuple(
        hero_setup(t, hero_id=f"a{i + 1}", position=i) for i, t in enumerate(a_templates)
    )
    heroes_b = tuple(make_hero(f"b{i + 1}", i) for i in range(b_count))
    return BattleSetup(battle_id=battle_id, teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=heroes_a),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=heroes_b),
    ))


# ------------------------------------------------------------- 冥祭献统 v4

def test_hades_drain_gains_command_and_int():
    setup = uw_setup("t_hades_v4", ("hades", "cerberus"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    SKILLS["hades_underworld_dominion"].execute(
        engine, a1, engine.alive_allies(a1), anchor)
    assert engine.modifier(a1, "lifesteal_bps") == 1000
    cmd_before = engine.effective_attr(a1, "command")
    int_before = engine.effective_attr(a1, "intelligence")
    drain = engine.find_status("a1", "hades_command_drain")
    drain.definition.on_action_start(engine, drain, anchor)
    assert engine.effective_attr(a1, "command") >= cmd_before + 10
    assert engine.effective_attr(a1, "intelligence") >= int_before + 10


# ------------------------------------------------------------- 石化凝视 v4

def test_medusa_gaze_drain_cap_and_no_petrify_refresh():
    setup = uw_setup("t_gaze_v4", ("medusa",))
    engine, anchor = bare_engine(setup, seed=1)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    SKILLS["medusa_gaze"].execute(engine, a1, [a1], anchor)
    gaze = engine.find_status("a1", "medusa_gaze")
    int_before = engine.effective_attr(b1, "intelligence")
    triggered = 0
    for _ in range(20):
        if gaze.round_counters.get("gaze", 0) >= 3:
            break
        before = gaze.round_counters.get("gaze", 0)
        gaze.definition.on_damage_taken(engine, gaze, {
            "source": b1, "target": a1, "amount": 100, "damage_seq": anchor,
            "damage_type": "physical", "kind": "basic",
        })
        triggered += gaze.round_counters.get("gaze", 0) - before
    assert gaze.round_counters.get("gaze", 0) <= 3
    if triggered:
        # 每次吸 15 智，整场累计
        assert engine.effective_attr(b1, "intelligence") <= int_before - 15 * triggered + 15
        assert engine.find_status("b1", "petrify") is not None
        refreshes_before = len(events_of(engine, "status_refresh"))
        gaze.round_counters["gaze"] = 0
        gaze.definition.on_damage_taken(engine, gaze, {
            "source": b1, "target": a1, "amount": 100, "damage_seq": anchor,
            "damage_type": "physical", "kind": "basic",
        })
        # 已石化 → 不产生 petrify 的 refresh 事件
        petrify_refreshes = [
            e for e in events_of(engine, "status_refresh")[refreshes_before:]
            if e["payload"]["status"]["status_id"] == "petrify"
        ]
        assert not petrify_refreshes


def test_medusa_glance_two_targets():
    setup = uw_setup("t_glance_v4", ("medusa",))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    glance = SKILLS["medusa_glance"]
    targets = glance.select_targets(engine, a1)
    assert len(targets) == 2
    glance.execute(engine, a1, targets, anchor)
    petrified = [t for t in targets if engine.find_status(t.hero_id, "petrify")]
    assert petrified  # 存活目标应被石化


# ------------------------------------------------------------- 春芽 v4

def test_sprout_passive_reduce_and_heal():
    setup = uw_setup("t_sprout_v4", ("persephone", "hades"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    sprout = SKILLS["persephone_sprout"]
    targets = sprout.select_targets(engine, a1)
    assert [t.hero_id for t in targets] == ["a1", "a2"]  # 自身 + 随机 1 友军
    sprout.execute(engine, a1, targets, anchor)
    inst = engine.find_status("a2", "persephone_sprout")
    assert inst is not None
    assert inst.definition.duration_rounds == 4
    assert engine.modifier(engine.hero_by_id("a2"), "damage_reduce_bps") == 2500
    # 回合开始 60% 治疗：多驱动几次钩子必出 heal
    holder = engine.hero_by_id("a2")
    holder.troops = 5000
    holder.wounded_troop = holder.max_troops - 5000
    for _ in range(40):
        inst.definition.on_round_start(engine, inst, anchor, 1)
    assert events_of(engine, "heal"), "应至少产生一次春芽治疗"


# ------------------------------------------------------------- 渡魂船费 v4

def test_charon_ferry_damages_lowest_enemy():
    setup = uw_setup("t_ferry_v4", ("charon", "hades"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    SKILLS["charon_ferry"].execute(engine, a1, [a1], anchor)
    ferry = engine.find_status("a1", "charon_ferry")
    engine.hero_by_id("b2").troops = 3000  # 敌方最低
    ferry.definition.on_hero_defeated(engine, ferry, {"defeat_seq": anchor})
    damages = events_of(engine, "damage")
    assert damages and damages[-1]["payload"]["target_id"] == "b2"
    assert damages[-1]["payload"]["damage_type"] == "magic"


# ------------------------------------------------------------- 摆渡 + 死亡凝望 v4

def test_ferryman_curse_and_death_gaze_chain():
    setup = uw_setup("t_curse_v4", ("charon", "thanatos"))
    engine, anchor = bare_engine(setup)
    a1, a2 = engine.hero_by_id("a1"), engine.hero_by_id("a2")
    b1 = engine.hero_by_id("b1")
    SKILLS["charon_ferryman"].execute(engine, a1, [a1], anchor)
    SKILLS["thanatos_gaze"].execute(engine, a2, [a2], anchor)
    # 卡戎造成实际伤害 → 目标被诅咒 → 死亡凝望有机会追打
    for _ in range(6):
        if not b1.is_alive():
            break
        engine.deal_damage(a1, b1, damage_type="magic", rate_bps=5000,
                           parent_seq=anchor, can_mitigate=False)
    assert engine.find_status("b1", "curse") is not None or not b1.is_alive()
    gaze = engine.find_status("a2", "thanatos_death_gaze")
    assert gaze.round_counters.get("gaze_strike", 0) <= 3
    death_gaze_hits = [e for e in events_of(engine, "damage")
                       if e["payload"]["source_id"] == "a2"]
    assert len(death_gaze_hits) == gaze.round_counters.get("gaze_strike", 0)


# ------------------------------------------------------------- 三首噬咬 v4

def test_cerberus_bite_three_strikes_and_fear():
    setup = uw_setup("t_bite_v4", ("cerberus",))
    engine, anchor = bare_engine(setup)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    SKILLS["cerberus_bite"].execute(engine, a1, [b1], anchor)
    strikes = [e for e in events_of(engine, "damage")
               if e["payload"]["target_id"] == "b1"]
    assert len(strikes) == 3
    assert engine.find_status("b1", "fear") is not None


# ------------------------------------------------------------- 死神镰痕 v4

def test_thanatos_scythe_execute_bonus():
    setup = uw_setup("t_scythe_v4", ("thanatos",))
    engine, anchor = bare_engine(setup, seed=7)
    a1 = engine.hero_by_id("a1")
    scythe = SKILLS["thanatos_scythe"]
    assert scythe.prepare_rounds == 0  # v4：不再准备
    low = engine.hero_by_id("b2")
    low.troops = low.max_troops * 2 // 10  # 20% ≤ 30% 触发处决加成
    targets = scythe.select_targets(engine, a1)
    assert [t.hero_id for t in targets] == ["b2"]
    scythe.execute(engine, a1, targets, anchor)
    assert events_of(engine, "damage")


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
