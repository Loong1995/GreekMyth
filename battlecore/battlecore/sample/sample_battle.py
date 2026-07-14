from __future__ import annotations

import json

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import HeroRole


def build_step1_basic_attack_input(seed: int = 20260627, max_rounds: int = 3) -> BattleInput:
    config_db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_step1_basic_attack",
        seed=seed,
        max_rounds=max_rounds,
        config_version=config_db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 10000, 120, 12, 80, 90, ["basic_attack"]),
            HeroConfig("a_deputy_1", "A-Deputy-1", "team_a", HeroRole.DEPUTY, 2, 10000, 120, 11, 75, 70, ["basic_attack"]),
            HeroConfig("a_deputy_2", "A-Deputy-2", "team_a", HeroRole.DEPUTY, 3, 10000, 120, 10, 70, 50, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 41, 200, 1, 80, ["basic_attack"]),
            HeroConfig("b_deputy_1", "B-Deputy-1", "team_b", HeroRole.DEPUTY, 2, 10000, 39, 200, 1, 60, ["basic_attack"]),
            HeroConfig("b_deputy_2", "B-Deputy-2", "team_b", HeroRole.DEPUTY, 3, 10000, 38, 200, 1, 40, ["basic_attack"]),
        ],
    )


def run_step1_basic_attack_demo() -> None:
    result = run_battle(build_step1_basic_attack_input())
    print("=== Summary ===")
    print(json.dumps(result.summary.to_dict(), ensure_ascii=False, indent=2))
    print("=== Human Logs ===")
    for line in result.human_logs:
        print(line)


if __name__ == "__main__":
    run_step1_basic_attack_demo()
