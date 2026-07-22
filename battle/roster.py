from __future__ import annotations

"""武将花名册 v4+（32 将 = 奥林匹斯 7 / 英雄 10 / 海域 7 / 冥界 8）。

- 四维属性：表中为 1 级初始值 + 每级成长（centi 精度，×100 存整数）；
  等级 L 的面板 = base + growth_centi × (L-1) // 100（整数下取整），默认 50 级。
- 每武将：1 自带战法（装配位 0，innate）+ 最多 2 可配置战法格。
- 珀尔修斯附带隐藏被动 perseus_mirror（镜盾石化免疫，常驻，自动装配）。
- faction（A4 定稿）：olympus/heroes/sea/underworld（原 gods/men 改名）；
  奥德修斯→sea、赫尔墨斯→underworld（manual_tasks 拍板项 1）。
  珀尔修斯借宝性格按 faction=="olympus" 判神阵营友军。
- 2026-07-22 追加：hecate / calypso；eris 改为 patroclus（英雄·借刀代战）。

用法：hero_setup("achilles", hero_id="a1", position=0, extra_skills=("achilles_thrust",))
"""

from dataclasses import dataclass

from battle.setup import HeroSetup

DEFAULT_LEVEL = 50


@dataclass(frozen=True, slots=True)
class HeroTemplate:
    template_id: str
    name: str
    faction: str
    gender: str          # m/f
    trait_id: str        # "" = 无性格
    # (base, growth_centi)：growth_centi=成长×100
    force: tuple[int, int]
    intelligence: tuple[int, int]
    command: tuple[int, int]
    speed: tuple[int, int]
    innate_skill_id: str
    hidden_skills: tuple[str, ...] = ()  # 自动装配的隐藏被动（珀尔修斯镜盾）
    crit_rate_bps: int = 0
    heal_crit_rate_bps: int = 0

    def attr_at(self, pair: tuple[int, int], level: int) -> int:
        base, growth_centi = pair
        return base + growth_centi * (level - 1) // 100


ROSTER: dict[str, HeroTemplate] = {
    t.template_id: t
    for t in (
        # ---- 奥林匹斯阵营（神示与落雷）----
        HeroTemplate("zeus", "宙斯", "olympus", "m", "duoqing",
                     (55, 90), (100, 300), (96, 230), (75, 140), "thunder_oracle"),
        HeroTemplate("athena", "雅典娜", "olympus", "f", "mingrui",
                     (78, 160), (96, 260), (98, 236), (70, 130), "athena_aegis"),
        HeroTemplate("ares", "阿瑞斯", "olympus", "m", "haozhan",
                     (98, 290), (32, 45), (90, 160), (72, 135), "ares_warfury"),
        HeroTemplate("hermes", "赫尔墨斯", "underworld", "m", "jiaoxia",
                     (55, 90), (85, 190), (50, 80), (100, 220), "hermes_oracle"),  # A4 →冥界
        HeroTemplate("apollo", "阿波罗", "olympus", "m", "guangming",
                     (60, 100), (93, 230), (70, 130), (82, 160), "delphi_revelation"),
        HeroTemplate("asclepius", "阿斯克勒庇俄斯", "olympus", "m", "renxin",
                     (15, 40), (88, 210), (40, 150), (76, 125), "asclepius_oracle"),
        HeroTemplate("artemis", "阿尔忒弥斯", "olympus", "f", "guyue",
                     (62, 101), (92, 235), (55, 90), (92, 195), "artemis_hunt"),
        HeroTemplate("nike", "尼刻", "olympus", "f", "qiusheng",
                     (70, 140), (68, 120), (60, 100), (88, 175), "nike_wings"),
        # ---- 英雄阵营（暴击与追加）----
        HeroTemplate("achilles", "阿喀琉斯", "heroes", "m", "aoman",
                     (100, 304), (40, 60), (76, 120), (86, 180), "achilles_wrath"),
        HeroTemplate("patroclus", "帕特洛克勒斯", "heroes", "m", "bonong",
                     (88, 210), (48, 75), (72, 135), (84, 170), "patroclus_standin"),
        HeroTemplate("heracles", "赫拉克勒斯", "heroes", "m", "lumang",
                     (97, 285), (30, 40), (95, 210), (60, 100), "heracles_trials"),
        HeroTemplate("odysseus", "奥德修斯", "sea", "m", "moushen",
                     (70, 130), (94, 225), (80, 160), (72, 130), "odysseus_trojan"),  # A4 →海域
        HeroTemplate("perseus", "珀尔修斯", "heroes", "m", "jiebao",
                     (91, 255), (55, 90), (58, 95), (82, 210), "perseus_relics",  # A4 速度基础 96→82 对表
                     hidden_skills=("perseus_mirror",)),
        HeroTemplate("atalanta", "阿塔兰忒", "heroes", "f", "zhuping",
                     (88, 220), (45, 70), (52, 85), (98, 215), "atalanta_swift"),
        HeroTemplate("paris", "帕里斯", "heroes", "m", "qiaoshe",
                     (84, 200), (60, 100), (48, 80), (85, 170), "paris_fatal_arrow"),
        HeroTemplate("ajax", "大埃阿斯", "heroes", "m", "jianren",
                     (88, 215), (25, 40), (96, 220), (50, 80), "ajax_shield"),
        # 喀戎 v4 下架（manual_tasks 拍板项 2）
        HeroTemplate("hector", "赫克托尔", "heroes", "m", "zhonglie",
                     (98, 292), (54, 80), (94, 218), (72, 135), "hector_warcry"),
        HeroTemplate("jason", "伊阿宋", "heroes", "m", "haozhao",
                     (82, 190), (82, 175), (80, 165), (84, 170), "jason_expedition"),
        HeroTemplate("castor", "卡斯托耳", "heroes", "m", "bingpei",
                     (89, 230), (42, 65), (76, 145), (88, 185), "castor_twin"),
        # ---- 海域阵营（震荡与节奏控制；奥德修斯 A4 迁入见上英雄段位置保持模板序）----
        HeroTemplate("poseidon", "波塞冬", "sea", "m", "jichou",
                     (92, 240), (80, 180), (92, 190), (68, 120), "poseidon_oracle"),
        HeroTemplate("amphitrite", "安菲特里忒", "sea", "f", "roubo",
                     (25, 40), (86, 200), (60, 100), (72, 125), "amphitrite_tide"),
        HeroTemplate("triton", "特里同", "sea", "m", "zhongyong",
                     (75, 155), (58, 95), (88, 190), (62, 105), "triton_horn"),
        HeroTemplate("siren", "塞壬", "sea", "f", "meihuo",
                     (30, 50), (87, 195), (45, 75), (90, 185), "siren_song"),
        HeroTemplate("scylla", "斯库拉", "sea", "f", "tanshi",
                     (90, 235), (35, 55), (78, 150), (66, 115), "scylla_maw"),
        HeroTemplate("calypso", "卡吕普索", "sea", "f", "jiliu",
                     (28, 45), (94, 235), (55, 90), (84, 165), "calypso_detain"),
        # 卡律布狄斯 v4 下架（manual_tasks 拍板项 2）
        # ---- 冥界阵营（吸取与处决；赫尔墨斯 A4 迁入见上奥林匹斯段位置保持模板序）----
        HeroTemplate("hades", "哈迪斯", "underworld", "m", "weiquan",
                     (60, 110), (92, 220), (97, 240), (55, 90), "hades_underworld_dominion"),
        HeroTemplate("medusa", "美杜莎", "underworld", "f", "guyuan",
                     (35, 55), (90, 215), (65, 140), (70, 120), "medusa_gaze"),
        HeroTemplate("persephone", "珀耳塞福涅", "underworld", "f", "huichun",
                     (38, 60), (91, 220), (72, 145), (74, 130), "persephone_seasons"),
        HeroTemplate("charon", "卡戎", "underworld", "m", "",
                     (45, 70), (84, 185), (82, 170), (58, 95), "charon_ferry"),
        HeroTemplate("thanatos", "塔纳托斯", "underworld", "m", "lengku",
                     (55, 90), (89, 210), (50, 80), (85, 170), "thanatos_scythe"),
        HeroTemplate("cerberus", "刻耳柏洛斯", "underworld", "m", "huzhu",
                     (93, 245), (15, 30), (85, 175), (62, 105), "cerberus_bite"),
        HeroTemplate("hecate", "赫卡忒", "underworld", "f", "chalou",
                     (32, 50), (96, 245), (58, 95), (80, 155), "hecate_torch"),
    )
}

FACTION_OF: dict[str, str] = {tid: t.faction for tid, t in ROSTER.items()}

MAX_EXTRA_SKILLS = 2  # 每武将 1 自带 + 2 可配置（任务书 5.1）


def hero_setup(
    template_id: str,
    *,
    hero_id: str,
    position: int,
    extra_skills: tuple[str, ...] = (),
    max_troops: int = 10000,
    initial_troops: int | None = None,
    level: int = DEFAULT_LEVEL,
) -> HeroSetup:
    template = ROSTER[template_id]
    if len(extra_skills) > MAX_EXTRA_SKILLS:
        raise ValueError(f"{template.name} 最多 {MAX_EXTRA_SKILLS} 个可配置战法")
    if template.innate_skill_id in extra_skills:
        raise ValueError(f"{template.innate_skill_id} 是 {template.name} 自带战法，不可重复装配")
    if not 1 <= level <= 50:
        raise ValueError("等级必须在 1~50")
    return HeroSetup(
        hero_id=hero_id,
        template_id=template_id,
        position=position,
        force=template.attr_at(template.force, level),
        intelligence=template.attr_at(template.intelligence, level),
        command=template.attr_at(template.command, level),
        speed=template.attr_at(template.speed, level),
        max_troops=max_troops,
        initial_troops=initial_troops,
        skills=(template.innate_skill_id, *template.hidden_skills, *extra_skills),
        crit_rate_bps=template.crit_rate_bps,
        heal_crit_rate_bps=template.heal_crit_rate_bps,
        trait_id=template.trait_id,
        gender=template.gender,
        level=level,
    )
