from __future__ import annotations

"""batch_sim：批量模拟统计工具（任务书 4.2，正式功能——运营期调平衡日常工具）。

给定阵容池（sample.py 注册的场景名）与种子范围，批量跑 N 场系列战，
输出胜率 / 局数 / 每武将伤害与治疗分布（均值 + 分位数）统计。

用法（仓库根目录执行）：
    python battle/tools/batch_sim.py                          # standard × 种子 0..199
    python battle/tools/batch_sim.py --scenarios standard oracle --seeds 0:1000
    python battle/tools/batch_sim.py --seeds 42:52 --json battle/out/batch.json
"""

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.sample import SCENARIOS
from battle.textlog import safe_print


def percentile(sorted_values: list[int], pct: int) -> int:
    """最近秩法分位数（纯整数，确定性）。"""
    if not sorted_values:
        return 0
    rank = max(0, (pct * len(sorted_values) + 99) // 100 - 1)
    return sorted_values[min(rank, len(sorted_values) - 1)]


def summarize(values: list[int]) -> dict[str, int]:
    ordered = sorted(values)
    total = sum(ordered)
    return {
        "mean": total // len(ordered) if ordered else 0,
        "min": ordered[0] if ordered else 0,
        "p50": percentile(ordered, 50),
        "p90": percentile(ordered, 90),
        "max": ordered[-1] if ordered else 0,
    }


def run_batch(scenario_name: str, seeds: range) -> dict[str, Any]:
    setup_factory = SCENARIOS[scenario_name]
    team_wins: dict[str, int] = {}
    draw_count = 0
    total_games = 0
    games_per_series: list[int] = []
    hero_damage: dict[str, list[int]] = {}
    hero_heal: dict[str, list[int]] = {}
    hero_kills: dict[str, int] = {}
    hero_survive: dict[str, int] = {}
    hero_team: dict[str, str] = {}

    started = time.perf_counter()
    for seed in seeds:
        report = simulate(setup_factory(), seed=seed)
        result = report["result"]
        winner = result["winner_team_id"]
        if winner is None:
            draw_count += 1
        else:
            team_wins[winner] = team_wins.get(winner, 0) + 1
        total_games += result["total_games"]
        games_per_series.append(result["total_games"])
        for team in report["teams"]:
            for hero in team["heroes"]:
                hero_team.setdefault(hero["hero_id"], team["team_id"])
        for stat in result["stats"]:
            hero_id = stat["hero_id"]
            hero_damage.setdefault(hero_id, []).append(stat["total_damage"])
            hero_heal.setdefault(hero_id, []).append(stat["total_heal"])
            hero_kills[hero_id] = hero_kills.get(hero_id, 0) + stat["kills"]
            if stat["final_troops"] > 0:
                hero_survive[hero_id] = hero_survive.get(hero_id, 0) + 1
    elapsed = time.perf_counter() - started

    n = len(games_per_series)
    return {
        "scenario": scenario_name,
        "series_count": n,
        "seed_range": [seeds.start, seeds.stop],
        "elapsed_sec": round(elapsed, 3),
        "games_per_sec": round(total_games / elapsed, 1) if elapsed > 0 else None,
        "win_rate": {
            **{team: {"wins": wins, "rate_pct": round(100 * wins / n, 2)}
               for team, wins in sorted(team_wins.items())},
            "draw": {"wins": draw_count, "rate_pct": round(100 * draw_count / n, 2)},
        },
        "games_per_series": summarize(games_per_series),
        "total_games": total_games,
        "heroes": {
            hero_id: {
                "team": hero_team.get(hero_id, "?"),
                "damage": summarize(hero_damage[hero_id]),
                "heal": summarize(hero_heal[hero_id]),
                "avg_kills": round(hero_kills.get(hero_id, 0) / n, 2),
                "survival_pct": round(100 * hero_survive.get(hero_id, 0) / n, 1),
            }
            for hero_id in hero_damage
        },
    }


def format_batch(batch: dict[str, Any]) -> str:
    lines = [
        f"=== 批量模拟 [{batch['scenario']}] 种子 {batch['seed_range'][0]}..{batch['seed_range'][1] - 1}"
        f"（{batch['series_count']} 系列 / {batch['total_games']} 局，"
        f"{batch['elapsed_sec']}s，{batch['games_per_sec']} 局/秒）===",
        "  胜率: " + "  ".join(
            f"{team}={info['wins']}({info['rate_pct']}%)"
            for team, info in batch["win_rate"].items()),
        f"  系列局数: 均值 {batch['games_per_series']['mean']} | "
        f"p50 {batch['games_per_series']['p50']} | p90 {batch['games_per_series']['p90']} | "
        f"最大 {batch['games_per_series']['max']}",
        "  武将（每系列口径）:",
        f"    {'武将':<14}{'队':<4}{'均伤':>8}{'伤p50':>8}{'伤p90':>8}{'伤max':>8}"
        f"{'均疗':>8}{'均击杀':>8}{'存活%':>8}",
    ]
    for hero_id, info in batch["heroes"].items():
        damage, heal = info["damage"], info["heal"]
        lines.append(
            f"    {hero_id:<14}{info['team']:<4}{damage['mean']:>8}{damage['p50']:>8}"
            f"{damage['p90']:>8}{damage['max']:>8}{heal['mean']:>8}"
            f"{info['avg_kills']:>8}{info['survival_pct']:>8}")
    return "\n".join(lines)


def parse_seeds(raw: str) -> range:
    if ":" in raw:
        start, stop = raw.split(":", 1)
        return range(int(start), int(stop))
    return range(0, int(raw))


def main() -> None:
    parser = argparse.ArgumentParser(description="阵容池 × 种子范围批量模拟统计")
    parser.add_argument("--scenarios", nargs="+", choices=sorted(SCENARIOS),
                        default=["standard"], help="阵容场景名（可多个，见 battle/sample.py）")
    parser.add_argument("--seeds", type=parse_seeds, default=range(0, 200),
                        help="种子范围 start:stop（如 0:1000）或数量 N（= 0:N），默认 0:200")
    parser.add_argument("--json", default=None, help="统计结果另存 JSON 路径")
    args = parser.parse_args()

    batches = [run_batch(name, args.seeds) for name in args.scenarios]
    for batch in batches:
        safe_print(format_batch(batch))
        print()
    if args.json:
        out_path = Path(args.json)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(batches, ensure_ascii=False, indent=1),
                            encoding="utf-8")
        print(f"统计 JSON 已写入 {out_path}")


if __name__ == "__main__":
    main()
