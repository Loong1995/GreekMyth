"""登场台词：全场羁绊单元编排后发 trait_trigger（effect=enter）。

规则（2026-07-22）：
- 机器羁绊表 S1/S2（bonds.py）；场上存活双方构成一条羁绊播放单元。
- **全部**羁绊单元按序播放（非只播最高档）。
- 总序：weight↑ → 跨队伍优先 → 均速↓ → 成员 id。
- 单元内发言序：A 队优先 → 速度↓ → position → hero_id。
- 同组：首条 parent=0 为组根，其余挂 parent，客户端一个 TraitLine 播完。
- **无任何羁绊**：各队主将播 generic 登场；队序优先 A 队（setup.teams 序）。
- **友/敌分池**：同队用 `{target}`，跨队用 `{target}_foe`；缺一侧回退另一侧再 generic。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle import bonds as bn
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
    """选登场池。有目标时按同队/跨队优先友池或敌池。"""
    by_scene = ENTER_LINES.get(speaker_template, {}).get("enter")
    if not by_scene:
        return None
    if target_template:
        foe_key = f"{target_template}_foe"
        # 同队：友 → 敌回退；跨队：敌 → 友回退（兼容未补全分册）
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


def emit_enter_line(
    engine: "SeriesEngine",
    speaker: "HeroState",
    target_template: str | None,
    parent_seq: int,
    *,
    same_team: bool = True,
) -> int:
    """发一条登场台词；target_template=None 时只用 generic。无词库则静默 0。"""
    picked = pick_enter_pool(
        speaker.template_id, target_template, same_team=same_team,
    )
    if picked is None:
        return 0
    pool_key, lines = picked
    if not lines:
        return 0
    rot_key = f"enter:{pool_key}"
    idx = speaker.trait_line_seq.get(rot_key, 0)
    speaker.trait_line_seq[rot_key] = idx + 1
    line = lines[idx % len(lines)]
    trait_id = speaker.trait_id or "voice"
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": speaker.hero_id,
            "trait_id": trait_id,
            "effect": "enter",
            "line": line,
        },
        parent_seq=parent_seq,
    )


def _alive_heroes(engine: "SeriesEngine") -> list["HeroState"]:
    return [engine.heroes[hid] for hid in engine.hero_order
            if engine.heroes[hid].is_alive()]


def _team_a_id(engine: "SeriesEngine") -> str:
    return engine.setup.teams[0].team_id


def _speed(engine: "SeriesEngine", hero: "HeroState") -> int:
    return engine.effective_attr(hero, "speed")


def _collect_bond_units(
    engine: "SeriesEngine",
) -> list[tuple[int, int, int, tuple[str, ...], list["HeroState"]]]:
    """(weight, same_team_flag, -avg_speed, id_key, speakers_unsorted)."""
    alive = _alive_heroes(engine)
    seen: set[frozenset[str]] = set()
    units: list[tuple[int, int, int, tuple[str, ...], list]] = []
    for i, a in enumerate(alive):
        for b in alive[i + 1:]:
            w = bn.bond_weight(a.template_id, b.template_id)
            if w is None:
                continue
            key = frozenset({a.hero_id, b.hero_id})
            if key in seen:
                continue
            seen.add(key)
            cross = 0 if a.team_id != b.team_id else 1
            avg = (_speed(engine, a) + _speed(engine, b)) // 2
            id_key = tuple(sorted((a.hero_id, b.hero_id)))
            units.append((w, cross, -avg, id_key, [a, b]))
    units.sort(key=lambda u: (u[0], u[1], u[2], u[3]))
    return units


def _sort_speakers(
    engine: "SeriesEngine", speakers: list["HeroState"], team_a: str,
) -> list["HeroState"]:
    return sorted(
        speakers,
        key=lambda h: (
            0 if h.team_id == team_a else 1,
            -_speed(engine, h),
            h.position,
            h.hero_id,
        ),
    )


def _emit_main_generic_fallback(engine: "SeriesEngine") -> int:
    """无羁绊：各队存活主将按 setup 队序（A 优先）播 generic，同一 TraitLine 组。"""
    root_seq = 0
    emitted = 0
    for team in engine.setup.teams:
        mid = team.main_hero_id
        hero = engine.heroes.get(mid)
        if hero is None or not hero.is_alive():
            continue
        parent = 0 if root_seq == 0 else root_seq
        seq = emit_enter_line(engine, hero, None, parent)
        if seq:
            if root_seq == 0:
                root_seq = seq
            emitted += 1
    return emitted


def emit_enter_dialogues(engine: "SeriesEngine") -> int:
    """全场羁绊登场对话（全部单元按序）；无羁绊则主将 generic。返回台词条数。"""
    units = _collect_bond_units(engine)
    if not units:
        return _emit_main_generic_fallback(engine)
    team_a = _team_a_id(engine)
    emitted = 0
    for _w, _cross, _avg, _ids, members in units:
        speakers = _sort_speakers(engine, members, team_a)
        root_seq = 0
        for i, speaker in enumerate(speakers):
            other = speakers[1 - i] if len(speakers) == 2 else next(
                (s for s in speakers if s.hero_id != speaker.hero_id), speaker
            )
            parent = 0 if root_seq == 0 else root_seq
            same = speaker.team_id == other.team_id
            seq = emit_enter_line(
                engine, speaker, other.template_id, parent, same_team=same,
            )
            if seq:
                if root_seq == 0:
                    root_seq = seq
                emitted += 1
    return emitted
