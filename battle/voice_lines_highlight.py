"""专属高光台词：核心卡的高光时刻发 trait_trigger（effect=highlight）。

权威文案：docs/character/*.md「#### 高光 highlight」；机器表：voice_highlight_data.py
（`python battle/tools/_extract_duel_voice.py` 抽取生成）。

池 key ＝**高光名**（如 `divine_punishment` 宙斯神罚），缺则回退 `generic`。
台词轮换确定性（trait_line_seq），不消耗 RNG。**独立组根（parent_seq=0）**：
契约不允许 trait_trigger 带 parent 时自开新组，而台词必须自成一个 TraitLine
播放单元；紧随其后的高光组走标准 cut-in 取景（hint.cut_in=highlight）。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle.voice_highlight_data import HIGHLIGHT_LINES

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState


def pick_highlight_pool(
    speaker_template: str, highlight_key: str,
) -> tuple[str, tuple[str, ...]] | None:
    """选高光池：专属池 → generic。无词库则 None。"""
    by_scene = HIGHLIGHT_LINES.get(speaker_template, {}).get("highlight")
    if not by_scene:
        return None
    for key in (highlight_key, "generic"):
        lines = by_scene.get(key)
        if lines:
            return key, lines
    return None


def emit_highlight_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    highlight_key: str,
) -> int:
    """发一条专属高光台词（独立组根，客户端 TraitLine 阻塞播完再进高光组）。

    无词库或空池 → 静默 0（高光演出照常，只是没台词）。
    """
    picked = pick_highlight_pool(speaker.template_id, highlight_key)
    if picked is None:
        return 0
    pool_key, lines = picked
    rot_key = f"highlight:{pool_key}"
    idx = speaker.trait_line_seq.get(rot_key, 0)
    speaker.trait_line_seq[rot_key] = idx + 1
    line = lines[idx % len(lines)]
    trait_id = speaker.trait_id or "voice"
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": speaker.hero_id,
            "trait_id": trait_id,
            "effect": "highlight",
            "line": line,
        },
        parent_seq=0,
    )
