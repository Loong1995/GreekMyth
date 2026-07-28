"""从 docs/character/*.md 抽取台词 → voice_duel_data.py / voice_enter_data.py。

兼容三种写法：
  - 「a」｜「b」｜「c」
  - ①a ②b ③c
  - a／b／c（无引号）
池 key 可写 `hector` / `→hector`；一行多 key（`**→a**／**→b**`）共享同组词。
登场友/敌（2026-07-22）：`**hector**（友）` → hector；`**hector**（敌）` → hector_foe。
未标友/敌的旧行仍写入 plain key（选池跨队会回退）。
场景：`duel_*` → voice_duel_data；`enter` → voice_enter_data；
`kill` → voice_kill_data（击杀者→死者，恒敌对，无友/敌分池）；
`highlight` → voice_highlight_data（专属高光，池 key＝高光名如 divine_punishment，
无对象，回退 generic）。
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHAR = ROOT / "docs" / "character"

HERO_RE = re.compile(r"^## (\w+)\s*\u00b7")
SCENE_RE = re.compile(
    r"`(duel_\w+|enter|kill|highlight)`|(duel_challenge|duel_accept|duel_reject)"
    r"|登场|击杀|高光"
)
# 捕获 **key** 及其后可选括号标签（友/敌/S1 等）；一行可多 key
KEY_RE = re.compile(
    r"\*\*(?:→)?([a-z][a-z0-9_]*|通用)\*\*(（[^）]*）)?"
)
CIRCLED = re.compile(r"[①②③④⑤⑥⑦⑧⑨⑩]([^①②③④⑤⑥⑦⑧⑨⑩]+)")
QUOTE_RE = re.compile(r"「([^」]+)」")


def _pool_keys(line: str) -> list[str]:
    """从台词行解析池 key。

    行内任一 key 标了（敌）→ 该行全部非 generic key 写为 {id}_foe
    （兼容 `**a**／**b**（敌）` 只标在末 key 上）。
    """
    matches = list(KEY_RE.finditer(line))
    if not matches:
        return []
    any_foe = any(m.group(2) and "敌" in m.group(2) for m in matches)
    keys: list[str] = []
    for m in matches:
        raw = "generic" if m.group(1) == "通用" else m.group(1)
        if raw != "generic" and any_foe:
            keys.append(f"{raw}_foe")
        else:
            keys.append(raw)
    return keys


def _split_lines(body: str) -> tuple[str, ...]:
    body = body.strip().rstrip("。").strip()
    quotes = QUOTE_RE.findall(body)
    if quotes:
        return tuple(q.strip() for q in quotes if q.strip())
    circled = CIRCLED.findall(body)
    if circled:
        return tuple(s.strip().rstrip("。").strip() for s in circled if s.strip())
    if "：" in body:
        body = body.split("：", 1)[-1]
    elif ":" in body:
        body = body.split(":", 1)[-1]
    parts = re.split(r"[／|/｜]", body)
    return tuple(p.strip().rstrip("。").strip() for p in parts if p.strip())


def _parse_scene(header: str) -> str | None:
    if ("duel_" in header or "`enter`" in header or "登场" in header
            or "`kill`" in header or "击杀" in header
            or "`highlight`" in header or "高光" in header):
        sm = SCENE_RE.search(header)
        if not sm:
            return None
        if sm.group(1):
            return sm.group(1)
        if sm.group(2):
            return sm.group(2)
        if "击杀" in header:  # 无反引号时按中文标题
            return "kill"
        return "highlight" if "高光" in header else "enter"
    return None


def _dump(path: Path, var_name: str, data: dict, header: str) -> int:
    chunks = [
        f'"""{header}\n\n'
        "改台词请改分册后重跑：python battle/tools/_extract_duel_voice.py\n"
        '"""\n',
        "from __future__ import annotations\n\n",
        f"# template_id -> scene -> pool_key(target_template|generic) -> lines\n",
        f"{var_name}: dict[str, dict[str, dict[str, tuple[str, ...]]]] = {{\n",
    ]
    n = 0
    for h in sorted(data):
        chunks.append(f"    {h!r}: {{\n")
        for scene in sorted(data[h]):
            chunks.append(f"        {scene!r}: {{\n")
            for pool, quotes in data[h][scene].items():
                chunks.append(f"            {pool!r}: {quotes!r},\n")
                n += len(quotes)
            chunks.append("        },\n")
        chunks.append("    },\n")
    chunks.append("}\n")
    path.write_text("".join(chunks), encoding="utf-8")
    return n


def main() -> None:
    duel: dict = {}
    enter: dict = {}
    kill: dict = {}
    highlight: dict = {}
    for path in sorted(CHAR.glob("*.md")):
        if path.name == "bonds.md":
            continue
        hero = None
        scene = None
        for line in path.read_text(encoding="utf-8").splitlines():
            hm = HERO_RE.match(line)
            if hm:
                hero = hm.group(1)
                scene = None
                continue
            if hero and line.startswith("####"):
                scene = _parse_scene(line)
                continue
            if not (hero and scene and line.startswith("- **")):
                continue
            keys = _pool_keys(line)
            if not keys:
                continue
            body = KEY_RE.sub("", line[2:])
            body = re.sub(r"^[\s：:／/|｜]+", "", body)
            quotes = _split_lines(body)
            if not quotes:
                continue
            if scene.startswith("duel_"):
                bucket = duel
            elif scene == "kill":
                bucket = kill
            elif scene == "highlight":
                bucket = highlight
            else:
                bucket = enter
            pools = bucket.setdefault(hero, {}).setdefault(scene, {})
            for key in keys:
                pools[key] = quotes

    nd = _dump(
        ROOT / "battle" / "voice_duel_data.py",
        "DUEL_LINES",
        duel,
        "单挑台词数据（docs/character 分册抽取）。",
    )
    ne = _dump(
        ROOT / "battle" / "voice_enter_data.py",
        "ENTER_LINES",
        enter,
        "登场台词数据（docs/character 分册抽取）。",
    )
    nk = _dump(
        ROOT / "battle" / "voice_kill_data.py",
        "KILL_LINES",
        kill,
        "击杀台词数据（docs/character 分册抽取）。",
    )
    nh = _dump(
        ROOT / "battle" / "voice_highlight_data.py",
        "HIGHLIGHT_LINES",
        highlight,
        "专属高光台词数据（docs/character 分册抽取）。",
    )
    print(
        f"wrote voice_duel_data heroes={len(duel)} slots={nd}; "
        f"voice_enter_data heroes={len(enter)} slots={ne}; "
        f"voice_kill_data heroes={len(kill)} slots={nk}; "
        f"voice_highlight_data heroes={len(highlight)} slots={nh}"
    )


if __name__ == "__main__":
    main()
