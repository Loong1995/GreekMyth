"""Phase 4 A3 奥林匹斯批语义测试：圣盾 v4（随机反弹/控制格挡/治疗上限）、
胜利羽翼双目标、凯歌新版、月影狩猎优先后排。

直接运行：python -m pytest battle/tests/test_phase4_gods.py -q
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import statuses as st
from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.skills import REGISTRY as SKILLS
from battle.tests.helpers import full_3v3_setup, make_hero


def bare_engine(setup: BattleSetup, seed: int = 7) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


def athena_3v3(seed: int = 7) -> tuple[SeriesEngine, int]:
    setup = BattleSetup(battle_id="t_aegis_v4", teams=(
        TeamSetup(team_id="A", main_hero_id="x1", heroes=(
            hero_setup("athena", hero_id="x1", position=0),
            hero_setup("apollo", hero_id="x2", position=1),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            make_hero("b1", 0), make_hero("b2", 1),
        )),
    ))
    return bare_engine(setup, seed=seed)


def cast_aegis(engine: SeriesEngine, anchor: int) -> None:
    athena = engine.hero_by_id("x1")
    skill = SKILLS["athena_aegis"]
    skill.execute(engine, athena, skill.select_targets(engine, athena), anchor)


# ----------------------------------------------------------------- 圣盾 v4

def test_aegis_ward_blocks_first_control_only():
    engine, anchor = athena_3v3()
    cast_aegis(engine, anchor)
    athena, b1 = engine.hero_by_id("x1"), engine.hero_by_id("b1")
    ward = engine.find_status("x1", "aegis_ward")
    assert ward is not None and ward.counters["control_block_charges"] == 1
    # 「守心消耗且控制未落地」的确定局面：直接清掉反弹率干扰，单测守心本身
    # （圣盾控制反弹默认 12%）
    aegis = engine.find_status("x1", "aegis_shield")
    engine.remove_status(aegis, reason="dispelled", parent_seq=anchor)
    assert engine.apply_status(b1, athena, st.petrify(), parent_seq=anchor) is None
    assert engine.find_status("x1", "aegis_ward") is None  # 耗尽摘除
    # 第二次控制正常落地
    assert engine.apply_status(b1, athena, st.petrify(), parent_seq=anchor) is not None


def test_aegis_damage_reflect_bounces_to_random_enemy():
    """伤害反弹目标 = 持有者的敌方随机存活单位（可能不是攻击者本人）。"""
    seen_reflect = False
    for seed in range(60):
        engine, anchor = athena_3v3(seed=seed)
        cast_aegis(engine, anchor)
        b1 = engine.hero_by_id("b1")
        for holder_id in ("x1", "x2"):
            holder = engine.hero_by_id(holder_id)
            engine.deal_damage(b1, holder, damage_type="physical",
                               rate_bps=10000, parent_seq=anchor)
        for damage in events_of(engine, "damage"):
            if damage["payload"].get("mitigation") == "reflect":
                assert damage["payload"]["amount"] == 0  # 免疫：受击方 0 结算
        reflected = [e for e in events_of(engine, "damage")
                     if e["payload"].get("damage_class") == "special"
                     and e["payload"]["amount"] >= 0
                     and e["payload"]["source_id"] in ("x1", "x2")]
        if reflected:
            seen_reflect = True
            for r in reflected:
                assert r["payload"]["target_id"] in ("b1", "b2"), "反弹必须落在敌方"
            break
    assert seen_reflect, "60 个种子未见圣盾反弹"


def test_aegis_heal_capped_twice_per_round():
    engine, anchor = athena_3v3()
    cast_aegis(engine, anchor)
    b1 = engine.hero_by_id("b1")
    # 统率最低者（模板统率：阿波罗 < 雅典娜？直接取引擎口径）
    lowest_id = min(("x1", "x2"), key=lambda h: (
        engine.effective_attr(engine.hero_by_id(h), "command"),
        engine.hero_order.index(h)))
    lowest = engine.hero_by_id(lowest_id)
    heals_before = len(events_of(engine, "heal"))
    for _ in range(4):  # 每次打掉 >10% 兵力，必中阈值
        engine.deal_damage(b1, lowest, damage_type="physical",
                           fixed_amount=lowest.max_troops // 5,
                           parent_seq=anchor, can_mitigate=False)
    aegis_heals = [e for e in events_of(engine, "heal")[heals_before:]
                   if e["payload"]["source_id"] == "x1"]
    assert len(aegis_heals) == 2, f"圣盾治疗每回合上限 2，实得 {len(aegis_heals)}"


# ----------------------------------------------------------------- 胜利羽翼 v4

def test_nike_wings_two_holders_force_and_int():
    setup = BattleSetup(battle_id="t_wings_v4", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            make_hero("a1", 0, force=99, intelligence=40),   # 武力最高
            make_hero("a2", 1, force=60, intelligence=95),   # 智力最高
            make_hero("a3", 2, force=70, intelligence=70),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0),)),
    ))
    engine, anchor = bare_engine(setup)
    actor = engine.hero_by_id("a3")
    skill = SKILLS["nike_wings"]
    targets = skill.select_targets(engine, actor)
    assert [t.hero_id for t in targets] == ["a1", "a2"]
    skill.execute(engine, actor, targets, anchor)
    assert engine.find_status("a1", "nike_wings") is not None
    assert engine.find_status("a2", "nike_wings") is not None
    assert engine.find_status("a3", "nike_wings") is None


def test_nike_wings_single_copy_when_same_hero_tops_both():
    setup = BattleSetup(battle_id="t_wings_dual", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            make_hero("a1", 0, force=99, intelligence=99),
            make_hero("a2", 1, force=60, intelligence=60),
        )),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0),)),
    ))
    engine, _ = bare_engine(setup)
    actor = engine.hero_by_id("a2")
    targets = SKILLS["nike_wings"].select_targets(engine, actor)
    assert [t.hero_id for t in targets] == ["a1"], "双料最高只得一份"


# ----------------------------------------------------------------- 凯歌 v4

def test_nike_paean_first_strike_two_rounds():
    engine, anchor = bare_engine(full_3v3_setup("t_paean_v4"))
    actor = engine.hero_by_id("a1")
    skill = SKILLS["nike_paean"]
    skill.execute(engine, actor, skill.select_targets(engine, actor), anchor)
    for ally_id in ("a1", "a2", "a3"):
        fs = engine.find_status(ally_id, "first_strike")
        assert fs is not None
        assert fs.definition.duration_rounds == 2
    assert not any(
        s.status_id == "hesitation"
        for b in ("b1", "b2", "b3") for s in engine.hero_statuses(b)
    ), "凯歌不再对敌施加犹豫"


# ----------------------------------------------------------------- 月影狩猎 v4

def test_moon_hunt_prefers_backline():
    setup = BattleSetup(battle_id="t_moon_v4", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(
            hero_setup("artemis", hero_id="a1", position=0),)),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(
            make_hero("b1", 1), make_hero("b2", 5),  # b2 后排
        )),
    ))
    engine, anchor = bare_engine(setup)
    actor = engine.hero_by_id("a1")
    skill = SKILLS["artemis_hunt"]
    skill.execute(engine, actor, [actor], anchor)
    assert engine.modifier(actor, "prefer_backline_bps") == 6000
    picks = []
    for _ in range(60):
        target = engine.select_enemy_by_hit_rate(actor, reason="t_moon")
        engine._drain_target_selects()
        picks.append(target.hero_id)
    ratio = picks.count("b2") / len(picks)
    assert ratio > 0.6, f"60% 优先后排应显著偏向 b2，实测 {ratio:.2f}"


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
