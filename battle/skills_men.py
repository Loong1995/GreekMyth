from __future__ import annotations

"""英雄（人）阵营战法（Phase 4 v4 池，机制标签：暴击/追加/连击/协击）。

自带：achilles_wrath 阿喀琉斯之怒 / heracles_trials 十二试炼 / odysseus_trojan 木马奇谋 /
      perseus_relics 镜盾疾袭 / hector_warcry 特洛伊战吼 / atalanta_swift 疾风女猎 /
      paris_fatal_arrow 致命一矢 / ajax_shield 七重牛皮盾 /
      jason_expedition 英雄远征 / castor_twin 双子协战
拆解：achilles_thrust 怒火突刺 / heracles_counter 狮皮反击 / odysseus_feint 声东击西 /
      perseus_flash 镜盾闪击 / hector_assault 决死猛攻 / atalanta_dash 疾走 /
      paris_heelseek 觅踵 / ajax_bulwark 坚壁 / jason_command 金羊号令 /
      castor_chase 并辔追击
喀戎（chiron_medicine/chiron_maxim）v4 下架（manual_tasks 拍板项 2）。
奥德修斯战法保留在本模块，A4 阵营重划（→海域）时随批迁移。
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
# 阿喀琉斯之怒：被动——物理暴击率 +25%；暴击后追加 80% 兵刃（无视统帅、可暴击），
# 每回合最多 7 次；追加可再触发（链式，回合计数封顶）。
# 性格·傲慢：目标残兵高于自身 25% 判定 → 追伤 ×1.5。
# =============================================================================

ACHILLES_FURY_RATE_BPS = 8000
ACHILLES_FURY_MAX_PER_ROUND = 7


def _achilles_on_damage_dealt(engine, status, ctx):
    if not ctx["is_crit"]:
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
    if trait is not None:  # 傲慢：目标残兵比例更高时 25% → 追伤 ×1.5 + 贯穿台词
        boost = trait.pursuit_boost_bps(engine, owner, target, ctx["damage_seq"])
        if boost > 0:
            rate = rate * (BPS + boost) // BPS
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.deal_damage(
        owner, target, damage_type="physical", rate_bps=rate,
        parent_seq=tick_seq, kind="fury", can_crit=True, ignore_defense=True,
    )


ACHILLES_WRATH_STATUS = StatusDef(
    status_id="achilles_wrath", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"physical_crit_rate_bps": 2500},
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
# 并 70% 使其造成伤害 -20%（1 回合，削弱独立判定）。
# =============================================================================

LION_WEAKEN_STATUS = StatusDef(
    status_id="lion_weaken", kind=DEBUFF, duration_rounds=1,
    refreshable=True, modifiers={"damage_up_bps": -2000},
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
    weaken_rate = status.definition.payload.get("weaken_rate_bps", 0)
    if weaken_rate and source.is_alive():
        w = engine.rng.rand_bps("status_trigger", f"lion_weaken:{status.owner_id}")
        if w < weaken_rate:
            engine.apply_status(owner, source, LION_WEAKEN_STATUS, parent_seq=tick_seq)


LION_COUNTER_STATUS = StatusDef(
    status_id="lion_counter", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=50, on_damage_taken=_lion_on_damage_taken,
    payload={"rate_bps": 4000, "damage_rate_bps": 4500, "weaken_rate_bps": 7000},
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
# 镜盾疾袭（珀尔修斯，v4 改版）：主动 60%——1~2 段（等概率），每段 120% 兵刃；
# 每段 60% 单独 roll 优先选敌方后排（站位 4~6；无后排/全后排时正常受击率选人）；
# 每段造成实际伤害后自身获得 1 层【格挡】（2 回合，最多持有 2 层）。
# 常驻被动（perseus_mirror 镜盾）：免疫石化。
# 性格·借宝：己方每名奥林匹斯（神）友军使本战法连发率 +15%（traits._Jiebao）。
# =============================================================================

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
    min_strikes: int = 1
    max_strikes: int = 2

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
            back = [h for h in enemies if h.is_backline]
            if back and len(back) < len(enemies):  # 后排/非后排并存才 roll
                roll = engine.rng.rand_bps(
                    "skill_effect", f"relics_back:{actor.hero_id}:{strike_no}"
                )
                if roll < 6000:  # 后排子集内仍按受击率加权
                    weights = [engine._hit_weight(h) for h in back]
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
                engine.grant_block(actor, 1, source=actor, parent_seq=trigger_seq,
                                   max_charges=2, duration_rounds=2)


# =============================================================================
# 镜盾闪击（珀尔修斯拆解）：主动 55%——自身获得 1 层【格挡】，
# 并对敌方单体（受击率选人）造成 320% 兵刃。
# =============================================================================

@dataclass(frozen=True, slots=True)
class PerseusFlash(Skill):
    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(
            actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        engine.grant_block(actor, 1, source=actor, parent_seq=trigger_seq)
        for target in targets:
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=32000,
                parent_seq=trigger_seq,
            )


# =============================================================================
# 疾风女猎（阿塔兰忒）：被动整局——速度 +35（v4 调参）；若本回合先于所有敌军行动，
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
    modifiers={"speed_delta": 35},
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
# 疾走（阿塔兰忒拆解）：被动——速度 +20（整局，v4 调参）；前三回合自身伤害 +20%。
# =============================================================================

DASH_SPEED_STATUS = StatusDef(
    status_id="atalanta_dash_speed", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"speed_delta": 20},
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
    modifiers={"crit_rate_bps": 3000, "crit_damage_up_bps": 5000},
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
# 七重牛皮盾（大埃阿斯，v4 改版）：被动整局——自身统率 +20%（百分比乘区）；
# 前三回合开始时获得 2 层【格挡】，最多持有 2 层。
# =============================================================================

def _ajax_on_round_start(engine, status, parent_seq, round_no):
    if round_no > 3:
        return
    owner = engine.hero_by_id(status.owner_id)
    engine.grant_block(owner, 2, source=engine.heroes.get(status.source_id, owner),
                       parent_seq=parent_seq, max_charges=2)


AJAX_SHIELD_STATUS = StatusDef(
    status_id="ajax_shield", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"command_bps": 2000},
    on_round_start=_ajax_on_round_start,
)


# =============================================================================
# 坚壁（大埃阿斯拆解）：主动 60%——己方兵力比例最低的 2 名单位分别
# 获得 1 层【格挡】并统率 +40（2 回合）。
# =============================================================================

BULWARK_COMMAND_STATUS = StatusDef(
    status_id="ajax_bulwark_command", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"command_delta": 40},
)


@dataclass(frozen=True, slots=True)
class AjaxBulwark(Skill):
    def select_targets(self, engine, actor):
        allies = sorted(
            engine.alive_allies(actor),
            key=lambda h: (h.troops * BPS // h.max_troops,
                           engine.hero_order.index(h.hero_id)),
        )
        return allies[:2]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.grant_block(target, 1, source=actor, parent_seq=trigger_seq)
            engine.apply_status(actor, target, BULWARK_COMMAND_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 特洛伊战吼（赫克托尔·自带，Phase 4 新增）：主动 45%，准备 1 回合。
# 释放：对敌全体 170% 兵刃；每名目标两次独立 50% 判定——缄默（1 回合）/
# 缴械（1 回合）。连发不需重新准备（引擎 _cast_active_skill 释放段直接连发）。
# 性格·忠烈：每次成功释放叠 +15% 连发率（≤2 层，traits._Zhonglie）。
# =============================================================================

WARCRY_CONTROL_RATE_BPS = 5000


@dataclass(frozen=True, slots=True)
class HectorWarcry(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_enemies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        from battle.statuses import disarm
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=17000,
                parent_seq=trigger_seq,
            )
            if not target.is_alive():
                continue
            for effect, builder in (("silence", silence), ("disarm", disarm)):
                roll = engine.rng.rand_bps(
                    "skill_effect", f"warcry_{effect}:{actor.hero_id}:{target.hero_id}"
                )
                if roll < WARCRY_CONTROL_RATE_BPS:
                    engine.apply_status(actor, target, builder(1), parent_seq=trigger_seq)


# =============================================================================
# 决死猛攻（赫克托尔拆解，Phase 4 新增）：主动 45%——对敌全体 180% 兵刃；
# 每成功释放一次，本战法伤害系数 +20%，最多累计 5 次（自身隐藏计数状态）。
# =============================================================================

ASSAULT_STACK_STATUS = StatusDef(
    status_id="hector_assault_stack", kind=SPECIAL, duration_rounds=PERMANENT,
)
ASSAULT_BASE_RATE_BPS = 18000
ASSAULT_STEP_BPS = 2000  # 每次成功释放 +20% 系数
ASSAULT_MAX_STACKS = 5


@dataclass(frozen=True, slots=True)
class HectorAssault(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_enemies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        carrier = engine.find_status(actor.hero_id, "hector_assault_stack")
        stacks = carrier.counters.get("assault", 0) if carrier is not None else 0
        rate = ASSAULT_BASE_RATE_BPS + ASSAULT_STEP_BPS * stacks
        for target in targets:
            if engine.game_over() or not actor.is_alive():
                return
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="physical", rate_bps=rate,
                parent_seq=trigger_seq,
            )
        if carrier is None:
            carrier = engine.apply_status(actor, actor, ASSAULT_STACK_STATUS,
                                          parent_seq=trigger_seq)
        if carrier is not None and stacks < ASSAULT_MAX_STACKS:
            carrier.counters["assault"] = stacks + 1


# =============================================================================
# 英雄远征（伊阿宋·自带，Phase 4 新增）：被动，准备阶段发动。
# 己方武力最高者前 2 回合【清醒】（免疫硬控）；每回合开始时己方当前武力最高者
# 本回合连击率 +35%（1 回合状态，逐回合重选）。
# =============================================================================

EXPEDITION_COMBO_STATUS = StatusDef(
    status_id="jason_expedition_combo", kind=BUFF, duration_rounds=1,
    refreshable=True, modifiers={"combo_rate_bps": 3500},
)


def _highest_force_ally(engine, hero):
    return max(
        engine.alive_allies(hero),
        key=lambda h: (engine.effective_attr(h, "force"),
                       -engine.hero_order.index(h.hero_id)),
    )


def _expedition_on_round_start(engine, status, parent_seq, round_no):
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    best = _highest_force_ally(engine, owner)
    engine.apply_status(owner, best, EXPEDITION_COMBO_STATUS, parent_seq=parent_seq)


JASON_EXPEDITION_STATUS = StatusDef(
    status_id="jason_expedition", kind=SPECIAL, duration_rounds=PERMANENT,
    on_round_start=_expedition_on_round_start,
)


@dataclass(frozen=True, slots=True)
class JasonExpedition(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        from battle.statuses import clear_mind
        best = _highest_force_ally(engine, actor)
        engine.apply_status(actor, best, clear_mind(2), parent_seq=trigger_seq)
        engine.apply_status(actor, actor, JASON_EXPEDITION_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 金羊号令（伊阿宋拆解，Phase 4 新增）：主动 70%——己方武力最高 2 名单位
# 连击率 +40%（2 回合）；若目标施加前已拥有连击率，额外使其伤害 +10%（2 回合）。
# =============================================================================

COMMAND_COMBO_STATUS = StatusDef(
    status_id="jason_command_combo", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"combo_rate_bps": 4000},
)
COMMAND_DAMAGE_STATUS = StatusDef(
    status_id="jason_command_damage", kind=BUFF, duration_rounds=2,
    refreshable=True, max_stacks=2, modifiers={"damage_up_bps": 1000},
)


@dataclass(frozen=True, slots=True)
class JasonCommand(Skill):
    def select_targets(self, engine, actor):
        allies = sorted(
            engine.alive_allies(actor),
            key=lambda h: (-engine.effective_attr(h, "force"),
                           engine.hero_order.index(h.hero_id)),
        )
        return allies[:2]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            had_combo = engine.modifier(target, "combo_rate_bps") > 0
            engine.apply_status(actor, target, COMMAND_COMBO_STATUS, parent_seq=trigger_seq)
            if had_combo:
                engine.apply_status(actor, target, COMMAND_DAMAGE_STATUS,
                                    parent_seq=trigger_seq)


# =============================================================================
# 双子协战（卡斯托耳·自带，Phase 4 新增）：被动——队友普攻后 50% 对同一目标
# 发动协击普攻（perform_coordinated_attack：不占行动、不连击、可追击），
# 每回合最多 2 次。性格·并辔：coord_certain 旗标使本次判定必成功（消费即清）。
# =============================================================================

def _twin_on_ally_basic(engine, status, ctx):
    """并辔 coord_certain：必成功且不计入协击回合上限（可在已达上限时仍触发）。"""
    payload = status.definition.payload
    owner = engine.hero_by_id(status.owner_id)
    target = ctx["target"]
    if not owner.is_alive() or not target.is_alive():
        return
    certain = payload.get("consume_certain") and engine.trait_flag(
        owner.hero_id, "coord_certain")
    if certain:
        engine.clear_trait_flag(owner.hero_id, "coord_certain")
    else:
        if status.round_counters.get("coord", 0) >= payload["max_per_round"]:
            return
        roll = engine.rng.rand_bps("status_trigger", f"twin:{status.owner_id}")
        if roll >= payload["rate_bps"]:
            return
        status.round_counters["coord"] = status.round_counters.get("coord", 0) + 1
    tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
    engine.perform_coordinated_attack(owner, target, parent_seq=tick_seq)


CASTOR_TWIN_STATUS = StatusDef(
    status_id="castor_twin", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=40, on_ally_basic_attack=_twin_on_ally_basic,
    payload={"rate_bps": 5000, "max_per_round": 2, "consume_certain": True},
)


# =============================================================================
# 并辔追击（卡斯托耳拆解，Phase 4 新增）：被动——自身吸血 +10%；
# 队友普攻后 35% 对同一目标发动协击普攻，每回合最多 1 次。
# =============================================================================

CASTOR_CHASE_STATUS = StatusDef(
    status_id="castor_chase", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"lifesteal_bps": 1000},
    response_priority=45, on_ally_basic_attack=_twin_on_ally_basic,
    payload={"rate_bps": 3500, "max_per_round": 1},
)


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
register(OdysseusFeint(skill_id="odysseus_feint", trigger_rate_bps=4000))
register(PerseusRelics(skill_id="perseus_relics", trigger_rate_bps=6000,
                       hint_intensity="strong"))
register(PerseusMirror(skill_id="perseus_mirror", timing=TIMING_PREPARE))
register(PerseusFlash(skill_id="perseus_flash", trigger_rate_bps=5500))
register(AtalantaSwift(skill_id="atalanta_swift", timing=TIMING_PREPARE))
register(AtalantaDash(skill_id="atalanta_dash", timing=TIMING_PREPARE))
register(SelfStatusPassive(skill_id="paris_fatal_arrow", timing=TIMING_PREPARE,
                           status_def=FATAL_ARROW_STATUS))
register(SelfStatusPassive(skill_id="paris_heelseek", timing=TIMING_PREPARE,
                           status_def=HEELSEEK_STATUS))
register(SelfStatusPassive(skill_id="ajax_shield", timing=TIMING_PREPARE,
                           status_def=AJAX_SHIELD_STATUS))
register(AjaxBulwark(skill_id="ajax_bulwark", trigger_rate_bps=6000))
register(HectorWarcry(skill_id="hector_warcry", trigger_rate_bps=4500,
                      prepare_rounds=1, hint_intensity="strong"))
register(HectorAssault(skill_id="hector_assault", trigger_rate_bps=5000))
register(JasonExpedition(skill_id="jason_expedition", timing=TIMING_PREPARE))
register(JasonCommand(skill_id="jason_command", trigger_rate_bps=7000))
register(SelfStatusPassive(skill_id="castor_twin", timing=TIMING_PREPARE,
                           status_def=CASTOR_TWIN_STATUS))
register(SelfStatusPassive(skill_id="castor_chase", timing=TIMING_PREPARE,
                           status_def=CASTOR_CHASE_STATUS))
