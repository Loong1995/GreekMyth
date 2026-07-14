from __future__ import annotations

"""人阵营战法（v3.1 池，机制标签：暴击与追加）。数值以 phase3 任务书 §三/§六 为准。

自带：achilles_wrath 阿喀琉斯之怒 / heracles_trials 十二试炼 / odysseus_trojan 木马奇谋 /
      perseus_relics 神器三借 / atalanta_swift 疾风女猎 / paris_fatal_arrow 致命一矢 /
      ajax_shield 七重牛皮盾 / chiron_medicine 贤者医术
拆解：achilles_thrust 怒火突刺 / heracles_counter 狮皮反击 / odysseus_feint 声东击西 /
      perseus_flash 镜盾闪袭 / atalanta_dash 疾走 / paris_heelseek 觅踵 /
      ajax_bulwark 坚壁 / chiron_maxim 导师箴言
"""

from dataclasses import dataclass

from battle import traits as tr
from battle.skill_common import BPS, emit_status_trigger, pick_distinct_enemies
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.statuses import (
    BUFF,
    DEBUFF,
    PERMANENT,
    SPECIAL,
    StatusDef,
    hesitation,
    silence,
)

# =============================================================================
# 阿喀琉斯之怒 ★验收标杆：被动（准备回合必发）——自身物理暴击率 +20%；
# 每次暴击后追加 120% 兵刃（无视统帅、不可暴击），每回合最多 3 次
# （2026-07-09 人工调参：60%→120%）。
# 性格·傲慢联动：目标残兵比例高于自身时 25% 判定，成功则本次追伤最终 ×1.5。
# =============================================================================

ACHILLES_FURY_RATE_BPS = 12000
ACHILLES_FURY_MAX_PER_ROUND = 3


def _achilles_on_damage_dealt(engine, status, ctx):
    if not ctx["is_crit"] or ctx["kind"] == "fury":
        return
    if status.round_counters.get("fury", 0) >= ACHILLES_FURY_MAX_PER_ROUND:
        return
    target = ctx["target"]
    if not target.is_alive():
        return  # 暴击已致死 → 无追加对象
    status.round_counters["fury"] = status.round_counters.get("fury", 0) + 1
    owner = engine.hero_by_id(status.owner_id)
    rate = ACHILLES_FURY_RATE_BPS
    trait = tr.of(owner)
    if trait is not None:  # 傲慢：追伤最终伤害 ×1.5（等效系数放大）
        boost = trait.pursuit_boost_bps(engine, owner, target, ctx["damage_seq"])
        if boost > 0:
            rate = rate * (BPS + boost) // BPS
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    if trait is not None and "pierce" in trait.lines:
        # 追伤必发贯穿台词（傲慢配 pierce 台词；不消耗 RNG）
        tr.emit_trigger(engine, owner, "pierce", parent_seq=tick_seq)
    engine.deal_damage(
        owner, target, damage_type="physical", rate_bps=rate,
        parent_seq=tick_seq, kind="fury", can_crit=False, ignore_defense=True,
    )


ACHILLES_WRATH_STATUS = StatusDef(
    status_id="achilles_wrath", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"physical_crit_rate_bps": 2000},
    response_priority=25, on_damage_dealt=_achilles_on_damage_dealt,
)


@dataclass(frozen=True, slots=True)
class AchillesWrath(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, ACHILLES_WRATH_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 怒火突刺（阿喀琉斯拆解）：追击 40%——自身暴击率 +20%（2 回合，刷新不叠加），
# 对普攻目标 300% 兵刃；若该击暴击，追加一次 80% 兵刃。
# =============================================================================

THRUST_CRIT_STATUS = StatusDef(
    status_id="achilles_thrust_crit", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"crit_rate_bps": 2000},
)


@dataclass(frozen=True, slots=True)
class AchillesThrust(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, THRUST_CRIT_STATUS, parent_seq=trigger_seq)
        for target in targets:
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=30000,
                parent_seq=trigger_seq, kind="pursuit",
            )
            if engine.last_damage_result.get("is_crit") and target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=8000,
                    parent_seq=trigger_seq, kind="pursuit", can_crit=False,
                )


# =============================================================================
# 十二试炼（赫拉克勒斯）：被动——受攻击后 70%（普通随机）触发：武力 +6、
# 物理吸血 +3%（累计）、对随机两名敌方各 60% 兵刃反打。
# 每局最多 12 次；每回合最多 4 次。
# =============================================================================

TRIALS_RATE_BPS = 7000
TRIALS_MAX_PER_GAME = 12
TRIALS_MAX_PER_ROUND = 4
TRIALS_FORCE_GAIN = 6
TRIALS_LIFESTEAL_GAIN_BPS = 300


def _trials_on_damage_taken(engine, status, ctx):
    if ctx["kind"] in ("trial", "counter"):
        return  # 反打类不触发试炼（防镜像递归）
    if status.counters.get("trials", 0) >= TRIALS_MAX_PER_GAME:
        return
    if status.round_counters.get("trials", 0) >= TRIALS_MAX_PER_ROUND:
        return
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    roll = engine.rng.rand_bps("status_trigger", f"trials:{status.owner_id}")
    if roll >= TRIALS_RATE_BPS:
        return
    status.counters["trials"] = status.counters.get("trials", 0) + 1
    status.round_counters["trials"] = status.round_counters.get("trials", 0) + 1
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.adjust_status_attr(status, "force", TRIALS_FORCE_GAIN, parent_seq=tick_seq)
    status.dynamic_modifiers["physical_lifesteal_bps"] = (
        status.dynamic_modifiers.get("physical_lifesteal_bps", 0) + TRIALS_LIFESTEAL_GAIN_BPS
    )
    struck: list[str] = []
    for strike_no in range(2):
        if engine.game_over() or not owner.is_alive():
            return
        target = engine.select_enemy_by_hit_rate(
            owner, reason=f"trials:{status.owner_id}:{strike_no}", exclude_ids=tuple(struck)
        )
        if target is None:
            return
        engine.deal_damage(
            owner, target, damage_type="physical", rate_bps=6000,
            parent_seq=tick_seq, kind="trial",
        )
        struck.append(target.hero_id)


HERACLES_TRIALS_STATUS = StatusDef(
    status_id="heracles_trials", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=50, on_damage_taken=_trials_on_damage_taken,
)


@dataclass(frozen=True, slots=True)
class HeraclesTrials(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, HERACLES_TRIALS_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 狮皮反击（赫拉克勒斯拆解）：被动（受击响应）40%——受攻击后对来源反打 45% 兵刃，
# 并使其造成伤害 -20%（2 回合）。
# =============================================================================

LION_WEAKEN_STATUS = StatusDef(
    status_id="lion_weaken", kind=DEBUFF, duration_rounds=2,
    modifiers={"damage_up_bps": -2000},
)


def _lion_on_damage_taken(engine, status, ctx):
    if ctx["kind"] in ("counter", "trial"):
        return  # 反打不触发反打（同源防递归）
    owner = engine.hero_by_id(status.owner_id)
    source = ctx["source"]
    if not owner.is_alive() or not source.is_alive() or source.team_id == owner.team_id:
        return
    roll = engine.rng.rand_bps("status_trigger", f"lion:{status.owner_id}")
    if roll >= status.definition.payload["rate_bps"]:
        return
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.deal_damage(
        owner, source, damage_type="physical",
        rate_bps=status.definition.payload["damage_rate_bps"],
        parent_seq=tick_seq, kind="counter",
    )
    if status.definition.payload.get("weaken", False) and source.is_alive():
        engine.apply_status(owner, source, LION_WEAKEN_STATUS, parent_seq=tick_seq)


LION_COUNTER_STATUS = StatusDef(
    status_id="lion_counter", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=50, on_damage_taken=_lion_on_damage_taken,
    payload={"rate_bps": 4000, "damage_rate_bps": 4500, "weaken": True},
)


@dataclass(frozen=True, slots=True)
class LionCounter(Skill):
    """狮皮反击载体（守门恶犬/漩涡巨口复用同一钩子，不同参数状态）。"""

    status_def: StatusDef = LION_COUNTER_STATUS

    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, self.status_def, parent_seq=trigger_seq)


# =============================================================================
# 木马奇谋（奥德修斯）★半核心：被动——第 3 回合开始必发：敌方全体【犹豫】+
# 【木马炸弹】；第 4 回合持有者行动前爆炸（奥德修斯智力 100% 魔法 + 缄默）。
# =============================================================================

def _trojan_bomb_on_action_start(engine, status, action_seq):
    if engine.current_round < status.definition.payload["explode_round"]:
        return
    owner = engine.hero_by_id(status.owner_id)
    source = engine.heroes.get(status.source_id)
    engine.remove_status(status, reason="consumed", parent_seq=action_seq)
    if source is None or not source.is_alive() or not owner.is_alive():
        return
    tick_seq = emit_status_trigger(engine, status, action_seq)
    engine.deal_damage(
        source, owner, damage_type="magic", rate_bps=10000,
        parent_seq=tick_seq, kind="trojan",
    )
    if owner.is_alive():
        engine.apply_status(source, owner, silence(1), parent_seq=tick_seq)


TROJAN_BOMB_STATUS = StatusDef(
    status_id="trojan_bomb", kind=DEBUFF, duration_rounds=PERMANENT,
    response_priority=5, on_action_start=_trojan_bomb_on_action_start,
    payload={"explode_round": 4},
)


def _trojan_on_round_start(engine, status, parent_seq, round_no):
    if round_no != 3 or status.counters.get("armed", 0):
        return
    status.counters["armed"] = 1
    owner = engine.hero_by_id(status.owner_id)
    tick_seq = emit_status_trigger(engine, status, parent_seq)
    for enemy in engine.alive_enemies(owner):
        engine.apply_status(owner, enemy, hesitation(), parent_seq=tick_seq)
        engine.apply_status(owner, enemy, TROJAN_BOMB_STATUS, parent_seq=tick_seq)


TROJAN_SCHEME_STATUS = StatusDef(
    status_id="trojan_scheme", kind=SPECIAL, duration_rounds=PERMANENT,
    on_round_start=_trojan_on_round_start,
)


@dataclass(frozen=True, slots=True)
class OdysseusTrojan(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, TROJAN_SCHEME_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 声东击西（奥德修斯拆解）：主动 35%——对敌 2~3 人（等概率）各 220% 魔法，
# 各 40% 施加【犹豫】。
# =============================================================================

@dataclass(frozen=True, slots=True)
class OdysseusFeint(Skill):
    def select_targets(self, engine, actor):
        count = 2 + engine.rng.rand_index(2, "skill_effect", f"feint_count:{actor.hero_id}")
        return pick_distinct_enemies(engine, actor, count, f"skill:{self.skill_id}")

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=22000,
                parent_seq=trigger_seq,
            )
            if target.is_alive():
                roll = engine.rng.rand_bps(
                    "skill_effect", f"feint_hes:{actor.hero_id}:{target.hero_id}"
                )
                if roll < 4000:
                    engine.apply_status(actor, target, hesitation(), parent_seq=trigger_seq)


# =============================================================================
# 神器三借（珀尔修斯）★新机制卖点：主动 55%【疾影连击】——2~4 段（等概率），
# 每段 60% 单独 roll 选敌方后排（否则正常受击率选人），每段 120% 兵刃；
# 每段命中后自身闪避 +8%（2 回合，最多叠 5 次，段间即时生效）。
# 常驻被动（perseus_mirror 镜盾）：免疫石化。
# =============================================================================

PERSEUS_EVADE_STATUS = StatusDef(
    status_id="perseus_evade", kind=BUFF, duration_rounds=2, max_stacks=5,
    refreshable=True, modifiers={"evade_bps": 800},
)
PERSEUS_MIRROR_STATUS = StatusDef(
    status_id="perseus_mirror", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"petrify_immune": True},
)


@dataclass(frozen=True, slots=True)
class PerseusMirror(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, PERSEUS_MIRROR_STATUS, parent_seq=trigger_seq)


@dataclass(frozen=True, slots=True)
class PerseusRelics(Skill):
    min_strikes: int = 2
    max_strikes: int = 4

    def execute(self, engine, actor, targets, trigger_seq):
        span = self.max_strikes - self.min_strikes + 1
        strikes = self.min_strikes + engine.rng.rand_index(
            span, "skill_effect", f"relics_count:{actor.hero_id}"
        )
        for strike_no in range(strikes):
            if engine.game_over() or not actor.is_alive():
                return
            enemies = engine.alive_enemies(actor)
            if not enemies:
                return
            target = None
            roll = engine.rng.rand_bps(
                "skill_effect", f"relics_back:{actor.hero_id}:{strike_no}"
            )
            if roll < 6000:  # 后排偏好：站位最大子集里按受击率选
                max_pos = max(h.position for h in enemies)
                back = [h for h in enemies if h.position == max_pos]
                if len(back) < len(enemies):
                    weights = [h.hit_points_bps() for h in back]
                    idx = engine.rng.rand_weighted_index(
                        weights, "target_select", f"relics_back:{actor.hero_id}:{strike_no}"
                    )
                    target = back[idx]
            if target is None:
                target = engine.select_enemy_by_hit_rate(
                    actor, reason=f"relics:{actor.hero_id}:{strike_no}"
                )
            if target is None:
                return
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=12000,
                parent_seq=trigger_seq, kind="relics",
            )
            if engine.last_damage_result.get("mitigation") is None:
                engine.apply_status(actor, actor, PERSEUS_EVADE_STATUS,
                                    parent_seq=trigger_seq)


# =============================================================================
# 镜盾闪袭（珀尔修斯拆解）：追击 40%——普攻后追加 260% 兵刃；命中后自身闪避 +10%
# （2 回合）。
# =============================================================================

FLASH_EVADE_STATUS = StatusDef(
    status_id="perseus_flash_evade", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"evade_bps": 1000},
)


@dataclass(frozen=True, slots=True)
class PerseusFlash(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=26000,
                parent_seq=trigger_seq, kind="pursuit",
            )
            if engine.last_damage_result.get("mitigation") is None:
                engine.apply_status(actor, actor, FLASH_EVADE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 疾风女猎（阿塔兰忒）：被动整局——速度 +15；若本回合先于所有敌军行动，
# 普攻后对敌方两人各 140% 兵刃（每回合一次）。
# =============================================================================

def _swift_on_action_start(engine, status, action_seq):
    if engine.acted_before_all_enemies(engine.hero_by_id(status.owner_id)):
        status.round_counters["first_mover"] = 1


def _swift_on_damage_dealt(engine, status, ctx):
    if ctx["kind"] != "basic" or not status.round_counters.get("first_mover"):
        return
    if status.round_counters.get("swift_burst"):
        return
    status.round_counters["swift_burst"] = 1
    owner = engine.hero_by_id(status.owner_id)
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    struck: list[str] = []
    for strike_no in range(2):
        if engine.game_over() or not owner.is_alive():
            return
        target = engine.select_enemy_by_hit_rate(
            owner, reason=f"swift:{status.owner_id}:{strike_no}", exclude_ids=tuple(struck)
        )
        if target is None:
            return
        engine.deal_damage(
            owner, target, damage_type="physical", rate_bps=14000,
            parent_seq=tick_seq, kind="swift",
        )
        struck.append(target.hero_id)


ATALANTA_SWIFT_STATUS = StatusDef(
    status_id="atalanta_swift", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"speed_delta": 15},
    response_priority=35,
    on_action_start=_swift_on_action_start,
    on_damage_dealt=_swift_on_damage_dealt,
)


@dataclass(frozen=True, slots=True)
class AtalantaSwift(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, ATALANTA_SWIFT_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 疾走（阿塔兰忒拆解）：被动——速度 +15（整局）；前三回合自身伤害 +20%。
# =============================================================================

DASH_SPEED_STATUS = StatusDef(
    status_id="atalanta_dash_speed", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"speed_delta": 15},
)
DASH_DAMAGE_STATUS = StatusDef(
    status_id="atalanta_dash_damage", kind=BUFF, duration_rounds=3,
    modifiers={"damage_up_bps": 2000},
)


@dataclass(frozen=True, slots=True)
class AtalantaDash(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, DASH_SPEED_STATUS, parent_seq=trigger_seq)
        engine.apply_status(actor, actor, DASH_DAMAGE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 致命一矢（帕里斯）：被动整局——自身暴击率 +30%；攻击暴击率 ≥50% 的目标时
# 必定触发暴击（on_pre_damage_dealt）。
# =============================================================================

def _fatal_arrow_pre_damage(engine, status, ctx):
    target_crit = engine.total_crit_rate(ctx["target"], ctx["damage_type"])
    if target_crit >= status.definition.payload["threshold_bps"]:
        if status.definition.payload.get("forced_crit"):
            ctx["forced_crit"] = True
        bonus = status.definition.payload.get("damage_up_bonus_bps", 0)
        if bonus:
            ctx["damage_up_bonus"] += bonus


FATAL_ARROW_STATUS = StatusDef(
    status_id="paris_fatal_arrow", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"crit_rate_bps": 3000},
    on_pre_damage_dealt=_fatal_arrow_pre_damage,
    payload={"threshold_bps": 5000, "forced_crit": True},
)

HEELSEEK_STATUS = StatusDef(
    status_id="paris_heelseek", kind=SPECIAL, duration_rounds=PERMANENT,
    on_pre_damage_dealt=_fatal_arrow_pre_damage,
    payload={"threshold_bps": 3000, "damage_up_bonus_bps": 3500},
)


@dataclass(frozen=True, slots=True)
class SelfStatusPassive(Skill):
    """通用被动载体：准备回合给自身挂一个状态。"""

    status_def: StatusDef = FATAL_ARROW_STATUS

    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, self.status_def, parent_seq=trigger_seq)


# =============================================================================
# 七重牛皮盾（大埃阿斯）：被动整局——前三回合每回合开始获得 1 次格挡；
# 自身受到伤害 -10%。
# =============================================================================

def _ajax_on_round_start(engine, status, parent_seq, round_no):
    if round_no > 3:
        return
    owner = engine.hero_by_id(status.owner_id)
    engine.grant_block(owner, 1, source=engine.heroes.get(status.source_id, owner),
                       parent_seq=parent_seq)


AJAX_SHIELD_STATUS = StatusDef(
    status_id="ajax_shield", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"damage_reduce_bps": 1000},
    on_round_start=_ajax_on_round_start,
)


# =============================================================================
# 坚壁（大埃阿斯拆解）：主动 40%——己方统率最低者获得 1 次格挡并统率 +15（2 回合）。
# =============================================================================

BULWARK_COMMAND_STATUS = StatusDef(
    status_id="ajax_bulwark_command", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"command_delta": 15},
)


@dataclass(frozen=True, slots=True)
class AjaxBulwark(Skill):
    def select_targets(self, engine, actor):
        allies = engine.alive_allies(actor)
        best = min(
            allies,
            key=lambda h: (engine.effective_attr(h, "command"),
                           engine.hero_order.index(h.hero_id)),
        )
        return [best]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.grant_block(target, 1, source=actor, parent_seq=trigger_seq)
            engine.apply_status(actor, target, BULWARK_COMMAND_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 贤者医术（喀戎）：主动 45%——治疗己方兵力比例最低者（智力 ×2.2，可暴击），
# 并驱散其 1 种负面状态。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ChironMedicine(Skill):
    def select_targets(self, engine, actor):
        return [engine.select_heal_target_lowest(actor)]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            base = engine.effective_attr(actor, "intelligence") * 22000 // BPS
            engine.heal(actor, target, fixed_base=base, parent_seq=trigger_seq)
            engine.dispel(target, count=1, parent_seq=trigger_seq)


# =============================================================================
# 导师箴言（喀戎拆解）：被动——己方全体武力 +10（整局，平加层，复用神示口径）。
# =============================================================================

MAXIM_STATUS = StatusDef(
    status_id="chiron_maxim", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"force_delta": 10},
)


@dataclass(frozen=True, slots=True)
class ChironMaxim(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, MAXIM_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 注册
# =============================================================================

register(AchillesWrath(skill_id="achilles_wrath", timing=TIMING_PREPARE))
register(AchillesThrust(skill_id="achilles_thrust", trigger_rate_bps=4000,
                        timing=TIMING_PURSUIT))
register(HeraclesTrials(skill_id="heracles_trials", timing=TIMING_PREPARE))
register(LionCounter(skill_id="heracles_counter", timing=TIMING_PREPARE,
                     status_def=LION_COUNTER_STATUS))
register(OdysseusTrojan(skill_id="odysseus_trojan", timing=TIMING_PREPARE,
                        hint_intensity="strong"))
register(OdysseusFeint(skill_id="odysseus_feint", trigger_rate_bps=3500))
register(PerseusRelics(skill_id="perseus_relics", trigger_rate_bps=5500,
                       hint_intensity="strong"))
register(PerseusMirror(skill_id="perseus_mirror", timing=TIMING_PREPARE))
register(PerseusFlash(skill_id="perseus_flash", trigger_rate_bps=4000,
                      timing=TIMING_PURSUIT))
register(AtalantaSwift(skill_id="atalanta_swift", timing=TIMING_PREPARE))
register(AtalantaDash(skill_id="atalanta_dash", timing=TIMING_PREPARE))
register(SelfStatusPassive(skill_id="paris_fatal_arrow", timing=TIMING_PREPARE,
                           status_def=FATAL_ARROW_STATUS))
register(SelfStatusPassive(skill_id="paris_heelseek", timing=TIMING_PREPARE,
                           status_def=HEELSEEK_STATUS))
register(SelfStatusPassive(skill_id="ajax_shield", timing=TIMING_PREPARE,
                           status_def=AJAX_SHIELD_STATUS))
register(AjaxBulwark(skill_id="ajax_bulwark", trigger_rate_bps=4000))
register(ChironMedicine(skill_id="chiron_medicine", trigger_rate_bps=4500))
register(ChironMaxim(skill_id="chiron_maxim", timing=TIMING_PREPARE))
