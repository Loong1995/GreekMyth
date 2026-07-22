"""标杆战法逐英雄验收测试（任务书 6.1：skill_files.py 对位实现 + 阿喀琉斯新标杆）。

每个战法验证：触发时机正确、事件结构合规、数值/上限语义正确。

直接运行：python battle/tests/test_standard_skills.py
"""

import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import simulate
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero


def flat_events(report: dict) -> list[dict]:
    return [e for g in report["games"] for e in g["events"]]


def vs_dummies(hero, battle_id="t_std", dummy_force=70, dummy_command=110):
    """标杆武将 + 两个白板队友 vs 三个白板：隔离观察单个战法。"""
    return BattleSetup(
        battle_id=battle_id,
        teams=(
            TeamSetup(team_id="A", main_hero_id="x1", heroes=(
                hero,
                make_hero("x2", 1, force=80, command=100, speed=85),
                make_hero("x3", 2, force=80, command=100, speed=80),
            )),
            TeamSetup(team_id="B", main_hero_id="d1", heroes=(
                make_hero("d1", 0, force=dummy_force, command=dummy_command, speed=88),
                make_hero("d2", 1, force=dummy_force, command=dummy_command, speed=82),
                make_hero("d3", 2, force=dummy_force, command=dummy_command, speed=78),
            )),
        ),
    )


def status_events(events, status_id, event_type="status_apply"):
    return [e for e in events if e["type"] == event_type
            and e["payload"]["status"]["status_id"] == status_id]


# ---------------------------------------------------------------- 阿喀琉斯（人工标杆）

def achilles_setup():
    return vs_dummies(hero_setup("achilles", hero_id="x1", position=0))


def test_achilles_wrath_cast_in_prepare_round():
    report = simulate(achilles_setup(), seed=1)
    events = flat_events(report)
    casts = [e for e in events if e["type"] == "skill_trigger"
             and e["payload"]["skill_id"] == "achilles_wrath"]
    assert casts, "阿喀琉斯之怒必须在准备回合释放"
    for event in casts:
        assert event["t"]["r"] == 0 and event["payload"]["kind"] == "cast"
    applies = status_events(events, "achilles_wrath")
    assert applies and all(e["payload"]["status"]["owner_id"] == "x1" for e in applies)


def test_achilles_crit_rate_plus_25():
    """基础 0 + 战法 25% 物理暴击率（统计验证；追伤可暴击拉高观测率）。"""
    crits = total = 0
    for seed in range(80):
        report = simulate(achilles_setup(), seed=seed)
        for event in flat_events(report):
            if event["type"] != "damage":
                continue
            p = event["payload"]
            if p["source_id"] == "x1" and p["damage_type"] == "physical":
                total += 1
                crits += p["is_crit"]
    assert 0.15 <= crits / total <= 0.50, f"阿喀琉斯暴击率 {crits/total:.3f} 异常"


def test_achilles_fury_on_crit_ignores_command_max_7_per_round():
    """每次暴击触发追加伤害（60%，无视统帅、可暴击）；每回合最多 7 次。"""
    fury_seen = False
    for seed in range(40):
        report = simulate(achilles_setup(), seed=seed)
        events = flat_events(report)
        by_seq = {e["seq"]: e for e in events}
        per_round = Counter()
        for tick in status_events(events, "achilles_wrath", "status_tick"):
            fury_seen = True
            assert tick["group_id"] == tick["seq"], "追加伤害是独立播放组"
            cause = by_seq[tick["parent_seq"]]
            assert cause["type"] == "damage" and cause["payload"]["is_crit"], \
                "追加伤害必须由暴击 damage 引发"
            children = [e for e in events if e["parent_seq"] == tick["seq"]
                        and e["type"] == "damage"]
            assert len(children) == 1
            fury = children[0]["payload"]
            assert fury["damage_type"] == "physical"
            assert fury["target_id"] == cause["payload"]["target_id"]
            t = tick["t"]
            per_round[(t["g"], t["r"])] += 1
        assert all(count <= 7 for count in per_round.values()), \
            f"seed={seed} 单回合追加伤害超过 7 次: {per_round}"
    assert fury_seen, "40 个种子未见暴击追加伤害"


def test_achilles_fury_ignores_command_numerically():
    """无视统帅：dummy 统率 110 与 300 两种局面下，追加伤害均值应基本一致。"""
    def fury_amounts(dummy_command):
        amounts = []
        for seed in range(60):
            setup = vs_dummies(
                hero_setup("achilles", hero_id="x1", position=0),
                dummy_command=dummy_command,
            )
            report = simulate(setup, seed=seed)
            events = flat_events(report)
            for tick in status_events(events, "achilles_wrath", "status_tick"):
                for e in events:
                    if e["parent_seq"] == tick["seq"] and e["type"] == "damage":
                        amounts.append(e["payload"]["amount"])
        return amounts

    low = fury_amounts(110)
    high = fury_amounts(300)
    assert low and high
    mean_low = sum(low) / len(low)
    mean_high = sum(high) / len(high)
    # 统率 +190 时普攻伤害会暴跌，但追加伤害无视统帅 → 均值差应 <15%
    assert abs(mean_high - mean_low) / mean_low < 0.15, \
        f"追加伤害受统率影响: {mean_low:.0f} vs {mean_high:.0f}"


# ---------------------------------------------------------------- 宙斯·雷霆神谕

def test_thunder_lightning_max_3_per_round_and_no_recursion():
    setup = vs_dummies(hero_setup("zeus", hero_id="x1", position=0))
    for seed in range(20):
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        by_seq = {e["seq"]: e for e in events}
        per_hero_round = Counter()
        for tick in status_events(events, "thunder", "status_tick"):
            owner = tick["payload"]["status"]["owner_id"]
            t = tick["t"]
            per_hero_round[(owner, t["g"], t["r"])] += 1
            cause = by_seq[tick["parent_seq"]]
            assert cause["type"] == "damage"
            # 落雷不触发雷霆：引发落雷的 damage 不能是落雷本身
            grand = by_seq.get(cause["parent_seq"])
            if grand is not None and grand["type"] == "status_tick":
                assert grand["payload"]["status"]["status_id"] != "thunder", "落雷递归"
            children = [e for e in events if e["parent_seq"] == tick["seq"]
                        and e["type"] == "damage"]
            assert children and children[0]["payload"]["damage_type"] == "magic"
        assert all(v <= 3 for v in per_hero_round.values()), f"seed={seed} 落雷超上限"


# ---------------------------------------------------------------- 蛇杖庇护

def test_snake_staff_heals_after_damage_taken():
    setup = vs_dummies(hero_setup("asclepius", hero_id="x1", position=0))
    healed = False
    for seed in range(20):
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        by_seq = {e["seq"]: e for e in events}
        for tick in status_events(events, "snake_staff_protection", "status_tick"):
            cause = by_seq[tick["parent_seq"]]
            assert cause["type"] == "damage"
            assert cause["payload"]["target_id"] == tick["payload"]["status"]["owner_id"], \
                "蛇杖必须由持有者受击引发"
            heals = [e for e in events if e["parent_seq"] == tick["seq"]
                     and e["type"] == "heal"]
            for heal in heals:
                healed = True
                assert heal["payload"]["source_id"] == "x1"  # 治疗记名圣谕持有者
                assert heal["payload"]["target_id"] == tick["payload"]["status"]["owner_id"]
    assert healed, "20 个种子蛇杖从未治疗"


# ---------------------------------------------------------------- 哈迪斯·冥域君临

def test_hades_dominion_drain_and_shadow_veil():
    setup = vs_dummies(hero_setup("hades", hero_id="x1", position=0))
    report = simulate(setup, seed=5)
    game1 = [e for e in report["games"][0]["events"]]

    # 三个状态在准备回合全军/自身施加
    for status_id, expected_owners in (
        ("hades_lifesteal", {"x1", "x2", "x3"}),
        ("shadow_veil", {"x1", "x2", "x3"}),
        ("hades_command_drain", {"x1"}),
    ):
        applies = status_events(game1, status_id)
        assert {e["payload"]["status"]["owner_id"] for e in applies} == expected_owners

    # 冥祭献统（Phase 3 修订）：哈迪斯行动 → 队友 command 下降 + 哈迪斯 intelligence 上升
    drains = [e for e in game1 if e["type"] == "attr_change"
              and e["payload"].get("source_status", {}).get("status_id") == "hades_command_loss"]
    gains = [e for e in game1 if e["type"] == "attr_change"
             and e["payload"].get("source_status", {}).get("status_id") == "hades_int_gain"]
    assert drains and gains
    # v4：1:1 统率提升 + 额外等量智力（两次 attr_change）
    gain_attrs = {c["attr"] for e in gains[:2] for c in e["payload"]["changes"]}
    assert gain_attrs == {"command", "intelligence"}
    first_gain = gains[0]["payload"]["changes"][0]
    assert first_gain["after"] > first_gain["before"]
    for event in drains:
        change = event["payload"]["changes"][0]
        assert change["attr"] == "command" and change["after"] < change["before"]


def test_hades_command_returns_after_hades_defeated():
    """哈迪斯阵亡 → 队友的统率削减状态被清理（属性返还）。"""
    for seed in range(60):
        setup = BattleSetup(
            battle_id="t_hades_death",
            teams=(
                TeamSetup(team_id="A", main_hero_id="x2", heroes=(
                    hero_setup("hades", hero_id="x1", position=0, initial_troops=800),
                    make_hero("x2", 1, force=80, command=100, speed=85),
                )),
                TeamSetup(team_id="B", main_hero_id="d1", heroes=(
                    make_hero("d1", 0, force=100, command=110, speed=95),
                    make_hero("d2", 1, force=95, command=110, speed=90),
                )),
            ),
        )
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        defeated = [e for e in events if e["type"] == "hero_defeated"
                    and e["payload"]["hero_id"] == "x1"]
        if not defeated:
            continue
        drains = [e for e in events if e["type"] == "attr_change"
                  and e["payload"].get("source_status", {}).get("status_id") == "hades_command_loss"]
        if not drains:
            continue
        removes = [e for e in events if e["type"] == "status_remove"
                   and e["payload"]["status"]["status_id"] == "hades_command_loss"
                   and e["payload"]["reason"] == "source_defeated"]
        assert removes, f"seed={seed} 哈迪斯阵亡未清理统率削减"
        return
    raise AssertionError("60 个种子未构造出「哈迪斯献祭后阵亡」局面")


# ---------------------------------------------------------------- 波塞冬·三叉戟

def test_poseidon_trident_excludes_original_target_first():
    setup = vs_dummies(hero_setup("poseidon", hero_id="x1", position=0))
    seen = False
    for seed in range(20):
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        by_seq = {e["seq"]: e for e in events}
        # 按引发伤害分组三叉戟 tick
        shocks_by_cause: dict[int, list[dict]] = {}
        for tick in status_events(events, "poseidon_tide", "status_tick"):
            shocks_by_cause.setdefault(tick["parent_seq"], []).append(tick)
        for cause_seq, ticks in shocks_by_cause.items():
            seen = True
            assert len(ticks) <= 3, "单次伤害震荡超过 3 次"
            original_target = by_seq[cause_seq]["payload"]["target_id"]
            shocked: list[str] = []
            for tick in ticks:
                children = [e for e in events if e["parent_seq"] == tick["seq"]
                            and e["type"] == "damage"]
                assert len(children) == 1
                shock = children[0]["payload"]
                # 首震不得选原目标；后续不得选已震荡目标
                if not shocked:
                    assert shock["target_id"] != original_target
                assert shock["target_id"] not in shocked
                # 震荡量 = 原伤害 50%（受目标兵力截断，故 ≤）
                assert shock["amount"] <= max(1, by_seq[cause_seq]["payload"]["amount"] // 2 + 1)
                shocked.append(shock["target_id"])
    assert seen, "20 个种子三叉戟从未触发"


# ---------------------------------------------------------------- 赫拉克勒斯·十二试炼

def test_heracles_trials_max_12_and_grows():
    setup = vs_dummies(hero_setup("heracles", hero_id="x1", position=0))
    seen = False
    for seed in range(20):
        report = simulate(setup, seed=seed)
        for game in report["games"]:
            ticks = status_events(game["events"], "heracles_trials", "status_tick")
            assert len(ticks) <= 12, f"seed={seed} 单局试炼超过 12 次"
            if not ticks:
                continue
            seen = True
            # 每次试炼：force +2（attr_change temporary）+ 最多两段 60% 伤害
            for tick in ticks:
                grows = [e for e in game["events"] if e["parent_seq"] == tick["seq"]
                         and e["type"] == "attr_change"]
                assert grows and grows[0]["payload"]["changes"][0]["attr"] == "force"
                strikes = [e for e in game["events"] if e["parent_seq"] == tick["seq"]
                           and e["type"] == "damage"]
                assert 0 <= len(strikes) <= 2
                targets = [e["payload"]["target_id"] for e in strikes]
                assert len(targets) == len(set(targets)), "试炼两段伤害目标必须不同"
    assert seen, "试炼从未触发"


# ---------------------------------------------------------------- 美杜莎·石化凝视

def test_medusa_gaze_drains_int_and_petrifies_attacker():
    setup = vs_dummies(hero_setup("medusa", hero_id="x1", position=0))
    seen = False
    for seed in range(20):
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        by_seq = {e["seq"]: e for e in events}
        for tick in status_events(events, "medusa_gaze", "status_tick"):
            seen = True
            cause = by_seq[tick["parent_seq"]]
            attacker = cause["payload"]["source_id"]
            children = [e for e in events if e["parent_seq"] == tick["seq"]]
            petrifies = [e for e in children if e["type"] in ("status_apply",)
                         and e["payload"]["status"]["status_id"] == "petrify"]
            for petrify_event in petrifies:
                assert petrify_event["payload"]["status"]["owner_id"] == attacker
            drains = [e for e in children if e["type"] == "attr_change"
                      and e["payload"]["hero_id"] == attacker]
            for drain in drains:
                change = drain["payload"]["changes"][0]
                assert change["attr"] == "intelligence" and change["after"] < change["before"]
    assert seen, "凝视从未触发"


def test_petrify_vulnerable_plus_10_percent():
    """石化易伤 +10% 落在易伤乘区（D-01）：直接引擎级验证 modifier。"""
    from battle.engine import SeriesEngine
    from battle.events import PHASE_ACTION
    from battle.statuses import petrify

    setup = vs_dummies(hero_setup("medusa", hero_id="x1", position=0))
    engine = SeriesEngine(setup, seed=1)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "x1", "skill_id": "t", "kind": "cast", "target_ids": []})
    medusa, dummy = engine.hero_by_id("x1"), engine.hero_by_id("d1")
    engine.apply_status(medusa, dummy, petrify(1), parent_seq=anchor)
    assert engine.modifier(dummy, "vulnerable_bps") == 1000
    assert engine.is_forbidden(dummy, "forbid_basic")
    assert engine.is_forbidden(dummy, "forbid_active")


# ---------------------------------------------------------------- 阿瑞斯·战神怒火

def test_ares_warfury_blood_battle_on_all_and_might_on_best():
    setup = vs_dummies(hero_setup("ares", hero_id="x1", position=0))
    report = simulate(setup, seed=2)
    game1 = report["games"][0]["events"]
    blood = status_events(game1, "blood_battle")
    assert {e["payload"]["status"]["owner_id"] for e in blood} == \
        {"x1", "x2", "x3", "d1", "d2", "d3"}, "血战必须覆盖全场双方"
    might = status_events(game1, "ares_might")
    assert len(might) == 1 and might[0]["payload"]["status"]["owner_id"] == "x1", \
        "战神之勇给己方最高武力者（阿瑞斯 100）"
    from battle.skills_gods import BLOOD_BATTLE_STATUS, WAR_FRENZY_STATUS
    assert BLOOD_BATTLE_STATUS.modifiers == {
        "vulnerable_bps": 2000, "crit_damage_up_bps": 5000,
    }
    assert WAR_FRENZY_STATUS.modifiers == {
        "physical_damage_up_bps": 3000, "crit_rate_bps": 1500,
    }


# ---------------------------------------------------------------- 赫尔墨斯神谕

def test_hermes_oracle_marks_enemies_and_causes_hesitation():
    setup = vs_dummies(hero_setup("hermes", hero_id="x1", position=0))
    hesitated = delayed = False
    for seed in range(30):
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        marks = status_events(events, "hermes_confusion_mark")
        assert {e["payload"]["status"]["owner_id"] for e in marks} >= {"d1", "d2", "d3"}
        if status_events(events, "hesitation"):
            hesitated = True
        if [e for e in events if e["type"] == "skill_trigger"
                and e["payload"]["kind"] == "delayed"]:
            delayed = True
        if hesitated and delayed:
            return
    raise AssertionError(f"赫尔墨斯神谕未产生完整犹豫链: 犹豫={hesitated}, 延迟={delayed}")


def test_hermes_mark_only_first_two_rounds():
    """扰心印记仅前 2 回合生效（人工修订 2026-07-05）：第 2 回合窗口后到期，
    第 3 回合起不再有印记引发的犹豫施加/刷新。"""
    setup = vs_dummies(hero_setup("hermes", hero_id="x1", position=0))
    for seed in range(30):
        report = simulate(setup, seed=seed)
        events = flat_events(report)
        for event in events:
            if event["type"] in ("status_apply", "status_refresh") and \
                    event["payload"]["status"]["status_id"] == "hesitation":
                assert event["t"]["r"] <= 2, \
                    f"seed={seed} 第 {event['t']['r']} 回合仍有犹豫施加（印记应已到期）"
        removes = [e for e in status_events(events, "hermes_confusion_mark", "status_remove")
                   if e["payload"]["reason"] == "expired"]
        assert removes and all(e["t"]["r"] <= 3 for e in removes), "印记应在前 2 回合覆盖后到期"


# （v3.1 移除 stheno/gorgon_gaze，相关多目标选人覆盖见 test_targeting.py 的血性咆哮用例）


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
