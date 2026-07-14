import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import DamageType, EventType, HeroRole, TargetPolicy, Timing
from battlecore.engine.battle_context import BattleContext
from _output_helper import format_battle_result, print_and_save_output


def build_exit_presence_input(seed: int = 1, max_rounds: int = 5) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="hero_exit_presence_test",
        seed=seed,
        max_rounds=max_rounds,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 10000, 80, 70, 40, 90, ["gorgon_gaze"]),
            HeroConfig(
                "a_d1",
                "A-D1",
                "team_a",
                HeroRole.DEPUTY,
                2,
                1000,
                75,
                60,
                40,
                70,
                ["gorgon_gaze", "delphi_revelation", "thunder_oracle"],
            ),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 10000, 70, 60, 40, 60, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 40, 80, ["basic_attack"]),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 10000, 70, 60, 40, 70, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 10000, 70, 60, 40, 60, ["basic_attack"]),
        ],
    )


def build_exit_presence_context() -> BattleContext:
    db = build_demo_config_db()
    context = BattleContext.build_from_input(build_exit_presence_input(max_rounds=2), db)
    context.rebuild_indexes()
    context.current_timing = Timing.ROUND_START
    return context


def format_context_logs(title: str, context: BattleContext, *, summary_lines: list[str] | None = None) -> str:
    lines = [f"=== {title} ===", ""]
    if summary_lines:
        lines.extend(summary_lines)
        lines.append("")
    lines.append("=== Human Logs ===")
    lines.extend(context.human_logs or ["<no human logs>"])
    return "\n".join(lines)


def test_exited_hero_removes_sourced_and_owned_states() -> None:
    context = build_exit_presence_context()
    source = context.heroes["a_main"]
    ally = context.heroes["a_d1"]
    enemy = context.heroes["b_main"]

    context.add_state(source, ally, "divine_revelation_state")
    context.add_state(source, enemy, "ming_lock_state", duration_override=1)
    context.add_state(source, source, "divine_revelation_state")

    assert any(state.source_actor_id == source.instance_id for state in context.state_instances.values())

    context.mark_hero_exited(source, reason="TEST_EXIT")

    assert not any(state.source_actor_id == source.instance_id for state in context.state_instances.values())
    assert not any(state.owner.instance_id == source.instance_id for state in context.state_instances.values())
    assert not ally.states
    assert len(enemy.states) == 0

    print_and_save_output(
        "test_exited_hero_removes_sourced_and_owned_states",
        format_context_logs(
            "Exited Hero Removes Sourced And Owned States",
            context,
            summary_lines=[
                f"exited_hero={source.name}",
                f"ally_remaining_states={len(ally.states)}",
                f"enemy_remaining_states={len(enemy.states)}",
                f"remaining_state_instances={len(context.state_instances)}",
            ],
        ),
    )


def test_same_name_main_exit_does_not_remove_other_team_sourced_states() -> None:
    """双方同名主将：一方阵亡时不得按 source_skill_id 误删他队同源技能状态。"""
    context = build_exit_presence_context()
    a_zeus = context.heroes["a_main"]
    b_zeus = context.heroes["b_main"]
    b_ally = context.heroes["b_d1"]

    context.add_state(a_zeus, a_zeus, "thunder_state")
    context.add_state(b_zeus, b_zeus, "thunder_state")
    context.add_state(b_zeus, b_ally, "thunder_state")

    assert sum(1 for s in context.state_instances.values() if s.config_id == "thunder_state") == 3

    context.mark_hero_exited(a_zeus, reason="TEST_EXIT")

    remaining_thunder = [
        s for s in context.state_instances.values() if s.config_id == "thunder_state"
    ]
    assert len(remaining_thunder) == 2
    assert all(s.source_actor_id == b_zeus.instance_id for s in remaining_thunder)
    assert {s.owner.instance_id for s in remaining_thunder} == {b_zeus.instance_id, b_ally.instance_id}


def test_exited_hero_skips_round_wounded_conversion() -> None:
    context = build_exit_presence_context()
    hero = context.heroes["a_main"]
    hero.wounded_troop = 1000
    hero.dead_troop = 100

    context.mark_hero_exited(hero, reason="TEST_EXIT")
    context._apply_wounded_to_dead_at_round_start()

    assert hero.wounded_troop == 1000
    assert hero.dead_troop == 100

    print_and_save_output(
        "test_exited_hero_skips_round_wounded_conversion",
        format_context_logs(
            "Exited Hero Skips Round Wounded Conversion",
            context,
            summary_lines=[
                f"exited_hero={hero.name}",
                f"wounded_troop={hero.wounded_troop}",
                f"dead_troop={hero.dead_troop}",
            ],
        ),
    )


def test_exited_hero_removed_from_timing_and_spy_indexes() -> None:
    context = build_exit_presence_context()
    hero = context.heroes["a_main"]
    ally = context.heroes["a_d1"]

    context.add_state(hero, hero, "thunder_state")
    context.add_state(hero, ally, "divine_revelation_state")
    context.rebuild_indexes()

    hero_skill_ids = {
        sid for sid in sum(context.skill_timing_index.values(), [])
        if context.skill_instances[sid].owner.instance_id == hero.instance_id
    }
    assert hero_skill_ids

    context.mark_hero_exited(hero, reason="TEST_EXIT")

    indexed_skills = sum(context.skill_timing_index.values(), [])
    assert not any(
        context.skill_instances[sid].owner.instance_id == hero.instance_id for sid in indexed_skills
    )
    spy_states = sum(context.spy_state_event_index.values(), [])
    assert not any(
        context.state_instances[sid].owner.instance_id == hero.instance_id for sid in spy_states
    )
    assert not any(
        context.state_instances[sid].source_actor_id == hero.instance_id
        for sid in context.state_instances
    )

    check = hero.skills[0].can_trigger_at(context, Timing.ACTIVE)
    assert not check.allowed
    assert check.reason in ("OWNER_EXITED", "DISABLED")


def test_exited_hero_cannot_be_selected_as_target() -> None:
    context = build_exit_presence_context()
    actor = context.heroes["b_main"]
    victim = context.heroes["a_main"]
    context.mark_hero_exited(victim, reason="TEST_EXIT")

    targets = context.select_targets(actor, TargetPolicy.RANDOM_ENEMY, 1, {})
    assert all(target.instance_id != victim.instance_id for target in targets)


def test_exited_hero_skills_are_disabled() -> None:
    context = build_exit_presence_context()
    hero = context.heroes["a_main"]
    owned_skills = [skill for skill in context.skill_instances.values() if skill.owner.instance_id == hero.instance_id]

    context.mark_hero_exited(hero, reason="TEST_EXIT")

    assert owned_skills
    assert all(not skill.enabled_flag for skill in owned_skills)

    print_and_save_output(
        "test_exited_hero_skills_are_disabled",
        format_context_logs(
            "Exited Hero Skills Are Disabled",
            context,
            summary_lines=[
                f"exited_hero={hero.name}",
                f"owned_skill_count={len(owned_skills)}",
                f"all_disabled={all(not skill.enabled_flag for skill in owned_skills)}",
            ],
        ),
    )


def test_hero_exit_presence_battle() -> None:
    db = build_demo_config_db()
    result = run_battle(build_exit_presence_input(), db)
    exited_heroes = [hero for hero in result.summary.hero_summaries if hero["exited"]]

    print_and_save_output(
        "test_hero_exit_presence_battle",
        format_battle_result("Hero Exit Presence Battle", result),
    )

    assert exited_heroes


def test_lethal_basic_pursuit_reports_invalid_when_source_target_exited() -> None:
    db = build_demo_config_db()
    context = BattleContext.build_from_input(
        BattleInput(
            battle_id="lethal_basic_pursuit_cache",
            seed=1,
            max_rounds=1,
            config_version=db.version,
            team_a_heroes=[
                HeroConfig(
                    "a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 10000,
                    120, 80, 80, 90, ["basic_attack", "pursuit_strike"],
                ),
            ],
            team_b_heroes=[
                HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 70, 80, ["basic_attack"]),
                HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 100, 80, 70, 70, 70, ["basic_attack"]),
            ],
        ),
        db,
    )
    context.rebuild_indexes()
    context.current_timing = Timing.BASIC
    context.round_no = 1
    attacker = context.heroes["a_main"]
    victim = context.heroes["b_d1"]
    pursuit = next(skill for skill in attacker.skills if skill.config_id == "pursuit_strike")

    context.mark_hero_exited(victim, reason="TROOPS_ZERO", killer=attacker)

    settled = context.emit_event(
        EventType.DAMAGE_SETTLED,
        actor_id=attacker.instance_id,
        target_ids=[victim.instance_id],
        skill_id="basic_attack",
        payload={"damage": 100},
    )
    context.try_trigger_triggerable(pursuit, Timing.BASIC, settled)

    invalid_logs = [
        line for line in context.human_logs
        if "突击" in line and "未执行：目标无效" in line and "B-D1" in line
    ]
    assert invalid_logs


def test_lethal_damage_skips_thunder_follow_up_on_exited_target() -> None:
    context = build_exit_presence_context()
    context.current_timing = Timing.BASIC
    attacker = context.heroes["a_main"]
    victim = context.heroes["b_main"]
    victim.troops = 100

    context.add_state(attacker, attacker, "thunder_state")
    context.apply_damage(attacker, victim, 100, DamageType.PHYSICAL)

    assert victim.exited
    skip_logs = [line for line in context.human_logs if "雷霆" in line and "未执行：目标无效" in line]
    assert skip_logs

    print_and_save_output(
        "test_lethal_damage_skips_thunder_follow_up_on_exited_target",
        format_context_logs(
            "Lethal Damage Skips Thunder On Exited Target",
            context,
            summary_lines=[
                f"victim_exited={victim.exited}",
                f"thunder_skip_logs={len(skip_logs)}",
            ],
        ),
    )


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
