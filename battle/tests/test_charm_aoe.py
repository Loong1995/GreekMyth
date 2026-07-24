from __future__ import annotations

"""魅惑：只改选敌初步备选池；技能内部规则（互斥/指名）仍在池上执行。"""

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.setup import BattleSetup, TeamSetup
from battle.skill_common import highest_attr_unit, pick_distinct_enemies
from battle.skills import REGISTRY
from battle.statuses import charm
from battle.tests.helpers import make_hero


def _engine() -> SeriesEngine:
    setup = BattleSetup(
        battle_id="t_charm_pool",
        teams=(
            TeamSetup(
                team_id="A",
                main_hero_id="a1",
                heroes=(
                    make_hero("a1", 0, force=50, intelligence=120, speed=100,
                              skills=("zeus_bolt",)),
                    make_hero("a2", 1, force=200, speed=80),  # 武力最高（含队友）
                ),
            ),
            TeamSetup(
                team_id="B",
                main_hero_id="b1",
                heroes=(
                    make_hero("b1", 0, force=100, speed=90),
                    make_hero("b2", 1, force=70, speed=70),
                ),
            ),
        ),
    )
    return SeriesEngine(setup, seed=1)


def _charm(engine: SeriesEngine, hero_id: str = "a1") -> None:
    hero = engine.hero_by_id(hero_id)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    engine.apply_status(hero, hero, charm(), parent_seq=0)


def test_alive_enemies_pool_under_charm():
    engine = _engine()
    _charm(engine)
    a1 = engine.hero_by_id("a1")
    ids = {h.hero_id for h in engine.alive_enemies(a1)}
    assert ids == {"a2", "b1", "b2"}
    assert {h.hero_id for h in engine._alive_enemies(a1)} == {"b1", "b2"}


def test_aoe_and_named_run_on_charm_pool():
    """全体 / 最高武力都在魅惑池上执行（指名仍生效，只是池变大）。"""
    engine = _engine()
    _charm(engine)
    a1 = engine.hero_by_id("a1")
    bolt = REGISTRY["zeus_bolt"].select_targets(engine, a1)
    assert {h.hero_id for h in bolt} == {"a2", "b1", "b2"}
    # 武力最高：池内 a2=200 > b1=100 → 仍走指名规则，选中队友
    picked = highest_attr_unit(engine, a1, "force", allies=False)
    assert picked is not None and picked.hero_id == "a2"


def test_mutex_pick_two_on_charm_pool():
    """互斥抽 2 人：在魅惑池上等概率，且互不重复。"""
    engine = _engine()
    _charm(engine)
    a1 = engine.hero_by_id("a1")
    picked = pick_distinct_enemies(engine, a1, 2, "t_charm")
    assert len(picked) == 2
    assert picked[0].hero_id != picked[1].hero_id
    assert {h.hero_id for h in picked} <= {"a2", "b1", "b2"}
