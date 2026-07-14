from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass(slots=True)
class DeterministicRNG:
    """Integer-only PCG-style RNG suitable for deterministic golden tests."""

    seed: int
    state: int = field(init=False)
    index: int = field(default=0, init=False)
    history: list[dict[str, Any]] = field(default_factory=list, init=False)

    def __post_init__(self) -> None:
        self.state = (self.seed & 0xFFFFFFFFFFFFFFFF) or 0x853C49E6748FEA9B

    def next_u32(self, source: str, reason: str) -> tuple[int, int]:
        self.state = (self.state * 6364136223846793005 + 1442695040888963407) & 0xFFFFFFFFFFFFFFFF
        xorshifted = (((self.state >> 18) ^ self.state) >> 27) & 0xFFFFFFFF
        rot = (self.state >> 59) & 31
        value = ((xorshifted >> rot) | (xorshifted << ((-rot) & 31))) & 0xFFFFFFFF
        self.index += 1
        self.history.append(
            {"rng_index": self.index, "value": value, "source": source, "reason": reason}
        )
        return self.index, value

    def rand_bps(self, source: str, reason: str) -> tuple[int, int]:
        rng_index, value = self.next_u32(source, reason)
        roll_bps = value % 10000
        self.history[-1]["roll_bps"] = roll_bps
        return rng_index, roll_bps

    def rand_index(self, upper_bound: int, source: str, reason: str) -> tuple[int, int]:
        if upper_bound <= 0:
            raise ValueError("upper_bound must be positive")
        rng_index, value = self.next_u32(source, reason)
        selected = value % upper_bound
        self.history[-1]["selected_index"] = selected
        self.history[-1]["upper_bound"] = upper_bound
        return rng_index, selected

    def rand_weighted_index(self, weights: list[int], source: str, reason: str) -> tuple[int, int]:
        if not weights:
            raise ValueError("weights must not be empty")
        total = sum(max(weight, 0) for weight in weights)
        if total <= 0:
            return self.rand_index(len(weights), source, reason)
        rng_index, value = self.next_u32(source, reason)
        roll = value % total
        cumulative = 0
        selected = len(weights) - 1
        for index, weight in enumerate(weights):
            cumulative += max(weight, 0)
            if roll < cumulative:
                selected = index
                break
        self.history[-1]["selected_index"] = selected
        self.history[-1]["weight_total"] = total
        self.history[-1]["weights"] = list(weights)
        return rng_index, selected
