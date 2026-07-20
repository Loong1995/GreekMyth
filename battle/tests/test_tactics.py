from __future__ import annotations

"""P4-C 经理人战术系统测试（docs/mechanics/manager_tactics.md）。

验收口径（phase4_plan §五4）：
- 空变更局：带 tactics 空配置 ≡ 无配置（除 setup_metadata 外逐字节一致）。
- 变更局：with_change 重模拟的第 1..N-1 回合事件与原战报**逐条一致**
  （替换段只从生效回合开始）——确定性下等价于「快照续算」。
- 2 次上限 / 最早第 2 回合生效 / 注册表校验逐条走查。
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import pytest

from battle import simulate
from battle.errors import SetupError
from battle.sample import scenario_standard
from battle.setup import BattleSetup
from battle.tactics import with_change


def setup_with_tactics(tactics: dict | None) -> BattleSetup:
    base = scenario_standard()
    metadata = dict(base.metadata)
    if tactics is not None:
        metadata["tactics"] = tactics
    return BattleSetup(battle_id=base.battle_id, teams=base.teams, metadata=metadata)


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


# ---------------------------------------------------------------- 预设战术生效


def test_empty_tactics_config_equals_no_config():
    plain = simulate(scenario_standard(), seed=42)
    empty = simulate(setup_with_tactics({"preset": {}, "changes": []}), seed=42)
    assert flat_events(plain) == flat_events(empty)
    assert plain["result"] == empty["result"]


def test_preset_focus_fire_applies_each_round():
    setup = setup_with_tactics({
        "preset": {"A": {"tactic_id": "focus_fire", "params": {"target_id": "哈迪斯"}}},
    })
    report = simulate(setup, seed=42)
    focus = [e for e in flat_events(report)
             if e["type"] == "status_apply" or e["type"] == "status_refresh"
             if e["payload"].get("status", {}).get("status_id") == "tactic_focus"]
    assert focus, "预设集火应逐回合施加 tactic_focus"
    assert all(e["payload"]["status"]["owner_id"] == "哈迪斯" for e in focus)
    # 预设不发 tactic_applied
    assert not [e for e in flat_events(report) if e["type"] == "tactic_applied"]


def test_preset_protect_and_stance():
    setup = setup_with_tactics({
        "preset": {
            "A": {"tactic_id": "protect", "params": {"target_id": "阿喀琉斯"}},
            "B": {"tactic_id": "stance", "params": {"level": 2}},
        },
    })
    report = simulate(setup, seed=42)
    events = flat_events(report)
    protects = [e for e in events if e["type"] in ("status_apply", "status_refresh")
                and e["payload"].get("status", {}).get("status_id") == "tactic_protect"]
    assert protects and all(e["payload"]["status"]["owner_id"] == "阿喀琉斯" for e in protects)
    stances = [e for e in events if e["type"] in ("status_apply", "status_refresh")
               and e["payload"].get("status", {}).get("status_id") == "tactic_stance"]
    owners = {e["payload"]["status"]["owner_id"] for e in stances}
    assert owners <= {"哈迪斯", "赫拉克勒斯", "美杜莎"} and stances


# ---------------------------------------------------------------- 变更与替换段


def test_change_emits_tactic_applied_and_prefix_identical():
    """核心验收：变更局的第 1..N-1 回合与原战报逐条一致（替换段等价快照续算）。"""
    base_setup = setup_with_tactics({"preset": {}, "changes": []})
    base = simulate(base_setup, seed=42)

    change = {"team_id": "A", "round": 3,
              "tactic_id": "focus_fire", "params": {"target_id": "哈迪斯"}}
    changed = simulate(with_change(base_setup, change), seed=42)

    applied = [e for e in flat_events(changed) if e["type"] == "tactic_applied"]
    # 每局第 3 回合头都会记录一次（战时状态不跨局，变更序列对每局生效）
    assert applied and all(e["payload"] == {
        "team_id": "A", "tactic_id": "focus_fire", "round_no": 3, "change_no": 1,
        "params": {"target_id": "哈迪斯"},
    } for e in applied)
    assert all(e["t"]["r"] == 3 for e in applied)

    # 第 1 局生效回合之前的事件流逐条一致（byte 级前缀等价）
    def prefix_before_round(report: dict, round_no: int) -> list[dict]:
        out = []
        for e in report["games"][0]["events"]:
            if e["t"]["r"] >= round_no:
                break
            out.append(e)
        return out

    assert prefix_before_round(base, 3) == prefix_before_round(changed, 3)
    # 生效回合起出现集火状态
    focus = [e for e in flat_events(changed)
             if e["type"] in ("status_apply", "status_refresh")
             and e["payload"].get("status", {}).get("status_id") == "tactic_focus"]
    assert focus and min(e["t"]["r"] for e in focus) == 3


def test_change_replaces_preset():
    setup = setup_with_tactics({
        "preset": {"A": {"tactic_id": "stance", "params": {"level": 1}}},
        "changes": [{"team_id": "A", "round": 2,
                     "tactic_id": "protect", "params": {"target_id": "宙斯"}}],
    })
    report = simulate(setup, seed=42)
    events = flat_events(report)
    stance_rounds = {e["t"]["r"] for e in events
                     if e["type"] in ("status_apply", "status_refresh")
                     and e["payload"].get("status", {}).get("status_id") == "tactic_stance"}
    assert stance_rounds == {1}, "变更后预设不再逐回合施加"
    protect_rounds = {e["t"]["r"] for e in events
                      if e["type"] in ("status_apply", "status_refresh")
                      and e["payload"].get("status", {}).get("status_id") == "tactic_protect"}
    assert protect_rounds and min(protect_rounds) == 2


def test_replay_closes_loop_with_tactics():
    """战术配置随 setup_metadata 入战报：重放工具口径（同 metadata 重模拟一致）。"""
    setup = setup_with_tactics({
        "preset": {"B": {"tactic_id": "stance", "params": {"level": -2}}},
        "changes": [{"team_id": "B", "round": 4,
                     "tactic_id": "focus_fire", "params": {"target_id": "宙斯"}}],
    })
    report = simulate(setup, seed=7)
    assert report["setup_metadata"]["tactics"]["changes"][0]["round"] == 4
    again = simulate(setup, seed=7)
    assert flat_events(report) == flat_events(again)


# ---------------------------------------------------------------- 校验红线


def test_validation_rules():
    base = setup_with_tactics({"preset": {}, "changes": []})
    with pytest.raises(SetupError):  # 未注册战术
        simulate(setup_with_tactics({"preset": {"A": {"tactic_id": "nope"}}}), seed=1)
    with pytest.raises(SetupError):  # 集火目标必须是敌方
        simulate(setup_with_tactics({"preset": {"A": {
            "tactic_id": "focus_fire", "params": {"target_id": "宙斯"}}}}), seed=1)
    with pytest.raises(SetupError):  # 保护目标必须是我方
        simulate(setup_with_tactics({"preset": {"A": {
            "tactic_id": "protect", "params": {"target_id": "哈迪斯"}}}}), seed=1)
    with pytest.raises(SetupError):  # stance 档位越界
        simulate(setup_with_tactics({"preset": {"A": {
            "tactic_id": "stance", "params": {"level": 3}}}}), seed=1)
    with pytest.raises(SetupError):  # 最早第 2 回合生效
        with_change(base, {"team_id": "A", "round": 1,
                           "tactic_id": "stance", "params": {"level": 1}})
    # 2 次上限：第 3 次追加即拒绝
    s1 = with_change(base, {"team_id": "A", "round": 2,
                            "tactic_id": "stance", "params": {"level": 1}})
    s2 = with_change(s1, {"team_id": "A", "round": 3,
                          "tactic_id": "stance", "params": {"level": -1}})
    with pytest.raises(SetupError):
        with_change(s2, {"team_id": "A", "round": 4,
                         "tactic_id": "stance", "params": {"level": 2}})
    # 对方队伍配额独立
    with_change(s2, {"team_id": "B", "round": 4,
                     "tactic_id": "stance", "params": {"level": 1}})
