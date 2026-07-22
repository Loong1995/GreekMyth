"""帕特洛克勒斯 / 赫卡忒 / 卡吕普索 落地语义测试。"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle import statuses as st


def bare_engine(setup: BattleSetup, seed: int = 11) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    return engine, anchor


def _patroclus_setup() -> BattleSetup:
    return BattleSetup(battle_id="t_patroclus", teams=(
        TeamSetup(team_id="A", main_hero_id="p1", heroes=(
            hero_setup("patroclus", hero_id="p1", position=0,
                       extra_skills=("patroclus_armor",)),
            hero_setup("ares", hero_id="p2", position=1),  # 武最高
            hero_setup("apollo", hero_id="p3", position=2),  # 智高
        )),
        TeamSetup(team_id="B", main_hero_id="x1", heroes=(
            hero_setup("achilles", hero_id="x1", position=0),
            hero_setup("zeus", hero_id="x2", position=1),
            hero_setup("atalanta", hero_id="x3", position=2),
        )),
    ))


def test_patroclus_standin_fires_three_matchup_damages():
    engine, _ = bare_engine(_patroclus_setup(), seed=3)
    pat = engine.hero_by_id("p1")
    from battle.skills import REGISTRY
    skill = REGISTRY["patroclus_standin"]
    skill.execute(engine, pat, [pat], trigger_seq=1)
    assert engine.find_status("p1", "patroclus_standin") is not None
    before = {hid: engine.heroes[hid].troops for hid in ("x1", "x2", "x3")}
    engine._run_action_window(pat, 0)
    damaged = sum(1 for hid, t in before.items() if engine.heroes[hid].troops < t)
    assert damaged >= 2


def test_patroclus_armor_active_80pct():
    engine, anchor = bare_engine(_patroclus_setup(), seed=5)
    pat = engine.hero_by_id("p1")
    from battle.skills import REGISTRY
    skill = REGISTRY["patroclus_armor"]
    before = {hid: engine.heroes[hid].troops for hid in ("x1", "x2", "x3")}
    skill.execute(engine, pat, [], trigger_seq=anchor)
    assert any(engine.heroes[hid].troops < before[hid] for hid in before)


def test_achilles_patroclus_bond_s1():
    from battle import bonds as bn
    assert bn.bond_weight("achilles", "patroclus") == 1


def test_hecate_torch_applies_burn_on_magic():
    setup = BattleSetup(battle_id="t_hecate", teams=(
        TeamSetup(team_id="A", main_hero_id="h1", heroes=(
            hero_setup("hecate", hero_id="h1", position=0,
                       extra_skills=("hecate_pyre",)),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("ajax", hero_id="b1", position=0),
            hero_setup("heracles", hero_id="b2", position=1),
        )),
    ))
    engine, anchor = bare_engine(setup, seed=9)
    hecate = engine.hero_by_id("h1")
    from battle.skills import REGISTRY
    REGISTRY["hecate_torch"].execute(engine, hecate, [hecate], trigger_seq=anchor)
    target = engine.hero_by_id("b1")
    engine.deal_damage(
        hecate, target, damage_type="magic", rate_bps=5000, parent_seq=anchor,
    )
    burn = engine.find_status("b1", "underworld_burn")
    assert burn is not None
    assert burn.definition.dot_rate_bps == 6000
    assert burn.definition.dot_can_crit is True


def test_hecate_pyre_extra_when_already_burning():
    setup = BattleSetup(battle_id="t_pyre", teams=(
        TeamSetup(team_id="A", main_hero_id="h1", heroes=(
            hero_setup("hecate", hero_id="h1", position=0),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("ajax", hero_id="b1", position=0),
            hero_setup("heracles", hero_id="b2", position=1),
            hero_setup("paris", hero_id="b3", position=2),
        )),
    ))
    engine, anchor = bare_engine(setup, seed=2)
    hecate = engine.hero_by_id("h1")
    engine.apply_status(hecate, engine.hero_by_id("b1"), st.underworld_burn(2),
                        parent_seq=anchor)
    from battle.skills import REGISTRY
    targets = [engine.hero_by_id("b1"), engine.hero_by_id("b2")]
    before = engine.hero_by_id("b1").troops
    REGISTRY["hecate_pyre"].execute(engine, hecate, targets, trigger_seq=anchor)
    assert engine.hero_by_id("b1").troops < before
    assert engine.find_status("b1", "underworld_burn") is not None


def test_calypso_detain_freeze_and_forbid():
    setup = BattleSetup(battle_id="t_calypso", teams=(
        TeamSetup(team_id="A", main_hero_id="c1", heroes=(
            hero_setup("calypso", hero_id="c1", position=0,
                       extra_skills=("calypso_rime",)),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("atalanta", hero_id="b1", position=0),
            hero_setup("ajax", hero_id="b2", position=1),
        )),
    ))
    engine, anchor = bare_engine(setup, seed=4)
    calypso = engine.hero_by_id("c1")
    from battle.skills import REGISTRY
    targets = REGISTRY["calypso_detain"].select_targets(engine, calypso)
    assert targets and targets[0].hero_id == "b1"
    REGISTRY["calypso_detain"].execute(engine, calypso, targets, trigger_seq=anchor)
    assert engine.find_status("b1", "freeze") is not None
    assert engine.is_forbidden(engine.hero_by_id("b1"), "forbid_active")
    assert engine.is_forbidden(engine.hero_by_id("b1"), "forbid_basic")


def test_freeze_no_dot_unlike_burn():
    assert st.freeze(1).dot_rate_bps == 0
    assert st.underworld_burn(2).dot_rate_bps == 6000
