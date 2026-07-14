from __future__ import annotations

from battlecore.domain.hero import Hero


def resolve_death(context, hero: Hero, killer: Hero | None = None) -> None:
    if hero.troops <= 0 and not hero.exited:
        context.mark_hero_exited(hero, reason="TROOPS_ZERO", killer=killer)
