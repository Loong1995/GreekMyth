from __future__ import annotations

"""海域阵营战法（Phase 4 v4 池，机制标签：震荡与节奏控制）。

自带：poseidon_oracle 海神三叉戟 / amphitrite_tide 潮汐抚愈 / triton_horn 海嗣号角 /
      siren_song 魅音 / scylla_maw 六首撕咬
拆解：poseidon_torrent 怒涛 / amphitrite_grace 海后之泽 / triton_surge 浪涌 /
      siren_charm 迷魂之歌 / scylla_bite 撕咬
卡律布狄斯（charybdis_maw/charybdis_swallow）v4 下架（manual_tasks 拍板项 2）。
奥德修斯 A4 阵营重划（men→sea）时随批迁入。
"""

from dataclasses import dataclass

from battle.skill_common import BPS, emit_status_trigger, lowest_ratio_allies, pick_distinct_enemies
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.statuses import (
    BUFF,
    DEBUFF,
    PERMANENT,
    SPECIAL,
    StatusDef,
    charm,
)

# =============================================================================
# 海神三叉戟（波塞冬，v4）：神谕必发——己方全体【海神】（整场）：造成非震荡
# 实际伤害后逐次 70% 判定震荡（普通随机，首次判失即停）：对原目标的一名
# 未被本次震荡命中过的存活友军（首个必异于受击目标）造成原伤害 50% 的固定
# 震荡伤害（继承物/魔类型；特殊伤害：不暴击、不吃乘区、不吸血、不再触发海神），
# 单次伤害最多 2 次震荡。
# =============================================================================

TRIDENT_RATE_BPS = 7000
TRIDENT_DAMAGE_BPS = 5000
TRIDENT_MAX_SHOCKS = 2  # v4：3 → 2


def _poseidon_on_damage_dealt(engine, status, ctx):
    if ctx["kind"] == "trident" or ctx["amount"] <= 0:
        return
    owner = engine.hero_by_id(status.owner_id)
    shock_amount = ctx["amount"] * TRIDENT_DAMAGE_BPS // BPS
    if shock_amount <= 0:
        return
    original_target_id = ctx["target"].hero_id
    shocked: list[str] = []
    for shock_no in range(TRIDENT_MAX_SHOCKS):
        if engine.game_over() or not owner.is_alive():
            return
        exclude = (original_target_id,) if shock_no == 0 else tuple(shocked)
        target = engine.select_enemy_by_hit_rate(
            owner, reason=f"trident:{status.owner_id}:{shock_no}", exclude_ids=exclude
        )
        if target is None:
            return  # 无合法目标，不 roll
        roll = engine.rng.rand_bps("status_trigger", f"trident:{status.owner_id}")
        if roll >= TRIDENT_RATE_BPS:
            return  # 首次判失即停
        tick_seq = emit_status_trigger(engine, status, ctx["damage_seq"])
        engine.deal_damage(
            owner, target, damage_type=ctx["damage_type"], fixed_amount=shock_amount,
            parent_seq=tick_seq, kind="trident", can_crit=False,
            is_special=True, can_mitigate=False,
        )
        shocked.append(target.hero_id)


POSEIDON_STATUS = StatusDef(
    status_id="poseidon_tide", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=40, on_damage_dealt=_poseidon_on_damage_dealt,
)  # v4：移除旧版全队闪避 +20%


@dataclass(frozen=True, slots=True)
class PoseidonOracle(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, POSEIDON_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 怒涛（波塞冬拆解）：追击 45%——对普攻目标施加【洪水】（受伤 +10%、统率 -15，
# 2 回合，v4 调参），并追加 2 次 140% 兵刃。
# =============================================================================

FLOOD_STATUS = StatusDef(
    status_id="flood", kind=DEBUFF, duration_rounds=2,
    modifiers={"vulnerable_bps": 1000, "command_delta": -15},
)


@dataclass(frozen=True, slots=True)
class PoseidonTorrent(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            engine.apply_status(actor, target, FLOOD_STATUS, parent_seq=trigger_seq)
            for _ in range(2):
                if not target.is_alive() or engine.game_over():
                    break
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=14000,
                    parent_seq=trigger_seq, kind="pursuit",
                )


# =============================================================================
# 潮汐抚愈（安菲特里忒，v4 改被动）：准备阶段发动，自身挂载体（整场）——
# ①每回合开始：己方全体受到治疗效果 +10%（1 回合状态，逐回合重挂）；
# ②每回合结束：治疗己方兵力比例最低 2 人（安菲特里忒智力 ×1.8，可暴击）。
# =============================================================================

TIDE_RECEIVE_STATUS = StatusDef(
    status_id="amphitrite_tide_receive", kind=BUFF, duration_rounds=1,
    refreshable=True, modifiers={"heal_received_up_bps": 1000},
)


def _tide_on_round_start(engine, status, parent_seq, round_no):
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    for ally in engine.alive_allies(owner):
        engine.apply_status(owner, ally, TIDE_RECEIVE_STATUS, parent_seq=parent_seq)


def _tide_on_round_end(engine, status, parent_seq, round_no):
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    tick_seq = emit_status_trigger(engine, status, parent_seq)
    base = engine.effective_attr(owner, "intelligence") * 18000 // BPS
    for target in lowest_ratio_allies(engine, owner, 2):
        if target.is_alive():
            engine.heal(owner, target, fixed_base=base, parent_seq=tick_seq)


AMPHITRITE_TIDE_STATUS = StatusDef(
    status_id="amphitrite_tide", kind=SPECIAL, duration_rounds=PERMANENT,
    response_priority=25,
    on_round_start=_tide_on_round_start, on_round_end=_tide_on_round_end,
)


@dataclass(frozen=True, slots=True)
class AmphitriteTide(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, AMPHITRITE_TIDE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 海后之泽（安菲特里忒拆解，v4 改版）：被动——前三回合结束时治疗己方全体
# （施放者智力 ×1.8，可暴击）。
# =============================================================================

def _grace_on_round_end(engine, status, parent_seq, round_no):
    if round_no > 3:
        return
    owner = engine.hero_by_id(status.owner_id)
    if not owner.is_alive():
        return
    tick_seq = emit_status_trigger(engine, status, parent_seq)
    base = engine.effective_attr(owner, "intelligence") * 18000 // BPS
    for target in engine.alive_allies(owner):
        if target.is_alive():
            engine.heal(owner, target, fixed_base=base, parent_seq=tick_seq)


GRACE_STATUS = StatusDef(
    status_id="amphitrite_grace", kind=SPECIAL, duration_rounds=3,
    response_priority=26, on_round_end=_grace_on_round_end,
)


@dataclass(frozen=True, slots=True)
class AmphitriteGrace(Skill):
    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, GRACE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 海嗣号角（特里同，v4）：主动初始 100%——己方全体获得 1 层格挡 + 统率 +25
# （整场，不限层数叠加）；每成功释放一次，之后发动率 -10%，最低降至 20%。
# 性格·忠勇：波塞冬存活时本自带战法连发率 +30%（traits._Zhongyong）。
# =============================================================================

HORN_COMMAND_STATUS = StatusDef(
    status_id="triton_horn_command", kind=BUFF, duration_rounds=PERMANENT,
    max_stacks=99, refreshable=True, modifiers={"command_delta": 25},
)
HORN_MIN_RATE_BPS = 2000


@dataclass(frozen=True, slots=True)
class TritonHorn(Skill):
    def trigger_rate_for(self, engine, actor):
        casts = engine.skill_cast_count(actor, self.skill_id)
        return max(HORN_MIN_RATE_BPS, self.trigger_rate_bps - casts * 1000)

    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        engine.note_skill_cast(actor, self.skill_id)
        for target in targets:
            engine.grant_block(target, 1, source=actor, parent_seq=trigger_seq)
            engine.apply_status(actor, target, HORN_COMMAND_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 浪涌（特里同拆解）：被动——我军全体获得【浪涌】（3 回合）：前三回合每回合开始
# 70% 获得 1 次格挡。
# =============================================================================

SURGE_FLOOD_CMD = StatusDef(
    status_id="triton_surge_flood", kind=DEBUFF, duration_rounds=1,
    refreshable=True, modifiers={"command_delta": -20},
)


def _surge_on_round_start(engine, status, parent_seq, round_no):
    if round_no > 3:
        return
    owner = engine.hero_by_id(status.owner_id)
    source = engine.heroes.get(status.source_id, owner)
    # 洪水联动：仅由本队浪涌持有者中站位序最小者结算一次——带 flood 的敌军统率 -20
    allies_surge = [
        h for h in engine.alive_allies(owner)
        if engine.find_status(h.hero_id, "triton_surge") is not None
    ]
    if allies_surge and owner.hero_id == min(
            allies_surge, key=lambda h: engine.hero_order.index(h.hero_id)).hero_id:
        for enemy in engine.alive_enemies(owner):
            if engine.find_status(enemy.hero_id, "flood") is not None:
                engine.apply_status(source, enemy, SURGE_FLOOD_CMD, parent_seq=parent_seq)
    roll = engine.rng.rand_bps("status_trigger", f"surge:{status.owner_id}")
    if roll >= 7000:
        return
    engine.grant_block(owner, 1, source=source, parent_seq=parent_seq)


SURGE_STATUS = StatusDef(
    status_id="triton_surge", kind=SPECIAL, duration_rounds=3,
    on_round_start=_surge_on_round_start,
)


@dataclass(frozen=True, slots=True)
class TritonSurge(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, SURGE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 魅音（塞壬，v4）：主动 55%——对敌方武力最高单体 350% 魔法 + 【魅惑】1 回合
# （普攻/主动选目标敌我不分；旧版为犹豫，v4 改魅惑）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class SirenSong(Skill):
    def select_targets(self, engine, actor):
        enemies = engine.alive_enemies(actor)
        if not enemies:
            return []
        best = max(
            enemies,
            key=lambda h: (engine.effective_attr(h, "force"),
                           -engine.hero_order.index(h.hero_id)),
        )
        return [best]

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if not target.is_alive():
                continue
            engine.deal_damage(
                actor, target, damage_type="magic", rate_bps=35000,
                parent_seq=trigger_seq,
            )
            if target.is_alive():
                engine.apply_status(actor, target, charm(), parent_seq=trigger_seq)


# =============================================================================
# 迷魂之歌（塞壬拆解，v4 改名，原魅惑术）：主动 55%——对敌 2 人（受击率、互斥）
# 各 180% 魔法并施加【魅惑】（1 回合）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class SirenCharm(Skill):
    def select_targets(self, engine, actor):
        return pick_distinct_enemies(engine, actor, 2, f"skill:{self.skill_id}")

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
                engine.apply_status(actor, target, charm(), parent_seq=trigger_seq)


# =============================================================================
# 六首撕咬（斯库拉，v4）：追击 100%——普攻后对随机一名**其他**敌军 180% 兵刃；
# 敌方仅剩 1 名存活单位时改为对原目标 90% 兵刃。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ScyllaMaw(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        exclude = tuple(t.hero_id for t in targets)
        other = engine.select_enemy_by_hit_rate(
            actor, reason=f"maw:{actor.hero_id}", exclude_ids=exclude
        )
        if other is not None:
            engine.deal_damage(
                actor, other, damage_type="physical", rate_bps=18000,
                parent_seq=trigger_seq, kind="maw",
            )
            return
        # 无其他敌军 → 对原目标同系数 180% 兵刃
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=18000,
                    parent_seq=trigger_seq, kind="maw",
                )


# =============================================================================
# 撕咬（斯库拉拆解，v4）：追击 35%——自身速度 +20（2 回合），并对普攻目标
# 320% 兵刃。
# =============================================================================

BITE_SPEED_STATUS = StatusDef(
    status_id="scylla_bite_speed", kind=BUFF, duration_rounds=2,
    refreshable=True, modifiers={"speed_delta": 20},
)


@dataclass(frozen=True, slots=True)
class ScyllaBite(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, BITE_SPEED_STATUS, parent_seq=trigger_seq)
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=38000,
                    parent_seq=trigger_seq, kind="maw",
                )


# 卡律布狄斯（漩涡巨口/吞流）已随 v4 武将池下架（manual_tasks 拍板项 2）


# =============================================================================
# 注册
# =============================================================================

register(PoseidonOracle(skill_id="poseidon_oracle", timing=TIMING_PREPARE,
                        is_oracle=True, hint_intensity="strong"))
register(PoseidonTorrent(skill_id="poseidon_torrent", trigger_rate_bps=4500,
                         timing=TIMING_PURSUIT))
register(AmphitriteTide(skill_id="amphitrite_tide", timing=TIMING_PREPARE))
register(AmphitriteGrace(skill_id="amphitrite_grace", timing=TIMING_PREPARE))
register(TritonHorn(skill_id="triton_horn", trigger_rate_bps=10000))
register(TritonSurge(skill_id="triton_surge", timing=TIMING_PREPARE))
register(SirenSong(skill_id="siren_song", trigger_rate_bps=5500,
                   hint_intensity="strong"))
register(SirenCharm(skill_id="siren_charm", trigger_rate_bps=3500))
register(ScyllaMaw(skill_id="scylla_maw", trigger_rate_bps=10000,
                   timing=TIMING_PURSUIT))
register(ScyllaBite(skill_id="scylla_bite", trigger_rate_bps=3500,
                    timing=TIMING_PURSUIT))
