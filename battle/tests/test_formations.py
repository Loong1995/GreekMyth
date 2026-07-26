"""阵型系统（battle/formations.py）：六套预设 + 雁行阵加成。

覆盖：detect 精确匹配、仅按站位自动识别、雁行受击点/被动、无匹配无阵型事件。
配将禁止传入 formation 字符串。
"""

from __future__ import annotations

from battle.api import simulate
from battle.formations import (
    FORMATION_REGISTRY,
    YANXING_EDGE,
    YANXING_GUARD,
    detect_formation,
    resolve_formation,
)
from battle.heroes import build_hero_state
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero

import pytest


def _yanxing_team(team_id: str, prefix: str) -> TeamSetup:
    heroes = (
        make_hero(f"{prefix}1", 1),
        make_hero(f"{prefix}2", 2),
        make_hero(f"{prefix}6", 6),
    )
    return TeamSetup(team_id=team_id, main_hero_id=f"{prefix}1", heroes=heroes)


def _setup(battle_id: str = "t_formation") -> BattleSetup:
    return BattleSetup(
        battle_id=battle_id,
        teams=(_yanxing_team("A", "a"), _yanxing_team("B", "b")),
    )


@pytest.mark.parametrize("fid,slots", [
    ("yizi", {1, 2, 3}),
    ("zhui", {2, 4, 6}),
    ("ji", {1, 5, 6}),
    ("fangyuan", {3, 4, 5}),
    ("yanyue", {1, 3, 5}),
    ("yanxing", {1, 2, 6}),
])
def test_detect_all_presets(fid, slots):
    assert detect_formation(slots) == fid
    assert FORMATION_REGISTRY[fid].positions == frozenset(slots)


def test_detect_non_preset_empty():
    assert detect_formation([1, 2, 4]) == ""
    assert detect_formation([1]) == ""
    assert resolve_formation([1, 2, 4]) is None


def test_team_formation_property_from_positions():
    """TeamSetup.formation 只读，由站位自动识别。"""
    team = _yanxing_team("A", "a")
    assert team.formation == "yanxing"
    cone = TeamSetup(
        team_id="A", main_hero_id="a1",
        heroes=(make_hero("a1", 2), make_hero("a2", 4), make_hero("a3", 6)),
    )
    assert cone.formation == "zhui"
    misc = TeamSetup(
        team_id="A", main_hero_id="a1",
        heroes=(make_hero("a1", 1), make_hero("a2", 2), make_hero("a3", 4)),
    )
    assert misc.formation == ""


def test_auto_detect_yanxing_hit_points():
    """站位 {1,2,6} → 自动雁行点数。"""
    team = _yanxing_team("A", "a")
    states = [build_hero_state(h, team) for h in team.heroes]
    points = {s.position: s.initial_hit_points_bps for s in states}
    assert points == {1: 10800, 2: 10800, 6: 5400}


def test_yanxing_initial_hit_points_and_rates():
    """满兵 40/40/20；6 号位残兵趋近 10%；1 号位残兵 32.5%。"""
    team = _yanxing_team("A", "a")
    states = [build_hero_state(h, team) for h in team.heroes]
    points = {s.position: s.initial_hit_points_bps for s in states}
    assert points == {1: 10800, 2: 10800, 6: 5400}

    full = [s.hit_points_bps() for s in states]
    assert full == [10800, 10800, 5400]
    total = sum(full)
    assert full[0] * 100 // total == 40
    assert full[2] * 100 // total == 20

    states[2].troops = 1
    low6 = states[2].hit_points_bps()
    assert low6 == 5400 - 3000 + 1
    states[2].troops = 0
    assert states[2].hit_points_bps() == 2400
    assert states[2].hit_points_bps() * 1000 // (
        full[0] + full[1] + 2400) == 100

    states[2].troops = states[2].max_troops
    states[0].troops = 0
    assert states[0].hit_points_bps() == 7800
    assert states[0].hit_points_bps() * 1000 // (
        7800 + full[1] + full[2]) == 325


def test_yanxing_buffs_applied_each_game():
    report = simulate(_setup(), seed=20260723)
    events = report["games"][0]["events"]
    applied = [
        e["payload"]["status"] for e in events
        if e["type"] == "status_apply"
        and e["payload"]["status"]["status_id"] in (YANXING_GUARD, YANXING_EDGE)
    ]
    owners = {(s["status_id"], s["owner_id"]) for s in applied}
    for prefix in ("a", "b"):
        assert (YANXING_GUARD, f"{prefix}1") in owners
        assert (YANXING_GUARD, f"{prefix}2") in owners
        assert (YANXING_EDGE, f"{prefix}6") in owners
    for game in report["games"][1:]:
        ids = {e["payload"]["status"]["status_id"] for e in game["events"]
               if e["type"] == "status_apply"}
        assert YANXING_GUARD in ids and YANXING_EDGE in ids


def test_no_formation_emits_no_formation_status():
    from battle.tests.helpers import full_3v3_setup

    report = simulate(full_3v3_setup(), seed=20260723)
    for game in report["games"]:
        for e in game["events"]:
            if e["type"] == "status_apply":
                assert not e["payload"]["status"]["status_id"].startswith("formation_")
