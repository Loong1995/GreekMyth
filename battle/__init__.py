"""battle - GreekMyth 战斗核心（新 core）。

对外唯一入口：simulate(battle_setup, seed) -> battle_report（纯函数，无跨场次状态）。
事件流契约见 docs/schema/battle_events.md；机制文档见 docs/mechanics/index.md。
"""

from battle.api import simulate, serialize_report
from battle.setup import BattleSetup, TeamSetup, HeroSetup
from battle.version import CORE_VERSION, SCHEMA_VERSION
from battle import skills_gods as _skills_gods  # noqa: F401  # 注册 v3.1 战法池
from battle import skills_men as _skills_men  # noqa: F401
from battle import skills_sea as _skills_sea  # noqa: F401
from battle import skills_underworld as _skills_underworld  # noqa: F401
from battle import skills_cal as _skills_cal  # noqa: F401  # 数值标定战法
from battle import traits as _traits  # noqa: F401  # 注册性格

__all__ = [
    "simulate",
    "serialize_report",
    "BattleSetup",
    "TeamSetup",
    "HeroSetup",
    "CORE_VERSION",
    "SCHEMA_VERSION",
]
