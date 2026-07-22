"""击杀台词：hero_defeated 后由**击杀者**对**死者**发 trait_trigger（effect=kill）。

规则（2026-07-22）：
- 视角：我刚亲手杀了你（character.md §2.2）；羁绊池 key=死者 template_id，
  无则 generic。击杀恒为敌对语境，不做友/敌分池（镜像对局的羁绊池
  本身已写成诀别/痛惜口径）。
- 时点：挂 hero_defeated 同组（parent=defeat_seq），客户端 TraitLineExtract
  抽成独占气泡，紧跟阵亡倒下之后播。
- 击杀者已阵亡（互杀/反弹收尾）或击杀者==死者（自伤致死）→ 静默。
- 轮换键 kill:{pool}，不耗 RNG（确定性）。
"""
from __future__ import annotations

from typing import TYPE_CHECKING

from battle.voice_kill_data import KILL_LINES

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState


def pick_kill_pool(
    killer_template: str, victim_template: str,
) -> tuple[str, tuple[str, ...]] | None:
    by_scene = KILL_LINES.get(killer_template, {}).get("kill")
    if not by_scene:
        return None
    lines = by_scene.get(victim_template)
    if lines:
        return victim_template, lines
    generic = by_scene.get("generic")
    if generic:
        return "generic", generic
    return None


def emit_kill_line(
    engine: "SeriesEngine",
    killer: "HeroState",
    victim: "HeroState",
    parent_seq: int,
) -> int:
    """发击杀台词；无词库/自杀/击杀者已亡则静默 0。"""
    if killer.hero_id == victim.hero_id or not killer.is_alive():
        return 0
    picked = pick_kill_pool(killer.template_id, victim.template_id)
    if picked is None:
        return 0
    pool_key, lines = picked
    if not lines:
        return 0
    rot_key = f"kill:{pool_key}"
    idx = killer.trait_line_seq.get(rot_key, 0)
    killer.trait_line_seq[rot_key] = idx + 1
    line = lines[idx % len(lines)]
    trait_id = killer.trait_id or "voice"
    return engine.writer.emit(
        "trait_trigger",
        {
            "hero_id": killer.hero_id,
            "trait_id": trait_id,
            "effect": "kill",
            "line": line,
        },
        parent_seq=parent_seq,
    )
