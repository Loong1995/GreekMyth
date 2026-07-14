from __future__ import annotations

from battlecore.api.battle_result import BattleResult
from battlecore.config.config_db import ConfigDB, build_demo_config_db
from battlecore.config.schema import BattleInput
from battlecore.engine.battle_engine import BattleEngine
from battlecore.replay.replay_builder import build_replay_data


def run_battle(input: BattleInput, config_db: ConfigDB | None = None) -> BattleResult:
    db = config_db or build_demo_config_db()
    engine = BattleEngine(db)
    summary, context = engine.run(input)
    return BattleResult(
        summary=summary,
        event_stream=context.event_stream,
        human_logs=context.human_logs,
        replay_data=build_replay_data(context.event_stream, context.human_logs),
    )
