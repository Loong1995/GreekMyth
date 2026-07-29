from __future__ import annotations

"""gen_golden：生成/更新 golden 基准战报（任务书 4.3 第 3 层，B4 冻结入库）。

golden = 固定 (场景, 种子) 的完整战报 JSON（serialize_report 规范字节），
入库 battle/tests/golden/，由 battle/tests/test_golden.py 逐字节回归。

纪律（任务书 9）：任何改动若使 golden 输出变化，必须显式重跑本脚本更新文件，
并在 commit message 中说明原因；禁止为通过测试而擅改公式或 golden。

用法（仓库根目录执行）：
    python battle/tools/gen_golden.py           # 对比现有 golden，仅报告差异
    python battle/tools/gen_golden.py --write   # 写入/更新全部 golden 文件
"""

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.sample import SCENARIOS

GOLDEN_DIR = Path(__file__).resolve().parents[1] / "tests" / "golden"

# 覆盖各机制类型阵容：纯普攻 / 单挑1v1 / B2原语 / 平局系列 / 超编NPC /
# B3全机制（单挑+神谕+被动+追击+准备型）/ 神谕连携+犹豫
GOLDEN_CASES: tuple[tuple[str, int], ...] = (
    ("3v3", 1),
    ("1v1", 7),
    ("skills", 11),
    ("stalemate", 1),
    ("npc", 3),
    ("standard", 42),
    ("standard", 20260705),
    ("oracle", 5),
    ("oracle", 99),
    ("sea_underworld", 9),
    ("men_gods", 0),
)


def golden_path(scenario: str, seed: int) -> Path:
    return GOLDEN_DIR / f"{scenario}_seed{seed}.json"


def generate(scenario: str, seed: int) -> str:
    return serialize_report(simulate(SCENARIOS[scenario](), seed=seed))


def main() -> None:
    parser = argparse.ArgumentParser(description="生成/校验 golden 基准战报")
    parser.add_argument("--write", action="store_true",
                        help="写入/更新 golden 文件（默认仅对比报告差异）")
    args = parser.parse_args()

    GOLDEN_DIR.mkdir(parents=True, exist_ok=True)
    changed = 0
    for scenario, seed in GOLDEN_CASES:
        path = golden_path(scenario, seed)
        payload = generate(scenario, seed)
        if path.exists() and path.read_text(encoding="utf-8") == payload:
            print(f"  一致   {path.name}")
            continue
        changed += 1
        if args.write:
            path.write_text(payload, encoding="utf-8", newline="\n")
            print(f"  已写入 {path.name}（{len(payload) / 1024:.0f} KB）")
        else:
            state = "有差异" if path.exists() else "缺失"
            print(f"  {state} {path.name}（--write 更新）")
    if changed and not args.write:
        raise SystemExit(f"{changed} 个 golden 与当前 core 输出不一致；"
                         "确认改动合理后用 --write 更新并在 commit 中说明原因。")
    print("全部 golden 与当前 core 输出一致。" if not changed
          else f"共更新 {changed} 个 golden。")


if __name__ == "__main__":
    main()
