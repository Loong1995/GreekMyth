"""数值等价统计验证（任务书 4.3 / Step B2 验收项）。

方法：同一套 3v3 纯普攻阵容（新旧 core 唯一都实现的机制交集），同一批种子
分别在旧 core（battlecore，只读引用）与新 core（battle）各跑 N 场，对比：
  1. A 队胜率 / B 队胜率 / 平局率；
  2. 场均总伤害（双方合计）；
  3. 场均剩余兵力（双方合计）。

两边 RNG 消费序不同（架构重写），单场不可能逐值一致，比较的是**分布统计**。
判定容差：胜率差 ≤ 4 个百分点；场均伤害/剩余兵力相对差 ≤ 2%。

口径对齐：
- 旧 core 单场 8 回合封顶，回合耗尽按剩余兵力多者胜（totals 相等为平）；
- 新 core 取系列第 1 局，round_limit 平局时按同规则事后判定（仅用于对比）。

直接运行：python battle/tools/numeric_equivalence.py [--battles 1000]
结论落档：docs/dev/numeric_equivalence.md
"""

import argparse
import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(_REPO_ROOT))
sys.path.insert(0, str(_REPO_ROOT / "battlecore"))

from battle import simulate  # noqa: E402
from battle.setup import BattleSetup, HeroSetup, TeamSetup  # noqa: E402

from battlecore.api.battle_runner import run_battle  # noqa: E402
from battlecore.config.config_db import build_demo_config_db  # noqa: E402
from battlecore.config.schema import BattleInput, HeroConfig  # noqa: E402
from battlecore.domain.enums import HeroRole  # noqa: E402

# 统一阵容（带少量不对称，让胜率偏离 50% 更有区分度）
LINEUP = {
    "A": [
        ("a1", 0, dict(force=95, intelligence=70, command=90, speed=88)),
        ("a2", 1, dict(force=85, intelligence=80, command=95, speed=80)),
        ("a3", 2, dict(force=75, intelligence=90, command=85, speed=95)),
    ],
    "B": [
        ("b1", 0, dict(force=94, intelligence=75, command=90, speed=90)),
        ("b2", 1, dict(force=86, intelligence=85, command=94, speed=82)),
        ("b3", 2, dict(force=78, intelligence=95, command=84, speed=99)),
    ],
}
MAIN = {"A": "a1", "B": "b1"}
MAX_TROOPS = 10000


def new_core_setup() -> BattleSetup:
    teams = []
    for team_id, rows in LINEUP.items():
        heroes = tuple(
            HeroSetup(hero_id=hid, template_id=f"tpl_{hid}", position=pos,
                      max_troops=MAX_TROOPS, **attrs)
            for hid, pos, attrs in rows
        )
        teams.append(TeamSetup(team_id=team_id, main_hero_id=MAIN[team_id], heroes=heroes))
    return BattleSetup(battle_id="equiv", teams=tuple(teams))


def old_core_heroes(team_id: str) -> list[HeroConfig]:
    heroes = []
    for hid, pos, attrs in LINEUP[team_id]:
        heroes.append(HeroConfig(
            hero_id=hid, name=hid, team_id=team_id,
            role=HeroRole.MAIN if hid == MAIN[team_id] else HeroRole.DEPUTY,
            position=pos, max_troops=MAX_TROOPS,
            skill_ids=["basic_attack"], **attrs,
        ))
    return heroes


def run_old(seed: int, config_db) -> dict:
    result = run_battle(
        BattleInput(battle_id=f"old_{seed}", seed=seed, max_rounds=8,
                    team_a_heroes=old_core_heroes("A"),
                    team_b_heroes=old_core_heroes("B"),
                    config_version=config_db.version),
        config_db,
    )
    summary = result.summary
    troops = {"A": 0, "B": 0}
    damage = {"A": 0, "B": 0}
    for hero in summary.hero_summaries:
        troops[hero["team_id"]] += hero["troops"]
        damage[hero["team_id"]] += hero["damage_dealt"]
    return {"winner": summary.winner_team_id, "troops": troops, "damage": damage}


def run_new(seed: int, setup: BattleSetup) -> dict:
    report = simulate(setup, seed=seed)
    game1 = report["games"][0]
    troops = {"A": 0, "B": 0}
    team_of = {hid: tid for tid, rows in LINEUP.items() for hid, _, _ in rows}
    for entry in game1["result"]["troops"]:
        troops[team_of[entry["hero_id"]]] += entry["troops_after"]
    damage = {"A": 0, "B": 0}
    for event in game1["events"]:
        if event["type"] == "damage":
            damage[team_of[event["payload"]["source_id"]]] += event["payload"]["amount"]
    winner = game1["result"]["winner_team_id"]
    if winner is None:  # 与旧 core finish_by_remaining_troops 同口径事后判定
        if troops["A"] != troops["B"]:
            winner = "A" if troops["A"] > troops["B"] else "B"
    return {"winner": winner, "troops": troops, "damage": damage}


def aggregate(rows: list[dict]) -> dict:
    n = len(rows)
    return {
        "win_a": sum(1 for r in rows if r["winner"] == "A") / n,
        "win_b": sum(1 for r in rows if r["winner"] == "B") / n,
        "draw": sum(1 for r in rows if r["winner"] is None) / n,
        "mean_damage": sum(r["damage"]["A"] + r["damage"]["B"] for r in rows) / n,
        "mean_troops": sum(r["troops"]["A"] + r["troops"]["B"] for r in rows) / n,
    }


def rel_diff(a: float, b: float) -> float:
    return abs(a - b) / max(abs(a), abs(b), 1e-9)


def main() -> int:
    parser = argparse.ArgumentParser(description="新旧 core 数值等价统计验证")
    parser.add_argument("--battles", type=int, default=1000)
    args = parser.parse_args()
    seeds = range(1, args.battles + 1)

    config_db = build_demo_config_db()
    setup = new_core_setup()

    old_rows, new_rows = [], []
    for index, seed in enumerate(seeds, 1):
        old_rows.append(run_old(seed, config_db))
        new_rows.append(run_new(seed, setup))
        if index % 200 == 0:
            print(f"  ... {index}/{args.battles}")

    old_stats, new_stats = aggregate(old_rows), aggregate(new_rows)

    lines = [f"对局数: {args.battles}（种子 1..{args.battles}，双核同种子批）", ""]
    lines.append(f"{'指标':<14}{'旧core':>14}{'新core':>14}{'差异':>12}")
    checks = []
    for key, label, kind in [
        ("win_a", "A队胜率", "pp"), ("win_b", "B队胜率", "pp"), ("draw", "平局率", "pp"),
        ("mean_damage", "场均总伤害", "rel"), ("mean_troops", "场均剩余兵力", "rel"),
    ]:
        o, n = old_stats[key], new_stats[key]
        if kind == "pp":
            diff = abs(o - n) * 100
            ok = diff <= 4.0
            lines.append(f"{label:<14}{o:>13.1%}{n:>13.1%}{diff:>10.2f}pp  {'OK' if ok else 'FAIL'}")
        else:
            diff = rel_diff(o, n)
            ok = diff <= 0.02
            lines.append(f"{label:<14}{o:>14.1f}{n:>14.1f}{diff:>11.2%}  {'OK' if ok else 'FAIL'}")
        checks.append(ok)

    verdict = "PASS：统计等价成立（容差内）" if all(checks) else "FAIL：存在超出容差的统计偏移"
    lines += ["", f"结论: {verdict}"]
    text = "\n".join(lines)

    out = Path(__file__).parent / "out"
    out.mkdir(exist_ok=True)
    (out / "numeric_equivalence_result.txt").write_text(text + "\n", encoding="utf-8")
    try:
        print(text)
    except UnicodeEncodeError:
        print(text.encode("gbk", errors="replace").decode("gbk"))
    print(f"\n结果已存: {out / 'numeric_equivalence_result.txt'}")
    return 0 if all(checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
