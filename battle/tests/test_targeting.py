"""受击率选人事件化测试（B4）：payload 可选字段 target_select 的结构与数值正确性。

规则见 docs/mechanics/targeting.md：受击点数 = 初始 5000 - 损兵比例×3000（动态重算），
敌方随机目标按点数加权；选人记录随最近的宣告/结算事件带出。

直接运行：python battle/tests/test_targeting.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.formulas import calc_hit_points_bps
from battle.tests.helpers import full_3v3_setup, skills_3v3_setup, standard_3v3_setup


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def test_normal_attack_carries_target_select():
    """每次普攻宣告必带选人记录：命中者 = 宣告目标，且在候选池中。"""
    report = simulate(full_3v3_setup(), seed=7)
    attacks = [e for e in flat_events(report) if e["type"] == "normal_attack"]
    assert attacks
    for attack in attacks:
        selects = attack["payload"].get("target_select")
        assert selects, "普攻必带 target_select"
        record = selects[-1]  # 连击时每击独立记录（各自宣告事件分开带出）
        assert record["selected_id"] == attack["payload"]["target_ids"][0]
        pool_ids = [c["hero_id"] for c in record["candidates"]]
        assert record["selected_id"] in pool_ids
        assert all(c["hit_bps"] >= 0 for c in record["candidates"])


def test_hit_points_match_formula_and_decay():
    """记录中的受击点数 = calc_hit_points_bps（按满兵基准动态重算）：
    候选点数单调不增（兵力只降不升出满编 → 点数只会下降）。"""
    report = simulate(full_3v3_setup(), seed=11)
    full_points = calc_hit_points_bps(
        initial_hit_points_bps=5000, max_troops=10000, current_troops=10000)
    seen_decayed = False
    last_points: dict[str, int] = {}
    for event in flat_events(report):
        for record in event["payload"].get("target_select", ()):
            for cand in record["candidates"]:
                points = cand["hit_bps"]
                assert points <= full_points
                # 该阵容无治疗，兵力单调下降（含跨局残血续战）→ 点数单调不增
                if cand["hero_id"] in last_points:
                    assert points <= last_points[cand["hero_id"]], \
                        "受击点数应随损兵单调下降"
                last_points[cand["hero_id"]] = points
                if points < full_points:
                    seen_decayed = True
    assert seen_decayed, "全场未见受击点数因损兵下降"


def test_skill_trigger_multi_select_with_exclusion():
    """多段选人（测试战法 2 目标互斥）：skill_trigger 带 2 条记录，
    第 2 条候选池排除第 1 条命中者。"""
    from dataclasses import dataclass

    from battle.setup import BattleSetup, TeamSetup
    from battle.skill_common import pick_distinct_enemies
    from battle.skills import REGISTRY, Skill, register
    from battle.tests.helpers import make_hero

    @dataclass(frozen=True, slots=True)
    class _TwoTargetStrike(Skill):
        def select_targets(self, engine, actor):
            return pick_distinct_enemies(engine, actor, 2, f"skill:{self.skill_id}")

        def execute(self, engine, actor, targets, trigger_seq):
            for target in targets:
                if target.is_alive():
                    engine.deal_damage(
                        actor, target, damage_type="physical", rate_bps=24000,
                        parent_seq=trigger_seq,
                    )

    if "test_two_target" not in REGISTRY:
        register(_TwoTargetStrike(skill_id="test_two_target", trigger_rate_bps=4000))

    setup = BattleSetup(
        battle_id="t_multi_select",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                make_hero("a1", 0, force=95, skills=("test_two_target",)),)),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, command=150),
                make_hero("b2", 1, command=150),
                make_hero("b3", 2, command=150),
            )),
        ),
    )
    for seed in range(40):
        report = simulate(setup, seed=seed)
        for event in flat_events(report):
            if event["type"] != "skill_trigger" or \
                    event["payload"]["skill_id"] != "test_two_target" or \
                    event["payload"]["kind"] not in ("cast", "release", "assist"):
                continue
            selects = event["payload"].get("target_select", [])
            if len(selects) < 2:
                continue
            first, second = selects[0], selects[1]
            second_pool = [c["hero_id"] for c in second["candidates"]]
            assert first["selected_id"] not in second_pool, "第二段选人必须排除首目标"
            return
    raise AssertionError("40 个种子未见血性咆哮两段选人")


def test_hit_weight_bias_scales_selection_weight():
    """受击权重偏置（Phase 4 集火底层）：hit_weight_up_bps 使记录权重按倍率放大，
    仍是加权随机（其余候选权重不变），无偏置时逐值等于受击点数。"""
    from battle.engine import SeriesEngine
    from battle.events import PHASE_ACTION
    from battle.statuses import BUFF, StatusDef

    engine = SeriesEngine(full_3v3_setup(), seed=5)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    base = b1.hit_points_bps()
    engine.apply_status(a1, b1, StatusDef(
        status_id="t_focus", kind=BUFF, duration_rounds=-1,
        modifiers={"hit_weight_up_bps": 10000},  # 权重 ×2
    ), parent_seq=anchor)
    engine.select_enemy_by_hit_rate(a1, reason="t_focus")
    record = engine._drain_target_selects()[-1]
    weights = {c["hero_id"]: c["hit_bps"] for c in record["candidates"]}
    assert weights["b1"] == base * 2
    assert weights["b2"] == engine.hero_by_id("b2").hit_points_bps()  # 无偏置不变


def test_brief_log_hides_and_all_log_shows_selection():
    from battle.textlog import format_report

    report = simulate(skills_3v3_setup(), seed=3)
    text_all = format_report(report, mode="all")
    text_brief = format_report(report, mode="brief")
    assert "·选人[普攻] 受击点数:" in text_all
    assert "·选人" not in text_brief


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
