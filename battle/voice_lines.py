"""单挑台词：羁绊问答（叫阵→应战/拒战）优先，否则武将双池（羁绊→generic）。

权威文案：羁绊问答 `docs/character/bond_dialogues_*.md`（机器表 `voice_bond_data.py`）；
武将扁平池 `docs/character/*.md`（机器表 `voice_duel_data.py`）。
客户端：挂在 duel 组内由 PlayDuel 按时点播（勿抽成独立 TraitLine）。

问答配对（2026-07-28）：叫阵方＝羁绊定义的 `first` 时启用问答——先派生取一条
叫阵句，应战/拒战再从**该句自己的** 3 条等价答句中取（各 9 种问答）。叫阵方是
定义的 `second`（武力序与定义序相反）时视角不成立，回退武将扁平池。
选词走 seed 派生哈希流（`voice_rng.py`），不消耗战斗 RNG。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle import bonds as bn
from battle import voice_rng as vr
from battle.voice_bond_data import BOND_DIALOGUES
from battle.voice_duel_data import DUEL_LINES

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState

DUEL_SCENES = ("duel_challenge", "duel_accept", "duel_reject")
_ANSWER_KEY = {"duel_accept": "accept", "duel_reject": "reject"}


def pick_duel_pool(
    speaker_template: str, scene: str, target_template: str,
) -> tuple[str, tuple[str, ...]] | None:
    """选武将扁平池：(pool_key, lines)。先羁绊池，否则 generic。"""
    by_scene = DUEL_LINES.get(speaker_template, {}).get(scene)
    if not by_scene:
        return None
    if target_template and by_scene.get(target_template):
        return target_template, by_scene[target_template]
    generic = by_scene.get("generic")
    if generic:
        return "generic", generic
    return None


def _emit(
    engine: "SeriesEngine", speaker: "HeroState", scene: str,
    line: str, parent_seq: int,
) -> int:
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": speaker.hero_id,
            "trait_id": speaker.trait_id or "voice",
            "effect": scene,
            "line": line,
        },
        parent_seq=parent_seq,
    )


def _bond_questions(
    speaker: "HeroState", target: "HeroState",
) -> tuple["bn.BondDef", tuple] | None:
    bond = bn.bond_of(speaker.template_id, target.template_id)
    if bond is None:
        return None
    questions = BOND_DIALOGUES.get(bond.bond_id, {}).get("duel")
    return (bond, questions) if questions else None


def _emit_bond_duel_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    scene: str,
    target: "HeroState",
    parent_seq: int,
) -> int:
    """羁绊问答路径；不适用（无分册/方向不符/无登记）返回 0。"""
    seed = engine.rng.seed
    if scene == "duel_challenge":
        found = _bond_questions(speaker, target)
        if found is None:
            return 0
        bond, questions = found
        if speaker.template_id != bond.first:
            return 0  # 视角：分册只写了 first 叫阵
        key = f"duel:{bond.bond_id}"
        occurrence = vr.next_occurrence(speaker, key)
        q_index = vr.pick_index(seed, key, occurrence, len(questions))
        engine.duel_qa[(speaker.hero_id, target.hero_id)] = (key, occurrence, q_index)
        return _emit(engine, speaker, scene, questions[q_index][0], parent_seq)

    state = engine.duel_qa.get((target.hero_id, speaker.hero_id))
    if state is None:
        return 0
    found = _bond_questions(speaker, target)
    if found is None:
        return 0
    _bond, questions = found
    key, occurrence, q_index = state
    if q_index >= len(questions):
        return 0
    answers = questions[q_index][1].get(_ANSWER_KEY[scene], ())
    line = vr.pick_with(
        seed, f"{key}:q{q_index}:{_ANSWER_KEY[scene]}", occurrence, answers,
    )
    if not line:
        return 0
    return _emit(engine, speaker, scene, line, parent_seq)


def emit_duel_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    scene: str,
    target: "HeroState",
    parent_seq: int,
) -> int:
    """发单挑台词 trait_trigger（挂 parent 与 duel 同组；客户端 PlayDuel 播）。

    优先羁绊问答对；否则武将扁平池。无词库或空池 → 静默 0。
    """
    if scene not in DUEL_SCENES:
        return 0
    seq = _emit_bond_duel_line(engine, speaker, scene, target, parent_seq)
    if seq:
        return seq
    picked = pick_duel_pool(speaker.template_id, scene, target.template_id)
    if picked is None:
        return 0
    pool_key, lines = picked
    line = vr.pick(engine.rng.seed, speaker, f"{scene}:{pool_key}", lines)
    if not line:
        return 0
    return _emit(engine, speaker, scene, line, parent_seq)
