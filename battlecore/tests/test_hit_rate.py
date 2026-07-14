from __future__ import annotations

import _path_bootstrap  # noqa: F401

from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import HeroRole, TargetPolicy, Timing
from battlecore.domain.hero import Hero
from battlecore.engine.battle_context import BattleContext
from battlecore.engine.hit_rate import (
    calc_hit_points_from_troops,
    calc_realtime_hit_rate_bps,
    calc_troop_hit_points_offset,
    format_realtime_hit_rate_formula,
    recalc_hit_points_from_troops,
)


def _hero(hero_id: str, team_id: str, *, position: int = 1) -> HeroConfig:
    return HeroConfig(
        hero_id=hero_id,
        name=hero_id,
        team_id=team_id,
        role=HeroRole.MAIN if position == 1 else HeroRole.DEPUTY,
        position=position,
        max_troops=1000,
        force=80,
        intelligence=80,
        command=80,
        speed=80,
        skill_ids=["basic_attack"],
    )


def test_realtime_hit_rate_normalization() -> None:
    assert calc_realtime_hit_rate_bps(5000, 15000) == 3333
    assert format_realtime_hit_rate_formula(5000, 15000, 3333) == "3333=5000/15000*10000"


def test_hit_points_from_initial_baseline_not_cumulative() -> None:
    hero = Hero(
        "h1", "h1", "H1", "team_a", HeroRole.MAIN, 1,
        max_troops=1000, troops=700, force=80, intelligence=80, command=80, speed=80,
        hit_points_bps=5000,
        initial_hit_points_bps=5000,
    )
    assert calc_troop_hit_points_offset(hero) == 900
    assert calc_hit_points_from_troops(hero) == 4100

    recalc_hit_points_from_troops(hero)
    assert hero.hit_points_bps == 4100

    # 同兵力再次重算，结果不变（非沿上次累扣）
    old, initial, offset, new = recalc_hit_points_from_troops(hero)
    assert (old, initial, offset, new) == (4100, 5000, 900, 4100)

    hero.troops = 400
    assert calc_hit_points_from_troops(hero) == 3200
    recalc_hit_points_from_troops(hero)
    assert hero.hit_points_bps == 3200

    hero.troops = 1000
    assert calc_hit_points_from_troops(hero) == 5000


def test_hit_rate_init_timing() -> None:
    config_db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="hit_rate_init",
        seed=1,
        max_rounds=1,
        config_version=config_db.version,
        team_a_heroes=[
            _hero("a1", "team_a", position=1),
            _hero("a2", "team_a", position=2),
            _hero("a3", "team_a", position=3),
        ],
        team_b_heroes=[
            _hero("b1", "team_b", position=1),
            _hero("b2", "team_b", position=2),
            _hero("b3", "team_b", position=3),
        ],
    )
    context = BattleContext.build_from_input(battle_input, config_db)
    context.run_timing(Timing.HIT_RATE_INIT)
    for hero_id in ("a1", "a2", "a3", "b1", "b2", "b3"):
        hero = context.heroes[hero_id]
        assert hero.initial_hit_points_bps == 5000
        assert hero.hit_points_bps == 5000
        assert hero.realtime_hit_rate_bps == 3333
    init_logs = [line for line in context.human_logs if "[受击率·初始化]" in line]
    assert len(init_logs) == 6
    assert "3333=5000/15000*10000" in init_logs[0]


def test_hit_rate_recalc_on_hero_exit() -> None:
    config_db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="hit_rate_exit",
        seed=3,
        max_rounds=2,
        config_version=config_db.version,
        team_a_heroes=[
            _hero("a1", "team_a", position=1),
            _hero("a2", "team_a", position=2),
            _hero("a3", "team_a", position=3),
        ],
        team_b_heroes=[_hero("b1", "team_b", position=1)],
    )
    context = BattleContext.build_from_input(battle_input, config_db)
    context.run_timing(Timing.HIT_RATE_INIT)

    exited = context.heroes["a3"]
    context.heroes["a1"].hit_points_bps = 5000
    context.heroes["a2"].hit_points_bps = 4100
    exited.hit_points_bps = 3200

    context.mark_hero_exited(exited, reason="TEST_EXIT")

    assert context.heroes["a1"].realtime_hit_rate_bps == calc_realtime_hit_rate_bps(5000, 9100)
    assert context.heroes["a2"].realtime_hit_rate_bps == calc_realtime_hit_rate_bps(4100, 9100)
    exit_logs = [line for line in context.human_logs if "[受击率·HERO_EXITED_SETTLED]" in line]
    assert exit_logs
    assert "归一分母改为 9100" in exit_logs[0]


def test_hit_rate_updates_after_damage_settlement() -> None:
    config_db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="hit_rate_damage",
        seed=2,
        max_rounds=8,
        config_version=config_db.version,
        team_a_heroes=[_hero("a1", "team_a", position=1)],
        team_b_heroes=[_hero("b1", "team_b", position=1)],
    )
    context = BattleContext.build_from_input(battle_input, config_db)
    context.run_battle()
    damage_logs = [line for line in context.human_logs if "[受击率·DAMAGE_SETTLED]" in line]
    assert damage_logs, "expected hit rate log after damage settlement"
    assert "初始5000-" in damage_logs[0]
    assert "*10000=" in damage_logs[0] or "*10000" in damage_logs[0]


def _two_team_hit_rate_context(*, seed: int = 1) -> BattleContext:
    config_db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="hit_rate_target",
        seed=seed,
        max_rounds=1,
        config_version=config_db.version,
        team_a_heroes=[_hero("a1", "team_a", position=1)],
        team_b_heroes=[
            _hero("b1", "team_b", position=1),
            _hero("b2", "team_b", position=2),
        ],
    )
    context = BattleContext.build_from_input(battle_input, config_db)
    context.run_timing(Timing.HIT_RATE_INIT)
    return context


def test_random_enemy_prefers_higher_realtime_hit_rate() -> None:
    context = _two_team_hit_rate_context()
    actor = context.heroes["a1"]
    low_rate = context.heroes["b1"]
    high_rate = context.heroes["b2"]
    low_rate.hit_points_bps = 0
    high_rate.hit_points_bps = 10000
    context._recalc_team_realtime_hit_rates("team_b")

    targets = context.select_targets(actor, TargetPolicy.RANDOM_ENEMY, 1, {})
    assert targets == [high_rate]
    select_logs = [line for line in context.human_logs if "[选人·RANDOM_ENEMY]" in line]
    assert select_logs
    assert "b2=10000" in select_logs[0]
    assert f"→ {high_rate.name}" in select_logs[0]


def test_random_enemy_hit_rate_weighting_is_deterministic() -> None:
    context_a = _two_team_hit_rate_context(seed=7)
    context_b = _two_team_hit_rate_context(seed=7)
    actor_a = context_a.heroes["a1"]
    actor_b = context_b.heroes["a1"]
    context_a.heroes["b1"].troops = 200
    context_b.heroes["b1"].troops = 200
    recalc_hit_points_from_troops(context_a.heroes["b1"])
    recalc_hit_points_from_troops(context_b.heroes["b1"])
    context_a._recalc_team_realtime_hit_rates("team_b")
    context_b._recalc_team_realtime_hit_rates("team_b")

    targets_a = context_a.select_targets(actor_a, TargetPolicy.RANDOM_ENEMY, 1, {})
    targets_b = context_b.select_targets(actor_b, TargetPolicy.RANDOM_ENEMY, 1, {})
    assert [target.instance_id for target in targets_a] == [target.instance_id for target in targets_b]
