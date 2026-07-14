from __future__ import annotations

"""标杆武将验收演示战（2026-07-05 人工指定阵容）：

  A 队（主将宙斯）  ：宙斯 + 阿喀琉斯 + 阿瑞斯
  B 队（主将哈迪斯）：哈迪斯 + 赫尔墨斯（犹豫神）+ 阿斯克勒庇俄斯（蛇杖神）

装配覆盖要求的战法类别：
  暴击类：阿喀琉斯之怒（暴击率+20%+暴击追伤）、战神怒火、试·战吼
  控制类：试·噤声（缄默，可打断蓄谕）、试·夺械（缴械）、赫尔墨斯神谕（犹豫延迟）
  持续伤害类：试·剧毒（DoT，双方各带一个）
  另有：雷霆神谕（暴击可与落雷连锁）、冥域君临（吸血+减伤+汲智）、蛇杖庇护（受击回复）、
        试·蓄能新星（准备型爆发）、怒火突刺（追击）、试·连击操练（连击）、试·愈合（治疗）

直接执行输出中文战斗日志文本（brief 主干 + all 全量两份，落盘 battle/out/）：
    python battle/tests/test_showcase_gods.py                  # 控制台打印 brief
    python battle/tests/test_showcase_gods.py --mode all       # 控制台打印全量
    python battle/tests/test_showcase_gods.py --seed 42
用 pytest 跑本文件则执行下方机制覆盖断言。
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.roster import hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.textlog import format_report, safe_print

DEFAULT_SEED = 20260705


def showcase_setup() -> BattleSetup:
    team_a = TeamSetup(
        team_id="A",
        main_hero_id="宙斯",
        heroes=(
            hero_setup("zeus", hero_id="宙斯", position=0,
                       extra_skills=("test_poison", "zeus_bolt")),
            hero_setup("achilles", hero_id="阿喀琉斯", position=1,
                       extra_skills=("achilles_thrust", "test_war_cry")),
            hero_setup("ares", hero_id="阿瑞斯", position=2,
                       extra_skills=("test_combo_drill",)),
        ),
    )
    team_b = TeamSetup(
        team_id="B",
        main_hero_id="哈迪斯",
        heroes=(
            hero_setup("hades", hero_id="哈迪斯", position=0,
                       extra_skills=("test_charged_nova", "test_silence")),
            hero_setup("hermes", hero_id="赫尔墨斯", position=1,
                       extra_skills=("test_disarm",)),
            hero_setup("asclepius", hero_id="阿斯克勒庇俄斯", position=2,
                       extra_skills=("test_mend", "test_poison")),
        ),
    )
    return BattleSetup(battle_id="showcase_gods", teams=(team_a, team_b))


# ------------------------------------------------------------------ 断言

def _all_events(report: dict):
    for game in report["games"]:
        yield from game["events"]


def test_showcase_covers_crit_control_dot_and_more():
    """机制覆盖（10 个种子并集）：暴击、怒击追伤、控制、犹豫、DoT、准备型、治疗。"""
    seen: set[str] = set()
    for seed in range(1, 11):
        report = simulate(showcase_setup(), seed=seed)
        for event in _all_events(report):
            p = event["payload"]
            if event["type"] == "damage" and p["is_crit"]:
                seen.add("crit")
            elif event["type"] == "status_tick" and p["status"]["status_id"] == "achilles_wrath":
                seen.add("fury")
            elif event["type"] == "status_apply":
                sid = p["status"]["status_id"]
                if sid in ("ming_lock", "silence", "disarm"):
                    seen.add("control")
                elif sid == "hesitation":
                    seen.add("hesitation")
                elif sid == "test_poison_status":
                    seen.add("dot_apply")
            elif (event["type"] == "status_tick"
                  and p["status"]["status_id"] == "test_poison_status"):
                seen.add("dot_tick")
            elif event["type"] == "skill_trigger" and p["kind"] == "prepare":
                seen.add("prepare")
            elif event["type"] == "heal":
                seen.add("heal")
    missing = {"crit", "fury", "control", "hesitation", "dot_apply",
               "dot_tick", "prepare", "heal"} - seen
    assert not missing, f"10 个种子并集仍未覆盖机制: {missing}"


def test_log_prints_chinese_names_in_both_modes():
    report = simulate(showcase_setup(), seed=DEFAULT_SEED)
    text_all = format_report(report, mode="all")
    text_brief = format_report(report, mode="brief")
    for name in ("阿喀琉斯之怒", "雷霆神谕", "冥域君临"):
        assert name in text_all, f"全量日志缺中文战法名: {name}"
        assert name in text_brief, f"brief 日志缺中文战法名: {name}"
    # 已登记 id 不得以原文出现在日志（中文化生效）
    for raw_id in ("achilles_wrath", "thunder_oracle", "ming_lock"):
        assert raw_id not in text_all
    # brief 是 all 的严格子集粒度：更短，且不含细节层事件
    assert len(text_brief) < len(text_all)
    assert "伤兵损耗" in text_all and "伤兵损耗" not in text_brief
    assert "系列结果" in text_brief


# ------------------------------------------------------------------ 直接执行：输出战斗日志

def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description="标杆武将验收演示战（日志输出）")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED)
    parser.add_argument("--mode", choices=("brief", "all"), default="brief",
                        help="控制台打印粒度（brief/all 两份文本都会落盘）")
    args = parser.parse_args()

    report = simulate(showcase_setup(), seed=args.seed)
    texts = {mode: format_report(report, mode=mode) for mode in ("brief", "all")}
    safe_print(texts[args.mode])

    out_dir = Path(__file__).resolve().parents[1] / "out"
    out_dir.mkdir(exist_ok=True)
    json_path = out_dir / f"showcase_gods_seed{args.seed}.json"
    json_path.write_text(serialize_report(report), encoding="utf-8")
    print(f"\n完整 JSON 战报: {json_path}")
    for mode, text in texts.items():
        txt_path = out_dir / f"showcase_gods_seed{args.seed}_{mode}.txt"
        txt_path.write_text(text, encoding="utf-8")
        print(f"文字日志（{mode}）: {txt_path}")


if __name__ == "__main__":
    main()
