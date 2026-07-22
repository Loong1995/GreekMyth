from __future__ import annotations

"""羁绊机器表（单挑初对用）：S1/S2 双向对 → weight。

来源：docs/character/bonds.md。S3 阵营共鸣不进单挑初对。
键为 frozenset({template_id_a, template_id_b})。
"""

# weight 1 = S1 传说；2 = S2 主线。同对重复登记取更小 weight。
_BOND_PAIRS: tuple[tuple[str, str, int], ...] = (
    # S1
    ("achilles", "hector", 1),
    ("achilles", "paris", 1),
    ("achilles", "patroclus", 1),
    ("perseus", "medusa", 1),
    ("hades", "persephone", 1),
    ("zeus", "poseidon", 1),
    ("odysseus", "poseidon", 1),
    ("hector", "paris", 1),
    ("athena", "medusa", 1),
    # S2
    ("zeus", "athena", 2),
    ("zeus", "ares", 2),
    ("apollo", "artemis", 2),
    ("apollo", "asclepius", 2),
    ("ares", "nike", 2),
    ("athena", "odysseus", 2),
    ("perseus", "athena", 2),
    ("achilles", "ajax", 2),
    ("jason", "castor", 2),
    ("jason", "heracles", 2),
    ("heracles", "zeus", 2),
    ("heracles", "cerberus", 2),
    ("artemis", "atalanta", 2),
    ("poseidon", "amphitrite", 2),
    ("poseidon", "triton", 2),
    ("siren", "odysseus", 2),
    ("scylla", "odysseus", 2),
    ("hades", "cerberus", 2),
    ("hades", "thanatos", 2),
    ("charon", "thanatos", 2),
    ("hermes", "zeus", 2),
    ("hermes", "hades", 2),
)

# 无羁绊排序哨兵（候选排序主键）
NO_BOND_WEIGHT = 99

_REGISTRY: dict[frozenset[str], int] = {}
for _a, _b, _w in _BOND_PAIRS:
    key = frozenset({_a, _b})
    prev = _REGISTRY.get(key)
    if prev is None or _w < prev:
        _REGISTRY[key] = _w


def bond_weight(template_a: str, template_b: str) -> int | None:
    """跨队两模板的羁绊 weight（1/2）；无则 None。"""
    if not template_a or not template_b or template_a == template_b:
        return None
    return _REGISTRY.get(frozenset({template_a, template_b}))
