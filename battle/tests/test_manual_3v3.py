from __future__ import annotations

"""手动 3v3 测试入口：直接改下方 TEAM_A / TEAM_B / SEED 即可换阵容跑仗。

每个英雄条目二选一写法：
  1. 池内武将（用 roster 模板 + 等级 + 额外战法）：
       {"template": "achilles", "level": 50, "extra_skills": ("achilles_thrust",)}
  2. 白板自定义（手填四维 + 任意战法组合，可用 test_* 测试原语）：
       {"hero_id": "自定义甲", "force": 95, "intelligence": 70, "command": 90,
        "speed": 88, "skills": ("test_blast", "test_mend"), "initial_troops": 8000}

可选公共键：max_troops（默认 10000）、initial_troops、hero_id（模板武将也可改名）。
列表首位 = 主将（主将阵亡判负）。每队最多 3 人——换新将时注释掉原有一名。
同名英雄可以两队各上一个（B 队自动改名「XX（敌）」保证事件流主键唯一），
但同队内不可重名。

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

SEED = 20260722

TEAM_A = [
    # 宙斯：谋略主C——天雷击 + 神使戏言
    {"template": "zeus", "extra_skills": ("zeus_bolt", "hermes_jest",)},
    # 雅典娜：防御核心——神盾格挡 + 灵蛇之吻
    {"template": "athena", "extra_skills": ("athena_guard", "asclepius_kiss",)},
    # 阿斯克勒庇俄斯：纯奶位
    #{"template": "asclepius", "extra_skills": ("asclepius_kiss", "athena_guard",)},
    # ---- 新武将模板（揭开注释，并注释掉上面一名，保持每队 ≤3）----
    # {"template": "patroclus", "extra_skills": ("patroclus_armor","athena_guard")}, # 帕特：代战+披甲
    #{"template": "hecate", "extra_skills": ("hecate_pyre","athena_guard")},         # 赫卡忒：三火炬+燔祭
    #{"template": "calypso", "extra_skills": ("calypso_rime","athena_guard")},       # 卡吕普索：羁留+霜潮
    {"template": "hector", "extra_skills": ("hector_assault", "jason_command",)},
    # {"template": "medusa", "extra_skills": ("medusa_glance", "siren_charm",)},
    #{"template": "patroclus", "extra_skills": ("patroclus_armor","siren_charm")}, # 帕特：代战+披甲（与阿喀琉斯 S1 羁绊）
]

TEAM_B = [
    # 哈迪斯：谋略吸取——冥河汲魂 + 死亡凝望
    #{"template": "hades", "extra_skills": ("hades_soul_drain", "thanatos_gaze",)},
    {"template": "patroclus", "extra_skills": ("patroclus_armor","athena_guard")}, # 帕特：代战+披甲（与阿喀琉斯 S1 羁绊）
    # {"template": "hecate", "extra_skills": ("hecate_pyre","athena_guard")},         # 赫卡忒：三火炬+燔祭
    # {"template": "calypso", "extra_skills": ("calypso_rime","athena_guard")},       # 卡吕普索：羁留+霜潮
    {"template": "ares", "extra_skills": ("siren_charm", "ajax_bulwark",)},
    # 阿喀琉斯：物理尖刀——怒火突刺 + 战争狂热
    {"template": "achilles", "extra_skills": ("achilles_thrust", "ares_frenzy",)},
    #{"template": "ajax", "extra_skills": ("ajax_bulwark", "athena_guard",)},
    # ---- 新武将备选（同样揭开换上）----
    # {"template": "patroclus", "extra_skills": ("patroclus_armor", "zeus_bolt",)},
    # {"template": "hecate", "extra_skills": ("hecate_pyre", "thanatos_gaze",)},
    # {"template": "calypso", "extra_skills": ("calypso_rime", "siren_charm",)},
    # {"template": "heracles", "extra_skills": ("heracles_counter", "ajax_bulwark",)},
    # {"template": "hector", "extra_skills": ("hector_assault", "jason_command",)},
    # {"hero_id": "木桩", "force": 70, "command": 300, "speed": 60, "skills": ()},
]

# 可选：性格判定概率覆盖（测试高概率版），如 {"haozhan.extra_action": 10000}
TRAIT_RATE_OVERRIDES: dict[str, int] = {}

# ===========================================================


def _build_hero(entry: dict, position: int, hero_id: str):
    if "template" in entry:
        return hero_setup(
            entry["template"],
            hero_id=hero_id,
            position=position,
            extra_skills=tuple(entry.get("extra_skills", ())),
            level=entry.get("level", DEFAULT_LEVEL),
            max_troops=entry.get("max_troops", 10000),
            initial_troops=entry.get("initial_troops"),
        )
    return make_hero(
        hero_id, position,
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


def _default_name(entry: dict) -> str:
    if "hero_id" in entry:
        return entry["hero_id"]
    return ROSTER[entry["template"]].name


def build_setup() -> BattleSetup:
    # hero_id 是全局事件流主键必须唯一；同名英雄跨队出现时自动改名区分
    # （同队重名仍是配置错误，直接报 SetupError）。B 队撞名者加「（敌）」后缀。
    a_names = {_default_name(e) for e in TEAM_A}
    teams = []
    for team_id, entries in (("A", TEAM_A), ("B", TEAM_B)):
        heroes = []
        for i, entry in enumerate(entries):
            hero_id = _default_name(entry)
            if team_id == "B" and hero_id in a_names:
                hero_id += "（敌）"
            heroes.append(_build_hero(entry, i, hero_id))
        heroes = tuple(heroes)
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
