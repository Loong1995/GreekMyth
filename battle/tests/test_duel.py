"""单挑系统测试（决策 D-03）：触发门槛、仅第 1 局、拒绝/胜负公式、四维惩罚与回滚。

直接运行：python battle/tests/test_duel.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.engine import SeriesEngine
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero


def duel_setup(force_a: int, force_b: int, battle_id: str = "t_duel") -> BattleSetup:
    """两队主将高武力，副将低武力垫场；克制阵亡速度让系列多打几局。"""
    return BattleSetup(
        battle_id=battle_id,
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                make_hero("a1", 0, force=force_a, command=200, speed=90),
                make_hero("a2", 1, force=60, command=200, speed=80),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, force=force_b, command=200, speed=88),
                make_hero("b2", 1, force=60, command=200, speed=82),
            )),
        ),
    )


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def events_in_game(report: dict, game_no: int, event_type: str) -> list[dict]:
    return [e for e in report["games"][game_no - 1]["events"] if e["type"] == event_type]


def test_duel_requires_force_above_90_on_both_sides():
    # B 方最高武力 90（不满足 >90）→ 无单挑
    report = simulate(duel_setup(95, 90), seed=1)
    assert not [e for e in flat_events(report) if e["type"].startswith("duel")]

    # 双方都 >90 → 第 1 局必有叫阵
    report = simulate(duel_setup(95, 91), seed=1)
    assert len(events_in_game(report, 1, "duel_challenge")) == 1
    assert len(events_in_game(report, 1, "duel_result")) == 1


def test_duel_only_in_first_game():
    for seed in range(30):
        report = simulate(duel_setup(95, 92), seed=seed)
        for game in report["games"][1:]:
            duels = [e for e in game["events"] if e["type"].startswith("duel")]
            assert duels == [], f"seed={seed} 第 {game['game_no']} 局出现单挑"


def test_challenger_is_higher_force_and_payload_shape():
    report = simulate(duel_setup(98, 93), seed=3)
    challenge = events_in_game(report, 1, "duel_challenge")[0]
    p = challenge["payload"]
    assert p["challenger_id"] == "a1" and p["defender_id"] == "b1"
    assert p["challenger_force"] == 98 and p["defender_force"] == 93
    result = events_in_game(report, 1, "duel_result")[0]
    assert result["parent_seq"] == challenge["seq"]
    assert result["group_id"] == challenge["seq"]  # 单挑全过程一个播放组


def test_diff_ge_10_challenger_always_wins_when_accepted():
    """武力差 ≥10：必定结果（接受则挑战者必胜）；拒绝率封顶 80%（仍有人接受）。"""
    accepted = rejected = 0
    for seed in range(300):
        report = simulate(duel_setup(105, 91), seed=seed)
        result = events_in_game(report, 1, "duel_result")[0]["payload"]
        if result["accepted"]:
            accepted += 1
            assert result["winner_id"] == "a1", f"seed={seed} 差 14 挑战者未必胜"
        else:
            rejected += 1
    # 拒绝率理论 80%：300 场中两侧都应出现
    assert accepted > 0 and rejected > 0
    assert 0.70 <= rejected / 300 <= 0.90


def test_equal_force_no_reject_and_fifty_fifty():
    """差 0：拒绝率 0（必接受），胜率 50/50；破平规则叫阵方为 A（队伍序）。"""
    wins = {"a1": 0, "b1": 0}
    for seed in range(300):
        report = simulate(duel_setup(95, 95), seed=seed)
        challenge = events_in_game(report, 1, "duel_challenge")[0]["payload"]
        assert challenge["challenger_id"] == "a1"
        result = events_in_game(report, 1, "duel_result")[0]["payload"]
        assert result["accepted"] is True  # 差 0 拒绝率 0
        wins[result["winner_id"]] += 1
    assert 0.40 <= wins["a1"] / 300 <= 0.60


def test_loser_penalty_minus_10_all_attrs_scope_game():
    for seed in range(50):
        report = simulate(duel_setup(105, 91), seed=seed)
        result_event = events_in_game(report, 1, "duel_result")[0]
        if not result_event["payload"]["accepted"]:
            continue
        loser = result_event["payload"]["loser_id"]
        attr_changes = [
            e for e in report["games"][0]["events"]
            if e["type"] == "attr_change" and e["payload"]["hero_id"] == loser
            and e["parent_seq"] == result_event["seq"]
        ]
        assert len(attr_changes) == 1
        p = attr_changes[0]["payload"]
        assert p["scope"] == "game"
        assert {c["attr"] for c in p["changes"]} == {"force", "intelligence", "command", "speed"}
        for c in p["changes"]:
            assert c["after"] == c["before"] - 10
        return
    raise AssertionError("50 个种子无一接受单挑（拒绝率封顶 80%，不应如此）")


def test_penalty_reverts_after_game_1():
    """惩罚 scope=game：第 1 局结束后回滚（引擎级验证）。"""
    engine = SeriesEngine(duel_setup(105, 91), seed=13)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, 1, 0)
    engine.writer.emit("game_start", {"game_no": 1, "troops": []})
    engine._run_duel(1)
    b1 = engine.hero_by_id("b1")
    results = [e for e in engine.writer.games_events()[0] if e["type"] == "duel_result"]
    if results and results[0]["payload"]["accepted"]:
        assert b1.force == 81  # 91 - 10
        engine._reset_game_state()
        assert b1.force == 91  # 局末回滚
    else:
        assert b1.force == 91


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
