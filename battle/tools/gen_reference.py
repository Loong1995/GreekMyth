from __future__ import annotations

"""gen_reference：生成人工审核参考物（Phase 3 §七，非机器 golden）。

- reference/golden/：关键场景战报 JSON + human log（brief）各一份；
- reference/characters/<template_id>/：24 武将逐一性格演示 human log
  （性格判定概率全量拉满 10000 bps、50 级、10000 兵，对面固定神/冥混编配角）。

用法（仓库根目录）：python battle/tools/gen_reference.py
输出目录：reference/（可整目录提交或人工翻阅后丢弃）。
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.roster import ROSTER, hero_setup
from battle.sample import SCENARIOS
from battle.setup import BattleSetup, TeamSetup
from battle.textlog import format_report

ROOT = Path(__file__).resolve().parents[2] / "reference"

# 性格判定全部拉满：每 trait 的全部 rate key（与 battle/traits.py 对齐）
TRAIT_RATE_KEYS = (
    "distract", "lapse", "extra_action", "wild", "backline", "flip", "pride",
    "heel", "boost", "taunt", "immune", "overthink", "apple", "fear",
    "stubborn", "rage", "offkey", "double", "mirror", "bloom",
)

REFERENCE_SCENARIOS = (
    ("standard", 20260705),
    ("oracle", 5),
    ("sea_underworld", 9),
    ("men_gods", 12),
)


def high_rate_overrides() -> dict[str, int]:
    return {
        f"{template.trait_id}.{key}": 10000
        for template in ROSTER.values() if template.trait_id
        for key in TRAIT_RATE_KEYS
    }


def character_setup(template_id: str) -> BattleSetup:
    """主角 + 两名同阵营配角 vs 固定对面（神/冥混编），性格概率拉满。"""
    template = ROSTER[template_id]
    allies = [t for t in ROSTER.values()
              if t.faction == template.faction and t.template_id != template_id][:2]
    enemies = [t for t in ROSTER.values()
               if t.template_id not in (template_id, *(a.template_id for a in allies))
               and t.faction != template.faction][:3]
    team_a = TeamSetup(
        team_id="A", main_hero_id=template.name,
        heroes=tuple(
            hero_setup(t.template_id, hero_id=t.name, position=i)
            for i, t in enumerate((template, *allies))
        ),
    )
    team_b = TeamSetup(
        team_id="B", main_hero_id=enemies[0].name,
        heroes=tuple(
            hero_setup(t.template_id, hero_id=t.name, position=i)
            for i, t in enumerate(enemies)
        ),
    )
    return BattleSetup(
        battle_id=f"ref_{template_id}",
        teams=(team_a, team_b),
        metadata={"trait_rate_overrides": high_rate_overrides()},
    )


def main() -> None:
    golden_dir = ROOT / "golden"
    golden_dir.mkdir(parents=True, exist_ok=True)
    for scenario, seed in REFERENCE_SCENARIOS:
        report = simulate(SCENARIOS[scenario](), seed=seed)
        stem = f"{scenario}_seed{seed}"
        (golden_dir / f"{stem}.json").write_text(
            serialize_report(report), encoding="utf-8")
        (golden_dir / f"{stem}.txt").write_text(
            format_report(report, mode="brief"), encoding="utf-8")
        print(f"  reference/golden/{stem}.json + .txt")

    for template_id, template in ROSTER.items():
        hero_dir = ROOT / "characters" / template_id
        hero_dir.mkdir(parents=True, exist_ok=True)
        report = simulate(character_setup(template_id), seed=7)
        (hero_dir / "battle_log.txt").write_text(
            format_report(report, mode="all"), encoding="utf-8")
        traits = [e for g in report["games"] for e in g["events"]
                  if e["type"] == "trait_trigger"
                  and e["payload"]["hero_id"] == template.name]
        summary = [f"{template.name}（{template_id}）性格 {template.trait_id}：",
                   f"trait_trigger 触发 {len(traits)} 次（概率拉满 seed=7）"]
        summary += [f"  r{e['t']['r']} {e['payload']['effect']}: {e['payload']['line']}"
                    for e in traits[:10]]
        (hero_dir / "summary.txt").write_text("\n".join(summary), encoding="utf-8")
        print(f"  reference/characters/{template_id}/ trait_trigger×{len(traits)}")

    print("完成。人工翻阅 reference/ 验收。")


if __name__ == "__main__":
    main()
