"""性格台词的**逐武将覆盖口**（预留配置位）。

默认性格台词写在 `battle/traits.py` 各 Trait 的 `lines[effect]`（按性格共享，
同性格的不同武将说同一套）。本表按 **template_id** 覆盖某个 effect 的整池，
用于给个别武将写专属口吻，不影响同性格的其他武将。

- 结构：`template_id -> effect -> (等价台词, ...)`，每池建议 3 条。
- 覆盖是**整池替换**（不与默认池混合），空元组＝该武将此 effect 静默。
- 只影响台词文本，**不改任何结算**。
- 选词与默认池同规则：seed 派生哈希流（`voice_rng.py`），不消耗战斗 RNG。
"""
from __future__ import annotations

TRAIT_LINE_OVERRIDES: dict[str, dict[str, tuple[str, ...]]] = {
    # 示例（保留为文档，非空表亦不改结算）：
    # "achilles": {"aoman_ignore": ("凡人的刃，碰不到我。", "……继续，我看着。", "再来。")},
}


def override_pool(template_id: str, effect: str) -> tuple[str, ...] | None:
    """取覆盖池；无覆盖返回 None（区别于「覆盖为静默」的空元组）。"""
    by_effect = TRAIT_LINE_OVERRIDES.get(template_id)
    if by_effect is None or effect not in by_effect:
        return None
    return by_effect[effect]
