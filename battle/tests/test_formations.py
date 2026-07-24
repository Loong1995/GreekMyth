from __future__ import annotations

"""阵型系统（battle/formations.py）：雁行阵 1/2/6。

覆盖：setup 校验、逐槽位初始受击点数（40/40/20 → 残兵 32.5/32.5/10）、
整场被动状态（1/2 号位 5% 减伤、6 号位 8% 增伤）逐局重挂、
无阵型行为不变（不产生阵型状态事件）。
"""

import pytest

from battle.api import simulate
from battle.errors import SetupError
from battle.formations import YANXING_EDGE, YANXING_GUARD
from battle.heroes import build_hero_state
from battle.setup import BattleSetup, TeamSetup, validate_setup
from battle.tests.helpers import make_hero


def _yanxing_team(team_id: str, prefix: str) -> TeamSetup:
    heroes = (
        make_hero(f"{prefix}1", 1),
        make_hero(f"{prefix}2", 2),
        make_hero(f"{prefix}6", 6),
    )
    return TeamSetup(team_id=team_id, main_hero_id=f"{prefix}1",
                     heroes=heroes, formation="yanxing")


def _setup(battle_id: str = "t_formation") -> BattleSetup:
    return BattleSetup(
        battle_id=battle_id,
        teams=(_yanxing_team("A", "a"), _yanxing_team("B", "b")),
    )


def test_unknown_formation_rejected():
    team = TeamSetup(team_id="A", main_hero_id="a1",
                     heroes=(make_hero("a1", 1),), formation="no_such")
    setup = BattleSetup(battle_id="t", teams=(
        team, TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 1),))))
    with pytest.raises(SetupError):
        validate_setup(setup)


def test_position_outside_formation_rejected():
    team = TeamSetup(team_id="A", main_hero_id="a1",
                     heroes=(make_hero("a1", 3),), formation="yanxing")
    setup = BattleSetup(battle_id="t", teams=(
        team, TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 1),))))
    with pytest.raises(SetupError):
        validate_setup(setup)


def test_yanxing_initial_hit_points_and_rates():
    """满兵 40/40/20；6 号位残兵趋近 10%；1 号位残兵 32.5%。"""
    team = _yanxing_team("A", "a")
    states = [build_hero_state(h, team) for h in team.heroes]
    points = {s.position: s.initial_hit_points_bps for s in states}
    assert points == {1: 10800, 2: 10800, 6: 5400}

    full = [s.hit_points_bps() for s in states]
    assert full == [10800, 10800, 5400]
    total = sum(full)  # 27000
    assert full[0] * 100 // total == 40
    assert full[2] * 100 // total == 20

    # 6 号位兵力趋近 0（其余满兵）→ 2400/24000 = 10%
    states[2].troops = 1
    low6 = states[2].hit_points_bps()
    assert low6 == 5400 - 3000 + 1  # 兵剩 1 时 offset=3000*(max-1)//max=2999
    states[2].troops = 0
    assert states[2].hit_points_bps() == 2400
    assert states[2].hit_points_bps() * 1000 // (
        full[0] + full[1] + 2400) == 100  # 10.0%

    # 1 号位兵力趋近 0 → 7800/24000 = 32.5%
    states[2].troops = states[2].max_troops
    states[0].troops = 0
    assert states[0].hit_points_bps() == 7800
    assert states[0].hit_points_bps() * 1000 // (
        7800 + full[1] + full[2]) == 325  # 32.5%


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
    # 逐局重挂：若有第 2 局，同样带阵型状态事件
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
