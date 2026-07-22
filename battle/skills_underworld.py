from __future__ import annotations

"""冥界阵营战法（Phase 4 v4 池，机制标签：吸取与处决）。

自带：hades_underworld_dominion 冥域君临 / medusa_gaze 石化凝视 /
      persephone_seasons 冬春轮转 / charon_ferry 渡魂船费 /
      thanatos_scythe 死神镰痕 / cerberus_bite 三首噬咬
拆解：hades_soul_drain 冥河汲魂 / medusa_glance 蛇瞳一瞥 / persephone_sprout 春芽 /
      charon_ferryman 摆渡 / thanatos_gaze 死亡凝望 / cerberus_guard 守门恶犬
赫尔墨斯 A4 阵营重划（gods→underworld）时随批迁入。
"""

from dataclasses import dataclass

from battle.skill_common import (
    BPS,
    emit_status_trigger,
    lowest_ratio_enemies,
)
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.skills_men import LionCounter, _lion_on_damage_taken
from battle.statuses import (
    DEBUFF,
    PERMANENT,
    SPECIAL,
    StatusDef,
    curse,
    fear,
    petrify,
    underworld_burn,
)

# =============================================================================
# 冥域君临（哈迪斯，v4）：神谕三重效果（均整局）：
# 1. 己方全体吸血属性 +10%（lifesteal_bps，走引擎通用吸血乘区）。
# 2. 己方全体【幽影蔽体】：行动窗口开始按已损兵比例刷新减伤（比例×70%，≤70%，
#    内部不事件化）。
# 3. 哈迪斯自身【冥祭献统】：行动窗口开始从每名其他存活友军汲取 10 统率，
#    1:1 提升自身统率并额外获得等量智力（性格·威权 20% 翻倍联动）；
#    统率削减随哈迪斯阵亡移除（source_defeated 通例）。
# =============================================================================

HADES_LIFESTEAL_STATUS = StatusDef(
    status_id="hades_lifesteal", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"lifesteal_bps": 1000},
)

SHADOW_VEIL_MAX_REDUCE_BPS = 7000  # v4：50% → 70%


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

HADES_DRAIN_PER_ALLY = 10  # v4：5 → 10

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
    # v4：1:1 统率提升 + 额外等量智力
    engine.adjust_status_attr(gain, "command", total, parent_seq=action_seq)
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
            engine.apply_status(actor, target, HADES_LIFESTEAL_STATUS, parent_seq=trigger_seq)
        for target in targets:
            engine.apply_status(actor, target, SHADOW_VEIL_STATUS, parent_seq=trigger_seq)
        engine.apply_status(actor, actor, HADES_COMMAND_DRAIN_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 冥河汲魂（哈迪斯拆解，v4 调参）：主动 40%——吸取敌全体各 25 点统率和智力转为
# 自身（2 回合可刷新），并对敌全体 150% 魔法伤害。
# =============================================================================

SOUL_DRAIN_LOSS_STATUS = StatusDef(
    status_id="soul_drain_loss", kind=DEBUFF, duration_rounds=2, refreshable=True,
)
SOUL_DRAIN_GAIN_STATUS = StatusDef(
    status_id="soul_drain_gain", kind=SPECIAL, duration_rounds=2, refreshable=True,
)
SOUL_DRAIN_PER_ENEMY = 25  # v4：10 → 25


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
                    actor, target, damage_type="magic", rate_bps=15000,
                    parent_seq=trigger_seq,
                )


# =============================================================================
# 石化凝视（美杜莎，v4）：被动——受敌方攻击并存活后 70%（普通随机）触发凝视：
# 吸取来源 15 点智力（整场累计）转自身，并石化来源 1 回合；每回合最多 3 次；
# 来源已石化时仍吸智但不刷新石化；来源已阵亡不触发；美杜莎阵亡后智力削减移除
# （source_defeated 通例）。
# =============================================================================

GAZE_RATE_BPS = 7000
GAZE_INT_DRAIN = 15  # v4：2 → 15
GAZE_MAX_PER_ROUND = 3

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
    if status.round_counters.get("gaze", 0) >= GAZE_MAX_PER_ROUND:
        return  # v4：每回合最多 3 次
    roll = engine.rng.rand_bps("status_trigger", f"gaze:{status.owner_id}")
    if roll >= GAZE_RATE_BPS:
        return
    status.round_counters["gaze"] = status.round_counters.get("gaze", 0) + 1
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
    # v4：来源已石化 → 仍吸智但不刷新石化持续时间
    if engine.find_status(source.hero_id, "petrify") is None:
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
# 蛇瞳一瞥（美杜莎拆解，v4 改版）：主动 35%——对敌随机 2 人（受击率、互斥）
# 各 180% 魔法伤害并石化 1 回合。
# =============================================================================

@dataclass(frozen=True, slots=True)
class MedusaGlance(Skill):
    def select_targets(self, engine, actor):
        from battle.skill_common import pick_distinct_enemies
        n = 2 + engine.rng.rand_index(2, "target_select", f"skill:{self.skill_id}:n")
        return pick_distinct_enemies(engine, actor, n, f"skill:{self.skill_id}")

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=18000,
                parent_seq=trigger_seq,
            )
            if target.is_alive():
                engine.apply_status(actor, target, petrify(1), parent_seq=trigger_seq)


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
# 春芽（珀耳塞福涅拆解，v4 改被动）：准备阶段使自身与随机 1 名友军获得【春芽】
# （3 回合）：受伤 -25%；受实际伤害后 40% 恢复施放者智力 ×0.6 的兵力；
# 每名持有者每回合最多触发 2 次治疗。
# =============================================================================

SPROUT_HEAL_RATE_BPS = 6000  # 每回合开始 60% 治疗


def _sprout_on_round_start(engine, status, parent_seq, round_no):
    owner = engine.hero_by_id(status.owner_id)
    caster = engine.heroes.get(status.source_id)
    if not owner.is_alive() or caster is None or not caster.is_alive():
        return
    roll = engine.rng.rand_bps("status_trigger", f"sprout:{status.owner_id}")
    if roll >= SPROUT_HEAL_RATE_BPS:
        return
    tick_seq = emit_status_trigger(engine, status, parent_seq)
    base = engine.effective_attr(caster, "intelligence") * 6000 // BPS
    engine.heal(caster, owner, fixed_base=base, parent_seq=tick_seq)


SPROUT_STATUS = StatusDef(
    status_id="persephone_sprout", kind=SPECIAL, duration_rounds=4,
    modifiers={"damage_reduce_bps": 2500},
    on_round_start=_sprout_on_round_start,
)


@dataclass(frozen=True, slots=True)
class PersephoneSprout(Skill):
    def select_targets(self, engine, actor):
        others = [h for h in engine.alive_allies(actor) if h.hero_id != actor.hero_id]
        picked = []
        if others:
            idx = engine.rng.rand_index(len(others), "target_select",
                                        f"skill:{self.skill_id}")
            picked.append(others[idx])
        return [actor] + picked

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, SPROUT_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 渡魂船费（卡戎，v4）：被动整局——任意武将阵亡时（不分敌我）：
# ①卡戎智力 +15（累计）；②治疗己方兵力比例最低者（智力 ×2.5）；
# ③对敌方兵力比例最低单位 200% 魔法伤害。多单位同亡按站位序逐一结算（引擎通例）。
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
    # v4：对敌方兵力比例最低单位 200% 魔法
    enemies = lowest_ratio_enemies(engine, owner, 1)
    if enemies and not engine.game_over():
        engine.deal_damage(
            owner, enemies[0], damage_type="magic", rate_bps=20000,
            parent_seq=tick_seq, kind="ferry",
        )


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
# 摆渡（卡戎拆解，v4 改被动）：自身对敌方造成实际伤害后，为目标施加【诅咒】
# （2 回合：智力 -20、受伤 +10%，A2 curse 原语——不可叠加、同 id 只刷新）。
# =============================================================================

def _ferryman_on_damage_dealt(engine, status, ctx):
    if ctx["amount"] <= 0 or ctx["kind"] == "ferry":
        return
    owner = engine.hero_by_id(status.owner_id)
    target = ctx["target"]
    if not owner.is_alive() or not target.is_alive():
        return
    if target.team_id == owner.team_id:
        return
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.apply_status(owner, target, curse(), parent_seq=tick_seq)


FERRYMAN_STATUS = StatusDef(
    status_id="charon_ferryman", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=35, on_damage_dealt=_ferryman_on_damage_dealt,
)


@dataclass(frozen=True, slots=True)
class CharonFerryman(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, FERRYMAN_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 死神镰痕（塔纳托斯，v4 改版）：主动 45%（不再准备）——对敌方兵力比例最低单体
# 350% 魔法；目标兵力比例 ≤30% 时本次伤害 +30%。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ThanatosScythe(Skill):
    def select_targets(self, engine, actor):
        return lowest_ratio_enemies(engine, actor, 1)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            ratio_bps = target.troops * BPS // max(1, target.max_troops)
            extra = 3000 if ratio_bps <= 3000 else 0
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=35000,
                parent_seq=trigger_seq, extra_damage_up_bps=extra,
            )


# =============================================================================
# 死亡凝望（塔纳托斯拆解，v4 改版）：被动——敌方单位每次被成功施加【诅咒】时
# 60% 对其 150% 魔法；每回合最多 3 次，同一次施加事件只触发一次
# （on_status_inflicted 分发天然一次一回调）。
# =============================================================================

GAZE_STRIKE_RATE_BPS = 6000
GAZE_STRIKE_MAX_PER_ROUND = 3


def _death_gaze_on_inflicted(engine, status, ctx):
    if ctx["status_id"] != "curse":
        return
    owner = engine.hero_by_id(status.owner_id)
    target = ctx["target"]
    if target.team_id == owner.team_id or not target.is_alive():
        return
    if status.round_counters.get("gaze_strike", 0) >= GAZE_STRIKE_MAX_PER_ROUND:
        return
    roll = engine.rng.rand_bps("status_trigger", f"death_gaze:{status.owner_id}")
    if roll >= GAZE_STRIKE_RATE_BPS:
        return
    status.round_counters["gaze_strike"] = (
        status.round_counters.get("gaze_strike", 0) + 1
    )
    tick_seq = emit_status_trigger(engine, status, ctx["apply_seq"])
    engine.deal_damage(
        owner, target, damage_type="magic", rate_bps=15000,
        parent_seq=tick_seq, kind="death_gaze",
    )


DEATH_GAZE_STATUS = StatusDef(
    status_id="thanatos_death_gaze", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=45, on_status_inflicted=_death_gaze_on_inflicted,
)


# =============================================================================
# 三首噬咬（刻耳柏洛斯，v4）：追击 40%——普攻后对普攻目标追加 3 次 110% 兵刃；
# 全部伤害结算后目标存活则施加【恐惧】1 回合（A2 fear 原语）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class CerberusBite(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            for _ in range(3):
                if not target.is_alive() or engine.game_over():
                    break
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=11000,
                    parent_seq=trigger_seq, kind="pursuit",
                )
            if target.is_alive() and not engine.game_over():
                engine.apply_status(actor, target, fear(1), parent_seq=trigger_seq)


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
# 三火炬（赫卡忒·自带·被动）：准备挂【岔路火种】；造成实际魔法伤害后挂/刷【冥火】2 回合，
# 每持有者每回合最多 2 次。燔祭（拆解·主动 50%）：敌随机 2 人 160% 谋略+冥火 3 回合；
# 已有冥火则本次额外 +15%。
# =============================================================================

HECATE_TORCH_MAX_PER_ROUND = 2


def _hecate_torch_on_damage_dealt(engine, status, ctx):
    if ctx["amount"] <= 0 or ctx["damage_type"] != "magic" or ctx["kind"] == "dot":
        return
    target = ctx["target"]
    owner = engine.hero_by_id(status.owner_id)
    if not target.is_alive() or target.team_id == owner.team_id:
        return
    if status.round_counters.get("torch", 0) >= HECATE_TORCH_MAX_PER_ROUND:
        return
    status.round_counters["torch"] = status.round_counters.get("torch", 0) + 1
    source = engine.heroes.get(status.source_id, owner)
    engine.apply_status(
        source, target, underworld_burn(2), parent_seq=ctx["damage_seq"],
    )


HECATE_TORCH_STATUS = StatusDef(
    status_id="hecate_torch", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=45, on_damage_dealt=_hecate_torch_on_damage_dealt,
)


@dataclass(frozen=True, slots=True)
class HecateTorch(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, HECATE_TORCH_STATUS, parent_seq=trigger_seq)


@dataclass(frozen=True, slots=True)
class HecatePyre(Skill):
    def select_targets(self, engine, actor):
        from battle.skill_common import pick_distinct_enemies
        return pick_distinct_enemies(engine, actor, 2, "hecate_pyre")

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            extra = 1500 if engine.find_status(target.hero_id, "underworld_burn") else 0
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=16000,
                parent_seq=trigger_seq, extra_damage_up_bps=extra,
            )
            if target.is_alive():
                engine.apply_status(
                    actor, target, underworld_burn(3), parent_seq=trigger_seq,
                )
            if engine._game_winner is not None:
                return


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
register(PersephoneSprout(skill_id="persephone_sprout", timing=TIMING_PREPARE))
register(CharonFerry(skill_id="charon_ferry", timing=TIMING_PREPARE))
register(CharonFerryman(skill_id="charon_ferryman", trigger_rate_bps=4000))
register(ThanatosScythe(skill_id="thanatos_scythe", trigger_rate_bps=5500,
                        hint_intensity="ultimate"))
register(SelfStatusPassive(skill_id="thanatos_gaze", timing=TIMING_PREPARE,
                           status_def=DEATH_GAZE_STATUS))
register(CerberusBite(skill_id="cerberus_bite", trigger_rate_bps=4000,
                      timing=TIMING_PURSUIT))
register(LionCounter(skill_id="cerberus_guard", timing=TIMING_PREPARE,
                     status_def=CERBERUS_GUARD_STATUS))
register(HecateTorch(skill_id="hecate_torch", timing=TIMING_PREPARE))
register(HecatePyre(skill_id="hecate_pyre", trigger_rate_bps=5000))
