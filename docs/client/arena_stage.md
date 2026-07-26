# 近 3D 舞台表现（arena_stage）

> 客户端战斗「近 3D 舞台」的**表现权威**：相机姿态、地/天底图拼接、
> 卡牌姿态与浮空。**站位几何不在本文**——见
> [battlefield_layout.md](battlefield_layout.md)。
> 出图规范见 `docs/dev/near3d_evaluation.md` §七；资源路径见
> `assets_upload_guide.md` §5b；层级/适配总表见 `rendering_layout.md`。

## 一、心智模型（桌面比喻）

- **地面 = 桌面**：水平板（XZ 平面，y=−5.2），铺正俯视 16:9 地面图。
- **天空 = 桌面后挡板**：竖直板立在地面远端，底边与地面接缝。
- **卡牌 = 桌上后仰的实体板**：**离竖直固定后倾 45°**（`CardPitchDeg`，
  即与地面 45°），下缘悬空 1/5 卡高，接地点精确落在站位点上。
- **相机 = 观者**：正面俯视 45°（＝`CardPitchDeg`，**光轴垂直于卡面**），
  不偏航不滚转；**极长焦**（`PilotDistance` 55，FOV ≈12°）≈ 平行投影。

## 二、模块划分（单一职责）

| 模块 | 职责 | 文件 |
|---|---|---|
| 相机姿态 | 透视取位（俯角/距离/FOV 自适配）；全部 Pilot 常量的**定义源** | `VFX/CameraFitter.cs` |
| 舞台底图 | 地/天两块板的构建与每帧取尺（防露黑冗余） | `Units/ArenaStageView.cs` |
| 战场分区/站位 | 地面矩形 → UI/主战场/隔离带 → 六等分格心 | `Units/BattlefieldLayout.cs` |
| 落点贴地 | 格心 → `GroundPoint`（下缘中点贴格心）；定位圆/影子 | `Units/ArenaSlotLayout.cs` |
| 卡尺寸/阵型 | 单体制卡宽高、六套预设识别 | `Units/StanceLayout.cs` |
| 卡牌姿态 | `ApplyCardLean`：固定后倾 `CardPitchDeg`、不偏航、**不读相机** | `Units/UnitView.cs` |
| 接线 | `_arenaMode`：Arena 底图齐备走透视落点，否则回退旧背景/正交 | `Units/BattleBoardView.cs` |

依赖：`ArenaStageView` / `ArenaSlotLayout` → `BattlefieldLayout` / `StanceLayout` /
`CameraFitter`；`BattleBoardView` 消费。禁止反向依赖。

## 三、关键几何（定稿值与调参入口）

| 常量 | 值 | 效果 | 位置 |
|---|---|---|---|
| `CardPitchDeg` | 45° | **卡牌后倾角**＝Euler X＝离竖直的夹角（唯一真源） | CameraFitter |
| `CardLeanDeg` | 45° | 卡牌与地面夹角＝90−后倾角（派生，仅供换算） | CameraFitter |
| `PilotPitchDeg` | 45° | **相机**俯角，定为＝`CardPitchDeg`（光轴垂直卡面） | CameraFitter |
| `PilotDistance` | 55 | 相机到棋盘距离＝**焦段**旋钮；FOV 由安全区反算（≈12°） | CameraFitter |
| `PilotYawDeg` | 0° | 桌面扭转（绕地面中心水平旋转，卡不自转）；8° 试后取消 | CameraFitter |
| `PilotGroundY` | −5.2 | 地面高度（唯一定义源，Stage/Slot 同源引用） | CameraFitter |
| `PilotPivotZ` | 1.5 | 地面/隔离带中心 z | CameraFitter |
| `CircleRadius` | 8 | **装饰圆**半径（贴图大圆参考；**不**用于站位） | ArenaSlotLayout |
| `HoverRatio` | 0.2 | 卡牌浮空（×卡高） | ArenaSlotLayout |
| `CardScaleBoost` | 1 | 卡牌整体放大；试过 2 已回 1 | StanceLayout |
| 地面板矩形 | 动态 | 「正好拍全」反算：近缘卡屏底、侧边卡接缝处屏边（仅 `EdgeGuard` 0.05 外扩）；权威 `BattlefieldLayout` | BattlefieldLayout / ArenaStageView |
| `GroundFarSeamZ` | 10 | 地天接缝（地面远缘 = 天空板 z） | BattlefieldLayout |
| `SkyMargin` | 1.2 | 天空高度冗余 | ArenaStageView |

站位分区常量见 [battlefield_layout.md](battlefield_layout.md) §二。
已废：`FrontRowRadial` / `BackRowRadial` / `SpreadCap`（逻辑圆弦长站位）。

## 四、站位（摘要，权威在 battlefield_layout）

1. 地面板 = 相机「正好拍全」矩形 → 两侧 UI 各 W/4 →
   中央缩后主战场（远侧抽出院区 D₀/5）→ 中缝隔离带 → A/B 六等分；
   院区主战场远缘→地天接缝，过渡天际线。
2. 前排 1–3 朝向隔离带，后排 4–6 靠己方外缘；B 镜面。
3. 落点经 `GroundPoint`：抬 y（浮空+倾斜补偿）、推 z，保证**下缘中点 = 格心**。
4. 群攻施法者落点 `Board.Center` = 隔离带中心上方的卡锚点。

## 四b、角度链定稿

三个角一条链，唯一真源是 `CardPitchDeg`（现行 **45°**）：

```
CardPitchDeg = 45  （卡后倾角 = Euler X = 离竖直）
CardLeanDeg  = 45  （= 90 − 后倾角，与地面夹角，派生）
PilotPitchDeg= 45  （= CardPitchDeg，光轴垂直卡面）
```

**术语红线**：「后倾 θ 度」一律指**离竖直** θ 度，实现即 `Euler(θ)`。
禁止拿 `cam.eulerAngles.x` 当倾角用。站位补偿（`GroundPoint`）、接地投影
（`GroundFoot`）、影子纵深（`CardShadowDepth`）、卡上缘高度（`CardTopY`）
一律用 `CardPitchDeg`。

历史上试过 30°/与地面 30°（Euler 60）等方案；影子过长与竖立件透视收敛
问题见 changelog 2026-07-25。现行以 45° + 长焦 55 为准。

## 四c、卡牌定位圆 = 卡牌影子的外接圆

- **影子**：卡牌矩形沿**竖直方向**投到地面的矩形 —— 横向 = 卡宽 W，
  纵深 = `CardShadowDepth` = 卡高 × sin(`CardPitchDeg`)。**不是相机投影**。
- **圆**：该矩形的外接圆，圆心 = **卡心**正下方，半径 = 半对角线。
- 所有「落在某张卡脚下」的演出（裂地、法阵、脚下光环、地面命中件、
  罩身件水平切面）的唯一定位与定径基准。

**罩身类特效**（`VfxShroudFitter`）：世界竖直、等比、水平切面对齐定位圆、
底面坐地；按件内 **Y 向最高渲染器**定径。细则见原验收与 pitfalls。

## 五、约束与已废弃方案（勿复踩）

- **相机禁止偏航/滚转**：真转相机都会让地台远边一头高一头低并露黑角。
  微透视需求一律走「地面中心扭转」（`PilotYawDeg`）。
- **禁止逻辑圆径向站位**：装饰圆可留，落点只读 `BattlefieldLayout`。
- **正交回退**：`PerspectivePilot=false` 或 Arena 底图缺失时，回退旧 XY 平面；
  站位仍走同一六等分算法（`StanceLayout.SlotCenter`）。
- **Play 中热重编译**会产生幽灵卡，见 pitfalls P-31；验收固定 stop → refresh → play。

## 六、验收方式

Play `ClientBattleDemo`（双方雁行 [1,2,6]）逐项对照：
六卡全部入画；两队接地点关于隔离带镜面对称；卡下缘中点落在格心；
地平线水平、地天接缝无黑角；卡牌悬空约 1/5 卡高。
