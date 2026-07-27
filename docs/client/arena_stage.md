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
- **相机 = 观者**：正面俯视 **35°**（`StagePerformanceConfig.PilotPitchDeg`，与卡后倾**解耦**），
  不偏航不滚转；**极长焦**（`PilotDistance` 55，FOV ≈12°）≈ 平行投影。
  `CameraFitter.PilotPitchDeg` 只转发该配置。

## 二、模块划分（单一职责）

| 模块 | 职责 | 文件 |
|---|---|---|
| 相机姿态 | 透视取位（俯角/距离/FOV 自适配）；全部 Pilot 常量的**定义源** | `VFX/CameraFitter.cs` |
| 舞台底图 | 地/天两块板的构建与每帧取尺（防露黑冗余） | `Units/ArenaStageView.cs` |
| 战场分区/站位 | 地面矩形 → UI/主战场/隔离带 → 六等分格心 | `Units/BattlefieldLayout.cs` |
| 落点贴地 | 格心 → `GroundPoint`（下缘中点贴格心）；定位圆/投影圆/影子 | `Units/ArenaSlotLayout.cs` |
| 卡尺寸/阵型 | 单体制卡宽高、六套预设识别 | `Units/StanceLayout.cs` |
| 卡牌姿态 | `ApplyCardLean`：后倾 `CardPitchDeg` **每卡随机 ±`CardPitchJitterDeg`**、不偏航、**不读相机** | `Units/UnitView.cs` |
| 接线 | `_arenaMode`：Arena 底图齐备走透视落点，否则回退旧背景/正交 | `Units/BattleBoardView.cs` |

依赖：`ArenaStageView` / `ArenaSlotLayout` → `BattlefieldLayout` / `StanceLayout` /
`CameraFitter`；`BattleBoardView` 消费。禁止反向依赖。

## 三、关键几何（定稿值与调参入口）

| 常量 | 值 | 效果 | 位置 |
|---|---|---|---|
| `CardPitchDeg` | 45° | **卡牌后倾角**＝Euler X＝离竖直的夹角（唯一真源） | CameraFitter |
| `CardLeanDeg` | 45° | 卡牌与地面夹角＝90−后倾角（派生，仅供换算） | CameraFitter |
| `CardPitchJitterDeg` | 5° | **每卡**后倾角在**基准 ± 此值**内随机（45±5 ＝ **40°~50°**），整排不像同一块板。**只抖视觉**：`GroundPoint`/`GroundFoot`/`CardShadowDepth` 仍按基准角算，故不宜 >8° | StagePerformanceConfig |
| `PilotPitchDeg` | 35° | **相机**俯角（从水平往下压）。与卡后倾解耦；**数值在 `StagePerformanceConfig`**，`CameraFitter` 只转发 | StagePerformanceConfig |
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

## 四b、角度定稿（卡 / 机两个独立旋钮）

```
CardPitchDeg = 45  （卡后倾角 = Euler X = 离竖直；站位/影子/两圆几何真源；CameraFitter）
CardLeanDeg  = 45  （= 90 − 后倾角，与地面夹角，派生）
PilotPitchDeg= 35  （相机俯角；数值权威 StagePerformanceConfig，CameraFitter 转发）
```

**术语红线**：「后倾 θ 度」一律指**离竖直** θ 度，实现即 `Euler(θ)`。
禁止拿 `cam.eulerAngles.x` 当卡牌倾角用。站位补偿（`GroundPoint`）、接地投影
（`GroundFoot`）、影子纵深（`CardShadowDepth`）、卡上缘高度（`CardTopY`）
一律用 `CardPitchDeg`，**不**跟相机俯角走。

卡 / 机解耦：可单独调机位而不动卡几何。卡 Euler 60 曾试废（影子过长、罩身件
透视收敛）；历史上试过 30°/与地面 30°（Euler 60）等方案，见 changelog 2026-07-25。
调俯角：改 `StagePerformanceConfig.PilotPitchDeg`，重新进 Play 生效。
单挑期间俯角会被 `StageCameraRig` 临时推到 `DuelCameraPitchDeg`（45＝**垂直卡面**）
并把距离缩到 `DuelCameraDistance`，回程还原（见 §四d）。

## 四c、卡牌的两个地面圆：定位圆 / 投影圆

2026-07-27 定名。此前只有一个叫「定位圆」的圆，实际算的却是整卡投影的外接圆，
名实不符正是混用的根源。**现在是两个圆，语义、圆心、半径全都不同。**

| | 定位圆 Anchor | 投影圆 Projection |
|---|---|---|
| 几何 | 卡牌**下边缘**（贴地的接触线）的端点绕下边缘中点转一周 | 整张卡沿**世界竖直**投到地面的影子矩形（W × `CardShadowDepth`）的**外接圆** |
| 圆心 | `GroundFoot` ＝接地点＝**站位点** | 卡心**正下方**（影子矩形中心） |
| 半径 | 卡宽 / 2（**直径＝卡宽**） | 影子矩形半对角线 |
| 语义 | 「这张卡**站在**地上的那一小圈」 | 「把**整张卡**包进去的那一圈」 |
| 用在 | 地面痕迹、裂地、脚下法阵、站位、单挑出阵/溃败特效 | 罩身/绕身件的水平切面与地面 Decal |
| API | `ArenaSlotLayout.AnchorCircleCenter / Radius / Diameter` | `ArenaSlotLayout.ProjectionCircleCenter / Radius / Diameter` |

后倾 45° 时 `CardShadowDepth ≈ 0.707 × 卡高`、卡高 ≈ 1.4 × 卡宽，
故**投影圆直径约为定位圆的 1.4 倍，且两圆不同心**（相差 `CardShadowDepth/2`）。
差得足够大，选错一眼可见：拿投影圆做地面痕迹会散出一圈虚边，
拿定位圆做罩身会把卡的两个上角切在圈外。

> 旧 API `CardCircle*` 指的是**投影圆**，已重命名废止——那个名字读起来像定位圆。
> **禁止再引入不带 `Anchor` / `Projection` 前缀的"圆"。**
>
> 画廊里两圆同屏可见：**青环＝投影圆、黄环＝定位圆**（`VfxGalleryRunner`），
> 审件时直接看是对准了哪一个。

**罩身类特效**（用**投影圆**）：
- 定径/摆位规格：`VfxShroudFitter.Fit`（世界竖直、原点＝投影圆心、
  Decal 严格＝投影圆直径）。
- **运行期跟随（通用）**：`VfxShroudFollower` —— melee / 平时一律钉
  `ProjectionCircleCenter(持有者 transform)`；个案显隐等各自挂组件，不写进 Follower。

## 四d、演出性运镜（StageCameraRig）

静态机位（`CameraFitter`）＋抖动（`CameraShaker`）之外，允许一段演出**临时接管**
相机做推拉，结束还位。唯一实现 `VFX/StageCameraRig.cs`，目前唯一用户是单挑。

- 只动**俯角**与**距离**，**不动 FOV**：FOV 是按安全区反算的取景基准，
  改它等于换镜头畸变；极长焦下缩距离就能"显著变近"而透视关系不变。
- 接管期间它是相机位姿的**唯一写方**，抖动被切到"只算不写"
  （`CameraShaker.Suspended`，偏移由 rig 叠加）。两个 `LateUpdate` 的先后
  在 Unity 里是不确定的，不这么做就会出现"抖一下不抖一下"。
- **谁接管谁归还**：`Release()` 幂等，正常收尾与中断
  （`CutInService.CancelAll` / `PerformanceRunner.HardStop`）三条路径都要走到。
- 全屏 cut-in 的挂点是**相机的子物体**（`CutInService.NewRoot`），
  所以运镜与 cut-in 天然解耦，屏不会随相机滑出视野。

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
