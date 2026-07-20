"""Phase 4 A2 原语单测：新状态（恐惧/诅咒/必胜/清醒/格挡上限）、
连发率加成来源、约战注册表、新性格钩子（忠烈/号召/并辔）。

直接运行：python -m pytest battle/tests/test_phase4_primitives.py -q
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import statuses as st, traits as tr
from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION, PHASE_DUEL
from battle.setup import BattleSetup, TeamSetup
from battle.skills import REGISTRY as SKILLS
from battle.tests.helpers import full_3v3_setup, make_hero

import battle.tests.test_phase4_base  # noqa: F401  注册 test_p4_* 测试战法


def bare_engine(setup: BattleSetup | None = None, seed: int = 7) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(setup or full_3v3_setup(), seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


# ----------------------------------------------------------------- 新状态

def test_fear_forbids_and_reduces_damage():
    engine, anchor = bare_engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(a1, b1, st.fear(), parent_seq=anchor)
    assert engine.is_forbidden(b1, "forbid_basic")
    assert engine.is_forbidden(b1, "forbid_pursuit")
    assert not engine.is_forbidden(b1, "forbid_active")
    assert engine.modifier(b1, "damage_up_bps") == -1500


def test_curse_refreshable_not_stackable():
    engine, anchor = bare_engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    first = engine.apply_status(a1, b1, st.curse(), parent_seq=anchor)
    assert first is not None
    assert engine.modifier(b1, "intelligence_delta") == -20
    assert engine.modifier(b1, "vulnerable_bps") == 1000
    first.action_tick_count = 1
    refreshed = engine.apply_status(a1, b1, st.curse(), parent_seq=anchor)
    assert refreshed is first and first.action_tick_count == 0  # 刷新不叠层
    assert first.stacks == 1
    assert len(events_of(engine, "status_refresh")) == 1


def test_certain_crit_forces_and_exhausts():
    engine, anchor = bare_engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.grant_certain_crit(a1, 2, source=a1, parent_seq=anchor)
    assert engine.find_status("a1", "certain_crit") is not None
    for _ in range(2):
        engine.deal_damage(a1, b1, damage_type="physical", rate_bps=100, parent_seq=anchor)
        assert engine.last_damage_result["is_crit"] is True
    # 计数耗尽：载体摘除，第 3 次不再必暴（0 暴击率 → 必不暴击）
    assert engine.find_status("a1", "certain_crit") is None
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=100, parent_seq=anchor)
    assert engine.last_damage_result["is_crit"] is False
    assert any(e["payload"]["reason"] == "exhausted"
               for e in events_of(engine, "status_remove"))


def test_clear_mind_blocks_control_not_hesitation():
    engine, anchor = bare_engine()
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(b1, b1, st.clear_mind(2), parent_seq=anchor)
    assert engine.apply_status(a1, b1, st.petrify(), parent_seq=anchor) is None
    assert engine.apply_status(a1, b1, st.silence(), parent_seq=anchor) is None
    # 犹豫是 SPECIAL，不在硬控免疫范围
    assert engine.apply_status(a1, b1, st.hesitation(), parent_seq=anchor) is not None


def test_block_cap_max_charges():
    engine, anchor = bare_engine()
    a1 = engine.hero_by_id("a1")
    engine.grant_block(a1, 2, source=a1, parent_seq=anchor, max_charges=2)
    engine.grant_block(a1, 1, source=a1, parent_seq=anchor, max_charges=2)  # 已满静默
    instance = engine.find_status("a1", "block")
    assert instance.counters["block_charges"] == 2
    assert len(events_of(engine, "status_refresh")) == 0  # 封顶拒绝不发事件
    engine.grant_block(a1, 3, source=a1, parent_seq=anchor, max_charges=2)
    assert instance.counters["block_charges"] == 2


# ----------------------------------------------------------------- 连发率来源

def test_effective_burst_rate_sources():
    engine, anchor = bare_engine()
    a1 = engine.hero_by_id("a1")
    skill = SKILLS["test_p4_no_burst"]  # 自身 0 连发
    assert engine.effective_burst_rate(a1, skill) == 0
    engine.apply_status(a1, a1, st.StatusDef(
        status_id="t_burst_up", kind=st.BUFF, duration_rounds=-1,
        modifiers={"burst_rate_up_bps": 1500},
    ), parent_seq=anchor)
    assert engine.effective_burst_rate(a1, skill) == 1500


# ----------------------------------------------------------------- 约战注册表

def _duel_setup(trait_a: str = "", trait_b: str = "", force_b: int = 92) -> BattleSetup:
    from dataclasses import replace
    hero_a = replace(make_hero("a1", 0, force=99, speed=90), trait_id=trait_a)
    hero_b = replace(make_hero("b1", 0, force=force_b, speed=85), trait_id=trait_b)
    return BattleSetup(battle_id="t_duel_p4", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(hero_a,)),
        TeamSetup(team_id="B", main_hero_id="b1", heroes=(hero_b,)),
    ))


@pytest.fixture
def duel_registry():
    """临时注册约战行为，用后清理（不污染全局注册表 → 其余测试/golden 不受影响）。"""
    added: list[str] = []

    def add(trait_id: str, behavior: tr.DuelBehavior):
        tr.register_duel_behavior(trait_id, behavior)
        added.append(trait_id)

    yield add
    for trait_id in added:
        del tr.DUEL_BEHAVIORS[trait_id]


def test_duel_always_accept_skips_reject_roll(duel_registry):
    duel_registry("aoman", tr.DuelBehavior(always_accept=True))
    # 武力差 7 → 拒绝率 56%：多种子下必应战者永不拒绝
    for seed in range(12):
        engine = SeriesEngine(_duel_setup(trait_b="aoman"), seed=seed)
        engine.writer.begin_game()
        engine.writer.set_time(1, 0, PHASE_DUEL, 0)
        engine._run_duel(1)
        results = events_of(engine, "duel_result")
        assert results and results[0]["payload"]["accepted"] is True


def test_duel_force_duel_cannot_reject(duel_registry):
    duel_registry("haozhan", tr.DuelBehavior(force_duel=True, challenge_below_threshold=True))
    for seed in range(12):
        engine = SeriesEngine(_duel_setup(trait_a="haozhan"), seed=seed)
        engine.writer.begin_game()
        engine.writer.set_time(1, 0, PHASE_DUEL, 0)
        engine._run_duel(1)
        results = events_of(engine, "duel_result")
        assert results and results[0]["payload"]["accepted"] is True


def test_duel_challenge_below_threshold(duel_registry):
    duel_registry("haozhan", tr.DuelBehavior(challenge_below_threshold=True))
    # b1 武力 85 ≤ 90：常规无单挑；好战注册后可叫阵/应战
    engine = SeriesEngine(_duel_setup(force_b=85), seed=1)
    assert engine._duel_champion("B") is None
    engine2 = SeriesEngine(_duel_setup(trait_b="haozhan", force_b=85), seed=1)
    champion = engine2._duel_champion("B")
    assert champion is not None and champion.hero_id == "b1"


def test_duel_empty_registry_keeps_legacy_behavior():
    """空注册表 = 旧单挑行为（golden 保障）：有性格但未注册约战行为不改判定。"""
    engine_plain = SeriesEngine(_duel_setup(), seed=3)
    engine_trait = SeriesEngine(_duel_setup(trait_b="aoman"), seed=3)
    for engine in (engine_plain, engine_trait):
        engine.writer.begin_game()
        engine.writer.set_time(1, 0, PHASE_DUEL, 0)
        engine._run_duel(1)
    assert (events_of(engine_plain, "duel_result")[0]["payload"]
            == events_of(engine_trait, "duel_result")[0]["payload"])


# ----------------------------------------------------------------- 新性格钩子

def _trait_3v3(trait_of: dict[str, str]) -> BattleSetup:
    from dataclasses import replace
    setup = full_3v3_setup("t_p4_traits")
    teams = []
    for team in setup.teams:
        heroes = tuple(
            replace(h, trait_id=trait_of.get(h.hero_id, "")) for h in team.heroes
        )
        teams.append(TeamSetup(team_id=team.team_id, main_hero_id=team.main_hero_id,
                               heroes=heroes))
    return BattleSetup(battle_id=setup.battle_id, teams=tuple(teams),
                       metadata={"trait_rate_overrides": {"bingpei.certain": 10000}})


def test_zhonglie_stacks_burst_rate():
    from dataclasses import replace
    setup = full_3v3_setup("t_zhonglie")
    hero = replace(setup.teams[0].heroes[0], trait_id="zhonglie",
                   skills=("test_p4_no_burst",))
    setup = BattleSetup(battle_id=setup.battle_id, teams=(
        TeamSetup(team_id="A", main_hero_id="a1",
                  heroes=(hero,) + setup.teams[0].heroes[1:]),
        setup.teams[1],
    ))
    engine, _ = bare_engine(setup)
    a1 = engine.hero_by_id("a1")
    skill = SKILLS["test_p4_no_burst"]
    assert engine.effective_burst_rate(a1, skill) == 0
    engine._cast_active_skill(a1, skill, "cast")
    assert engine.effective_burst_rate(a1, skill) == 1500  # 1 层
    engine._cast_active_skill(a1, skill, "cast")
    engine._cast_active_skill(a1, skill, "cast")
    assert engine.effective_burst_rate(a1, skill) == 3000  # 封顶 2 层
    assert engine.hero_statuses("a1")[0].stacks == 2


def test_haozhao_rally_on_ally_combo():
    engine, anchor = bare_engine(_trait_3v3({"a2": "haozhao"}))
    a1 = engine.hero_by_id("a1")
    # a1 100% 连击 → 触发号召
    engine.apply_status(a1, a1, st.StatusDef(
        status_id="t_combo", kind=st.BUFF, duration_rounds=-1,
        modifiers={"combo_rate_bps": 10000},
    ), parent_seq=anchor)
    engine._perform_basic_attack(a1)
    rally = engine.find_status("a2", "haozhao_rally")
    assert rally is not None
    assert engine.effective_attr(engine.hero_by_id("a2"), "speed") == 80 + 8
    assert any(e["payload"]["effect"] == "rally"
               for e in events_of(engine, "trait_trigger"))


def test_bingpei_flag_once_per_round():
    engine, _ = bare_engine(_trait_3v3({"a2": "bingpei"}))  # 100% 概率覆盖
    a1 = engine.hero_by_id("a1")
    engine._perform_basic_attack(a1)
    assert engine.trait_flag("a2", "coord_certain")
    assert engine.trait_flag("a2", "bingpei_used")
    assert len([e for e in events_of(engine, "trait_trigger")
                if e["payload"]["effect"] == "certain"]) == 1
    engine._perform_basic_attack(a1)  # 每回合最多 1 次：不再新增台词
    assert len([e for e in events_of(engine, "trait_trigger")
                if e["payload"]["effect"] == "certain"]) == 1


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
