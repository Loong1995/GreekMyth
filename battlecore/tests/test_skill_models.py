import _path_bootstrap  # noqa: F401

from types import SimpleNamespace

from battlecore.config.skill_files import (
    build_asclepius_oracle_skill,
    build_delphi_charged_oracle_skill,
    build_gorgon_gaze_skill,
    build_pythia_woven_scheme_skill,
    build_thunder_oracle_skill,
)
from battlecore.domain.enums import HeroRole, StateType
from battlecore.domain.skill import State
from battlecore.engine.damage_calculator import (
    apply_heal_settlement_adjustments,
    calc_snake_staff_base_heal,
)
from battlecore.domain.hero import Hero


def attr_state(payload: dict) -> SimpleNamespace:
    return SimpleNamespace(state_type=StateType.ATTR, payload=payload)


def make_hero(hero_id: str, *, max_troop: int, intelligence: int) -> Hero:
    return Hero(
        instance_id=hero_id,
        config_id=hero_id,
        name=hero_id,
        team_id="team_a",
        role=HeroRole.MAIN,
        position=1,
        max_troops=max_troop,
        troops=max_troop,
        force=100,
        intelligence=intelligence,
        command=100,
        speed=100,
    )


def test_asclepius_config_matches_description() -> None:
    _, _, states = build_asclepius_oracle_skill()
    payload = states["snake_staff_protection_state"].payload
    assert payload["heal_max_troop_bps"] == 100
    assert payload["heal_source_intelligence_bps"] == 10000
    assert payload["probability_bps"] == 4000
    assert "max_trigger_per_round" not in payload


def test_thunder_config_matches_description() -> None:
    _, _, states = build_thunder_oracle_skill()
    payload = states["thunder_state"].payload
    assert payload["probability_bps"] == 7000
    assert payload["damage_coefficient_bps"] == 10000
    assert payload["max_trigger_per_round"] == 3


def test_gorgon_config_matches_description() -> None:
    skill, effects, states = build_gorgon_gaze_skill()
    assert skill.name == "戈耳工凝视"
    assert skill.probability_bps == 3500
    assert skill.effect_ids == [
        "gorgon_gaze_damage_1",
        "gorgon_gaze_ming_lock_1",
        "gorgon_gaze_damage_2",
        "gorgon_gaze_ming_lock_2",
    ]
    assert effects["gorgon_gaze_ming_lock_1"].probability_bps == 4500
    assert states["ming_lock_state"].name == "冥锁"
    assert states["ming_lock_state"].payload["forbid_basic"] is True
    assert states["ming_lock_state"].payload["forbid_active"] is True


def test_delphi_charged_oracle_config_matches_description() -> None:
    skill, effects, states = build_delphi_charged_oracle_skill()
    assert skill.name == "德尔斐蓄谕"
    assert skill.probability_bps == 5000
    assert skill.params["prepare_rounds"] == 1
    assert effects["delphi_charged_release_damage"].coefficient_bps == 30000
    assert effects["delphi_charged_release_damage"].damage_type.value == "MAGIC"
    assert states["delphi_charged_preparing_state"].name == "神谕吟诵"
    assert states["delphi_charged_preparing_state"].payload["source_skill_id"] == "delphi_charged_oracle"
    assert "active_preparing" in states["delphi_charged_preparing_state"].tags


def test_pythia_woven_scheme_config_matches_description() -> None:
    skill, effects, states = build_pythia_woven_scheme_skill()
    assert skill.name == "皮提亚筹谋"
    assert skill.params["prepare_state_config_id"] == "pythia_woven_preparing_state"
    assert effects["pythia_woven_release_damage"].coefficient_bps == 25000
    assert states["pythia_woven_preparing_state"].name == "筹谋酝酿"
    assert states["pythia_woven_preparing_state"].payload["source_skill_id"] == "pythia_woven_scheme"


def test_snake_staff_base_heal_is_skill_layer_only() -> None:
    oracle_holder = make_hero("oracle", max_troop=10000, intelligence=100)
    wounded = make_hero("wounded", max_troop=10000, intelligence=50)
    oracle_holder.states = [attr_state({"heal_up_bps": 1000})]
    wounded.states = [attr_state({"heal_received_up_bps": 500})]

    base_heal = calc_snake_staff_base_heal(
        wounded,
        oracle_holder,
        heal_max_troop_bps=100,
        heal_source_intelligence_bps=10000,
    )
    assert base_heal == 200


def test_snake_staff_settlement_applies_heal_modifiers_and_crit() -> None:
    oracle_holder = make_hero("oracle", max_troop=10000, intelligence=100)
    wounded = make_hero("wounded", max_troop=10000, intelligence=50)
    oracle_holder.states = [attr_state({"heal_up_bps": 1000})]
    wounded.states = [attr_state({"heal_received_up_bps": 500})]
    base_heal = 200

    settled = apply_heal_settlement_adjustments(
        oracle_holder,
        wounded,
        base_heal,
        crit_multiplier_bps=20000,
    )
    # 200 × 1.10 × 1.05 × 2.0 = 462
    assert settled == 462


def test_thunder_state_enforces_max_three_triggers_per_round() -> None:
    _, _, states = build_thunder_oracle_skill()
    owner = make_hero("owner", max_troop=10000, intelligence=120)
    state = State.from_config("state:1", states["thunder_state"], owner)
    assert state.max_trigger_per_round == 3

    state.trigger_count_round = 3
    check = state.enabled(SimpleNamespace(battle_finished=False))
    assert check.allowed is False
    assert check.reason == "MAX_TRIGGER_PER_ROUND"
