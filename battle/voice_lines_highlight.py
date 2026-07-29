"""高光台词与**巨伤台词**（共用同一词池）：发 trait_trigger。

权威文案：docs/character/*.md「#### 高光 highlight」；机器表：voice_highlight_data.py
（`python battle/tools/_extract_duel_voice.py` 抽取生成）。

两类事件共用高光池（2026-07-28）：

| 场景 | effect | 触发 | 选池顺序 |
|---|---|---|---|
| 高光 | `highlight` | 武将专属高光释放（`hint.cut_in=highlight`） | `{高光名}_{对象}` → `{高光名}` → `{对象}` → `generic` |
| 巨伤 | `massive` | 单条伤害 > 巨伤阈值（`engine.MASSIVE_LINE_THRESHOLD`） | `massive_{对象}` → `massive` → `{对象}` → `generic` |

「高光」是武将专属机制的高光（多数武将为空），「巨伤」任何武将都可能触发，
故限次：**每武将每回合至多 1 条**（引擎侧记账）。带对象的池 key 即
「对羁绊武将特殊配置」——分册写 `**divine_punishment_poseidon**` 或 `**poseidon**`。

台词随机走 seed 派生哈希流（`voice_rng.py`），不消耗战斗 RNG。高光台词**独立
组根（parent_seq=0）**：契约不允许 trait_trigger 带 parent 时自开新组，而台词必须
自成一个 TraitLine 播放单元；紧随其后的高光组走标准 cut-in（hint.cut_in=highlight）。
巨伤台词相反，**挂在那条伤害上**（同组内抽成独占 TraitLine），否则会被排到整段
出击（含阵亡）之后——人死了还在说话（同 P-72 教训）。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle import voice_rng as vr
from battle.voice_highlight_data import HIGHLIGHT_LINES

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState


def pick_highlight_pool(
    speaker_template: str,
    highlight_key: str,
    target_template: str | None = None,
) -> tuple[str, tuple[str, ...]] | None:
    """选高光/巨伤池：对象专配 → 高光名 → 对象通用 → generic。无词库则 None。"""
    by_scene = HIGHLIGHT_LINES.get(speaker_template, {}).get("highlight")
    if not by_scene:
        return None
    candidates = []
    if target_template:
        candidates.append(f"{highlight_key}_{target_template}")
    candidates.append(highlight_key)
    if target_template:
        candidates.append(target_template)
    candidates.append("generic")
    for key in candidates:
        lines = by_scene.get(key)
        if lines:
            return key, lines
    return None


def _emit(
    engine: "SeriesEngine", speaker: "HeroState", effect: str,
    line: str, parent_seq: int,
) -> int:
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": speaker.hero_id,
            "trait_id": speaker.trait_id or "voice",
            "effect": effect,
            "line": line,
        },
        parent_seq=parent_seq,
    )


def emit_highlight_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    highlight_key: str,
    target_template: str | None = None,
) -> int:
    """发一条专属高光台词（独立组根，客户端 TraitLine 阻塞播完再进高光组）。

    无词库或空池 → 静默 0（高光演出照常，只是没台词）。
    """
    picked = pick_highlight_pool(speaker.template_id, highlight_key, target_template)
    if picked is None:
        return 0
    pool_key, lines = picked
    line = vr.pick(engine.rng.seed, speaker, f"highlight:{pool_key}", lines)
    if not line:
        return 0
    return _emit(engine, speaker, "highlight", line, 0)


def emit_massive_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    target: "HeroState",
    damage_seq: int,
) -> int:
    """发一条巨伤台词（effect=massive，挂在该条伤害上）。与高光共用词池。

    限次由调用方（引擎）把关：每武将每回合至多 1 条。
    """
    picked = pick_highlight_pool(
        speaker.template_id, "massive", target.template_id,
    )
    if picked is None:
        return 0
    pool_key, lines = picked
    line = vr.pick(engine.rng.seed, speaker, f"massive:{pool_key}", lines)
    if not line:
        return 0
    return _emit(engine, speaker, "massive", line, damage_seq)
