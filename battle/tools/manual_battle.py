from __future__ import annotations

"""manual_battle：手动测试入口（Phase 3 §七）。

命令行或 JSON 配置任意双方阵容（模板武将、等级、额外战法、性格概率覆盖、种子），
输出完整战报 JSON + 人类可读日志到 battle/out/manual/。

用法（仓库根目录）：
    # 命令行快速配阵：--a/--b 逗号分隔，元素 = 模板id[:等级][+额外战法…]
    python battle/tools/manual_battle.py --a zeus+zeus_bolt,achilles:40 --b hades,medusa

    # JSON 配置（结构见 --example 输出）
    python battle/tools/manual_battle.py --config my_battle.json --seed 42

    # 打印可用武将/战法清单
    python battle/tools/manual_battle.py --list

    # 性格高概率测试：--trait-override haozhan.extra_action=10000
    python battle/tools/manual_battle.py --a ares --b hades \
        --trait-override haozhan.extra_action=10000
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.names import skill_name
from battle.roster import DEFAULT_LEVEL, ROSTER, hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.textlog import MODES, format_report, safe_print

OUT_DIR = Path(__file__).resolve().parents[1] / "out" / "manual"

EXAMPLE_CONFIG = {
    "battle_id": "manual_demo",
    "seed": 7,
    "trait_rate_overrides": {"haozhan.extra_action": 10000},
    "teams": [
        {"team_id": "A", "heroes": [
            {"template": "zeus", "level": 50, "extra_skills": ["zeus_bolt"]},
            {"template": "achilles", "extra_skills": ["achilles_thrust"]},
        ]},
        {"team_id": "B", "heroes": [
            {"template": "hades", "extra_skills": ["hades_soul_drain"]},
            {"template": "medusa"},
        ]},
    ],
}


def parse_cli_hero(spec: str, position: int) -> dict:
    """`模板id[:等级][+额外战法]*` → hero dict。如 achilles:40+achilles_thrust。"""
    parts = spec.split("+")
    head, extras = parts[0], parts[1:]
    template, _, level_s = head.partition(":")
    return {
        "template": template.strip(),
        "level": int(level_s) if level_s else DEFAULT_LEVEL,
        "extra_skills": [e.strip() for e in extras if e.strip()],
    }


def build_team(team_id: str, heroes: list[dict],
               positions: list[int] | None = None,
               formation: str = "") -> TeamSetup:
    """heroes 为 config 条目；可选 positions 与 heroes 等长（1~6）；
    可选 formation（阵型 id，battle/formations.py 注册表）。
    缺省每位用 h['position']，再缺省按序 1..n（前排起）。"""
    if positions is not None and len(positions) != len(heroes):
        raise ValueError(
            f"team {team_id}: positions 长度须与 heroes 一致"
        )
    setups = []
    for idx, h in enumerate(heroes):
        template = ROSTER[h["template"]]
        if positions is not None:
            pos = int(positions[idx])
        elif "position" in h:
            pos = int(h["position"])
        else:
            pos = idx + 1
        setups.append(hero_setup(
            h["template"],
            hero_id=h.get("hero_id", template.name),
            position=pos,
            extra_skills=tuple(h.get("extra_skills", ())),
            level=h.get("level", DEFAULT_LEVEL),
            max_troops=h.get("max_troops", 10000),
            initial_troops=h.get("initial_troops"),
        ))
    return TeamSetup(team_id=team_id, main_hero_id=setups[0].hero_id,
                     heroes=tuple(setups), formation=formation)


def build_setup(config: dict) -> BattleSetup:
    teams = tuple(
        build_team(
            t.get("team_id", "AB"[i]),
            t["heroes"],
            positions=t.get("positions"),
            formation=t.get("formation", ""),
        )
        for i, t in enumerate(config["teams"])
    )
    metadata = {}
    if config.get("trait_rate_overrides"):
        metadata["trait_rate_overrides"] = {
            k: int(v) for k, v in config["trait_rate_overrides"].items()
        }
    return BattleSetup(
        battle_id=config.get("battle_id", "manual"),
        teams=teams,
        metadata=metadata,
    )


def print_roster() -> None:
    print(f"{'模板id':<14} {'名字':<10} {'阵营':<11} {'性格':<10} 自带战法")
    print("-" * 72)
    for t in ROSTER.values():
        print(f"{t.template_id:<14} {t.name:<10} {t.faction:<11} "
              f"{t.trait_id:<10} {t.innate_skill_id}（{skill_name(t.innate_skill_id)}）")


def main() -> None:
    parser = argparse.ArgumentParser(description="手动配阵战斗（战报+日志输出）")
    parser.add_argument("--a", help="A 队：模板id[:等级][+额外战法]*，逗号分隔")
    parser.add_argument("--b", help="B 队：同 --a")
    parser.add_argument("--config", help="JSON 配置文件路径（结构见 --example）")
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--mode", choices=MODES, default="brief")
    parser.add_argument("--trait-override", action="append", default=[],
                        metavar="trait_id.key=bps", help="性格概率覆盖，可多次")
    parser.add_argument("--list", action="store_true", help="打印武将清单")
    parser.add_argument("--example", action="store_true", help="打印 JSON 配置样例")
    args = parser.parse_args()

    if args.list:
        print_roster()
        return
    if args.example:
        print(json.dumps(EXAMPLE_CONFIG, ensure_ascii=False, indent=2))
        return

    if args.config:
        config = json.loads(Path(args.config).read_text(encoding="utf-8"))
        seed = args.seed if args.seed != 7 else config.get("seed", args.seed)
    elif args.a and args.b:
        config = {
            "battle_id": "manual_cli",
            "teams": [
                {"team_id": "A",
                 "heroes": [parse_cli_hero(s, i) for i, s in enumerate(args.a.split(","))]},
                {"team_id": "B",
                 "heroes": [parse_cli_hero(s, i) for i, s in enumerate(args.b.split(","))]},
            ],
        }
        seed = args.seed
    else:
        parser.error("需要 --a/--b 或 --config（--list 查武将，--example 查配置样例）")
        return

    overrides = dict(config.get("trait_rate_overrides", {}))
    for item in args.trait_override:
        key, _, value = item.partition("=")
        overrides[key.strip()] = int(value)
    if overrides:
        config["trait_rate_overrides"] = overrides

    setup = build_setup(config)
    report = simulate(setup, seed=seed)
    text = format_report(report, mode=args.mode)
    safe_print(text)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    stem = f"{setup.battle_id}_seed{seed}"
    json_path = OUT_DIR / f"{stem}.json"
    txt_path = OUT_DIR / f"{stem}_{args.mode}.txt"
    json_path.write_text(serialize_report(report), encoding="utf-8")
    txt_path.write_text(text, encoding="utf-8")
    print(f"\n战报 JSON: {json_path}")
    print(f"文字日志: {txt_path}")


if __name__ == "__main__":
    main()
