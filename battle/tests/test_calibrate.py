"""标定战法冒烟：注册齐全、减伤生效、期望系数可触发。"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.cal_teams import TEAM_KINDS, build_cal_setup
from battle.engine import SeriesEngine
from battle.skills import REGISTRY
from battle.skills_cal import (
    CAL_DR_MID_BPS,
    CAL_RATE_MID_BPS,
)


def test_cal_skills_registered():
    for sid in (
        "cal_dr_low", "cal_dr_mid", "cal_dr_high",
        "cal_active_low", "cal_active_mid", "cal_active_high",
        "cal_pursuit_low", "cal_pursuit_mid", "cal_pursuit_high",
        "cal_passive_low", "cal_passive_mid", "cal_passive_high",
    ):
        assert sid in REGISTRY, sid


def test_cal_team_kinds_build():
    for kind in TEAM_KINDS:
        setup = build_cal_setup(kind, "pure", troops=5000)
        assert setup.teams[0].heroes[0].max_troops == 5000
        if kind == "pure":
            assert setup.teams[0].heroes[0].skills == ()
        else:
            assert setup.teams[0].heroes[0].skills[0].startswith("cal_dr_")


def test_cal_dr_applies_team_reduce():
    setup = build_cal_setup("regular_mid", "pure", troops=10000)
    engine = SeriesEngine(setup, seed=1)
    engine.writer.begin_game()
    engine._reset_game_state()
    engine._run_prepare_round(1)
    # A 队三人应各有减伤 25%
    for hid in ("A1", "A2", "A3"):
        assert engine.modifier(engine.heroes[hid], "damage_reduce_bps") == CAL_DR_MID_BPS


def test_cal_battle_completes():
    report = simulate(build_cal_setup("pure", "regular_low"), seed=7)
    assert report["games"]
    assert report["result"]["winner_team_id"] in {"A", "B", None}


def test_cal_attr_tiers():
    for tier, val in (("low", 100), ("mid", 200), ("high", 300)):
        setup = build_cal_setup("pure", "pure", attr_tier=tier)
        h = setup.teams[0].heroes[0]
        assert h.force == h.intelligence == h.command == h.speed == val


def test_cal_attr_per_team():
    setup = build_cal_setup(
        "pure", "regular_mid", attr_tier_a="low", attr_tier_b="high",
    )
    assert setup.teams[0].heroes[0].force == 100
    assert setup.teams[1].heroes[0].force == 300


def test_cal_active_rate():
    skill = REGISTRY["cal_active_mid"]
    assert skill.trigger_rate_bps == 10000
    assert skill.rate_bps == CAL_RATE_MID_BPS
