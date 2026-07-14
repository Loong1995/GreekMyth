from __future__ import annotations

"""武将四维：内部字段名与对外英文/中文叫法。

内部模型、配置、payload 仍使用 force / intelligence / command / speed。
战报、调试表头等对外英文展示使用 Might / Hex / Guard / Speed；中文仍为武力/智力/统率/敏捷。
"""

ATTR_MIGHT = "force"
ATTR_HEX = "intelligence"
ATTR_GUARD = "command"
ATTR_SPEED = "speed"

ATTR_FIELD_NAMES: tuple[str, ...] = (ATTR_MIGHT, ATTR_HEX, ATTR_GUARD, ATTR_SPEED)

ATTR_EN_LABELS: dict[str, str] = {
    ATTR_MIGHT: "Might",
    ATTR_HEX: "Hex",
    ATTR_GUARD: "Guard",
    ATTR_SPEED: "Speed",
}

ATTR_ZH_LABELS: dict[str, str] = {
    ATTR_MIGHT: "武力",
    ATTR_HEX: "智力",
    ATTR_GUARD: "统率",
    ATTR_SPEED: "敏捷",
}

# 行动顺序 / EffectiveAttrs 表：Speed 在前，随后 Might Hex Guard
ATTR_BATTLE_LOG_HEADER = "\t".join(
    ATTR_EN_LABELS[key] for key in (ATTR_SPEED, ATTR_MIGHT, ATTR_HEX, ATTR_GUARD)
)

# 终局状态等：按 Might Hex Guard Speed 排列
ATTR_STAT_HEADER = "\t".join(ATTR_EN_LABELS[key] for key in ATTR_FIELD_NAMES)


def attr_en_label(field_name: str) -> str:
    return ATTR_EN_LABELS.get(field_name, field_name)
