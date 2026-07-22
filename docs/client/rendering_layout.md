# 渲染 · 分辨率 · 布局与层级（rendering_layout）

> 卡牌与特效「画在哪、画多大、谁盖住谁」的唯一权威。
> 覆盖：机型分辨率适配、图像槽位缩放、sorting 层级表、棋盘布局。

## 一、自然语言叙述：任意手机上打开一场战斗

不论 4:3 平板还是 21:9 手机：相机自动缩放让整个棋盘安全区完整可见；
背景图等比放大裁切铺满（不变形）；每张卡牌里，不论美术上传的立绘是
310×573 还是 240×339，都等比缩进同一个立绘槽，观感大小一致；
落雷、头像标、气泡各在自己的层，互不遮挡错乱。

## 二、分辨率适配（机型兼容红线）

| 机制 | 做法 | 代码 |
|---|---|---|
| 取景权威 | 按当前宽高比每帧调 orthographicSize，保证安全区（半宽 4.6 / 半高 5.2 世界单位）完整可见；分辨率热切换自动跟随 | `VFX/CameraFitter.cs` |
| 背景铺满 | cover 模式：等比放大到两边都盖住（超出裁切），跟随相机每帧算 | `BattleBoardView.BackgroundFitter` |
| OnGUI 缩放 | 横幅/按钮/结算表按 `Screen.height/800` 缩放字号与矩形 | `BannerService.OnGUI` / `SettlementPanel.OnGUI` 等 |
| 禁止事项 | 表现层不得写死 orthoSize / 像素坐标 / 分辨率假设 | — |

## 三、图像槽位缩放（2026-07-20 定）

任意分辨率/PPU 的上传图都在**运行时**缩放到固定世界槽位，
资产侧不要求统一像素（P-20 上传红线另见 assets_upload_guide）：

| 元素 | 模式 | 槽位（世界单位） |
|---|---|---|
| 立绘 Portrait | **contain 等比**（不裁不拉伸，扁图留边） | 1.45 × 1.7 |
| 头像标 PortraitMark | contain 等比 | 0.72 × 0.72 |
| 卡框 Frame / 石化层 / 白闪 | **stretch 铺满**（占位方块要拉成卡面比例） | 1.7 × 2.3 |
| 满档流光 Glow | stretch，略大于卡框 | ×1.09 / ×1.07 |

代码：`UnitView.FitSpriteToSlot`（contain，`Mathf.Min(slotW/w, slotH/h)`）与
`StretchSpriteToSlot`（非等比）；槽位常量在 `UnitView` 顶部。
特效 prefab 不走槽位——按**目视校准**定 variant 根缩放（P-06），
演出层只允许相对乘法（`*=`），回池由 `VfxOriginalScale` 复位。

## 四、sorting 层级表（谁盖住谁——查此表再查逻辑，P-21）

数值越大越靠前。新增表现物必须先在此登记档位：

| order | 元素 | 代码 |
|---|---|---|
| -100 | 棋盘背景 | `BattleBoardView` |
| -50 | 整盘滤镜 | `BoardFilterOverlay` |
| -1 | 势能满档流光 | `UnitView` |
| 0 / 1 | 卡框 / 立绘 | `UnitView` |
| 2~4 | 血条/势能条（文字类 +10 → 12/14） | `UnitView` |
| 5 / 6 | 石化覆盖层 / 溢出白闪 | `UnitView` |
| 15 | 状态常驻光环 | `UnitAuraService` |
| 30 | 中央状态大图标 | `StatusIconPanel` |
| 40 | **VFX 池默认**（弹道/斩击/落雷/占位块） | `VFXManager` |
| 55 | 头像标（必须盖过落雷） | `UnitView.ShowPortraitMark` |
| 60 | 飘字 | `FloatingTextService` |
| 70 / 71 | 台词气泡底板 / 文字 | `ChatBubbleService` |
| 80~90 | 全屏 cut-in（80 暗幕 / 82 斜带·半屏卡 / 83 巨幅立绘 / 85 裂缝线 / 90 大字） | `CutInService` |

## 五、棋盘布局与卡牌结构

- 上下布局：A 队下、B 队上；队内按站位（1~6，4~6 为后排语义）从左到右横排居中。
- 卡牌 GameObject 树：Frame（阵营色染色）→ Portrait（槽位 contain）→
  NameLabel → HpBar+HpLabel → 四轨势能迷你条 → StatusIconPanel（中央大图标）→
  PetrifyOverlay → BubbleAnchor（右上，气泡锚点）。
- 阵营配色唯一源：`BattleBoardView.FactionColors`（神金/人红/海蓝/冥紫），
  规范见 [faction_style.md](faction_style.md)。
- 待机呼吸：存活卡立绘正弦浮动、相位按站位错开（画面永远有活物）。

## 六、维护清单

- 上传图不生效 → 先查路径（必须 `Resources/ClientBattle/<类别>/`）与
  Texture Type=Sprite（P-20），再查槽位/层级。
- 「特效没播」→ 先对照 §四层级表排除遮挡（P-21）。
- 改卡面尺寸：只动 `UnitView` 槽位常量，勿逐元素改 localScale。
