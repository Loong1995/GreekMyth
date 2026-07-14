from __future__ import annotations

"""battle_setup 输入模型与校验（系统边界）。

规则来源：任务书 5.1（1~3 人/队、主将必设）、4.4（支持指定初始兵力、>10000 兵 NPC）、
决策 D-07（人数校验）。
"""

from dataclasses import dataclass, field

from battle.errors import SetupError

ATTR_NAMES = ("force", "intelligence", "command", "speed")


@dataclass(frozen=True, slots=True)
class HeroSetup:
    hero_id: str
    template_id: str
    position: int
    force: int
    intelligence: int
    command: int
    speed: int
    max_troops: int = 10000
    initial_troops: int | None = None  # None = 满编进场
    skills: tuple[str, ...] = ()  # 下标即装配顺序；普攻内置、不在此列
    crit_rate_bps: int = 0
    heal_crit_rate_bps: int = 0
    trait_id: str = ""       # 性格（Phase 3；空 = 无性格）
    gender: str = "m"        # m/f（宙斯多情等性格判定用）
    level: int = 50          # 等级 1~50（四维 = 模板基础 + 成长×(level-1)，由 roster 预算好）

    def resolved_initial_troops(self) -> int:
        if self.initial_troops is None:
            return self.max_troops
        return self.initial_troops


@dataclass(frozen=True, slots=True)
class TeamSetup:
    team_id: str
    main_hero_id: str
    heroes: tuple[HeroSetup, ...]


@dataclass(frozen=True, slots=True)
class BattleSetup:
    battle_id: str
    teams: tuple[TeamSetup, ...]  # 恰 2 队；team_id 字典序决定 A/B 展示顺序
    metadata: dict = field(default_factory=dict)


def validate_setup(setup: BattleSetup) -> None:
    if not setup.battle_id:
        raise SetupError("battle_id 不能为空")
    if len(setup.teams) != 2:
        raise SetupError("必须恰好两队", battle_id=setup.battle_id)

    team_ids = [team.team_id for team in setup.teams]
    if len(set(team_ids)) != 2:
        raise SetupError("team_id 重复", team_ids=team_ids)

    seen_hero_ids: set[str] = set()
    for team in setup.teams:
        if not 1 <= len(team.heroes) <= 3:
            raise SetupError("每队 1~3 名武将", team_id=team.team_id, count=len(team.heroes))
        hero_ids = {hero.hero_id for hero in team.heroes}
        if team.main_hero_id not in hero_ids:
            raise SetupError("主将必须在队内", team_id=team.team_id, main=team.main_hero_id)
        positions = [hero.position for hero in team.heroes]
        if len(set(positions)) != len(positions):
            raise SetupError("站位不能重复", team_id=team.team_id, positions=positions)
        for hero in team.heroes:
            if hero.hero_id in seen_hero_ids:
                raise SetupError("hero_id 全局重复", hero_id=hero.hero_id)
            seen_hero_ids.add(hero.hero_id)
            if not 0 <= hero.position <= 2:
                raise SetupError("position 必须在 0~2", hero_id=hero.hero_id)
            if hero.max_troops <= 0:
                raise SetupError("max_troops 必须为正", hero_id=hero.hero_id)
            initial = hero.resolved_initial_troops()
            if not 0 < initial <= hero.max_troops:
                raise SetupError(
                    "initial_troops 必须在 (0, max_troops]",
                    hero_id=hero.hero_id,
                    initial=initial,
                    max_troops=hero.max_troops,
                )
            for attr in ATTR_NAMES:
                if getattr(hero, attr) < 0:
                    raise SetupError("属性不能为负", hero_id=hero.hero_id, attr=attr)
