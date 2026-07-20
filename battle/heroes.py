from __future__ import annotations

"""武将运行时状态（跨局持有：兵力三池、系列统计；战时状态随局重置）。"""

from dataclasses import dataclass, field

from battle.formulas import DEFAULT_HIT_POINTS_BPS, calc_hit_points_bps
from battle.setup import HeroSetup, TeamSetup


@dataclass(slots=True)
class HeroState:
    hero_id: str
    team_id: str
    position: int
    is_main: bool
    template_id: str
    # 基础面板（有效属性 = 基础面板 + 状态修正，由 SeriesEngine.effective_attr 计算；
    # 单挑/削弱等 attr_change 直接修改这里并事件化）
    force: int
    intelligence: int
    command: int
    speed: int
    crit_rate_bps: int
    heal_crit_rate_bps: int
    max_troops: int
    troops: int
    skills: tuple[str, ...] = ()  # 装配顺序即触发判定顺序
    wounded_troop: int = 0
    dead_troop: int = 0
    defeated: bool = False  # 兵力归零退出战斗（跨局不复活）
    initial_hit_points_bps: int = DEFAULT_HIT_POINTS_BPS
    # 系列统计（result.stats）
    total_damage: int = 0
    total_heal: int = 0
    kills: int = 0
    # ---- Phase 3：性格系统 ----
    trait_id: str = ""
    gender: str = "m"
    level: int = 50
    trait_line_seq: dict[str, int] = field(default_factory=dict)  # 台词轮换计数（确定性）
    last_damaged_by: str = ""  # 最后伤害过自己的敌军（波塞冬记仇等）

    def is_alive(self) -> bool:
        return not self.defeated and self.troops > 0

    @property
    def is_backline(self) -> bool:
        """Phase 4 站位口径：position 4~6 为后排（0~2 旧口径均为前排）。"""
        return self.position >= 4

    def hit_points_bps(self) -> int:
        return calc_hit_points_bps(
            initial_hit_points_bps=self.initial_hit_points_bps,
            max_troops=self.max_troops,
            current_troops=self.troops,
        )


def build_hero_state(hero: HeroSetup, team: TeamSetup) -> HeroState:
    return HeroState(
        hero_id=hero.hero_id,
        team_id=team.team_id,
        position=hero.position,
        is_main=hero.hero_id == team.main_hero_id,
        template_id=hero.template_id,
        force=hero.force,
        intelligence=hero.intelligence,
        command=hero.command,
        speed=hero.speed,
        crit_rate_bps=hero.crit_rate_bps,
        heal_crit_rate_bps=hero.heal_crit_rate_bps,
        max_troops=hero.max_troops,
        troops=hero.resolved_initial_troops(),
        skills=hero.skills,
        trait_id=hero.trait_id,
        gender=hero.gender,
        level=hero.level,
    )


def troops_delta(hero: HeroState, before: tuple[int, int, int]) -> dict:
    """按契约 TroopsDelta 结构输出。before = (troops, wounded, dead) 快照。"""
    return {
        "hero_id": hero.hero_id,
        "troops_before": before[0],
        "troops_after": hero.troops,
        "wounded_before": before[1],
        "wounded_after": hero.wounded_troop,
        "dead_before": before[2],
        "dead_after": hero.dead_troop,
    }


def troops_snapshot(hero: HeroState) -> tuple[int, int, int]:
    return (hero.troops, hero.wounded_troop, hero.dead_troop)
