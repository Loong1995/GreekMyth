from __future__ import annotations

"""神阵营战法（v3.1 池，机制标签：神示与落雷）。数值以 phase3 任务书 §二/§六 为准。

自带：thunder_oracle 雷霆神谕 / athena_aegis 埃癸斯圣盾 / ares_warfury 战神怒火 /
      hermes_oracle 赫尔墨斯神谕 / delphi_revelation 德尔斐启示 /
      asclepius_oracle 蛇杖庇护圣谕 / artemis_hunt 月影狩猎 / nike_wings 胜利羽翼
拆解：zeus_bolt 天雷击 / athena_guard 神盾格挡 / ares_roar 血性咆哮 /
      hermes_jest 神使戏言 / apollo_blessing 日光祝祷 / asclepius_kiss 灵蛇之吻 /
      artemis_arrow 猎月之矢 / nike_paean 凯歌
"""

from dataclasses import dataclass

from battle.pseudo_random import PseudoRandomParams
from battle.skill_common import ATTR_DELTA_KEYS, BPS, emit_status_trigger, pick_distinct_enemies
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.statuses import (
    BUFF,
    DEBUFF,
    PERMANENT,
    SPECIAL,
    StatusDef,
    disarm,
    hesitation,
    silence,
)

# =============================================================================
# 雷霆神谕（宙斯）：己方全体【雷霆】——造成非落雷伤害后 70%（伪随机：失败+9%、
# 成功-7%、30%~85%、4 次保底）追加落雷（触发者智力 100% 魔法），每人每回合 3 次。
# 性格·多情联动：宙斯分神（oracle_suppressed 旗标）本回合全队雷霆不触发。
# =============================================================================

_THUNDER_PR = PseudoRandomParams(
    bonus_per_fail_bps=900, penalty_per_success_bps=700,
    min_rate_bps=3000, max_rate_bps=8500, guarantee_fail_count=4,
)
THUNDER_RATE_BPS = 7000
THUNDER_MAX_PER_ROUND = 3


def _thunder_on_damage_dealt(engine, status, ctx):
    if ctx["kind"] == "lightning":
        return  # 落雷不触发雷霆（防递归）
    if engine.trait_flag(status.source_id, "oracle_suppressed"):
        return  # 宙斯多情·分神：本回合雷霆不触发
    if status.round_counters.get("lightning", 0) >= THUNDER_MAX_PER_ROUND:
        return
    target = ctx["target"]
    if not target.is_alive():
        return  # 本次受击目标已阵亡 → 无追加对象，不 roll
    triggered = engine.pseudo_random.roll(
        engine.rng, (status.owner_id, "thunder"), THUNDER_RATE_BPS, _THUNDER_PR,
        source="status_trigger", reason=f"thunder:{status.owner_id}",
    )
    if not triggered:
        return
    status.round_counters["lightning"] = status.round_counters.get("lightning", 0) + 1
    owner = engine.hero_by_id(status.owner_id)
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.deal_damage(
        owner, target, damage_type="magic", rate_bps=10000,
        parent_seq=tick_seq, kind="lightning",
    )


THUNDER_STATUS = StatusDef(
    status_id="thunder", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=30, on_damage_dealt=_thunder_on_damage_dealt,
)


@dataclass(frozen=True, slots=True)
class ThunderOracle(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, THUNDER_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 天雷击（宙斯拆解）：主动 50%——以智力对敌全体造成 200% 魔法伤害。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ZeusBolt(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_enemies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="magic", rate_bps=20000,
                    parent_seq=trigger_seq,
                )


# =============================================================================
# 埃癸斯圣盾（雅典娜）【皇·反制位】：神谕三重效果（均整局）：
# 1. 己方全体【圣盾】反弹（v3.2 改版）：受到伤害时 25% 反弹——受伤归零，
#    本应受伤害原样反弹给攻击者（特殊伤害固定量，不触发连锁、不可再被减免）。
#    走引擎减免通道（reflect_rate_bps），与格挡/闪避的先后 = 状态施加顺序。
# 1b. 受到控制后 25% 反制——对敌方随机目标施加同种控制。
# 2. 我方统率最低单体单次受伤超过其受击前兵力 10% → 雅典娜为其回复（智力×0.9）。
# 性格·明睿联动：匠心旁骛（oracle_suppressed）本回合圣盾不生效（含反弹闸门）。
# =============================================================================

AEGIS_COUNTER_RATE_BPS = 2500
AEGIS_HEAL_THRESHOLD_BPS = 1000
AEGIS_HEAL_RATE = 9000  # 智力 ×0.9


def _aegis_suppressed(engine, status) -> bool:
    return engine.trait_flag(status.source_id, "oracle_suppressed")


def _aegis_mitigation_gate(engine, status) -> bool:
    return not _aegis_suppressed(engine, status)


def _aegis_lowest_command_id(engine, status) -> str:
    owner = engine.hero_by_id(status.owner_id)
    allies = engine.alive_allies(owner)
    best = min(
        allies,
        key=lambda h: (engine.effective_attr(h, "command"),
                       engine.hero_order.index(h.hero_id)),
    )
    return best.hero_id


def _aegis_on_damage_taken(engine, status, ctx):
    if _aegis_suppressed(engine, status):
        return
    owner = engine.hero_by_id(status.owner_id)
    amount = ctx["amount"]
    # 效果 2：统率最低单体受重击 → 雅典娜回复（受击前兵力 = 当前 + 已扣）
    if amount <= 0 or not owner.is_alive():
        return
    if status.owner_id != _aegis_lowest_command_id(engine, status):
        return
    before_troops = owner.troops + amount
    if before_troops <= 0 or amount * BPS // before_troops <= AEGIS_HEAL_THRESHOLD_BPS:
        return
    caster = engine.heroes.get(status.source_id)
    if caster is None or not caster.is_alive():
        return
    base = engine.effective_attr(caster, "intelligence") * AEGIS_HEAL_RATE // BPS
    if base > 0:
        tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
        engine.heal(caster, owner, fixed_base=base, parent_seq=tick_seq)


def _aegis_on_control_taken(engine, status, ctx):
    if _aegis_suppressed(engine, status):
        return
    owner = engine.hero_by_id(status.owner_id)
    roll = engine.rng.rand_bps("status_trigger", f"aegis_ctrl:{status.owner_id}")
    if roll >= AEGIS_COUNTER_RATE_BPS:
        return
    target = engine.select_enemy_by_hit_rate(owner, reason=f"aegis_ctrl:{status.owner_id}")
    if target is None:
        return
    tick_seq = emit_status_trigger(engine, status, ctx["parent_seq"])
    engine.apply_status(owner, target, ctx["control"], parent_seq=tick_seq)


AEGIS_STATUS = StatusDef(
    status_id="aegis_shield", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=15,
    modifiers={"reflect_rate_bps": AEGIS_COUNTER_RATE_BPS},
    mitigation_gate=_aegis_mitigation_gate,
    on_damage_taken=_aegis_on_damage_taken,
    on_control_taken=_aegis_on_control_taken,
)


@dataclass(frozen=True, slots=True)
class AthenaAegis(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, AEGIS_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 神盾格挡（雅典娜拆解）：被动（准备阶段释放给全队）——前三回合受伤 30% 格挡归零。
# =============================================================================

GUARD_BLOCK_STATUS = StatusDef(
    status_id="athena_guard", kind=BUFF, duration_rounds=3,
    modifiers={"block_rate_bps": 3000},
)


@dataclass(frozen=True, slots=True)
class AthenaGuard(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, GUARD_BLOCK_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 战神怒火（阿瑞斯）：全场【血战】（受物理易伤 +30%、物理暴击率 +20%，整局）；
# 己方武力最高者【战神之勇】（武力 +5、速度 +5，整局）。
# =============================================================================

BLOOD_BATTLE_STATUS = StatusDef(
    status_id="blood_battle", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"physical_vulnerable_bps": 3000, "physical_crit_rate_bps": 2000},
)
ARES_MIGHT_STATUS = StatusDef(
    status_id="ares_might", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"force_delta": 5, "speed_delta": 5},
)


@dataclass(frozen=True, slots=True)
class AresWarfury(Skill):
    def select_targets(self, engine, actor):
        return [
            engine.hero_by_id(hero_id)
            for hero_id in engine.hero_order
            if engine.hero_by_id(hero_id).is_alive()
        ]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, BLOOD_BATTLE_STATUS, parent_seq=trigger_seq)
        allies = engine.alive_allies(actor)
        best = allies[0]
        for ally in allies[1:]:
            if engine.effective_attr(ally, "force") > engine.effective_attr(best, "force"):
                best = ally  # 并列取遍历序靠前（D-08）
        engine.apply_status(actor, best, ARES_MIGHT_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 血性咆哮（阿瑞斯拆解）：主动 40%——对敌两人施加物理易伤 +15%（2 回合）并各造成
# 240% 兵刃伤害。
# =============================================================================

ROAR_VULNERABLE_STATUS = StatusDef(
    status_id="ares_roar_vulnerable", kind=DEBUFF, duration_rounds=2,
    modifiers={"physical_vulnerable_bps": 1500},
)


@dataclass(frozen=True, slots=True)
class AresRoar(Skill):
    def select_targets(self, engine, actor):
        return pick_distinct_enemies(engine, actor, 2, f"skill:{self.skill_id}")

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if not target.is_alive():
                continue
            engine.apply_status(actor, target, ROAR_VULNERABLE_STATUS, parent_seq=trigger_seq)
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=24000,
                parent_seq=trigger_seq,
            )


# =============================================================================
# 赫尔墨斯神谕：敌方全体【扰心印记】（仅第 1 回合生效：行动窗口开始 50% 施加犹豫）；
# 我方全体【神使印记】（回合开始 50% 获得先攻 1 回合）。
# =============================================================================

HERMES_MARK_RATE_BPS = 5000

FIRST_STRIKE_STATUS = StatusDef(
    status_id="first_strike", kind=BUFF, duration_rounds=1,
    refreshable=True, modifiers={"first_strike": True},
)


def _hermes_mark_on_action_start(engine, status, action_seq):
    owner = engine.hero_by_id(status.owner_id)
    source = engine.heroes.get(status.source_id)
    if source is None:
        return
    roll = engine.rng.rand_bps("status_trigger", f"hermes_mark:{status.owner_id}")
    if roll >= HERMES_MARK_RATE_BPS:
        return
    engine.apply_status(source, owner, hesitation(5000, 2), parent_seq=action_seq)


HERMES_MARK_STATUS = StatusDef(
    status_id="hermes_confusion_mark", kind=SPECIAL, duration_rounds=1,
    response_priority=30, on_action_start=_hermes_mark_on_action_start,
)


def _herald_on_round_start(engine, status, parent_seq, round_no):
    owner = engine.hero_by_id(status.owner_id)
    source = engine.heroes.get(status.source_id, owner)
    roll = engine.rng.rand_bps("status_trigger", f"herald:{status.owner_id}")
    if roll >= HERMES_MARK_RATE_BPS:
        return
    engine.apply_status(source, owner, FIRST_STRIKE_STATUS, parent_seq=parent_seq)


HERMES_HERALD_STATUS = StatusDef(
    status_id="hermes_herald_mark", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=30, on_round_start=_herald_on_round_start,
)


@dataclass(frozen=True, slots=True)
class HermesOracle(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_enemies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, HERMES_MARK_STATUS, parent_seq=trigger_seq)
        for ally in engine.alive_allies(actor):
            engine.apply_status(actor, ally, HERMES_HERALD_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 神使戏言（赫尔墨斯拆解）：主动 35%——我方速度最高者获得先攻（1 回合）；
# 敌方当前速度最高者被施加【犹豫】。
# =============================================================================

@dataclass(frozen=True, slots=True)
class HermesJest(Skill):
    def _fastest(self, engine, pool):
        return max(
            pool,
            key=lambda h: (engine.effective_attr(h, "speed"),
                           -engine.hero_order.index(h.hero_id)),
        )

    def select_targets(self, engine, actor):
        targets = [self._fastest(engine, engine.alive_allies(actor))]
        enemies = engine.alive_enemies(actor)
        if enemies:
            targets.append(self._fastest(engine, enemies))
        return targets

    def execute(self, engine, actor, targets, trigger_seq):
        if targets:
            engine.apply_status(actor, targets[0], FIRST_STRIKE_STATUS, parent_seq=trigger_seq)
        if len(targets) > 1 and targets[1].is_alive():
            engine.apply_status(actor, targets[1], hesitation(), parent_seq=trigger_seq)


# =============================================================================
# 德尔斐启示（阿波罗）：己方全体【神示】四维各 +30（整局，平加层）。
# =============================================================================

DIVINE_REVELATION_STATUS = StatusDef(
    status_id="divine_revelation", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={key: 30 for key in ATTR_DELTA_KEYS},
)


@dataclass(frozen=True, slots=True)
class DelphiRevelation(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, DIVINE_REVELATION_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 日光祝祷（阿波罗拆解）：主动 45%——己方全体武/智/统 +25（2 回合，可叠 2 次）。
# =============================================================================

SUN_BLESSING_STATUS = StatusDef(
    status_id="sun_blessing", kind=BUFF, duration_rounds=2, max_stacks=2,
    modifiers={"force_delta": 25, "intelligence_delta": 25, "command_delta": 25},
)


@dataclass(frozen=True, slots=True)
class ApolloBlessing(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, SUN_BLESSING_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 蛇杖庇护圣谕（阿斯克勒庇俄斯）：己方全体【蛇杖庇护】——受实际伤害后 40%
# （伪随机：失败+8%、成功-6%、20%~70%、5 次保底）触发治疗（1% 上限 + 施放者智力×1）；
# 每回合结束时对我方兵力最低单位额外治疗一次（挂施放者【灵蛇看护】）。
# =============================================================================

_SNAKE_PR = PseudoRandomParams(
    bonus_per_fail_bps=800, penalty_per_success_bps=600,
    min_rate_bps=2000, max_rate_bps=7000, guarantee_fail_count=5,
)
SNAKE_RATE_BPS = 4000
SNAKE_MAX_TROOP_BPS = 100  # 1% 兵力上限


def _snake_base(engine, caster, owner) -> int:
    return owner.max_troops * SNAKE_MAX_TROOP_BPS // BPS + engine.effective_attr(
        caster, "intelligence"
    )


def _snake_on_damage_taken(engine, status, ctx):
    if ctx["amount"] <= 0:
        return
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    triggered = engine.pseudo_random.roll(
        engine.rng, (status.owner_id, "snake_staff"), SNAKE_RATE_BPS, _SNAKE_PR,
        source="status_trigger", reason=f"snake_staff:{status.owner_id}",
    )
    if not triggered:
        return
    caster = engine.heroes.get(status.source_id)
    if caster is None or not caster.is_alive():
        return
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.heal(caster, owner, fixed_base=_snake_base(engine, caster, owner),
                parent_seq=tick_seq)


SNAKE_STAFF_STATUS = StatusDef(
    status_id="snake_staff_protection", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=20, on_damage_taken=_snake_on_damage_taken,
)


def _snake_tender_on_round_end(engine, status, parent_seq, round_no):
    caster = engine.hero_by_id(status.owner_id)
    ally = engine.select_ally_lowest_troops(caster)
    tick_seq = emit_status_trigger(engine, status, parent_seq)
    engine.heal(caster, ally, fixed_base=_snake_base(engine, caster, ally),
                parent_seq=tick_seq)


SNAKE_TENDER_STATUS = StatusDef(
    status_id="snake_staff_tender", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=20, on_round_end=_snake_tender_on_round_end,
)


@dataclass(frozen=True, slots=True)
class AsclepiusOracle(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, SNAKE_STAFF_STATUS, parent_seq=trigger_seq)
        engine.apply_status(actor, actor, SNAKE_TENDER_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 灵蛇之吻（阿斯克勒庇俄斯拆解）：主动 45%——驱散己方兵力比例最低者全部负面，
# 治疗（智力 ×2.5，吃治疗乘区可暴击）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class AsclepiusKiss(Skill):
    def select_targets(self, engine, actor):
        return [engine.select_heal_target_lowest(actor)]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            engine.dispel(target, count=None, parent_seq=trigger_seq)
            base = engine.effective_attr(actor, "intelligence") * 25000 // BPS
            engine.heal(actor, target, fixed_base=base, parent_seq=trigger_seq)


# =============================================================================
# 月影狩猎（阿尔忒弥斯）：被动整局——自身伤害 +30%，伤害优先锁定敌军兵力最低单体。
# =============================================================================

MOON_HUNT_STATUS = StatusDef(
    status_id="moon_hunt", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"damage_up_bps": 3000, "lock_lowest_target": True},
)


@dataclass(frozen=True, slots=True)
class ArtemisHunt(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, MOON_HUNT_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 猎月之矢（阿尔忒弥斯拆解）：追击 40%——普攻后 360% 魔法；若目标为敌方兵力比例
# 最低者，追加一次 100% 魔法。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ArtemisArrow(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        from battle.skill_common import lowest_ratio_enemies
        for target in targets:
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=36000,
                parent_seq=trigger_seq, kind="pursuit",
            )
            if not target.is_alive():
                continue
            lowest = lowest_ratio_enemies(engine, actor, 1)
            if lowest and lowest[0].hero_id == target.hero_id:
                engine.deal_damage(
                    actor, target, damage_type="magic", rate_bps=10000,
                    parent_seq=trigger_seq, kind="pursuit",
                )


# =============================================================================
# 胜利羽翼（尼刻）：神谕——我方武力/智力最高单体【胜利之翼】：每回合获得一次
# 暴击机会（第一次伤害/治疗必暴击）；其击杀敌方单位再获一次。
# =============================================================================

def _wings_on_round_start(engine, status, parent_seq, round_no):
    status.counters["forced_crit_charges"] = (
        status.counters.get("forced_crit_charges", 0) + 1
    )


def _wings_on_hero_defeated(engine, status, ctx):
    if ctx["killer"].hero_id == status.owner_id:
        status.counters["forced_crit_charges"] = (
            status.counters.get("forced_crit_charges", 0) + 1
        )


NIKE_WINGS_STATUS = StatusDef(
    status_id="nike_wings", kind=SPECIAL, duration_rounds=PERMANENT,
    on_round_start=_wings_on_round_start,
    on_hero_defeated=_wings_on_hero_defeated,
)


@dataclass(frozen=True, slots=True)
class NikeWings(Skill):
    def select_targets(self, engine, actor):
        allies = engine.alive_allies(actor)
        best = max(
            allies,
            key=lambda h: (
                max(engine.effective_attr(h, "force"),
                    engine.effective_attr(h, "intelligence")),
                -engine.hero_order.index(h.hero_id),
            ),
        )
        return [best]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            instance = engine.apply_status(actor, target, NIKE_WINGS_STATUS,
                                           parent_seq=trigger_seq)
            if instance is not None:
                instance.counters["forced_crit_charges"] = 1  # r=0 施放即带首回合次数


# =============================================================================
# 凯歌（尼刻拆解）：主动 45%——己方全体速度 +15（2 回合）；对敌方兵力最低单体
# 各 50% 独立判定缄默/缴械（2 回合）。
# =============================================================================

PAEAN_SPEED_STATUS = StatusDef(
    status_id="nike_paean_speed", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"speed_delta": 15},
)


@dataclass(frozen=True, slots=True)
class NikePaean(Skill):
    def select_targets(self, engine, actor):
        from battle.skill_common import lowest_ratio_enemies
        lowest = lowest_ratio_enemies(engine, actor, 1)
        return engine.alive_allies(actor) + lowest

    def execute(self, engine, actor, targets, trigger_seq):
        enemies = [t for t in targets if t.team_id != actor.team_id]
        for ally in (t for t in targets if t.team_id == actor.team_id):
            engine.apply_status(actor, ally, PAEAN_SPEED_STATUS, parent_seq=trigger_seq)
        for enemy in enemies:
            if not enemy.is_alive():
                continue
            if engine.rng.rand_bps("skill_effect", f"paean_silence:{actor.hero_id}") < 5000:
                engine.apply_status(actor, enemy, silence(2), parent_seq=trigger_seq)
            if engine.rng.rand_bps("skill_effect", f"paean_disarm:{actor.hero_id}") < 5000:
                engine.apply_status(actor, enemy, disarm(2), parent_seq=trigger_seq)


# =============================================================================
# 注册
# =============================================================================

register(ThunderOracle(skill_id="thunder_oracle", timing=TIMING_PREPARE,
                       is_oracle=True, hint_intensity="strong"))
register(ZeusBolt(skill_id="zeus_bolt", trigger_rate_bps=5000, hint_intensity="strong"))
register(AthenaAegis(skill_id="athena_aegis", timing=TIMING_PREPARE,
                     is_oracle=True, hint_intensity="strong"))
register(AthenaGuard(skill_id="athena_guard", timing=TIMING_PREPARE))
register(AresWarfury(skill_id="ares_warfury", timing=TIMING_PREPARE,
                     is_oracle=True, hint_intensity="strong"))
register(AresRoar(skill_id="ares_roar", trigger_rate_bps=4000))
register(HermesOracle(skill_id="hermes_oracle", timing=TIMING_PREPARE, is_oracle=True))
register(HermesJest(skill_id="hermes_jest", trigger_rate_bps=3500))
register(DelphiRevelation(skill_id="delphi_revelation", timing=TIMING_PREPARE,
                          is_oracle=True))
register(ApolloBlessing(skill_id="apollo_blessing", trigger_rate_bps=4500))
register(AsclepiusOracle(skill_id="asclepius_oracle", timing=TIMING_PREPARE,
                         is_oracle=True))
register(AsclepiusKiss(skill_id="asclepius_kiss", trigger_rate_bps=4500))
register(ArtemisHunt(skill_id="artemis_hunt", timing=TIMING_PREPARE))
register(ArtemisArrow(skill_id="artemis_arrow", trigger_rate_bps=4000,
                      timing=TIMING_PURSUIT))
register(NikeWings(skill_id="nike_wings", timing=TIMING_PREPARE, is_oracle=True))
register(NikePaean(skill_id="nike_paean", trigger_rate_bps=4500))
