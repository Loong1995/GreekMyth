from __future__ import annotations

"""战报组装与序列化（严格符合 docs/schema/battle_events.schema.json）。"""

import json
from typing import Any

from battle.engine import SeriesEngine
from battle.setup import BattleSetup
from battle.skill_catalog import build_skill_catalog
from battle.version import CORE_VERSION, SCHEMA_VERSION


def build_team_snapshots(setup: BattleSetup) -> list[dict[str, Any]]:
    snapshots = []
    for team in sorted(setup.teams, key=lambda t: t.team_id):
        snapshots.append(
            {
                "team_id": team.team_id,
                "main_hero_id": team.main_hero_id,
                "heroes": [
                    {
                        "hero_id": hero.hero_id,
                        "template_id": hero.template_id,
                        "position": hero.position,
                        "force": hero.force,
                        "intelligence": hero.intelligence,
                        "command": hero.command,
                        "speed": hero.speed,
                        "max_troops": hero.max_troops,
                        "initial_troops": hero.resolved_initial_troops(),
                        "skills": list(hero.skills),
                        # 1.3.0 加法字段：使快照可无损还原 HeroSetup（客服重放闭环）
                        "crit_rate_bps": hero.crit_rate_bps,
                        "heal_crit_rate_bps": hero.heal_crit_rate_bps,
                        "trait_id": hero.trait_id,
                        "gender": hero.gender,
                        "level": hero.level,
                    }
                    for hero in sorted(team.heroes, key=lambda h: h.position)
                ],
            }
        )
    return snapshots


def build_report(setup: BattleSetup, seed: int, engine: SeriesEngine, series: dict[str, Any]) -> dict[str, Any]:
    games = []
    for game_result, events in zip(engine.game_results, engine.writer.games_events()):
        games.append(
            {
                "game_no": game_result["game_no"],
                "events": events,
                "result": {
                    "winner_team_id": game_result["winner_team_id"],
                    "reason": game_result["reason"],
                    "end_round": game_result["end_round"],
                    "troops": game_result["troops"],
                },
            }
        )

    stats = [
        {
            "hero_id": hero_id,
            "total_damage": engine.heroes[hero_id].total_damage,
            "total_heal": engine.heroes[hero_id].total_heal,
            "kills": engine.heroes[hero_id].kills,
            "final_troops": engine.heroes[hero_id].troops,
        }
        for hero_id in engine.hero_order
    ]

    return {
        # 下划线开头键为调试侧信道（不进契约、序列化时剥除，golden 不受影响）
        "_debug_rolls": engine.debug_rolls,
        "schema_version": SCHEMA_VERSION,
        "core_version": CORE_VERSION,
        "battle_id": setup.battle_id,
        "rng_seed": seed,
        # 1.3.0 加法字段：影响结算的 setup.metadata（如 trait_rate_overrides），重放必需
        "setup_metadata": dict(setup.metadata),
        # 1.5.0 加法字段：出场战法标签目录（定义期声明，客户端播放层直读）
        "skill_catalog": build_skill_catalog(setup),
        "teams": build_team_snapshots(setup),
        "games": games,
        "result": {
            "winner_team_id": series["winner_team_id"],
            "total_games": series["total_games"],
            "reason": series["reason"],
            "game_summaries": [
                {"game_no": game["game_no"], "winner_team_id": game["result"]["winner_team_id"]}
                for game in games
            ],
            "stats": stats,
        },
    }


def serialize_report(report: dict[str, Any]) -> str:
    """规范序列化：紧凑、保持插入序、非 ASCII 原样输出。逐字节确定性以此为准。
    下划线开头的顶层键（调试侧信道）剥除，不进契约 JSON。"""
    public = {k: v for k, v in report.items() if not k.startswith("_")}
    return json.dumps(public, ensure_ascii=False, separators=(",", ":"))
