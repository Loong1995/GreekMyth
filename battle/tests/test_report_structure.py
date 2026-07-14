from __future__ import annotations

"""战报结构合法性：逐条核对冻结契约（docs/schema/battle_events.md）的硬性不变量。

直接运行：python battle/tests/test_report_structure.py
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.tests.helpers import full_3v3_setup, standard_3v3_setup

KNOWN_EVENT_TYPES = {
    "battle_start", "game_start", "round_start", "action_start",
    "normal_attack", "skill_trigger", "damage", "heal",
    "status_apply", "status_refresh", "status_tick", "status_remove",
    "attr_change", "troops_change", "hero_defeated",
    "duel_challenge", "duel_result", "round_end", "game_end", "battle_end",
    "phase_start", "trait_trigger",
}

# 必为组根的类型（无论有无 parent）
GROUP_ROOT_TYPES = {
    "battle_start", "game_start", "round_start", "action_start", "round_end",
    "game_end", "battle_end", "normal_attack", "duel_challenge", "phase_start",
}
# 带 parent 时**允许**自开新组的类型（契约 §3.2 连锁跨组）：
# skill_trigger（追击/连携）、status_tick（事件驱动状态发动）。其余带 parent 必须继承父组。
MAY_FORK_GROUP_TYPES = {"skill_trigger", "status_tick"}


def run_report() -> dict:
    report = simulate(full_3v3_setup(), seed=42)
    # 序列化后回读，确保是纯 JSON 可传输结构
    return json.loads(serialize_report(report))


def run_standard_report() -> dict:
    """B3 全机制阵容：单挑/神谕/被动/追击/准备型/连锁全上。"""
    return json.loads(serialize_report(simulate(standard_3v3_setup(), seed=42)))


def flat_events(report: dict) -> list[dict]:
    return [event for game in report["games"] for event in game["events"]]


def test_top_level_fields():
    report = run_report()
    for key in ("schema_version", "core_version", "battle_id", "rng_seed", "teams", "games", "result"):
        assert key in report, f"缺少顶层字段 {key}"
    assert len(report["teams"]) == 2
    assert 1 <= len(report["games"]) <= 7
    result = report["result"]
    for key in ("winner_team_id", "total_games", "reason", "game_summaries", "stats"):
        assert key in result


def test_seq_monotonic_and_time_order():
    for report in (run_report(), run_standard_report()):
        events = flat_events(report)
        assert [event["seq"] for event in events] == list(range(1, len(events) + 1))
        times = [(e["t"]["g"], e["t"]["r"], e["t"]["p"], e["t"]["s"]) for e in events]
        assert times == sorted(times), "t 字典序与 seq 序不一致"


def test_boundary_events():
    report = run_report()
    events = flat_events(report)
    assert events[0]["type"] == "battle_start"
    assert events[-1]["type"] == "battle_end"
    for game in report["games"]:
        types = [event["type"] for event in game["events"]]
        assert "game_start" in types and types[-1] in {"game_end", "battle_end"}
        assert types.count("game_start") == 1 and types.count("game_end") == 1


def test_event_types_and_grouping():
    for report in (run_report(), run_standard_report()):
        events = flat_events(report)
        group_of: dict[int, int] = {}
        for event in events:
            assert event["type"] in KNOWN_EVENT_TYPES, f"未知事件类型 {event['type']}"
            seq, parent, group = event["seq"], event["parent_seq"], event["group_id"]
            assert 0 <= parent < seq
            if parent == 0 or event["type"] in GROUP_ROOT_TYPES:
                assert group == seq, f"组根 group_id 必须等于自身 seq: {event}"
            elif event["type"] in MAY_FORK_GROUP_TYPES:
                assert group in (seq, group_of[parent]), f"分叉组非法: {event}"
            else:
                assert group == group_of[parent], f"子事件必须继承父组: {event}"
            group_of[seq] = group


def test_troops_accounting_chain():
    """每个武将的兵力三池在事件链上必须连续（before == 上一次 after）。"""
    for report in (run_report(), run_standard_report()):
        pools: dict[str, tuple[int, int, int]] = {}
        first_game = report["games"][0]
        game_start = next(e for e in first_game["events"] if e["type"] == "game_start")
        for entry in game_start["payload"]["troops"]:
            pools[entry["hero_id"]] = (
                entry["troops_after"], entry["wounded_after"], entry["dead_after"],
            )

        def check_delta(delta: dict) -> None:
            hero_id = delta["hero_id"]
            assert pools[hero_id] == (
                delta["troops_before"], delta["wounded_before"], delta["dead_before"],
            ), f"{hero_id} 兵力链断裂: {delta} vs {pools[hero_id]}"
            pools[hero_id] = (
                delta["troops_after"], delta["wounded_after"], delta["dead_after"],
            )
            total = delta["troops_after"] + delta["wounded_after"] + delta["dead_after"]
            assert total <= 10000, "三池之和不能超过初始兵力"

        for event in first_game["events"]:
            if event["type"] in {"damage", "heal", "troops_change"}:
                check_delta(event["payload"]["troops"])


def test_damage_payload_shape():
    report = run_report()
    damages = [e for e in flat_events(report) if e["type"] == "damage"]
    assert damages, "整个系列没有伤害事件"
    for event in damages:
        payload = event["payload"]
        for key in ("source_id", "target_id", "damage_type", "amount", "is_crit", "troops"):
            assert key in payload
        assert payload["damage_type"] == "physical"
        assert payload["amount"] >= 1
        delta = payload["troops"]
        assert delta["troops_before"] - delta["troops_after"] == payload["amount"]


def test_result_consistency():
    report = run_report()
    result = report["result"]
    assert result["total_games"] == len(report["games"])
    if result["winner_team_id"] is not None:
        assert result["reason"] == "main_hero_defeated"
        last_game = report["games"][-1]
        assert last_game["result"]["winner_team_id"] == result["winner_team_id"]
        # 败方主将必须已阵亡
        defeats = [e for e in flat_events(report) if e["type"] == "hero_defeated"]
        assert any(e["payload"]["is_main_hero"] for e in defeats)


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
