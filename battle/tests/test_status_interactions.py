"""控制/延迟类状态交互矩阵逐格测试（任务书 B3；矩阵文档
docs/mechanics/status_interactions.md，本文件与矩阵逐格对应）。

引擎级驱动：直接构造 SeriesEngine，摆好状态后调行动窗口/结算入口，
断言事件与 RNG 消耗，逐格精确验证。

直接运行：python battle/tests/test_status_interactions.py
"""

import sys
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION, PHASE_GAME_START
from battle.skills import Skill, TIMING_PURSUIT, register
from battle.statuses import (
    DEBUFF,
    SPECIAL,
    PERMANENT,
    StatusDef,
    disarm,
    hesitation,
    ming_lock,
    petrify,
    silence,
)
from battle.tests.helpers import make_hero
from battle.setup import BattleSetup, TeamSetup


# ---------------------------------------------------------------- 测试专用注册

@dataclass(frozen=True, slots=True)
class _SurePursuit(Skill):
    """100% 追击：矩阵测试用（test_pursuit 是 50% 概率，不便断言必然性）。"""

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(actor, target, damage_type="physical",
                                   rate_bps=5000, parent_seq=trigger_seq, kind="pursuit")


register(_SurePursuit(skill_id="t_pursuit_100", timing=TIMING_PURSUIT))

# 受击即石化攻击者的反制状态（模拟美杜莎时机，100% 便于断言）
def _counter_petrify(engine, status, ctx):
    source = ctx["source"]
    owner = engine.hero_by_id(status.owner_id)
    if source.is_alive() and source.team_id != owner.team_id:
        engine.apply_status(owner, source, petrify(1), parent_seq=ctx["damage_seq"])


COUNTER_PETRIFY = StatusDef(
    status_id="t_counter_petrify", kind=SPECIAL, duration_rounds=PERMANENT,
    on_damage_taken=_counter_petrify,
)


# ---------------------------------------------------------------- 工具

def build_engine(a_skills=(), b_skills=(), seed=1) -> SeriesEngine:
    setup = BattleSetup(
        battle_id="t_matrix",
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(
                make_hero("a1", 0, force=90, intelligence=90, command=120, speed=90,
                          skills=a_skills),
                make_hero("a2", 1, force=80, command=120, speed=80),
            )),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(
                make_hero("b1", 0, force=85, intelligence=85, command=120, speed=85,
                          skills=b_skills),
                make_hero("b2", 1, force=75, command=120, speed=75),
            )),
        ),
    )
    engine = SeriesEngine(setup, seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 0, PHASE_GAME_START, 0)
    engine.writer.emit("game_start", {"game_no": 1, "troops": []})
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    return engine


def anchor(engine) -> int:
    return engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "t_anchor", "kind": "cast", "target_ids": []})


def events(engine, event_type=None):
    all_events = engine.writer.games_events()[-1]
    if event_type is None:
        return all_events
    return [e for e in all_events if e["type"] == event_type]


# ================================================================= 矩阵格 1：缄默 × 准备中

def test_cell_silence_interrupts_prepare():
    """缄默施加瞬间打断准备（缄默 + 打断两个事件、同组，任务书 5.3）。"""
    engine = build_engine(b_skills=("test_charged_nova",))
    seq = anchor(engine)
    caster, target = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine._preparing["b1"] = {"skill_id": "test_charged_nova", "remaining": 1}

    engine.apply_status(caster, target, silence(1), parent_seq=seq)

    interrupted = [e for e in events(engine, "skill_trigger")
                   if e["payload"]["kind"] == "interrupted"]
    assert len(interrupted) == 1
    p = interrupted[0]["payload"]
    assert p["skill_id"] == "test_charged_nova"
    assert p["interrupted_by"]["status_id"] == "silence"
    applies = events(engine, "status_apply")
    assert interrupted[0]["group_id"] == applies[-1]["group_id"], "缄默与打断同组"
    assert "b1" not in engine._preparing


def test_cell_ming_lock_and_petrify_also_interrupt():
    for status_builder in (ming_lock, petrify):
        engine = build_engine(b_skills=("test_charged_nova",))
        seq = anchor(engine)
        engine._preparing["b1"] = {"skill_id": "test_charged_nova", "remaining": 2}
        engine.apply_status(engine.hero_by_id("a1"), engine.hero_by_id("b1"),
                            status_builder(1), parent_seq=seq)
        interrupted = [e for e in events(engine, "skill_trigger")
                       if e["payload"]["kind"] == "interrupted"]
        assert len(interrupted) == 1, status_builder.__name__
        assert "b1" not in engine._preparing


def test_cell_disarm_does_not_interrupt_prepare():
    """缴械只禁普攻，不打断准备。"""
    engine = build_engine(b_skills=("test_charged_nova",))
    seq = anchor(engine)
    engine._preparing["b1"] = {"skill_id": "test_charged_nova", "remaining": 1}
    engine.apply_status(engine.hero_by_id("a1"), engine.hero_by_id("b1"),
                        disarm(1), parent_seq=seq)
    assert not [e for e in events(engine, "skill_trigger")
                if e["payload"]["kind"] == "interrupted"]
    assert "b1" in engine._preparing


# ================================================================= 矩阵格 2：石化 × 暴击

def test_cell_petrify_vulnerable_stacks_additively_with_crit():
    """石化 +10% 落易伤乘区（D-01），与暴击乘区相互独立（公式级验证）。"""
    from battle import formulas

    base_kwargs = dict(
        core_damage=360,
        attacker_current_troops=10000, target_current_troops=10000,
        skill_rate_bps=10000, random_coef_bps=10000,
    )
    plain = formulas.calc_damage(**base_kwargs)
    petrified = formulas.calc_damage(**base_kwargs, vulnerable_bps=1000)
    crit = formulas.calc_damage(**base_kwargs, crit_multiplier_bps=20000)
    both = formulas.calc_damage(**base_kwargs, vulnerable_bps=1000,
                                crit_multiplier_bps=20000)
    assert petrified == round(plain * 1.1)
    assert crit == plain * 2
    assert abs(both - plain * 2.2) <= 1  # 独立乘区连乘（±1 舍入）


def test_cell_petrified_hero_can_still_be_crit_and_takes_more():
    """被石化者仍可被暴击；易伤对暴击伤害同样生效（引擎级聚合验证）。"""
    engine = build_engine()
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(a1, b1, petrify(1), parent_seq=seq)
    assert engine.modifier(b1, "vulnerable_bps") == 1000
    # 石化不封锁暴击 roll：crit_rate 聚合不受石化影响
    assert engine.modifier(b1, "crit_rate_bps") == 0


# ================================================================= 矩阵格 3：犹豫 × 冥锁

def test_cell_hesitation_with_ming_lock_no_delay_roll():
    """冥锁全禁（无可延后的行动）→ 不做犹豫判定（不消耗 RNG）、无动作事件。"""
    engine = build_engine(b_skills=("test_blast",))
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(a1, b1, hesitation(10000), parent_seq=seq)  # 100% 延迟率
    engine.apply_status(a1, b1, ming_lock(1), parent_seq=seq)

    rng_before = engine.rng.index
    engine._run_action_window(b1, slot=0)
    assert engine.rng.index == rng_before, "全禁窗口不得消耗任何 RNG"
    window_actions = [e for e in events(engine)
                      if e["type"] in ("normal_attack",)
                      or (e["type"] == "skill_trigger"
                          and e["payload"]["actor_id"] == "b1")]
    assert window_actions == []
    starts = [e for e in events(engine, "action_start")
              if e["payload"]["actor_id"] == "b1"]
    assert starts and starts[0]["payload"].get("skipped") is True
    # 犹豫仍照常计次（D-02：行动窗口结算完毕后计次）
    hes = engine.find_status("b1", "hesitation")
    assert hes is not None and hes.action_tick_count == 1


def test_cell_hesitation_with_silence_delays_basic_only():
    """缄默 + 犹豫（100% 延迟率）：主动被禁不参与，普攻仍可被延后。"""
    engine = build_engine(b_skills=("test_blast",))
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(a1, b1, hesitation(10000), parent_seq=seq)
    engine.apply_status(a1, b1, silence(1), parent_seq=seq)

    engine._run_action_window(b1, slot=0)
    delayed = [e for e in events(engine, "skill_trigger")
               if e["payload"]["kind"] == "delayed"]
    assert len(delayed) == 1 and delayed[0]["payload"]["skill_id"] == "basic_attack"
    assert engine._delayed_actions["b1"][0]["kind"] == "basic"
    assert not events(engine, "normal_attack")


# ================================================================= 矩阵格 4：延迟期间施法者被控

def test_cell_delayed_active_voided_by_silence_basic_still_lands():
    """延迟到期时施法者被缄默：主动作废，普攻照常补打。"""
    engine = build_engine(b_skills=("test_blast",))
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine._delayed_actions["b1"] = [
        {"kind": "skill", "skill_id": "test_blast", "remaining": 1},
        {"kind": "basic", "skill_id": "basic_attack", "remaining": 1},
    ]
    engine.apply_status(a1, b1, silence(1), parent_seq=seq)

    engine._run_action_window(b1, slot=0)
    releases = [e for e in events(engine, "skill_trigger")
                if e["payload"]["kind"] == "release"]
    assert releases == [], "缄默期间延迟主动必须作废"
    attacks = [e for e in events(engine, "normal_attack")
               if e["payload"]["actor_id"] == "b1"]
    assert len(attacks) >= 1, "缄默不禁普攻：延迟普攻必须补打"
    assert engine._delayed_actions["b1"] == []


def test_cell_delayed_all_voided_by_ming_lock():
    """延迟到期时施法者被冥锁：主动与普攻一并作废（且到期条目清除）。"""
    engine = build_engine(b_skills=("test_blast",))
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine._delayed_actions["b1"] = [
        {"kind": "skill", "skill_id": "test_blast", "remaining": 1},
        {"kind": "basic", "skill_id": "basic_attack", "remaining": 1},
    ]
    engine.apply_status(a1, b1, ming_lock(1), parent_seq=seq)

    engine._run_action_window(b1, slot=0)
    assert not [e for e in events(engine, "skill_trigger")
                if e["payload"]["kind"] == "release"]
    assert not [e for e in events(engine, "normal_attack")
                if e["payload"]["actor_id"] == "b1"]
    assert engine._delayed_actions["b1"] == []


# ================================================================= 矩阵格 5：延迟目标阵亡 → 重选

def test_cell_delayed_action_reselects_target():
    """延迟生效时重新选目标（D-02 边界 3）：原目标阵亡自然换人，无合法目标作废。"""
    engine = build_engine(b_skills=("test_blast",))
    b1 = engine.hero_by_id("b1")
    engine._delayed_actions["b1"] = [{"kind": "skill", "skill_id": "test_blast",
                                      "remaining": 1}]
    # 杀掉 a2，只剩 a1 可选
    a2 = engine.hero_by_id("a2")
    a2.defeated = True

    engine._run_action_window(b1, slot=0)
    releases = [e for e in events(engine, "skill_trigger")
                if e["payload"]["kind"] == "release"]
    assert len(releases) == 1
    assert releases[0]["payload"]["target_ids"] == ["a1"], "重选目标只能是存活敌方"


# ================================================================= 矩阵格 6：施法者阵亡 / 跨局

def test_cell_defeat_clears_delayed_and_preparing():
    engine = build_engine(b_skills=("test_blast", "test_charged_nova"))
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine._delayed_actions["b1"] = [{"kind": "basic", "skill_id": "basic_attack",
                                      "remaining": 2}]
    engine._preparing["b1"] = {"skill_id": "test_charged_nova", "remaining": 1}
    b1.troops = 1
    engine.deal_damage(a1, b1, damage_type="physical", rate_bps=10000, parent_seq=seq)
    assert not b1.is_alive()
    assert "b1" not in engine._delayed_actions
    assert "b1" not in engine._preparing


def test_cell_game_reset_clears_delayed_and_preparing():
    """延迟/准备跨局边界 → 随局清空（任务书 5.1 / D-02 边界 4）。"""
    engine = build_engine(b_skills=("test_blast",))
    engine._delayed_actions["b1"] = [{"kind": "basic", "skill_id": "basic_attack",
                                      "remaining": 3}]
    engine._preparing["b1"] = {"skill_id": "test_charged_nova", "remaining": 1}
    engine._reset_game_state()
    assert engine._delayed_actions == {} and engine._preparing == {}


# ================================================================= 矩阵格 7：控制 × 追击

def test_cell_petrify_mid_chain_stops_pursuit():
    """普攻反制石化攻击者 → 追击不触发（禁普攻即无追击，任务书 5.4-1）。"""
    engine = build_engine(a_skills=("t_pursuit_100",))
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    engine.apply_status(b1, b1, COUNTER_PETRIFY, parent_seq=seq)
    # 只留 b1 一个目标，保证普攻必打 b1
    engine.hero_by_id("b2").defeated = True

    engine._run_action_window(a1, slot=0)
    petrified = [e for e in events(engine, "status_apply")
                 if e["payload"]["status"]["status_id"] == "petrify"
                 and e["payload"]["status"]["owner_id"] == "a1"]
    assert petrified, "测试前提：反制石化必须命中攻击者"
    pursuits = [e for e in events(engine, "skill_trigger")
                if e["payload"]["skill_id"] == "t_pursuit_100"]
    assert pursuits == [], "石化后追击必须被封锁"


def test_cell_pursuit_fires_without_control():
    """对照组：无控制时 100% 追击必然触发。"""
    engine = build_engine(a_skills=("t_pursuit_100",))
    a1 = engine.hero_by_id("a1")
    engine._run_action_window(a1, slot=0)
    pursuits = [e for e in events(engine, "skill_trigger")
                if e["payload"]["skill_id"] == "t_pursuit_100"]
    assert len(pursuits) == 1


# ================================================================= 矩阵格 8：控制 × DoT

def test_cell_dot_ticks_while_owner_controlled():
    """冥锁不冻结 DoT：受控者的中毒照常在回合开始结算。"""
    engine = build_engine()
    seq = anchor(engine)
    a1, b1 = engine.hero_by_id("a1"), engine.hero_by_id("b1")
    dot = StatusDef(status_id="t_dot", kind=DEBUFF, duration_rounds=2, dot_rate_bps=5000)
    engine.apply_status(a1, b1, dot, parent_seq=seq)
    engine.apply_status(a1, b1, ming_lock(1), parent_seq=seq)

    troops_before = b1.troops
    engine._tick_periodic_statuses(seq)
    assert b1.troops < troops_before, "冥锁下 DoT 必须照常掉血"


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
