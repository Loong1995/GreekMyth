# 渲染 · 分辨率 · 布局与层级（rendering_layout）

> 卡牌与特效「画在哪、画多大、谁盖住谁」的唯一权威。
> 覆盖：机型分辨率适配、图像槽位缩放、sorting 层级表、棋盘布局摘要。
> **站位几何权威**：[battlefield_layout.md](battlefield_layout.md)。

## 一、自然语言叙述：任意手机上打开一场战斗

不论 4:3 平板还是 21:9 手机：相机自动缩放让整个棋盘安全区完整可见；
背景图等比放大裁切铺满（不变形）；每张卡牌里，不论美术上传的立绘是
310×573 还是 240×339，都等比缩进同一个立绘槽，观感大小一致；
落雷、头像标、气泡各在自己的层，互不遮挡错乱。

## 二、分辨率适配（机型兼容红线）

| 机制 | 做法 | 代码 |
|---|---|---|
| 取景权威 | 按当前宽高比取景：正交调 orthoSize；**近 3D 默认**调 FOV（`CameraFitter.PerspectivePilot`，卡牌后倾 `CardPitchDeg`=**45°**、相机俯角 `StagePerformanceConfig.PilotPitchDeg`=**35°**（与卡解耦，`CameraFitter` 转发）、焦段 `PilotDistance`=**55**；单挑期间由 `StageCameraRig` 临时推到俯角 45（垂直卡面）／距离 34 并还位，见 arena_stage §四d；细则见 arena_stage；「桌面扭转」`PilotYawDeg`=**0°**：机制保留（绕地面中心旋转、卡牌不自转），真转相机会让地台远边斜掉+露黑角，禁止），保证安全区（半宽 4.6 / 半高 5.2）完整可见 | `VFX/CameraFitter.cs` + `Units/StagePerformanceConfig.cs` |
| 近 3D 站位落地 | Arena 生效时站位由 `ArenaSlotLayout.SlotCenter`（矩形六等分格心 + 下缘贴地）；**权威** [battlefield_layout.md](battlefield_layout.md) | `BattlefieldLayout` / `ArenaSlotLayout` / `BattleBoardView` |
| 背景铺满 | cover 模式：等比放大到两边都盖住（超出裁切），跟随相机每帧算 | `BattleBoardView.BackgroundFitter` |
| OnGUI 缩放 | 横幅/按钮/结算表按 `Screen.height/800` 缩放字号与矩形 | `BannerService.OnGUI` / `SettlementPanel.OnGUI` 等 |
| 渲染器类型 | **URP Universal Renderer（3D 前向）**，非 2D Renderer（2026-07-28 核实更正）。深度缓冲是活依赖（`CardDepthProxy` 排罩身前后、折射取不透明拷贝），故不可换 2D Renderer | `Assets/Settings/{PC,Mobile}_Renderer.asset` |
| 管线资产 | 两套成对维护，只允许 `RenderScale` 一项有差（真机 0.8 / 编辑器 1.0）；实时阴影两套全关（场上无投影物）。差异表见 [vfx_pack_integration.md](vfx_pack_integration.md) §2.1 | `Assets/Settings/{PC,Mobile}_RPAsset.asset` |
| 镜头层（Bloom） | 强制 HDR+Bloom（厂包峰值与熔岩锋面靠它成光），**强度按画质档**，见 [vfx_mobile_budget.md](vfx_mobile_budget.md) §二b | `VFX/BattlePostFx.cs` + `VFX/VfxQuality.cs` |
| 禁止事项 | 表现层不得写死 orthoSize / 像素坐标 / 分辨率假设 | — |

## 三、图像槽位缩放（随站位格反算）

卡面世界尺寸由 `StanceLayout.Recalc` 按单格宽高与垫缝反算（单体制一档），
不再写死 1.55×2.54。`UnitView` 以 `LayoutScale` 相对 Antique 基准等比缩放
立绘/血条/锚点：

| 元素 | 模式 | 槽位 |
|---|---|---|
| 立绘 Portrait | **contain 等比** | 相对框宽高保持 0.96/1.55、1.47/2.54 比例；沿卡面法线抬离卡框 `PortraitDepth`=0.05×LayoutScale（景深，配合惯性视差）。**运行期 pos/scale/rot 由 `CardIdleMotion` 唯一写入**，勿在别处 tween |
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
| -3 | 卡牌接地阴影（近 3D 舞台才有） | `Units/CardGroundShadow.cs` |
| -2 | 突进残影 | `Units/AfterImageService.cs` |
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
| 80~93 | 全屏 cut-in。**单人**：80 暗幕 / 82 斜带 / 83 巨幅立绘 / 90 大字。**单挑舞台**：80 暗幕 / 81 屏边框 / 82 屏底 / 83 阵营辉光 / 84 放射光芒 / 85 四角纹饰 / 86 后景浮尘 / 87 立绘背光 / 88 飞行立绘 / 89 前景浮尘 / 90 中央图标 / 91 冲击环 / 92 交错白闪 / 93 影院黑边 | `CutInService`；单挑 `DuelStage`（88/87）+ `DuelStageChrome`（其余，类头有总表） |

> **加单挑装饰先去 `DuelStageChrome` 层号总表占号**。同号元素之间的先后由
> 距离/建序决定，会随机闪。屏边框必须**低于**屏底（它是整块实心，只靠屏底
> 略小一圈露出边），撞反了整块屏就被边框糊死。

> **cut-in 的挂点是相机的子物体**：整套构件挂在相机正前方 12 单位那块平面的
> 局部坐标里（`CutInService.NewRoot`，半宽半高取 `ScreenRect`），所以
> 「屏幕左右上下」在任何相机俯角下都成立，**且运镜时整块屏跟着相机走**。
> 曾按 `(cam.x, cam.y, 0)` 无旋转摆放，俯角改成 35° 后整个 cut-in 飞出视锥
> （P-64）；改成算一次世界坐标仍会在单挑推镜时滑出视野（P-65）。

## 五、棋盘布局与卡牌结构

站位几何**唯一权威**：[battlefield_layout.md](battlefield_layout.md)
（地面矩形 → UI / 主战场 / 隔离带 → 每队六等分格心；卡下缘中点贴格心）。

- **六套预设**（精确集合；客户端 `StanceLayout` / 服务端 `formations`）：
  一字 `{1,2,3}` / 锥形 `{2,4,6}` / 箕形 `{1,5,6}` / 方圆 `{3,4,5}` /
  偃月 `{1,3,5}` / 雁行 `{1,2,6}`。不匹配 = 无阵型。
- **卡尺**：单体制（按格纵深反算一档 `CardWidth`≈1.442，与宽高比无关）；
  旧交错/非交错双档（2.041 / 1.206）已废。
- 地面板与站位矩形按相机「正好拍全」动态反算（见 battlefield_layout §二）；
  宽高比差异全部落在两侧 UI 区，站位格纵深恒定。
- 建棋盘：`CameraFitter.Fit` → **逐队** `DetectFormation` →
  `RecalcFromCamera(formA, formB)` → `ArenaSlotLayout.SlotCenter`。
- 历史 `position` 0~2 → 1~3；卡牌树与阵营色不变。

## 六、维护清单

- 上传图不生效 → 先查路径（必须 `Resources/ClientBattle/<类别>/`）与
  Texture Type=Sprite（P-20），再查槽位/层级。
- 「特效没播」→ 先对照 §四层级表排除遮挡（P-21）。
- 改卡面尺寸：调 `BattlefieldLayout` 格尺寸或 `StanceLayout` 垫缝，由 `Recalc`
  反算；勿在 `UnitView` 写死世界单位；改参照卡宽后跑 VFX 标准化回填。
