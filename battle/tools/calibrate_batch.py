"""数值标定批量脚本：千场统计 A/B 标定队伍对战。

用法（仓库根目录）：
    python battle/tools/calibrate_batch.py --a pure --b regular_mid
    python battle/tools/calibrate_batch.py --a regular_low --b regular_high --n 1000 --troops 10000
    python battle/tools/calibrate_batch.py --a pure --b pure --attr mid --json battle/out/cal.json
    python battle/tools/calibrate_batch.py --a pure --b regular_mid --attr-a low --attr-b high

队伍 kind（battle/cal_teams.py）：
    pure            纯兵，无技能
    regular_low     常规·低减伤（全队 10%）+ 中档主动/追击/被动
    regular_mid     常规·中减伤（全队 25%）+ 中档伤害填充
    regular_high    常规·高减伤（全队 40%）+ 中档伤害填充

属性档（全维同值）：high=300 / mid=200 / low=100（默认 mid；可用 --attr-a/b 分队）

输出：平均结束回合、A/B 死伤、各武将各技能平均释放次数/伤害量。
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

from battle import simulate
from battle.cal_teams import ATTR_TIER_IDS, ATTR_TIERS, TEAM_KINDS, build_cal_setup
from battle.names import skill_name
from battle.skills_cal import CAL_PASSIVE_STATUS_TO_SKILL
from battle.textlog import safe_print


def _index_events(events: list[dict]) -> dict[int, dict]:
    return {e["seq"]: e for e in events}


def _resolve_skill_key(ev: dict, by_seq: dict[int, dict]) -> tuple[str, str]:
    """沿 parent_seq 上溯到 skill_trigger / normal_attack / status_tick。
    返回 (source_hero_id, skill_key)。"""
    cur = ev
    for _ in range(64):
        t = cur["type"]
        p = cur.get("payload") or {}
        if t == "skill_trigger":
            return p.get("actor_id", ""), p.get("skill_id", "unknown")
        if t == "normal_attack":
            return p.get("actor_id", ""), "basic_attack"
        if t == "status_tick":
            status = p.get("status") or {}
            sid = status.get("status_id", "")
            skill = CAL_PASSIVE_STATUS_TO_SKILL.get(sid, sid or "status")
            return p.get("source_id") or status.get("owner_id", ""), skill
        parent = cur.get("parent_seq") or 0
        if parent <= 0 or parent not in by_seq:
            break
        cur = by_seq[parent]
    src = (ev.get("payload") or {}).get("source_id", "")
    return src, "unknown"


def aggregate_game(events: list[dict]) -> dict[tuple[str, str], dict[str, int]]:
    """单局：(hero_id, skill_key) → {triggers, damage}。"""
    by_seq = _index_events(events)
    rows: dict[tuple[str, str], dict[str, int]] = defaultdict(
        lambda: {"triggers": 0, "damage": 0}
    )
    for ev in events:
        t = ev["type"]
        p = ev.get("payload") or {}
        if t == "skill_trigger":
            # prepare/release/assist 计释放；skip/interrupted 不计；
            # 标定被动的「释放」用 status_tick 计（每回合伤害一次），此处跳过
            kind = p.get("kind", "cast")
            if kind in ("skip", "interrupted"):
                continue
            hero = p.get("actor_id", "")
            skill = p.get("skill_id", "")
            if skill in CAL_PASSIVE_STATUS_TO_SKILL.values():
                continue
            if hero and skill:
                rows[(hero, skill)]["triggers"] += 1
        elif t == "normal_attack":
            hero = p.get("actor_id", "")
            if hero:
                rows[(hero, "basic_attack")]["triggers"] += 1
        elif t == "status_tick":
            status = p.get("status") or {}
            sid = status.get("status_id", "")
            if sid in CAL_PASSIVE_STATUS_TO_SKILL:
                hero = p.get("source_id") or status.get("owner_id", "")
                skill = CAL_PASSIVE_STATUS_TO_SKILL[sid]
                if hero:
                    rows[(hero, skill)]["triggers"] += 1
        elif t == "damage":
            amount = int(p.get("amount") or 0)
            if amount <= 0:
                continue
            if p.get("mitigation") in ("block", "evade"):
                continue
            hero, skill = _resolve_skill_key(ev, by_seq)
            if hero:
                rows[(hero, skill)]["damage"] += amount
    return rows


def run_batch(
    team_a: str,
    team_b: str,
    *,
    n: int = 1000,
    troops: int = 10000,
    attr_tier: str = "mid",
    attr_tier_a: str | None = None,
    attr_tier_b: str | None = None,
    seed_start: int = 0,
) -> dict[str, Any]:
    a_tier = attr_tier_a or attr_tier
    b_tier = attr_tier_b or attr_tier
    setup = build_cal_setup(
        team_a, team_b, troops=troops,
        attr_tier_a=a_tier, attr_tier_b=b_tier,
    )
    # 预登记英雄/队伍映射
    hero_team = {
        h.hero_id: team.team_id
        for team in setup.teams
        for h in team.heroes
    }
    hero_skills = {
        h.hero_id: list(h.skills) + ["basic_attack"]
        for team in setup.teams
        for h in team.heroes
    }

    end_rounds: list[int] = []
    wins: dict[str, int] = defaultdict(int)
    draws = 0
    # team → dead / wounded 累加
    team_dead: dict[str, int] = defaultdict(int)
    team_wounded: dict[str, int] = defaultdict(int)
    team_remain: dict[str, int] = defaultdict(int)
    # (hero, skill) → triggers / damage 累加
    skill_triggers: dict[tuple[str, str], int] = defaultdict(int)
    skill_damage: dict[tuple[str, str], int] = defaultdict(int)

    started = time.perf_counter()
    for i in range(n):
        report = simulate(setup, seed=seed_start + i)
        result = report["result"]
        winner = result["winner_team_id"]
        if winner is None:
            draws += 1
        else:
            wins[winner] += 1

        # 取最后一局结束回合与死伤
        last = report["games"][-1]
        end_rounds.append(int(last["result"]["end_round"]))
        for entry in last["result"]["troops"]:
            hid = entry["hero_id"]
            tid = hero_team[hid]
            team_dead[tid] += int(entry.get("dead_after", 0))
            team_wounded[tid] += int(entry.get("wounded_after", 0))
            team_remain[tid] += int(entry.get("troops_after", 0))

        # 技能统计：系列全部局合计（通常 1 局）
        for game in report["games"]:
            rows = aggregate_game(game["events"])
            for key, vals in rows.items():
                skill_triggers[key] += vals["triggers"]
                skill_damage[key] += vals["damage"]

    elapsed = time.perf_counter() - started
    n_f = float(n)

    heroes_out: dict[str, Any] = {}
    for hid, skills in hero_skills.items():
        skill_rows = []
        # 已装配 + 实际出现过的 key
        seen = set(skills)
        for (h, sk) in skill_triggers:
            if h == hid:
                seen.add(sk)
        for sk in sorted(seen, key=lambda s: (0 if s in skills else 1, skills.index(s) if s in skills else 99, s)):
            trig = skill_triggers.get((hid, sk), 0)
            dmg = skill_damage.get((hid, sk), 0)
            if trig == 0 and dmg == 0 and sk not in (hero_skills[hid]):
                continue
            skill_rows.append({
                "skill_id": sk,
                "name": skill_name(sk) if sk != "basic_attack" else "普攻",
                "avg_triggers": round(trig / n_f, 3),
                "avg_damage": round(dmg / n_f, 1),
            })
        heroes_out[hid] = {
            "team": hero_team[hid],
            "skills": skill_rows,
        }

    return {
        "team_a": team_a,
        "team_b": team_b,
        "troops": troops,
        "attr_a": a_tier,
        "attr_b": b_tier,
        "attr_value_a": ATTR_TIERS[a_tier],
        "attr_value_b": ATTR_TIERS[b_tier],
        "n": n,
        "seed_range": [seed_start, seed_start + n],
        "elapsed_sec": round(elapsed, 3),
        "battles_per_sec": round(n / elapsed, 1) if elapsed > 0 else None,
        "avg_end_round": round(sum(end_rounds) / n_f, 3),
        "win_rate": {
            "A": {"wins": wins.get("A", 0), "rate_pct": round(100 * wins.get("A", 0) / n_f, 2)},
            "B": {"wins": wins.get("B", 0), "rate_pct": round(100 * wins.get("B", 0) / n_f, 2)},
            "draw": {"wins": draws, "rate_pct": round(100 * draws / n_f, 2)},
        },
        "teams": {
            "A": {
                "avg_dead": round(team_dead["A"] / n_f, 1),
                "avg_wounded": round(team_wounded["A"] / n_f, 1),
                "avg_remain": round(team_remain["A"] / n_f, 1),
            },
            "B": {
                "avg_dead": round(team_dead["B"] / n_f, 1),
                "avg_wounded": round(team_wounded["B"] / n_f, 1),
                "avg_remain": round(team_remain["B"] / n_f, 1),
            },
        },
        "heroes": heroes_out,
    }


def format_report(batch: dict[str, Any]) -> str:
    lines = [
        f"=== 标定批量 [{batch['team_a']} vs {batch['team_b']}] "
        f"兵力={batch['troops']} "
        f"属性 A={batch['attr_a']}({batch['attr_value_a']}) "
        f"B={batch['attr_b']}({batch['attr_value_b']}) "
        f"× {batch['n']} 场 "
        f"（种子 {batch['seed_range'][0]}..{batch['seed_range'][1] - 1}，"
        f"{batch['elapsed_sec']}s，{batch['battles_per_sec']} 场/秒）===",
        f"  平均结束回合: {batch['avg_end_round']}",
        "  胜率: " + "  ".join(
            f"{k}={v['wins']}({v['rate_pct']}%)"
            for k, v in batch["win_rate"].items()
        ),
        f"  A 死伤余: 死 {batch['teams']['A']['avg_dead']} / "
        f"伤 {batch['teams']['A']['avg_wounded']} / "
        f"余 {batch['teams']['A']['avg_remain']}",
        f"  B 死伤余: 死 {batch['teams']['B']['avg_dead']} / "
        f"伤 {batch['teams']['B']['avg_wounded']} / "
        f"余 {batch['teams']['B']['avg_remain']}",
        "  ---- 各武将技能 ----",
    ]
    for hid, info in batch["heroes"].items():
        lines.append(f"  [{info['team']}] {hid}")
        for row in info["skills"]:
            if row["avg_triggers"] == 0 and row["avg_damage"] == 0:
                continue
            lines.append(
                f"    {row['name']}({row['skill_id']}): "
                f"均释放 {row['avg_triggers']} / 均伤害 {row['avg_damage']}"
            )
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description="数值标定千场批量统计")
    parser.add_argument("--a", required=True, choices=TEAM_KINDS, help="A 队 kind")
    parser.add_argument("--b", required=True, choices=TEAM_KINDS, help="B 队 kind")
    parser.add_argument("--n", type=int, default=1000, help="场次（默认 1000）")
    parser.add_argument("--troops", type=int, default=10000, help="每将兵力（默认 10000）")
    parser.add_argument(
        "--attr", choices=ATTR_TIER_IDS, default="mid",
        help="双方属性档 high=300/mid=200/low=100（默认 mid）",
    )
    parser.add_argument(
        "--attr-a", choices=ATTR_TIER_IDS, default=None,
        help="A 队属性档（覆盖 --attr）",
    )
    parser.add_argument(
        "--attr-b", choices=ATTR_TIER_IDS, default=None,
        help="B 队属性档（覆盖 --attr）",
    )
    parser.add_argument("--seed", type=int, default=0, help="起始种子（默认 0）")
    parser.add_argument("--json", type=str, default="", help="可选：落盘 JSON 路径")
    args = parser.parse_args()

    batch = run_batch(
        args.a, args.b, n=args.n, troops=args.troops,
        attr_tier=args.attr, attr_tier_a=args.attr_a, attr_tier_b=args.attr_b,
        seed_start=args.seed,
    )
    text = format_report(batch)
    safe_print(text)
    if args.json:
        path = Path(args.json)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(batch, ensure_ascii=False, indent=2), encoding="utf-8")
        safe_print(f"\nJSON: {path}")


if __name__ == "__main__":
    main()
