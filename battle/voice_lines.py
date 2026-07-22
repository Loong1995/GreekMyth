"""单挑台词：按武将模板双池（羁绊 → generic）发 trait_trigger。

权威文案：docs/character/*.md；机器表：voice_duel_data.py（抽取工具生成）。
客户端：挂在 duel 组内由 PlayDuel 按时点播（勿抽成独立 TraitLine）。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle import bonds as bn
from battle.voice_duel_data import DUEL_LINES

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState

DUEL_SCENES = ("duel_challenge", "duel_accept", "duel_reject")


def pick_duel_pool(
    speaker_template: str, scene: str, target_template: str,
) -> tuple[str, tuple[str, ...]] | None:
    """选池：(pool_key, lines)。先羁绊 weight 更小且有词的池，否则 generic。"""
    by_scene = DUEL_LINES.get(speaker_template, {}).get(scene)
    if not by_scene:
        return None
    # 候选：目标模板池（须双方确有羁绊登记，或分册直接写了对方 key）
    candidates: list[tuple[int, str, tuple[str, ...]]] = []
    if target_template and target_template in by_scene:
        w = bn.bond_weight(speaker_template, target_template)
        # 分册写了对方 key 即可用；无机器羁绊时 weight 取 50（劣于 S1/S2）
        candidates.append((w if w is not None else 50, target_template, by_scene[target_template]))
    # 同场景其它已声明羁绊池：若目标恰好匹配（已处理）外，不另扫全场
    generic = by_scene.get("generic")
    if candidates:
        candidates.sort(key=lambda x: (x[0], x[1]))
        _w, key, lines = candidates[0]
        if lines:
            return key, lines
    if generic:
        return "generic", generic
    return None


def emit_duel_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    scene: str,
    target: "HeroState",
    parent_seq: int,
) -> int:
    """发单挑台词 trait_trigger（挂 parent 以便与 duel 同组；客户端 PlayDuel 播）。

    无词库或空池 → 静默 0。轮换键 scene:pool，不耗 RNG。
    """
    if scene not in DUEL_SCENES:
        return 0
    picked = pick_duel_pool(speaker.template_id, scene, target.template_id)
    if picked is None:
        return 0
    pool_key, lines = picked
    if not lines:
        return 0
    rot_key = f"{scene}:{pool_key}"
    idx = speaker.trait_line_seq.get(rot_key, 0)
    speaker.trait_line_seq[rot_key] = idx + 1
    line = lines[idx % len(lines)]
    trait_id = speaker.trait_id or "voice"
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": speaker.hero_id,
            "trait_id": trait_id,
            "effect": scene,
            "line": line,
        },
        parent_seq=parent_seq,
    )
