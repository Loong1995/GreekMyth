from __future__ import annotations

"""冒烟与规则测试：1v1/3v3 跑通、系列连战编排、初始兵力注入。

直接运行：python battle/tests/test_smoke_battle.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import pytest

from battle import simulate
from battle.errors import SetupError
from battle.setup import BattleSetup, HeroSetup, TeamSetup
from battle.tests.helpers import duel_1v1_setup, full_3v3_setup, make_hero, stalemate_setup


def test_1v1_basic_attack_battle_completes():
    report = simulate(duel_1v1_setup(), seed=1)
    assert report["result"]["winner_team_id"] in {"A", "B", None}
    assert report["games"][0]["events"], "事件流为空"


def test_3v3_runs_and_produces_stats():
    report = simulate(full_3v3_setup(), seed=99)
    stats = {entry["hero_id"]: entry for entry in report["result"]["stats"]}
    assert len(stats) == 6
    assert sum(entry["total_damage"] for entry in stats.values()) > 0


def test_stalemate_produces_7_game_series_draw():
    """互相打不动 → 每局 round_limit 平局 → 7 局系列平局（任务书 5.1）。"""
    report = simulate(stalemate_setup(), seed=5)
    assert report["result"]["winner_team_id"] is None
    assert report["result"]["reason"] == "series_limit"
    assert report["result"]["total_games"] == 7
    for game in report["games"]:
        assert game["result"]["winner_team_id"] is None
        assert game["result"]["reason"] == "round_limit"
        assert game["result"]["end_round"] == 8


def test_carryover_troops_between_games():
    """平局续战：下一局 game_start 快照等于上一局终局兵力（伤兵不恢复）。"""
    report = simulate(stalemate_setup(), seed=5)
    for prev_game, next_game in zip(report["games"], report["games"][1:]):
        prev_end = {e["hero_id"]: e for e in prev_game["result"]["troops"]}
        next_start_event = next(
            event for event in next_game["events"] if event["type"] == "game_start"
        )
        for entry in next_start_event["payload"]["troops"]:
            prev = prev_end[entry["hero_id"]]
            assert entry["troops_before"] == prev["troops_after"]
            assert entry["wounded_before"] == prev["wounded_after"]
            assert entry["dead_before"] == prev["dead_after"]


def test_initial_troops_and_overstacked_npc():
    """battle_setup 支持指定初始兵力与 >10000 兵 NPC（任务书 4.4）。"""
    npc = make_hero("npc", 0, force=120, command=120, speed=100,
                    max_troops=30000, initial_troops=25000)
    setup = BattleSetup(
        battle_id="t_npc",
        teams=(
            TeamSetup(team_id="A", main_hero_id="npc", heroes=(npc,)),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0),)),
        ),
    )
    report = simulate(setup, seed=3)
    snapshot = report["teams"][0]["heroes"][0]
    assert snapshot["max_troops"] == 30000
    assert snapshot["initial_troops"] == 25000
    assert report["result"]["winner_team_id"] == "A"  # 超编 NPC 兵力系数 >1，必碾压


def test_setup_validation_rejects_bad_input():
    hero = make_hero("x1", 0)
    with pytest.raises(SetupError):  # 主将不在队内
        simulate(
            BattleSetup(
                battle_id="bad",
                teams=(
                    TeamSetup(team_id="A", main_hero_id="ghost", heroes=(hero,)),
                    TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0),)),
                ),
            ),
            seed=1,
        )
    with pytest.raises(SetupError):  # 4 人超编
        heroes = tuple(make_hero(f"h{i}", i) for i in range(4))
        simulate(
            BattleSetup(
                battle_id="bad2",
                teams=(
                    TeamSetup(team_id="A", main_hero_id="h0", heroes=heroes),
                    TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0),)),
                ),
            ),
            seed=1,
        )
    with pytest.raises(SetupError):  # 未注册战法
        simulate(
            BattleSetup(
                battle_id="bad3",
                teams=(
                    TeamSetup(team_id="A", main_hero_id="a1",
                              heroes=(make_hero("a1", 0, skills=("no_such_skill",)),)),
                    TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0),)),
                ),
            ),
            seed=1,
        )


if __name__ == "__main__":
    raise SystemExit(pytest.main([__file__, "-v"]))
