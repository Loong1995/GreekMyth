"""台词选词随机：seed 派生独立哈希流（**不动战斗 RNG**）。

红线：确定性最高优先级（`docs/discipline/global_rules.md`）。台词随机**禁止**
使用 `engine.rng`——那会改变后续所有掷点，等于用表现层扰动战斗结果。
本模块改用 `blake2b(battle_seed | 语义键 | 触发序号)` 派生索引：

- 同 seed 同 setup → 逐字节可重放（战报确定性不破）；
- 不同 seed → 台词组合不同（玩家侧「有随机感」）；
- 对结算零影响（不消耗任何掷点）。

语义键约定：`{scene}:{pool}` 或 `{scene}:{bond_id}:{side}`；触发序号取说话者
`trait_line_seq[key]`（同一场同一键第 N 次触发），保证一场内重复触发不必然重词。
"""
from __future__ import annotations

from hashlib import blake2b
from typing import TYPE_CHECKING, Sequence, TypeVar

if TYPE_CHECKING:
    from battle.heroes import HeroState

T = TypeVar("T")


def pick_index(seed: int, key: str, occurrence: int, count: int) -> int:
    """派生索引：seed+key+occurrence → [0, count)。count≤1 时恒 0。"""
    if count <= 1:
        return 0
    digest = blake2b(
        f"{seed}|{key}|{occurrence}".encode("utf-8"), digest_size=8
    ).digest()
    return int.from_bytes(digest, "big") % count


def next_occurrence(speaker: "HeroState", key: str) -> int:
    """取并自增该说话者在该键上的触发序号（确定性，不耗 RNG）。"""
    idx = speaker.trait_line_seq.get(key, 0)
    speaker.trait_line_seq[key] = idx + 1
    return idx


def pick(
    seed: int, speaker: "HeroState", key: str, options: Sequence[T],
) -> T | None:
    """按派生流从 options 取一项；空池 → None。自增触发序号。"""
    if not options:
        return None
    occurrence = next_occurrence(speaker, key)
    return options[pick_index(seed, key, occurrence, len(options))]


def pick_with(
    seed: int, key: str, occurrence: int, options: Sequence[T],
) -> T | None:
    """已知触发序号时取词（问答配对：答案复用问的 occurrence）。"""
    if not options:
        return None
    return options[pick_index(seed, key, occurrence, len(options))]
