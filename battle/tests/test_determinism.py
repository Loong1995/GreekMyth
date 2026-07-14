from __future__ import annotations

"""确定性测试（任务书 B1 验收：同种子 100 次逐字节一致）。

直接运行：python battle/tests/test_determinism.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.tests.helpers import duel_1v1_setup, full_3v3_setup, standard_3v3_setup


def test_same_seed_100_runs_byte_identical():
    setup = full_3v3_setup()
    baseline = serialize_report(simulate(setup, seed=20260705))
    for _ in range(99):
        assert serialize_report(simulate(setup, seed=20260705)) == baseline


def test_standard_lineup_100_runs_byte_identical():
    """B3 全机制阵容（单挑/神谕/被动/追击/准备型/响应钩子）同样逐字节确定。"""
    setup = standard_3v3_setup()
    baseline = serialize_report(simulate(setup, seed=20260705))
    for _ in range(99):
        assert serialize_report(simulate(setup, seed=20260705)) == baseline


def test_different_seeds_diverge():
    setup = full_3v3_setup()
    reports = {serialize_report(simulate(setup, seed=seed)) for seed in range(1, 21)}
    assert len(reports) > 1, "20 个种子产出完全相同战报，随机源疑似失效"


def test_seed_zero_is_valid():
    setup = duel_1v1_setup()
    a = serialize_report(simulate(setup, seed=0))
    b = serialize_report(simulate(setup, seed=0))
    assert a == b


def test_audit_flag_does_not_change_report():
    setup = full_3v3_setup()
    assert serialize_report(simulate(setup, seed=7)) == serialize_report(
        simulate(setup, seed=7, audit=True)
    )


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
