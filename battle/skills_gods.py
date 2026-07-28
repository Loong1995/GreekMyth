from __future__ import annotations

"""奥林匹斯（神）阵营战法（Phase 4 v4 池）。

自带：thunder_oracle 雷霆神谕 / athena_aegis 埃癸斯圣盾 / ares_warfury 战神怒火 /
      hermes_oracle 赫尔墨斯神谕 / delphi_revelation 德尔斐启示 /
      asclepius_oracle 蛇杖庇护圣谕 / artemis_hunt 月影狩猎 / nike_wings 胜利羽翼
拆解：zeus_bolt 天雷击 / athena_guard 神盾格挡 / ares_frenzy 战争狂热 /
      hermes_jest 神使戏言 / apollo_blessing 日光祝祷 / asclepius_kiss 灵蛇之吻 /
      artemis_arrow 猎月之矢 / nike_paean 凯歌
"""

from dataclasses import dataclass

from battle.pseudo_random import PseudoRandomParams
from battle.skill_common import (
    ATTR_DELTA_KEYS,
    BPS,
    emit_highlight_trigger,
    emit_status_trigger,
    lowest_troops_enemies,
    pick_distinct_enemies,
)
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.statuses import (
    BUFF,
    DEBUFF,
    PERMANENT,
    SEQUENTIAL,
    SIMULTANEOUS,
    SPECIAL,
    StatusDef,
    disarm,
    hesitation,
    silence,
)
from battle.voice_lines_highlight import emit_highlight_line

# =============================================================================
# 雷霆神谕（宙斯）：己方全体【雷霆】——造成非落雷伤害后 70%（伪随机：失败+9%、
# 成功-7%、30%~85%、4 次保底）追加落雷（触发者智力 85% 魔法），每人每回合 3 次。
# 【神罚】每回合内敌方**单个**单位被落雷打满 3 次 → 宙斯对敌方**兵力最低**单位
# 造成 100% 魔法伤害；发动前走宙斯专属高光台词 + 标准 cut-in 取景。
# 性格·多情联动：宙斯分神（oracle_suppressed 旗标）本回合全队雷霆不触发。
# =============================================================================

_THUNDER_PR = PseudoRandomParams(
    bonus_per_fail_bps=900, penalty_per_success_bps=700,
    min_rate_bps=3000, max_rate_bps=8500, guarantee_fail_count=4,
)
THUNDER_RATE_BPS = 7000
THUNDER_MAX_PER_ROUND = 3
DIVINE_PUNISH_HITS = 3           # 同一敌方单位本回合被落雷击中满此数 → 神罚
DIVINE_PUNISH_RATE_BPS = 10000   # 宙斯智力 100% 魔法
DIVINE_PUNISH_KEY = "divine_punishment"  # 台词池 key（docs/character 高光分场）
# 事件归因 id：神罚不是装配战法（不进 skill_catalog），只作 skill_trigger 归因，
# 客户端据此取中文名「神罚」与专属演出配置。
DIVINE_PUNISH_SKILL_ID = "zeus_divine_punishment"


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
    damage_seq = engine.deal_damage(
        owner, target, damage_type="magic", rate_bps=8500,
        parent_seq=tick_seq, kind="lightning",
    )
    _count_for_divine_punishment(engine, status, target, damage_seq or tick_seq)


def _zeus_thunder_status(engine, zeus_id: str):
    """神罚记账挂在宙斯**自己的**【雷霆】实例上：round_counters 由引擎在回合开始
    统一清零，不必另建回合作用域容器。宙斯阵亡/无雷霆 → None（神罚不判定：
    神罚是宙斯亲自降下的，不是雷霆状态自身的效果）。"""
    zeus = engine.heroes.get(zeus_id)
    if zeus is None or not zeus.is_alive():
        return None
    for owned in engine.hero_statuses(zeus_id):
        if owned.status_id == THUNDER_STATUS.status_id:
            return owned
    return None


def _count_for_divine_punishment(engine, status, victim, parent_seq: int) -> None:
    """落雷落地后按**受击者**记账；同一敌方单位本回合满 3 次 → 神罚（每单位每回合
    一次：只在计数恰好等于阈值那次发动）。"""
    zeus_status = _zeus_thunder_status(engine, status.source_id)
    if zeus_status is None:
        return
    zeus = engine.hero_by_id(status.source_id)
    if victim.team_id == zeus.team_id:
        return  # 魅惑等敌我不分的落雷打到自己人，不计入神罚
    key = f"punish:{victim.hero_id}"
    hits = zeus_status.round_counters.get(key, 0) + 1
    zeus_status.round_counters[key] = hits
    if hits != DIVINE_PUNISH_HITS or engine.game_over():
        return
    targets = lowest_troops_enemies(engine, zeus, 1)
    if not targets:
        return
    # 专属高光：先台词（独立 TraitLine 单元），再 cut-in 取景组
    emit_highlight_line(engine, zeus, DIVINE_PUNISH_KEY)
    punish_seq = emit_highlight_trigger(
        engine, zeus, DIVINE_PUNISH_SKILL_ID, targets, parent_seq,
    )
    engine.deal_damage(
        zeus, targets[0], damage_type="magic", rate_bps=DIVINE_PUNISH_RATE_BPS,
        parent_seq=punish_seq, kind="lightning",
    )


THUNDER_STATUS = StatusDef(
    status_id="thunder", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=30, on_damage_dealt=_thunder_on_damage_dealt,
    # 落雷是「目标头顶劈下」，与持有者无关：同一次群攻引发的多道落雷（哪怕分属
    # 不同持有者）在客户端并成一个播放单元齐发（statuses.SIMULTANEOUS）。
    playback_tags=(SIMULTANEOUS,),
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
# 埃癸斯圣盾（雅典娜）【皇·反制位】：神谕（Phase 4 v4 版，均整局）：
# 1. 己方全体【圣盾】：受到伤害或控制时 15% 免疫，并将原伤害/控制反弹给
#    **敌方随机存活单位**（受击率选取）。伤害走引擎减免通道（reflect_rate_bps
#    + payload reflect_to_random_enemy），控制走控制减免链（control_reflect_bps）；
#    反弹为特殊固定伤害/不可连锁（引擎口径）。与格挡/闪避先后 = 状态施加顺序。
# 2. 己方统率最低单位单次受伤超过其受击前兵力 8% → 雅典娜为其回复
#    （智力×0.9），**每回合最多 2 次**（按雅典娜自身圣盾实例回合计数）。
# 3. 雅典娜额外获得 1 次控制格挡【圣盾·守心】：首次受到硬控消耗并免疫。
# 性格·明睿联动：匠心旁骛（oracle_suppressed）本回合圣盾不生效（含反弹闸门）。
# =============================================================================

AEGIS_COUNTER_RATE_BPS = 1500
AEGIS_HEAL_THRESHOLD_BPS = 800
AEGIS_HEAL_RATE = 9000  # 智力 ×0.9
AEGIS_HEAL_MAX_PER_ROUND = 2


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
    # 每回合最多 2 次：按雅典娜自身持有的圣盾实例计数（全队共享上限）
    counter_holder = engine.find_status(caster.hero_id, "aegis_shield") or status
    if counter_holder.round_counters.get("aegis_heal", 0) >= AEGIS_HEAL_MAX_PER_ROUND:
        return
    base = engine.effective_attr(caster, "intelligence") * AEGIS_HEAL_RATE // BPS
    if base > 0:
        counter_holder.round_counters["aegis_heal"] = (
            counter_holder.round_counters.get("aegis_heal", 0) + 1
        )
        tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
        engine.heal(caster, owner, fixed_base=base, parent_seq=tick_seq)


AEGIS_STATUS = StatusDef(
    status_id="aegis_shield", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=15,
    modifiers={
        "reflect_rate_bps": AEGIS_COUNTER_RATE_BPS,
        "control_reflect_bps": AEGIS_COUNTER_RATE_BPS,
    },
    payload={"reflect_to_random_enemy": True},
    mitigation_gate=_aegis_mitigation_gate,
    on_damage_taken=_aegis_on_damage_taken,
    # 圣盾反制/重击回血的演出是**持有者自己动**（反弹突进、回血闪光），
    # 且语义上要求「逐次触发」——禁止并组（statuses.SEQUENTIAL）。
    playback_tags=(SEQUENTIAL,),
)

# 效果 3：雅典娜个人 1 次控制格挡（首控消耗并免疫；耗尽摘除）
AEGIS_WARD_STATUS = StatusDef(
    status_id="aegis_ward", kind=SPECIAL, duration_rounds=PERMANENT,
    payload={"remove_when_exhausted": True},
)


@dataclass(frozen=True, slots=True)
class AthenaAegis(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, AEGIS_STATUS, parent_seq=trigger_seq)
        ward = engine.apply_status(actor, actor, AEGIS_WARD_STATUS, parent_seq=trigger_seq)
        if ward is not None:
            ward.counters["control_block_charges"] = 1


# =============================================================================
# 神盾格挡（雅典娜拆解）：前三回合格挡率 +30%；三回合以后全队统率 +35。
# =============================================================================

GUARD_BLOCK_STATUS = StatusDef(
    status_id="athena_guard", kind=BUFF, duration_rounds=3,
    modifiers={"block_rate_bps": 3000},
)
GUARD_COMMAND_STATUS = StatusDef(
    status_id="athena_guard_command", kind=BUFF, duration_rounds=PERMANENT,
    modifiers={"command_delta": 35},
)


def _guard_late_on_round_start(engine, status, parent_seq, round_no):
    if round_no <= 3:
        return
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    if engine.find_status(owner.hero_id, "athena_guard_command") is not None:
        return
    source = engine.heroes.get(status.source_id, owner)
    engine.apply_status(source, owner, GUARD_COMMAND_STATUS, parent_seq=parent_seq)


GUARD_LATE_CARRIER = StatusDef(
    status_id="athena_guard_late", kind=SPECIAL, duration_rounds=PERMANENT,
    on_round_start=_guard_late_on_round_start,
)


@dataclass(frozen=True, slots=True)
class AthenaGuard(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, GUARD_BLOCK_STATUS, parent_seq=trigger_seq)
            engine.apply_status(actor, target, GUARD_LATE_CARRIER, parent_seq=trigger_seq)


# =============================================================================
# 战神怒火（阿瑞斯）v5（2026-07-21）：敌我全体【血战】（通用易伤 +20%、
# 暴击伤害 +50%，整局）；己方武力最高者【战神之勇】（武力 +20、速度 +20，
# 整局；并列取小站位）。
# =============================================================================

BLOOD_BATTLE_STATUS = StatusDef(
    status_id="blood_battle", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"vulnerable_bps": 2000, "crit_damage_up_bps": 5000},
)
ARES_MIGHT_STATUS = StatusDef(
    status_id="ares_might", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"force_delta": 20, "speed_delta": 20},
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
            af = engine.effective_attr(ally, "force")
            bf = engine.effective_attr(best, "force")
            if af > bf or (af == bf and ally.position < best.position):
                best = ally
        engine.apply_status(actor, best, ARES_MIGHT_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 战争狂热（阿瑞斯拆解）v6（2026-07-21）：被动（准备阶段自身入场）——
# 自身物理伤害 +30%、暴击率 +15%（整局）。
# =============================================================================

WAR_FRENZY_STATUS = StatusDef(
    status_id="war_frenzy", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"physical_damage_up_bps": 3000, "crit_rate_bps": 1500},
)


@dataclass(frozen=True, slots=True)
class AresFrenzy(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, WAR_FRENZY_STATUS, parent_seq=trigger_seq)


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
            # 延迟率 100%：持有者下次行动窗必延后；duration=1 = 覆盖其下一次
            # 行动窗口（计次 1 仍生效，计次 2 到期；statuses.md §3）。
            engine.apply_status(
                actor, targets[1], hesitation(10000, 1), parent_seq=trigger_seq)


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
# 蛇杖庇护圣谕（阿斯克勒庇俄斯）v4：己方全体【蛇杖庇护】——受实际伤害后 40%
# （伪随机：失败+8%、成功-6%、20%~70%、5 次保底）触发治疗（0.5% 上限 + 施放者智力×1），
# **每名持有者每回合最多 2 次**；每回合结束时对我方兵力比例最低单位额外治疗一次
# （挂施放者【灵蛇看护】）。施放者阵亡 → 全部移除（引擎 source_defeated 通例）。
# =============================================================================

_SNAKE_PR = PseudoRandomParams(
    bonus_per_fail_bps=800, penalty_per_success_bps=600,
    min_rate_bps=2000, max_rate_bps=7000, guarantee_fail_count=5,
)
SNAKE_RATE_BPS = 4000
SNAKE_MAX_TROOP_BPS = 50  # 0.5% 兵力上限
SNAKE_MAX_PER_ROUND = 2


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
    if status.round_counters.get("snake_heal", 0) >= SNAKE_MAX_PER_ROUND:
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
    status.round_counters["snake_heal"] = status.round_counters.get("snake_heal", 0) + 1
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
# 灵蛇之吻（阿斯克勒庇俄斯拆解）v4：主动 50%——驱散己方兵力比例最低者 **1 种**
# 负面，治疗（智力 ×2.5，吃治疗乘区可暴击）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class AsclepiusKiss(Skill):
    def select_targets(self, engine, actor):
        return [engine.select_heal_target_lowest(actor)]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            engine.dispel(target, count=1, parent_seq=trigger_seq)
            base = engine.effective_attr(actor, "intelligence") * 25000 // BPS
            engine.heal(actor, target, fixed_base=base, parent_seq=trigger_seq)


# =============================================================================
# 月影狩猎（阿尔忒弥斯）v4：被动整局——自身造成伤害 +30%；自由选敌类伤害 60%
# 优先选择敌方后排（引擎 prefer_backline_bps；无存活后排时正常选敌不耗 RNG）。
# =============================================================================

MOON_HUNT_STATUS = StatusDef(
    status_id="moon_hunt", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"damage_up_bps": 3000, "prefer_backline_bps": 6000},
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
# 胜利羽翼（尼刻）v4：神谕——己方**武力最高**与**智力最高**单位分别获得
# 【胜利羽翼】（同一人双料最高只得一份）。每回合开始持有者获得 1 次【必胜】
# 计数（下次伤害/治疗必暴击）；持有者击败敌方再获 1 次，
# **每回合最多通过击败额外获得 1 次**。
# =============================================================================

def _wings_on_round_start(engine, status, parent_seq, round_no):
    status.counters["forced_crit_charges"] = (
        status.counters.get("forced_crit_charges", 0) + 1
    )


def _wings_on_hero_defeated(engine, status, ctx):
    if ctx["killer"].hero_id != status.owner_id:
        return
    if status.round_counters.get("kill_gain", 0) >= 1:
        return  # 每回合最多通过击败额外 +1
    status.round_counters["kill_gain"] = 1
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

        def best_by(attr):
            return max(
                allies,
                key=lambda h: (engine.effective_attr(h, attr),
                               -engine.hero_order.index(h.hero_id)),
            )

        force_best, int_best = best_by("force"), best_by("intelligence")
        targets = [force_best]
        if int_best.hero_id != force_best.hero_id:  # 双料最高只得一份
            targets.append(int_best)
        return targets

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            instance = engine.apply_status(actor, target, NIKE_WINGS_STATUS,
                                           parent_seq=trigger_seq)
            if instance is not None:
                instance.counters["forced_crit_charges"] = 1  # r=0 施放即带首回合次数


# =============================================================================
# 凯歌（尼刻拆解）：主动 45%——己方全体获得【先攻】（2 回合）。
# =============================================================================

PAEAN_FIRST_STRIKE = StatusDef(
    status_id="first_strike", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"first_strike": True},
)


@dataclass(frozen=True, slots=True)
class NikePaean(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for ally in targets:
            engine.apply_status(actor, ally, PAEAN_FIRST_STRIKE, parent_seq=trigger_seq)


# =============================================================================
# 注册
# =============================================================================

# damage_type 标签义务见 Skill 基类 docstring：伤害归因（含状态钩子）随定义声明
register(ThunderOracle(skill_id="thunder_oracle", timing=TIMING_PREPARE,
                       is_oracle=True, hint_intensity="strong",
                       damage_type="magic"))  # 雷霆状态钩子落雷
register(ZeusBolt(skill_id="zeus_bolt", trigger_rate_bps=5000, hint_intensity="strong",
                  damage_type="magic"))
register(AthenaAegis(skill_id="athena_aegis", timing=TIMING_PREPARE,
                     is_oracle=True, hint_intensity="strong"))
register(AthenaGuard(skill_id="athena_guard", timing=TIMING_PREPARE))
register(AresWarfury(skill_id="ares_warfury", timing=TIMING_PREPARE,
                     is_oracle=True, hint_intensity="strong"))
register(AresFrenzy(skill_id="ares_frenzy", timing=TIMING_PREPARE))
register(HermesOracle(skill_id="hermes_oracle", timing=TIMING_PREPARE, is_oracle=True))
register(HermesJest(skill_id="hermes_jest", trigger_rate_bps=5000))
register(DelphiRevelation(skill_id="delphi_revelation", timing=TIMING_PREPARE,
                          is_oracle=True))
register(ApolloBlessing(skill_id="apollo_blessing", trigger_rate_bps=4500))
register(AsclepiusOracle(skill_id="asclepius_oracle", timing=TIMING_PREPARE,
                         is_oracle=True))
register(AsclepiusKiss(skill_id="asclepius_kiss", trigger_rate_bps=5000))
register(ArtemisHunt(skill_id="artemis_hunt", timing=TIMING_PREPARE))
register(ArtemisArrow(skill_id="artemis_arrow", trigger_rate_bps=4000,
                      timing=TIMING_PURSUIT, damage_type="magic"))
register(NikeWings(skill_id="nike_wings", timing=TIMING_PREPARE, is_oracle=True))
register(NikePaean(skill_id="nike_paean", trigger_rate_bps=4500))
