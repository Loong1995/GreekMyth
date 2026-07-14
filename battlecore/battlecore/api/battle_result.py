from __future__ import annotations

from dataclasses import dataclass

from battlecore.config.schema import BattleSummary
from battlecore.event.battle_event import BattleEvent


@dataclass(slots=True)
class BattleResult:
    summary: BattleSummary
    event_stream: list[BattleEvent]
    human_logs: list[str]
    replay_data: dict

    def to_dict(self) -> dict:
        return {
            "summary": self.summary.to_dict(),
            "event_stream": [event.to_dict() for event in self.event_stream],
            "human_logs": list(self.human_logs),
            "replay_data": self.replay_data,
        }
