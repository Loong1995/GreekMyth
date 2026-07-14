from __future__ import annotations

from battlecore.domain.enums import TargetPolicy
from battlecore.domain.hero import Hero


def select_targets(context, actor: Hero, target_policy: TargetPolicy, target_count: int) -> list[Hero]:
    return context.select_targets(actor, target_policy, target_count)
