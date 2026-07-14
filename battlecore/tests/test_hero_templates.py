import _path_bootstrap  # noqa: F401

import pytest

from battlecore.config.config_db import build_demo_config_db
from battlecore.config.hero_files import Apollo, Asclepius, Zeus, hero_from_template, ZEUS
from battlecore.domain.enums import HeroRole


def test_zeus_template_has_high_intelligence_and_innate_thunder_oracle() -> None:
    hero = Zeus(["gorgon_gaze", "delphi_revelation", "asclepius_oracle"])

    assert hero.name == "宙斯"
    assert hero.template_id == "zeus"
    assert hero.portrait == "portraits/zeus.png"
    assert hero.innate_skill_id == "thunder_oracle"
    assert hero.intelligence == ZEUS.intelligence
    assert hero.skill_ids == [
        "thunder_oracle",
        "gorgon_gaze",
        "delphi_revelation",
        "asclepius_oracle",
        "basic_attack",
    ]


def test_oracle_heroes_each_have_exclusive_innate_skill() -> None:
    apollo = Apollo(["gorgon_gaze", "thunder_oracle", "asclepius_oracle"])
    asclepius = Asclepius(["gorgon_gaze", "delphi_revelation", "thunder_oracle"])

    assert apollo.innate_skill_id == "delphi_revelation"
    assert asclepius.innate_skill_id == "asclepius_oracle"


def test_hero_factory_allows_fewer_than_three_extra_skills() -> None:
    hero = Zeus(["gorgon_gaze"])

    assert hero.skill_ids == ["thunder_oracle", "gorgon_gaze", "basic_attack"]


def test_hero_factory_rejects_more_than_three_extra_skills() -> None:
    with pytest.raises(ValueError, match="at most 3 extra skills"):
        Zeus(["gorgon_gaze", "delphi_revelation", "asclepius_oracle", "hades_underworld_dominion"])


def test_hero_factory_rejects_re_equipping_innate_skill() -> None:
    with pytest.raises(ValueError, match="innate"):
        Zeus(["thunder_oracle", "gorgon_gaze", "delphi_revelation"])


def test_hero_templates_are_registered_in_config_db() -> None:
    db = build_demo_config_db()

    assert set(db.hero_templates) == {"zeus", "apollo", "asclepius", "hades"}
    assert db.hero_templates["zeus"].name == "宙斯"


def test_hero_from_template_preserves_battle_placement() -> None:
    hero = hero_from_template(
        ZEUS,
        ["gorgon_gaze", "delphi_revelation", "asclepius_oracle"],
        hero_id="b_main",
        team_id="team_b",
        role=HeroRole.MAIN,
        position=1,
    )

    assert hero.hero_id == "b_main"
    assert hero.team_id == "team_b"
    assert hero.role == HeroRole.MAIN
