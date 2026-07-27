from __future__ import annotations

"""skill_catalog：战法标签目录（schema 1.5.0 顶层可选字段，加法演进）。

战法在定义处声明的播放标签（damage_type / category / tags…）经此模块
统一导出到战报头 ``skill_catalog``，客户端播放层**直读**、不再逐事件推断
（docs/schema/battle_events.md §skill_catalog）。

唯一真源是 ``battle.skills.Skill`` 的字段与 ``category`` property；
本模块只做「取哪些战法、导出哪些字段」的裁剪，禁止在这里二次推断语义。
report（战报头）与 client_battle_bridge（配阵目录）都从这里取条目，
保证两条出口不漂移。
"""

from typing import Any

from battle.names import skill_name
from battle.setup import BattleSetup
from battle.skills import REGISTRY


# 普攻不走 REGISTRY（引擎内占位 stub），但事件流会归因 skill_id="basic_attack"，
# 目录必须收一条固定条目，客户端不做特判。
_BASIC_ATTACK_ENTRY: dict[str, Any] = {
    "name": "普攻",
    "category": "basic",
    "timing": "active",
    "damage_type": "physical",
    "is_oracle": False,
    "prepare_rounds": 0,
}


def catalog_entry(skill_id: str) -> dict[str, Any]:
    """单条战法标签（键序固定，保证序列化确定性）。"""
    if skill_id == "basic_attack":
        return dict(_BASIC_ATTACK_ENTRY)
    sk = REGISTRY[skill_id]
    entry: dict[str, Any] = {
        "name": skill_name(skill_id),
        "category": sk.category,
        "timing": sk.timing,
        "damage_type": sk.damage_type,
        "is_oracle": bool(sk.is_oracle),
        "prepare_rounds": sk.prepare_rounds,
    }
    if sk.tags:
        entry["tags"] = list(sk.tags)
    return entry


def build_skill_catalog(setup: BattleSetup) -> dict[str, dict[str, Any]]:
    """本战出场武将全部装配战法的标签表（skill_id 字典序，确定性输出）。

    只收「会在本战报事件流里出现归因」的战法：各 HeroSetup.skills 并集。
    状态（status_id）不进本表——状态到来源战法的归因是客户端
    StatusPresentationRegistry 的职责，双方各管一层，不重复建账。
    """
    ids: set[str] = {"basic_attack"}
    for team in setup.teams:
        for hero in team.heroes:
            ids.update(hero.skills)
    return {sid: catalog_entry(sid)
            for sid in sorted(ids) if sid == "basic_attack" or sid in REGISTRY}
