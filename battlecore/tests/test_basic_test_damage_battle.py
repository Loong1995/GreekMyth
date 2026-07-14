import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_basic_test_damage_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import HeroRole
from _output_helper import format_battle_result, print_and_save_output

_CALIBRATION_TROOPS = 10000
_CALIBRATION_STATS = dict(force=100, intelligence=100, command=100, speed=90)


def _hero(
    hero_id: str,
    name: str,
    team_id: str,
    role: HeroRole,
    position: int,
    skill_ids: list[str],
) -> HeroConfig:
    return HeroConfig(
        hero_id=hero_id,
        name=name,
        team_id=team_id,
        role=role,
        position=position,
        max_troops=_CALIBRATION_TROOPS,
        skill_ids=skill_ids,
        **_CALIBRATION_STATS,
    )


def build_basic_test_damage_input(seed: int = 42) -> BattleInput:
    """3v3 标定战：双方属性相同 10000 兵力，每人携带普攻/主动/准备/追击/控制，主将各带一个神谕。"""
    db = build_basic_test_damage_config_db()
    common_tail = ["gorgon_gaze"]
    return BattleInput(
        battle_id="battle_basic_test_damage",
        seed=seed,
        max_rounds=8,
        config_version=db.version,
        team_a_heroes=[
            _hero(
                "a_main",
                "A-阿喀琉斯",
                "team_a",
                HeroRole.MAIN,
                1,
                [
                    "basic_attack",
                    "theseus_labyrinth_charge",
                    "atalanta_hunting_arrow",
                    
                    *common_tail,
                ],
            ),
            _hero(
                "a_d1",
                "A-阿瑞斯",
                "team_a",
                HeroRole.DEPUTY,
                2,
                [
                    "basic_attack",
                    "ares_blood_axe",
                    #"bellerophon_pegasus_dive",
                    "delphi_revelation",
                    *common_tail,
                ],
            ),
            _hero(
                "a_d2",
                "A-雅典娜",
                "team_a",
                HeroRole.DEPUTY,
                3,
                [
                    "basic_attack",
                    "athena_tactical_decree",
                    "delphi_oracle_chant",
                    "hermes_shadow_message",
                    *common_tail,
                ],
            ),
        ],
        team_b_heroes=[
            _hero(
                "b_main",
                "B-赫拉克勒斯",
                "team_b",
                HeroRole.MAIN,
                1,
                [
                    "basic_attack",
    
                    "poseidon_abyssal_tide",
                    "diomedes_god_wound",
                    "thunder_oracle",
                    *common_tail,
                ],
            ),
            _hero(
                "b_d1",
                "B-阿波罗",
                "team_b",
                HeroRole.DEPUTY,
                2,
                [
                    "basic_attack",
                    #"apollo_solar_arrow",
                    "theseus_labyrinth_charge",
                    "delphi_revelation",
                    *common_tail,
                ],
            ),
            _hero(
                "b_d2",
                "B-哈迪斯",
                "team_b",
                HeroRole.DEPUTY,
                3,
                [
                    "basic_attack",
                    #"hades_styx_sentence",
                    #"bellerophon_pegasus_dive",
                    "hades_underworld_dominion",
                    *common_tail,
                ],
            ),
        ],
    )


def test_basic_test_damage_battle() -> None:
    db = build_basic_test_damage_config_db()
    battle_input = build_basic_test_damage_input()
    result = run_battle(battle_input, config_db=db)
    print_and_save_output(
        "test_basic_test_damage_battle",
        format_battle_result("BASIC_TEST_DAMAGE Calibration Battle", result),
    )
    assert result.summary.winner_team_id in {"team_a", "team_b"}
    assert len(result.human_logs) > 0


if __name__ == "__main__":
    db = build_basic_test_damage_config_db()
    result = run_battle(build_basic_test_damage_input(), config_db=db)
    print_and_save_output(
        "test_basic_test_damage_battle",
        format_battle_result("BASIC_TEST_DAMAGE Calibration Battle", result),
    )
