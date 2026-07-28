"""skill_catalog（1.5.0）/ status_catalog（1.5.2）与定义期播放标签的回归。

标签是客户端播放编译层的唯一分类依据（不再逐事件推断），
这里锁住：注册校验、category 推导、目录覆盖面与确定性。
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import pytest

import battle.skills_cal  # noqa: F401  确保全部注册
import battle.skills_gods  # noqa: F401
import battle.skills_men  # noqa: F401
import battle.skills_sea  # noqa: F401
import battle.skills_underworld  # noqa: F401
from battle import simulate
from battle.sample import scenario_men_gods
from battle.skill_catalog import build_skill_catalog, catalog_entry
from battle.skills import REGISTRY, Skill, register
from battle.status_catalog import build_status_catalog
from battle.statuses import SEQUENTIAL, SIMULTANEOUS, SPECIAL, STATUS_DEFS, StatusDef


def test_register_rejects_bad_damage_type():
    with pytest.raises(ValueError, match="damage_type"):
        register(Skill(skill_id="_bad_dt", damage_type="fire"))
    assert "_bad_dt" not in REGISTRY


def test_all_registered_have_valid_tags():
    for sid, sk in REGISTRY.items():
        assert sk.damage_type in {"physical", "magic", "mixed", "none"}, sid
        assert sk.category in {"active", "prepare_active", "passive",
                               "pursuit", "oracle"}, sid


def test_category_derivation_samples():
    assert REGISTRY["hector_warcry"].category == "prepare_active"
    assert REGISTRY["thunder_oracle"].category == "oracle"
    assert REGISTRY["achilles_thrust"].category == "pursuit"
    assert REGISTRY["achilles_wrath"].category == "passive"
    assert REGISTRY["zeus_bolt"].category == "active"


def test_basic_attack_entry_fixed():
    e = catalog_entry("basic_attack")
    assert e["category"] == "basic"
    assert e["damage_type"] == "physical"


def test_catalog_covers_lineup_and_is_sorted():
    setup = scenario_men_gods()
    cat = build_skill_catalog(setup)
    assert "basic_attack" in cat
    for team in setup.teams:
        for hero in team.heroes:
            for sid in hero.skills:
                assert sid in cat, sid
    assert list(cat) == sorted(cat)


def test_report_carries_catalog():
    setup = scenario_men_gods()
    report = simulate(setup, seed=7)
    # skill_catalog 自 1.5.0 起随战报头下发；版本只作「≥ 引入版本」的下界校验，
    # 免得每次加法式小版本都要改这条断言。
    assert report["schema_version"] >= "1.5.0"
    cat = report["skill_catalog"]
    assert cat == build_skill_catalog(setup)


# ---------------------------------------------------------- status_catalog

def test_status_playback_tags_are_declared_at_definition():
    """播放标签只能取合法值，且必须在 StatusDef 定义处声明（自注册进 STATUS_DEFS）。"""
    with pytest.raises(ValueError, match="playback_tags"):
        StatusDef(status_id="_bad_playback", kind=SPECIAL, playback_tags=("burst",))
    assert STATUS_DEFS["thunder"].playback_tags == (SIMULTANEOUS,)
    assert STATUS_DEFS["aegis_shield"].playback_tags == (SEQUENTIAL,)


def test_status_catalog_only_tagged_and_sorted():
    cat = build_status_catalog()
    assert list(cat) == sorted(cat)
    assert cat["thunder"]["tags"] == ["simultaneous"]
    # 无标签的状态不进目录（默认语义，不占战报体积）
    assert "silence" not in cat
    for entry in cat.values():
        assert entry["tags"], "进目录的状态必须至少带一个标签"


def test_report_carries_status_catalog():
    setup = scenario_men_gods()
    report = simulate(setup, seed=7)
    assert report["schema_version"] >= "1.5.2"
    assert report["status_catalog"] == build_status_catalog()


if __name__ == "__main__":
    raise SystemExit(pytest.main([__file__, "-v"]))
