from __future__ import annotations

from battlecore.config.config_db import ConfigDB
from battlecore.config.schema import BattleInput, BattleSummary
from battlecore.config.validation import validate_battle_input
from battlecore.engine.battle_context import BattleContext


class BattleEngine:
    def __init__(self, config_db: ConfigDB) -> None:
        self.config_db = config_db

    def run(self, battle_input: BattleInput) -> tuple[BattleSummary, BattleContext]:
        validate_battle_input(battle_input, self.config_db)
        context = BattleContext.build_from_input(battle_input, self.config_db)
        summary = context.run_battle()
        return summary, context
