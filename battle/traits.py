from __future__ import annotations

"""性格系统（Phase 3 §二/§六）：每武将一条性格，对战斗机制有强制修正。

- Trait 子类实现具体钩子；REGISTRY 注册（trait_id → 实例，无状态）。
- 触发点位由引擎在固定位置调用（见 docs/mechanics/traits.md）：
    attr_bonus            有效属性聚合（多情/借宝/忠勇/护主/光明…静态或条件面板加成）
    on_round_start        回合开始逐武将（分神/匠心旁骛/鲁莽/谋深/怒涛/畏战…回合 roll）
    hesitation_immune     犹豫判定豁免（明睿/威权恒免；谋深按回合 roll 结果）
    force_basic_target    普攻/自由选敌强制目标（怒涛）或随机目标（好战/逐苹）
    prefer_target         自由选敌偏好（狡黠后排 / 鲁莽统率最高）
    damage_out_bonus      造成伤害的临时增伤（记仇 +25% / 鲁莽 +15%）
    damage_in_reduce      受到伤害减免（魅惑 -10%）
    crit_damage_bonus     会心/奇谋伤害加成（巧射 +15% / 冷酷 +10%）
    basic_lifesteal       普攻吸血（贪食 10% / 暴食 8%）
    heal_up_bonus         治疗量加成（仁心 +15% / 师者 +10% / 柔波 +10%）
    flip_heal_lowest      治疗兵力最低单位前判定改治疗对面（仁心 20%）
    forced_crit_on_taken  受击判定使该次攻击必定暴击（踵之弱 15%）
    pursuit_boost         追伤最终伤害倍率（傲慢 无条件 25% 判定 ×1.5）
    attr_drain_multiplier 吸取属性效果翻倍（威权 20% ×2）
    on_kill               己方击杀后（求胜四维+10）
    on_any_defeat         任意武将阵亡后（好战 15% 额外行动一轮）
    on_petrify_out        石化别人时（孤怨 8% 照影自身石化）
    block_denied          本回合无法获得格挡（坚忍 5% 执拗）
    trait_flag(key)       回合级抑制旗标：oracle_suppressed（分神/匠心旁骛）、
                          own_skill_disabled（号角走音）、postpone（畏战/算计过深）

- 台词：仅任务书标注"播放台词"的触发发 trait_trigger 事件（契约 1.2.0 新增），
  payload = {hero_id, trait_id, effect, line}；台词按触发次数确定性轮换
  （hero.trait_line_seq，不消耗 RNG）。纯数值静默修正不发事件。
- 测试开关：BattleSetup.metadata["trait_rate_overrides"] = {"trait_id.key": bps}
  可覆盖任意判定概率（高概率测试版），正式默认按表。
"""

from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from battle.engine import SeriesEngine
    from battle.heroes import HeroState

BPS = 10000


@dataclass(frozen=True)
class Trait:
    """性格基类：默认全部无效果；子类按需覆盖钩子。lines[effect] = 台词轮换表。"""

    trait_id: str
    name: str
    lines: dict[str, tuple[str, ...]] = field(default_factory=dict)

    # ---- 面板 ----
    def attr_bonus(self, engine: "SeriesEngine", hero: "HeroState", attr: str) -> int:
        return 0

    # ---- 回合 roll（设置回合旗标 / 发台词）----
    def on_round_start(self, engine: "SeriesEngine", hero: "HeroState", parent_seq: int) -> None:
        return None

    # ---- 行动/判定修正 ----
    def hesitation_immune(self, engine: "SeriesEngine", hero: "HeroState") -> bool:
        return False

    def force_basic_target(self, engine, hero, reason: str):
        """返回强制/随机目标（HeroState）或 None=不干预。"""
        return None

    def prefer_target(self, engine, hero, candidates: list, reason: str):
        """自由选敌偏好：返回子集或单目标列表；None=不干预。"""
        return None

    def damage_out_bonus(self, engine, source, target, kind: str,
                         parent_seq: int = 0) -> int:
        return 0

    def damage_in_reduce(self, engine, target) -> int:
        return 0

    def crit_damage_bonus(self, engine, hero) -> int:
        return 0

    def basic_lifesteal(self, engine, hero) -> int:
        return 0

    def heal_up_bonus(self, engine, healer) -> int:
        return 0

    def flip_heal_lowest(self, engine, healer, parent_seq: int) -> bool:
        return False

    def forced_crit_on_taken(self, engine, target, parent_seq: int) -> bool:
        return False

    def pursuit_boost_bps(self, engine, source, target, parent_seq: int) -> int:
        """追伤最终伤害倍率加成（bps 增量，0=不加成）。"""
        return 0

    def attr_drain_multiplier(self, engine, hero, parent_seq: int) -> int:
        return 1

    def on_kill(self, engine, hero, killer, victim, parent_seq: int) -> None:
        """己方（hero 所在队）发生击杀后；hero 为性格持有者。"""
        return None

    def on_any_defeat(self, engine, hero, victim, parent_seq: int) -> bool:
        """返回 True = 额外行动一轮（引擎排队执行）。"""
        return False

    def on_petrify_out(self, engine, source, parent_seq: int) -> None:
        return None

    def block_denied(self, engine, hero) -> bool:
        return engine.trait_flag(hero.hero_id, "block_denied")

    def burst_rate_bonus(self, engine, hero, skill) -> int:
        """连发率加成 bps（Phase 4：借宝按己方神阵营人数 / 忠勇看波塞冬存活）。"""
        return 0

    def ally_damage_in_bonus(self, engine, holder, source, target) -> int:
        """队友承伤加成 bps（Phase 4 魅惑 v4：敌方对塞壬同阵营队友伤害+10%）。
        holder=性格持有者（存活、与 target 同队、非 target 本人），
        返回值加法叠入攻击方 damage_up 乘区。"""
        return 0

    def on_skill_cast(self, engine, hero, skill, trigger_seq: int) -> None:
        """自身主动战法每次成功释放后（Phase 4 忠烈连发层数；含连发的每一发）。"""
        return None

    def on_ally_combo(self, engine, hero, attacker, parent_seq: int) -> None:
        """己方任意单位触发连击后（Phase 4 号召）。hero=性格持有者。"""
        return None

    def on_ally_basic(self, engine, hero, attacker, target, parent_seq: int) -> None:
        """己方其他单位普攻每击结算后（Phase 4 并辔；先于状态协击钩子分发）。"""
        return None

    def on_round_end(self, engine, hero, parent_seq: int, round_no: int) -> None:
        """回合结束（持有者存活时）；羁留援手等。"""
        return None


REGISTRY: dict[str, Trait] = {}


# =============================================================================
# 单挑 / 登场台词：见 battle/voice_lines.py、voice_lines_enter.py。
# =============================================================================

def emit_duel_line(engine: "SeriesEngine", hero: "HeroState", effect: str,
                   parent_seq: int, *, target: "HeroState") -> int:
    """转发至 voice_lines（保留 traits 入口，避免引擎多处改 import）。"""
    from battle import voice_lines as vl
    return vl.emit_duel_line(engine, hero, effect, target, parent_seq)


def register(trait: Trait) -> Trait:
    if trait.trait_id in REGISTRY:
        raise ValueError(f"trait_id 重复注册: {trait.trait_id}")
    REGISTRY[trait.trait_id] = trait
    return trait


def of(hero: "HeroState") -> Trait | None:
    return REGISTRY.get(hero.trait_id) if hero.trait_id else None


def emit_trigger(engine: "SeriesEngine", hero: "HeroState", effect: str,
                 *, parent_seq: int = 0, new_group: bool | None = None) -> int:
    """发 trait_trigger 事件（契约 1.2.0 加法演进）。台词确定性轮换，不消耗 RNG。
    new_group：None 时沿用默认（无 parent → 新组；有 parent → 同组）。"""
    trait = REGISTRY[hero.trait_id]
    pool = trait.lines.get(effect, ())
    idx = hero.trait_line_seq.get(effect, 0)
    hero.trait_line_seq[effect] = idx + 1
    line = pool[idx % len(pool)] if pool else ""
    ng = (parent_seq == 0) if new_group is None else new_group
    return engine.writer.emit(
        "trait_trigger",
        {"hero_id": hero.hero_id, "trait_id": trait.trait_id,
         "effect": effect, "line": line},
        parent_seq=parent_seq,
        new_group=ng,
    )


def rate(engine: "SeriesEngine", trait_id: str, key: str, default_bps: int) -> int:
    """判定概率（可被 metadata.trait_rate_overrides 覆盖，测试高概率版用）。"""
    overrides = engine.setup.metadata.get("trait_rate_overrides", {})
    return int(overrides.get(f"{trait_id}.{key}", default_bps))


# =============================================================================
# 神阵营
# =============================================================================

@dataclass(frozen=True)
class _Duoqing(Trait):
    """宙斯·多情：全场每存活一名女性武将智力+8；每回合开始对方每有一名女武将
    8% 分神（独立判定），分神则本回合雷霆不触发（oracle_suppressed 旗标+台词）。"""

    def attr_bonus(self, engine, hero, attr):
        if attr != "intelligence":
            return 0
        n = sum(
            1 for hid in engine.hero_order
            if engine.heroes[hid].is_alive() and engine.heroes[hid].gender == "f"
        )
        return n * 8

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "distract", 800)
        enemy_females = [
            h for h in engine.alive_enemies(hero) if h.gender == "f"
        ]
        for female in enemy_females:  # 依次独立判定（确定序）
            if engine.rng.rand_bps("trait", f"{hero.hero_id}:distract:{female.hero_id}") < r:
                engine.set_trait_flag(hero.hero_id, "oracle_suppressed")
                # 台词按被谁分神选专属故事台词（distract_<template_id>），
                # 池外女将（自定义等）回退通用 distract
                effect = f"distract_{female.template_id}"
                if effect not in self.lines:
                    effect = "distract"
                emit_trigger(engine, hero, effect, parent_seq=parent_seq)


@dataclass(frozen=True)
class _Mingrui(Trait):
    """雅典娜·明睿：不受犹豫影响、智力+5；回合开始 8% 匠心旁骛本回合圣盾不生效。"""

    def attr_bonus(self, engine, hero, attr):
        return 5 if attr == "intelligence" else 0

    def hesitation_immune(self, engine, hero):
        return True

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "lapse", 800)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:lapse") < r:
            engine.set_trait_flag(hero.hero_id, "oracle_suppressed")
            emit_trigger(engine, hero, "lapse", parent_seq=parent_seq)


@dataclass(frozen=True)
class _Haozhan(Trait):
    """阿瑞斯·好战：任意单位阵亡后 15% 当场行动一轮（v4：每回合最多 1 次）；
    常驻 8% 普攻目标完全随机。"""

    def on_any_defeat(self, engine, hero, victim, parent_seq):
        if engine.trait_flag(hero.hero_id, "haozhan_extra_used"):
            return False  # v4：每回合最多触发 1 次
        r = rate(engine, self.trait_id, "extra_action", 1500)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:extra") < r:
            engine.set_trait_flag(hero.hero_id, "haozhan_extra_used")
            emit_trigger(engine, hero, "extra_action", parent_seq=parent_seq)
            return True
        return False

    def force_basic_target(self, engine, hero, reason):
        if not reason.startswith("basic"):
            return None
        r = rate(engine, self.trait_id, "wild", 800)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:wild") < r:
            pool = engine.alive_enemies(hero)
            if pool:
                target = pool[engine.rng.rand_index(len(pool), "trait", f"{hero.hero_id}:wildpick")]
                emit_trigger(engine, hero, "wild")
                return target
        return None


@dataclass(frozen=True)
class _Jiaoxia(Trait):
    """赫尔墨斯·狡黠：速度+10；自由选敌类 30% 优先敌方后排
    （v4：后排 = 站位 4~6；候选中无后排/全后排时不 roll、不消耗 RNG）。"""

    def attr_bonus(self, engine, hero, attr):
        return 10 if attr == "speed" else 0

    def prefer_target(self, engine, hero, candidates, reason):
        back = [c for c in candidates if c.is_backline]
        if not back or len(back) == len(candidates):
            return None
        r = rate(engine, self.trait_id, "backline", 3000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:backline") < r:
            return back
        return None


@dataclass(frozen=True)
class _Guangming(Trait):
    """阿波罗·光明：智力+12。"""

    def attr_bonus(self, engine, hero, attr):
        return 12 if attr == "intelligence" else 0


@dataclass(frozen=True)
class _Renxin(Trait):
    """阿斯克勒庇俄斯·仁心：治疗量+15%；治疗兵力最低单位前 20% 判定改治疗对面
    兵力最低（治疗对面前播台词）。"""

    def heal_up_bonus(self, engine, healer):
        return 1500

    def flip_heal_lowest(self, engine, healer, parent_seq):
        r = rate(engine, self.trait_id, "flip", 2000)
        if engine.rng.rand_bps("trait", f"{healer.hero_id}:flip") < r:
            emit_trigger(engine, healer, "flip", parent_seq=parent_seq)
            return True
        return False


@dataclass(frozen=True)
class _Guyue(Trait):
    """阿尔忒弥斯·孤月：速度+8。"""

    def attr_bonus(self, engine, hero, attr):
        return 8 if attr == "speed" else 0


@dataclass(frozen=True)
class _Qiusheng(Trait):
    """尼刻·求胜（v4）：己方每次击败敌方后，自身四维各+10（状态层，
    最多叠 3 层；满层后静默不再加）。首层/叠层播台词。"""

    def on_kill(self, engine, hero, killer, victim, parent_seq):
        if killer.team_id != hero.team_id:
            return
        instance = engine.apply_status(hero, hero, _QIUSHENG_WIN, parent_seq=parent_seq)
        if instance is not None:
            emit_trigger(engine, hero, "win", parent_seq=parent_seq)


# =============================================================================
# 人阵营
# =============================================================================

@dataclass(frozen=True)
class _Aoman(Trait):
    """阿喀琉斯·傲慢：追伤前无条件 25% 判定成功则追伤 ×1.5
    并播贯穿台词（pierce）；受击 7.5% 踵之弱→该次攻击必定暴击
    （台词延到暴击伤害落账后，见 engine.deal_damage）。"""

    def pursuit_boost_bps(self, engine, source, target, parent_seq):
        r = rate(engine, self.trait_id, "pride", 2500)
        if engine.rng.rand_bps("trait", f"{source.hero_id}:pride") < r:
            emit_trigger(engine, source, "pierce", parent_seq=parent_seq)
            return 5000  # ×1.5
        return 0

    def forced_crit_on_taken(self, engine, target, parent_seq):
        # 2026-07-09 人工调参：踵之弱 15%→7.5%（降低一倍）
        # 只判定+挂旗，不立刻弹台词（等暴击伤害事件写出后再 emit heel）
        r = rate(engine, self.trait_id, "heel", 750)
        if engine.rng.rand_bps("trait", f"{target.hero_id}:heel") < r:
            engine.set_trait_flag(target.hero_id, "heel_line_pending")
            return True
        return False


@dataclass(frozen=True)
class _Lumang(Trait):
    """赫拉克勒斯·鲁莽：行动时 40% 一回合增伤+15%；伤害 60% 常驻优先选
    敌军统率最高者（自由选敌类）。台词均在造成伤害前弹出（boost/taunt）。"""

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "boost", 4000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:boost") < r:
            engine.set_trait_flag(hero.hero_id, "lumang_boost")
            # 台词延到本回合首次造成伤害前（damage_out_bonus）

    def damage_out_bonus(self, engine, source, target, kind, parent_seq=0):
        bonus = 1500 if engine.trait_flag(source.hero_id, "lumang_boost") else 0
        # 嘲讽选人成功：本击造成伤害前弹 taunt
        if engine.trait_flag(source.hero_id, "lumang_taunt"):
            engine.clear_trait_flag(source.hero_id, "lumang_taunt")
            emit_trigger(engine, source, "taunt", parent_seq=parent_seq)
        # 回合增伤：本回合首次造成伤害前弹 boost（只说一次）
        if (engine.trait_flag(source.hero_id, "lumang_boost")
                and not engine.trait_flag(source.hero_id, "lumang_boost_said")):
            engine.set_trait_flag(source.hero_id, "lumang_boost_said")
            emit_trigger(engine, source, "boost", parent_seq=parent_seq)
        return bonus

    def prefer_target(self, engine, hero, candidates, reason):
        r = rate(engine, self.trait_id, "taunt", 6000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:taunt") < r:
            best = max(candidates, key=lambda c: (engine.effective_attr(c, "command"),
                                                  -c.position))
            engine.set_trait_flag(hero.hero_id, "lumang_taunt")
            return [best]
        return None


@dataclass(frozen=True)
class _Moushen(Trait):
    """奥德修斯·谋深：回合开始 20% 本回合不受犹豫影响；8% 算计过深行动顺延至回合末。"""

    def on_round_start(self, engine, hero, parent_seq):
        r1 = rate(engine, self.trait_id, "immune", 2000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:immune") < r1:
            engine.set_trait_flag(hero.hero_id, "hesitation_immune")
            emit_trigger(engine, hero, "immune", parent_seq=parent_seq)
        r2 = rate(engine, self.trait_id, "overthink", 800)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:overthink") < r2:
            engine.set_trait_flag(hero.hero_id, "postpone")
            emit_trigger(engine, hero, "overthink", parent_seq=parent_seq)

    def hesitation_immune(self, engine, hero):
        return engine.trait_flag(hero.hero_id, "hesitation_immune")


@dataclass(frozen=True)
class _Jiebao(Trait):
    """珀尔修斯·借宝（v4）：己方每名奥林匹斯（神）阵营存活友军使自身**自带主动
    战法**连发率 +15%（faction 由 roster 标注；A4 更名 olympus 后随枚举同步）。"""

    def burst_rate_bonus(self, engine, hero, skill):
        if not hero.skills or skill.skill_id != hero.skills[0]:
            return 0  # 只加成自带战法（装配位 0）
        from battle.roster import FACTION_OF
        n = sum(
            1 for ally in engine.alive_allies(hero)
            if ally.hero_id != hero.hero_id
            and FACTION_OF.get(ally.template_id) == "olympus"
        )
        return n * 1500


@dataclass(frozen=True)
class _Zhuping(Trait):
    """阿塔兰忒·逐苹：速度+12；6% 金苹果本回合普攻目标随机（负触发播台词）。"""

    def attr_bonus(self, engine, hero, attr):
        return 12 if attr == "speed" else 0

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "apple", 600)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:apple") < r:
            engine.set_trait_flag(hero.hero_id, "random_basic_target")
            emit_trigger(engine, hero, "apple", parent_seq=parent_seq)

    def force_basic_target(self, engine, hero, reason):
        if not reason.startswith("basic"):
            return None
        if not engine.trait_flag(hero.hero_id, "random_basic_target"):
            return None
        pool = engine.alive_enemies(hero)
        if not pool:
            return None
        return pool[engine.rng.rand_index(len(pool), "trait", f"{hero.hero_id}:applepick")]


@dataclass(frozen=True)
class _Qiaoshe(Trait):
    """帕里斯·巧射：暴击伤害+15%；8% 畏战本回合行动顺延至最后（负触发播台词）。"""

    def crit_damage_bonus(self, engine, hero):
        return 1500

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "fear", 800)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:fear") < r:
            engine.set_trait_flag(hero.hero_id, "postpone")
            emit_trigger(engine, hero, "fear", parent_seq=parent_seq)


@dataclass(frozen=True)
class _Jianren(Trait):
    """大埃阿斯·坚忍：统率+10；5% 执拗本回合无法获得格挡（负触发播台词）。"""

    def attr_bonus(self, engine, hero, attr):
        return 10 if attr == "command" else 0

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "stubborn", 500)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:stubborn") < r:
            engine.set_trait_flag(hero.hero_id, "block_denied")
            emit_trigger(engine, hero, "stubborn", parent_seq=parent_seq)


# 喀戎·师者 已随 v4 武将池下架（manual_tasks 拍板项 2）


# =============================================================================
# 海阵营
# =============================================================================

@dataclass(frozen=True)
class _Jichou(Trait):
    """波塞冬·记仇：对最后伤害过自己的敌军伤害+25%；每回合开始 40% 怒涛难抑
    本回合所有伤害强制指向该目标（怒涛触发播台词）。"""

    def damage_out_bonus(self, engine, source, target, kind, parent_seq=0):
        return 2500 if source.last_damaged_by == target.hero_id else 0

    def on_round_start(self, engine, hero, parent_seq):
        if not hero.last_damaged_by:
            return
        grudge = engine.heroes.get(hero.last_damaged_by)
        if grudge is None or not grudge.is_alive():
            return
        r = rate(engine, self.trait_id, "rage", 4000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:rage") < r:
            engine.set_trait_flag(hero.hero_id, "force_target:" + grudge.hero_id)
            emit_trigger(engine, hero, "rage", parent_seq=parent_seq)

    def force_basic_target(self, engine, hero, reason):
        for key in engine.trait_flags(hero.hero_id):
            if key.startswith("force_target:"):
                target = engine.heroes.get(key.split(":", 1)[1])
                if target is not None and target.is_alive():
                    return target
        return None


@dataclass(frozen=True)
class _Roubo(Trait):
    """安菲特里忒·柔波：治疗量+10%。"""

    def heal_up_bonus(self, engine, healer):
        return 1000


@dataclass(frozen=True)
class _Zhongyong(Trait):
    """特里同·忠勇（v4）：波塞冬存活时自带战法连发率+30%；6% 号角走音本回合
    无法释放自带战法（负触发播台词）。"""

    def burst_rate_bonus(self, engine, hero, skill):
        if not hero.skills or skill.skill_id != hero.skills[0]:
            return 0  # 只加成自带战法（装配位 0）
        for ally in engine.alive_allies(hero):
            if ally.template_id == "poseidon":
                return 3000
        return 0

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "offkey", 600)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:offkey") < r:
            engine.set_trait_flag(hero.hero_id, "own_skill_disabled")
            emit_trigger(engine, hero, "offkey", parent_seq=parent_seq)


@dataclass(frozen=True)
class _Meihuo(Trait):
    """塞壬·魅惑（v4）：敌方对塞壬伤害-10%（减伤乘区）；敌方对塞壬同阵营
    队友伤害+10%（攻击方 damage_up 乘区，塞壬存活时生效）。"""

    def damage_in_reduce(self, engine, target):
        return 1000

    def ally_damage_in_bonus(self, engine, holder, source, target):
        return 1000


@dataclass(frozen=True)
class _Tanshi(Trait):
    """斯库拉·贪食：普攻吸血 10%。"""

    def basic_lifesteal(self, engine, hero):
        return 1000


# 卡律布狄斯·暴食 已随 v4 武将池下架（manual_tasks 拍板项 2）


# =============================================================================
# 冥阵营
# =============================================================================

@dataclass(frozen=True)
class _Weiquan(Trait):
    """哈迪斯·威权：不受犹豫影响；吸取属性效果 20% 翻倍（翻倍触发播台词）。"""

    def hesitation_immune(self, engine, hero):
        return True

    def attr_drain_multiplier(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "double", 2000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:double") < r:
            emit_trigger(engine, hero, "double", parent_seq=parent_seq)
            return 2
        return 1


@dataclass(frozen=True)
class _Guyuan(Trait):
    """美杜莎·孤怨：石化别人时 12% 照影自身石化 1 回合（触发播台词）。"""

    def on_petrify_out(self, engine, source, parent_seq):
        from battle.statuses import petrify
        r = rate(engine, self.trait_id, "mirror", 1200)
        if engine.rng.rand_bps("trait", f"{source.hero_id}:mirror") < r:
            seq = emit_trigger(engine, source, "mirror", parent_seq=parent_seq)
            engine.apply_status(source, source, petrify(1), parent_seq=seq)


@dataclass(frozen=True)
class _Huichun(Trait):
    """珀耳塞福涅·回春：每回合开始 40% 自身回复（60% 智力）兵力。"""

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "bloom", 4000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:bloom") < r:
            amount = engine.effective_attr(hero, "intelligence") * 6000 // BPS
            if amount > 0:
                engine.heal(hero, hero, parent_seq=parent_seq, can_crit=False,
                            fixed_base=amount, apply_modifiers=False)


@dataclass(frozen=True)
class _Lengku(Trait):
    """塔纳托斯·冷酷：暴击伤害+10%。"""

    def crit_damage_bonus(self, engine, hero):
        return 1000


@dataclass(frozen=True)
class _Huzhu(Trait):
    """刻耳柏洛斯·护主：哈迪斯存活时自身全属性+5。"""

    def attr_bonus(self, engine, hero, attr):
        for ally in engine.alive_allies(hero):
            if ally.template_id == "hades":
                return 5
        return 0


# =============================================================================
# 注册（台词：符合人设、简短；正/负面分列，确定性轮换）
# =============================================================================

register(_Duoqing("duoqing", "多情", {
    "distract": ("那位女将……有点眼熟。", "美，总是让雷霆迟疑。", "咳，朕方才在想什么？"),
    "distract_athena": ("从我头颅中诞生的智慧……为父看着就欣慰。",),
    "distract_artemis": ("月神的箭，让我想起她母亲勒托……",),
    "distract_nike": ("胜利女神曾为我执掌凯歌……那时的荣光。",),
    "distract_atalanta": ("跑得比风还快的姑娘……像极了当年的猎手们。",),
    "distract_amphitrite": ("海后当年，连波塞冬都追到海豚开口才点头……",),
    "distract_siren": ("那歌声……连神王也想解下缆绳听一听。",),
    "distract_scylla": ("她也曾是位美丽的宁芙，可惜了……",),
    "distract_charybdis": ("那漩涡里，藏着被我雷霆贬下的旧怨。",),
    "distract_medusa": ("别对上她的眼睛……可我偏偏想看。",),
    "distract_persephone": ("春之女儿……德墨忒尔又要来找朕诉苦了。",),
}))
register(_Mingrui("mingrui", "明睿", {
    "lapse": ("此织物的纹样……失神了。", "智慧偶尔也会走神。"),
}))
register(_Haozhan("haozhan", "好战", {
    "extra_action": ("血！还不够！", "战斗才刚刚开始！"),
    "wild": ("谁都行，让我砍！", "挡路者，死！"),
}))
register(_Jiaoxia("jiaoxia", "狡黠", {}))
register(_Guangming("guangming", "光明", {}))
register(_Renxin("renxin", "仁心", {
    "flip": ("伤者不分敌我。", "医者面前，没有阵营。"),
}))
register(_Guyue("guyue", "孤月", {}))
register(_Qiusheng("qiusheng", "求胜", {
    "win": ("胜利！这就是胜利的味道！", "凯歌为你们而奏！"),
}))
register(_Aoman("aoman", "傲慢", {
    "pride": ("在我面前还敢站着？", "凡人，见识半神之怒！"),
    "pierce": ("此枪，无坚不摧！", "贯穿！没有盾能挡住我！"),
    "heel": ("呃——我的脚踝！", "不……那是我唯一的弱点！"),
}))
register(_Lumang("lumang", "鲁莽", {
    "boost": ("力量在沸腾！", "看我把山也砸碎！"),
    "taunt": ("最硬的盾？正合我意！", "你，就是你，过来挨打！"),
}))
register(_Moushen("moushen", "谋深", {
    "immune": ("我的心，不会动摇。", "计中有计，岂会犹豫。"),
    "overthink": ("等等……让我再想想。", "此局……还有变数。"),
}))
register(_Jiebao("jiebao", "借宝", {}))
register(_Zhuping("zhuping", "逐苹", {
    "apple": ("金苹果……在哪儿？", "那果子……好生诱人。"),
}))
register(_Qiaoshe("qiaoshe", "巧射", {
    "fear": ("别、别急，稳一点……", "英雄们先上，我殿后。"),
}))
register(_Jianren("jianren", "坚忍", {
    "stubborn": ("我不需要盾！", "让开，我自己扛！"),
}))
register(_Jichou("jichou", "记仇", {
    "rage": ("敢伤海皇？浪涛记住你了！", "今日之潮，只为你而涨！"),
}))
register(_Roubo("roubo", "柔波", {}))
register(_Zhongyong("zhongyong", "忠勇", {
    "offkey": ("咳咳……号角呛水了。", "音走了……陛下恕罪！"),
}))
register(_Meihuo("meihuo", "魅惑", {}))
register(_Tanshi("tanshi", "贪食", {}))
register(_Weiquan("weiquan", "威权", {
    "double": ("献上来，加倍地献上来。", "王座之下，皆为贡品。"),
}))
register(_Guyuan("guyuan", "孤怨", {
    "mirror": ("镜中的……是我自己……", "这目光，为何回望着我……"),
}))
register(_Huichun("huichun", "回春", {}))
register(_Lengku("lengku", "冷酷", {}))
register(_Huzhu("huzhu", "护主", {}))


# =============================================================================
# Phase 4 新武将性格（A2 先注册原语，A3 战法批接线到 roster；未装配前零行为差异）
# =============================================================================

@dataclass(frozen=True)
class _Zhonglie(Trait):
    """赫克托尔·忠烈：统率+10；自带主动战法（装配位 0）每次成功释放后，
    获得 1 层【忠烈·连发】（burst_rate_up_bps +1500，整场，最多 2 层），
    作用于自身所有主动战法的连发判定。"""

    def attr_bonus(self, engine, hero, attr):
        return 10 if attr == "command" else 0

    def on_skill_cast(self, engine, hero, skill, trigger_seq):
        if not hero.skills or skill.skill_id != hero.skills[0]:
            return
        engine.apply_status(hero, hero, _ZHONGLIE_BURST, parent_seq=trigger_seq)


@dataclass(frozen=True)
class _Haozhao(Trait):
    """伊阿宋·号召：己方任意单位触发连击后，自身速度+8（整场，最多叠 4 层）；
    每回合首次触发播台词。"""

    def on_ally_combo(self, engine, hero, attacker, parent_seq):
        instance = engine.apply_status(hero, hero, _HAOZHAO_RALLY, parent_seq=parent_seq)
        if instance is not None and not engine.trait_flag(hero.hero_id, "haozhao_line"):
            engine.set_trait_flag(hero.hero_id, "haozhao_line")
            emit_trigger(engine, hero, "rally", parent_seq=parent_seq)


@dataclass(frozen=True)
class _Bingpei(Trait):
    """卡斯托耳·并辔：己方其他单位普攻后 15% 使本次【双子协战】判定必定成功
    （coord_certain，不计入协击上限；消费即清），每回合最多 1 次。"""

    def on_ally_basic(self, engine, hero, attacker, target, parent_seq):
        if engine.trait_flag(hero.hero_id, "bingpei_used"):
            return
        r = rate(engine, self.trait_id, "certain", 1500)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:bingpei") < r:
            engine.set_trait_flag(hero.hero_id, "bingpei_used")
            engine.set_trait_flag(hero.hero_id, "coord_certain")
            emit_trigger(engine, hero, "certain", parent_seq=parent_seq)


register(_Zhonglie("zhonglie", "忠烈", {}))
register(_Haozhao("haozhao", "号召", {
    "rally": ("好样的！英雄们，跟上节奏！", "阿尔戈号的旗帜，指向胜利！"),
}))
register(_Bingpei("bingpei", "并辔", {
    "certain": ("兄弟，这次一起上！", "双子同辔，万夫莫当！"),
}))


# =============================================================================
# 2026-07-22：帕特洛克勒斯 / 赫卡忒 / 卡吕普索
# =============================================================================

@dataclass(frozen=True)
class _Bonong(Trait):
    """点将：己方武力/智力/速度最高单位造成伤害各 +8%（一人兼多项则叠加）。"""

    def damage_out_bonus(self, engine, source, target, kind, parent_seq=0):
        from battle.skill_common import highest_attr_unit
        bonus = 0
        for attr in ("force", "intelligence", "speed"):
            best = highest_attr_unit(engine, source, attr, allies=True)
            if best is not None and best.hero_id == source.hero_id:
                bonus += 800
        return bonus


@dataclass(frozen=True)
class _Chalou(Trait):
    """赫卡忒·岔路：对【冥火】目标造成伤害 +10%。"""

    def damage_out_bonus(self, engine, source, target, kind, parent_seq=0):
        if engine.find_status(target.hero_id, "underworld_burn") is not None:
            return 1000
        return 0


@dataclass(frozen=True)
class _Jiliu(Trait):
    """卡吕普索·羁留：对【冰锢】目标伤害 +12%；回合结束 20% 为一名受控友军清除冰锢。"""

    def damage_out_bonus(self, engine, source, target, kind, parent_seq=0):
        if engine.find_status(target.hero_id, "freeze") is not None:
            return 1200
        return 0

    def on_round_end(self, engine, hero, parent_seq, round_no):
        from battle.statuses import CONTROL
        candidates = []
        for ally in engine.alive_allies(hero):
            if ally.hero_id == hero.hero_id:
                continue
            freeze = engine.find_status(ally.hero_id, "freeze")
            if freeze is None:
                continue
            controlled = any(
                s.definition.kind == CONTROL
                for s in engine.hero_statuses(ally.hero_id)
            )
            if controlled:
                candidates.append((ally, freeze))
        if not candidates:
            return
        # 站位序确定性遍历；每人独立 20%
        for ally, freeze in sorted(
            candidates, key=lambda pair: pair[0].position
        ):
            if engine.rng.rand_bps("trait", f"{hero.hero_id}:jiliu:{ally.hero_id}") < 2000:
                engine.remove_status(freeze, reason="trait", parent_seq=parent_seq)


register(_Bonong("bonong", "点将", {}))
register(_Chalou("chalou", "岔路", {}))
register(_Jiliu("jiliu", "羁留", {
    "aid": ("奥杰吉厄的潮水，先松开你们。", "且让寒冰退一寸。"),
}))

# 忠烈/号召的载体状态（放注册后定义避免循环 import；traits 仅依赖 statuses）
from battle.statuses import BUFF as _BUFF, PERMANENT as _PERMANENT, StatusDef as _StatusDef  # noqa: E402

_ZHONGLIE_BURST = _StatusDef(
    status_id="zhonglie_burst", kind=_BUFF, duration_rounds=_PERMANENT,
    max_stacks=2, modifiers={"burst_rate_up_bps": 1500},
)
_HAOZHAO_RALLY = _StatusDef(
    status_id="haozhao_rally", kind=_BUFF, duration_rounds=_PERMANENT,
    max_stacks=4, modifiers={"speed_delta": 8},
)
_QIUSHENG_WIN = _StatusDef(
    status_id="qiusheng_win", kind=_BUFF, duration_rounds=_PERMANENT,
    max_stacks=3, refreshable=False,  # 满层静默拒绝 → 不再播台词
    modifiers={"force_delta": 10, "intelligence_delta": 10,
               "command_delta": 10, "speed_delta": 10},
)
