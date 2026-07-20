# 飘字手调操作文档（B4）

> 全部飘字参数（字体/字号/颜色/上浮动画）收口在一个 ScriptableObject：
> `FloatingTextTuning`。Inspector 改完即生效，零代码。
> 代码位置：`Assets/Scripts/ClientBattle/Units/FloatingTextTuning.cs`
> （服务读取方：`FloatingTextService`，保持零 alloc 与字形预热机制不变）。

## 一、创建调参资产（一次性）

1. Project 窗口右键 → Create → GreekMyth → **Floating Text Tuning**。
2. 命名 `FloatingTextTuning`，放到 `Assets/Resources/ClientBattle/` 下
   （路径必须是 `Resources/ClientBattle/FloatingTextTuning.asset`，服务按此加载）。
3. 不创建也能跑：缺失时用代码默认值（与历史硬编码观感一致）。

## 二、可调参数一览

| 参数 | 默认 | 说明 |
|---|---|---|
| FontName | 空 | `Resources/ClientBattle/Fonts/` 下的字体资产名；空=Unity 内置默认 |
| FontSize | 48 | 动态字体字形档位；改后开战预热自动带上新档位 |
| BaseScale | 0.1 | 世界空间基准缩放（整体大小） |
| FloatDuration | 1.1 | 单条飘字寿命（秒） |
| RiseDistance | 0.9 | 上浮距离（世界单位；OutCubic 上浮 + InQuad 淡出） |
| StackSpacing | 0.35 | 同单位连续飘字纵向错位间距 |
| CritScale / HealCritScale | 1.45 / 1.35 | 暴击/治疗暴击放大倍率 |
| 颜色 9 项 | 见 Inspector | 物理红/魔法紫/真伤黄/减免蓝/治疗绿/状态得失/属性升降 |

## 三、换字体（免费商用推荐）

1. 下载字体（均可免费商用）：
   - **思源黑体**（SourceHanSansCN，Adobe/Google OFL）——中性百搭
   - **得意黑**（SmileySans，OFL）——倾斜有速度感，适合战斗飘字
   - **站酷高端黑 / 站酷快乐体**（站酷授权免费商用）——标题感强
2. 把 `.ttf/.otf` 拖入 `Assets/Resources/ClientBattle/Fonts/`；
   Importer 保持默认 Dynamic 模式即可（动态字体 + 运行时字形预热）。
3. 在 Tuning 资产 `FontName` 填资产名（不含扩展名），Play 验证。
4. 授权文件与来源登记进 `assets_upload_guide.md` §三采购登记表。

## 四、调参验收建议

- 用 `standard_seed42.json`（控制/石化多）与 `men_gods_seed12.json`
  （连发/准备）各播一遍，重点看：暴击字是否醒目、同单位堆叠是否重叠、
  减免文案（格挡!/闪避!/反弹!）是否可读。
- 手机端（或 Game 窗口 21:9/4:3）各看一次，字号以最小屏可读为准。
