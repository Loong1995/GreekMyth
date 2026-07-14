from __future__ import annotations

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

from battlecore.domain.enums import HeroRole

if TYPE_CHECKING:
    from battlecore.domain.skill import Skill, State


@dataclass(slots=True)
class Hero:
    instance_id: str
    config_id: str
    name: str
    team_id: str
    role: HeroRole
    position: int
    max_troops: int
    troops: int
    force: int
    intelligence: int
    command: int
    speed: int
    crit_rate_bps: int = 0
    heal_crit_rate_bps: int = 0
    hit_points_bps: int = 5000
    initial_hit_points_bps: int = 5000
    realtime_hit_rate_bps: int = 0
    skills: list[Skill] = field(default_factory=list)
    states: list[State] = field(default_factory=list)
    exited: bool = False
    exit_round: int | None = None
    exit_reason: str | None = None
    damage_dealt: int = 0
    damage_taken: int = 0
    heal_done: int = 0
    heal_taken: int = 0
    kills: int = 0
    exited_enemies: int = 0
    skill_trigger_success: int = 0
    skill_trigger_fail: int = 0
    state_trigger_success: int = 0
    state_trigger_fail: int = 0
    wounded_troop: int = 0
    dead_troop: int = 0

    @property
    def current_troop(self) -> int:
        return self.troops

    @current_troop.setter
    def current_troop(self, value: int) -> None:
        self.troops = max(0, min(int(value), self.max_troops))

    @property
    def max_troop(self) -> int:
        return self.max_troops

    def is_alive(self) -> bool:
        return self.troops > 0 and not self.exited

    def can_act(self) -> bool:
        return self.is_alive()

    def is_main_hero(self) -> bool:
        return self.role == HeroRole.MAIN

    def add_skill(self, skill: Skill) -> None:
        self.skills.append(skill)

    def add_state(self, state: State) -> None:
        self.states.append(state)

    def remove_state(self, instance_id: str) -> None:
        self.states = [state for state in self.states if state.instance_id != instance_id]

    def has_state_tag(self, tag: str) -> bool:
        return any(tag in state.tags for state in self.states)

    def get_state_payload_sum(self, key: str, default: int = 0) -> int:
        return sum(int(state.payload.get(key, default)) for state in self.states)

    def summary(self) -> dict[str, Any]:
        return {
            "hero_id": self.instance_id,
            "config_id": self.config_id,
            "name": self.name,
            "team_id": self.team_id,
            "role": self.role.value,
            "exited": self.exited,
            "exit_round": self.exit_round,
            "exit_reason": self.exit_reason,
            "troops": self.troops,
            "max_troops": self.max_troops,
            "current_troop": self.current_troop,
            "max_troop": self.max_troop,
            "wounded_troop": self.wounded_troop,
            "dead_troop": self.dead_troop,
            "damage_dealt": self.damage_dealt,
            "damage_taken": self.damage_taken,
            "heal_done": self.heal_done,
            "heal_taken": self.heal_taken,
            "kills": self.kills,
            "remaining_states": [state.instance_id for state in self.states],
        }
