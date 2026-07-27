"""client_battle_bridge：客户端手动配阵页（ManualSetupPanel）专用桥接。

Unity 通过命令行子进程调用，三种模式：

    # 1) 导出武将/战法目录（配阵页备选池 + 详情数据）
    python battle/tools/client_battle_bridge.py --catalog --out catalog.json

    # 2) 单场对战：读配阵 config（manual_battle 同构），写战报 JSON 供客户端播放
    python battle/tools/client_battle_bridge.py --config cfg.json --seed 7 --out report.json

    # 3) 百场统计：跑 n 场，输出 calibrate_batch 同风格统计 JSON
    python battle/tools/client_battle_bridge.py --config cfg.json --n 100 --seed 0 --stats-out stats.json

config 结构同 manual_battle.py --example；跨队同模板武将自动改名「XX（敌）」
（hero_id 全局唯一约束，同 test_manual_3v3）。
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from collections import defaultdict
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.names import STATUS_NAMES, skill_name
from battle.skill_catalog import catalog_entry
from battle.roster import DEFAULT_LEVEL, MAX_EXTRA_SKILLS, ROSTER, hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.skills import (
    REGISTRY,
    TIMING_ACTIVE,
    TIMING_PREPARE,
    TIMING_PURSUIT,
)
from battle.tools.calibrate_batch import aggregate_game

TIMING_LABEL = {
    TIMING_ACTIVE: "主动",
    TIMING_PREPARE: "被动/准备",
    TIMING_PURSUIT: "追击",
}

FACTION_LABEL = {
    "olympus": "奥林匹斯",
    "heroes": "英雄",
    "sea": "海域",
    "underworld": "冥界",
}


# ---------------------------------------------------------------- 目录导出

def skill_info(skill_id: str) -> dict[str, Any]:
    """配阵页展示条目＝skill_catalog 条目 + UI 附加字段（复用同一真源）。"""
    sk = REGISTRY[skill_id]
    info = {"skill_id": skill_id, **catalog_entry(skill_id)}
    info["timing"] = TIMING_LABEL.get(sk.timing, sk.timing)
    info["trigger_rate_bps"] = sk.trigger_rate_bps
    return info


def build_catalog() -> dict[str, Any]:
    """配阵页数据：全部武将（50 级面板）+ 可装配战法池 + 全战法详情。"""
    innate = {t.innate_skill_id for t in ROSTER.values()}
    hidden = {s for t in ROSTER.values() for s in t.hidden_skills}
    pool = sorted(
        k for k in REGISTRY
        if not k.startswith(("cal_", "test_"))
        and k not in innate and k not in hidden and k != "basic_attack"
    )
    heroes = []
    for t in ROSTER.values():
        heroes.append({
            "template_id": t.template_id,
            "name": t.name,
            "faction": t.faction,
            "faction_name": FACTION_LABEL.get(t.faction, t.faction),
            "gender": t.gender,
            "trait_id": t.trait_id,
            "force": t.attr_at(t.force, DEFAULT_LEVEL),
            "intelligence": t.attr_at(t.intelligence, DEFAULT_LEVEL),
            "command": t.attr_at(t.command, DEFAULT_LEVEL),
            "speed": t.attr_at(t.speed, DEFAULT_LEVEL),
            "innate_skill": t.innate_skill_id,
            "hidden_skills": list(t.hidden_skills),
        })
    used = sorted(innate | hidden | set(pool))
    return {
        "level": DEFAULT_LEVEL,
        "max_extra_skills": MAX_EXTRA_SKILLS,
        "heroes": heroes,
        "skill_pool": pool,
        "skills": {sid: skill_info(sid) for sid in used},
    }


# ---------------------------------------------------------------- 配阵构建

def build_setup_from_config(config: dict) -> BattleSetup:
    """同 manual_battle.build_setup，另加跨队同模板改名「（敌）」。

    每位英雄可带 ``position``（1~6；缺省按数组序 1..n，兼容旧 config 的
    隐式 0..n-1 由 hero_setup 写入前在此显式化）。也可在队级提供
    ``positions: [1,4,2]`` 与 heroes 等长，优先于英雄字段。
    """
    seen_ids: set[str] = set()
    teams = []
    for i, tcfg in enumerate(config["teams"]):
        team_id = tcfg.get("team_id", "AB"[i])
        heroes_cfg = tcfg["heroes"]
        team_positions = tcfg.get("positions")
        if team_positions is not None and len(team_positions) != len(heroes_cfg):
            raise ValueError(
                f"team {team_id}: positions 长度须与 heroes 一致"
                f"（{len(team_positions)} vs {len(heroes_cfg)}）"
            )
        setups = []
        for idx, h in enumerate(heroes_cfg):
            template = ROSTER[h["template"]]
            hero_id = h.get("hero_id", template.name)
            while hero_id in seen_ids:
                hero_id += "（敌）"
            seen_ids.add(hero_id)
            if team_positions is not None:
                pos = int(team_positions[idx])
            elif "position" in h:
                pos = int(h["position"])
            else:
                # 缺省：按出现序占前排 1..n（新口径）；不再写 0..n-1
                pos = idx + 1
            setups.append(hero_setup(
                h["template"],
                hero_id=hero_id,
                position=pos,
                extra_skills=tuple(h.get("extra_skills", ())),
                level=h.get("level", DEFAULT_LEVEL),
                max_troops=h.get("max_troops", 10000),
                initial_troops=h.get("initial_troops"),
            ))
        teams.append(TeamSetup(
            team_id=team_id, main_hero_id=setups[0].hero_id,
            heroes=tuple(setups),
        ))
    return BattleSetup(
        battle_id=config.get("battle_id", "manual_ui"),
        teams=tuple(teams),
    )


# ---------------------------------------------------------------- 百场统计

def _display_name(skill_key: str) -> str:
    """统计行显示名：战法名 → 状态名（DOT 等 status_tick 归因）→ 原 id。"""
    if skill_key == "basic_attack":
        return "普攻"
    name = skill_name(skill_key)
    if name == skill_key and skill_key in STATUS_NAMES:
        return STATUS_NAMES[skill_key] + "（持续）"
    return name


def run_stats(setup_config: dict, *, n: int, seed_start: int) -> dict[str, Any]:
    setup = build_setup_from_config(setup_config)
    hero_team = {
        h.hero_id: team.team_id
        for team in setup.teams for h in team.heroes
    }
    hero_order = [h.hero_id for team in setup.teams for h in team.heroes]

    end_rounds: list[int] = []
    wins: dict[str, int] = defaultdict(int)
    draws = 0
    team_dead: dict[str, int] = defaultdict(int)
    team_wounded: dict[str, int] = defaultdict(int)
    team_remain: dict[str, int] = defaultdict(int)
    skill_triggers: dict[tuple[str, str], int] = defaultdict(int)
    skill_damage: dict[tuple[str, str], int] = defaultdict(int)

    started = time.perf_counter()
    for i in range(n):
        report = simulate(setup, seed=seed_start + i)
        winner = report["result"]["winner_team_id"]
        if winner is None:
            draws += 1
        else:
            wins[winner] += 1
        last = report["games"][-1]
        end_rounds.append(int(last["result"]["end_round"]))
        for entry in last["result"]["troops"]:
            tid = hero_team[entry["hero_id"]]
            team_dead[tid] += int(entry.get("dead_after", 0))
            team_wounded[tid] += int(entry.get("wounded_after", 0))
            team_remain[tid] += int(entry.get("troops_after", 0))
        for game in report["games"]:
            for key, vals in aggregate_game(game["events"]).items():
                skill_triggers[key] += vals["triggers"]
                skill_damage[key] += vals["damage"]

    elapsed = time.perf_counter() - started
    n_f = float(n)

    heroes_out = []
    for hid in hero_order:
        keys = sorted({sk for (h, sk) in set(skill_triggers) | set(skill_damage) if h == hid})
        rows = [{
            "skill_id": sk,
            "name": _display_name(sk),
            "avg_triggers": round(skill_triggers.get((hid, sk), 0) / n_f, 2),
            "avg_damage": round(skill_damage.get((hid, sk), 0) / n_f, 1),
        } for sk in keys]
        rows.sort(key=lambda r: -r["avg_damage"])
        heroes_out.append({"hero_id": hid, "team": hero_team[hid], "rows": rows})

    team_ids = [t.team_id for t in setup.teams]
    return {
        "n": n,
        "seed_start": seed_start,
        "elapsed_sec": round(elapsed, 2),
        "avg_end_round": round(sum(end_rounds) / n_f, 2),
        "win_rate": {
            **{tid: {"wins": wins[tid], "rate_pct": round(wins[tid] * 100 / n_f, 1)}
               for tid in team_ids},
            "draw": {"wins": draws, "rate_pct": round(draws * 100 / n_f, 1)},
        },
        "teams": {
            tid: {
                "avg_dead": round(team_dead[tid] / n_f, 1),
                "avg_wounded": round(team_wounded[tid] / n_f, 1),
                "avg_remain": round(team_remain[tid] / n_f, 1),
            } for tid in team_ids
        },
        "heroes": heroes_out,
    }


# ---------------------------------------------------------------- CLI

def _write(path: str, payload: str) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(payload, encoding="utf-8")
    print(f"OK {p}")


def main() -> None:
    parser = argparse.ArgumentParser(description="客户端手动配阵页桥接")
    parser.add_argument("--catalog", action="store_true", help="导出武将/战法目录")
    parser.add_argument("--config", help="配阵 JSON（结构同 manual_battle --example）")
    parser.add_argument("--n", type=int, default=1, help="场次；>1 时输出统计")
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--out", help="输出路径（目录/单场战报）")
    parser.add_argument("--stats-out", help="统计 JSON 输出路径（n>1）")
    args = parser.parse_args()

    if args.catalog:
        payload = json.dumps(build_catalog(), ensure_ascii=False, indent=1)
        if args.out:
            _write(args.out, payload)
        else:
            print(payload)
        return

    if not args.config:
        parser.error("需要 --catalog 或 --config")
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))

    if args.n > 1:
        stats = run_stats(config, n=args.n, seed_start=args.seed)
        out = args.stats_out or args.out
        if not out:
            parser.error("n>1 需要 --stats-out")
        _write(out, json.dumps(stats, ensure_ascii=False, indent=1))
        return

    setup = build_setup_from_config(config)
    report = simulate(setup, seed=args.seed)
    if not args.out:
        parser.error("单场需要 --out")
    _write(args.out, serialize_report(report))


if __name__ == "__main__":
    main()
