"""抽取羁绊**交互问答**台词：docs/character/bond_dialogues_*.md → voice_bond_data.py。

与 `_extract_duel_voice.py`（按武将抽扁平池）互补：本工具抽的是**成对**问答，
key 是 `bond_id`，不是武将。分册格式（权威文案在分册，机器表只是产物）：

```
### bond.achilles_hector · 阿喀琉斯 → 赫克托尔
#### 登场（敌）
- 问 「十年了，该清账。」
  - 答 「墙还在，我也在。」｜「清账？先过我的枪。」｜「等你很久了。」
#### 登场（友）
- 问 「…」
  - 答 「…」｜「…」｜「…」
#### 单挑
- 叫阵 「…」
  - 应战 「…」｜「…」｜「…」
  - 拒战 「…」｜「…」｜「…」
```

改词请改分册后重跑：python battle/tools/_extract_bond_dialogues.py
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHAR = ROOT / "docs" / "character"
OUT = ROOT / "battle" / "voice_bond_data.py"

BOND_RE = re.compile(r"^###\s+(bond\.[a-z0-9_]+)")
QUOTE_RE = re.compile(r"「([^」]+)」")
ASK_RE = re.compile(r"^-\s*(问|叫阵)\s*(.*)$")
ANSWER_RE = re.compile(r"^\s+-\s*(答|应战|拒战)\s*(.*)$")

ANSWER_KEY = {"答": "reply", "应战": "accept", "拒战": "reject"}


def _scene_of(header: str) -> str | None:
    if "登场" in header:
        if "敌" in header:
            return "enter_foe"
        if "友" in header:
            return "enter_ally"
        return None
    if "单挑" in header:
        return "duel"
    return None


def _quotes(body: str) -> tuple[str, ...]:
    return tuple(q.strip() for q in QUOTE_RE.findall(body) if q.strip())


def main() -> None:
    # bond_id -> scene -> [ (question, {answer_key: (lines,)}) ]
    data: dict[str, dict[str, list]] = {}
    files = sorted(CHAR.glob("bond_dialogues*.md"))
    for path in files:
        bond = scene = None
        for raw in path.read_text(encoding="utf-8").splitlines():
            bm = BOND_RE.match(raw)
            if bm:
                bond, scene = bm.group(1), None
                continue
            if raw.startswith("####"):
                scene = _scene_of(raw) if bond else None
                continue
            if not (bond and scene):
                continue
            am = ASK_RE.match(raw)
            if am:
                q = _quotes(am.group(2))
                if q:
                    data.setdefault(bond, {}).setdefault(scene, []).append(
                        (q[0], {})
                    )
                continue
            nm = ANSWER_RE.match(raw)
            if nm:
                bucket = data.get(bond, {}).get(scene)
                if not bucket:
                    continue
                lines = _quotes(nm.group(2))
                if lines:
                    bucket[-1][1][ANSWER_KEY[nm.group(1)]] = lines

    chunks = [
        '"""羁绊交互问答台词数据（docs/character/bond_dialogues_*.md 抽取）。\n\n'
        "改台词请改分册后重跑：python battle/tools/_extract_bond_dialogues.py\n"
        '"""\n',
        "from __future__ import annotations\n\n",
        "# bond_id -> scene(enter_foe|enter_ally|duel)\n"
        "#   -> ((问, {答案键: (等价答句, ...)}), ...)\n"
        "# 答案键：reply（登场）/ accept（应战）/ reject（拒战）\n",
        "BOND_DIALOGUES: dict[\n"
        "    str, dict[str, tuple[tuple[str, dict[str, tuple[str, ...]]], ...]]\n"
        "] = {\n",
    ]
    q_total = a_total = 0
    for bond in sorted(data):
        chunks.append(f"    {bond!r}: {{\n")
        for scene in sorted(data[bond]):
            chunks.append(f"        {scene!r}: (\n")
            for question, answers in data[bond][scene]:
                q_total += 1
                a_total += sum(len(v) for v in answers.values())
                chunks.append(f"            ({question!r}, {answers!r}),\n")
            chunks.append("        ),\n")
        chunks.append("    },\n")
    chunks.append("}\n")
    OUT.write_text("".join(chunks), encoding="utf-8")
    print(
        f"wrote voice_bond_data bonds={len(data)} questions={q_total} "
        f"answers={a_total} (from {len(files)} 分册)"
    )


if __name__ == "__main__":
    main()
