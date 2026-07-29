#!/usr/bin/env python3
"""打印 .playback.json 里的每回合用时（Unity 导出时写入的 timing，解析模型 v2）。

用法（仓库根）：
  python battle/tools/estimate_playback_rounds.py path/to/report.playback.json
  python battle/tools/estimate_playback_rounds.py path/to/report.json
      # 若同目录已有 *.playback.json 则读它；否则提示先在 Unity 导出

时长逐拍取自演出配置并对真播标定过（见 docs/client/playback_script.md §四.1）。
标定：`compare_playback_timing.py`（模型 vs PlaybackDirector.OnGroupPlayed 录的真值）。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


def _load_playback(path: Path) -> dict:
    p = path
    if p.suffix == ".json" and not p.name.endswith(".playback.json"):
        cand = Path(str(p.with_suffix("")) + ".playback.json")
        if cand.is_file():
            p = cand
        else:
            raise SystemExit(
                f"未找到 {cand.name}。请先用 Unity 菜单\n"
                f"  GreekMyth → 播放 → 导出 PlaybackScript\n"
                f"生成中间结果后再跑本工具。"
            )
    data = json.loads(p.read_text(encoding="utf-8"))
    if "timing" not in data:
        raise SystemExit(
            f"{p} 无 timing 字段（旧导出）。请重新导出 PlaybackScript。"
        )
    return data


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = Path(argv[1])
    if not path.is_file():
        print(f"文件不存在: {path}", file=sys.stderr)
        return 1
    data = _load_playback(path)
    t = data["timing"]
    print(
        f"battle_id={data.get('battle_id')}  model={t.get('model')}  "
        f"DurationMul={t.get('duration_mul')} Speed={t.get('speed')}  "
        f"行动停顿={t.get('action_pause_sec')}s 单元停顿={t.get('group_pause_sec')}s"
    )
    print(t.get("note", ""))
    print()
    prev_game = None
    for r in t.get("rounds", []):
        g = r["game_no"]
        if g != prev_game:
            if prev_game is not None:
                for gt in t.get("games", []):
                    if gt["game_no"] == prev_game:
                        print(f"  ── 第{prev_game}局合计 ≈ {gt['est_sec']}s")
            prev_game = g
            print(f"第 {g} 局：")
        label = r.get("label") or (
            "开场" if int(r["round_no"]) < 0 else f"回合 {int(r['round_no']):2d}"
        )
        print(
            f"  {label:<6}  {r['est_sec']:6.1f}s   "
            f"(单元 {r['groups']:3d} / 行动 {r['actions']:2d}"
            + (f" / 其中停顿 {r['pause_sec']:5.1f}s" if "pause_sec" in r else "")
            + ")"
        )
    if prev_game is not None:
        for gt in t.get("games", []):
            if gt["game_no"] == prev_game:
                print(f"  ── 第{prev_game}局合计 ≈ {gt['est_sec']}s")
    print(f"\n全系列合计 ≈ {t.get('total_est_sec')}s")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
