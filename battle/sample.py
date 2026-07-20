from __future__ import annotations

"""示例入口：跑一场演示战斗，打印人类可读过程摘要，并落盘完整 JSON 战报。

用法（在仓库根目录执行，python battle/sample.py 或 python -m battle.sample）：
    python battle/sample.py                     # 默认 standard（B3 标杆武将 3v3）
    python battle/sample.py --scenario oracle   # B3：神谕连携 + 犹豫 + 单挑演示
    python battle/sample.py --scenario skills   # B2：测试战法/状态/DoT/治疗/暴击
    python battle/sample.py --scenario 3v3      # B1：纯普攻
    python battle/sample.py --scenario 1v1 --seed 42
    python battle/sample.py --scenario stalemate    # 7 局系列平局演示
    python battle/sample.py --scenario npc          # 30000 兵超编 NPC 演示
    python battle/sample.py --mode brief            # 主干日志（默认 all 全量）

战报 JSON 输出到 battle/out/<scenario>_seed<seed>.json。
文字日志（中文战法名，粒度见 battle/textlog.py）输出到同名 .txt。
"""

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from battle import serialize_report, simulate
from battle.roster import hero_setup
from battle.setup import BattleSetup, HeroSetup, TeamSetup
from battle.textlog import MODES, format_report, safe_print


# ---------------------------------------------------------------- 演示阵容

def _hero(hero_id: str, position: int, *, force: int, intelligence: int = 70,
          command: int, speed: int, max_troops: int = 10000,
          initial_troops: int | None = None, skills: tuple[str, ...] = (),
          crit_rate_bps: int = 0, heal_crit_rate_bps: int = 0) -> HeroSetup:
    return HeroSetup(
        hero_id=hero_id, template_id=f"tpl_{hero_id}", position=position,
        force=force, intelligence=intelligence, command=command, speed=speed,
        max_troops=max_troops, initial_troops=initial_troops, skills=skills,
        crit_rate_bps=crit_rate_bps, heal_crit_rate_bps=heal_crit_rate_bps,
    )


def scenario_3v3() -> BattleSetup:
    return BattleSetup(
        battle_id="sample_3v3",
        teams=(
            TeamSetup(team_id="A", main_hero_id="宙斯", heroes=(
                _hero("宙斯", 0, force=90, intelligence=95, command=110, speed=85),
                _hero("阿波罗", 1, force=85, intelligence=50, command=30, speed=95),
                _hero("哈迪斯", 2, force=80, intelligence=90, command=100, speed=95),
            )),
            TeamSetup(team_id="B", main_hero_id="阿瑞斯", heroes=(
                _hero("阿瑞斯", 0, force=105, intelligence=40, command=85, speed=88),
                _hero("赫拉克勒斯", 1, force=100, intelligence=35, command=90, speed=75),
                _hero("美杜莎", 2, force=60, intelligence=95, command=70, speed=92),
            )),
        ),
    )


def scenario_1v1() -> BattleSetup:
    return BattleSetup(
        battle_id="sample_1v1",
        teams=(
            TeamSetup(team_id="A", main_hero_id="阿喀琉斯", heroes=(
                _hero("阿喀琉斯", 0, force=98, command=85, speed=93),)),
            TeamSetup(team_id="B", main_hero_id="赫克托尔", heroes=(
                _hero("赫克托尔", 0, force=92, command=90, speed=86),)),
        ),
    )


def scenario_stalemate() -> BattleSetup:
    def tank(hero_id: str, position: int) -> HeroSetup:
        return _hero(hero_id, position, force=10, command=300, speed=80)
    return BattleSetup(
        battle_id="sample_stalemate",
        teams=(
            TeamSetup(team_id="A", main_hero_id="铁壁甲", heroes=(tank("铁壁甲", 0), tank("铁壁乙", 1))),
            TeamSetup(team_id="B", main_hero_id="磐石甲", heroes=(tank("磐石甲", 0), tank("磐石乙", 1))),
        ),
    )


def scenario_npc() -> BattleSetup:
    return BattleSetup(
        battle_id="sample_npc",
        teams=(
            TeamSetup(team_id="A", main_hero_id="提丰", heroes=(
                _hero("提丰", 0, force=120, command=120, speed=100,
                      max_troops=30000, initial_troops=25000),)),
            TeamSetup(team_id="B", main_hero_id="珀尔修斯", heroes=(
                _hero("珀尔修斯", 0, force=90, command=85, speed=90),
                _hero("雅典娜卫", 1, force=85, command=95, speed=85),
                _hero("赫尔墨斯", 2, force=75, command=70, speed=99),
            )),
        ),
    )


def scenario_skills() -> BattleSetup:
    """B2 演示：六个测试战法覆盖伤害/治疗/DoT/增益叠层/控制/属性削弱。"""
    return BattleSetup(
        battle_id="sample_skills",
        teams=(
            TeamSetup(team_id="A", main_hero_id="宙斯", heroes=(
                _hero("宙斯", 0, force=90, intelligence=95, command=100, speed=85,
                      skills=("test_war_cry", "test_blast"), crit_rate_bps=1500),
                _hero("阿斯克勒庇俄斯", 1, force=60, intelligence=115, command=95, speed=80,
                      skills=("test_mend",), heal_crit_rate_bps=2000),
                _hero("赫卡忒", 2, force=65, intelligence=100, command=85, speed=95,
                      skills=("test_poison",)),
            )),
            TeamSetup(team_id="B", main_hero_id="阿瑞斯", heroes=(
                _hero("阿瑞斯", 0, force=105, intelligence=40, command=88, speed=88,
                      skills=("test_disarm",)),
                _hero("喀耳刻", 1, force=70, intelligence=105, command=92, speed=82,
                      skills=("test_mend", "test_sap")),
                _hero("美杜莎", 2, force=60, intelligence=95, command=78, speed=92,
                      skills=("test_blast",)),
            )),
        ),
    )


def scenario_standard() -> BattleSetup:
    """v3.1 标杆验收阵容：宙斯（雷霆）+ 阿喀琉斯（暴击追伤）+ 蛇杖 对
    哈迪斯（冥域）+ 赫拉克勒斯（试炼）+ 美杜莎（石化凝视）。
    覆盖：单挑（阿喀琉斯 vs 赫拉克勒斯）、神谕、被动、追击、性格。"""
    return BattleSetup(
        battle_id="sample_standard",
        teams=(
            TeamSetup(team_id="A", main_hero_id="宙斯", heroes=(
                hero_setup("zeus", hero_id="宙斯", position=0, extra_skills=("zeus_bolt",)),
                hero_setup("achilles", hero_id="阿喀琉斯", position=1,
                           extra_skills=("achilles_thrust",)),
                hero_setup("asclepius", hero_id="阿斯克勒庇俄斯", position=2),
            )),
            TeamSetup(team_id="B", main_hero_id="哈迪斯", heroes=(
                hero_setup("hades", hero_id="哈迪斯", position=0,
                           extra_skills=("hades_soul_drain",)),
                hero_setup("heracles", hero_id="赫拉克勒斯", position=1),
                hero_setup("medusa", hero_id="美杜莎", position=2,
                           extra_skills=("medusa_glance",)),
            )),
        ),
    )


def scenario_oracle() -> BattleSetup:
    """神谕连携演示：波塞冬主将（三叉戟神谕）+ 两名主动自带副将 → 准备回合连携；
    对面赫尔墨斯神谕全场撒犹豫 + 阿瑞斯血战。"""
    return BattleSetup(
        battle_id="sample_oracle",
        teams=(
            TeamSetup(team_id="A", main_hero_id="波塞冬", heroes=(
                hero_setup("poseidon", hero_id="波塞冬", position=0),
                hero_setup("siren", hero_id="塞壬", position=1),
                hero_setup("amphitrite", hero_id="安菲特里忒", position=2),
            )),
            TeamSetup(team_id="B", main_hero_id="赫尔墨斯", heroes=(
                hero_setup("hermes", hero_id="赫尔墨斯", position=0),
                hero_setup("ares", hero_id="阿瑞斯", position=1),
                hero_setup("apollo", hero_id="阿波罗", position=2),
            )),
        ),
    )


def scenario_sea_underworld() -> BattleSetup:
    """海 vs 冥：震荡链/格挡 对 吸取/处决（v4：死神镰痕改即发单体处决）。"""
    return BattleSetup(
        battle_id="sample_sea_underworld",
        teams=(
            TeamSetup(team_id="A", main_hero_id="波塞冬", heroes=(
                hero_setup("poseidon", hero_id="波塞冬", position=0,
                           extra_skills=("poseidon_torrent",)),
                hero_setup("triton", hero_id="特里同", position=1),
                hero_setup("scylla", hero_id="斯库拉", position=2),
            )),
            TeamSetup(team_id="B", main_hero_id="哈迪斯", heroes=(
                hero_setup("hades", hero_id="哈迪斯", position=0),
                hero_setup("thanatos", hero_id="塔纳托斯", position=1),
                hero_setup("cerberus", hero_id="刻耳柏洛斯", position=2,
                           extra_skills=("cerberus_guard",)),
            )),
        ),
    )


def scenario_men_gods() -> BattleSetup:
    """人 vs 神：暴击/追加/准备（赫克托尔战吼）对 反制/先攻/收割。"""
    return BattleSetup(
        battle_id="sample_men_gods",
        teams=(
            TeamSetup(team_id="A", main_hero_id="阿喀琉斯", heroes=(
                hero_setup("achilles", hero_id="阿喀琉斯", position=0,
                           extra_skills=("achilles_thrust",)),
                hero_setup("hector", hero_id="赫克托尔", position=1),
                hero_setup("ajax", hero_id="大埃阿斯", position=2),
            )),
            TeamSetup(team_id="B", main_hero_id="雅典娜", heroes=(
                hero_setup("athena", hero_id="雅典娜", position=0),
                hero_setup("artemis", hero_id="阿尔忒弥斯", position=1),
                hero_setup("nike", hero_id="尼刻", position=2),
            )),
        ),
    )


def scenario_burst_tactics() -> BattleSetup:
    """Phase 4 演示：连发 + 预设战术（B 批播放验收入口用）。

    - 连发源：赫克托尔（忠烈：施放自带 +15%/层连发率，最多 2 层）、
      特里同（忠勇：波塞冬存活时海嗣号角连发率 +30%）。
    - 预设战术（经理人 P4-C，不发 tactic_applied 事件）：
      A 队集火哈迪斯（受击 ×2）、B 队攻势倾向 +1。"""
    return BattleSetup(
        battle_id="sample_burst_tactics",
        teams=(
            TeamSetup(team_id="A", main_hero_id="波塞冬", heroes=(
                hero_setup("poseidon", hero_id="波塞冬", position=0),
                hero_setup("triton", hero_id="特里同", position=1),
                hero_setup("hector", hero_id="赫克托尔", position=2),
            )),
            TeamSetup(team_id="B", main_hero_id="哈迪斯", heroes=(
                hero_setup("hades", hero_id="哈迪斯", position=0),
                hero_setup("heracles", hero_id="赫拉克勒斯", position=1),
                hero_setup("medusa", hero_id="美杜莎", position=2),
            )),
        ),
        metadata={"tactics": {
            "preset": {
                "A": {"tactic_id": "focus_fire", "params": {"target_id": "哈迪斯"}},
                "B": {"tactic_id": "stance", "params": {"level": 1}},
            },
            "changes": [],
        }},
    )


SCENARIOS = {
    "standard": scenario_standard,
    "burst_tactics": scenario_burst_tactics,
    "oracle": scenario_oracle,
    "sea_underworld": scenario_sea_underworld,
    "men_gods": scenario_men_gods,
    "3v3": scenario_3v3,
    "skills": scenario_skills,
    "1v1": scenario_1v1,
    "stalemate": scenario_stalemate,
    "npc": scenario_npc,
}


# ---------------------------------------------------------------- 入口

def main() -> None:
    parser = argparse.ArgumentParser(description="battle 示例战斗")
    parser.add_argument("--scenario", choices=sorted(SCENARIOS), default="standard")
    parser.add_argument("--seed", type=int, default=20260705)
    parser.add_argument("--mode", choices=MODES, default="all",
                        help="日志粒度：brief=主干 / all=全量（默认）")
    args = parser.parse_args()

    setup = SCENARIOS[args.scenario]()
    report = simulate(setup, seed=args.seed)

    # 可读战报同时写入 UTF-8 文本文件（Windows 控制台 GBK 代码页下中文会乱码，
    # 以 .txt 文件为准；控制台想正常显示可先执行 chcp 65001）
    text = format_report(report, mode=args.mode)
    safe_print(text)

    out_dir = Path(__file__).parent / "out"
    out_dir.mkdir(exist_ok=True)
    json_path = out_dir / f"{args.scenario}_seed{args.seed}.json"
    txt_path = out_dir / f"{args.scenario}_seed{args.seed}_{args.mode}.txt"
    json_path.write_text(serialize_report(report), encoding="utf-8")
    txt_path.write_text(text, encoding="utf-8")
    total_events = sum(len(game["events"]) for game in report["games"])
    size_kb = json_path.stat().st_size / 1024
    print(f"\n完整 JSON 战报: {json_path}（{total_events} 事件, {size_kb:.1f} KB）")
    print(f"可读文字战报:   {txt_path}（{args.mode} 模式）")


if __name__ == "__main__":
    main()
