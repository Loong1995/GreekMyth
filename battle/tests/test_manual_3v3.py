from __future__ import annotations

"""手动 3v3 测试入口：直接改下方 TEAM_A / TEAM_B / SEED / POSITIONS 即可换阵容跑仗。

每个英雄条目二选一写法：
  1. 池内武将（用 roster 模板 + 等级 + 额外战法）：
       {"template": "achilles", "level": 50, "extra_skills": ("achilles_thrust",)}
  2. 白板自定义（手填四维 + 任意战法组合，可用 test_* 测试原语）：
       {"hero_id": "自定义甲", "force": 95, "intelligence": 70, "command": 90,
        "speed": 88, "skills": ("test_blast", "test_mend"), "initial_troops": 8000}

可选公共键：max_troops（默认 10000）、initial_troops、hero_id（模板武将也可改名）、
position（单人站位 1~6；若该队已设 POSITIONS 数组则数组优先）。

站位：TEAM_A_POSITIONS / TEAM_B_POSITIONS 与英雄列表等长，取值 1~6。
阵型**只由站位集合自动识别**（禁止传 formation 字符串）：
一字{1,2,3} / 锥形{2,4,6} / 箕形{1,5,6} / 方圆{3,4,5} /
偃月{1,3,5} / 雁行{1,2,6}。命中则挂加成（雁行有受击点/被动，其余骨架）。
几何见 docs/client/battlefield_layout.md。
为 None 时每位用条目 position，再缺省按序 1..n。

雁行 {1,2,6}：1/2 号位初始受击 10800 + 减伤 5%，6 号位 5400 + 增伤 8%。

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

# 与 TEAM_A 等长；例：雁行[1,2,6] / 一字[1,2,3] / 锥形[2,4,6]。阵型自动识别。
TEAM_A_POSITIONS: list[int] | None = [2, 4, 6]

TEAM_B = [
    # 哈迪斯：谋略吸取——冥河汲魂 + 死亡凝望
    #{"template": "hades", "extra_skills": ("hades_soul_drain", "thanatos_gaze",)},
    #{"template": "patroclus", "extra_skills": ("patroclus_armor","athena_guard")}, # 帕特：代战+披甲（与阿喀琉斯 S1 羁绊）
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
    {"template": "hector", "extra_skills": ("hector_assault", "jason_command",)},
    # {"hero_id": "木桩", "force": 70, "command": 300, "speed": 60, "skills": ()},
]

# B 队同为雁行；锥形改 [2,4,6] 即可（自动识别 zhui，无加成骨架）。
TEAM_B_POSITIONS: list[int] | None = [2, 4, 6]

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


def _resolve_positions(team_id: str, entries: list[dict],
                       positions: list[int] | None) -> list[int]:
    """站位解析：队级数组 > 条目 position > 按序 1..n。"""
    n = len(entries)
    if positions is not None:
        if len(positions) != n:
            raise ValueError(
                f"TEAM_{team_id}_POSITIONS 长度须与 TEAM_{team_id} 一致"
                f"（{len(positions)} vs {n}）"
            )
        return [int(p) for p in positions]
    return [
        int(e["position"]) if "position" in e else (i + 1)
        for i, e in enumerate(entries)
    ]


def build_setup() -> BattleSetup:
    # hero_id 是全局事件流主键必须唯一；同名英雄跨队出现时自动改名区分
    # （同队重名仍是配置错误，直接报 SetupError）。B 队撞名者加「（敌）」后缀。
    # 阵型由站位自动识别（TeamSetup.formation 只读属性）。
    a_names = {_default_name(e) for e in TEAM_A}
    teams = []
    for team_id, entries, pos_arr in (
        ("A", TEAM_A, TEAM_A_POSITIONS),
        ("B", TEAM_B, TEAM_B_POSITIONS),
    ):
        stance = _resolve_positions(team_id, entries, pos_arr)
        heroes = []
        for i, entry in enumerate(entries):
            hero_id = _default_name(entry)
            if team_id == "B" and hero_id in a_names:
                hero_id += "（敌）"
            heroes.append(_build_hero(entry, stance[i], hero_id))
        heroes = tuple(heroes)
        teams.append(TeamSetup(
            team_id=team_id, main_hero_id=heroes[0].hero_id, heroes=heroes,
        ))
    metadata = (
        {"trait_rate_overrides": TRAIT_RATE_OVERRIDES} if TRAIT_RATE_OVERRIDES else {}
    )
    return BattleSetup(battle_id="manual_3v3", teams=tuple(teams), metadata=metadata)


# ---------------------------------------------------------------- pytest 冒烟

def test_manual_3v3_smoke():
    """当前配置能完整跑出战报（改完阵容跑一下确认没配错战法/模板名）。"""
    setup = build_setup()
    assert [h.position for h in setup.teams[0].heroes] == _resolve_positions(
        "A", TEAM_A, TEAM_A_POSITIONS)
    assert [h.position for h in setup.teams[1].heroes] == _resolve_positions(
        "B", TEAM_B, TEAM_B_POSITIONS)
    assert setup.teams[0].formation == "yanxing"
    assert setup.teams[1].formation == "yanxing"
    report = simulate(setup, seed=SEED)
    assert report["games"], "战报为空"
    assert report["result"]["total_games"] >= 1


def test_manual_formation_auto_from_positions():
    """只改站位即可切换阵型；无 formation 字符串入参。"""
    from battle.tests import test_manual_3v3 as m

    old = m.TEAM_A_POSITIONS
    try:
        m.TEAM_A_POSITIONS = [2, 4, 6]
        setup = m.build_setup()
        assert [h.position for h in setup.teams[0].heroes] == [2, 4, 6]
        assert setup.teams[0].formation == "zhui"
    finally:
        m.TEAM_A_POSITIONS = old


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
