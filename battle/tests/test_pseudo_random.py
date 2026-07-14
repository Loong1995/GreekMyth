"""伪随机补偿单测（决策 D-09：战法触发保底，真累计）。

直接运行：python battle/tests/test_pseudo_random.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.pseudo_random import PseudoRandomBook, PseudoRandomParams
from battle.rng import DeterministicRNG


def test_guarantee_after_fail_count():
    """连败 N 次后下一次必中：任意窗口内至少 1/(N+1) 触发。"""
    params = PseudoRandomParams(guarantee_fail_count=3)
    book = PseudoRandomBook()
    rng = DeterministicRNG(seed=42)
    key = ("hero", "skill")
    consecutive_fails = 0
    for _ in range(500):
        if book.roll(rng, key, 100, params):  # 基础 1%，几乎全靠保底
            consecutive_fails = 0
        else:
            consecutive_fails += 1
        assert consecutive_fails <= 3, "保底失效：连续失败超过 guarantee_fail_count"


def test_bonus_accumulates_and_resets_on_success():
    """失败递增补偿：fail 3 次后当前概率 = base + 3×bonus，可越过 roll 阈值。"""
    params = PseudoRandomParams(bonus_per_fail_bps=3000)

    class FixedRNG:
        def rand_bps(self, source, reason):
            return 8000  # 恒定 roll

    book = PseudoRandomBook()
    rng = FixedRNG()
    key = ("h", "s")
    results = [book.roll(rng, key, 2000, params) for _ in range(6)]
    # 概率轨迹：2000 F → 5000 F → 8000 F → 11000 T → 2000 F（成功清零 fail）...
    assert results[:5] == [False, False, False, True, False]


def test_penalty_reduces_after_success_streak():
    params = PseudoRandomParams(penalty_per_success_bps=4000, min_rate_bps=1000)

    class FixedRNG:
        def rand_bps(self, source, reason):
            return 4500

    book = PseudoRandomBook()
    key = ("h", "s")
    results = [book.roll(FixedRNG(), key, 9000, params) for _ in range(3)]
    # 9000 T（streak→1）→ 9000-4000=5000 T（streak→2）→ 9000-8000=1000(min) F
    assert results == [True, True, False]


def test_certain_rate_consumes_no_rng():
    book = PseudoRandomBook()
    rng = DeterministicRNG(seed=1)
    assert book.roll(rng, ("h", "s"), 10000) is True
    assert book.roll(rng, ("h", "s"), 15000) is True
    assert rng.index == 0  # 必中不消耗随机数（RNG 消费点登记规则）


def test_keys_are_isolated():
    params = PseudoRandomParams(guarantee_fail_count=2)

    class NeverRNG:
        def rand_bps(self, source, reason):
            return 9999

    book = PseudoRandomBook()
    rng = NeverRNG()
    assert book.roll(rng, ("a", "x"), 0, params) is False
    assert book.roll(rng, ("a", "x"), 0, params) is False
    # ("a","x") 已累计 2 败，("b","x") 全新记账不受影响
    assert book.roll(rng, ("b", "x"), 0, params) is False
    assert book.roll(rng, ("a", "x"), 0, params) is True  # 保底


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
