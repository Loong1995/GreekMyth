import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import EventType, HeroRole, Timing
from _output_helper import format_battle_result, print_and_save_output

DUAL_PREP_SKILLS = ["delphi_charged_oracle", "pythia_woven_scheme", "basic_attack"]
DUMMY_SKILLS = ["basic_attack"]


def build_prepare_release_input(*, seed: int, max_rounds: int = 3) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_delphi_charged_oracle",
        seed=seed,
        max_rounds=max_rounds,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig(
                "a_main",
                "Caster",
                "team_a",
                HeroRole.MAIN,
                1,
                10000,
                80,
                150,
                80,
                100,
                ["delphi_charged_oracle", "basic_attack"],
            ),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 3000, 70, 60, 50, 60, DUMMY_SKILLS),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 3000, 70, 60, 50, 50, DUMMY_SKILLS),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 80, 50, DUMMY_SKILLS),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 3000, 70, 60, 50, 40, DUMMY_SKILLS),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 3000, 70, 60, 50, 30, DUMMY_SKILLS),
        ],
    )


def build_dual_prep_input(*, seed: int, max_rounds: int = 3) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_dual_prep_skills",
        seed=seed,
        max_rounds=max_rounds,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig(
                "a_main",
                "Caster",
                "team_a",
                HeroRole.MAIN,
                1,
                10000,
                80,
                150,
                80,
                100,
                DUAL_PREP_SKILLS,
            ),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 3000, 70, 60, 50, 60, DUMMY_SKILLS),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 3000, 70, 60, 50, 50, DUMMY_SKILLS),
        ],
        team_b_heroes=[
            HeroConfig("b_main", "B-Main", "team_b", HeroRole.MAIN, 1, 10000, 80, 70, 80, 50, DUMMY_SKILLS),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 3000, 70, 60, 50, 40, DUMMY_SKILLS),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 3000, 70, 60, 50, 30, DUMMY_SKILLS),
        ],
    )


def build_interrupt_input(*, seed: int) -> BattleInput:
    db = build_demo_config_db()
    return BattleInput(
        battle_id="battle_delphi_charged_interrupt",
        seed=seed,
        max_rounds=3,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig(
                "a_main",
                "Caster",
                "team_a",
                HeroRole.MAIN,
                1,
                10000,
                80,
                150,
                80,
                100,
                ["delphi_charged_oracle", "basic_attack"],
            ),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 3000, 70, 60, 50, 60, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 3000, 70, 60, 50, 50, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig(
                "b_main",
                "Gorgon",
                "team_b",
                HeroRole.MAIN,
                1,
                10000,
                80,
                120,
                80,
                90,
                ["gorgon_gaze", "basic_attack"],
            ),
            HeroConfig("b_d1", "B-D1", "team_b", HeroRole.DEPUTY, 2, 3000, 70, 60, 50, 40, ["basic_attack"]),
            HeroConfig("b_d2", "B-D2", "team_b", HeroRole.DEPUTY, 3, 3000, 70, 60, 50, 30, ["basic_attack"]),
        ],
    )


def _skill_summary(result, skill_id: str) -> dict:
    for summary in result.summary.skill_summaries:
        if summary["skill_id"] == skill_id and summary["owner"] == "a_main":
            return summary
    raise AssertionError(f"missing skill summary for {skill_id}")


def test_delphi_charged_oracle_prepare_then_release() -> None:
    result = run_battle(build_prepare_release_input(seed=3, max_rounds=2))
    logs = "\n".join(result.human_logs)

    print_and_save_output(
        "test_delphi_charged_oracle_prepare_then_release",
        format_battle_result("Delphi Charged Oracle", result),
    )

    assert "进入【神谕吟诵】" in logs
    assert "德尔斐蓄谕【神谕吟诵】进度 1/1" in logs
    assert "德尔斐蓄谕 准备完成，释放" in logs
    assert "德尔斐蓄谕落咒" in logs

    prepare_events = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.TRIGGER_SUCCESS
        and event.payload.get("phase") == "PREPARE"
    ]
    release_events = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.TRIGGER_SUCCESS
        and event.payload.get("phase") == "RELEASE"
    ]
    prepare_before_signals = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.BEFORE_ACTIVE_SIGNAL
        and event.payload.get("trigger_phase") == "PREPARE"
    ]
    release_before_signals = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.BEFORE_ACTIVE_SIGNAL
        and event.payload.get("trigger_phase") == "RELEASE"
    ]
    release_active_signals = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.ACTIVE_SIGNAL
        and event.payload.get("trigger_phase") == "RELEASE"
    ]
    prepare_active_signals = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.ACTIVE_SIGNAL
        and event.payload.get("trigger_phase") == "PREPARE"
    ]
    assert prepare_events
    assert release_events
    assert prepare_before_signals
    assert not prepare_active_signals
    assert release_before_signals
    assert release_active_signals

    prepare_post = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.POST_TRIGGER
        and event.payload.get("trigger_phase") == "PREPARE"
    ]
    release_post = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.POST_TRIGGER
        and event.payload.get("trigger_phase") == "RELEASE"
    ]
    effective_post = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.POST_TRIGGER
        and event.payload.get("effective") is True
    ]
    assert prepare_post
    assert all(event.payload.get("effective") is False for event in prepare_post)
    assert len(release_post) == 1
    assert release_post[0].payload.get("effective") is True
    assert effective_post == release_post

    summary = _skill_summary(result, "delphi_charged_oracle")
    assert summary["success_count"] == 1

    executed_release = [
        record
        for record in summary["effect_execution_records"]
        if record["effect_id"] == "delphi_charged_release_damage" and record["status"] == "EXECUTED"
    ]
    assert executed_release

    round_one_logs = [line for line in result.human_logs if "[Round 1][ACTIVE]" in line]
    assert any("准备完成，释放" not in line for line in round_one_logs)

    preparing_fails = [
        event
        for event in result.event_stream
        if event.skill_id == "delphi_charged_oracle"
        and event.event_type == EventType.TRIGGER_FAIL
        and event.payload.get("reason") == "ACTIVE_PREPARING"
    ]
    assert not preparing_fails

    round_two_active_events = [
        event
        for event in result.event_stream
        if event.round_no == 2
        and event.timing == Timing.ACTIVE
        and event.actor_id == "a_main"
        and event.skill_id == "delphi_charged_oracle"
    ]
    release_idx = next(
        i
        for i, event in enumerate(round_two_active_events)
        if event.event_type == EventType.TRIGGER_SUCCESS and event.payload.get("phase") == "RELEASE"
    )
    after_release = round_two_active_events[release_idx + 1 :]
    assert not any(event.event_type == EventType.PRE_TRIGGER_CHECK for event in after_release)
    assert not any(
        event.event_type == EventType.TRIGGER_FAIL and event.payload.get("reason") == "ACTIVE_PREPARING"
        for event in after_release
    )


def test_dual_prep_skills_independent_same_round_prepare_and_release() -> None:
    result = run_battle(build_dual_prep_input(seed=1, max_rounds=2))
    logs = "\n".join(result.human_logs)

    print_and_save_output(
        "test_dual_prep_skills_independent_same_round_prepare_and_release",
        format_battle_result("Dual Prep Skills", result),
    )

    round_one_active = [line for line in result.human_logs if "[Round 1][ACTIVE]" in line]
    round_two_active = [line for line in result.human_logs if "[Round 2][ACTIVE]" in line]

    assert any("德尔斐蓄谕" in line and "进入【神谕吟诵】" in line for line in round_one_active)
    assert any("皮提亚筹谋" in line and "进入【筹谋酝酿】" in line for line in round_one_active)

    assert any("德尔斐蓄谕【神谕吟诵】进度 1/1" in line for line in round_two_active)
    assert any("皮提亚筹谋【筹谋酝酿】进度 1/1" in line for line in round_two_active)
    assert any("德尔斐蓄谕 准备完成，释放" in line for line in round_two_active)
    assert any("皮提亚筹谋 准备完成，释放" in line for line in round_two_active)
    assert "德尔斐蓄谕落咒" in logs
    assert "皮提亚筹谋落策" in logs

    delphi_summary = _skill_summary(result, "delphi_charged_oracle")
    pythia_summary = _skill_summary(result, "pythia_woven_scheme")
    assert delphi_summary["success_count"] == 1
    assert pythia_summary["success_count"] == 1

    round_one_preparing_fails = [
        event
        for event in result.event_stream
        if event.event_type == EventType.TRIGGER_FAIL
        and event.payload.get("reason") == "ACTIVE_PREPARING"
    ]
    assert not round_one_preparing_fails

    round_two_preparing_fails = [
        event
        for event in result.event_stream
        if event.round_no == 2
        and event.event_type == EventType.TRIGGER_FAIL
        and event.payload.get("reason") == "ACTIVE_PREPARING"
    ]
    assert not round_two_preparing_fails


def test_ming_lock_interrupts_delphi_charged_preparation() -> None:
    result = run_battle(build_interrupt_input(seed=12))
    logs = "\n".join(result.human_logs)

    print_and_save_output(
        "test_ming_lock_interrupts_delphi_charged_preparation",
        format_battle_result("Delphi Charged Interrupt", result),
    )

    assert "进入【神谕吟诵】" in logs or "进入神谕吟诵" in logs
    assert "已被打断" in logs
    assert "CONTROL_INTERRUPT" in logs

    summary = _skill_summary(result, "delphi_charged_oracle")
    assert summary["success_count"] == 0
    assert "CONTROL_FORBID_ACTIVE" in logs


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
