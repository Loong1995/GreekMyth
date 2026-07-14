import _path_bootstrap  # noqa: F401

from battlecore.config.config_db import build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import HeroRole, Timing
from battlecore.engine.battle_context import BattleContext
from battlecore.engine.damage_calculator import get_effective_attr
from _output_helper import print_and_save_output


def _find_state(hero, state_config_id: str):
    for state in hero.states:
        if state.state_config_id == state_config_id:
            return state
    return None


def build_hades_drain_context(*, ally_command: int = 13) -> BattleContext:
    db = build_demo_config_db()
    battle_input = BattleInput(
        battle_id="hades_command_drain_test",
        seed=1,
        max_rounds=2,
        config_version=db.version,
        team_a_heroes=[
            HeroConfig("a_main", "A-Main", "team_a", HeroRole.MAIN, 1, 10000, 80, 70, 40, 90, ["basic_attack"]),
            HeroConfig("a_d1", "A-D1", "team_a", HeroRole.DEPUTY, 2, 10000, 80, 70, 40, 80, ["basic_attack"]),
            HeroConfig("a_d2", "A-D2", "team_a", HeroRole.DEPUTY, 3, 10000, 80, 70, 40, 70, ["basic_attack"]),
        ],
        team_b_heroes=[
            HeroConfig(
                "b_hades",
                "B-Hades",
                "team_b",
                HeroRole.MAIN,
                1,
                10000,
                80,
                70,
                100,
                90,
                ["hades_underworld_dominion"],
            ),
            HeroConfig(
                "b_ally",
                "B-Ally",
                "team_b",
                HeroRole.DEPUTY,
                2,
                10000,
                70,
                60,
                ally_command,
                70,
                ["basic_attack"],
            ),
            HeroConfig("b_tank", "B-Tank", "team_b", HeroRole.DEPUTY, 3, 10000, 70, 60, 0, 50, ["basic_attack"]),
        ],
    )
    context = BattleContext.build_from_input(battle_input, db)
    context.rebuild_indexes()
    context.current_timing = Timing.BEFORE_ACTION
    context.add_state(context.heroes["b_hades"], context.heroes["b_hades"], "hades_command_drain_state")
    return context


def _trigger_drain(context: BattleContext) -> None:
    hades = context.heroes["b_hades"]
    drain_state = _find_state(hades, "hades_command_drain_state")
    assert drain_state is not None
    drain_state.execute(context, None)


def _format_context_logs(title: str, context: BattleContext, *, summary_lines: list[str] | None = None) -> str:
    lines = [f"=== {title} ===", ""]
    if summary_lines:
        lines.extend(summary_lines)
        lines.append("")
    lines.append("=== Human Logs ===")
    lines.extend(context.human_logs or ["<no human logs>"])
    return "\n".join(lines)


def test_command_drain_stops_when_ally_command_reaches_zero_and_converts_to_force() -> None:
    context = build_hades_drain_context(ally_command=13)
    ally = context.heroes["b_ally"]
    hades = context.heroes["b_hades"]

    _trigger_drain(context)
    assert get_effective_attr(ally, "command") == 8
    assert get_effective_attr(hades, "force") == 85
    assert get_effective_attr(hades, "command") == 100

    _trigger_drain(context)
    assert get_effective_attr(ally, "command") == 3
    assert get_effective_attr(hades, "force") == 90

    _trigger_drain(context)
    assert get_effective_attr(ally, "command") == 0
    assert get_effective_attr(hades, "force") == 93

    _trigger_drain(context)
    assert get_effective_attr(ally, "command") == 0
    assert get_effective_attr(hades, "force") == 93

    print_and_save_output(
        "test_command_drain_stops_when_ally_command_reaches_zero",
        _format_context_logs(
            "Hades Sacrifice Force Demo",
            context,
            summary_lines=[
                "ally_base_command=13",
                "hades_base_force=80",
                "drain_per_action=5",
                "after_3_drains_ally_effective_command=0",
                "after_3_drains_hades_effective_force=93",
                "after_4th_drain_hades_force_unchanged=93",
            ],
        ),
    )


def test_ally_exit_keeps_hades_sacrifice_force() -> None:
    context = build_hades_drain_context(ally_command=100)
    hades = context.heroes["b_hades"]
    ally = context.heroes["b_ally"]

    _trigger_drain(context)
    _trigger_drain(context)
    sacrifice = _find_state(hades, "hades_force_gain_state")
    assert sacrifice is not None
    assert int(sacrifice.payload["force_delta"]) == 10
    assert get_effective_attr(hades, "force") == 90
    assert get_effective_attr(hades, "command") == 100

    context.mark_hero_exited(ally, reason="TEST_EXIT")

    sacrifice_after = _find_state(hades, "hades_force_gain_state")
    assert sacrifice_after is not None
    assert int(sacrifice_after.payload["force_delta"]) == 10
    assert get_effective_attr(hades, "force") == 90
    assert _find_state(ally, "hades_command_loss_state") is None
    assert not ally.states

    print_and_save_output(
        "test_ally_exit_keeps_hades_sacrifice_force",
        _format_context_logs(
            "Ally Exit Keeps Hades Sacrifice Force Demo",
            context,
            summary_lines=[
                "hades_force_before_exit=90",
                "hades_force_after_ally_exit=90",
                f"sacrifice_force_delta={sacrifice_after.payload['force_delta']}",
                "ally_states_after_exit=0",
            ],
        ),
    )


def test_hades_uses_gain_and_loss_states_for_cleanup_compatibility() -> None:
    context = build_hades_drain_context(ally_command=20)
    hades = context.heroes["b_hades"]
    ally = context.heroes["b_ally"]

    _trigger_drain(context)

    gain_state = _find_state(hades, "hades_force_gain_state")
    loss_state = _find_state(ally, "hades_command_loss_state")

    assert gain_state is not None
    assert loss_state is not None
    assert gain_state.source_actor_id == hades.instance_id
    assert loss_state.source_actor_id == hades.instance_id
    assert int(gain_state.payload["force_delta"]) == 5
    assert int(loss_state.payload["command_delta"]) == -5


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
