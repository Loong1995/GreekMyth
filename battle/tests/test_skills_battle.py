"""端到端：带全套 B2 测试战法的完整系列（伤害/治疗/DoT/buff/控制/属性修改）。

验证事件流在战法参与下仍满足全部结构不变量 + 各类事件语义正确 + 确定性不破。
直接运行：python battle/tests/test_skills_battle.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.tests.helpers import skills_3v3_setup
from battle.tests.test_report_structure import GROUP_ROOT_TYPES, KNOWN_EVENT_TYPES


def flat_events(report: dict) -> list[dict]:
    return [event for game in report["games"] for event in game["events"]]


def scan_reports(seeds: range) -> list[dict]:
    return [simulate(skills_3v3_setup(), seed=seed) for seed in seeds]


def test_determinism_with_skills():
    baseline = serialize_report(simulate(skills_3v3_setup(), seed=99))
    for _ in range(30):
        assert serialize_report(simulate(skills_3v3_setup(), seed=99)) == baseline


def test_structure_invariants_hold_with_skills():
    """seq 连续、t 单调、分组继承、事件类型合法——扫 10 个种子。"""
    for report in scan_reports(range(1, 11)):
        events = flat_events(report)
        assert [e["seq"] for e in events] == list(range(1, len(events) + 1))
        times = [(e["t"]["g"], e["t"]["r"], e["t"]["p"], e["t"]["s"]) for e in events]
        assert times == sorted(times)
        group_of: dict[int, int] = {}
        for event in events:
            assert event["type"] in KNOWN_EVENT_TYPES
            seq, parent, group = event["seq"], event["parent_seq"], event["group_id"]
            assert 0 <= parent < seq
            if parent == 0 or event["type"] in GROUP_ROOT_TYPES:
                assert group == seq
            else:
                assert group == group_of[parent]
            group_of[seq] = group


def test_skill_triggers_are_group_roots_with_declared_targets():
    found_types = set()
    for report in scan_reports(range(1, 16)):
        for event in flat_events(report):
            if event["type"] != "skill_trigger":
                continue
            found_types.add(event["payload"]["skill_id"])
            assert event["parent_seq"] == 0 and event["group_id"] == event["seq"]
            assert event["payload"]["kind"] == "cast"
            assert event["payload"]["target_ids"], "B2 测试战法都必须宣告目标"
    # 六个测试战法在 15 个种子内都应触发过
    assert found_types == {"test_blast", "test_mend", "test_poison",
                           "test_war_cry", "test_disarm", "test_sap"}


def test_status_lifecycle_in_stream():
    """状态施加→（tick/刷新）→移除，全生命周期在流内可闭合。"""
    saw_expired = saw_refresh_or_stack = saw_dot_tick = False
    for report in scan_reports(range(1, 16)):
        active: set[int] = set()
        for event in flat_events(report):
            payload = event["payload"]
            if event["type"] == "status_apply":
                instance_id = payload["status"]["instance_id"]
                assert instance_id not in active, "同 instance_id 重复施加"
                active.add(instance_id)
            elif event["type"] == "status_refresh":
                assert payload["status"]["instance_id"] in active
                saw_refresh_or_stack = True
            elif event["type"] == "status_tick":
                assert payload["status"]["instance_id"] in active
                saw_dot_tick = True
            elif event["type"] == "status_remove":
                assert payload["status"]["instance_id"] in active, "移除未施加的状态"
                active.discard(payload["status"]["instance_id"])
                if payload["reason"] == "expired":
                    saw_expired = True
                else:
                    assert payload["reason"] in {"dispelled", "source_defeated", "game_end"}
            elif event["type"] == "game_end":
                active.clear()  # 局末语义清空
    assert saw_expired and saw_refresh_or_stack and saw_dot_tick


def test_magic_damage_and_heal_events_present_and_wellformed():
    saw_magic = saw_heal = saw_crit = False
    for report in scan_reports(range(1, 16)):
        for event in flat_events(report):
            if event["type"] == "damage":
                payload = event["payload"]
                assert payload["damage_type"] in {"physical", "magic", "true"}
                assert payload["amount"] >= 1
                delta = payload["troops"]
                assert delta["troops_before"] - delta["troops_after"] == payload["amount"]
                # 30/70 拆分守恒
                dealt = payload["amount"]
                dead = delta["dead_after"] - delta["dead_before"]
                wounded = delta["wounded_after"] - delta["wounded_before"]
                assert dead + wounded == dealt and dead == dealt * 3000 // 10000
                saw_magic |= payload["damage_type"] == "magic"
                saw_crit |= payload["is_crit"]
            elif event["type"] == "heal":
                payload = event["payload"]
                assert payload["amount"] >= 1, "0 量治疗不应发事件"
                delta = payload["troops"]
                assert delta["troops_after"] - delta["troops_before"] == payload["amount"]
                assert delta["wounded_before"] - delta["wounded_after"] == payload["amount"]
                assert delta["dead_after"] == delta["dead_before"], "治疗不复活"
                assert delta["troops_after"] <= 10000, "治疗不超上限"
                saw_heal = True
    assert saw_magic and saw_heal and saw_crit


def test_attr_change_events_wellformed():
    saw = False
    for report in scan_reports(range(1, 16)):
        for event in flat_events(report):
            if event["type"] != "attr_change":
                continue
            saw = True
            payload = event["payload"]
            assert payload["scope"] in {"temporary", "game", "series"}
            for change in payload["changes"]:
                assert change["attr"] in {"force", "intelligence", "command", "speed"}
                assert change["after"] >= 0
    assert saw, "test_sap 在 15 个种子内应至少触发一次 attr_change"


def test_control_status_suppresses_basic_attack():
    """被缴械武将的行动窗口内不得出现其 normal_attack。"""
    checked = 0
    for report in scan_reports(range(1, 21)):
        for game in report["games"]:
            disarmed_until: dict[str, int] = {}  # hero_id -> 到期前最后一个 seq 观测哨
            for event in game["events"]:
                payload = event["payload"]
                if (event["type"] == "status_apply"
                        and payload["status"]["status_id"] == "test_disarm_status"):
                    disarmed_until[payload["status"]["owner_id"]] = payload["status"]["instance_id"]
                elif (event["type"] == "status_remove"
                      and payload["status"]["status_id"] == "test_disarm_status"):
                    disarmed_until.pop(payload["status"]["owner_id"], None)
                elif event["type"] == "normal_attack":
                    assert payload["actor_id"] not in disarmed_until, (
                        f"被缴械的 {payload['actor_id']} 仍在普攻"
                    )
                    checked += 1
    assert checked > 0


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
