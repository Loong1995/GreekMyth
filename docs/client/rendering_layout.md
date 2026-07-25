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
| 取景权威 | 按当前宽高比取景：正交调 orthoSize；**近 3D 默认**调 FOV（`CameraFitter.PerspectivePilot`，俯角/`PilotPitchDeg`=**45°**，卡-地夹角≈45°），保证安全区（半宽 4.6 / 半高 5.2）完整可见 | `VFX/CameraFitter.cs` |
| 背景铺满 | cover 模式：等比放大到两边都盖住（超出裁切），跟随相机每帧算 | `BattleBoardView.BackgroundFitter` |
| OnGUI 缩放 | 横幅/按钮/结算表按 `Screen.height/800` 缩放字号与矩形 | `BannerService.OnGUI` / `SettlementPanel.OnGUI` 等 |
| 禁止事项 | 表现层不得写死 orthoSize / 像素坐标 / 分辨率假设 | — |

## 三、图像槽位缩放（随站位区域反算）

卡面世界尺寸由 `StanceLayout` 按阵型带极大化（含休息点抖动与上下
台词带；交错阵以后排齐边带为竖向上限），不再写死 1.55×2.54。`UnitView` 以 `LayoutScale` 相对 Antique
基准等比缩放立绘/血条/锚点：

| 元素 | 模式 | 槽位 |
|---|---|---|
| 立绘 Portrait | **contain 等比** | 相对框宽高保持 0.96/1.55、1.47/2.54 比例 |
| 头像标 PortraitMark | contain 等比 | `0.72 × LayoutScale` |
| 卡框 Frame / 石化 / 白闪 | **stretch** | `StanceLayout.CardWidth × CardHeight` |
| 满档外溢光环 | CFXR 挂卡后 | LightGlow A 去星点，scale 1.18~1.65；与火同渐灭 |

代码：`StanceLayout.Recalc()` + `UnitView.FitSpriteToSlot` /
`StretchSpriteToSlot`。特效 prefab 仍目视校准（P-06），演出层只 `*=`。

## 四、sorting 层级表（谁盖住谁——查此表再查逻辑，P-21）

数值越大越靠前。新增表现物必须先在此登记档位：

| order | 元素 | 代码 |
|---|---|---|
| -100 | 棋盘背景 | `BattleBoardView` |
| -50 | 整盘滤镜 | `BoardFilterOverlay` |
| -1 | 势能满档卡后光环 | `UnitAuraService.MountMomentumGlow` |
| 0 / 1 | 卡框 / 立绘 | `UnitView` |
| 2~4 | 血条/势能条（文字类 +10 → 12/14） | `UnitView` |
| 5 / 6 | 石化覆盖层 / 溢出白闪 | `UnitView` |
| 15 | 状态常驻光环 | `UnitAuraService` |
| 30 | 卡顶外侧状态图标 | `StatusIconPanel` |
| 40 | **VFX 池默认**（弹道/斩击/落雷/占位块） | `VFXManager` |
| 55 | 头像标（必须盖过落雷） | `UnitView.ShowPortraitMark` |
| 60 | 飘字 | `FloatingTextService` |
| 70 / 71 | 台词气泡底板 / 文字 | `ChatBubbleService` |
| 80~90 | 全屏 cut-in（80 暗幕 / 82 斜带·半屏卡 / 83 巨幅立绘 / 85 裂缝 / 88 交错白闪 / 90 大字） | `CutInService` |

## 五、棋盘布局与卡牌结构

- **阵型组合**（禁止同列前后排同时放卡，避免竖向四倍卡高）：
  - **方圆阵** `{1,5,6}`：前左 + 后中 + 后右。
  - **却月阵** `{1,2,6}`：前左 + 前中 + 后右。
  - **鹤翼阵** `{2,4,6}`：前中 + 后左 + 后右。
  - **前列横排** `{1,2,3}`：旧战报/仅前排兼容。
  - **六区格心**（其它占位）：前 1~3 / 后 4~6 各落半格中心。
- **布局半宽半高锁定设计安全区**（4.6×5.2，与 `CameraFitter` 一致）；
  宽屏两侧余量只铺背景，三列不随视野横向撑开。
- **交错阵几何**（方圆/却月/鹤翼共用，`StanceLayout`）：
  **上侧(B)**：后排卡上缘贴队区上界、下缘贴 **前排区下 1/3 线**（卡高=该跨度）；
  **前排卡底缘贴队区内缘（中缝侧）**，避免同卡高穿入中线；A 侧镜像。
- 建棋盘：`CameraFitter.Fit` → **逐队** `DetectFormation` → `RecalcFromCamera(formA, formB)`；
  异阵对打（如方圆 vs 鹤翼）各按本队落点，卡尺取交错带（任一方交错即用）。
- 历史 `position` 0~2 → 1~3；卡牌树与阵营色不变。

## 六、维护清单

- 上传图不生效 → 先查路径（必须 `Resources/ClientBattle/<类别>/`）与
  Texture Type=Sprite（P-20），再查槽位/层级。
- 「特效没播」→ 先对照 §四层级表排除遮挡（P-21）。
- 改卡面尺寸：只调 `StanceLayout` 的 LineReserve/MidClear/垫缝或安全区，
  由 `Recalc` 反算；勿在 `UnitView` 写死世界单位。
