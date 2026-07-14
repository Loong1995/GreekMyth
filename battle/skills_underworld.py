from __future__ import annotations

"""冥阵营战法（v3.1 池，机制标签：吸取与处决）。数值以 phase3 任务书 §五/§六 为准。

自带：hades_underworld_dominion 冥域君临 / medusa_gaze 石化凝视 /
      persephone_seasons 冬春轮转 / charon_ferry 渡魂船费 /
      thanatos_scythe 死神镰痕 / cerberus_bite 三首噬咬
拆解：hades_soul_drain 冥河汲魂 / medusa_glance 蛇瞳一瞥 / persephone_sprout 春芽 /
      charon_ferryman 摆渡 / thanatos_gaze 死亡凝望 / cerberus_guard 守门恶犬
"""

from dataclasses import dataclass

from battle.skill_common import (
    BPS,
    emit_status_trigger,
    lowest_ratio_allies,
    lowest_ratio_enemies,
)
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.skills_men import LionCounter, _lion_on_damage_taken
from battle.statuses import DEBUFF, PERMANENT, SPECIAL, StatusDef, petrify

# =============================================================================
# 冥域君临（哈迪斯）：神谕三重效果（均整局）：
# 1. 己方全体【冥河血誓】：造成实际伤害后 10% 转自疗（raw 固定量）。
# 2. 己方全体【幽影蔽体】：行动窗口开始按已损兵比例刷新减伤（≤50%，内部不事件化）。
# 3. 哈迪斯自身【冥祭献统】：行动窗口开始从每名友军汲取 5 统率 1:1 转自身智力
#    （Phase 3 修订：目标属性为智力；性格·威权 20% 翻倍联动）。
# =============================================================================

STYX_HEAL_BPS = 1000


def _styx_on_damage_dealt(engine, status, ctx):
    if ctx["amount"] <= 0:
        return
    owner = engine.hero_by_id(status.owner_id)
    amount = ctx["amount"] * STYX_HEAL_BPS // BPS
    if amount <= 0 or not owner.is_alive():
        return
    missing = owner.max_troops - owner.troops
    if owner.wounded_troop <= 0 or missing <= 0:
        return  # 治不进则不发 tick
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.heal(owner, owner, fixed_base=amount, parent_seq=tick_seq,
                can_crit=False, apply_modifiers=False)


STYX_BLOOD_OATH_STATUS = StatusDef(
    status_id="styx_blood_oath", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=10, on_damage_dealt=_styx_on_damage_dealt,
)

SHADOW_VEIL_MAX_REDUCE_BPS = 5000


def _shadow_on_apply(engine, instance):
    owner = engine.hero_by_id(instance.owner_id)
    instance.counters["entry_troops"] = owner.troops


def _shadow_on_action_start(engine, status, action_seq):
    owner = engine.hero_by_id(status.owner_id)
    entry = status.counters.get("entry_troops", owner.max_troops)
    if entry <= 0:
        return
    loss_ratio_bps = max(0, (entry - owner.troops) * BPS // entry)
    status.dynamic_modifiers["damage_reduce_bps"] = min(
        SHADOW_VEIL_MAX_REDUCE_BPS, loss_ratio_bps * SHADOW_VEIL_MAX_REDUCE_BPS // BPS
    )  # 内部乘区刷新，不事件化


SHADOW_VEIL_STATUS = StatusDef(
    status_id="shadow_veil", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=10, on_apply=_shadow_on_apply, on_action_start=_shadow_on_action_start,
)

HADES_DRAIN_PER_ALLY = 5

HADES_COMMAND_LOSS_STATUS = StatusDef(
    status_id="hades_command_loss", kind=SPECIAL, duration_rounds=PERMANENT,
)
HADES_INT_GAIN_STATUS = StatusDef(
    status_id="hades_int_gain", kind=SPECIAL, duration_rounds=PERMANENT,
)


def _hades_drain_on_action_start(engine, status, action_seq):
    hades = engine.hero_by_id(status.owner_id)
    multiplier = engine.drain_multiplier(hades, action_seq)  # 威权 20% 翻倍
    total = 0
    for ally in engine.alive_allies(hades):
        if ally.hero_id == hades.hero_id:
            continue
        drained = min(HADES_DRAIN_PER_ALLY * multiplier,
                      engine.effective_attr(ally, "command"))
        if drained <= 0:
            continue
        loss = engine.find_status(ally.hero_id, "hades_command_loss")
        if loss is None:
            loss = engine.apply_status(
                hades, ally, HADES_COMMAND_LOSS_STATUS, parent_seq=action_seq
            )
        engine.adjust_status_attr(loss, "command", -drained, parent_seq=action_seq)
        total += drained
    if total <= 0:
        return
    gain = engine.find_status(hades.hero_id, "hades_int_gain")
    if gain is None:
        gain = engine.apply_status(
            hades, hades, HADES_INT_GAIN_STATUS, parent_seq=action_seq
        )
    engine.adjust_status_attr(gain, "intelligence", total, parent_seq=action_seq)


HADES_COMMAND_DRAIN_STATUS = StatusDef(
    status_id="hades_command_drain", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=20, on_action_start=_hades_drain_on_action_start,
)


@dataclass(frozen=True, slots=True)
class HadesUnderworldDominion(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, STYX_BLOOD_OATH_STATUS, parent_seq=trigger_seq)
        for target in targets:
            engine.apply_status(actor, target, SHADOW_VEIL_STATUS, parent_seq=trigger_seq)
        engine.apply_status(actor, actor, HADES_COMMAND_DRAIN_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 冥河汲魂（哈迪斯拆解）：主动 40%——吸取敌全体 10 点统率和智力转为自身
# （2 回合可叠加），并对敌全体 180% 魔法伤害。
# =============================================================================

SOUL_DRAIN_LOSS_STATUS = StatusDef(
    status_id="soul_drain_loss", kind=DEBUFF, duration_rounds=2, refreshable=True,
)
SOUL_DRAIN_GAIN_STATUS = StatusDef(
    status_id="soul_drain_gain", kind=SPECIAL, duration_rounds=2, refreshable=True,
)
SOUL_DRAIN_PER_ENEMY = 10


@dataclass(frozen=True, slots=True)
class HadesSoulDrain(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_enemies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        multiplier = engine.drain_multiplier(actor, trigger_seq)
        total_command = 0
        total_int = 0
        for target in targets:
            if not target.is_alive():
                continue
            drain_c = min(SOUL_DRAIN_PER_ENEMY * multiplier,
                          engine.effective_attr(target, "command"))
            drain_i = min(SOUL_DRAIN_PER_ENEMY * multiplier,
                          engine.effective_attr(target, "intelligence"))
            loss = engine.find_status(target.hero_id, "soul_drain_loss")
            if loss is None:
                loss = engine.apply_status(actor, target, SOUL_DRAIN_LOSS_STATUS,
                                           parent_seq=trigger_seq)
            if loss is not None:
                if drain_c > 0:
                    engine.adjust_status_attr(loss, "command", -drain_c,
                                              parent_seq=trigger_seq)
                if drain_i > 0:
                    engine.adjust_status_attr(loss, "intelligence", -drain_i,
                                              parent_seq=trigger_seq)
            total_command += drain_c
            total_int += drain_i
        if total_command > 0 or total_int > 0:
            gain = engine.find_status(actor.hero_id, "soul_drain_gain")
            if gain is None:
                gain = engine.apply_status(actor, actor, SOUL_DRAIN_GAIN_STATUS,
                                           parent_seq=trigger_seq)
            if gain is not None:
                if total_command > 0:
                    engine.adjust_status_attr(gain, "command", total_command,
                                              parent_seq=trigger_seq)
                if total_int > 0:
                    engine.adjust_status_attr(gain, "intelligence", total_int,
                                              parent_seq=trigger_seq)
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="magic", rate_bps=18000,
                    parent_seq=trigger_seq,
                )


# =============================================================================
# 石化凝视（美杜莎）：被动——受敌方攻击后 70%（普通随机）触发凝视：吸取来源 2 点
# 智力（累计）转自身，并石化来源 1 回合。
# =============================================================================

GAZE_RATE_BPS = 7000
GAZE_INT_DRAIN = 2

MEDUSA_INT_LOSS_STATUS = StatusDef(
    status_id="medusa_int_loss", kind=SPECIAL, duration_rounds=PERMANENT,
)
MEDUSA_INT_GAIN_STATUS = StatusDef(
    status_id="medusa_int_gain", kind=SPECIAL, duration_rounds=PERMANENT,
)


def _gaze_on_damage_taken(engine, status, ctx):
    source = ctx["source"]
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive() or not source.is_alive():
        return
    if source.team_id == owner.team_id:
        return  # 只凝视敌方来源
    roll = engine.rng.rand_bps("status_trigger", f"gaze:{status.owner_id}")
    if roll >= GAZE_RATE_BPS:
        return
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    multiplier = engine.drain_multiplier(owner, tick_seq)
    drained = min(GAZE_INT_DRAIN * multiplier, engine.effective_attr(source, "intelligence"))
    if drained > 0:
        loss = engine.find_status(source.hero_id, "medusa_int_loss")
        if loss is None:
            loss = engine.apply_status(owner, source, MEDUSA_INT_LOSS_STATUS, parent_seq=tick_seq)
        engine.adjust_status_attr(loss, "intelligence", -drained, parent_seq=tick_seq)
        gain = engine.find_status(owner.hero_id, "medusa_int_gain")
        if gain is None:
            gain = engine.apply_status(owner, owner, MEDUSA_INT_GAIN_STATUS, parent_seq=tick_seq)
        engine.adjust_status_attr(gain, "intelligence", drained, parent_seq=tick_seq)
    engine.apply_status(owner, source, petrify(1), parent_seq=tick_seq)


MEDUSA_GAZE_STATUS = StatusDef(
    status_id="medusa_gaze", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=50, on_damage_taken=_gaze_on_damage_taken,
)


@dataclass(frozen=True, slots=True)
class MedusaGaze(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, MEDUSA_GAZE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 蛇瞳一瞥（美杜莎拆解）：主动 35%——敌单体石化 1 回合 + 280% 魔法伤害。
# =============================================================================

@dataclass(frozen=True, slots=True)
class MedusaGlance(Skill):
    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            engine.apply_status(actor, target, petrify(1), parent_seq=trigger_seq)
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=28000,
                parent_seq=trigger_seq,
            )


# =============================================================================
# 冬春轮转（珀耳塞福涅）：被动整局——奇数回合结束治疗己方全体（智力 ×1.0）；
# 偶数回合结束对敌方全体智力 ×1.2 魔法伤害。
# =============================================================================

def _seasons_on_round_end(engine, status, parent_seq, round_no):
    owner = engine.hero_by_id(status.owner_id)
    tick_seq = emit_status_trigger(engine, status, parent_seq)
    if round_no % 2 == 1:
        base = engine.effective_attr(owner, "intelligence")
        for ally in engine.alive_allies(owner):
            engine.heal(owner, ally, fixed_base=base, parent_seq=tick_seq)
    else:
        for enemy in engine.alive_enemies(owner):
            if engine.game_over() or not owner.is_alive():
                return
            if enemy.is_alive():
                engine.deal_damage(
                    owner, enemy, damage_type="magic", rate_bps=12000,
                    parent_seq=tick_seq, kind="seasons",
                )


SEASONS_STATUS = StatusDef(
    status_id="persephone_seasons", kind=SPECIAL, duration_rounds=PERMANENT,
    on_round_end=_seasons_on_round_end,
)


@dataclass(frozen=True, slots=True)
class PersephoneSeasons(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, SEASONS_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 春芽（珀耳塞福涅拆解）：主动 40%——驱散己方全体每人 1 种负面并治疗（智力 ×1.0）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class PersephoneSprout(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        base = engine.effective_attr(actor, "intelligence")
        for target in targets:
            if not target.is_alive():
                continue
            engine.dispel(target, count=1, parent_seq=trigger_seq)
            engine.heal(actor, target, fixed_base=base, parent_seq=trigger_seq)


# =============================================================================
# 渡魂船费（卡戎）：被动整局——任意武将阵亡时：卡戎智力 +15（累计），
# 然后治疗己方兵力比例最低者（智力 ×2.5）。
# =============================================================================

CHARON_INT_GAIN_STATUS = StatusDef(
    status_id="charon_int_gain", kind=SPECIAL, duration_rounds=PERMANENT,
)


def _ferry_on_hero_defeated(engine, status, ctx):
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    tick_seq = emit_status_trigger(engine, status, ctx["defeat_seq"])
    gain = engine.find_status(owner.hero_id, "charon_int_gain")
    if gain is None:
        gain = engine.apply_status(owner, owner, CHARON_INT_GAIN_STATUS, parent_seq=tick_seq)
    engine.adjust_status_attr(gain, "intelligence", 15, parent_seq=tick_seq)
    ally = engine.select_ally_lowest_troops(owner)
    base = engine.effective_attr(owner, "intelligence") * 25000 // BPS
    engine.heal(owner, ally, fixed_base=base, parent_seq=tick_seq)


FERRY_STATUS = StatusDef(
    status_id="charon_ferry", kind=SPECIAL, duration_rounds=PERMANENT,
    on_hero_defeated=_ferry_on_hero_defeated,
)


@dataclass(frozen=True, slots=True)
class CharonFerry(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, FERRY_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 摆渡（卡戎拆解）：主动 40%——治疗己方兵力比例最低 (1 + 本局阵亡数) 人（智力 ×2）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class CharonFerryman(Skill):
    def select_targets(self, engine, actor):
        return lowest_ratio_allies(engine, actor, 1 + engine.defeat_count)

    def execute(self, engine, actor, targets, trigger_seq):
        base = engine.effective_attr(actor, "intelligence") * 20000 // BPS
        for target in targets:
            if target.is_alive():
                engine.heal(actor, target, fixed_base=base, parent_seq=trigger_seq)


# =============================================================================
# 死神镰痕（塔纳托斯）：主动 45%，准备 1 回合——对敌方兵力比例最低 2 名各 300% 魔法。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ThanatosScythe(Skill):
    def select_targets(self, engine, actor):
        return lowest_ratio_enemies(engine, actor, 2)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="magic", rate_bps=30000,
                    parent_seq=trigger_seq,
                )


# =============================================================================
# 死亡凝望（塔纳托斯拆解）：被动——对兵力比例最低的敌军伤害 +20%（pre_damage）。
# =============================================================================

def _death_gaze_pre_damage(engine, status, ctx):
    owner = ctx["source"]
    lowest = lowest_ratio_enemies(engine, owner, 1)
    if lowest and lowest[0].hero_id == ctx["target"].hero_id:
        ctx["damage_up_bonus"] += 2000


DEATH_GAZE_STATUS = StatusDef(
    status_id="thanatos_death_gaze", kind=SPECIAL, duration_rounds=PERMANENT,
    on_pre_damage_dealt=_death_gaze_pre_damage,
)


# =============================================================================
# 三首噬咬（刻耳柏洛斯）：追击 40%——普攻后对普攻目标追加 2 次 110% 兵刃。
# =============================================================================

@dataclass(frozen=True, slots=True)
class CerberusBite(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            for _ in range(2):
                if not target.is_alive() or engine.game_over():
                    break
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=11000,
                    parent_seq=trigger_seq, kind="pursuit",
                )


# =============================================================================
# 守门恶犬（刻耳柏洛斯拆解）：被动——自身受伤 -15%；受击后 20% 对来源反打
# 60% 兵刃（复用狮皮反击口径）。
# =============================================================================

CERBERUS_GUARD_STATUS = StatusDef(
    status_id="cerberus_guard", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"damage_reduce_bps": 1500},
    response_priority=50, on_damage_taken=_lion_on_damage_taken,
    payload={"rate_bps": 2000, "damage_rate_bps": 6000, "weaken": False},
)


# =============================================================================
# 注册
# =============================================================================

from battle.skills_men import SelfStatusPassive  # noqa: E402

register(HadesUnderworldDominion(skill_id="hades_underworld_dominion",
                                 timing=TIMING_PREPARE, is_oracle=True,
                                 hint_intensity="strong"))
register(HadesSoulDrain(skill_id="hades_soul_drain", trigger_rate_bps=4000,
                        hint_intensity="strong"))
register(MedusaGaze(skill_id="medusa_gaze", timing=TIMING_PREPARE))
register(MedusaGlance(skill_id="medusa_glance", trigger_rate_bps=3500))
register(PersephoneSeasons(skill_id="persephone_seasons", timing=TIMING_PREPARE))
register(PersephoneSprout(skill_id="persephone_sprout", trigger_rate_bps=4000))
register(CharonFerry(skill_id="charon_ferry", timing=TIMING_PREPARE))
register(CharonFerryman(skill_id="charon_ferryman", trigger_rate_bps=4000))
register(ThanatosScythe(skill_id="thanatos_scythe", trigger_rate_bps=4500,
                        prepare_rounds=1, hint_intensity="ultimate"))
register(SelfStatusPassive(skill_id="thanatos_gaze", timing=TIMING_PREPARE,
                           status_def=DEATH_GAZE_STATUS))
register(CerberusBite(skill_id="cerberus_bite", trigger_rate_bps=4000,
                      timing=TIMING_PURSUIT))
register(LionCounter(skill_id="cerberus_guard", timing=TIMING_PREPARE,
                     status_def=CERBERUS_GUARD_STATUS))
