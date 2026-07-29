from __future__ import annotations

"""羁绊机器表：S1/S2 **有序**双向对 → bond_id / weight / 发言序。

来源：docs/character/bonds.md。S3 阵营共鸣不进机器表（不参与登场/单挑编排）。

**定义顺序即播放顺序（2026-07-28）**：
- 表内每行 `(first, second, weight, bond_id)`：`first` 是**发问方**（先说），
  `second` 是**作答方**（后说），形成「有问有答」。
- 行在表中的位置＝`order`，登场羁绊单元排序的次键（主键＝跨队优先）。
键仍用 frozenset 查询（无向），但查回来的 `BondDef` 保留方向与定义序。
"""

from dataclasses import dataclass


@dataclass(frozen=True)
class BondDef:
    """一条羁绊定义。first→second 即台词交互顺序（问→答）。"""

    bond_id: str
    first: str
    second: str
    weight: int   # 1=S1 传说；2=S2 主线
    order: int    # 定义序号（表内位置，0 起）

    def asker(self, template_a: str, template_b: str) -> str:
        """两模板中谁先发言（恒为 first）。"""
        return self.first if self.first in (template_a, template_b) else template_a


# (first=发问方, second=作答方, weight, bond_id)
_BOND_DEFS: tuple[tuple[str, str, int, str], ...] = (
    # ---- S1 传说级 ----
    ("achilles", "hector", 1, "bond.achilles_hector"),
    ("achilles", "paris", 1, "bond.achilles_paris"),
    ("achilles", "patroclus", 1, "bond.achilles_patroclus"),
    ("perseus", "medusa", 1, "bond.perseus_medusa"),
    ("hades", "persephone", 1, "bond.hades_persephone"),
    ("zeus", "poseidon", 1, "bond.zeus_poseidon"),
    ("odysseus", "poseidon", 1, "bond.odysseus_poseidon"),
    ("hector", "paris", 1, "bond.hector_paris"),
    ("athena", "medusa", 1, "bond.athena_medusa"),
    # ---- S2 主线 ----
    ("zeus", "athena", 2, "bond.zeus_athena"),
    ("zeus", "ares", 2, "bond.zeus_ares"),
    ("apollo", "artemis", 2, "bond.apollo_artemis"),
    ("apollo", "asclepius", 2, "bond.apollo_asclepius"),
    ("ares", "nike", 2, "bond.ares_nike"),
    ("athena", "odysseus", 2, "bond.athena_odysseus"),
    ("perseus", "athena", 2, "bond.perseus_athena"),
    ("achilles", "ajax", 2, "bond.achilles_ajax"),
    ("jason", "castor", 2, "bond.jason_castor"),
    ("jason", "heracles", 2, "bond.jason_heracles"),
    ("heracles", "zeus", 2, "bond.heracles_zeus"),
    ("heracles", "cerberus", 2, "bond.heracles_cerberus"),
    ("artemis", "atalanta", 2, "bond.artemis_atalanta"),
    # bonds.md 的 bond.poseidon_family（海族王室）在机器表按对拆开：
    # 问答台词必须对得上**具体**作答者，一个 id 两对会串味。
    ("poseidon", "amphitrite", 2, "bond.poseidon_amphitrite"),
    ("poseidon", "triton", 2, "bond.poseidon_triton"),
    ("siren", "odysseus", 2, "bond.siren_odysseus"),
    ("scylla", "odysseus", 2, "bond.scylla_odysseus"),
    ("hades", "cerberus", 2, "bond.hades_cerberus"),
    ("hades", "thanatos", 2, "bond.hades_thanatos"),
    ("charon", "thanatos", 2, "bond.charon_thanatos"),
    ("hermes", "zeus", 2, "bond.hermes_zeus"),
    ("hermes", "hades", 2, "bond.hermes_hades"),
)

# 无羁绊排序哨兵（候选排序主键）
NO_BOND_WEIGHT = 99

BOND_DEFS: tuple[BondDef, ...] = tuple(
    BondDef(bond_id=bid, first=a, second=b, weight=w, order=i)
    for i, (a, b, w, bid) in enumerate(_BOND_DEFS)
)

_REGISTRY: dict[frozenset[str], BondDef] = {}
for _d in BOND_DEFS:
    _key = frozenset({_d.first, _d.second})
    _prev = _REGISTRY.get(_key)
    # 同对重复登记取 weight 更小者；同 weight 取定义序更前者
    if _prev is None or (_d.weight, _d.order) < (_prev.weight, _prev.order):
        _REGISTRY[_key] = _d


def bond_of(template_a: str, template_b: str) -> BondDef | None:
    """两模板的羁绊定义（含方向与定义序）；无则 None。"""
    if not template_a or not template_b or template_a == template_b:
        return None
    return _REGISTRY.get(frozenset({template_a, template_b}))


def bond_weight(template_a: str, template_b: str) -> int | None:
    """跨队两模板的羁绊 weight（1/2）；无则 None。"""
    d = bond_of(template_a, template_b)
    return None if d is None else d.weight
