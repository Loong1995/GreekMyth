from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass

from battlecore.config.schema import HeroConfig
from battlecore.domain.enums import HeroRole

BASIC_ATTACK_SKILL_ID = "basic_attack"
EXTRA_SKILL_SLOT_COUNT = 3


@dataclass(slots=True)
class HeroTemplateConfig:
    """Static hero roster entry: name, stats, portrait, and innate main skill."""

    template_id: str
    name: str
    portrait: str
    force: int
    intelligence: int
    command: int
    speed: int
    innate_skill_id: str
    crit_rate_bps: int = 0
    heal_crit_rate_bps: int = 0
    default_max_troops: int = 10000


ZEUS = HeroTemplateConfig(
    template_id="zeus",
    name="宙斯",
    portrait="portraits/zeus.png",
    force=90,
    intelligence=95,
    command=110,
    speed=85,
    innate_skill_id="thunder_oracle",
)

APOLLO = HeroTemplateConfig(
    template_id="apollo",
    name="阿波罗",
    portrait="portraits/apollo.png",
    force=85,
    intelligence=50,
    command=30,
    speed=95,
    innate_skill_id="delphi_revelation",
)

ASCLEPIUS = HeroTemplateConfig(
    template_id="asclepius",
    name="阿斯克勒庇俄斯",
    portrait="portraits/asclepius.png",
    force=70,
    intelligence=130,
    command=105,
    speed=80,
    innate_skill_id="asclepius_oracle",
)

HADES = HeroTemplateConfig(
    template_id="hades",
    name="哈迪斯",
    portrait="portraits/hades.png",
    force=80,
    intelligence=90,
    command=100,
    speed=95,
    innate_skill_id="hades_underworld_dominion",
)

HERO_TEMPLATES: dict[str, HeroTemplateConfig] = {
    template.template_id: template
    for template in (ZEUS, APOLLO, ASCLEPIUS, HADES)
}


def hero_from_template(
    template: HeroTemplateConfig,
    extra_skills: Sequence[str],
    *,
    hero_id: str,
    team_id: str,
    role: HeroRole,
    position: int,
    max_troops: int | None = None,
) -> HeroConfig:
    """Build a battle hero from roster template plus up to three extra learned skills."""
    learned_skills = list(extra_skills)
    if len(learned_skills) > EXTRA_SKILL_SLOT_COUNT:
        raise ValueError(
            f"{template.name} allows at most {EXTRA_SKILL_SLOT_COUNT} extra skills, got {len(learned_skills)}"
        )
    if template.innate_skill_id in learned_skills:
        raise ValueError(f"{template.innate_skill_id} is innate to {template.name} and cannot be re-equipped")

    skill_ids = [template.innate_skill_id, *learned_skills, BASIC_ATTACK_SKILL_ID]
    return HeroConfig(
        hero_id=hero_id,
        name=template.name,
        team_id=team_id,
        role=role,
        position=position,
        max_troops=max_troops if max_troops is not None else template.default_max_troops,
        force=template.force,
        intelligence=template.intelligence,
        command=template.command,
        speed=template.speed,
        skill_ids=skill_ids,
        crit_rate_bps=template.crit_rate_bps,
        heal_crit_rate_bps=template.heal_crit_rate_bps,
        template_id=template.template_id,
        portrait=template.portrait,
        innate_skill_id=template.innate_skill_id,
    )


def Zeus(
    extra_skills: Sequence[str],
    *,
    hero_id: str = "zeus",
    team_id: str = "team_a",
    role: HeroRole = HeroRole.MAIN,
    position: int = 1,
    max_troops: int | None = None,
) -> HeroConfig:
    return hero_from_template(
        ZEUS,
        extra_skills,
        hero_id=hero_id,
        team_id=team_id,
        role=role,
        position=position,
        max_troops=max_troops,
    )


def Apollo(
    extra_skills: Sequence[str],
    *,
    hero_id: str = "apollo",
    team_id: str = "team_a",
    role: HeroRole = HeroRole.MAIN,
    position: int = 1,
    max_troops: int | None = None,
) -> HeroConfig:
    return hero_from_template(
        APOLLO,
        extra_skills,
        hero_id=hero_id,
        team_id=team_id,
        role=role,
        position=position,
        max_troops=max_troops,
    )


def Asclepius(
    extra_skills: Sequence[str],
    *,
    hero_id: str = "asclepius",
    team_id: str = "team_a",
    role: HeroRole = HeroRole.MAIN,
    position: int = 1,
    max_troops: int | None = None,
) -> HeroConfig:
    return hero_from_template(
        ASCLEPIUS,
        extra_skills,
        hero_id=hero_id,
        team_id=team_id,
        role=role,
        position=position,
        max_troops=max_troops,
    )


def Hades(
    extra_skills: Sequence[str],
    *,
    hero_id: str = "hades",
    team_id: str = "team_a",
    role: HeroRole = HeroRole.MAIN,
    position: int = 1,
    max_troops: int | None = None,
) -> HeroConfig:
    return hero_from_template(
        HADES,
        extra_skills,
        hero_id=hero_id,
        team_id=team_id,
        role=role,
        position=position,
        max_troops=max_troops,
    )
