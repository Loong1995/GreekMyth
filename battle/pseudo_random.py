from __future__ import annotations

"""战法触发伪随机补偿（保底机制，决策 D-09：保留且修正为真累计）。

- key 为结构化元组（禁止拼接字符串，v0_analysis D2），默认 (caster_id, skill_id)，
  同一施放者同一战法的失败/成功在**一局内**真累计（战时记账随局清空）。
- current = clamp(base + fail×bonus - streak×penalty, min, max)；
  fail_count ≥ guarantee_count 时保底成功；base ≥ 10000 时必中且不消耗 RNG。
"""

from dataclasses import dataclass, field

from battle.rng import BPS, DeterministicRNG

Key = tuple


@dataclass(frozen=True, slots=True)
class PseudoRandomParams:
    bonus_per_fail_bps: int = 0
    penalty_per_success_bps: int = 0
    guarantee_fail_count: int = 0  # 0=无保底
    min_rate_bps: int = 0
    max_rate_bps: int = BPS


PLAIN = PseudoRandomParams()


@dataclass(slots=True)
class PseudoRandomBook:
    """一局内的伪随机记账簿（局边界整体丢弃重建）。"""

    _states: dict[Key, list[int]] = field(default_factory=dict)  # key -> [fail, streak]

    def roll(
        self,
        rng: DeterministicRNG,
        key: Key,
        base_rate_bps: int,
        params: PseudoRandomParams = PLAIN,
        *,
        source: str = "pseudo_random",
        reason: str = "",
        debug_sink: list | None = None,
    ) -> bool:
        """debug_sink 非 None 时追加一条判定明细（base/current/roll/allowed/guaranteed），
        仅调试侧信道用，不影响 RNG 消费与记账。"""
        if base_rate_bps >= BPS:
            if debug_sink is not None:
                debug_sink.append({"base": base_rate_bps, "current": base_rate_bps,
                                   "roll": None, "allowed": True, "guaranteed": False})
            return True  # 必中不 roll、不消耗 RNG、不记账
        state = self._states.setdefault(key, [0, 0])
        fail_count, success_streak = state

        if params.guarantee_fail_count > 0 and fail_count >= params.guarantee_fail_count:
            allowed = True
            if debug_sink is not None:
                debug_sink.append({"base": base_rate_bps, "current": None,
                                   "roll": None, "allowed": True, "guaranteed": True})
        else:
            current = base_rate_bps
            current += fail_count * params.bonus_per_fail_bps
            current -= success_streak * params.penalty_per_success_bps
            current = max(params.min_rate_bps, min(current, params.max_rate_bps))
            roll = rng.rand_bps(source, reason)
            allowed = roll < current
            if debug_sink is not None:
                debug_sink.append({"base": base_rate_bps, "current": current,
                                   "roll": roll, "allowed": allowed, "guaranteed": False})

        if allowed:
            state[0] = 0
            state[1] = success_streak + 1
        else:
            state[0] = fail_count + 1
            state[1] = 0
        return allowed
