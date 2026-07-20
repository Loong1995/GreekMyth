"""Phase 4 A1 底座单测：站位 1~6/后排、连发 burst、四轨势能、协击钩子。

直接运行：python -m pytest battle/tests/test_phase4_base.py -q
"""

import sys
from dataclasses import dataclass, replace
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle.api import simulate
from battle.engine import MAX_CASTS_PER_WINDOW, MOMENTUM_FULL, SeriesEngine
from battle.errors import SetupError
from battle.events import PHASE_ACTION
from battle.setup import BattleSetup, TeamSetup, validate_setup
from battle.skills import REGISTRY, Skill, register
from battle.statuses import SPECIAL, StatusDef
from battle.tests.helpers import full_3v3_setup, make_hero


# ----------------------------------------------------------------- 测试用战法

@dataclass(frozen=True, slots=True)
class _P4Strike(Skill):
    """低伤单体兵刃：验证连发（burst_rate_bps 由用例覆盖）。"""

    def select_targets(self, engine, actor):
        target = engine.select_enemy_by_hit_rate(actor, reason=f"skill:{self.skill_id}")
        return [target] if target is not None else []

    def execute(self, engine, actor, targets, trigger_seq):
        for target in targets:
            if target.is_alive():
                engine.deal_damage(
                    actor, target, damage_type="physical", rate_bps=1000,
                    parent_seq=trigger_seq, can_crit=False,
                )


if "test_p4_burst" not in REGISTRY:
    register(_P4Strike(skill_id="test_p4_burst", burst_rate_bps=10000))
    register(_P4Strike(skill_id="test_p4_no_burst"))


def bare_engine(*, momentum: bool = False) -> SeriesEngine:
    setup = full_3v3_setup()
    if momentum:
        setup = BattleSetup(
            battle_id=setup.battle_id, teams=setup.teams,
            metadata={"enable_momentum": True},
        )
    engine = SeriesEngine(setup, seed=7)
    engine.writer.begin_game()
    engine.writer.set_time(1, 1, PHASE_ACTION, 0)
    return engine


def events_of(engine: SeriesEngine, event_type: str) -> list[dict]:
    return [e for e in engine.writer.games_events()[-1] if e["type"] == event_type]


# ----------------------------------------------------------------- 站位 1~6

def test_position_range_and_backline():
    team_a = TeamSetup(team_id="A", main_hero_id="a1", heroes=(
        make_hero("a1", 1), make_hero("a2", 3), make_hero("a3", 6)))
    team_b = TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 4),))
    setup = BattleSetup(battle_id="t_pos", teams=(team_a, team_b))
    validate_setup(setup)  # 1~6 合法
    assert not team_a.heroes[0].is_backline and not team_a.heroes[1].is_backline
    assert team_a.heroes[2].is_backline and team_b.heroes[0].is_backline

    bad = BattleSetup(battle_id="t_pos_bad", teams=(
        TeamSetup(team_id="A", main_hero_id="a1", heroes=(make_hero("a1", 7),)),
        team_b,
    ))
    with pytest.raises(SetupError):
        validate_setup(bad)


def test_hero_state_backline():
    engine = bare_engine()
    assert not engine.hero_by_id("a1").is_backline  # 旧口径 0~2 均前排


# ----------------------------------------------------------------- 连发 burst

def test_burst_hard_cap_and_burst_no():
    engine = bare_engine()
    hero = engine.hero_by_id("a1")
    engine._cast_active_skill(hero, REGISTRY["test_p4_burst"], "cast")

    triggers = events_of(engine, "skill_trigger")
    assert len(triggers) == MAX_CASTS_PER_WINDOW  # 100% 连发 → 硬上限 7 次
    assert "burst_no" not in triggers[0]["payload"]
    assert [t["payload"]["burst_no"] for t in triggers[1:]] == list(range(2, 8))
    first_seq = triggers[0]["seq"]
    # 连发各自成组（独立播放单元），parent 指回首发触发事件
    for t in triggers[1:]:
        assert t["parent_seq"] == first_seq
        assert t["group_id"] == t["seq"]


def test_no_burst_single_cast_no_rng():
    engine = bare_engine()
    hero = engine.hero_by_id("a1")
    before = engine.rng.index
    engine._cast_active_skill(hero, REGISTRY["test_p4_no_burst"], "cast")
    triggers = events_of(engine, "skill_trigger")
    assert len(triggers) == 1
    # burst_rate=0：除选人/伤害内部 roll 外不额外消耗连发 RNG（无 burst 调试记录）
    assert not any(r["kind"] == "burst" for r in engine.debug_rolls)
    assert engine.rng.index > before  # 正常效果照常消耗


# ----------------------------------------------------------------- 四轨势能

def test_momentum_accounting_and_cut_in():
    engine = bare_engine(momentum=True)
    hero = engine.hero_by_id("a1")
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    for _ in range(MOMENTUM_FULL + 1):
        engine.add_momentum(hero, "active", reason="active_cast", parent_seq=anchor)

    changes = events_of(engine, "momentum_change")
    assert [c["payload"]["value"] for c in changes] == list(range(1, MOMENTUM_FULL + 2))
    # value<5 不 cut_in；value≥5（满档当次起）每次 cut_in=True
    assert all("cut_in" not in c["payload"] for c in changes[: MOMENTUM_FULL - 1])
    assert all(c["payload"].get("cut_in") is True for c in changes[MOMENTUM_FULL - 1 :])
    assert engine.momentum_of("a1", "active") == MOMENTUM_FULL + 1


def test_momentum_enabled_by_default_and_opt_out():
    """契约 1.4.0 收口：默认开启，metadata 显式 False 可关。"""
    report_on = simulate(full_3v3_setup(), seed=42)
    assert any(e["type"] == "momentum_change"
               for g in report_on["games"] for e in g["events"])
    setup = full_3v3_setup("t_momentum_off")
    setup = BattleSetup(battle_id=setup.battle_id, teams=setup.teams,
                        metadata={"enable_momentum": False})
    report_off = simulate(setup, seed=42)
    for game in report_off["games"]:
        assert all(e["type"] != "momentum_change" for e in game["events"])


def test_momentum_enabled_full_battle():
    setup = full_3v3_setup("t_momentum")
    report = simulate(setup, seed=42)
    changes = [e for game in report["games"] for e in game["events"]
               if e["type"] == "momentum_change"]
    assert changes, "全普攻对局应产生普攻轨势能"
    for c in changes:
        assert c["payload"]["track"] in ("active", "passive", "oracle", "basic_pursuit")
        assert c["payload"]["delta"] == 1 and c["payload"]["value"] >= 1

    # 势能纯表现记账：关闭/开启两份战报的非势能事件序列必须逐条一致（不扰动 RNG）
    off_setup = BattleSetup(battle_id="t_momentum", teams=full_3v3_setup("t_momentum").teams,
                            metadata={"enable_momentum": False})
    base = simulate(off_setup, seed=42)
    for g_on, g_off in zip(report["games"], base["games"]):
        on = [e for e in g_on["events"] if e["type"] != "momentum_change"]
        off = list(g_off["events"])
        assert len(on) == len(off)
        for a, b in zip(on, off):
            assert a["type"] == b["type"] and a["payload"] == b["payload"]


# ----------------------------------------------------------------- 协击

COORD_STATUS = StatusDef(
    status_id="test_p4_coord", kind=SPECIAL, duration_rounds=-1,
    on_ally_basic_attack=lambda engine, status, ctx: engine.perform_coordinated_attack(
        engine.hero_by_id(status.owner_id), ctx["target"],
        parent_seq=ctx["damage_seq"],
    ),
)


def test_coordinated_attack_follows_ally_basic():
    engine = bare_engine()
    a1, a2 = engine.hero_by_id("a1"), engine.hero_by_id("a2")
    engine.apply_status(a2, a2, COORD_STATUS,
                        parent_seq=engine.writer.emit("round_start", {"round_no": 1}))
    engine._perform_basic_attack(a1)

    attacks = events_of(engine, "normal_attack")
    coord = [a for a in attacks if a["payload"].get("kind") == "coordinated"]
    plain = [a for a in attacks if "kind" not in a["payload"]]
    assert len(plain) >= 1 and len(coord) == len(plain)  # 每击各协击一次
    for c in coord:
        assert c["payload"]["actor_id"] == "a2"
        assert c["group_id"] == c["seq"]  # 协击是新播放组


def test_coordinated_attack_skipped_when_forbidden():
    from battle.statuses import CONTROL

    engine = bare_engine()
    a1, a2 = engine.hero_by_id("a1"), engine.hero_by_id("a2")
    anchor = engine.writer.emit("round_start", {"round_no": 1})
    engine.apply_status(a2, a2, COORD_STATUS, parent_seq=anchor)
    engine.apply_status(
        a1, a2,
        StatusDef(status_id="test_p4_lock", kind=CONTROL, duration_rounds=1,
                  modifiers={"forbid_basic": True}),
        parent_seq=anchor,
    )
    engine._perform_basic_attack(a1)
    attacks = events_of(engine, "normal_attack")
    assert all(a["payload"].get("kind") != "coordinated" for a in attacks)


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
