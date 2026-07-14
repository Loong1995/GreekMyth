from __future__ import annotations

"""bench_simulate：单进程模拟吞吐基准（任务书 4.2）。

目标：纯模拟（不做 JSON 序列化，事件流在内存正常生成）≥ 100 局/秒，
3v3 满编、普通回合数、以单局计（系列按实际局数折算）。
同时给出开启 JSON 序列化（serialize_report）后的实测数据与战报体积。

用法（仓库根目录执行）：
    python battle/benchmarks/bench_simulate.py                # 默认每场景 200 系列
    python battle/benchmarks/bench_simulate.py --series 500
    python battle/benchmarks/bench_simulate.py --report docs/dev/performance.md
"""

import argparse
import platform
import sys
import time
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.sample import SCENARIOS
from battle.version import CORE_VERSION

BENCH_SCENARIOS = ("3v3", "skills", "standard")  # 纯普攻 / B2 原语 / B3 全机制


def bench_scenario(name: str, series_count: int) -> dict:
    factory = SCENARIOS[name]
    for seed in range(3):  # 预热（注册表/首次导入开销不计入）
        simulate(factory(), seed=seed)

    reports = []
    started = time.perf_counter()
    for seed in range(series_count):
        reports.append(simulate(factory(), seed=seed))
    sim_elapsed = time.perf_counter() - started

    total_games = sum(r["result"]["total_games"] for r in reports)
    total_events = sum(len(g["events"]) for r in reports for g in r["games"])

    started = time.perf_counter()
    total_bytes = sum(len(serialize_report(r).encode("utf-8")) for r in reports)
    ser_elapsed = time.perf_counter() - started

    return {
        "scenario": name,
        "series": series_count,
        "games": total_games,
        "events": total_events,
        "sim_sec": sim_elapsed,
        "sim_games_per_sec": total_games / sim_elapsed,
        "sim_plus_ser_games_per_sec": total_games / (sim_elapsed + ser_elapsed),
        "avg_report_kb": total_bytes / len(reports) / 1024,
    }


def format_results(results: list[dict]) -> str:
    lines = [
        f"基准环境: Python {platform.python_version()} / {platform.system()} "
        f"{platform.machine()} / {CORE_VERSION} / {date.today().isoformat()}",
        "",
        "| 场景 | 系列数 | 总局数 | 纯模拟 局/秒 | 含序列化 局/秒 | 平均战报 KB |",
        "|---|---|---|---|---|---|",
    ]
    for r in results:
        lines.append(
            f"| {r['scenario']} | {r['series']} | {r['games']} "
            f"| {r['sim_games_per_sec']:.0f} | {r['sim_plus_ser_games_per_sec']:.0f} "
            f"| {r['avg_report_kb']:.0f} |")
    worst = min(r["sim_games_per_sec"] for r in results)
    lines.append("")
    lines.append(f"最慢场景纯模拟吞吐 {worst:.0f} 局/秒，目标 ≥100 局/秒："
                 + ("**达标**" if worst >= 100 else "**未达标**"))
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description="模拟吞吐基准")
    parser.add_argument("--series", type=int, default=200, help="每场景系列数（默认 200）")
    parser.add_argument("--report", default=None, help="将结果表写入 Markdown 文件路径")
    args = parser.parse_args()

    results = [bench_scenario(name, args.series) for name in BENCH_SCENARIOS]
    text = format_results(results)
    print(text)
    if args.report:
        out_path = Path(args.report)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(
            "# 性能基准报告（bench_simulate）\n\n"
            "> 由 `python battle/benchmarks/bench_simulate.py --report <path>` 生成。\n"
            "> 目标（任务书 4.2）：单进程纯模拟 ≥100 局/秒。\n\n" + text + "\n",
            encoding="utf-8")
        print(f"\n基准报告已写入 {out_path}")


if __name__ == "__main__":
    main()
