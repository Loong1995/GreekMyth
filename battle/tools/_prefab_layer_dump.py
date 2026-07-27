"""列出 prefab 的层构成（节点名 + 引用材质/shader），供特效标准化「定件」用。

docs/client/vfx_standardization.md §3.1 要求点名厂包件后**逐层判定**可迁移/需替代，
其中 URP 下画不出的厂包深度贴花（KriptoFX/RFX1|RFX4/Decal，见 P-33）只能替代。
Unity 编辑器不可用时用本脚本离线看清层构成，避免"整件直挂"。

用法：python battle/tools/_prefab_layer_dump.py <prefab 相对路径> [...]
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
NAME_RE = re.compile(r"^\s+m_Name:\s*(.+)$", re.M)


def guid_map() -> dict[str, pathlib.Path]:
    """扫全库 .meta 建 guid → 资产路径。只跑一次，几秒。"""
    out: dict[str, pathlib.Path] = {}
    for meta in (ROOT / "Assets").rglob("*.meta"):
        try:
            head = meta.read_text(encoding="utf-8", errors="ignore")[:400]
        except OSError:
            continue
        m = GUID_RE.search(head)
        if m:
            out[m.group(1)] = meta.with_suffix("")
    return out


def shader_of(mat: pathlib.Path, guids: dict[str, pathlib.Path]) -> str:
    try:
        text = mat.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return "?"
    m = re.search(r"m_Shader:.*?guid:\s*([0-9a-f]{32})", text, re.S)
    if not m:
        return "?"
    target = guids.get(m.group(1))
    return target.name if target else "?"


def main() -> int:
    guids = guid_map()
    for rel in sys.argv[1:]:
        path = ROOT / rel
        print(f"\n===== {rel}")
        if not path.is_file():
            print("  缺失")
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        names = [n.strip() for n in NAME_RE.findall(text) if n.strip()]
        print("  节点：" + ", ".join(dict.fromkeys(names)))

        mats, shaders = set(), set()
        for g in set(GUID_RE.findall(text)):
            asset = guids.get(g)
            if asset is None or asset.suffix != ".mat":
                continue
            mats.add(asset.name)
            shaders.add(shader_of(asset, guids))
        print("  材质：" + (", ".join(sorted(mats)) or "无"))
        print("  shader：" + (", ".join(sorted(shaders)) or "无"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
