from types import SimpleNamespace

import _path_bootstrap  # noqa: F401

from battlecore.domain.enums import HeroRole, DamageType, StateType
from battlecore.domain.hero import Hero
from battlecore.engine.damage_calculator import (
    ATTR_DIFF_FLOOR,
    apply_damage,
    apply_heal,
    apply_wounded_to_dead,
    calc_attr_diff,
    calc_damage,
    calc_heal,
)
from _output_helper import print_and_save_output


def attr_state(payload: dict) -> SimpleNamespace:
    return SimpleNamespace(state_type=StateType.ATTR, payload=payload)


def damage_reduce_state(payload: dict) -> SimpleNamespace:
    return SimpleNamespace(state_type=StateType.DAMAGE_REDUCE, payload=payload)


def non_const_state(payload: dict) -> SimpleNamespace:
    return SimpleNamespace(state_type=StateType.BUFF, payload=payload)


def make_hero(
    hero_id: str,
    *,
    max_troop: int,
    current_troop: int,
    force: int = 100,
    intelligence: int = 100,
    command: int = 100,
) -> Hero:
    return Hero(
        instance_id=hero_id,
        config_id=hero_id,
        name=hero_id,
        team_id="team_a",
        role=HeroRole.DEPUTY,
        position=1,
        max_troops=max_troop,
        troops=current_troop,
        force=force,
        intelligence=intelligence,
        command=command,
        speed=1,
    )


def test_damage_model_example_and_wounded_split() -> None:
    caster = make_hero("caster", max_troop=10000, current_troop=8000, force=120)
    target = make_hero("target", max_troop=10000, current_troop=10000, command=100)
    caster.states = [attr_state({"damage_up_bps": 2000})]
    target.states = [damage_reduce_state({"damage_reduce_bps": 1000}), attr_state({"vulnerable_bps": 0})]

    damage = calc_damage(
        caster,
        target,
        DamageType.PHYSICAL,
        skill_rate_bps=20000,
        random_coef_bps=10000,
    )
    result = apply_damage(target, damage)

    output = "\n".join(
        [
            "=== Damage Model Example ===",
            f"damage={damage}",
            f"actual_damage={result['actual_damage']}",
            f"dead={result['dead']}",
            f"wounded={result['wounded']}",
            f"target_current_troop={target.current_troop}",
            f"target_dead_troop={target.dead_troop}",
            f"target_wounded_troop={target.wounded_troop}",
        ]
    )
    print_and_save_output("test_damage_model_example_and_wounded_split", output)

    assert damage == 1045
    assert result == {"actual_damage": 1045, "dead": 313, "wounded": 732}
    assert target.current_troop == 8955
    assert target.dead_troop == 313
    assert target.wounded_troop == 732


def test_heal_model_only_restores_wounded_troop() -> None:
    healer = make_hero("healer", max_troop=10000, current_troop=10000, intelligence=130)
    target = make_hero("target", max_troop=10000, current_troop=8000)
    healer.states = [attr_state({"heal_up_bps": 1000})]
    target.wounded_troop = 1200
    target.dead_troop = 300

    heal = calc_heal(healer, target, heal_rate_bps=15000, random_coef_bps=10000)
    actual_heal = apply_heal(target, heal)

    output = "\n".join(
        [
            "=== Heal Model Example ===",
            f"heal={heal}",
            f"actual_heal={actual_heal}",
            f"target_current_troop={target.current_troop}",
            f"target_wounded_troop={target.wounded_troop}",
            f"target_dead_troop={target.dead_troop}",
        ]
    )
    print_and_save_output("test_heal_model_only_restores_wounded_troop", output)

    assert heal == 1073
    assert actual_heal == 1073
    assert target.current_troop == 9073
    assert target.wounded_troop == 127
    assert target.dead_troop == 300


def test_physical_one_vs_one_high_rate_can_kill_ten_thousand_troops() -> None:
    caster = make_hero("force_150_attacker", max_troop=10000, current_troop=10000, force=150)
    target = make_hero("command_50_target", max_troop=10000, current_troop=10000, command=50)
    caster.states = [attr_state({"damage_up_bps": 10000})]
    target.states = [attr_state({"vulnerable_bps": 5000})]

    damage = calc_damage(
        caster,
        target,
        DamageType.PHYSICAL,
        skill_rate_bps=100000,
        random_coef_bps=10000,
    )
    result = apply_damage(target, damage)

    output = "\n".join(
        [
            "=== 1v1 Physical One-Shot Model Demo ===",
            "attacker.max_troop=10000",
            "attacker.current_troop=10000",
            "attacker.force=150",
            "attacker.damage_up_bps=10000 (+100%)",
            "target.current_troop=10000",
            "target.command=50",
            "target.vulnerable_bps=5000 (+50%)",
            "skill_rate_bps=100000 (1000%)",
            "random_coef_bps=10000 (1.0)",
            f"actual_damage={result['actual_damage']}",
            f"dead={result['dead']}",
            f"wounded={result['wounded']}",
            f"target_current_troop={target.current_troop}",
            f"target_dead_troop={target.dead_troop}",
            f"target_wounded_troop={target.wounded_troop}",
        ]
    )
    print_and_save_output("test_physical_one_vs_one_high_rate_can_kill_ten_thousand_troops", output)

    assert damage == 10000
    assert result == {"actual_damage": 10000, "dead": 3000, "wounded": 7000}
    assert target.current_troop == 0
    assert target.dead_troop == 3000
    assert target.wounded_troop == 7000


def test_damage_calibration_targets_for_basic_and_magic() -> None:
    target = make_hero("command_50_target", max_troop=10000, current_troop=10000, command=50, intelligence=50)
    force_100_at_3000 = make_hero("force_100_3000", max_troop=10000, current_troop=3000, force=100)
    force_100_at_6000 = make_hero("force_100_6000", max_troop=10000, current_troop=6000, force=100)
    force_100_at_8000 = make_hero("force_100_8000", max_troop=10000, current_troop=8000, force=100)
    force_50_at_3000 = make_hero("force_50_3000", max_troop=10000, current_troop=3000, force=50)
    int_100_at_3000 = make_hero("int_100_3000", max_troop=10000, current_troop=3000, intelligence=100)

    physical_3000 = calc_damage(force_100_at_3000, target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)
    physical_6000 = calc_damage(force_100_at_6000, target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)
    physical_8000 = calc_damage(force_100_at_8000, target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)
    physical_force_50 = calc_damage(force_50_at_3000, target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)
    magic_3000 = calc_damage(int_100_at_3000, target, DamageType.MAGIC, 10000, random_coef_bps=10000)

    output = "\n".join(
        [
            "=== Damage Calibration Targets ===",
            f"physical_force100_troop3000_vs_command50={physical_3000}",
            f"physical_force100_troop6000_vs_command50={physical_6000}",
            f"physical_force100_troop8000_vs_command50={physical_8000}",
            f"physical_force50_troop3000_vs_command50={physical_force_50}",
            f"magic_int100_troop3000_vs_int50={magic_3000}",
        ]
    )
    print_and_save_output("test_damage_calibration_targets_for_basic_and_magic", output)

    assert physical_3000 == 458
    assert physical_6000 == 600
    assert physical_8000 == 695
    assert physical_force_50 == 226
    assert magic_3000 == 458


def test_heal_calibration_targets() -> None:
    healer_int100 = make_hero("healer_int100", max_troop=10000, current_troop=10000, intelligence=100)
    healer_int80 = make_hero("healer_int80", max_troop=10000, current_troop=10000, intelligence=80)
    target = make_hero("target", max_troop=10000, current_troop=5000)
    target.wounded_troop = 5000

    heal_int100 = calc_heal(healer_int100, target, heal_rate_bps=10000, random_coef_bps=10000)
    heal_int80 = calc_heal(healer_int80, target, heal_rate_bps=10000, random_coef_bps=10000)

    output = "\n".join(
        [
            "=== Heal Calibration Targets ===",
            f"heal_int100_rate100={heal_int100}",
            f"heal_int80_rate100={heal_int80}",
        ]
    )
    print_and_save_output("test_heal_calibration_targets", output)

    assert heal_int100 == 500
    assert heal_int80 == 400


def test_const_attr_state_changes_damage_calculation() -> None:
    target = make_hero("command_50_target", max_troop=10000, current_troop=10000, command=50)
    caster = make_hero("force_80_with_const_state", max_troop=10000, current_troop=3000, force=80)
    caster.states = [attr_state({"force_delta": 20})]

    damage = calc_damage(caster, target, DamageType.PHYSICAL, skill_rate_bps=10000, random_coef_bps=10000)
    output = "\n".join(
        [
            "=== ATTR Attr State Damage Demo ===",
            "base_force=80",
            "state.force_delta=20",
            "effective_force=100",
            f"damage={damage}",
        ]
    )
    print_and_save_output("test_const_attr_state_changes_damage_calculation", output)

    assert damage == 458


def test_non_const_state_does_not_change_damage_calculation() -> None:
    target = make_hero("command_50_target", max_troop=10000, current_troop=10000, command=50)
    caster = make_hero("force_80_with_non_const_state", max_troop=10000, current_troop=3000, force=80)
    caster.states = [non_const_state({"force_delta": 20, "damage_up_bps": 10000})]

    damage = calc_damage(caster, target, DamageType.PHYSICAL, skill_rate_bps=10000, random_coef_bps=10000)
    output = "\n".join(
        [
            "=== Non-CONST State Ignored By Model Demo ===",
            "base_force=80",
            "non_const_state.force_delta=20",
            "non_const_state.damage_up_bps=10000",
            f"damage={damage}",
        ]
    )
    print_and_save_output("test_non_const_state_does_not_change_damage_calculation", output)

    assert damage == 365


def test_troop_coef_uses_global_max_troops_not_hero_max() -> None:
    target = make_hero("target", max_troop=10000, current_troop=10000, command=100)
    full_1k = make_hero("full_1k", max_troop=1000, current_troop=1000, force=100)
    full_30k = make_hero("full_30k", max_troop=30000, current_troop=30000, force=100)

    damage_1k = calc_damage(full_1k, target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)
    damage_30k = calc_damage(full_30k, target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)

    assert damage_30k > damage_1k
    assert damage_1k == 179
    assert damage_30k == 858


def test_attr_diff_clamp_and_true_damage_ignores_target_command() -> None:
    target = make_hero("target", max_troop=10000, current_troop=10000, command=100)
    high_force = make_hero("high_force", max_troop=10000, current_troop=10000, force=200)
    low_force = make_hero("low_force", max_troop=10000, current_troop=10000, force=10)
    high_command = make_hero("high_command", max_troop=10000, current_troop=10000, command=200)

    assert calc_attr_diff(high_force, target, DamageType.PHYSICAL) == 100
    assert calc_attr_diff(low_force, high_command, DamageType.PHYSICAL) == ATTR_DIFF_FLOOR

    tough_target = make_hero("tough_target", max_troop=10000, current_troop=10000, command=150)
    attacker = make_hero("attacker", max_troop=10000, current_troop=10000, force=100)
    physical = calc_damage(attacker, tough_target, DamageType.PHYSICAL, 10000, random_coef_bps=10000)
    true_damage = calc_damage(attacker, tough_target, DamageType.TRUE, 10000, random_coef_bps=10000)
    assert true_damage > physical


def test_apply_wounded_to_dead_converts_thirty_percent_at_round_start() -> None:
    hero = make_hero("wounded_hero", max_troop=10000, current_troop=8000)
    hero.wounded_troop = 1000
    hero.dead_troop = 200
    troops_before = hero.current_troop

    result = apply_wounded_to_dead(hero)

    assert result == {
        "converted": 300,
        "old_wounded_troop": 1000,
        "new_wounded_troop": 700,
        "old_dead_troop": 200,
        "new_dead_troop": 500,
    }
    assert hero.wounded_troop == 700
    assert hero.dead_troop == 500
    assert hero.current_troop == troops_before


def test_apply_wounded_to_dead_noop_when_pool_empty() -> None:
    hero = make_hero("healthy", max_troop=10000, current_troop=10000)

    result = apply_wounded_to_dead(hero)

    assert result["converted"] == 0
    assert hero.wounded_troop == 0
    assert hero.dead_troop == 0


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
