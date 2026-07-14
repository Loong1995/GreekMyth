from __future__ import annotations

from battlecore.event.battle_event import BattleEvent


def build_human_logs_from_events(events: list[BattleEvent]) -> list[str]:
    logs: list[str] = []
    for event in events:
        if event.event_type.value == "DAMAGE_APPLIED":
            logs.append(
                f"R{event.round_no} {event.actor_id} -> {event.target_ids} "
                f"damage={event.payload.get('damage')} {event.payload.get('old_troops')}->{event.payload.get('new_troops')}"
            )
        elif event.event_type.value == "HERO_EXITED":
            logs.append(f"R{event.round_no} exited {event.target_ids} reason={event.payload.get('reason')}")
        elif event.event_type.value == "BATTLE_FINISHED":
            logs.append(f"finished {event.payload.get('result')} winner={event.payload.get('winner_team_id')}")
    return logs


def build_replay_data(events: list[BattleEvent], human_logs: list[str]) -> dict:
    return {
        "schema_version": 1,
        "event_stream": [event.to_dict() for event in events],
        "human_logs": list(human_logs),
    }
