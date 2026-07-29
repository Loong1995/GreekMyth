"""台词系统（2026-07-28 升级）：派生随机确定性、问答配对、巨伤限次、覆盖口。"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import bonds as bn
from battle import simulate
from battle import voice_lines_enter as vle
from battle import voice_lines_highlight as vlh
from battle import voice_rng as vr
from battle.engine import MASSIVE_LINE_THRESHOLD, SeriesEngine
from battle.events import PHASE_ACTION, PHASE_GAME_START
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.voice_bond_data import BOND_DIALOGUES


def _pair_setup(
    template_a: str, template_b: str, *, same_team: bool, battle_id: str,
) -> BattleSetup:
    if same_team:
        return BattleSetup(battle_id=battle_id, teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                hero_setup(template_a, hero_id="a1", position=0),
                hero_setup(template_b, hero_id="a2", position=1),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                hero_setup("charon", hero_id="b1", position=0),
            )),
        ))
    return BattleSetup(battle_id=battle_id, teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup(template_a, hero_id="a1", position=0),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup(template_b, hero_id="b1", position=0),
        )),
    ))


def _enter_lines(setup: BattleSetup, seed: int) -> list[tuple[str, str]]:
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    vle.emit_enter_dialogues(engine)
    return [
        (e["payload"]["hero_id"], e["payload"]["line"])
        for e in engine.writer.games_events()[0]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "enter"
    ]


# ---------------------------------------------------------------- 派生随机

def test_pick_index_deterministic_and_seed_sensitive():
    a = [vr.pick_index(1, "enter:bond.x:enter_foe", i, 3) for i in range(20)]
    b = [vr.pick_index(1, "enter:bond.x:enter_foe", i, 3) for i in range(20)]
    c = [vr.pick_index(2, "enter:bond.x:enter_foe", i, 3) for i in range(20)]
    assert a == b               # 同 seed 同键 → 逐条可重放
    assert a != c               # 不同 seed → 组合不同
    assert set(a) == {0, 1, 2}  # 三条都用得上
    assert all(0 <= i < 3 for i in a + c)


def test_voice_pick_does_not_consume_battle_rng():
    """台词选词禁止动战斗 RNG（确定性红线）：掷点计数不得增长。"""
    setup = _pair_setup(
        "achilles", "hector", same_team=False, battle_id="t_voice_rng",
    )
    engine = SeriesEngine(setup, seed=7)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    before = engine.rng.index
    vle.emit_enter_dialogues(engine)
    assert engine.rng.index == before


def test_same_seed_same_lines_across_runs():
    setup = _pair_setup(
        "achilles", "hector", same_team=False, battle_id="t_voice_repeat",
    )
    assert _enter_lines(setup, 11) == _enter_lines(setup, 11)


# ---------------------------------------------------------------- 问答配对

def test_enter_bond_dialogue_is_question_then_matching_answer():
    setup = _pair_setup(
        "achilles", "hector", same_team=False, battle_id="t_voice_qa",
    )
    lines = _enter_lines(setup, 5)
    assert len(lines) == 2
    (asker, question), (answerer, answer) = lines
    # 定义序：bond.achilles_hector 的 first=achilles → a1 发问、b1 作答
    assert (asker, answerer) == ("a1", "b1")
    qa = BOND_DIALOGUES["bond.achilles_hector"]["enter_foe"]
    asked = next(q for q in qa if q[0] == question)
    assert answer in asked[1]["reply"]


def test_enter_cross_team_before_same_team():
    """先与对方队伍的羁绊，再与本方队伍的羁绊。"""
    setup = BattleSetup(battle_id="t_voice_order", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("achilles", hero_id="a1", position=0),
            hero_setup("patroclus", hero_id="a2", position=1),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("hector", hero_id="b1", position=0),
        )),
    ))
    engine = SeriesEngine(setup, seed=3)
    engine.writer.begin_game()
    units = vle._collect_bond_units(engine)
    kinds = [(u[0], u[2].bond_id) for u in units]
    assert kinds[0][0] == 0 and kinds[0][1] == "bond.achilles_hector"
    assert kinds[-1][0] == 1 and kinds[-1][1] == "bond.achilles_patroclus"


def test_bond_dialogue_scenes_have_three_questions_and_three_answers():
    """硬性条数：每场景 3 问、每问每答案键 3 条。"""
    for bond_id, scenes in BOND_DIALOGUES.items():
        for scene, questions in scenes.items():
            assert len(questions) == 3, (bond_id, scene, len(questions))
            for question, answers in questions:
                assert question
                keys = ("accept", "reject") if scene == "duel" else ("reply",)
                for key in keys:
                    assert len(answers.get(key, ())) == 3, (bond_id, scene, key)


def test_every_machine_bond_has_enter_dialogue():
    """机器表每条羁绊都必须有登场问答（敌/友双向），禁止半成品。"""
    ids = {d.bond_id for d in bn.BOND_DEFS}
    assert set(BOND_DIALOGUES) <= ids
    for bond_id in ids:
        scenes = BOND_DIALOGUES.get(bond_id, {})
        assert "enter_foe" in scenes and "enter_ally" in scenes, bond_id


def test_bond_direction_matches_dialogue_author_order():
    """分册的问方必须是机器表 first（否则视角写反）。"""
    for bond_id in BOND_DIALOGUES:
        d = next(x for x in bn.BOND_DEFS if x.bond_id == bond_id)
        assert d.first and d.second and d.first != d.second


# ---------------------------------------------------------------- 巨伤台词

def _massive_engine() -> SeriesEngine:
    setup = _pair_setup(
        "achilles", "hector", same_team=False, battle_id="t_massive",
    )
    engine = SeriesEngine(setup, seed=9)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    engine.current_game = 1
    engine.current_round = 1
    return engine


def _massive_count(engine: SeriesEngine) -> int:
    return sum(
        1 for e in engine.writer.games_events()[0]
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "massive"
    )


def test_massive_line_once_per_round_per_hero():
    engine = _massive_engine()
    src, tgt = engine.heroes["a1"], engine.heroes["b1"]
    big = MASSIVE_LINE_THRESHOLD + 1
    engine._maybe_emit_massive_line(src, tgt, big, None, 0)
    engine._maybe_emit_massive_line(src, tgt, big, None, 0)
    assert _massive_count(engine) == 1
    engine.current_round = 2
    engine._maybe_emit_massive_line(src, tgt, big, None, 0)
    assert _massive_count(engine) == 2


def test_massive_line_skipped_below_threshold_or_mitigated():
    engine = _massive_engine()
    src, tgt = engine.heroes["a1"], engine.heroes["b1"]
    engine._maybe_emit_massive_line(src, tgt, MASSIVE_LINE_THRESHOLD, None, 0)
    engine._maybe_emit_massive_line(
        src, tgt, MASSIVE_LINE_THRESHOLD + 1, "block", 0,
    )
    engine._maybe_emit_massive_line(src, src, MASSIVE_LINE_THRESHOLD + 1, None, 0)
    assert _massive_count(engine) == 0


def test_massive_shares_highlight_pool():
    """巨伤与高光共用词池（回退 generic 高光词）。"""
    picked = vlh.pick_highlight_pool("achilles", "massive", "hector")
    assert picked is not None
    key, lines = picked
    assert key == "generic" and lines
    assert vlh.pick_highlight_pool("achilles", "highlight", None)[1] == lines


# ---------------------------------------------------------------- 覆盖口

def test_trait_line_override_replaces_pool():
    from battle import voice_trait_data as vtd

    assert vtd.override_pool("achilles", "aoman_ignore") is None
    vtd.TRAIT_LINE_OVERRIDES["achilles"] = {"aoman_ignore": ("测试专属句",)}
    try:
        assert vtd.override_pool("achilles", "aoman_ignore") == ("测试专属句",)
        assert vtd.override_pool("achilles", "other") is None
    finally:
        vtd.TRAIT_LINE_OVERRIDES.pop("achilles", None)


def test_full_battle_still_emits_voice_events():
    report = simulate(
        _pair_setup("achilles", "hector", same_team=False, battle_id="t_voice_e2e"),
        seed=4,
    )
    effects = {
        e["payload"]["effect"]
        for g in report["games"] for e in g["events"]
        if e["type"] == "trait_trigger"
    }
    assert "enter" in effects
