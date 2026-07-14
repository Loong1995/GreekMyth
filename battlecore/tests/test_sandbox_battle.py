"""万能战斗沙盒：在此文件内自由配置 6 名武将后运行/调试。

用法：
  1. 修改下方「场景配置」区的常量与 TEAM_A / TEAM_B 六个武将。
  2. 直接运行：  python tests/test_sandbox_battle.py
  3. 或 pytest：  python -m pytest tests/test_sandbox_battle.py -q

进场兵力（开战时 current_troop = max_troops）：
  - DEFAULT_MAX_TROOPS：全场默认，默认 10000。
  - TEAM_A_MAX_TROOPS / TEAM_B_MAX_TROOPS：分队默认，None 表示沿用 DEFAULT_MAX_TROOPS。
  - SandboxHeroSpec.max_troops：单人覆盖，优先级最高。

武将配置两种方式（二选一）：
  A. 模板模式 —— 填 template_id（如 "hades"），自带四维 + 自带战法；
     再填 extra_skills（最多 3 个习得战法，可少于 3 个），可选 append_skills 追加更多。
     属性字段（force/command/…）留 None 则用模板默认，填了则覆盖。
  B. 全自定义 —— 不填 template_id，填 name + 四维 + skill_ids。

输出：
  tests/output/{OUTPUT_NAME}.txt  （战报摘要 + 终局武将状态 + Human Logs）
  LOG_MODE="full" | "brief"：brief 省略受击率、选人、Effect 概率 roll、Effect 概率失败行。
  命令行：python tests/test_sandbox_battle.py --log-mode brief
  改 OUTPUT_NAME 只改输出文件名，不会改 pytest 函数名。
  直接 python 运行默认不把全文刷到终端（见 __main__）；pytest 仍保存全文到文件。

配置库（CONFIG_DB）：
  - "basic_test_damage"：demo + BASIC_TEST_DAMAGE 标定技能（推荐，技能最全）
  - "demo"：仅 demo 内置技能（神谕、戈耳工凝视、普攻等）

模板 id 见 battlecore/config/hero_files.py（zeus / apollo / asclepius / hades）。
技能 id 须存在于所选 config_db。
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, field, replace

import _path_bootstrap  # noqa: F401

from battlecore.config.config_db import build_basic_test_damage_config_db, build_demo_config_db
from battlecore.config.hero_files import EXTRA_SKILL_SLOT_COUNT, HERO_TEMPLATES, hero_from_template
from battlecore.config.schema import BattleInput, BattleSummary, HeroConfig
from battlecore.domain.enums import HeroRole
from battlecore.engine.battle_engine import BattleEngine
from battlecore.engine.battle_context import BattleContext
from battlecore.domain.hero_attrs import ATTR_STAT_HEADER
from battlecore.engine.damage_calculator import get_effective_attr
from _output_helper import print_and_save_output

# =============================================================================
# 场景配置 —— 只改这里即可
# =============================================================================

OUTPUT_NAME = "test_sandbox_battle"
BATTLE_ID = "sandbox_battle"
SEED = 42
MAX_ROUNDS = 10
CONFIG_DB = "basic_test_damage"  # "basic_test_damage" | "demo"
LOG_MODE = "brief"  # "full" | "brief"

# 进场兵力：开战时每位武将 current_troop = max_troops
DEFAULT_MAX_TROOPS = 10000
TEAM_A_MAX_TROOPS: int | None = None  # None → DEFAULT_MAX_TROOPS
TEAM_B_MAX_TROOPS: int | None = None

DEFAULT_SKILLS = ["basic_attack"]


@dataclass(slots=True)
class SandboxHeroSpec:
    """单武将配置。

    模板模式：设 template_id + extra_skills（最多 3 个，可少于 3 个），自带战法自动装配。
    全自定义：不设 template_id，设 name + skill_ids 等。
    """

    hero_id: str
    team_id: str
    role: HeroRole
    position: int

    template_id: str | None = None
    extra_skills: list[str] = field(default_factory=list)
    append_skills: list[str] = field(default_factory=list)

    name: str | None = None
    max_troops: int | None = None  # 单人进场兵力；None 用分队/全场默认
    force: int | None = None
    intelligence: int | None = None
    command: int | None = None
    speed: int | None = None
    skill_ids: list[str] | None = None
    crit_rate_bps: int | None = None
    heal_crit_rate_bps: int | None = None


PURSUIT_SKILL_ID = "pursuit_strike"

TEAM_A: list[SandboxHeroSpec] = [
    SandboxHeroSpec(
        "a_main",
        "team_a",
        HeroRole.MAIN,
        1,
        template_id="zeus",
        extra_skills=["circe_transfiguring_curse","medea_black_flame","medea_black_flame"]
    ),
    SandboxHeroSpec(
        "a_d1",
        "team_a",
        HeroRole.DEPUTY,
        2,
        template_id="hades",
        extra_skills=["medea_black_flame", "erinyes_vengeance_whisper", "circe_transfiguring_curse"]
    ),
    SandboxHeroSpec(
        "a_d2",
        "team_a",
        HeroRole.DEPUTY,
        3,
        template_id="asclepius",
        extra_skills=[]
    ),
]

TEAM_B: list[SandboxHeroSpec] = [
    SandboxHeroSpec(
        "b_main",
        "team_b",
        HeroRole.MAIN,
        1,
        template_id="zeus",
        extra_skills=["gorgon_gaze",]
    ),
    SandboxHeroSpec(
        "b_d1",
        "team_b",
        HeroRole.DEPUTY,
        2,
        template_id="apollo",
        extra_skills=["apollo_solar_arrow", ],

    ),
    SandboxHeroSpec(
        "b_d2",
        "team_b",
        HeroRole.DEPUTY,
        3,
        template_id="asclepius",
        extra_skills=["poseidon_abyssal_tide", "hermes_shadow_message", "erinyes_vengeance_whisper"],

    ),
]

# =============================================================================
# 构建与运行（一般无需修改）
# =============================================================================


def _resolve_config_db():
    if CONFIG_DB == "demo":
        return build_demo_config_db()
    if CONFIG_DB == "basic_test_damage":
        return build_basic_test_damage_config_db()
    raise ValueError(f'未知 CONFIG_DB="{CONFIG_DB}"，请使用 "demo" 或 "basic_test_damage"')


def _team_default_max_troops(team_id: str) -> int:
    if team_id == "team_a" and TEAM_A_MAX_TROOPS is not None:
        return TEAM_A_MAX_TROOPS
    if team_id == "team_b" and TEAM_B_MAX_TROOPS is not None:
        return TEAM_B_MAX_TROOPS
    return DEFAULT_MAX_TROOPS


def _resolve_max_troops(spec: SandboxHeroSpec) -> int:
    if spec.max_troops is not None:
        return spec.max_troops
    return _team_default_max_troops(spec.team_id)


def _apply_optional_overrides(hero: HeroConfig, spec: SandboxHeroSpec) -> HeroConfig:
    overrides: dict = {}
    for attr in (
        "name",
        "max_troops",
        "force",
        "intelligence",
        "command",
        "speed",
        "crit_rate_bps",
        "heal_crit_rate_bps",
    ):
        value = getattr(spec, attr)
        if value is not None:
            overrides[attr] = value
    if spec.append_skills:
        merged = list(hero.skill_ids)
        for skill_id in spec.append_skills:
            if skill_id not in merged:
                merged.append(skill_id)
        overrides["skill_ids"] = merged
    if overrides:
        return replace(hero, **overrides)
    return hero


def _spec_to_hero_config(spec: SandboxHeroSpec) -> HeroConfig:
    if spec.template_id is not None:
        template = HERO_TEMPLATES.get(spec.template_id)
        if template is None:
            known = ", ".join(sorted(HERO_TEMPLATES))
            raise ValueError(f"未知 template_id={spec.template_id!r}，可选：{known}")
        if len(spec.extra_skills) > EXTRA_SKILL_SLOT_COUNT:
            raise ValueError(
                f"{spec.hero_id} 使用模板 {spec.template_id} 时 extra_skills 最多 "
                f"{EXTRA_SKILL_SLOT_COUNT} 个，当前 {len(spec.extra_skills)} 个"
            )
        if spec.skill_ids is not None:
            raise ValueError(
                f"{spec.hero_id} 已设 template_id，请勿同时设 skill_ids；"
                "请用 extra_skills / append_skills"
            )
        hero = hero_from_template(
            template,
            spec.extra_skills,
            hero_id=spec.hero_id,
            team_id=spec.team_id,
            role=spec.role,
            position=spec.position,
            max_troops=_resolve_max_troops(spec),
        )
        return _apply_optional_overrides(hero, spec)

    if not spec.name:
        raise ValueError(f"{spec.hero_id} 全自定义模式必须提供 name")
    return HeroConfig(
        hero_id=spec.hero_id,
        name=spec.name,
        team_id=spec.team_id,
        role=spec.role,
        position=spec.position,
        max_troops=_resolve_max_troops(spec),
        force=spec.force if spec.force is not None else 100,
        intelligence=spec.intelligence if spec.intelligence is not None else 100,
        command=spec.command if spec.command is not None else 100,
        speed=spec.speed if spec.speed is not None else 90,
        skill_ids=list(spec.skill_ids or DEFAULT_SKILLS),
        crit_rate_bps=spec.crit_rate_bps or 0,
        heal_crit_rate_bps=spec.heal_crit_rate_bps or 0,
    )


def build_sandbox_battle_input(
    *,
    seed: int | None = None,
    max_rounds: int | None = None,
    battle_id: str | None = None,
) -> tuple[BattleInput, object]:
    db = _resolve_config_db()
    battle_input = BattleInput(
        battle_id=battle_id or BATTLE_ID,
        seed=seed if seed is not None else SEED,
        max_rounds=max_rounds if max_rounds is not None else MAX_ROUNDS,
        config_version=db.version,
        team_a_heroes=[_spec_to_hero_config(spec) for spec in TEAM_A],
        team_b_heroes=[_spec_to_hero_config(spec) for spec in TEAM_B],
    )
    return battle_input, db


def _format_hero_end_states(context: BattleContext) -> list[str]:
    lines = [
        "=== End Hero States ===",
        "hero\tteam\texited\ttroops\twounded\tdead\t"
        f"{ATTR_STAT_HEADER}\tstates",
    ]
    for hero_id in sorted(context.heroes):
        hero = context.heroes[hero_id]
        state_names = ",".join(state.name for state in hero.states) or "-"
        lines.append(
            f"{hero.name}\t{hero.team_id}\t{hero.exited}\t{hero.troops}\t"
            f"{hero.wounded_troop}\t{hero.dead_troop}\t"
            f"{get_effective_attr(hero, 'force')}\t{get_effective_attr(hero, 'intelligence')}\t"
            f"{get_effective_attr(hero, 'command')}\t{get_effective_attr(hero, 'speed')}\t"
            f"{state_names}"
        )
    return lines


def _is_brief_hidden_human_log(line: str) -> bool:
    """brief 模式省略的 Human Log 行。"""
    if "[受击率" in line:
        return True
    if "[选人·" in line:
        return True
    if " 的效果 " not in line:
        return False
    if " 概率失败，" in line:
        return True
    if " 概率成功，" in line and "roll=" in line:
        return True
    return False


def filter_human_logs(logs: list[str], log_mode: str) -> list[str]:
    if log_mode == "brief":
        return [line for line in logs if not _is_brief_hidden_human_log(line)]
    if log_mode != "full":
        raise ValueError(f'未知 log_mode="{log_mode}"，请使用 "full" 或 "brief"')
    return list(logs)


def format_sandbox_result(
    title: str,
    summary: BattleSummary,
    context: BattleContext,
    *,
    log_mode: str = LOG_MODE,
) -> str:
    lines = [
        f"=== {title} ===",
        "",
        "=== Battle Summary ===",
        f"battle_id={summary.battle_id}",
        f"seed={context.seed}",
        f"max_rounds={context.max_rounds}",
        f"default_max_troops={DEFAULT_MAX_TROOPS}",
        f"team_a_max_troops={TEAM_A_MAX_TROOPS if TEAM_A_MAX_TROOPS is not None else DEFAULT_MAX_TROOPS}",
        f"team_b_max_troops={TEAM_B_MAX_TROOPS if TEAM_B_MAX_TROOPS is not None else DEFAULT_MAX_TROOPS}",
        f"config_version={context.config_version}",
        f"result={summary.result}",
        f"winner={summary.winner_team_id}",
        f"rounds_played={summary.rounds}",
        f"finish_reason={summary.finish_reason}",
        f"event_count={summary.event_count}",
        f"log_mode={log_mode}",
        "",
    ]
    raw_logs = context.human_logs or ["<no human logs>"]
    visible_logs = filter_human_logs(raw_logs, log_mode)
    if log_mode == "brief":
        lines.append(f"human_log_lines={len(visible_logs)} (filtered from {len(raw_logs)})")
        lines.append("")
    lines.extend(_format_hero_end_states(context))
    lines.extend(["", "=== Human Logs ==="])
    lines.extend(visible_logs)
    return "\n".join(lines)


def run_sandbox_battle(
    *,
    seed: int | None = None,
    max_rounds: int | None = None,
    battle_id: str | None = None,
) -> tuple[BattleSummary, BattleContext]:
    battle_input, db = build_sandbox_battle_input(
        seed=seed,
        max_rounds=max_rounds,
        battle_id=battle_id,
    )
    summary, context = BattleEngine(db).run(battle_input)
    return summary, context


def _print_sandbox_console_summary(summary: BattleSummary, context: BattleContext, output_path) -> None:
    print("=== Sandbox Battle Done ===")
    print(f"winner={summary.winner_team_id}  rounds={summary.rounds}  reason={summary.finish_reason}")
    print(f"log_lines={len(context.human_logs)}")
    print(f"full_report={output_path}")


def test_sandbox_battle() -> None:
    summary, context = run_sandbox_battle()
    print_and_save_output(
        OUTPUT_NAME,
        format_sandbox_result("Sandbox Battle", summary, context, log_mode=LOG_MODE),
        echo_full=False,
    )
    assert summary.winner_team_id in {"team_a", "team_b", None}
    assert len(context.human_logs) > 0


def test_sandbox_brief_log_filter() -> None:
    sample_logs = [
        "[Battle x][Round 1][BASIC] [受击率·初始化] A 实时受击率=3333",
        "[Battle x][Round 1][BASIC] [选人·RANDOM_ENEMY] A 选中 候选权重: B=3333 → B",
        "[Battle x][Round 1][BASIC] A 触发 Basic Attack 成功，reason=ALWAYS_TRIGGER",
        "[Battle x][Round 1][BASIC] A 触发 雷霆 失败，概率 roll=8000 >= 7000",
        "[Battle x][Round 1][BASIC] A 的效果 Basic Attack Damage 概率成功，reason=ALWAYS_TRIGGER",
        "[Battle x][Round 1][BASIC] A 的效果 戈耳工凝视伤害 概率失败，概率 roll=8000 >= 3500",
        "[Battle x][Round 1][BASIC] A 的效果 日冕箭伤害 概率成功，概率 roll=1000 < 5000",
        "[Battle x][Round 1][BASIC] A 对 B 造成 100 点 PHYSICAL 伤害",
    ]
    filtered = filter_human_logs(sample_logs, "brief")
    assert filtered == [
        "[Battle x][Round 1][BASIC] A 触发 Basic Attack 成功，reason=ALWAYS_TRIGGER",
        "[Battle x][Round 1][BASIC] A 触发 雷霆 失败，概率 roll=8000 >= 7000",
        "[Battle x][Round 1][BASIC] A 的效果 Basic Attack Damage 概率成功，reason=ALWAYS_TRIGGER",
        "[Battle x][Round 1][BASIC] A 对 B 造成 100 点 PHYSICAL 伤害",
    ]


def test_sandbox_template_allows_fewer_than_three_extra_skills() -> None:
    spec = SandboxHeroSpec(
        "a_test",
        "team_a",
        HeroRole.DEPUTY,
        9,
        template_id="zeus",
        extra_skills=["gorgon_gaze"],
    )
    hero = _spec_to_hero_config(spec)
    assert hero.skill_ids == ["thunder_oracle", "gorgon_gaze", "basic_attack"]


def test_sandbox_resolve_max_troops_priority(monkeypatch) -> None:
    spec = SandboxHeroSpec("x", "team_a", HeroRole.MAIN, 1, template_id="zeus", max_troops=8000)
    assert _resolve_max_troops(spec) == 8000

    team_spec = SandboxHeroSpec("y", "team_b", HeroRole.MAIN, 1, template_id="zeus")
    monkeypatch.setattr(__name__ + ".TEAM_B_MAX_TROOPS", 6000)
    assert _resolve_max_troops(team_spec) == 6000
    monkeypatch.setattr(__name__ + ".TEAM_B_MAX_TROOPS", None)
    monkeypatch.setattr(__name__ + ".DEFAULT_MAX_TROOPS", 5000)
    assert _resolve_max_troops(team_spec) == 5000


def test_sandbox_hades_template_includes_innate_skill() -> None:
    hero = _spec_to_hero_config(TEAM_A[1])
    assert hero.template_id == "hades"
    assert hero.innate_skill_id == "hades_underworld_dominion"
    assert hero.skill_ids[0] == "hades_underworld_dominion"
    assert "basic_attack" in hero.skill_ids
    assert PURSUIT_SKILL_ID in hero.skill_ids


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Run sandbox battle and save human-readable report.")
    parser.add_argument(
        "--log-mode",
        choices=("full", "brief"),
        default=LOG_MODE,
        help="full=完整 Human Logs；brief=省略受击率、选人、Effect roll、Effect 概率失败",
    )
    args = parser.parse_args()
    log_mode = args.log_mode
    output_name = OUTPUT_NAME if log_mode == "full" else f"{OUTPUT_NAME}_brief"

    summary, context = run_sandbox_battle()
    path = print_and_save_output(
        output_name,
        format_sandbox_result("Sandbox Battle", summary, context, log_mode=log_mode),
        echo_full=False,
    )
    _print_sandbox_console_summary(summary, context, path)
