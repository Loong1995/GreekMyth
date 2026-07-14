"""普攻伤害定标与平衡性优化脚本。

以 30000v30000（每将 10000 兵）、仅携带普攻的 3v3 对战为基准，
批量模拟并自动微调 BASE_DAMAGE / ATTR_DIFF_COEF，
使四种属性模板同时满足平衡性约束，然后写回 damage_calculator.py。

用法：
    python bench_basic.py                  # 搜索 + 1000 场验证 + 写回
    python bench_basic.py --search-only    # 仅搜索，不写回
    python bench_basic.py --battles 200    # 搜索阶段每场 archetype 200 局
"""
from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from battlecore.api.battle_runner import run_battle
from battlecore.config.config_db import ConfigDB, build_demo_config_db
from battlecore.config.schema import BattleInput, HeroConfig
from battlecore.domain.enums import HeroRole
import battlecore.engine.damage_calculator as damage_calculator

TROOPS_PER_HERO = 10000
MAX_ROUNDS = 8
INTEL = 70
SPEEDS = (80, 70, 60)

# ---------------------------------------------------------------------------
# 四种标定模板（两队共 6 将，属性完全一致）
# ---------------------------------------------------------------------------
@dataclass(frozen=True, slots=True)
class Archetype:
    key: str
    label: str
    force: int
    command: int


ARCHETYPES: tuple[Archetype, ...] = (
    Archetype("HH", "高武低统", force=95, command=25),   # 武≥90 统≤30
    Archetype("HM", "高武中统", force=95, command=70),   # 武≥90 统=70
    Archetype("HL", "高武高统", force=95, command=90),   # 武≥90 统=90
    Archetype("LH", "低武高统", force=55, command=90),   # 武≤60 统=90
)

# ---------------------------------------------------------------------------
# 约束阈值（基于 1000 场批量统计）
# ---------------------------------------------------------------------------
HH_MAIN_DEATH_MIN = 0.15          # 可观概率：至少 15% 以主将被击溃结束
HH_MAIN_HERO_EXIT_MIN = 0.15      # finish_reason=MAIN_HERO_EXITED 占比

HM_MAIN_SURVIVAL_MIN = 0.90       # 8 回合极大概率主将存活
HL_MAIN_SURVIVAL_MIN = 0.90
LH_MAIN_SURVIVAL_MIN = 0.90

HL_CASUALTY_TARGET = 3000         # 每人战损约 3000
HL_CASUALTY_MIN = 2600
HL_CASUALTY_MAX = 3400

LH_CASUALTY_MAX = 2000            # 每人平均战损不超过 2000

DAMAGE_CALCULATOR_PATH = (
    PROJECT_ROOT / "battlecore" / "engine" / "damage_calculator.py"
)
REPORT_PATH = PROJECT_ROOT / "tests" / "output" / "bench_basic_report.txt"


@dataclass
class ScenarioStats:
    battles: int = 0
    main_death_count: int = 0
    both_mains_alive_count: int = 0
    main_hero_exit_count: int = 0
    per_hero_casualty_total: float = 0.0

    @property
    def main_death_rate(self) -> float:
        return self.main_death_count / self.battles if self.battles else 0.0

    @property
    def both_mains_alive_rate(self) -> float:
        return self.both_mains_alive_count / self.battles if self.battles else 0.0

    @property
    def main_hero_exit_rate(self) -> float:
        return self.main_hero_exit_count / self.battles if self.battles else 0.0

    @property
    def avg_per_hero_casualty(self) -> float:
        return self.per_hero_casualty_total / self.battles if self.battles else 0.0


@dataclass
class BenchResult:
    base_damage: int
    attr_diff_coef: int
    stats: dict[str, ScenarioStats] = field(default_factory=dict)
    violations: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.violations


def apply_constants(base_damage: int, attr_diff_coef: int) -> None:
    damage_calculator.BASE_DAMAGE = base_damage
    damage_calculator.ATTR_DIFF_COEF = attr_diff_coef


def build_team(team_id: str, archetype: Archetype) -> list[HeroConfig]:
    """构建 3 将队伍；全队 force/command 与 archetype 一致。"""
    roles = (HeroRole.MAIN, HeroRole.DEPUTY, HeroRole.DEPUTY)
    positions = (1, 2, 3)
    suffixes = ("Main", "D1", "D2")
    heroes: list[HeroConfig] = []
    for role, pos, suffix, speed in zip(roles, positions, suffixes, SPEEDS, strict=True):
        heroes.append(
            HeroConfig(
                hero_id=f"{team_id}_{suffix.lower()}",
                name=f"{team_id}-{suffix}",
                team_id=team_id,
                role=role,
                position=pos,
                max_troops=TROOPS_PER_HERO,
                force=archetype.force,
                intelligence=INTEL,
                command=archetype.command,
                speed=speed,
                skill_ids=["basic_attack"],
            )
        )
    return heroes


def build_input(archetype: Archetype, seed: int, config_version: str) -> BattleInput:
    return BattleInput(
        battle_id=f"bench_{archetype.key}_{seed}",
        seed=seed,
        max_rounds=MAX_ROUNDS,
        team_a_heroes=build_team("team_a", archetype),
        team_b_heroes=build_team("team_b", archetype),
        config_version=config_version,
    )


def per_hero_casualty(hero_summaries: list[dict], team_id: str) -> float:
    team = [h for h in hero_summaries if h["team_id"] == team_id]
    if not team:
        return 0.0
    return sum(h["max_troops"] - h["current_troop"] for h in team) / len(team)


def run_scenario(
    archetype: Archetype,
    *,
    battle_count: int,
    config_db: ConfigDB,
) -> ScenarioStats:
    stats = ScenarioStats()
    for seed in range(1, battle_count + 1):
        result = run_battle(build_input(archetype, seed, config_db.version), config_db)
        stats.battles += 1
        heroes = result.summary.hero_summaries
        main_a_dead = any(
            h["team_id"] == "team_a" and h["role"] == "MAIN" and h["exited"] for h in heroes
        )
        main_b_dead = any(
            h["team_id"] == "team_b" and h["role"] == "MAIN" and h["exited"] for h in heroes
        )
        if main_a_dead or main_b_dead:
            stats.main_death_count += 1
        if not main_a_dead and not main_b_dead:
            stats.both_mains_alive_count += 1
        if result.summary.finish_reason == "MAIN_HERO_EXITED":
            stats.main_hero_exit_count += 1
        avg_cas = (per_hero_casualty(heroes, "team_a") + per_hero_casualty(heroes, "team_b")) / 2
        stats.per_hero_casualty_total += avg_cas
    return stats


def evaluate(base_damage: int, attr_diff_coef: int, *, battle_count: int, config_db: ConfigDB) -> BenchResult:
    apply_constants(base_damage, attr_diff_coef)
    result = BenchResult(base_damage=base_damage, attr_diff_coef=attr_diff_coef)
    for archetype in ARCHETYPES:
        result.stats[archetype.key] = run_scenario(archetype, battle_count=battle_count, config_db=config_db)
    result.violations = check_constraints(result.stats)
    return result


def check_constraints(stats: dict[str, ScenarioStats]) -> list[str]:
    violations: list[str] = []
    hh = stats["HH"]
    hm = stats["HM"]
    hl = stats["HL"]
    lh = stats["LH"]

    if hh.main_death_rate < HH_MAIN_DEATH_MIN:
        violations.append(
            f"HH 主将阵亡率 {hh.main_death_rate:.1%} < {HH_MAIN_DEATH_MIN:.0%}（需可观概率以主将被击溃结束）"
        )
    if hh.main_hero_exit_rate < HH_MAIN_HERO_EXIT_MIN:
        violations.append(
            f"HH MAIN_HERO_EXITED 率 {hh.main_hero_exit_rate:.1%} < {HH_MAIN_HERO_EXIT_MIN:.0%}"
        )

    if hm.both_mains_alive_rate < HM_MAIN_SURVIVAL_MIN:
        violations.append(
            f"HM 双主将存活率 {hm.both_mains_alive_rate:.1%} < {HM_MAIN_SURVIVAL_MIN:.0%}"
        )
    if hl.both_mains_alive_rate < HL_MAIN_SURVIVAL_MIN:
        violations.append(
            f"HL 双主将存活率 {hl.both_mains_alive_rate:.1%} < {HL_MAIN_SURVIVAL_MIN:.0%}"
        )
    if not (HL_CASUALTY_MIN <= hl.avg_per_hero_casualty <= HL_CASUALTY_MAX):
        violations.append(
            f"HL 每人战损 {hl.avg_per_hero_casualty:.0f} 不在 [{HL_CASUALTY_MIN}, {HL_CASUALTY_MAX}]（目标≈{HL_CASUALTY_TARGET}）"
        )

    if lh.both_mains_alive_rate < LH_MAIN_SURVIVAL_MIN:
        violations.append(
            f"LH 双主将存活率 {lh.both_mains_alive_rate:.1%} < {LH_MAIN_SURVIVAL_MIN:.0%}"
        )
    if lh.avg_per_hero_casualty > LH_CASUALTY_MAX:
        violations.append(
            f"LH 每人战损 {lh.avg_per_hero_casualty:.0f} > {LH_CASUALTY_MAX}"
        )
    return violations


def loss(stats: dict[str, ScenarioStats]) -> float:
    """软约束损失，越小越好。"""
    hh, hm, hl, lh = stats["HH"], stats["HM"], stats["HL"], stats["LH"]
    total = 0.0
    total += max(0.0, HH_MAIN_DEATH_MIN - hh.main_death_rate) ** 2 * 400
    total += max(0.0, HH_MAIN_HERO_EXIT_MIN - hh.main_hero_exit_rate) ** 2 * 200
    total += max(0.0, HM_MAIN_SURVIVAL_MIN - hm.both_mains_alive_rate) ** 2 * 300
    total += max(0.0, HL_MAIN_SURVIVAL_MIN - hl.both_mains_alive_rate) ** 2 * 300
    total += max(0.0, LH_MAIN_SURVIVAL_MIN - lh.both_mains_alive_rate) ** 2 * 300
    hl_mid = (HL_CASUALTY_MIN + HL_CASUALTY_MAX) / 2
    hl_half = (HL_CASUALTY_MAX - HL_CASUALTY_MIN) / 2
    hl_norm = (hl.avg_per_hero_casualty - hl_mid) / hl_half
    total += hl_norm**2 * 50
    total += max(0.0, lh.avg_per_hero_casualty - LH_CASUALTY_MAX) ** 2 * 0.05
    return total


def format_stats(result: BenchResult) -> str:
    lines = [
        f"BASE_DAMAGE={result.base_damage}",
        f"ATTR_DIFF_COEF={result.attr_diff_coef}",
        "",
    ]
    for arch in ARCHETYPES:
        s = result.stats[arch.key]
        lines.extend(
            [
                f"[{arch.key}] {arch.label}  force={arch.force} command={arch.command}",
                f"  主将阵亡率={s.main_death_rate:.2%}",
                f"  双主将存活率={s.both_mains_alive_rate:.2%}",
                f"  MAIN_HERO_EXITED率={s.main_hero_exit_rate:.2%}",
                f"  每人平均战损={s.avg_per_hero_casualty:.1f}",
                "",
            ]
        )
    if result.violations:
        lines.append("未满足约束：")
        lines.extend(f"  - {v}" for v in result.violations)
    else:
        lines.append("全部约束满足。")
    return "\n".join(lines)


def coarse_grid_search(*, battle_count: int, config_db: ConfigDB) -> BenchResult:
    """粗网格扫描，快速定位可行区域。"""
    best: BenchResult | None = None
    best_loss = float("inf")
    bases = range(330, 451, 20)
    attrs = range(6, 16, 1)
    total = len(bases) * len(attrs)
    idx = 0
    print(f"粗网格扫描 {total} 组 (每模板 {battle_count} 场)...", flush=True)
    for base_damage in bases:
        for attr_diff_coef in attrs:
            idx += 1
            trial = evaluate(base_damage, attr_diff_coef, battle_count=battle_count, config_db=config_db)
            trial_loss = loss(trial.stats)
            if trial.ok:
                print(f"  [{idx}/{total}] 命中 base={base_damage} attr={attr_diff_coef}", flush=True)
                return trial
            if trial_loss < best_loss:
                best = trial
                best_loss = trial_loss
                print(
                    f"  [{idx}/{total}] 当前最优 base={base_damage} attr={attr_diff_coef} "
                    f"loss={trial_loss:.1f} HH死亡={trial.stats['HH'].main_death_rate:.1%} "
                    f"HL战损={trial.stats['HL'].avg_per_hero_casualty:.0f}",
                    flush=True,
                )
    assert best is not None
    return best


def optimize(
    *,
    search_battles: int,
    max_iterations: int,
    config_db: ConfigDB,
) -> BenchResult:
    coarse_battles = max(40, search_battles // 3)
    best = coarse_grid_search(battle_count=coarse_battles, config_db=config_db)
    if best.ok:
        return best

    print(f"\n粗网格未完全命中，从 base={best.base_damage} attr={best.attr_diff_coef} 精细搜索...\n", flush=True)
    base_step = 10
    attr_step = 1
    best_loss = loss(best.stats)
    print(f"初始 loss={best_loss:.2f}\n{format_stats(best)}\n", flush=True)

    for iteration in range(1, max_iterations + 1):
        if best.ok:
            break
        candidates = [
            (best.base_damage + base_step, best.attr_diff_coef),
            (best.base_damage - base_step, best.attr_diff_coef),
            (best.base_damage, best.attr_diff_coef + attr_step),
            (best.base_damage, best.attr_diff_coef - attr_step),
            (best.base_damage + base_step, best.attr_diff_coef + attr_step),
            (best.base_damage + base_step, best.attr_diff_coef - attr_step),
            (best.base_damage - base_step, best.attr_diff_coef + attr_step),
            (best.base_damage - base_step, best.attr_diff_coef - attr_step),
        ]
        improved = False
        for cand_base, cand_attr in candidates:
            cand_base = max(200, min(800, cand_base))
            cand_attr = max(1, min(30, cand_attr))
            if cand_base == best.base_damage and cand_attr == best.attr_diff_coef:
                continue
            trial = evaluate(cand_base, cand_attr, battle_count=search_battles, config_db=config_db)
            trial_loss = loss(trial.stats)
            if trial.ok or trial_loss < best_loss:
                best = trial
                best_loss = trial_loss
                improved = True
                print(
                    f"迭代 {iteration}: 采纳 base={cand_base} attr={cand_attr} "
                    f"loss={trial_loss:.2f} ok={trial.ok}",
                    flush=True,
                )
                if trial.ok:
                    break
        if not improved:
            base_step = max(5, base_step // 2)
            attr_step = max(1, attr_step // 2)
            print(
                f"迭代 {iteration}: 无改进，缩小步长 base_step={base_step} attr_step={attr_step}",
                flush=True,
            )
            if base_step <= 5 and attr_step <= 1:
                break

    return best


def write_damage_calculator(result: BenchResult, *, battle_count: int) -> None:
    path = DAMAGE_CALCULATOR_PATH
    text = path.read_text(encoding="utf-8")
    hh = result.stats["HH"]
    hm = result.stats["HM"]
    hl = result.stats["HL"]
    lh = result.stats["LH"]

    text = re.sub(
        r"^BASE_DAMAGE = \d+",
        f"BASE_DAMAGE = {result.base_damage}",
        text,
        count=1,
        flags=re.MULTILINE,
    )
    text = re.sub(
        r"^ATTR_DIFF_COEF = \d+",
        f"ATTR_DIFF_COEF = {result.attr_diff_coef}",
        text,
        count=1,
        flags=re.MULTILINE,
    )

    base_comment = (
        f"# 100% 技能率下的固定基础伤害（与兵力无关），当前标定值 {result.base_damage}。\n"
        f"# 由 bench_basic.py 标定（{battle_count} 场/模板）：100% 普攻为全技能伤害基准。\n"
        f"# 标定场景 30000v30000 仅普攻 8 回合：\n"
        f"# - 高武低统(武95/统25)：主将阵亡率≈{hh.main_death_rate:.1%}，以主将被击溃结束。\n"
        f"# - 高武中统(武95/统70)：双主将存活率≈{hm.both_mains_alive_rate:.1%}。\n"
        f"# - 高武高统(武95/统90)：双主将存活率≈{hl.both_mains_alive_rate:.1%}，每人战损≈{hl.avg_per_hero_casualty:.0f}。\n"
        f"# - 低武高统(武55/统90)：双主将存活率≈{lh.both_mains_alive_rate:.1%}，每人战损≈{lh.avg_per_hero_casualty:.0f}。\n"
        f"# 调大该值会让整体战斗更快，调小会让整体战斗更慢。"
    )
    text = re.sub(
        r"BASE_DAMAGE = \d+\n(?:#.*\n)*# 调大该值会让整体战斗更快，调小会让整体战斗更慢。\n",
        f"BASE_DAMAGE = {result.base_damage}\n{base_comment}\n",
        text,
        count=1,
    )

    attr_comment = (
        f"# 属性差值固定伤害系数（bench_basic.py 标定值 {result.attr_diff_coef}）。\n"
        f"# AttrBonus = AttrDiff × ATTR_DIFF_COEF，与 BaseDamage 相加后再乘其余倍率。\n"
        f"# 高武低统 vs 高武高统 的击杀率差异主要由本系数拉开；\n"
        f"# 低武打高统 受负差惩罚，战损显著低于高武高统。\n"
        f"# 调大 ATTR_DIFF_COEF 会让属性压制更明显；调小则让属性影响更弱。"
    )
    text = re.sub(
        r"ATTR_DIFF_COEF = \d+\n(?:#.*\n)*# 调大 ATTR_DIFF_COEF 会让属性压制更明显；调小则让属性影响更弱。\n",
        f"ATTR_DIFF_COEF = {result.attr_diff_coef}\n{attr_comment}\n",
        text,
        count=1,
    )

    path.write_text(text, encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="普攻伤害定标 bench_basic.py")
    parser.add_argument("--battles", type=int, default=200, help="搜索阶段每个模板模拟场次")
    parser.add_argument("--validate-battles", type=int, default=1000, help="最终验证每个模板模拟场次")
    parser.add_argument("--max-iter", type=int, default=40, help="最大优化迭代次数")
    parser.add_argument("--search-only", action="store_true", help="仅搜索不写回 damage_calculator.py")
    parser.add_argument("--base", type=int, default=None, help="固定 BASE_DAMAGE（跳过搜索）")
    parser.add_argument("--attr", type=int, default=None, help="固定 ATTR_DIFF_COEF（跳过搜索）")
    args = parser.parse_args()

    config_db = build_demo_config_db()
    print("=== 普攻伤害定标 bench_basic.py ===")
    print(f"搜索: 每模板 {args.battles} 场 | 验证: 每模板 {args.validate_battles} 场\n")

    if args.base is not None and args.attr is not None:
        best = evaluate(args.base, args.attr, battle_count=args.battles, config_db=config_db)
    else:
        best = optimize(
            search_battles=args.battles,
            max_iterations=args.max_iter,
            config_db=config_db,
        )

    print("\n=== 搜索阶段最优 ===")
    print(format_stats(best))

    print(f"\n=== 最终验证 ({args.validate_battles} 场/模板) ===")
    validated = evaluate(
        best.base_damage,
        best.attr_diff_coef,
        battle_count=args.validate_battles,
        config_db=config_db,
    )
    report = format_stats(validated)
    print(report)

    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text(
        "=== bench_basic.py 定标报告 ===\n\n" + report + "\n",
        encoding="utf-8",
    )
    print(f"\n报告已写入 {REPORT_PATH}")

    if not validated.ok:
        print("\n验证未通过全部约束，未写回 damage_calculator.py。")
        return 1

    if args.search_only:
        print("\n--search-only：跳过写回。")
        return 0

    write_damage_calculator(validated, battle_count=args.validate_battles)
    apply_constants(validated.base_damage, validated.attr_diff_coef)
    print(f"\n已写回 {DAMAGE_CALCULATOR_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
