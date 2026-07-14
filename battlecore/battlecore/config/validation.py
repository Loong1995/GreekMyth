from __future__ import annotations

from battlecore.config.config_db import ConfigDB
from battlecore.config.schema import BattleInput
from battlecore.domain.enums import HeroRole


def validate_battle_input(input_data: BattleInput, config_db: ConfigDB) -> None:
    if input_data.max_rounds <= 0:
        raise ValueError("max_rounds must be positive")
    if input_data.config_version != config_db.version:
        raise ValueError(
            f"config_version mismatch: input={input_data.config_version}, db={config_db.version}"
        )

    for team_name, heroes in (
        ("team_a_heroes", input_data.team_a_heroes),
        ("team_b_heroes", input_data.team_b_heroes),
    ):
        if len(heroes) != 3:
            raise ValueError(f"{team_name} must contain exactly 3 heroes in MVP")
        main_count = sum(1 for hero in heroes if hero.role == HeroRole.MAIN)
        if main_count != 1:
            raise ValueError(f"{team_name} must contain exactly one MAIN hero")
        positions = [hero.position for hero in heroes]
        if len(set(positions)) != len(positions):
            raise ValueError(f"{team_name} has duplicated positions")
        for hero in heroes:
            if hero.max_troops <= 0:
                raise ValueError(f"{hero.hero_id} max_troops must be positive")
            for skill_id in hero.skill_ids:
                if skill_id not in config_db.skill_configs:
                    raise ValueError(f"{hero.hero_id} references missing skill {skill_id}")

    for skill in config_db.skill_configs.values():
        if not (0 <= skill.probability_bps <= 10000):
            raise ValueError(f"skill {skill.skill_id} probability out of range")
        for effect_id in skill.effect_ids:
            if effect_id not in config_db.effect_configs:
                raise ValueError(f"skill {skill.skill_id} references missing effect {effect_id}")

    for effect in config_db.effect_configs.values():
        if not (0 <= effect.probability_bps <= 10000):
            raise ValueError(f"effect {effect.effect_id} probability out of range")
        if effect.target_count <= 0:
            raise ValueError(f"effect {effect.effect_id} target_count must be positive")
