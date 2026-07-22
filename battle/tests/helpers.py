from __future__ import annotations

"""测试用阵容工厂。"""

from battle.setup import BattleSetup, HeroSetup, TeamSetup


def make_hero(
    hero_id: str,
    position: int,
    *,
    force: int = 80,
    intelligence: int = 70,
    command: int = 80,
    speed: int = 80,
    max_troops: int = 10000,
    initial_troops: int | None = None,
    skills: tuple[str, ...] = (),
    crit_rate_bps: int = 0,
    heal_crit_rate_bps: int = 0,
) -> HeroSetup:
    return HeroSetup(
        hero_id=hero_id,
        template_id=f"tpl_{hero_id}",
        position=position,
        force=force,
        intelligence=intelligence,
        command=command,
        speed=speed,
        max_troops=max_troops,
        initial_troops=initial_troops,
        skills=skills,
        crit_rate_bps=crit_rate_bps,
        heal_crit_rate_bps=heal_crit_rate_bps,
    )


def duel_1v1_setup(battle_id: str = "t_1v1") -> BattleSetup:
    return BattleSetup(
        battle_id=battle_id,
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(make_hero("a1", 0, force=95, speed=90),)),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(make_hero("b1", 0, force=85, speed=85),)),
        ),
    )


def full_3v3_setup(battle_id: str = "t_3v3") -> BattleSetup:
    team_a = TeamSetup(
        team_id="A",
        main_hero_id="a1",
        heroes=(
            make_hero("a1", 0, force=95, command=90, speed=88),
            make_hero("a2", 1, force=85, command=95, speed=80),
            make_hero("a3", 2, force=75, command=85, speed=95),
        ),
    )
    team_b = TeamSetup(
        team_id="B",
        main_hero_id="b1",
        heroes=(
            make_hero("b1", 0, force=92, command=88, speed=90),
            make_hero("b2", 1, force=88, command=92, speed=82),
            make_hero("b3", 2, force=70, command=80, speed=99),
        ),
    )
    return BattleSetup(battle_id=battle_id, teams=(team_a, team_b))


def skills_3v3_setup(battle_id: str = "t_skills") -> BattleSetup:
    """带 B2 测试战法的 3v3：覆盖伤害/治疗/DoT/buff/控制/属性修改全部原语。"""
    team_a = TeamSetup(
        team_id="A",
        main_hero_id="a1",
        heroes=(
            make_hero("a1", 0, force=95, command=90, speed=88,
                      skills=("test_war_cry", "test_blast"), crit_rate_bps=1000),
            make_hero("a2", 1, force=70, intelligence=110, command=95, speed=80,
                      skills=("test_mend",), heal_crit_rate_bps=1500),
            make_hero("a3", 2, force=75, intelligence=95, command=85, speed=95,
                      skills=("test_poison",)),
        ),
    )
    team_b = TeamSetup(
        team_id="B",
        main_hero_id="b1",
        heroes=(
            make_hero("b1", 0, force=92, command=88, speed=90,
                      skills=("test_disarm",)),
            make_hero("b2", 1, force=88, intelligence=100, command=92, speed=82,
                      skills=("test_mend", "test_sap")),
            make_hero("b3", 2, force=70, intelligence=105, command=80, speed=99,
                      skills=("test_blast",)),
        ),
    )
    return BattleSetup(battle_id=battle_id, teams=(team_a, team_b))


def standard_3v3_setup(battle_id: str = "t_standard") -> BattleSetup:
    """v3.1 标杆全家桶：单挑 + 神谕 + 被动 + 追击 + 准备型主动 + 性格全覆盖。"""
    from battle.roster import hero_setup

    team_a = TeamSetup(
        team_id="A",
        main_hero_id="a1",
        heroes=(
            hero_setup("zeus", hero_id="a1", position=0, extra_skills=("zeus_bolt",)),
            hero_setup("achilles", hero_id="a2", position=1,
                       extra_skills=("achilles_thrust",)),
            hero_setup("asclepius", hero_id="a3", position=2),
        ),
    )
    team_b = TeamSetup(
        team_id="B",
        main_hero_id="b1",
        heroes=(
            hero_setup("hades", hero_id="b1", position=0,
                       extra_skills=("hades_soul_drain",)),
            hero_setup("heracles", hero_id="b2", position=1),
            hero_setup("medusa", hero_id="b3", position=2,
                       extra_skills=("thanatos_scythe",)),
        ),
    )
    return BattleSetup(battle_id=battle_id, teams=(team_a, team_b))


def stalemate_setup(battle_id: str = "t_stalemate") -> BattleSetup:
    """超高统率互相打不动 + 覆盖回合上限 8 → 每局打满平局 → 7 局系列平局。

    2026-07-22 D-06 修订后默认打到主将阵亡（上限 999）；本场景显式覆盖
    rounds_per_game=8 以保留平局/续战路径的测试覆盖。"""
    def tank(hero_id: str, position: int) -> HeroSetup:
        return make_hero(hero_id, position, force=10, command=300, speed=80)

    return BattleSetup(
        battle_id=battle_id,
        teams=(
            TeamSetup(team_id="A", main_hero_id="a1", heroes=(tank("a1", 0), tank("a2", 1))),
            TeamSetup(team_id="B", main_hero_id="b1", heroes=(tank("b1", 0), tank("b2", 1))),
        ),
        metadata={"rounds_per_game": 8},
    )
