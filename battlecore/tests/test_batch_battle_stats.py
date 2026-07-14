import _path_bootstrap  # noqa: F401

from battlecore.api.battle_runner import run_battle
from battlecore.domain.enums import EventType
from _output_helper import print_and_save_output
from test_oracle_skills import build_oracle_input


def _pct(numerator: int, denominator: int) -> str:
    if denominator <= 0:
        return "0.00%"
    return f"{numerator * 100 / denominator:.2f}%"


def _new_trigger_stats() -> dict[str, int | None]:
    return {
        "success": 0,
        "probability_fail": 0,
        "rule_fail": 0,
        "always_trigger": 0,
        "guarantee_trigger": 0,
        "panel_rate_bps": None,
    }


def _collect_trigger_stats(result, skill_stats: dict[str, dict], spy_state_stats: dict[str, dict]) -> None:
    spy_state_ids = {
        state["state_instance_id"]
        for state in result.summary.state_summaries
        if state.get("listen_event_types")
    }
    for event in result.event_stream:
        if event.event_type not in (EventType.TRIGGER_SUCCESS, EventType.TRIGGER_FAIL):
            continue
        if event.source_type == "SKILL":
            key = event.skill_id or event.source_id or "UNKNOWN_SKILL"
            bucket = skill_stats.setdefault(key, _new_trigger_stats())
        elif event.source_type == "STATE" and event.state_instance_id in spy_state_ids:
            key = event.source_id or event.state_instance_id or "UNKNOWN_STATE"
            bucket = spy_state_stats.setdefault(key, _new_trigger_stats())
        else:
            continue

        panel_rate_bps = event.payload.get("base_rate_bps")
        if panel_rate_bps is not None:
            bucket["panel_rate_bps"] = int(panel_rate_bps)

        reason = event.payload.get("reason")
        if event.event_type == EventType.TRIGGER_SUCCESS:
            bucket["success"] += 1
            if reason == "ALWAYS_TRIGGER":
                bucket["always_trigger"] += 1
            if reason == "GUARANTEE_TRIGGER" or event.payload.get("guarantee_triggered"):
                bucket["guarantee_trigger"] += 1
        elif event.payload.get("failure_kind") == "PROBABILITY":
            bucket["probability_fail"] += 1
        else:
            bucket["rule_fail"] += 1


def _append_trigger_lines(lines: list[str], title: str, stats_by_id: dict[str, dict]) -> None:
    lines.extend([title])
    if not stats_by_id:
        lines.extend(["无", ""])
        return
    for trigger_id, stats in sorted(stats_by_id.items()):
        judged = int(stats["success"]) + int(stats["probability_fail"])
        total = judged + int(stats["rule_fail"])
        panel_rate = stats["panel_rate_bps"]
        panel_rate_text = "N/A" if panel_rate is None else _pct(int(panel_rate), 10000)
        lines.extend(
            [
                f"[{trigger_id}]",
                f"面板概率={panel_rate_text}",
                f"总检查次数={total}",
                f"进入概率判定次数={judged}",
                f"成功次数={stats['success']}",
                f"概率失败次数={stats['probability_fail']}",
                f"规则失败次数={stats['rule_fail']}",
                f"必定触发次数={stats['always_trigger']}",
                f"保底触发次数={stats['guarantee_trigger']}",
                f"总机会生效率={_pct(int(stats['success']), total)}",
                f"排除规则后的判定成功率={_pct(int(stats['success']), judged)}",
                "",
            ]
        )


def test_batch_battle_stats_for_team_strength() -> None:
    battle_count = 1000
    stats = {
        "team_a": {
            "wins": 0,
            "main_deaths": 0,
            "dead_troop": 0,
            "wounded_troop": 0,
            "remaining_troop": 0,
        },
        "team_b": {
            "wins": 0,
            "main_deaths": 0,
            "dead_troop": 0,
            "wounded_troop": 0,
            "remaining_troop": 0,
        },
        "draws": 0,
        "finish_reasons": {},
    }
    skill_stats: dict[str, dict] = {}
    spy_state_stats: dict[str, dict] = {}

    for seed in range(1, battle_count + 1):
        result = run_battle(build_oracle_input(seed=seed))
        summary = result.summary
        _collect_trigger_stats(result, skill_stats, spy_state_stats)
        if summary.winner_team_id in ("team_a", "team_b"):
            stats[summary.winner_team_id]["wins"] += 1
        else:
            stats["draws"] += 1
        stats["finish_reasons"][summary.finish_reason] = stats["finish_reasons"].get(summary.finish_reason, 0) + 1

        for hero in summary.hero_summaries:
            team = hero["team_id"]
            stats[team]["dead_troop"] += int(hero.get("dead_troop", 0))
            stats[team]["wounded_troop"] += int(hero.get("wounded_troop", 0))
            stats[team]["remaining_troop"] += int(hero.get("current_troop", hero.get("troops", 0)))
            if hero["role"] == "MAIN" and hero["exited"]:
                stats[team]["main_deaths"] += 1

    lines = [
        "=== 批量战斗队伍强度统计 ===",
        f"战斗场次={battle_count}",
        "",
    ]
    for team_id in ("team_a", "team_b"):
        team = stats[team_id]
        lines.extend(
            [
                f"[{team_id}]",
                f"胜场={team['wins']}",
                f"胜率={_pct(team['wins'], battle_count)}",
                f"主将死亡次数={team['main_deaths']}",
                f"主将死亡率={_pct(team['main_deaths'], battle_count)}",
                f"平均阵亡={team['dead_troop'] / battle_count:.2f}",
                f"平均伤兵={team['wounded_troop'] / battle_count:.2f}",
                f"平均剩余兵力={team['remaining_troop'] / battle_count:.2f}",
                "",
            ]
        )
    lines.append(f"平局场次={stats['draws']}")
    lines.append("结束原因分布=" + ", ".join(f"{k}:{v}" for k, v in sorted(stats["finish_reasons"].items())))
    lines.append("")
    _append_trigger_lines(lines, "=== Skill 批量实际生效概率 ===", skill_stats)
    _append_trigger_lines(lines, "=== SPY State 批量实际生效概率 ===", spy_state_stats)

    print_and_save_output("test_batch_battle_stats_for_team_strength", "\n".join(lines))

    assert stats["team_a"]["wins"] + stats["team_b"]["wins"] + stats["draws"] == battle_count


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
