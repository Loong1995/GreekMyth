"""golden 逐字节回归（任务书 4.3 第 3 层——B4 冻结后的真正基准）。

battle/tests/golden/ 下的每份战报由 battle/tools/gen_golden.py 生成冻结。
本测试重新模拟同一 (场景, 种子) 并与入库文件逐字节比较：任何改动若使输出
变化，必须显式重跑 gen_golden.py --write 并在 commit message 中说明原因。

直接运行：python battle/tests/test_golden.py
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

import pytest

from battle.tools.gen_golden import GOLDEN_CASES, generate, golden_path


def test_golden_dir_complete():
    """golden 目录与用例清单一一对应（防漏生成/防孤儿文件）。"""
    expected = {golden_path(s, seed).name for s, seed in GOLDEN_CASES}
    actual = {p.name for p in golden_path("x", 0).parent.glob("*.json")}
    assert actual == expected


@pytest.mark.parametrize("scenario,seed", GOLDEN_CASES,
                         ids=[f"{s}_seed{seed}" for s, seed in GOLDEN_CASES])
def test_golden_byte_identical(scenario: str, seed: int):
    path = golden_path(scenario, seed)
    assert path.exists(), f"golden 缺失：{path.name}（gen_golden.py --write 生成）"
    frozen = path.read_text(encoding="utf-8")
    current = generate(scenario, seed)
    # 不用 assert ==：战报可达数 MB，pytest 逐字符 diff 会卡死；只报长度与首个差异位置
    if current != frozen:
        diff_at = next((i for i, (a, b) in enumerate(zip(current, frozen)) if a != b),
                       min(len(current), len(frozen)))
        pytest.fail(
            f"golden 回归失败：{path.name} 与当前 core 输出不一致"
            f"（长度 {len(frozen)} -> {len(current)}，首个差异偏移 {diff_at}）。"
            "若改动是有意的，重跑 python battle/tools/gen_golden.py --write "
            "并在 commit message 中说明原因。")


if __name__ == "__main__":
    raise SystemExit(pytest.main([__file__, "-v"]))
