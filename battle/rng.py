from __future__ import annotations

"""单一确定性 RNG 流（PCG 变体，纯整数）。

算法与旧 core `battlecore/rng/deterministic_rng.py` 完全一致，保证跨版本可解释。
种子外部注入；全场战斗（整个系列）共用一条流，index 单调递增。
audit=True 时记录每次调用（source/reason/roll），供 replay_dump 全量档使用；
批量模拟档关闭以省内存（v0_analysis P3）。
"""

from typing import Any

_MASK64 = 0xFFFFFFFFFFFFFFFF
_DEFAULT_STATE = 0x853C49E6748FEA9B
_MUL = 6364136223846793005
_INC = 1442695040888963407

BPS = 10000


class DeterministicRNG:
    __slots__ = ("seed", "state", "index", "audit", "history")

    def __init__(self, seed: int, *, audit: bool = False) -> None:
        self.seed = seed
        self.state = (seed & _MASK64) or _DEFAULT_STATE
        self.index = 0
        self.audit = audit
        self.history: list[dict[str, Any]] = []

    def next_u32(self, source: str, reason: str) -> int:
        self.state = (self.state * _MUL + _INC) & _MASK64
        xorshifted = (((self.state >> 18) ^ self.state) >> 27) & 0xFFFFFFFF
        rot = (self.state >> 59) & 31
        value = ((xorshifted >> rot) | (xorshifted << ((-rot) & 31))) & 0xFFFFFFFF
        self.index += 1
        if self.audit:
            self.history.append(
                {"rng_index": self.index, "value": value, "source": source, "reason": reason}
            )
        return value

    def rand_bps(self, source: str, reason: str) -> int:
        """返回 [0, 10000) 的 roll，用于概率判定（roll < rate 即成功）。"""
        return self.next_u32(source, reason) % BPS

    def rand_index(self, upper_bound: int, source: str, reason: str) -> int:
        if upper_bound <= 0:
            raise ValueError("upper_bound must be positive")
        return self.next_u32(source, reason) % upper_bound

    def rand_weighted_index(self, weights: list[int], source: str, reason: str) -> int:
        """按非负整数权重抽取下标；总权重为 0 时退化为均匀抽取。"""
        if not weights:
            raise ValueError("weights must not be empty")
        total = sum(max(weight, 0) for weight in weights)
        if total <= 0:
            return self.rand_index(len(weights), source, reason)
        roll = self.next_u32(source, reason) % total
        cumulative = 0
        for index, weight in enumerate(weights):
            cumulative += max(weight, 0)
            if roll < cumulative:
                return index
        return len(weights) - 1
