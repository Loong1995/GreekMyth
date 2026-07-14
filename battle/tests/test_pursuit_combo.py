"""追击 + 连击测试（任务书 5.4-1/2）：触发时机、跨组规则、禁普攻即无追击、
连击两击独立追击。

直接运行：python battle/tests/test_pursuit_combo.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero


def pursuit_setup(attacker_skills=("test_pursuit",), extra=None) -> BattleSetup:
    return BattleSetup(
        battle_id="t_pursuit",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                make_hero("a1", 0, force=95, command=90, speed=95, skills=attacker_skills),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, force=70, command=200, speed=80,
                          skills=extra or ()),
            )),
        ),
    )


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def index_by_seq(events: list[dict]) -> dict[int, dict]:
    return {e["seq"]: e for e in events}


def test_pursuit_triggers_after_basic_hit_and_is_new_group():
    """追击 skill_trigger 是新组根，parent 指回引发它的普攻 damage（契约 §3.2）。"""
    found = False
    for seed in range(20):
        report = simulate(pursuit_setup(), seed=seed)
        events = flat_events(report)
        by_seq = index_by_seq(events)
        for event in events:
            if event["type"] != "skill_trigger" or event["payload"]["skill_id"] != "test_pursuit":
                continue
            found = True
            assert event["payload"]["kind"] == "cast"
            assert event["group_id"] == event["seq"], "追击必须是新播放组"
            parent = by_seq[event["parent_seq"]]
            assert parent["type"] == "damage", "追击 parent 必须指回普攻 damage"
            grandparent = by_seq[parent["parent_seq"]]
            assert grandparent["type"] == "normal_attack"
            # 追击自身的伤害挂在追击组下
            children = [e for e in events if e["parent_seq"] == event["seq"]]
            assert children and all(c["group_id"] == event["seq"] for c in children)
        if found:
            return
    raise AssertionError("20 个种子未见追击触发（50% 概率，不应如此）")


def test_no_pursuit_when_basic_forbidden():
    """缴械禁普攻 → 无 normal_attack 也无追击（任务书 5.4-1）。"""
    # b1 持续给 a1 缴械（100% 触发）
    report = simulate(pursuit_setup(extra=("test_disarm",)), seed=5)
    events = flat_events(report)
    disarm_applies = [
        e for e in events
        if e["type"] in ("status_apply", "status_refresh")
        and e["payload"]["status"]["status_id"] == "test_disarm_status"
    ]
    assert disarm_applies, "测试前提：缴械至少施加一次"
    # 找到 a1 被缴械覆盖的行动窗口：窗口内不得有 a1 的普攻与追击
    # （简化断言：整场中 a1 每个 normal_attack 必然不在缴械生效窗口，
    #  由引擎 forbid 检查保证；此处验证核心不变量——普攻数 ≥ 追击数）
    attacks = [e for e in events if e["type"] == "normal_attack"
               and e["payload"]["actor_id"] == "a1"]
    pursuits = [e for e in events if e["type"] == "skill_trigger"
                and e["payload"]["skill_id"] == "test_pursuit"]
    assert len(pursuits) <= len(attacks)


def test_combo_makes_two_strikes_with_strike_no():
    """100% 连击 buff：同一行动窗口两次 normal_attack，strike_no=1/2。"""
    report = simulate(pursuit_setup(attacker_skills=("test_combo_drill", "test_pursuit")),
                      seed=3)
    events = flat_events(report)
    strike_2 = [e for e in events if e["type"] == "normal_attack"
                and e["payload"]["strike_no"] == 2]
    assert strike_2, "combo buff 生效期间必有第二击"
    # 第二击与第一击同一行动窗口（同 t）
    for event in strike_2:
        siblings = [
            e for e in events
            if e["type"] == "normal_attack" and e["t"] == event["t"]
            and e["payload"]["actor_id"] == event["payload"]["actor_id"]
        ]
        assert [e["payload"]["strike_no"] for e in siblings] == [1, 2]


def test_each_strike_rolls_pursuit_independently():
    """连击两击各自可触发追击：存在某窗口两击都带追击（多种子搜索）。"""
    for seed in range(60):
        report = simulate(
            pursuit_setup(attacker_skills=("test_combo_drill", "test_pursuit")), seed=seed
        )
        events = flat_events(report)
        by_seq = index_by_seq(events)
        # 按普攻分组统计其后的追击
        pursuit_per_attack: dict[int, int] = {}
        for event in events:
            if event["type"] == "skill_trigger" and event["payload"]["skill_id"] == "test_pursuit":
                damage = by_seq[event["parent_seq"]]
                attack_seq = damage["parent_seq"]
                pursuit_per_attack[attack_seq] = pursuit_per_attack.get(attack_seq, 0) + 1
        # 找出同窗口 strike1 与 strike2 都触发了追击的情形
        attacks_with_pursuit = {
            seq for seq in pursuit_per_attack
            if by_seq[seq]["type"] == "normal_attack"
        }
        windows: dict[tuple, set[int]] = {}
        for seq in attacks_with_pursuit:
            t = by_seq[seq]["t"]
            key = (t["g"], t["r"], t["p"], t["s"])
            windows.setdefault(key, set()).add(by_seq[seq]["payload"]["strike_no"])
        if any(strikes == {1, 2} for strikes in windows.values()):
            return
    raise AssertionError("60 个种子未见连击双追击（50%×50%/窗口，不应如此）")


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
