"""复算特效画廊的分组与排序，把「包 i/N 件 j/M」还原成 prefab 路径。

为什么需要它：人点名厂包件时说的是画廊里看到的**序号**（如「3/8 包的 19/54 件」），
而画廊的顺序由 `Assets/Editor/GreekMyth/VfxGalleryLauncher.cs::CollectGroups`
决定（路径排除 → IsFragment 后置 → 路径 Ordinal 升序 → IsEffect 过滤）。
序号错一位就会接错件，所以这里**照抄那段规则**离线复算，而不是靠肉眼数。

IsEffect 原本要加载 prefab 判组件；离线改为扫 YAML 里的 classID：
    !u!137 SkinnedMeshRenderer（命中即排除：厂包示例角色）
    !u!198 ParticleSystem / !u!120 LineRenderer / !u!96 TrailRenderer（命中即特效）

**自检**：脚本会打印每组件数，必须与画廊横幅上的分母一致（RFX4=54、
Magic Pack v1=61）。对不上说明规则漂了（比如包内容变了或有嵌套 prefab），
此时**不要**按本输出接线。

用法：python battle/tools/_gallery_index_dump.py [包序号:件序号 ...]
例：  python battle/tools/_gallery_index_dump.py 3:19 2:32 3:15
"""

from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]

# 画廊 [1/8]＝我方标准件（Runner：Resources.LoadAll + OrderBy name Ordinal，
# 排除 _bak_* / *_pre_magic，与 VfxResourcesFilter 对齐）。
# 其后厂包与 VfxGalleryLauncher.Packs 逐字对应 → 厂包组号 = 本表下标 + 2。
# 【勿】再用「分母 61＝Magic」纠偏：标准件目录现亦约 61 件，口头「1/8·分母61」
# 默认就是本包，不是 Magic（P-71）。[1/8] 序号随入库漂 → 点名以 key 为准（P-82）。
OURS_DIR = "Assets/Resources/ClientBattle/VFX"
PACKS = [
    ("Magic Pack v1", "Assets/KriptoFX/Magic Effects Pack v1/Prefabs"),
    ("RFX4", "Assets/KriptoFX/Realistic Effects Pack v4/Effects"),
    ("Vefects 连击闪卡", "Assets/Vefects/Combat Flipbook VFX URP/VFX"),
    ("Cartoon FX Remaster", "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs"),
    ("2D 斩击", "Assets/Cartoon Coffee/2D Slash VFX/Prefabs"),
    ("彩色系列", "Assets/VFX/Prefabs"),
    ("闪电链", "Assets/LightningBolt"),
]

EXCLUDED = ("/Demo/", "/SceneResources/", "/Models/", "/Materials/")

SKINNED = "!u!137 "
EFFECT_IDS = ("!u!198 ", "!u!120 ", "!u!96 ")


def is_fragment(path: str) -> bool:
    return "/EffectParts/" in path or "_Collision" in path or "_Part" in path


def is_effect(abs_path: pathlib.Path) -> bool:
    try:
        text = abs_path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return False
    if SKINNED in text:
        return False
    return any(cid in text for cid in EFFECT_IDS)


def collect(rel_dir: str) -> list[str]:
    base = ROOT / rel_dir
    if not base.is_dir():
        return []
    paths = [
        p.relative_to(ROOT).as_posix()
        for p in base.rglob("*.prefab")
    ]
    paths = [p for p in paths if not any(x in p for x in EXCLUDED)]
    # OrderBy(IsFragment).ThenBy(path, Ordinal)：Python 的 str 排序即码点序，
    # 与 StringComparer.Ordinal 在 ASCII 路径上一致。
    paths.sort(key=lambda p: (is_fragment(p), p))
    return [p for p in paths if is_effect(ROOT / p)]


def is_ours_gallery_item(name: str) -> bool:
    """与 VfxGalleryRunner.IsOursGalleryItem 同判据：排除备份/过渡件，
    避免 Ordinal 清单被 _bak_* / *_pre_magic 顶偏（P-82）。"""
    if not name:
        return False
    if name.startswith("_bak_"):
        return False
    if name.endswith("_pre_magic"):
        return False
    return True


def collect_ours() -> list[str]:
    """与 VfxGalleryRunner.EnsureOwnGroup 对齐：LoadAll 后按 name Ordinal。
    离线用文件名 stem 排序；路径写成 Resources 相对形式便于对照。"""
    base = ROOT / OURS_DIR
    if not base.is_dir():
        return []
    names = sorted(
        (p.stem for p in base.glob("*.prefab") if is_ours_gallery_item(p.stem)),
        key=lambda s: s,
    )
    return [f"{OURS_DIR}/{n}.prefab" for n in names]


def main() -> int:
    ours = collect_ours()
    groups: list[tuple[str, list[str]]] = [
        ("我方标准件（Resources/ClientBattle/VFX）", ours),
    ]
    for name, rel in PACKS:
        items = collect(rel)
        if items:
            groups.append((name, items))

    print(f"共 {len(groups)} 组（含我方标准件）")
    for i, (name, items) in enumerate(groups, start=1):
        print(f"  包 [{i}/{len(groups)}] {name}：{len(items)} 件")

    for spec in sys.argv[1:]:
        gi, ii = (int(x) for x in spec.split(":"))
        name, items = groups[gi - 1]
        print(f"\n=== 包 [{gi}/{len(groups)}] {name}  件 [{ii}/{len(items)}]")
        for j in range(max(1, ii - 2), min(len(items), ii + 2) + 1):
            mark = "  <<<< 命中" if j == ii else ""
            print(f"  [{j:>3}/{len(items)}] {items[j - 1]}{mark}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
