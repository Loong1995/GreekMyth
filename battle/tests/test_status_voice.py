"""状态台词：仅在控制/犹豫/先攻真正改写行为时发 trait_trigger。"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.setup import BattleSetup, TeamSetup
from battle.statuses import disarm, hesitation, silence
from battle import status_voice as sv
from battle.tests.helpers import make_hero


def _engine(seed: int = 1) -> SeriesEngine:
    setup = BattleSetup(
        battle_id="status_voice",
        teams=(
            TeamSetup(
                team_id="A",
                main_hero_id="a1",
                heroes=(
                    make_hero(
                        "a1", 0, speed=100,
                        skills=("zeus_bolt",),  # 主动，便于测缄默
                    ),
                ),
            ),
            TeamSetup(
                team_id="B",
                main_hero_id="b1",
                heroes=(make_hero("b1", 0, speed=50),),
            ),
        ),
        metadata={"enable_momentum": False},
    )
    engine = SeriesEngine(setup, seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    return engine


def _all_events(engine: SeriesEngine) -> list[dict]:
    games = engine.writer.games_events()
    return games[-1] if games else []


def _status_lines(engine: SeriesEngine) -> list[dict]:
    return [
        e for e in _all_events(engine)
        if e["type"] == "trait_trigger" and e["payload"].get("trait_id") == "status"
    ]


def test_silence_voices_when_active_blocked():
    engine = _engine()
    hero = engine.heroes["a1"]
    enemy = engine.heroes["b1"]
    engine.apply_status(enemy, hero, silence(2), parent_seq=0)
    before = len(_status_lines(engine))
    engine._run_action_window(hero, 0)
    lines = _status_lines(engine)[before:]
    assert len(lines) == 1
    assert lines[0]["payload"]["effect"] == "silence"
    assert lines[0]["payload"]["line"] in sv.LINES["silence"]


def test_hesitation_voices_when_delayed():
    engine = _engine(seed=99)
    hero = engine.heroes["a1"]
    enemy = engine.heroes["b1"]
    engine.apply_status(
        enemy, hero, hesitation(delay_rate_bps=10000, duration_rounds=2), parent_seq=0
    )
    before = len(_status_lines(engine))
    engine._run_action_window(hero, 0)
    lines = _status_lines(engine)[before:]
    assert any(e["payload"]["effect"] == "hesitation" for e in lines)
    assert any(
        e["type"] == "skill_trigger" and e["payload"].get("kind") == "delayed"
        for e in _all_events(engine)
    )


def test_line_rotation_deterministic():
    engine = _engine()
    hero = engine.heroes["a1"]
    for _ in range(3):
        sv.emit_status_voice(engine, hero, "disarm", parent_seq=0)
    lines = [e["payload"]["line"] for e in _status_lines(engine)]
    assert lines == list(sv.LINES["disarm"])
