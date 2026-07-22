"""单挑系统测试（D-03 配对升级）：资格、入池、空池拒战、真决斗公式、clash_cutins。

直接运行：python -m pytest battle/tests/test_duel.py -q
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.engine import (
    SeriesEngine,
    DUEL_PAIR_RATE_AT_50_BPS,
    DUEL_PAIR_RATE_AT_ZERO_BPS,
    DUEL_WIN_BASE_BPS,
    DUEL_WIN_PER_DIFF_BPS,
)
from battle.events import PHASE_ACTION
from battle.setup import BattleSetup, HeroSetup, TeamSetup
from battle.tests.helpers import make_hero


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def events_in_game(report: dict, game_no: int, event_type: str) -> list[dict]:
    for game in report["games"]:
        if game["game_no"] == game_no:
            return [e for e in game["events"] if e["type"] == event_type]
    return []


def duel_setup(
    *,
    force_a: int,
    int_a: int,
    force_b: int,
    int_b: int,
    battle_id: str = "t_duel",
    template_a: str = "tpl_a",
    template_b: str = "tpl_b",
) -> BattleSetup:
    return BattleSetup(
        battle_id=battle_id,
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                HeroSetup(
                    hero_id="a1", template_id=template_a, position=0,
                    force=force_a, intelligence=int_a, command=80, speed=80,
                    max_troops=10000, initial_troops=10000, skills=(),
                ),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                HeroSetup(
                    hero_id="b1", template_id=template_b, position=0,
                    force=force_b, intelligence=int_b, command=80, speed=80,
                    max_troops=10000, initial_troops=10000, skills=(),
                ),
            )),
        ),
    )


def test_duel_requires_force_gt_intelligence_both_sides():
    # B 方武力≤智力 → 无单挑
    report = simulate(duel_setup(force_a=100, int_a=50, force_b=80, int_b=90), seed=1)
    assert not [e for e in flat_events(report) if e["type"].startswith("duel")]

    report = simulate(duel_setup(force_a=100, int_a=50, force_b=91, int_b=40), seed=1)
    assert len(events_in_game(report, 1, "duel_challenge")) == 1


def test_duel_only_in_first_game():
    for seed in range(5):
        report = simulate(duel_setup(force_a=100, int_a=40, force_b=95, int_b=40), seed=seed)
        for game in report["games"]:
            if game["game_no"] == 1:
                continue
            duels = [e for e in game["events"] if e["type"].startswith("duel")]
            assert duels == [], f"seed={seed} 第 {game['game_no']} 局出现单挑"


def test_duel_challenge_includes_clash_cutins():
    report = simulate(duel_setup(force_a=100, int_a=40, force_b=95, int_b=40), seed=3)
    challenge = events_in_game(report, 1, "duel_challenge")[0]
    assert challenge["payload"]["clash_cutins"] in (1, 2, 3)
    # 差 5 → 3 段
    assert challenge["payload"]["clash_cutins"] == 3
    result = events_in_game(report, 1, "duel_result")[0]
    assert result["group_id"] == challenge["seq"]


def test_duel_clash_cutins_by_diff():
    engine = SeriesEngine(duel_setup(force_a=100, int_a=40, force_b=50, int_b=40), seed=1)
    assert engine._duel_clash_cutins(10) == 3
    assert engine._duel_clash_cutins(11) == 2
    assert engine._duel_clash_cutins(20) == 2
    assert engine._duel_clash_cutins(21) == 1


def test_duel_pair_admit_rate_endpoints():
    engine = SeriesEngine(duel_setup(force_a=100, int_a=40, force_b=90, int_b=40), seed=1)
    assert engine._duel_pair_admit_bps(0) == DUEL_PAIR_RATE_AT_ZERO_BPS
    assert engine._duel_pair_admit_bps(50) == DUEL_PAIR_RATE_AT_50_BPS
    assert engine._duel_pair_admit_bps(51) == DUEL_PAIR_RATE_AT_50_BPS
    assert engine._duel_pair_admit_bps(25) == 9000 - 25 * 170


def test_duel_win_rate_is_50_plus_d_certain_at_50():
    # 高武力胜率 = 50% + d（百分点）；d≥50 → 10000 bps 必胜
    assert DUEL_WIN_BASE_BPS + 0 * DUEL_WIN_PER_DIFF_BPS == 5000
    assert DUEL_WIN_BASE_BPS + 25 * DUEL_WIN_PER_DIFF_BPS == 7500
    assert DUEL_WIN_BASE_BPS + 50 * DUEL_WIN_PER_DIFF_BPS == 10000


def test_duel_emits_bond_voice_lines():
    """阿喀琉斯↔赫克托尔：叫阵/拒战应发羁绊池 trait_trigger（挂 duel 组）。"""
    report = simulate(
        duel_setup(
            force_a=241, int_a=40, force_b=248, int_b=40,
            template_a="hector", template_b="achilles",
            battle_id="t_duel_voice",
        ),
        seed=1,
    )
    duel_lines = [
        e for e in events_in_game(report, 1, "trait_trigger")
        if e["payload"].get("effect", "").startswith("duel_")
    ]
    effects = {e["payload"]["effect"] for e in duel_lines}
    assert "duel_challenge" in effects
    assert "duel_reject" in effects or "duel_accept" in effects
    chal = next(e for e in duel_lines if e["payload"]["effect"] == "duel_challenge")
    assert "赫克托尔" in chal["payload"]["line"] or "单挑" in chal["payload"]["line"] or "矛" in chal["payload"]["line"]
    # 叫阵挂在 duel_challenge 组下
    challenge = events_in_game(report, 1, "duel_challenge")[0]
    assert chal["group_id"] == challenge["group_id"]


def test_duel_bond_pair_preferred_in_pool():
    """阿喀琉斯↔赫克托尔为 S1：与无羁绊对并存时优先取羁绊对（入池后排序）。"""
    setup = BattleSetup(
        battle_id="t_bond_duel",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                HeroSetup(
                    hero_id="a1", template_id="achilles", position=0,
                    force=200, intelligence=50, command=100, speed=100,
                    max_troops=10000, initial_troops=10000, skills=(),
                ),
                HeroSetup(
                    hero_id="a2", template_id="tpl_filler", position=1,
                    force=150, intelligence=40, command=80, speed=80,
                    max_troops=10000, initial_troops=10000, skills=(),
                ),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                HeroSetup(
                    hero_id="b1", template_id="hector", position=0,
                    force=195, intelligence=50, command=100, speed=90,
                    max_troops=10000, initial_troops=10000, skills=(),
                ),
                HeroSetup(
                    hero_id="b2", template_id="tpl_other", position=1,
                    force=148, intelligence=40, command=80, speed=70,
                    max_troops=10000, initial_troops=10000, skills=(),
                ),
            )),
        ),
    )
    # 强制 100% 入池：覆盖 metadata 不可用，改用多 seed 直到出现 duel，
    # 并断言双方为 achilles-hector（叫阵/应战之一）
    seen = False
    for seed in range(80):
        report = simulate(setup, seed=seed)
        challenges = events_in_game(report, 1, "duel_challenge")
        if not challenges:
            continue
        seen = True
        ids = {challenges[0]["payload"]["challenger_id"],
               challenges[0]["payload"]["defender_id"]}
        # 差很小的序号对 a2-b2 也可能入池，但羁绊 weight 更优应压过
        assert ids == {"a1", "b1"}, f"seed={seed} ids={ids}"
        break
    assert seen, "未见到单挑"


def test_duel_scripted_reject_when_pool_empty():
    """武力差极大 → 入池率 5%；用 metadata 无法强制，改为直接调空池路径。"""
    setup = duel_setup(force_a=200, int_a=40, force_b=50, int_b=40)
    engine = SeriesEngine(setup, seed=1)
    engine.writer.begin_game()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine._resolve_duel_scripted_reject(1, a1, b1)
    events = engine.writer.games_events()[0]
    assert any(e["type"] == "duel_challenge" for e in events)
    result = [e for e in events if e["type"] == "duel_result"][0]
    assert result["payload"]["accepted"] is False
    assert "winner_id" not in result["payload"]


def test_equal_force_challenger_is_team_a():
    report = simulate(duel_setup(force_a=95, int_a=40, force_b=95, int_b=40), seed=3)
    challenge = events_in_game(report, 1, "duel_challenge")[0]["payload"]
    assert challenge["challenger_id"] == "a1"
    assert challenge["defender_id"] == "b1"


def test_accepted_duel_applies_penalty():
    for seed in range(50):
        report = simulate(duel_setup(force_a=105, int_a=40, force_b=91, int_b=40), seed=seed)
        result = events_in_game(report, 1, "duel_result")[0]["payload"]
        if not result.get("accepted"):
            continue
        attrs = [e for e in events_in_game(report, 1, "attr_change")
                 if e["payload"].get("scope") == "game"]
        assert attrs, "接受单挑应有四维惩罚"
        return
    raise AssertionError("50 个种子无一接受单挑")
