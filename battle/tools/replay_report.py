from __future__ import annotations

"""客服重放工具：玩家战报 JSON → 还原 BattleSetup → 重跑 → 逐字节校验 → 全量日志。

用法（仓库根目录）：
    python battle/tools/replay_report.py path/to/report.json            # 校验 + all 日志到 stdout
    python battle/tools/replay_report.py report.json --out out_dir      # 日志与重跑战报落盘
    python battle/tools/replay_report.py report.json --mode brief       # 主干日志

流程：
1. 从战报顶层与 teams 快照无损还原 BattleSetup（1.3.0 起快照含
   crit_rate_bps/heal_crit_rate_bps/trait_id/gender/level 与顶层 setup_metadata；
   更早版本战报缺这些字段时回退 roster 模板补齐并打印警告——仅当武将池未变时可靠）。
2. simulate(setup, rng_seed) 重跑，serialize_report 与玩家提交 JSON 逐字节比对：
   一致 = 确认就是该场战斗；不一致则报长度与首个差异偏移（多为 core_version 不匹配，
   需检出战报中 core_version 对应的代码版本重跑）。
3. 输出 all 模式文字日志（含技能掷点明细 ⚄/⊘ 调试侧信道），供排查玩家投诉。
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.setup import BattleSetup, HeroSetup, TeamSetup
from battle.textlog import format_report, safe_print
from battle.version import CORE_VERSION


def _hero_from_snapshot(snap: dict, warnings: list[str]) -> HeroSetup:
    missing = [k for k in ("trait_id", "gender", "level", "crit_rate_bps",
                           "heal_crit_rate_bps") if k not in snap]
    fallback: dict = {}
    if missing:
        # 1.3.0 之前的战报：回退当前 roster 模板补齐（武将池若已变更则不可靠）
        try:
            from battle.roster import ROSTER
            template = ROSTER[snap["template_id"]]
            fallback = {
                "trait_id": template.trait_id, "gender": template.gender,
                "crit_rate_bps": template.crit_rate_bps,
                "heal_crit_rate_bps": template.heal_crit_rate_bps,
            }
            warnings.append(
                f"{snap['hero_id']}: 快照缺 {missing}，按当前 roster 模板"
                f" {snap['template_id']} 回退补齐（旧版战报，结果可能不可靠）"
            )
        except KeyError:
            warnings.append(
                f"{snap['hero_id']}: 快照缺 {missing} 且模板 {snap['template_id']}"
                f" 不在当前 roster，按默认值补齐（大概率无法逐字节复现）"
            )
    return HeroSetup(
        hero_id=snap["hero_id"],
        template_id=snap["template_id"],
        position=snap["position"],
        force=snap["force"],
        intelligence=snap["intelligence"],
        command=snap["command"],
        speed=snap["speed"],
        max_troops=snap["max_troops"],
        initial_troops=snap["initial_troops"],
        skills=tuple(snap["skills"]),
        crit_rate_bps=snap.get("crit_rate_bps", fallback.get("crit_rate_bps", 0)),
        heal_crit_rate_bps=snap.get(
            "heal_crit_rate_bps", fallback.get("heal_crit_rate_bps", 0)),
        trait_id=snap.get("trait_id", fallback.get("trait_id", "")),
        gender=snap.get("gender", fallback.get("gender", "m")),
        level=snap.get("level", 50),
    )


def setup_from_report(report: dict, warnings: list[str]) -> BattleSetup:
    teams = tuple(
        TeamSetup(
            team_id=team["team_id"],
            main_hero_id=team["main_hero_id"],
            heroes=tuple(_hero_from_snapshot(h, warnings) for h in team["heroes"]),
        )
        for team in report["teams"]
    )
    if "setup_metadata" not in report:
        warnings.append("战报缺顶层 setup_metadata（1.3.0 前旧版），按空 metadata 重放；"
                        "若原战斗用了 trait_rate_overrides 将无法复现")
    return BattleSetup(
        battle_id=report["battle_id"],
        teams=teams,
        metadata=dict(report.get("setup_metadata", {})),
    )


def replay(report_path: Path, *, mode: str = "all",
           out_dir: Path | None = None) -> bool:
    """返回是否逐字节一致。日志无论一致与否都会输出（供排查）。"""
    original_text = report_path.read_text(encoding="utf-8").strip()
    original = json.loads(original_text)

    print(f"== 重放 {original['battle_id']} | seed={original['rng_seed']} | "
          f"战报 core={original['core_version']} / 本机 core={CORE_VERSION} ==")
    if original["core_version"] != CORE_VERSION:
        print("!! core 版本不匹配：逐字节复现只对同 core_version 成立，"
              "请检出对应版本代码重跑")

    warnings: list[str] = []
    setup = setup_from_report(original, warnings)
    for w in warnings:
        print(f"!! {w}")

    replayed = simulate(setup, seed=original["rng_seed"])
    replayed_text = serialize_report(replayed)
    # 玩家提交的 JSON 可能被重新格式化过：规范化后再比
    normalized_original = json.dumps(
        original, ensure_ascii=False, separators=(",", ":"))
    identical = replayed_text == normalized_original
    if identical:
        print("== 校验：重跑战报与玩家战报逐字节一致（确认为该场战斗） ==")
    else:
        diff_at = next(
            (i for i, (a, b) in enumerate(zip(normalized_original, replayed_text))
             if a != b),
            min(len(normalized_original), len(replayed_text)),
        )
        print(f"!! 校验失败：长度 {len(normalized_original)} -> {len(replayed_text)}，"
              f"首个差异偏移 {diff_at}。日志仍按重跑结果输出，仅供参考")

    text = format_report(replayed, mode=mode)
    if out_dir is not None:
        out_dir.mkdir(parents=True, exist_ok=True)
        stem = report_path.stem
        (out_dir / f"{stem}_replay_{mode}.txt").write_text(text, encoding="utf-8")
        (out_dir / f"{stem}_replay.json").write_text(replayed_text, encoding="utf-8")
        print(f"日志: {out_dir / f'{stem}_replay_{mode}.txt'}")
    else:
        safe_print(text)
    return identical


def main() -> None:
    parser = argparse.ArgumentParser(description="从玩家战报 JSON 重放并输出排查日志")
    parser.add_argument("report", type=Path, help="战报 JSON 路径")
    parser.add_argument("--mode", choices=("brief", "all"), default="all")
    parser.add_argument("--out", type=Path, default=None, help="落盘目录（默认打印 stdout）")
    args = parser.parse_args()
    ok = replay(args.report, mode=args.mode, out_dir=args.out)
    sys.exit(0 if ok else 2)


if __name__ == "__main__":
    main()
