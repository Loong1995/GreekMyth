from __future__ import annotations

"""State 响应顺序配置：REGULAR（Timing）与 SPY（Event）分组内的固定触发顺序。

- RegularGroupConfig：同一 timing 下 REGULAR 状态的 steps 顺序
- SpyGroupConfig：同一 event_type 下 SPY 状态 + 连锁 SKILL 的 steps 顺序
- 未列入 steps 的 State 由 UnconfiguredStateSortConfig 稳定排序

完整机制见仓库根目录 STATE_RESPONSE_REFERENCE.md。
"""

from dataclasses import dataclass
from typing import Literal

from battlecore.domain.enums import EventType, SkillCategory, Timing

StateSortKey = Literal["owner_position", "owner_instance_id", "state_instance_id"]


@dataclass(frozen=True, slots=True)
class TriggerStepConfig:
    """有序队列中的一步。

    kind=STATE：匹配携带指定 tags 的 State 实例。
    kind=SKILL：仅 SpyGroupConfig 使用，匹配连锁战法（如 PURSUIT）。
    """

    step_id: str
    kind: Literal["STATE", "SKILL"] = "STATE"
    state_tags: tuple[str, ...] = ()
    skill_category: SkillCategory | None = None


@dataclass(frozen=True, slots=True)
class SpyGroupConfig:
    """监听同一类事件的 SPY 状态有序步骤。"""

    group_id: str
    listen_event_types: tuple[EventType, ...]
    steps: tuple[TriggerStepConfig, ...]


@dataclass(frozen=True, slots=True)
class RegularGroupConfig:
    """同一 timing 下 REGULAR 状态有序步骤（仅 STATE）。"""

    group_id: str
    timing: Timing
    steps: tuple[TriggerStepConfig, ...]


@dataclass(frozen=True, slots=True)
class UnconfiguredStateSortConfig:
    """未列入 steps 的 State 稳定排序键（从前到后比较）。"""

    keys: tuple[StateSortKey, ...] = (
        "owner_position",
        "owner_instance_id",
        "state_instance_id",
    )


DEFAULT_UNCONFIGURED_STATE_SORT = UnconfiguredStateSortConfig()

# demo：DAMAGE_SETTLED SPY 顺序（先→后）
DAMAGE_SETTLED_SPY = SpyGroupConfig(
    group_id="damage_settled",
    listen_event_types=(EventType.DAMAGE_SETTLED,),
    steps=(
        TriggerStepConfig("styx_blood_oath", state_tags=("styx_blood_oath",)),
        TriggerStepConfig("snake_staff_protection", state_tags=("snake_staff_protection",)),
        TriggerStepConfig("thunder_oracle", state_tags=("thunder_oracle",)),
        TriggerStepConfig("pursuit", kind="SKILL", skill_category=SkillCategory.PURSUIT),
    ),
)

# demo：BEFORE_ACTION REGULAR 顺序（先→后）
# 1. 幽影蔽体 — 按损失兵力刷新减伤，先更新承伤区段
# 2. 冥祭献统 — 再献祭友军统率并累加武力
BEFORE_ACTION_REGULAR = RegularGroupConfig(
    group_id="before_action",
    timing=Timing.BEFORE_ACTION,
    steps=(
        TriggerStepConfig("shadow_veil", state_tags=("shadow_veil",)),
        TriggerStepConfig("hades_command_drain", state_tags=("hades_command_drain",)),
    ),
)

DEFAULT_SPY_GROUPS: tuple[SpyGroupConfig, ...] = (DAMAGE_SETTLED_SPY,)
DEFAULT_REGULAR_GROUPS: tuple[RegularGroupConfig, ...] = (BEFORE_ACTION_REGULAR,)

# 兼容旧名（逐步废弃）
ChainStepConfig = TriggerStepConfig
ChainGroupConfig = SpyGroupConfig
ActiveGroupConfig = RegularGroupConfig
DAMAGE_SETTLED_CHAIN = DAMAGE_SETTLED_SPY
BEFORE_ACTION_ACTIVE = BEFORE_ACTION_REGULAR
DEFAULT_CHAIN_REACTION_GROUPS = DEFAULT_SPY_GROUPS
DEFAULT_ACTIVE_GROUPS = DEFAULT_REGULAR_GROUPS
UnconfiguredSpySortConfig = UnconfiguredStateSortConfig
DEFAULT_UNCONFIGURED_SPY_SORT = DEFAULT_UNCONFIGURED_STATE_SORT
