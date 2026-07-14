from __future__ import annotations

from pathlib import Path
from typing import Any


OUTPUT_DIR = Path(__file__).resolve().parent / "output"


def print_and_save_output(name: str, content: str, *, echo_full: bool = True) -> Path:
    """写入 tests/output/{name}.txt。

    echo_full=True（默认）：全文打印到终端（战报很长时像卡住）。
    echo_full=False：终端只打印保存路径与行数，全文请看输出文件。
    """
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    output_path = OUTPUT_DIR / f"{name}.txt"
    output_path.write_text(content, encoding="utf-8")
    if echo_full:
        print(content)
    else:
        line_count = content.count("\n") + (1 if content else 0)
        print(f"[TEST_OUTPUT_SAVED] {output_path} ({line_count} lines, open file for full log)")
    if echo_full:
        print(f"\n[TEST_OUTPUT_SAVED] {output_path}")
    return output_path


def format_battle_result(title: str, result: Any) -> str:
    lines: list[str] = [
        f"=== {title} ===",
        "",
        "=== Human Logs ===",
    ]
    lines.extend(result.human_logs or ["<no human logs>"])
    return "\n".join(lines)


def format_rng_history(title: str, rng: Any) -> str:
    import json

    return "\n".join(
        [
            f"=== {title} ===",
            "",
            f"rng_count={rng.index}",
            "",
            "=== RNG History ===",
            json.dumps(rng.history, ensure_ascii=False, indent=2),
        ]
    )
