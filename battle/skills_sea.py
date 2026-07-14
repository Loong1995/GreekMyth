from __future__ import annotations

"""海阵营战法（v3.1 池，机制标签：震荡与节奏控制）。数值以 phase3 任务书 §四/§六 为准。

自带：poseidon_oracle 海神三叉戟 / amphitrite_tide 潮汐抚愈 / triton_horn 海嗣号角 /
      siren_song 魅音 / scylla_maw 六首撕咬 / charybdis_maw 漩涡巨口
拆解：poseidon_torrent 怒涛 / amphitrite_grace 海后之泽 / triton_surge 浪涌 /
      siren_charm 魅惑术 / scylla_bite 撕咬 / charybdis_swallow 吞流
"""

from dataclasses import dataclass

from battle.skill_common import BPS, emit_status_trigger, lowest_ratio_allies, pick_distinct_enemies
from battle.skills import TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.skills_men import LION_COUNTER_STATUS, LionCounter, SelfStatusPassive
from battle.statuses import (
    BUFF,
    DEBUFF,
    PERMANENT,
    SPECIAL,
    StatusDef,
    charm,
    hesitation,
)

# =============================================================================
# 海神三叉戟（波塞冬）：己方全体【海神】——造成非震荡实际伤害后逐次 70% 判定震荡
# （普通随机，首次判失即停）：对未被本链震荡过的敌方（首个必须异于受击目标）造成
# 原伤害 50% 的固定震荡伤害（特殊伤害：播放但不触发响应），单次最多 3 次；
# 另我方全体闪避 +20%。
# =============================================================================

TRIDENT_RATE_BPS = 7000
TRIDENT_DAMAGE_BPS = 5000
TRIDENT_MAX_SHOCKS = 3


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
    modifiers={"evade_bps": 2000},
    response_priority=40, on_damage_dealt=_poseidon_on_damage_dealt,
)


@dataclass(frozen=True, slots=True)
class PoseidonOracle(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, POSEIDON_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 怒涛（波塞冬拆解）：追击 45%——对普攻目标施加【洪水】（受伤 +10%、统率 -10，
# 2 回合），并追加 2 次 140% 兵刃。
# =============================================================================

FLOOD_STATUS = StatusDef(
    status_id="flood", kind=DEBUFF, duration_rounds=2,
    modifiers={"vulnerable_bps": 1000, "command_delta": -10},
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
# 潮汐抚愈（安菲特里忒）：主动 45%——治疗己方兵力比例最低 2 人（智力 ×1.8，可暴击）。
# =============================================================================

@dataclass(frozen=True, slots=True)
class AmphitriteTide(Skill):
    def select_targets(self, engine, actor):
        return lowest_ratio_allies(engine, actor, 2)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                base = engine.effective_attr(actor, "intelligence") * 18000 // BPS
                engine.heal(actor, target, fixed_base=base, parent_seq=trigger_seq)


# =============================================================================
# 海后之泽（安菲特里忒拆解）：被动——己方全体治疗量 +10%（整局）。
# =============================================================================

GRACE_STATUS = StatusDef(
    status_id="amphitrite_grace", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"heal_up_bps": 1000},
)


@dataclass(frozen=True, slots=True)
class AmphitriteGrace(Skill):
    def select_targets(self, engine, actor):
        return engine.alive_allies(actor)

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, GRACE_STATUS, parent_seq=trigger_seq)


# =============================================================================
# 海嗣号角（特里同）：主动 80%——己方全体获得 1 次格挡 + 统率 +15（不限层数叠加）；
# 每次释放后自身该战法释放概率 -10%（动态触发率）。
# =============================================================================

HORN_COMMAND_STATUS = StatusDef(
    status_id="triton_horn_command", kind=BUFF, duration_rounds=PERMANENT,
    max_stacks=99, refreshable=True, modifiers={"command_delta": 15},
)


@dataclass(frozen=True, slots=True)
class TritonHorn(Skill):
    def trigger_rate_for(self, engine, actor):
        casts = engine.skill_cast_count(actor, self.skill_id)
        return max(0, self.trigger_rate_bps - casts * 1000)

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

def _surge_on_round_start(engine, status, parent_seq, round_no):
    if round_no > 3:
        return
    roll = engine.rng.rand_bps("status_trigger", f"surge:{status.owner_id}")
    if roll >= 7000:
        return
    owner = engine.hero_by_id(status.owner_id)
    engine.grant_block(owner, 1, source=engine.heroes.get(status.source_id, owner),
                       parent_seq=parent_seq)


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
# 魅音（塞壬）：主动 45%——对敌方武力最高单体 350% 魔法 + 【犹豫】。
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
                engine.apply_status(actor, target, hesitation(), parent_seq=trigger_seq)


# =============================================================================
# 魅惑术（塞壬拆解）：主动 55%——对敌 2 人各 180% 魔法并施加【魅惑】
# （普攻和主动战法选目标敌我不分，1 回合）。
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
                actor, target, damage_type="magic", rate_bps=18000,
                parent_seq=trigger_seq,
            )
            if target.is_alive():
                engine.apply_status(actor, target, charm(), parent_seq=trigger_seq)


# =============================================================================
# 六首撕咬（斯库拉）：追击 100%——普攻后对随机一名**其他**敌军 180% 兵刃。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ScyllaMaw(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        exclude = tuple(t.hero_id for t in targets)
        other = engine.select_enemy_by_hit_rate(
            actor, reason=f"maw:{actor.hero_id}", exclude_ids=exclude
        )
        if other is None:
            return
        engine.deal_damage(
            actor, other, damage_type="physical", rate_bps=18000,
            parent_seq=trigger_seq, kind="maw",
        )


# =============================================================================
# 撕咬（斯库拉拆解）：追击 35%——普攻后对普攻目标 320% 兵刃。
# =============================================================================

@dataclass(frozen=True, slots=True)
class ScyllaBite(Skill):
    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=32000,
                    parent_seq=trigger_seq, kind="maw",
                )


# =============================================================================
# 漩涡巨口（卡律布狄斯）：被动整局——自身受伤 -20%；受击后 30% 对来源反打
# 80% 兵刃（复用狮皮反击口径）。
# 吞流（拆解）：被动——自身统率 +20（整局）。
# =============================================================================

from battle.skills_men import _lion_on_damage_taken  # noqa: E402  复用受击反打钩子

CHARYBDIS_MAW_STATUS = StatusDef(
    status_id="charybdis_maw", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"damage_reduce_bps": 2000},
    response_priority=50, on_damage_taken=_lion_on_damage_taken,
    payload={"rate_bps": 3000, "damage_rate_bps": 8000, "weaken": False},
)

SWALLOW_STATUS = StatusDef(
    status_id="charybdis_swallow", kind=SPECIAL, duration_rounds=PERMANENT,
    modifiers={"command_delta": 20},
)


# =============================================================================
# 注册
# =============================================================================

register(PoseidonOracle(skill_id="poseidon_oracle", timing=TIMING_PREPARE,
                        is_oracle=True, hint_intensity="strong"))
register(PoseidonTorrent(skill_id="poseidon_torrent", trigger_rate_bps=4500,
                         timing=TIMING_PURSUIT))
register(AmphitriteTide(skill_id="amphitrite_tide", trigger_rate_bps=4500))
register(AmphitriteGrace(skill_id="amphitrite_grace", timing=TIMING_PREPARE))
register(TritonHorn(skill_id="triton_horn", trigger_rate_bps=8000))
register(TritonSurge(skill_id="triton_surge", timing=TIMING_PREPARE))
register(SirenSong(skill_id="siren_song", trigger_rate_bps=4500,
                   hint_intensity="strong"))
register(SirenCharm(skill_id="siren_charm", trigger_rate_bps=5500))
register(ScyllaMaw(skill_id="scylla_maw", trigger_rate_bps=10000,
                   timing=TIMING_PURSUIT))
register(ScyllaBite(skill_id="scylla_bite", trigger_rate_bps=3500,
                    timing=TIMING_PURSUIT))
register(LionCounter(skill_id="charybdis_maw", timing=TIMING_PREPARE,
                     status_def=CHARYBDIS_MAW_STATUS))
register(SelfStatusPassive(skill_id="charybdis_swallow", timing=TIMING_PREPARE,
                           status_def=SWALLOW_STATUS))
