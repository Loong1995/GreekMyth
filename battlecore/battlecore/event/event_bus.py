from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field

from battlecore.event.battle_event import BattleEvent


@dataclass(slots=True)
class EventBus:
    queue: deque[BattleEvent] = field(default_factory=deque)
    stream: list[BattleEvent] = field(default_factory=list)

    def emit(self, event: BattleEvent) -> None:
        self.stream.append(event)
        self.queue.append(event)
