from __future__ import annotations

"""replay_dump：战报 JSON → 人类可读逐局逐回合文本日志（任务书 6.3 运维要求）。

运营与策划排查用，不依赖客户端。两档粒度：
  brief —— 只输出客户端反演所需主干事件；
  all   —— 全量（含选人受击点数、状态计次、犹豫判定等冗余判定信息）。

用法（仓库根目录执行）：
    python battle/tools/replay_dump.py battle/out/standard_seed20260705.json
    python battle/tools/replay_dump.py 战报.json --mode brief
    python battle/tools/replay_dump.py 战报.json -o dump.txt
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.textlog import MODES, format_report, safe_print


def main() -> None:
    parser = argparse.ArgumentParser(description="战报 JSON 转人类可读文本日志")
    parser.add_argument("report_json", help="serialize_report 产出的战报 JSON 文件路径")
    parser.add_argument("--mode", choices=MODES, default="all",
                        help="brief=客户端反演主干 / all=全量含内部判定（默认）")
    parser.add_argument("-o", "--out", default=None,
                        help="输出文本文件路径（默认打印到控制台，UTF-8）")
    args = parser.parse_args()

    report = json.loads(Path(args.report_json).read_text(encoding="utf-8"))
    text = format_report(report, mode=args.mode)
    if args.out:
        out_path = Path(args.out)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(text, encoding="utf-8")
        print(f"已写入 {out_path}（{args.mode} 模式）")
    else:
        safe_print(text)


if __name__ == "__main__":
    main()
