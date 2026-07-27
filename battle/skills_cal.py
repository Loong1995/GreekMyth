"""数值标定用战法池（cal_ 前缀）。

减伤三档：准备回合给全队挂常驻 damage_reduce（10% / 25% / 40%）。
伤害三档 × 三时机：主动 / 追击 / 被动，每回合数学期望伤害系数
（概率折算后）= 100 / 150 / 250（即 rate_bps=10000/15000/25000、触发率 100%）。

被动 = 准备回合挂自带状态，每正常回合开始对随机敌造成一次期望系数伤害。
仅供标定脚本 / 调参，不入正式武将池。
"""
from __future__ import annotations

from dataclasses import dataclass

from battle.skill_common import emit_status_trigger
from battle.skills import TIMING_ACTIVE, TIMING_PREPARE, TIMING_PURSUIT, Skill, register
from battle.statuses import BUFF, PERMANENT, SPECIAL, StatusDef

# 期望伤害系数（万分比）：低/中/高 = 100% / 150% / 250%
CAL_RATE_LOW_BPS = 10000
CAL_RATE_MID_BPS = 15000
CAL_RATE_HIGH_BPS = 25000

# 全队常驻减伤（万分比）
CAL_DR_LOW_BPS = 1000   # 10%
CAL_DR_MID_BPS = 2500   # 25%
CAL_DR_HIGH_BPS = 4000  # 40%


def _make_dr_status(status_id: str, reduce_bps: int) -> StatusDef:
    return StatusDef(
        status_id=status_id,
        kind=BUFF,
        duration_rounds=PERMANENT,
        refreshable=True,
        max_stacks=1,
        modifiers={"damage_reduce_bps": reduce_bps},
    )


CAL_DR_LOW_STATUS = _make_dr_status("cal_dr_low", CAL_DR_LOW_BPS)
CAL_DR_MID_STATUS = _make_dr_status("cal_dr_mid", CAL_DR_MID_BPS)
CAL_DR_HIGH_STATUS = _make_dr_status("cal_dr_high", CAL_DR_HIGH_BPS)


@dataclass(frozen=True, slots=True)
class CalTeamDR(Skill):
    """标定·全队常驻减伤（准备回合施加，refreshable 不叠）。"""

    status_def: StatusDef = CAL_DR_MID_STATUS

    def select_targets(self, engine, actor):
        return list(engine.alive_allies(actor))

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            engine.apply_status(actor, target, self.status_def, parent_seq=trigger_seq)


@dataclass(frozen=True, slots=True)
class CalActiveStrike(Skill):
    """标定·主动：行动窗 100% 触发，对随机敌造成期望系数兵刃伤害。"""

    rate_bps: int = CAL_RATE_MID_BPS

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical",
                    rate_bps=self.rate_bps, parent_seq=trigger_seq,
                )


@dataclass(frozen=True, slots=True)
class CalPursuitStrike(Skill):
    """标定·追击：普攻命中后 100% 追加期望系数兵刃伤害。"""

    rate_bps: int = CAL_RATE_MID_BPS

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical",
                    rate_bps=self.rate_bps, parent_seq=trigger_seq,
                    kind="pursuit",
                )


def _make_passive_on_round(rate_bps: int):
    def _on_round_start(engine, status, parent_seq, round_no):
        owner = engine.hero_by_id(status.owner_id)
        if not owner.is_alive():
            return
        target = engine.select_enemy_by_hit_rate(
            owner, reason=f"status:{status.status_id}"
        )
        if target is None or not target.is_alive():
            return
        tick_seq = emit_status_trigger(engine, status, parent_seq)
        engine.deal_damage(
            owner, target, damage_type="physical",
            rate_bps=rate_bps, parent_seq=tick_seq, kind="passive",
        )

    return _on_round_start


def _make_passive_status(status_id: str, rate_bps: int) -> StatusDef:
    return StatusDef(
        status_id=status_id,
        kind=SPECIAL,
        duration_rounds=PERMANENT,
        refreshable=True,
        max_stacks=1,
        on_round_start=_make_passive_on_round(rate_bps),
    )


CAL_PASSIVE_LOW_STATUS = _make_passive_status("cal_passive_low", CAL_RATE_LOW_BPS)
CAL_PASSIVE_MID_STATUS = _make_passive_status("cal_passive_mid", CAL_RATE_MID_BPS)
CAL_PASSIVE_HIGH_STATUS = _make_passive_status("cal_passive_high", CAL_RATE_HIGH_BPS)

# status_id → skill_id（批量统计归因用）
CAL_PASSIVE_STATUS_TO_SKILL = {
    "cal_passive_low": "cal_passive_low",
    "cal_passive_mid": "cal_passive_mid",
    "cal_passive_high": "cal_passive_high",
}


@dataclass(frozen=True, slots=True)
class CalPassiveStrike(Skill):
    """标定·被动：准备回合挂自带状态，每正常回合开始打一发期望系数兵刃。"""

    status_def: StatusDef = CAL_PASSIVE_MID_STATUS

    def select_targets(self, engine, actor):
        return [actor]

    def execute(self, engine, actor, targets, trigger_seq):
        engine.apply_status(actor, actor, self.status_def, parent_seq=trigger_seq)


# ---- 注册 ----

register(CalTeamDR(skill_id="cal_dr_low", timing=TIMING_PREPARE,
                   status_def=CAL_DR_LOW_STATUS))
register(CalTeamDR(skill_id="cal_dr_mid", timing=TIMING_PREPARE,
                   status_def=CAL_DR_MID_STATUS))
register(CalTeamDR(skill_id="cal_dr_high", timing=TIMING_PREPARE,
                   status_def=CAL_DR_HIGH_STATUS))

register(CalActiveStrike(skill_id="cal_active_low", timing=TIMING_ACTIVE,
                         trigger_rate_bps=10000, rate_bps=CAL_RATE_LOW_BPS,
                         damage_type="physical"))
register(CalActiveStrike(skill_id="cal_active_mid", timing=TIMING_ACTIVE,
                         trigger_rate_bps=10000, rate_bps=CAL_RATE_MID_BPS,
                         damage_type="physical"))
register(CalActiveStrike(skill_id="cal_active_high", timing=TIMING_ACTIVE,
                         trigger_rate_bps=10000, rate_bps=CAL_RATE_HIGH_BPS,
                         damage_type="physical"))

register(CalPursuitStrike(skill_id="cal_pursuit_low", timing=TIMING_PURSUIT,
                          trigger_rate_bps=10000, rate_bps=CAL_RATE_LOW_BPS,
                          damage_type="physical"))
register(CalPursuitStrike(skill_id="cal_pursuit_mid", timing=TIMING_PURSUIT,
                          trigger_rate_bps=10000, rate_bps=CAL_RATE_MID_BPS,
                          damage_type="physical"))
register(CalPursuitStrike(skill_id="cal_pursuit_high", timing=TIMING_PURSUIT,
                          trigger_rate_bps=10000, rate_bps=CAL_RATE_HIGH_BPS,
                          damage_type="physical"))

register(CalPassiveStrike(skill_id="cal_passive_low", timing=TIMING_PREPARE,
                          status_def=CAL_PASSIVE_LOW_STATUS,
                          damage_type="physical"))
register(CalPassiveStrike(skill_id="cal_passive_mid", timing=TIMING_PREPARE,
                          status_def=CAL_PASSIVE_MID_STATUS,
                          damage_type="physical"))
register(CalPassiveStrike(skill_id="cal_passive_high", timing=TIMING_PREPARE,
                          status_def=CAL_PASSIVE_HIGH_STATUS,
                          damage_type="physical"))
