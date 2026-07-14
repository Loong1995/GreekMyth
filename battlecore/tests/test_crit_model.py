from types import SimpleNamespace

import _path_bootstrap  # noqa: F401

from battlecore.domain.enums import DamageType, HeroRole, StateType
from battlecore.domain.hero import Hero
from battlecore.engine.damage_calculator import (
    CRIT_DAMAGE_MULTIPLIER_BPS,
    CRIT_HEAL_MULTIPLIER_BPS,
    apply_damage,
    apply_heal,
    calc_damage,
    calc_heal,
    get_effective_crit_rate_bps,
    get_effective_heal_crit_rate_bps,
)
from _output_helper import print_and_save_output


def attr_state(payload: dict) -> SimpleNamespace:
    return SimpleNamespace(state_type=StateType.ATTR, payload=payload)


def make_hero(
    hero_id: str,
    *,
    max_troop: int,
    current_troop: int,
    force: int = 100,
    intelligence: int = 100,
    command: int = 100,
    crit_rate_bps: int = 0,
    heal_crit_rate_bps: int = 0,
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
        crit_rate_bps=crit_rate_bps,
        heal_crit_rate_bps=heal_crit_rate_bps,
    )


def test_crit_rate_combines_hero_and_const_state() -> None:
    hero = make_hero("hero", max_troop=1000, current_troop=1000, crit_rate_bps=500, heal_crit_rate_bps=300)
    hero.states = [attr_state({"crit_rate_bps": 700, "heal_crit_rate_bps": 400})]

    assert get_effective_crit_rate_bps(hero) == 1200
    assert get_effective_heal_crit_rate_bps(hero) == 700


def test_crit_multiplier_doubles_damage_and_heal() -> None:
    caster = make_hero("caster", max_troop=10000, current_troop=8000, force=120)
    target = make_hero("target", max_troop=10000, current_troop=10000, command=100)
    healer = make_hero("healer", max_troop=10000, current_troop=10000, intelligence=100)
    heal_target = make_hero("heal_target", max_troop=10000, current_troop=8000)
    heal_target.wounded_troop = 2000

    base_damage = calc_damage(
        caster,
        target,
        DamageType.PHYSICAL,
        skill_rate_bps=10000,
        random_coef_bps=10000,
    )
    crit_damage = calc_damage(
        caster,
        target,
        DamageType.PHYSICAL,
        skill_rate_bps=10000,
        random_coef_bps=10000,
        crit_multiplier_bps=CRIT_DAMAGE_MULTIPLIER_BPS,
    )
    base_heal = calc_heal(healer, heal_target, heal_rate_bps=10000, random_coef_bps=10000)
    crit_heal = calc_heal(
        healer,
        heal_target,
        heal_rate_bps=10000,
        random_coef_bps=10000,
        crit_multiplier_bps=CRIT_HEAL_MULTIPLIER_BPS,
    )

    output = "\n".join(
        [
            "=== Crit Multiplier Demo ===",
            f"base_damage={base_damage}",
            f"crit_damage={crit_damage}",
            f"base_heal={base_heal}",
            f"crit_heal={crit_heal}",
        ]
    )
    print_and_save_output("test_crit_multiplier_doubles_damage_and_heal", output)

    assert abs(crit_damage - base_damage * 2) <= 1
    assert abs(crit_heal - base_heal * 2) <= 1
    assert apply_damage(target, crit_damage)["actual_damage"] == crit_damage
    assert apply_heal(heal_target, crit_heal) == crit_heal
