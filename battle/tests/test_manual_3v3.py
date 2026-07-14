from __future__ import annotations

"""手动 3v3 测试入口：直接改下方 TEAM_A / TEAM_B / SEED 即可换阵容跑仗。

每个英雄条目二选一写法：
  1. 池内武将（用 roster 模板 + 等级 + 额外战法）：
       {"template": "achilles", "level": 50, "extra_skills": ("achilles_thrust",)}
  2. 白板自定义（手填四维 + 任意战法组合，可用 test_* 测试原语）：
       {"hero_id": "自定义甲", "force": 95, "intelligence": 70, "command": 90,
        "speed": 88, "skills": ("test_blast", "test_mend"), "initial_troops": 8000}

可选公共键：max_troops（默认 10000）、initial_troops、hero_id（模板武将也可改名）。
列表首位 = 主将（主将阵亡判负）。

直接运行（仓库根目录）：
    python battle/tests/test_manual_3v3.py               # 打印 brief 日志
    python battle/tests/test_manual_3v3.py --mode all    # 全量日志
    python battle/tests/test_manual_3v3.py --seed 42
战报与日志落盘 battle/out/manual/。用 pytest 跑本文件则只做冒烟断言（战报能生成）。

武将模板清单：python battle/tools/manual_battle.py --list
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from battle import serialize_report, simulate
from battle.roster import DEFAULT_LEVEL, ROSTER, hero_setup
from battle.setup import BattleSetup, TeamSetup
from battle.tests.helpers import make_hero

# ======================= 在这里改阵容 =======================

SEED = 20260709

TEAM_A = [
    # 宙斯：谋略主C——天雷击补谋略爆发，神使戏言削敌智力，日光祝祷回蓝续航
    {"template": "zeus", "extra_skills": ("zeus_bolt", "hermes_jest", )},
    # 阿喀琉斯：物理暴击尖刀——怒火突刺提暴击追伤，觅踵打高暴击目标增伤，镜盾闪袭补突袭
    {"template": "achilles", "extra_skills": ("achilles_thrust", "perseus_flash", )},
    # 阿斯克勒庇俄斯：纯奶位——灵蛇之吻单奶，导师箴言群体增益，海后之泽持续治疗
    #{"template": "asclepius", "extra_skills": ("asclepius_kiss", "chiron_maxim", )},

    {"template": "athena", "extra_skills": ("athena_guard", "asclepius_kiss", )},
]

TEAM_B = [
    # 哈迪斯：谋略吸取核心——冥河汲魂吸智，死亡凝望压血线，摆渡收割
    {"template": "hades", "extra_skills": ("hades_soul_drain", "thanatos_gaze",)},
    # 赫拉克勒斯：物理前排——狮皮反击惩罚攻击者，坚壁提坦度，血性咆哮多目标输出
    {"template": "heracles", "extra_skills": ("heracles_counter", "ajax_bulwark",)},
    # 美杜莎：控制副C——蛇瞳一瞥点控，魅惑术叠控制链，春芽给队伍兜底
    {"template": "medusa", "extra_skills": ("medusa_glance", "siren_charm", )},
    # 白板示例：想精确控变量时用这种写法
    # {"hero_id": "木桩", "force": 70, "command": 300, "speed": 60, "skills": ()},
]

# 可选：性格判定概率覆盖（测试高概率版），如 {"haozhan.extra_action": 10000}
TRAIT_RATE_OVERRIDES: dict[str, int] = {}

# ===========================================================


def _build_hero(entry: dict, position: int):
    if "template" in entry:
        template = ROSTER[entry["template"]]
        return hero_setup(
            entry["template"],
            hero_id=entry.get("hero_id", template.name),
            position=position,
            extra_skills=tuple(entry.get("extra_skills", ())),
            level=entry.get("level", DEFAULT_LEVEL),
            max_troops=entry.get("max_troops", 10000),
            initial_troops=entry.get("initial_troops"),
        )
    return make_hero(
        entry["hero_id"], position,
        force=entry.get("force", 80),
        intelligence=entry.get("intelligence", 70),
        command=entry.get("command", 80),
        speed=entry.get("speed", 80),
        max_troops=entry.get("max_troops", 10000),
        initial_troops=entry.get("initial_troops"),
        skills=tuple(entry.get("skills", ())),
        crit_rate_bps=entry.get("crit_rate_bps", 0),
        heal_crit_rate_bps=entry.get("heal_crit_rate_bps", 0),
    )


def build_setup() -> BattleSetup:
    teams = []
    for team_id, entries in (("A", TEAM_A), ("B", TEAM_B)):
        heroes = tuple(_build_hero(e, i) for i, e in enumerate(entries))
        teams.append(TeamSetup(team_id=team_id, main_hero_id=heroes[0].hero_id,
                               heroes=heroes))
    metadata = (
        {"trait_rate_overrides": TRAIT_RATE_OVERRIDES} if TRAIT_RATE_OVERRIDES else {}
    )
    return BattleSetup(battle_id="manual_3v3", teams=tuple(teams), metadata=metadata)


# ---------------------------------------------------------------- pytest 冒烟

def test_manual_3v3_smoke():
    """当前配置能完整跑出战报（改完阵容跑一下确认没配错战法/模板名）。"""
    report = simulate(build_setup(), seed=SEED)
    assert report["games"], "战报为空"
    assert report["result"]["total_games"] >= 1


# ---------------------------------------------------------------- 直接执行

def main() -> None:
    import argparse

    from battle.textlog import MODES, format_report, safe_print

    parser = argparse.ArgumentParser(description="手动 3v3 测试入口（改文件顶部阵容）")
    parser.add_argument("--seed", type=int, default=SEED)
    parser.add_argument("--mode", choices=MODES, default="brief")
    args = parser.parse_args()

    report = simulate(build_setup(), seed=args.seed)
    text = format_report(report, mode=args.mode)
    safe_print(text)

    out_dir = Path(__file__).resolve().parents[1] / "out" / "manual"
    out_dir.mkdir(parents=True, exist_ok=True)
    json_path = out_dir / f"manual_3v3_seed{args.seed}.json"
    txt_path = out_dir / f"manual_3v3_seed{args.seed}_{args.mode}.txt"
    json_path.write_text(serialize_report(report), encoding="utf-8")
    txt_path.write_text(text, encoding="utf-8")
    print(f"\n战报 JSON: {json_path}")
    print(f"文字日志: {txt_path}")


if __name__ == "__main__":
    main()
