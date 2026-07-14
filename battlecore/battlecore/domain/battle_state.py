from __future__ import annotations

from dataclasses import dataclass, field

from battlecore.domain.enums import BattleResultType


@dataclass(slots=True)
class BattleState:
    battle_finished: bool = False
    battle_result: BattleResultType = BattleResultType.UNFINISHED
    winner_team_id: str | None = None
    finish_reason: str | None = None
    round_no: int = 0
    human_logs: list[str] = field(default_factory=list)
