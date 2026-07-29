#!/usr/bin/env python3
"""比对离线时长模型与真播测量值（标定用）。

用法（仓库根）：
  python battle/tools/compare_playback_timing.py <报告名.playback.json> <报告名.measured.tsv>

- `.playback.json` 由 Unity 菜单「导出 PlaybackScript」产出，逐组带 `est_sec`；
- `.measured.tsv` 由 `PlaybackDirector.OnGroupPlayed` 钩子录制（root_seq/kind/sec）。

输出：按 kind 汇总的 模型/真值 比值 —— 比值明显偏离 1 的那一类就是算错的那一拍。
"""

from __future__ import annotations

import collections
import json
import sys
from pathlib import Path


def main(argv: list[str]) -> int:
    if len(argv) < 3:
        print(__doc__)
        return 2
    playback = json.loads(Path(argv[1]).read_text(encoding="utf-8"))
    est = {}
    for game in playback["games"]:
        for g in game["groups"]:
            est[int(g["root_seq"])] = (g["kind"], float(g.get("est_sec", 0.0)))

    real: dict[int, float] = {}
    for line in Path(argv[2]).read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        seq, kind, sec = line.split("\t")
        real[int(seq)] = float(sec)

    agg = collections.defaultdict(lambda: [0.0, 0.0, 0])
    for seq, sec in real.items():
        kind, e = est.get(seq, ("<未在编译产物>", 0.0))
        row = agg[kind]
        row[0] += e
        row[1] += sec
        row[2] += 1

    print(f"{'kind':<14}{'组数':>5}{'模型s':>10}{'真值s':>10}{'比值':>8}{'差s':>9}")
    tot_e = tot_r = 0.0
    for kind, (e, r, n) in sorted(agg.items(), key=lambda kv: -kv[1][1]):
        tot_e += e
        tot_r += r
        ratio = e / r if r > 0.01 else float("nan")
        print(f"{kind:<14}{n:>5}{e:>10.1f}{r:>10.1f}{ratio:>8.2f}{e - r:>9.1f}")
    print(f"{'合计':<14}{len(real):>5}{tot_e:>10.1f}{tot_r:>10.1f}"
          f"{tot_e / tot_r if tot_r else float('nan'):>8.2f}{tot_e - tot_r:>9.1f}")

    print("\n单组偏差最大的 15 组：")
    worst = sorted(real.items(), key=lambda kv: -abs(est.get(kv[0], ("", 0.0))[1] - kv[1]))
    for seq, sec in worst[:15]:
        kind, e = est.get(seq, ("?", 0.0))
        print(f"  seq {seq:>4}  {kind:<14} 模型 {e:6.2f}s  真值 {sec:6.2f}s  差 {e - sec:+6.2f}s")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
