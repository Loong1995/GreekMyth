"""Phase 4 A3 海域批语义测试：三叉戟震荡上限 2、潮汐抚愈被动化、海嗣号角
衰减下限 20%、忠勇连发接线、魅音改魅惑、六首撕咬孤敌回落、魅惑队友承伤。

直接运行：python -m pytest battle/tests/test_phase4_sea.py -q
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.skills import REGISTRY as SKILLS
from battle.tests.helpers import make_hero


def bare_engine(setup: BattleSetup, seed: int = 3) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


def sea_setup(battle_id: str, a_templates: tuple, b_count: int = 3) -> BattleSetup:
    heroes_a = tuple(
        hero_setup(t, hero_id=f"a{i + 1}", position=i) for i, t in enumerate(a_templates)
    )
    heroes_b = tuple(make_hero(f"b{i + 1}", i) for i in range(b_count))
    return BattleSetup(battle_id=battle_id, teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=heroes_a),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=heroes_b),
    ))


# ----------------------------------------------------------------- 三叉戟 v4

def test_trident_shock_max_two():
    seen = False
    for seed in range(30):
        setup = sea_setup("t_trident_v4", ("poseidon",), b_count=3)
        engine, anchor = bare_engine(setup, seed=seed)
        a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
        SKILLS["poseidon_oracle"].execute(engine, a1, [a1], anchor)
        assert engine.modifier(a1, "evade_bps") == 0  # v4：移除旧版闪避 +20%
        engine.deal_damage(a1, b1, damage_type="physical", rate_bps=10000,
                           parent_seq=anchor)
        specials = [e for e in events_of(engine, "damage")
                    if e["payload"].get("damage_class") == "special"]
        assert len(specials) <= 2, "单次伤害最多 2 次震荡"
        # 震荡不落在原目标（首个必异于受击目标；后续互斥）
        assert all(e["payload"]["target_id"] != "b1" for e in specials)
        if len(specials) == 2:
            seen = True
            break
    assert seen, "30 个种子未见 2 连震荡"


# ----------------------------------------------------------------- 潮汐抚愈 v4

def test_amphitrite_tide_passive_round_hooks():
    setup = sea_setup("t_tide_v4", ("amphitrite", "poseidon"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    SKILLS["amphitrite_tide"].execute(engine, a1, [a1], anchor)
    carrier = engine.find_status("a1", "amphitrite_tide")
    assert carrier is not None
    # 回合开始：全队受疗 +10%（1 回合）
    carrier.definition.on_round_start(engine, carrier, anchor, 1)
    for hid in ("a1", "a2"):
        assert engine.modifier(engine.hero_by_id(hid), "heal_received_up_bps") == 1000
    # 回合结束：治疗兵力比例最低 2 人（治疗只回伤兵 → 打残并挂伤兵池）
    for hid, troops in (("a1", 6000), ("a2", 4000)):
        h = engine.hero_by_id(hid)
        h.troops = troops
        h.wounded_troop = h.max_troops - troops
    heals_before = len(events_of(engine, "heal"))
    carrier.definition.on_round_end(engine, carrier, anchor, 1)
    heals = events_of(engine, "heal")[heals_before:]
    assert len(heals) == 2
    assert {h["payload"]["target_id"] for h in heals} == {"a1", "a2"}


def test_amphitrite_grace_first_three_rounds():
    setup = sea_setup("t_grace_v4", ("amphitrite", "poseidon"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    SKILLS["amphitrite_grace"].execute(engine, a1, [a1], anchor)
    for hid in ("a1", "a2"):
        h = engine.hero_by_id(hid)
        h.troops = 6000
        h.wounded_troop = 4000
    carrier = engine.find_status("a1", "amphitrite_grace")
    carrier.definition.on_round_end(engine, carrier, anchor, 3)
    assert len(events_of(engine, "heal")) == 2  # 全体（2 人）
    carrier.definition.on_round_end(engine, carrier, anchor, 4)
    assert len(events_of(engine, "heal")) == 2  # 第 4 回合不再治疗


# ----------------------------------------------------------------- 海嗣号角 v4

def test_triton_horn_rate_floor_and_command():
    setup = sea_setup("t_horn_v4", ("triton",))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    horn = SKILLS["triton_horn"]
    assert horn.trigger_rate_for(engine, a1) == 10000  # 初始 100%
    for _ in range(12):
        engine.note_skill_cast(a1, "triton_horn")
    assert horn.trigger_rate_for(engine, a1) == 2000  # 最低 20%
    horn.execute(engine, a1, horn.select_targets(engine, a1), anchor)
    cmd = engine.find_status("a1", "triton_horn_command")
    assert cmd is not None and cmd.definition.modifiers["command_delta"] == 25


def test_zhongyong_burst_needs_poseidon_alive():
    setup = sea_setup("t_zy_v4", ("triton", "poseidon"))
    engine, _ = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    horn = SKILLS["triton_horn"]
    assert engine.effective_burst_rate(a1, horn) == 3000  # 波塞冬存活 +30%
    engine.hero_by_id("a2").troops = 0  # 阵亡
    assert engine.effective_burst_rate(a1, horn) == 0


# ----------------------------------------------------------------- 魅音 v4

def test_siren_song_applies_charm():
    setup = sea_setup("t_song_v4", ("siren",))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    song = SKILLS["siren_song"]
    targets = song.select_targets(engine, a1)
    song.execute(engine, a1, targets, anchor)
    tid = targets[0].hero_id
    assert engine.find_status(tid, "charm") is not None  # v4：犹豫 → 魅惑
    assert engine.find_status(tid, "hesitation") is None


def test_meihuo_ally_damage_in_bonus():
    """魅惑 v4：敌方对塞壬 -10%；对塞壬同阵营队友 +10%（塞壬存活时）。"""
    setup = sea_setup("t_meihuo_v4", ("siren", "poseidon"))
    engine, anchor = bare_engine(setup)
    b1 = engine.hero_by_id("b1")
    siren, ally = engine.hero_by_id("a1"), engine.hero_by_id("a2")
    engine.deal_damage(b1, ally, damage_type="physical", rate_bps=10000,
                       parent_seq=anchor, can_mitigate=False)
    with_siren = events_of(engine, "damage")[-1]["payload"]["amount"]
    siren.troops = 0  # 塞壬阵亡 → 加成消失
    ally.troops = ally.max_troops
    engine.deal_damage(b1, ally, damage_type="physical", rate_bps=10000,
                       parent_seq=anchor, can_mitigate=False)
    without_siren = events_of(engine, "damage")[-1]["payload"]["amount"]
    assert with_siren > without_siren, "塞壬存活时队友承伤应更高"


# ----------------------------------------------------------------- 六首撕咬 v4

def test_scylla_maw_falls_back_to_original_when_lone_enemy():
    setup = sea_setup("t_maw_v4", ("scylla",), b_count=1)
    engine, anchor = bare_engine(setup)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    SKILLS["scylla_maw"].execute(engine, a1, [b1], anchor)
    damages = events_of(engine, "damage")
    assert len(damages) == 1 and damages[0]["payload"]["target_id"] == "b1"


def test_scylla_bite_speed_buff():
    setup = sea_setup("t_bite_v4", ("scylla",))
    engine, anchor = bare_engine(setup)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    base_speed = engine.effective_attr(a1, "speed")
    SKILLS["scylla_bite"].execute(engine, a1, [b1], anchor)
    assert engine.effective_attr(a1, "speed") == base_speed + 20


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
