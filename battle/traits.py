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
    pursuit_boost         追伤最终伤害倍率（傲慢 25% 判定 ×1.5）
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

    def damage_out_bonus(self, engine, source, target, kind: str) -> int:
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


REGISTRY: dict[str, Trait] = {}


def register(trait: Trait) -> Trait:
    if trait.trait_id in REGISTRY:
        raise ValueError(f"trait_id 重复注册: {trait.trait_id}")
    REGISTRY[trait.trait_id] = trait
    return trait


def of(hero: "HeroState") -> Trait | None:
    return REGISTRY.get(hero.trait_id) if hero.trait_id else None


def emit_trigger(engine: "SeriesEngine", hero: "HeroState", effect: str,
                 *, parent_seq: int = 0) -> int:
    """发 trait_trigger 事件（契约 1.2.0 加法演进）。台词确定性轮换，不消耗 RNG。"""
    trait = REGISTRY[hero.trait_id]
    pool = trait.lines.get(effect, ())
    idx = hero.trait_line_seq.get(effect, 0)
    hero.trait_line_seq[effect] = idx + 1
    line = pool[idx % len(pool)] if pool else ""
    return engine.writer.emit(
        "trait_trigger",
        {"hero_id": hero.hero_id, "trait_id": trait.trait_id,
         "effect": effect, "line": line},
        parent_seq=parent_seq,
        new_group=parent_seq == 0,
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
    """阿瑞斯·好战：任意单位阵亡后 15% 当场行动一轮；常驻 8% 普攻目标完全随机。"""

    def on_any_defeat(self, engine, hero, victim, parent_seq):
        r = rate(engine, self.trait_id, "extra_action", 1500)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:extra") < r:
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
    """赫尔墨斯·狡黠：速度+10；自由选敌类 30% 优先敌方后排。"""

    def attr_bonus(self, engine, hero, attr):
        return 10 if attr == "speed" else 0

    def prefer_target(self, engine, hero, candidates, reason):
        r = rate(engine, self.trait_id, "backline", 3000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:backline") < r:
            max_pos = max(c.position for c in candidates)
            back = [c for c in candidates if c.position == max_pos]
            if back and len(back) < len(candidates):
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
    """尼刻·求胜：己方击杀后自身四维立即+10（播台词）。"""

    def on_kill(self, engine, hero, killer, victim, parent_seq):
        if killer.team_id != hero.team_id:
            return
        seq = emit_trigger(engine, hero, "win", parent_seq=parent_seq)
        engine.modify_attr(
            hero,
            [(a, 10) for a in ("force", "intelligence", "command", "speed")],
            scope="game", parent_seq=seq,
        )


# =============================================================================
# 人阵营
# =============================================================================

@dataclass(frozen=True)
class _Aoman(Trait):
    """阿喀琉斯·傲慢：目标残兵比例高于自身时，造成非自带战法伤害前 25% 判定，
    成功则本次触发的追伤最终伤害 ×1.5；受击 15% 踵之弱→该次攻击必定暴击。"""

    def pursuit_boost_bps(self, engine, source, target, parent_seq):
        t_ratio = target.troops * BPS // target.max_troops
        s_ratio = source.troops * BPS // source.max_troops
        if t_ratio <= s_ratio:
            return 0
        r = rate(engine, self.trait_id, "pride", 2500)
        if engine.rng.rand_bps("trait", f"{source.hero_id}:pride") < r:
            emit_trigger(engine, source, "pride", parent_seq=parent_seq)
            return 5000  # ×1.5
        return 0

    def forced_crit_on_taken(self, engine, target, parent_seq):
        # 2026-07-09 人工调参：踵之弱 15%→7.5%（降低一倍）
        r = rate(engine, self.trait_id, "heel", 750)
        if engine.rng.rand_bps("trait", f"{target.hero_id}:heel") < r:
            emit_trigger(engine, target, "heel", parent_seq=parent_seq)
            return True
        return False


@dataclass(frozen=True)
class _Lumang(Trait):
    """赫拉克勒斯·鲁莽：行动时 40% 一回合增伤+15%；伤害 60% 常驻优先选
    敌军统率最高者（自由选敌类）。正负触发都播台词。"""

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "boost", 4000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:boost") < r:
            engine.set_trait_flag(hero.hero_id, "lumang_boost")
            emit_trigger(engine, hero, "boost", parent_seq=parent_seq)

    def damage_out_bonus(self, engine, source, target, kind):
        return 1500 if engine.trait_flag(source.hero_id, "lumang_boost") else 0

    def prefer_target(self, engine, hero, candidates, reason):
        r = rate(engine, self.trait_id, "taunt", 6000)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:taunt") < r:
            best = max(candidates, key=lambda c: (engine.effective_attr(c, "command"),
                                                  -c.position))
            emit_trigger(engine, hero, "taunt")
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
    """珀尔修斯·借宝：每名神阵营存活友军使自身速度+8（faction 由 roster 标注）。"""

    def attr_bonus(self, engine, hero, attr):
        if attr != "speed":
            return 0
        from battle.roster import FACTION_OF
        n = sum(
            1 for ally in engine.alive_allies(hero)
            if ally.hero_id != hero.hero_id
            and FACTION_OF.get(ally.template_id) == "gods"
        )
        return n * 8


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


@dataclass(frozen=True)
class _Shizhe(Trait):
    """喀戎·师者：治疗量+10%。"""

    def heal_up_bonus(self, engine, healer):
        return 1000


# =============================================================================
# 海阵营
# =============================================================================

@dataclass(frozen=True)
class _Jichou(Trait):
    """波塞冬·记仇：对最后伤害过自己的敌军伤害+25%；每回合开始 40% 怒涛难抑
    本回合所有伤害强制指向该目标（怒涛触发播台词）。"""

    def damage_out_bonus(self, engine, source, target, kind):
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
    """特里同·忠勇：波塞冬在场时自身全属性+10；6% 号角走音本回合无法释放
    自带战法（负触发播台词）。"""

    def attr_bonus(self, engine, hero, attr):
        for ally in engine.alive_allies(hero):
            if ally.template_id == "poseidon":
                return 10
        return 0

    def on_round_start(self, engine, hero, parent_seq):
        r = rate(engine, self.trait_id, "offkey", 600)
        if engine.rng.rand_bps("trait", f"{hero.hero_id}:offkey") < r:
            engine.set_trait_flag(hero.hero_id, "own_skill_disabled")
            emit_trigger(engine, hero, "offkey", parent_seq=parent_seq)


@dataclass(frozen=True)
class _Meihuo(Trait):
    """塞壬·魅惑：敌方对塞壬伤害-10%（减伤乘区）。"""

    def damage_in_reduce(self, engine, target):
        return 1000


@dataclass(frozen=True)
class _Tanshi(Trait):
    """斯库拉·贪食：普攻吸血 10%。"""

    def basic_lifesteal(self, engine, hero):
        return 1000


@dataclass(frozen=True)
class _Baoshi(Trait):
    """卡律布狄斯·暴食：普攻吸血 8%（复用贪食口径）。"""

    def basic_lifesteal(self, engine, hero):
        return 800


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
    """美杜莎·孤怨：石化别人时 8% 照影自身石化 1 回合（触发播台词）。"""

    def on_petrify_out(self, engine, source, parent_seq):
        from battle.statuses import petrify
        r = rate(engine, self.trait_id, "mirror", 800)
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
    """刻耳柏洛斯·护主：哈迪斯在场时自身全属性+5（复用忠勇口径）。"""

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
register(_Shizhe("shizhe", "师者", {}))
register(_Jichou("jichou", "记仇", {
    "rage": ("敢伤海皇？浪涛记住你了！", "今日之潮，只为你而涨！"),
}))
register(_Roubo("roubo", "柔波", {}))
register(_Zhongyong("zhongyong", "忠勇", {
    "offkey": ("咳咳……号角呛水了。", "音走了……陛下恕罪！"),
}))
register(_Meihuo("meihuo", "魅惑", {}))
register(_Tanshi("tanshi", "贪食", {}))
register(_Baoshi("baoshi", "暴食", {}))
register(_Weiquan("weiquan", "威权", {
    "double": ("献上来，加倍地献上来。", "王座之下，皆为贡品。"),
}))
register(_Guyuan("guyuan", "孤怨", {
    "mirror": ("镜中的……是我自己……", "这目光，为何回望着我……"),
}))
register(_Huichun("huichun", "回春", {}))
register(_Lengku("lengku", "冷酷", {}))
register(_Huzhu("huzhu", "护主", {}))
