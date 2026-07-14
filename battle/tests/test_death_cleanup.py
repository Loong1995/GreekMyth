"""阵亡与退出战斗鲁棒清理专项边界测试（任务书 5.5）。

覆盖：阵亡者施加给他人的状态事件化清理、自身状态静默清空、DoT 来源阵亡后不再 tick、
阵亡者不可为目标/不再行动、主将阵亡即局终、治疗不复活。
直接运行：python battle/tests/test_death_cleanup.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.engine import SeriesEngine
from battle.events import PHASE_ACTION
from battle.statuses import BUFF, DEBUFF, StatusDef
from battle.tests.helpers import full_3v3_setup, make_hero, skills_3v3_setup
from battle import simulate
from battle.setup import BattleSetup, TeamSetup


def bare_engine(seed: int = 1) -> tuple[SeriesEngine, int]:
    engine = SeriesEngine(full_3v3_setup(), seed=seed)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    anchor = engine.writer.emit("skill_trigger", {
        "actor_id": "a1", "skill_id": "test_anchor", "kind": "cast", "target_ids": [],
    })
    return engine, anchor


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


DOT = StatusDef(status_id="dot_from_victim", kind=DEBUFF, duration_rounds=8, dot_rate_bps=3000)
MARK = StatusDef(status_id="mark_from_victim", kind=DEBUFF, duration_rounds=8,
                 modifiers={"vulnerable_bps": 1000})
SELF_BUFF = StatusDef(status_id="victim_own_buff", kind=BUFF, duration_rounds=8,
                      modifiers={"damage_up_bps": 1000})


def kill(engine: SeriesEngine, victim_id: str, killer_id: str, parent_seq: int) -> None:
    """把 victim 打到兵力归零（走正式伤害原语，多次真伤直至致死）。"""
    victim = engine.hero_by_id(victim_id)
    killer = engine.hero_by_id(killer_id)
    while victim.is_alive():
        engine.deal_damage(killer, victim, damage_type="true", rate_bps=100000,
                           parent_seq=parent_seq, can_crit=False,
                           fixed_extra_damage=victim.troops)


def test_defeat_removes_statuses_given_to_others_with_events():
    engine, anchor = bare_engine()
    b2 = engine.hero_by_id("b2")
    engine.apply_status(b2, engine.hero_by_id("a1"), DOT, parent_seq=anchor)
    engine.apply_status(b2, engine.hero_by_id("a2"), MARK, parent_seq=anchor)
    engine.apply_status(b2, b2, SELF_BUFF, parent_seq=anchor)
    # 其他来源的状态不受影响
    engine.apply_status(engine.hero_by_id("b1"), engine.hero_by_id("a1"), MARK, parent_seq=anchor)

    kill(engine, "b2", "a1", anchor)

    removes = events_of(engine, "status_remove")
    assert {(e["payload"]["status"]["status_id"], e["payload"]["status"]["owner_id"])
            for e in removes} == {("dot_from_victim", "a1"), ("mark_from_victim", "a2")}
    assert all(e["payload"]["reason"] == "source_defeated" for e in removes)
    defeated_seq = events_of(engine, "hero_defeated")[0]["seq"]
    assert all(e["parent_seq"] == defeated_seq for e in removes)  # 挂 hero_defeated 之下

    # 阵亡者自身状态静默清空（无事件）；他源状态保留
    assert engine.hero_statuses("b2") == []
    assert [s.status_id for s in engine.hero_statuses("a1")] == ["mark_from_victim"]
    assert [s.source_id for s in engine.hero_statuses("a1")] == ["b1"]


def test_dot_stops_ticking_after_source_defeated():
    engine, anchor = bare_engine()
    b2, a1 = engine.hero_by_id("b2"), engine.hero_by_id("a1")
    engine.apply_status(b2, a1, DOT, parent_seq=anchor)

    kill(engine, "b2", "a1", anchor)
    damage_count_before = len(events_of(engine, "damage"))
    engine._tick_periodic_statuses(anchor)  # 来源已亡 → 状态已清 → 无 tick

    assert len(events_of(engine, "status_tick")) == 0
    assert len(events_of(engine, "damage")) == damage_count_before


def test_defeated_hero_not_targetable_and_not_acting():
    engine, anchor = bare_engine()
    kill(engine, "b2", "a1", anchor)

    a1 = engine.hero_by_id("a1")
    for _ in range(50):
        target = engine.select_enemy_by_hit_rate(a1, reason="test")
        assert target.hero_id != "b2"

    assert "b2" not in engine._build_action_order(round_no=2)


def test_heal_cannot_revive_defeated_hero():
    engine, anchor = bare_engine()
    kill(engine, "b2", "a1", anchor)
    b2, b1 = engine.hero_by_id("b2"), engine.hero_by_id("b1")

    heal_seq = engine.heal(b1, b2, rate_bps=100000, parent_seq=anchor, can_crit=False)
    assert heal_seq == 0  # 兵力 0（缺兵不设限但 troops 0 → wounded 有但 constrain… 见下）
    assert not b2.is_alive()

    # 已阵亡者也不能再被施加状态
    assert engine.apply_status(b1, b2, SELF_BUFF, parent_seq=anchor) is None


def test_main_hero_defeat_ends_game_immediately():
    engine, anchor = bare_engine()
    kill(engine, "b1", "a1", anchor)  # b1 是 B 队主将
    assert engine._game_winner == "A"
    assert events_of(engine, "hero_defeated")[0]["payload"]["is_main_hero"] is True


def test_deputy_defeat_does_not_end_game():
    engine, anchor = bare_engine()
    kill(engine, "b3", "a1", anchor)
    assert engine._game_winner is None


def test_full_battle_with_lethal_dot_is_robust():
    """端到端：DoT 能致死且致死后战报仍满足结构约束（无half-截断、无死者事件）。"""
    # 极端阵容：a1 挂毒后 b 方脆皮很快被 DoT 磨死
    team_a = TeamSetup(team_id="A", main_hero_id="a1", heroes=(
        make_hero("a1", 0, intelligence=200, command=200, speed=99, skills=("test_poison",)),
    ))
    team_b = TeamSetup(team_id="B", main_hero_id="b1", heroes=(
        make_hero("b1", 0, force=10, command=10, speed=10, max_troops=10000,
                  initial_troops=1500),
    ))
    report = simulate(BattleSetup(battle_id="t_dot_lethal", teams=(team_a, team_b)), seed=7)
    assert report["result"]["winner_team_id"] == "A"

    dead_at: dict[str, int] = {}
    for game in report["games"]:
        for event in game["events"]:
            if event["type"] == "hero_defeated":
                dead_at[event["payload"]["hero_id"]] = event["seq"]
            actor = event["payload"].get("actor_id") or event["payload"].get("source_id")
            if actor in dead_at and event["seq"] > dead_at[actor]:
                raise AssertionError(f"阵亡者 {actor} 在 seq={event['seq']} 仍产生动作/来源事件")


def test_skills_battle_no_events_from_dead_across_seeds():
    """多种子扫描：全战法阵容下，阵亡者绝不再作为 actor/source 出现。"""
    for seed in range(1, 21):
        report = simulate(skills_3v3_setup(), seed=seed)
        dead_at: dict[str, int] = {}
        for game in report["games"]:
            for event in game["events"]:
                if event["type"] == "hero_defeated":
                    dead_at[event["payload"]["hero_id"]] = event["seq"]
                payload = event["payload"]
                actor = payload.get("actor_id") or payload.get("source_id")
                if actor in dead_at and event["seq"] > dead_at[actor]:
                    raise AssertionError(
                        f"seed={seed}: 阵亡者 {actor} 在 seq={event['seq']} 仍活动"
                    )


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__, "-v"]))
