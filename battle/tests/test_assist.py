"""连携测试（Phase 3 改版）：主将神谕后副将按**自带战法自身释放率**立即释放；
kind=assist；普通随机、不影响伪随机记账；不占用本回合正常释放机会；
仅主动自带战法参与；准备型无需准备直接释放；必发战法（≥100%）不 roll。

直接运行：python battle/tests/test_assist.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero


def assist_setup(deputy1_skills, deputy2_skills, main_is_oracle=True) -> BattleSetup:
    main_skills = ("delphi_revelation",) if main_is_oracle else ("achilles_wrath",)
    return BattleSetup(
        battle_id="t_assist",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                make_hero("a1", 0, force=85, intelligence=95, command=150, speed=90,
                          skills=main_skills),
                make_hero("a2", 1, force=80, intelligence=100, command=150, speed=85,
                          skills=deputy1_skills),
                make_hero("a3", 2, force=80, intelligence=100, command=150, speed=80,
                          skills=deputy2_skills),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, force=85, command=150, speed=88),
                make_hero("b2", 1, force=85, command=150, speed=82),
            )),
        ),
    )


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def assist_events(events, actor=None):
    return [e for e in events if e["type"] == "skill_trigger"
            and e["payload"]["kind"] == "assist"
            and (actor is None or e["payload"]["actor_id"] == actor)]


def test_assist_fires_at_skill_own_rate():
    """副将自带 test_blast/test_mend 均为 50% → 连携率应 ≈50%（Phase 3 新规）。"""
    fired = total = 0
    for seed in range(150):
        report = simulate(assist_setup(("test_blast",), ("test_mend",)), seed=seed)
        events = [e for e in report["games"][0]["events"]]
        for actor in ("a2", "a3"):
            total += 1
            hits = assist_events(events, actor)
            if hits:
                fired += 1
                assert all(e["t"]["r"] == 0 for e in hits), "连携必须发生在准备回合"
                assert len(hits) == 1, "每副将每局至多连携一次"
    assert 0.42 <= fired / total <= 0.58, f"连携触发率 {fired/total:.2f} 偏离 50%"


def test_assist_guaranteed_for_full_rate_skill():
    """自带战法 100% 触发率（test_war_cry）→ 连携必发、不消耗 RNG。"""
    for seed in range(20):
        report = simulate(assist_setup(("test_war_cry",), ("test_mend",)), seed=seed)
        events = [e for e in report["games"][0]["events"]]
        assert assist_events(events, "a2"), f"seed={seed} 必发战法连携缺失"


def test_assist_parent_is_oracle_trigger_and_new_group():
    for seed in range(20):
        report = simulate(assist_setup(("test_blast",), ("test_mend",)), seed=seed)
        events = flat_events(report)
        by_seq = {e["seq"]: e for e in events}
        hits = assist_events(events)
        if not hits:
            continue
        for event in hits:
            assert event["group_id"] == event["seq"], "连携是独立播放组"
            parent = by_seq[event["parent_seq"]]
            assert parent["type"] == "skill_trigger"
            assert parent["payload"]["skill_id"] == "delphi_revelation"
        return
    raise AssertionError("20 个种子无连携")


def test_no_assist_without_oracle_main():
    """主将自带非神谕（被动）→ 不触发连携。"""
    for seed in range(30):
        report = simulate(
            assist_setup(("test_blast",), ("test_mend",), main_is_oracle=False), seed=seed
        )
        assert not assist_events(flat_events(report)), f"seed={seed} 非神谕主将出现连携"


def test_assist_only_for_active_innate():
    """副将自带为被动（prepare 时机）→ 不参与连携。"""
    for seed in range(30):
        report = simulate(assist_setup(("achilles_wrath",), ("medusa_gaze",)), seed=seed)
        assert not assist_events(flat_events(report)), f"seed={seed} 被动自带出现连携"


def test_assist_prepare_type_releases_immediately():
    """准备型主动被连携 → 无需准备直接释放（assist 事件带目标 + 伤害子事件）。"""
    for seed in range(40):
        report = simulate(
            assist_setup(("test_charged_nova",), ("test_mend",)), seed=seed
        )
        events = flat_events(report)
        hits = assist_events(events, "a2")
        if not hits:
            continue
        event = hits[0]
        damages = [e for e in events if e["parent_seq"] == event["seq"]
                   and e["type"] == "damage"]
        assert damages, "连携的准备型战法必须立即结算伤害"
        return
    raise AssertionError("40 个种子无准备型连携")


def test_assist_does_not_consume_normal_cast():
    """连携后同一战法当回合仍可正常释放（D-04）：搜索准备回合连携 +
    第 1 回合同武将同战法 cast 并存的种子。"""
    for seed in range(200):
        report = simulate(assist_setup(("test_war_cry",), ("test_mend",)), seed=seed)
        events = [e for e in report["games"][0]["events"]]
        assisted = assist_events(events, "a2")
        if not assisted:
            continue
        normal_casts = [
            e for e in events
            if e["type"] == "skill_trigger" and e["payload"]["kind"] == "cast"
            and e["payload"]["actor_id"] == "a2"
            and e["payload"]["skill_id"] == "test_war_cry" and e["t"]["r"] == 1
        ]
        if normal_casts:
            return
    raise AssertionError("200 个种子未见「连携后当回合再正常释放」（war_cry 100% 触发，不应如此）")


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
