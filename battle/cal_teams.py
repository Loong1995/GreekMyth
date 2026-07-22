"""数值标定队伍池。

纯兵队：无任何技能，三将同面板。
常规队：每人一带减伤（全队常驻，refreshable 不叠）+ 中档主动/追击/被动伤害，
        按减伤档分 high/mid/low 三支。

属性档（全维同值，可选）：high=300 / mid=200 / low=100。
武力=智力 → 不开单挑。兵力可覆写。
"""
from __future__ import annotations

from battle.setup import BattleSetup, HeroSetup, TeamSetup

# 全属性三档（武力/智力/统率/速度同值）
ATTR_TIERS: dict[str, int] = {
    "high": 300,
    "mid": 200,
    "low": 100,
}
ATTR_TIER_IDS = tuple(ATTR_TIERS)

# 常规队伤害填充（中档期望系数 150）
CAL_DMG_FILL = ("cal_active_mid", "cal_pursuit_mid", "cal_passive_mid")

DR_SKILLS = {
    "low": "cal_dr_low",
    "mid": "cal_dr_mid",
    "high": "cal_dr_high",
}


def attr_of(tier: str) -> int:
    if tier not in ATTR_TIERS:
        raise ValueError(f"属性档须为 {ATTR_TIER_IDS}，收到 {tier!r}")
    return ATTR_TIERS[tier]


def cal_hero(
    hero_id: str,
    position: int,
    *,
    skills: tuple[str, ...] = (),
    troops: int = 10000,
    attr: int = 200,
) -> HeroSetup:
    return HeroSetup(
        hero_id=hero_id,
        template_id=f"cal_{hero_id}",
        position=position,
        force=attr,
        intelligence=attr,
        command=attr,
        speed=attr,
        max_troops=troops,
        initial_troops=troops,
        skills=skills,
    )


def make_team(
    team_id: str,
    *,
    kind: str,
    troops: int = 10000,
    attr_tier: str = "mid",
    prefix: str | None = None,
) -> TeamSetup:
    """kind: pure | regular_low | regular_mid | regular_high。"""
    tag = prefix or team_id
    attr = attr_of(attr_tier)
    if kind == "pure":
        heroes = tuple(
            cal_hero(f"{tag}{i + 1}", i, skills=(), troops=troops, attr=attr)
            for i in range(3)
        )
    elif kind.startswith("regular_"):
        tier = kind.split("_", 1)[1]
        if tier not in DR_SKILLS:
            raise ValueError(f"未知常规减伤档: {kind}")
        dr = DR_SKILLS[tier]
        skills = (dr,) + CAL_DMG_FILL
        heroes = tuple(
            cal_hero(f"{tag}{i + 1}", i, skills=skills, troops=troops, attr=attr)
            for i in range(3)
        )
    else:
        raise ValueError(f"未知标定队伍 kind: {kind}")
    return TeamSetup(team_id=team_id, main_hero_id=heroes[0].hero_id, heroes=heroes)


TEAM_KINDS = ("pure", "regular_low", "regular_mid", "regular_high")


def build_cal_setup(
    team_a: str,
    team_b: str,
    *,
    troops: int = 10000,
    attr_tier: str = "mid",
    attr_tier_a: str | None = None,
    attr_tier_b: str | None = None,
    battle_id: str = "calibrate",
) -> BattleSetup:
    """拼一对标定阵容。

    attr_tier：双方默认属性档（high/mid/low）；
    attr_tier_a / attr_tier_b：可分别覆盖单队。
    """
    if team_a not in TEAM_KINDS or team_b not in TEAM_KINDS:
        raise ValueError(f"队伍 kind 须为 {TEAM_KINDS}")
    a_tier = attr_tier_a or attr_tier
    b_tier = attr_tier_b or attr_tier
    return BattleSetup(
        battle_id=battle_id,
        teams=(
            make_team("A", kind=team_a, troops=troops, attr_tier=a_tier, prefix="A"),
            make_team("B", kind=team_b, troops=troops, attr_tier=b_tier, prefix="B"),
        ),
    )
