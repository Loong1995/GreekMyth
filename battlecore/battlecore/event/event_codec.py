from __future__ import annotations

import json

from battlecore.event.battle_event import BattleEvent


def encode_event(event: BattleEvent) -> dict:
    return event.to_dict()


def encode_event_stream(events: list[BattleEvent]) -> list[dict]:
    return [event.to_dict() for event in events]


def dumps_event_stream(events: list[BattleEvent]) -> str:
    return json.dumps(encode_event_stream(events), ensure_ascii=False, separators=(",", ":"))
