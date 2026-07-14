from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any

from battlecore.config.schema import to_jsonable
from battlecore.domain.enums import EventType, Timing


@dataclass(slots=True)
class BattleEvent:
    event_id: int
    event_type: EventType
    round_no: int
    timing: Timing | None
    chain_depth: int
    rng_index: int | None
    source_type: str | None
    source_id: str | None
    actor_id: str | None
    target_ids: list[str] = field(default_factory=list)
    skill_id: str | None = None
    effect_id: str | None = None
    state_instance_id: str | None = None
    payload: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        return to_jsonable(asdict(self))
