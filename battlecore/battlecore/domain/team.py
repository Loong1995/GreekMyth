from __future__ import annotations

from dataclasses import dataclass


@dataclass(slots=True)
class Team:
    team_id: str
    hero_ids: list[str]
    main_hero_id: str
