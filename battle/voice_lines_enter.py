"""登场台词：羁绊问答对依序播 trait_trigger（effect=enter）。

规则（2026-07-28 升级）：
- 单元＝一条场上羁绊（`bonds.py` S1/S2 机器表）。**全部**单元按序播。
- 单元总序：**跨队优先**（先与对方队伍的羁绊，再与本方队伍的羁绊）
  → 羁绊表**定义序**（`BondDef.order`）。weight 不再参与排序（定义序已含档位）。
- 单元内发言序＝**羁绊定义方向**：`BondDef.first` 发问、`second` 作答，
  形成「有问有答」。问从该场景 3 问中派生随机取一条，答从**该问自己的**
  3 条等价答句中取（登场＝9 种问答；`voice_bond_data.py`）。
- 回退：该羁绊未写问答分册时，退回按武将扁平池（`voice_enter_data.py`，
  同队 `{target}` / 跨队 `{target}_foe`），单向各说一句。
- **无任何羁绊**：只播各队存活主将的 generic 登场（队序 A 优先）。
- 同组：首条 parent=0 为组根，其余挂 parent，客户端一个 TraitLine 播完。
- 选词随机：seed 派生哈希流（`voice_rng.py`），**不消耗战斗 RNG**。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle import bonds as bn
from battle import voice_rng as vr
from battle.voice_bond_data import BOND_DIALOGUES
from battle.voice_enter_data import ENTER_LINES

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState


def pick_enter_pool(
    speaker_template: str,
    target_template: str | None,
    *,
    same_team: bool = True,
) -> tuple[str, tuple[str, ...]] | None:
    """选登场回退池（按武将扁平池）。有目标时按同队/跨队优先友池或敌池。"""
    by_scene = ENTER_LINES.get(speaker_template, {}).get("enter")
    if not by_scene:
        return None
    if target_template:
        foe_key = f"{target_template}_foe"
        order = (
            (target_template, foe_key) if same_team
            else (foe_key, target_template)
        )
        for key in order:
            lines = by_scene.get(key)
            if lines:
                return key, lines
    generic = by_scene.get("generic")
    if generic:
        return "generic", generic
    return None


def _emit(
    engine: "SeriesEngine", speaker: "HeroState", line: str, parent_seq: int,
) -> int:
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": speaker.hero_id,
            "trait_id": speaker.trait_id or "voice",
            "effect": "enter",
            "line": line,
        },
        parent_seq=parent_seq,
    )


def emit_enter_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    target_template: str | None,
    parent_seq: int,
    *,
    same_team: bool = True,
) -> int:
    """发一条登场台词（回退路径）；target_template=None 时只用 generic。"""
    picked = pick_enter_pool(
        speaker.template_id, target_template, same_team=same_team,
    )
    if picked is None:
        return 0
    pool_key, lines = picked
    line = vr.pick(engine.rng.seed, speaker, f"enter:{pool_key}", lines)
    if not line:
        return 0
    return _emit(engine, speaker, line, parent_seq)


def _alive_heroes(engine: "SeriesEngine") -> list["HeroState"]:
    return [engine.heroes[hid] for hid in engine.hero_order
            if engine.heroes[hid].is_alive()]


def _collect_bond_units(
    engine: "SeriesEngine",
) -> list[tuple[int, int, "bn.BondDef", "HeroState", "HeroState"]]:
    """(cross_flag, 定义序, 羁绊, 发问者, 作答者)，已按播放序排好。

    cross_flag：0＝跨队（先播），1＝同队。同一对武将只成一个单元。
    """
    alive = _alive_heroes(engine)
    seen: set[frozenset[str]] = set()
    units: list[tuple[int, int, bn.BondDef, HeroState, HeroState]] = []
    for i, a in enumerate(alive):
        for b in alive[i + 1:]:
            d = bn.bond_of(a.template_id, b.template_id)
            if d is None:
                continue
            key = frozenset({a.hero_id, b.hero_id})
            if key in seen:
                continue
            seen.add(key)
            asker, answerer = (a, b) if a.template_id == d.first else (b, a)
            cross = 0 if a.team_id != b.team_id else 1
            units.append((cross, d.order, d, asker, answerer))
    # 跨队优先 → 定义序 → hero_id（同定义序的镜像对局兜底稳定序）
    units.sort(key=lambda u: (u[0], u[1], u[3].hero_id, u[4].hero_id))
    return units


def _emit_bond_dialogue(
    engine: "SeriesEngine",
    bond: "bn.BondDef",
    asker: "HeroState",
    answerer: "HeroState",
    *,
    cross: bool,
) -> int:
    """一条羁绊的问答对（问 → 该问的答）。无问答分册则返回 -1 表示需回退。"""
    scene = "enter_foe" if cross else "enter_ally"
    questions = BOND_DIALOGUES.get(bond.bond_id, {}).get(scene)
    if not questions:
        return -1
    seed = engine.rng.seed
    key = f"enter:{bond.bond_id}:{scene}"
    occurrence = vr.next_occurrence(asker, key)
    q_index = vr.pick_index(seed, key, occurrence, len(questions))
    question, answers = questions[q_index]
    root_seq = _emit(engine, asker, question, 0)
    emitted = 1 if root_seq else 0
    reply = vr.pick_with(
        seed, f"{key}:q{q_index}:reply", occurrence, answers.get("reply", ()),
    )
    if reply and root_seq and _emit(engine, answerer, reply, root_seq):
        emitted += 1
    return emitted


def _emit_main_generic_fallback(engine: "SeriesEngine") -> int:
    """无羁绊：各队存活主将按 setup 队序（A 优先）播 generic，同一 TraitLine 组。"""
    root_seq = 0
    emitted = 0
    for team in engine.setup.teams:
        hero = engine.heroes.get(team.main_hero_id)
        if hero is None or not hero.is_alive():
            continue
        seq = emit_enter_line(engine, hero, None, 0 if root_seq == 0 else root_seq)
        if seq:
            if root_seq == 0:
                root_seq = seq
            emitted += 1
    return emitted


def emit_enter_dialogues(engine: "SeriesEngine") -> int:
    """全场羁绊登场问答（跨队先、再同队，各按定义序）；无羁绊则主将 generic。"""
    units = _collect_bond_units(engine)
    if not units:
        return _emit_main_generic_fallback(engine)
    emitted = 0
    for cross_flag, _order, bond, asker, answerer in units:
        cross = cross_flag == 0
        got = _emit_bond_dialogue(engine, bond, asker, answerer, cross=cross)
        if got >= 0:
            emitted += got
            continue
        # 回退：按武将扁平池，双方各说一句（问答不配对）
        root_seq = 0
        for speaker, other in ((asker, answerer), (answerer, asker)):
            seq = emit_enter_line(
                engine, speaker, other.template_id,
                0 if root_seq == 0 else root_seq,
                same_team=not cross,
            )
            if seq:
                if root_seq == 0:
                    root_seq = seq
                emitted += 1
    return emitted
