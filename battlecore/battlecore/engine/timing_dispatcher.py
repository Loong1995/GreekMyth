from __future__ import annotations

from battlecore.domain.enums import Timing
from battlecore.domain.hero import Hero


def dispatch_timing(context, timing: Timing, actor: Hero | None = None) -> None:
    context.run_timing(timing, actor)
