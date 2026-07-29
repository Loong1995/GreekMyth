"""登场羁绊对话编排测试。"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.engine import SeriesEngine
from battle.events import PHASE_GAME_START
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle import voice_lines_enter as vle
from battle.voice_bond_data import BOND_DIALOGUES


def _setup_bond_cross() -> BattleSetup:
    """S1 跨队阿喀琉斯↔赫克托尔 + 同队阿喀琉斯↔埃阿斯 S2 等。"""
    return BattleSetup(battle_id="t_enter", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("achilles", hero_id="a1", position=0),
            hero_setup("ajax", hero_id="a2", position=1),
            hero_setup("asclepius", hero_id="a3", position=2),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("hector", hero_id="b1", position=0),
            hero_setup("paris", hero_id="b2", position=1),
            hero_setup("medusa", hero_id="b3", position=2),
        )),
    ))


def _setup_no_bond() -> BattleSetup:
    """无机器羁绊对：宙斯 vs 美杜莎（表中无登记）。"""
    return BattleSetup(battle_id="t_enter_none", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("zeus", hero_id="a1", position=0),
            hero_setup("asclepius", hero_id="a2", position=1),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("medusa", hero_id="b1", position=0),
            hero_setup("charon", hero_id="b2", position=1),
        )),
    ))


def test_enter_emits_all_bond_units_before_duel():
    report = simulate(_setup_bond_cross(), seed=3)
    events = report["games"][0]["events"]
    gs = next(i for i, e in enumerate(events) if e["type"] == "game_start")
    enters = [
        e for e in events[gs + 1:]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "enter"
    ]
    assert len(enters) >= 2
    duel_i = next(
        (i for i, e in enumerate(events) if e["type"] == "duel_challenge"), None
    )
    if duel_i is not None:
        assert all(e["seq"] < events[duel_i]["seq"] for e in enters)


def test_enter_unit_same_group_id():
    engine = SeriesEngine(_setup_bond_cross(), seed=3)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    n = vle.emit_enter_dialogues(engine)
    assert n >= 2
    enters = [
        e for e in engine.writer.games_events()[0]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "enter"
    ]
    by_g: dict[int, list] = {}
    for e in enters:
        by_g.setdefault(e["group_id"], []).append(e)
    assert any(len(v) >= 2 for v in by_g.values())


def test_enter_sort_cross_team_first_then_definition_order():
    """排序主键=跨队优先（0 先于 1），次键=羁绊表定义序（2026-07-28）。"""
    engine = SeriesEngine(_setup_bond_cross(), seed=1)
    engine.writer.begin_game()
    units = vle._collect_bond_units(engine)
    assert units
    assert [u[0] for u in units] == sorted(u[0] for u in units)
    for flag in (0, 1):
        same_side = [u[1] for u in units if u[0] == flag]
        assert same_side == sorted(same_side)


def test_enter_ally_vs_foe_pool_differs():
    """同队用友池、跨队用敌池；阿喀琉斯↔帕特洛克勒斯文案不同。"""
    ally = vle.pick_enter_pool("achilles", "patroclus", same_team=True)
    foe = vle.pick_enter_pool("achilles", "patroclus", same_team=False)
    assert ally is not None and foe is not None
    assert ally[0] == "patroclus"
    assert foe[0] == "patroclus_foe"
    assert ally[1] != foe[1]
    assert "密友" in ally[1][0] or "跟紧" in ally[1][0]
    assert "对面" in foe[1][0] or "无密友" in foe[1][0] or "两断" in foe[1][0]


def test_enter_cross_team_emits_foe_line():
    setup = BattleSetup(battle_id="t_enter_foe", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("achilles", hero_id="a1", position=0),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("patroclus", hero_id="b1", position=0),
        )),
    ))
    engine = SeriesEngine(setup, seed=1)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    n = vle.emit_enter_dialogues(engine)
    assert n == 2
    enters = [
        e for e in engine.writer.games_events()[0]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "enter"
    ]
    lines = {e["payload"]["hero_id"]: e["payload"]["line"] for e in enters}
    assert "密友在侧" not in lines["a1"]
    assert "我心安" not in lines["b1"]


def test_enter_same_team_emits_ally_line():
    setup = BattleSetup(battle_id="t_enter_ally", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("achilles", hero_id="a1", position=0),
            hero_setup("patroclus", hero_id="a2", position=1),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("medusa", hero_id="b1", position=0),
        )),
    ))
    engine = SeriesEngine(setup, seed=1)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    vle.emit_enter_dialogues(engine)
    enters = [
        e for e in engine.writer.games_events()[0]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "enter"
    ]
    by_hero = {e["payload"]["hero_id"]: e["payload"]["line"] for e in enters}
    # 有问答分册（bond.achilles_patroclus）：a1 问、a2 答**同一问**的答案集
    questions = BOND_DIALOGUES["bond.achilles_patroclus"]["enter_ally"]
    asked = next(q for q in questions if q[0] == by_hero["a1"])
    assert by_hero["a2"] in asked[1]["reply"]

    engine = SeriesEngine(_setup_no_bond(), seed=2)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    assert not vle._collect_bond_units(engine)
    n = vle.emit_enter_dialogues(engine)
    assert n >= 1
    enters = [
        e for e in engine.writer.games_events()[0]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "enter"
    ]
    assert enters[0]["payload"]["hero_id"] == "a1"
    if len(enters) >= 2:
        assert enters[1]["payload"]["hero_id"] == "b1"
        assert enters[0]["group_id"] == enters[1]["group_id"]
