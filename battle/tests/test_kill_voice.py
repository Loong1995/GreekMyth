"""击杀台词测试：执行者视角、羁绊池优先、自杀/互杀静默。"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle import voice_lines_kill as vlk


def test_pick_kill_pool_bond_then_generic():
    key, lines = vlk.pick_kill_pool("achilles", "hector")
    assert key == "hector" and lines
    key2, lines2 = vlk.pick_kill_pool("achilles", "medusa")
    assert key2 == "generic" and lines2


def _setup_kill() -> BattleSetup:
    """阿喀琉斯（超强） vs 赫克托尔（脆皮）：必出击杀。"""
    return BattleSetup(battle_id="t_kill", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("achilles", hero_id="a1", position=0,
                       extra_skills=("achilles_thrust",)),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            hero_setup("hector", hero_id="b1", position=0, max_troops=1000,
                       initial_troops=1000),
        )),
    ))


def test_kill_line_speaker_is_killer_and_in_defeat_group():
    report = simulate(_setup_kill(), seed=5)
    events = report["games"][0]["events"]
    defeats = [e for e in events if e["type"] == "hero_defeated"]
    assert defeats
    defeat = defeats[0]
    kills = [
        e for e in events
        if e["type"] == "trait_trigger" and e["payload"].get("effect") == "kill"
    ]
    assert kills, "无击杀台词"
    line = kills[0]
    # 执行者说话（不是死者），挂在 hero_defeated 之下同组
    assert line["payload"]["hero_id"] == defeat["payload"]["killer_id"]
    assert line["payload"]["hero_id"] != defeat["payload"]["hero_id"]
    assert line["parent_seq"] == defeat["seq"]
    assert line["group_id"] == defeat["group_id"]
    # 阿喀琉斯→赫克托尔走 S1 羁绊池
    assert line["payload"]["line"] in vlk.pick_kill_pool("achilles", "hector")[1]
