from __future__ import annotations

"""客户端机制覆盖测试（Phase 2 B3 验收用例）。

逐机制指定 golden 战报，断言事件流含对应关键事件；并可一键同步到
Unity StreamingAssets 供 BattleDemo 切换人工验收。

机制 → golden 对照（与 PlayMode CardBattleMechanicsTests 同源）：
  连携 assist     oracle_seed99.json
  单挑 duel       1v1_seed7.json
  中毒 poison     skills_seed11.json
  控制 control    standard_seed42.json
  追击 pursuit    standard_seed20260705.json
  准备 prepare    men_gods_seed0.json

直接运行（仓库根目录）：
    python battle/tests/test_client_mechanics.py              # 校验覆盖
    python battle/tests/test_client_mechanics.py --list       # 打印对照表
    python battle/tests/test_client_mechanics.py --export     # 同步 StreamingAssets
"""

import argparse
import json
import sys
from collections.abc import Callable
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import pytest

GOLDEN_DIR = Path(__file__).resolve().parent / "golden"
STREAMING_DIR = Path(__file__).resolve().parents[2] / "Assets" / "StreamingAssets" / "battle_reports"


def _flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def _has_assist(events: list[dict]) -> bool:
    return any(
        e["type"] == "skill_trigger" and e["payload"].get("kind") == "assist"
        for e in events
    )


def _has_duel(events: list[dict]) -> bool:
    types = {e["type"] for e in events}
    return "duel_challenge" in types and "duel_result" in types


def _has_poison(events: list[dict]) -> bool:
    applied = ticked = False
    for e in events:
        if e["type"] not in ("status_apply", "status_tick"):
            continue
        sid = e["payload"].get("status", {}).get("status_id")
        if sid == "test_poison_status":
            if e["type"] == "status_apply":
                applied = True
            else:
                ticked = True
    return applied and ticked


def _has_control(events: list[dict]) -> bool:
    control_ids = {"silence", "disarm", "ming_lock", "petrify", "hesitation"}
    return any(
        e["type"] == "status_apply"
        and e["payload"].get("status", {}).get("status_id") in control_ids
        for e in events
    )


def _has_pursuit(events: list[dict]) -> bool:
    """追击战法触发：skill_trigger 的 skill 注册为 TIMING_PURSUIT（v3.1 如 achilles_thrust）。"""
    from battle.skills import REGISTRY, TIMING_PURSUIT

    pursuit_ids = {k for k, v in REGISTRY.items() if v.timing == TIMING_PURSUIT}
    return any(
        e["type"] == "skill_trigger"
        and e["payload"].get("skill_id") in pursuit_ids
        for e in events
    )


def _has_prepare(events: list[dict]) -> bool:
    return any(
        e["type"] == "skill_trigger" and e["payload"].get("kind") == "prepare"
        for e in events
    )


MECHANIC_CASES: dict[str, dict] = {
    "连携": {
        # v4 海域批后 seed5 无 assist（潮汐抚愈改被动、率值变动致 RNG 移位），改用 seed99
        "golden": "oracle_seed99.json",
        "scene": "波塞冬神谕局 · 准备回合副将 assist",
        "check": _has_assist,
    },
    "单挑": {
        "golden": "1v1_seed7.json",
        "scene": "阿喀琉斯 vs 赫克托尔 · duel_challenge/result",
        "check": _has_duel,
    },
    "中毒": {
        "golden": "skills_seed11.json",
        "scene": "试·剧毒 · status_apply + status_tick DoT",
        "check": _has_poison,
    },
    "控制": {
        "golden": "standard_seed42.json",
        "scene": "缴械/缄默/冥锁/石化/犹豫 · status_apply",
        "check": _has_control,
    },
    "追击": {
        "golden": "standard_seed20260705.json",
        "scene": "怒火突刺 achilles_thrust · 普攻后追加",
        "check": _has_pursuit,
    },
    "准备": {
        # v4 冥界批后死神镰痕改即发；准备覆盖改用赫克托尔战吼（men_gods 场景）
        "golden": "men_gods_seed0.json",
        "scene": "特洛伊战吼 hector_warcry · skill_trigger kind=prepare",
        "check": _has_prepare,
    },
}


def load_golden(name: str) -> dict:
    path = GOLDEN_DIR / name
    assert path.exists(), f"golden 缺失：{path}（python battle/tools/gen_golden.py --write）"
    return json.loads(path.read_text(encoding="utf-8"))


def verify_mechanic(name: str) -> None:
    case = MECHANIC_CASES[name]
    report = load_golden(case["golden"])
    events = _flat_events(report)
    assert case["check"](events), (
        f"机制「{name}」在 {case['golden']} 中未找到预期事件（{case['scene']}）"
    )


def export_goldens() -> None:
    """同步机制验收用 golden 到 Unity StreamingAssets。"""
    STREAMING_DIR.mkdir(parents=True, exist_ok=True)
    names = sorted({c["golden"] for c in MECHANIC_CASES.values()})
    for name in names:
        src = GOLDEN_DIR / name
        dst = STREAMING_DIR / name
        dst.write_text(src.read_text(encoding="utf-8"), encoding="utf-8")
        print(f"  已同步 {name} → {dst.relative_to(Path.cwd())}")


# ---------------------------------------------------------------- pytest

@pytest.mark.parametrize("mechanic", list(MECHANIC_CASES.keys()))
def test_mechanic_golden_contains_events(mechanic: str):
    verify_mechanic(mechanic)


def test_mechanic_goldens_all_distinct_files_exist():
    for case in MECHANIC_CASES.values():
        assert (GOLDEN_DIR / case["golden"]).exists()


# ---------------------------------------------------------------- CLI

def print_table() -> None:
    print("客户端机制验收用例（golden → 机制）\n")
    print(f"{'机制':<6} {'golden 战报':<28} 场景说明")
    print("-" * 72)
    for name, case in MECHANIC_CASES.items():
        print(f"{name:<6} {case['golden']:<28} {case['scene']}")
    print("\nUnity 切换：BattleDemoRunner → Report Path = battle_reports/<文件名>")
    print("Python 校验：python battle/tests/test_client_mechanics.py")


def main() -> None:
    parser = argparse.ArgumentParser(description="客户端机制覆盖 golden 校验")
    parser.add_argument("--list", action="store_true", help="打印机制→战报对照表")
    parser.add_argument("--export", action="store_true",
                        help="同步机制 golden 到 Assets/StreamingAssets/battle_reports/")
    args = parser.parse_args()

    if args.list:
        print_table()
        return

    if args.export:
        print("同步机制验收战报到 StreamingAssets …")
        export_goldens()
        print("完成。")
        return

    print("校验机制 golden 事件覆盖 …")
    for name in MECHANIC_CASES:
        verify_mechanic(name)
        case = MECHANIC_CASES[name]
        print(f"  ✓ {name:<4} ← {case['golden']}")
    print(f"\n全部 {len(MECHANIC_CASES)} 项机制覆盖通过。")
    print("Unity PlayMode：CardBattleMechanicsTests（同名 golden）")


if __name__ == "__main__":
    main()
