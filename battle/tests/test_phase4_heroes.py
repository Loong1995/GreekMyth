"""Phase 4 A3 英雄批语义测试：阿喀琉斯之怒链式、镜盾疾袭格挡、决死猛攻叠系数、
英雄远征连击、金羊号令、双子协战协击（含并辔必成功）、坚壁双目标。

直接运行：python -m pytest battle/tests/test_phase4_heroes.py -q
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
from battle.tests.helpers import make_hero


def bare_engine(setup: BattleSetup, seed: int = 3) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


def duo_vs_duo(battle_id: str, a_templates: tuple, b_count: int = 2) -> BattleSetup:
    heroes_a = tuple(
        hero_setup(t, hero_id=f"a{i + 1}", position=i) for i, t in enumerate(a_templates)
    )
    heroes_b = tuple(make_hero(f"b{i + 1}", i) for i in range(b_count))
    return BattleSetup(battle_id=battle_id, teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=heroes_a),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=heroes_b),
    ))


# ----------------------------------------------------------------- 阿喀琉斯之怒 v4

def test_achilles_fury_chains_capped_seven():
    setup = duo_vs_duo("t_fury_v4", ("achilles",))
    engine, anchor = bare_engine(setup)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    SKILLS["achilles_wrath"].execute(engine, a1, [a1], anchor)
    # 100% 暴击 → 追伤可暴击链式延续，回合封顶 7
    engine.apply_status(a1, a1, st.StatusDef(
        status_id="t_crit", kind=st.BUFF, duration_rounds=-1,
        modifiers={"crit_rate_bps": 10000},
    ), parent_seq=anchor)
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=100,
                       parent_seq=anchor)
    status = engine.find_status("a1", "achilles_wrath")
    fury_count = status.round_counters.get("fury", 0)
    assert 1 <= fury_count <= 7, "追伤应触发且每回合≤7 次"
    assert len(events_of(engine, "status_tick")) >= fury_count


def _aoman_fury_engine(*, a_troops: int, b_troops: int, seed: int = 3):
    setup = BattleSetup(
        battle_id="t_aoman_pierce",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                hero_setup("achilles", hero_id="a1", position=0),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0),
            )),
        ),
        metadata={"trait_rate_overrides": {"aoman.pride": 10000}},
    )
    engine, anchor = bare_engine(setup, seed=seed)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    SKILLS["achilles_wrath"].execute(engine, a1, [a1], anchor)
    engine.apply_status(a1, a1, st.StatusDef(
        status_id="t_crit", kind=st.BUFF, duration_rounds=-1,
        modifiers={"crit_rate_bps": 10000},
    ), parent_seq=anchor)
    a1.troops, b1.troops = a_troops, b_troops
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=5000,
                       parent_seq=anchor)
    return engine


def test_aoman_pierce_only_when_target_ratio_higher():
    """傲慢贯穿：仅目标残兵比例 > 自身时才有机会触发，非每次追伤必播。"""
    # 目标更残（g1r3 同类）：不得播 pierce
    low = _aoman_fury_engine(a_troops=9000, b_troops=5000)
    assert not any(
        e["payload"].get("effect") == "pierce"
        for e in events_of(low, "trait_trigger")
    ), "残兵比例不高于自身时不得播贯穿"
    assert events_of(low, "status_tick"), "应有追伤以证明场景有效"

    # 目标更满 + 100% 傲慢判定 → 必播 pierce
    high = _aoman_fury_engine(a_troops=4000, b_troops=9000, seed=5)
    pierce = [e for e in events_of(high, "trait_trigger")
              if e["payload"].get("effect") == "pierce"]
    assert pierce, "目标残兵比例更高且判定成功应播贯穿"


# ----------------------------------------------------------------- 镜盾疾袭 v4

def test_perseus_relics_grants_block_capped():
    setup = duo_vs_duo("t_relics_v4", ("perseus",))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    skill = SKILLS["perseus_relics"]
    for _ in range(4):  # 多次施放：段数 1~2，格挡最多持有 2 层
        skill.execute(engine, a1, skill.select_targets(engine, a1), anchor)
    block = engine.find_status("a1", "block")
    assert block is not None
    assert block.counters["block_charges"] <= 2
    assert block.definition.duration_rounds == 2  # 限时格挡


def test_jiebao_burst_bonus_counts_gods_allies():
    setup = duo_vs_duo("t_jiebao_v4", ("perseus", "athena"))
    engine, _ = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    relics = SKILLS["perseus_relics"]
    assert engine.effective_burst_rate(a1, relics) == 1500  # 1 名神友军
    assert engine.effective_burst_rate(a1, SKILLS["perseus_flash"]) == 0  # 仅自带


# ----------------------------------------------------------------- 决死猛攻

def test_hector_assault_rate_stacks():
    setup = duo_vs_duo("t_assault_v4", ("hector",))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    skill = SKILLS["hector_assault"]
    for _ in range(7):  # 超过 5 次上限
        skill.execute(engine, a1, skill.select_targets(engine, a1), anchor)
    carrier = engine.find_status("a1", "hector_assault_stack")
    assert carrier.counters["assault"] == 5  # 封顶 5 次
    # 第 7 次施放使用第 6 次后的系数：18000 + 2000×5 = 28000
    last_damage = [e for e in events_of(engine, "damage")][-1]
    assert last_damage["payload"]["source_id"] == "a1"


# ----------------------------------------------------------------- 战吼连发不重新准备

def test_hector_warcry_burst_releases_without_reprepare():
    """准备型主动的连发：release 后直接按连发率再次释放，不再进入准备。"""
    setup = duo_vs_duo("t_warcry_burst", ("hector",), b_count=2)
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    # 连发率拉满 100%：必连发直到 MAX_CASTS_PER_WINDOW（7 次）
    engine.apply_status(a1, a1, st.StatusDef(
        status_id="t_burst_full", kind=st.BUFF, duration_rounds=-1,
        modifiers={"burst_rate_up_bps": 10000},
    ), parent_seq=anchor)
    # 模拟准备完成：登记 remaining=1 后走 _settle_preparing 释放
    engine._preparing["a1"] = {"skill_id": "hector_warcry", "remaining": 1}
    engine._settle_preparing(a1)
    triggers = [e for e in events_of(engine, "skill_trigger")
                if e["payload"]["skill_id"] == "hector_warcry"]
    releases = [e for e in triggers if e["payload"]["kind"] == "release"]
    prepares = [e for e in triggers if e["payload"]["kind"] == "prepare"]
    assert len(releases) == 7, "100% 连发应打满窗口上限 7 次释放"
    assert not prepares, "连发不得重新进入准备"
    assert "a1" not in engine._preparing
    burst_nos = [e["payload"].get("burst_no") for e in releases]
    assert burst_nos == [None, 2, 3, 4, 5, 6, 7]


# ----------------------------------------------------------------- 英雄远征/金羊号令

def test_jason_expedition_clear_mind_and_combo():
    setup = duo_vs_duo("t_exped_v4", ("jason", "achilles"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    SKILLS["jason_expedition"].execute(engine, a1, [a1], anchor)
    # 武力最高 = 阿喀琉斯（a2）
    assert engine.find_status("a2", "clear_mind") is not None
    assert engine.find_status("a1", "jason_expedition") is not None
    # 回合开始钩子：手动分发一次验证连击 buff 落到武力最高者
    carrier = engine.find_status("a1", "jason_expedition")
    carrier.definition.on_round_start(engine, carrier, anchor, 1)
    combo = engine.find_status("a2", "jason_expedition_combo")
    assert combo is not None
    assert engine.modifier(engine.hero_by_id("a2"), "combo_rate_bps") == 3500


def test_jason_command_extra_damage_when_has_combo():
    setup = duo_vs_duo("t_command_v4", ("jason", "achilles", "ajax"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    a2 = engine.hero_by_id("a2")
    # 先给 a2 挂连击率 → 号令后 a2 额外得增伤，另一目标不额外
    engine.apply_status(a2, a2, st.StatusDef(
        status_id="t_combo", kind=st.BUFF, duration_rounds=-1,
        modifiers={"combo_rate_bps": 1000},
    ), parent_seq=anchor)
    skill = SKILLS["jason_command"]
    targets = skill.select_targets(engine, a1)
    # 武力前二：阿喀琉斯(248) > 大埃阿斯(193) > 伊阿宋(175)
    assert [t.hero_id for t in targets] == ["a2", "a3"]
    skill.execute(engine, a1, targets, anchor)
    assert engine.find_status("a2", "jason_command_combo") is not None
    assert engine.find_status("a2", "jason_command_damage") is not None  # 已有连击率
    assert engine.find_status("a3", "jason_command_combo") is not None
    assert engine.find_status("a3", "jason_command_damage") is None  # 无连击率不加成


# ----------------------------------------------------------------- 双子协战

def _castor_engine(seed: int = 3, chase: bool = False):
    setup = duo_vs_duo("t_twin_v4", ("achilles", "castor"))
    if chase:
        heroes = list(setup.teams[0].heroes)
        from dataclasses import replace
        heroes[1] = replace(heroes[1], skills=("castor_twin", "castor_chase"))
        setup = BattleSetup(battle_id=setup.battle_id, teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=tuple(heroes)),
            setup.teams[1],
        ))
    engine, anchor = bare_engine(setup, seed=seed)
    castor = engine.hero_by_id("a2")
    SKILLS["castor_twin"].execute(engine, castor, [castor], anchor)
    return engine, anchor


def test_castor_twin_coordinated_max_two_per_round():
    seen = False
    for seed in range(20):
        engine, _ = _castor_engine(seed=seed)
        a1 = engine.hero_by_id("a1")
        for _ in range(5):
            if engine.game_over():
                break
            engine._perform_basic_attack(a1)
        coords = [e for e in events_of(engine, "normal_attack")
                  if e["payload"].get("kind") == "coordinated"]
        assert len(coords) <= 2, "双子协战每回合最多 2 次"
        if coords:
            seen = True
            assert all(e["payload"]["actor_id"] == "a2" for e in coords)
            break
    assert seen, "20 个种子未见协击"


def test_bingpei_certain_flag_forces_coordination():
    engine, anchor = _castor_engine(seed=0)
    a1 = engine.hero_by_id("a1")
    engine.set_trait_flag("a2", "coord_certain")  # 并辔旗标：判定必成功、不 roll
    engine._perform_basic_attack(a1)
    coords = [e for e in events_of(engine, "normal_attack")
              if e["payload"].get("kind") == "coordinated"]
    assert len(coords) >= 1
    assert not engine.trait_flag("a2", "coord_certain"), "旗标消费后清除"


# ----------------------------------------------------------------- 坚壁 v4

def test_ajax_bulwark_two_lowest_ratio_allies():
    setup = duo_vs_duo("t_bulwark_v4", ("ajax", "achilles", "paris"))
    engine, anchor = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    engine.hero_by_id("a2").troops = 3000   # 最低
    engine.hero_by_id("a3").troops = 6000   # 次低
    skill = SKILLS["ajax_bulwark"]
    targets = skill.select_targets(engine, a1)
    assert [t.hero_id for t in targets] == ["a2", "a3"]
    skill.execute(engine, a1, targets, anchor)
    for hid in ("a2", "a3"):
        block = engine.find_status(hid, "block")
        assert block is not None and block.counters["block_charges"] == 1
        assert engine.find_status(hid, "ajax_bulwark_command") is not None


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
