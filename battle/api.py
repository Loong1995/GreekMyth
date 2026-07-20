from __future__ import annotations

"""battle 对外唯一入口：simulate(battle_setup, seed) -> battle_report（纯函数）。"""

from typing import Any

from battle.engine import SeriesEngine
from battle.report import build_report, serialize_report
from battle.setup import BattleSetup, validate_setup
from battle.tactics import validate_tactics

__all__ = ["simulate", "serialize_report"]


def simulate(battle_setup: BattleSetup, seed: int, *, audit: bool = False) -> dict[str, Any]:
    """运行一个完整系列（1~7 局），返回符合冻结 Schema 的 battle_report dict。

    确定性保证：同 (battle_setup, seed, core_version) 输入，任何机器任何时间
    serialize_report(simulate(...)) 逐字节相同。core 不持有跨场次状态。
    audit=True 时额外记录 RNG 调用史（不进战报，供 replay_dump 全量档）。
    """
    validate_setup(battle_setup)
    validate_tactics(battle_setup)  # P4-C：经理人战术配置校验（无配置零开销）
    engine = SeriesEngine(battle_setup, seed, audit=audit)
    series = engine.run()
    return build_report(battle_setup, seed, engine, series)
