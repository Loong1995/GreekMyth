"""状态台词（控制 / 犹豫 / 先攻）：临「产生影响」的执行节点发 trait_trigger。

契约复用 trait_trigger：`trait_id="status"`，`effect=<status_id>`，`line` 为轮换台词。
确定性轮换（hero.trait_line_seq），不消耗 RNG。

发送点（parent_seq=0 自成 TraitLine 播放组，客户端弹气泡）：
- silence：因缄默跳过主动时
- disarm/fear：因禁普攻跳过普攻时
- ming_lock/petrify：全禁 skipped 或分项跳过时
- hesitation：写出 delayed 行动前
- charm：魅惑改选人前（同窗一次）
- first_strike：先攻改序后紧随 action_start
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState

# 每类 3 条；idx % 3 轮换
LINES: dict[str, tuple[str, str, str]] = {
    "silence": (
        "喉舌被封，神力也吐不出来……",
        "想唱战歌？先把这缄默撕开！",
        "无声之境——主动战法，免谈。",
    ),
    "disarm": (
        "兵刃离手，这一拳也打不出去。",
        "缴了械，只好干瞪眼。",
        "普攻？手里空空如也。",
    ),
    "ming_lock": (
        "冥锁合拢，身与术皆不得动。",
        "锁链一紧——这回合，什么都别想。",
        "冥府的锁，连呼吸都算僭越。",
    ),
    "petrify": (
        "石化为牢，连眨眼都是奢侈。",
        "身子僵了……这一动，免了吧。",
        "石像无声，战场也当我死物。",
    ),
    "freeze": (
        "奥杰吉厄的寒潮锁住了手脚。",
        "冰锢合拢——主动与普攻，皆不可。",
        "霜封战意，动弹不得。",
    ),
    "charm": (
        "心神一晃，敌我竟分不清了……",
        "歌声在耳——刀刃指向谁，由不得我。",
        "魅惑之下，队友也像仇敌。",
    ),
    "fear": (
        "腿软了……连举刀的力气都怕。",
        "恐惧扼住喉咙，追击也散了。",
        "三首的阴影还在——不敢出手。",
    ),
    "hesitation": (
        "脚下一顿，这一手先按住。",
        "犹豫如潮，行动推到下一浪。",
        "再等等……不，已经晚了半拍。",
    ),
    "first_strike": (
        "先机在握——你们慢了半息。",
        "先攻之势，抢在雷霆落地前。",
        "谁快？我说了算。",
    ),
}

# 全禁跳过时挑一条主因（越「硬」越优先）
_SKIP_PRIORITY = (
    "petrify",
    "freeze",
    "ming_lock",
    "fear",
    "silence",
    "disarm",
)

# 禁制分项跳过时的候选台词（按优先序取持有的第一条）。
# 新增可发声控制：LINES 加词 + 在此登记，engine 零改动。
FORBID_ACTIVE_VOICE = ("silence", "ming_lock", "petrify", "freeze")
FORBID_BASIC_VOICE = ("disarm", "fear", "ming_lock", "petrify", "freeze")


def _said_set(engine: "SeriesEngine") -> set[str]:
    said = getattr(engine, "_status_voice_said", None)
    if said is None:
        said = set()
        engine._status_voice_said = said
    return said


def emit_voice_once(engine: "SeriesEngine", hero: "HeroState", status_id: str) -> int:
    """同行动窗同人同状态只发一次台词（去重集随 action_start 重置）。"""
    said = _said_set(engine)
    key = f"{hero.hero_id}:{status_id}"
    if key in said:
        return 0
    said.add(key)
    return emit_status_voice(engine, hero, status_id, parent_seq=0)


def emit_forbid_voice(
    engine: "SeriesEngine", hero: "HeroState", candidates: tuple[str, ...]
) -> int:
    """禁制分项跳过：按候选优先序找持有者身上第一条状态并弹词（同窗一次）。"""
    owned = {inst.definition.status_id for inst in engine.hero_statuses(hero.hero_id)}
    for sid in candidates:
        if sid in owned:
            return emit_voice_once(engine, hero, sid)
    return 0


def emit_status_voice(
    engine: "SeriesEngine",
    hero: "HeroState",
    status_id: str,
    *,
    parent_seq: int = 0,
) -> int:
    """发状态台词；无词库则 no-op。返回事件 seq（0=未发）。"""
    pool = LINES.get(status_id)
    if not pool:
        return 0
    key = f"status:{status_id}"
    idx = hero.trait_line_seq.get(key, 0)
    hero.trait_line_seq[key] = idx + 1
    line = pool[idx % len(pool)]
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": hero.hero_id,
            "trait_id": "status",
            "effect": status_id,
            "line": line,
        },
        parent_seq=parent_seq,
        new_group=(parent_seq == 0),
    )


def status_ids_with_mod(engine: "SeriesEngine", hero: "HeroState", mod_key: str) -> list[str]:
    """持有者身上带某修正键的状态 id（hero_order 内施加序）。"""
    out: list[str] = []
    for inst in engine.hero_statuses(hero.hero_id):
        if inst.definition.modifiers.get(mod_key):
            sid = inst.definition.status_id
            if sid not in out:
                out.append(sid)
    return out


def pick_skip_voice_id(engine: "SeriesEngine", hero: "HeroState") -> str | None:
    """行动窗完全跳过时选一条控制台词。"""
    owned = {inst.definition.status_id for inst in engine.hero_statuses(hero.hero_id)}
    for sid in _SKIP_PRIORITY:
        if sid in owned and sid in LINES:
            return sid
    return None
