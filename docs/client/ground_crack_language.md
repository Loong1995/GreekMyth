# 裂地表现语言（权威）

> 弹道 / 命中 / 轨迹 共用**同一套材质与熔岩逻辑**，结构为
> **模式两类 × 强度三档 + 面积**。
>
> **何时出、出几档**（含物理/魔法分界）：[ground_crack_config.md](ground_crack_config.md)
> ——本文不重复选档表。
>
> 上位：[performance_mechanisms.md](performance_mechanisms.md)、
> [arena_stage.md](arena_stage.md)、[assets_upload_guide.md](assets_upload_guide.md)、
> [vfx_standardization.md](vfx_standardization.md)。

## 一、现行结论（先读）

1. **厂包深度投影贴花在 URP 下不可用**（KriptoFX Decal）：裂地走自研平躺面片
   + `GroundCrack.shader`。
2. **两件骨架 prefab**：`ground_crack_path` / `ground_crack_hit`。三档通吃、
   任意面积通吃；**不为分档或分大小另烘变体**。
3. **颜色唯一真源** `VFX/GroundCrackPalette.cs`；触发唯一入口
   `VFX/GroundCrackService.cs`。演出模板禁止直接 `PlayAt("ground_crack_*")`。
4. **熔岩**：弹道/命中三档全开；弹道同档比命中稍弱（`GlowPeak`/`Ember` ×**0.78**），
   且熔岩**只在主缝上**（遮罩 R 通道门控）。生长/灭点逻辑共用。
5. **档 3＝档 2 的自然增强**（骨架放大 + 略提亮），禁止叠自带轮廓的厂包熔岩层
   （`ground_lava_bloom` 留库未接线，见 P-55）。
6. **物魔**：命中 / 轨迹同规；**仅弹道裂地限物理**（见 config §一）。

## 二、地面前置（已完成）

舞台地面：`MeshRenderer` Quad + `URP/Unlit` Opaque + `ZWrite On`
（`ArenaStageView.BuildQuads`）。贴图仍是 `arena_<stage>`。这是一切地面演出的前提；
厂包贴花仍不可用是 shader 管线问题，不是缺深度。

## 三、配方与两维结构

### 3.0 层

| 层 | 作用 | 实现 | 颜色 |
|---|---|---|---|
| L1 裂缝 | 缝口细线 | 平躺 Sprite，遮罩 alpha | `Palette.Crack` |
| L2 缝底 | 缝里深处 | 同遮罩 ×0.55、更暗 | `Palette.CrackCore` |
| L3 碎块 | （已关） | `ChunkCount=0`；俯视像漂浮烟雾 | 底图现切工具仍保留 |

尘雾亦已关。冲击环未做。

### 3.0.1 模式 × 强度 × 面积

| 维度 | 管什么 | 真源 | 谁写入 |
|---|---|---|---|
| **模式** Mode | 遮罩形状、生长方向、朝向、尺寸基准 | `PathMode` / `ImpactMode` | G4 烘 prefab |
| **强度** Strength | 缝宽 + 持续 + 熔岩亮度（整档取） | `SpecOf(strength, mode)` | `Decal.ApplyStrength` |
| **面积** area | 命中类大小倍率（默认 1＝卡宽×1.5） | `GroundHitArea` / 拉满×1.5 | `Decal.ApplyArea` |

强度表（`StrengthSpec`；`_MaskGain` 两模式同；熔岩按模式分流；持续按模式分档）：

| 档 | 枚举 | `_MaskGain` | 持续相对档 1 | 命中 `GlowPeak` | 弹道 `GlowPeak`（×0.78） | `SizeScale` |
|---|---|---|---|---|---|---|
| 1 轻 | `Light` | **1.15** | ×1 | 2.1 | **1.64** | ×1 |
| 2 重 | `Heavy` | **2.55** | 弹道 ×1.25 / 命中 ×1.5 | 3.6 | **2.81** | ×1 |
| 3 熔岩 | `Blaze` | **3.8** | 弹道 ×1.5 / 命中 ×2 | 4.4 | **3.43** | **×1.35** |

命中三档 `_FrontWidth`/`_EmberFloor`：0.14/0.12 · 0.20/0.28 · 0.24/0.34。
弹道同档 Ember 亦 ×0.78（约 0.09 / 0.22 / 0.27），FrontWidth 与命中同。

**选档与面积** → [ground_crack_config.md](ground_crack_config.md)（物魔同规 + 弹道例外）。
缝宽走 `_MaskGain`，**不要靠缩放面片**（会把放射骨架拉成椭圆）。

### 3.1 生长 + 熔岩

Shader：`Assets/Shaders/ClientBattle/GroundCrack.shader`（预乘 alpha）。
生长/灭点时序弹道与命中**分流生长、共用灭点**；弹道三档熔岩均开，亮度＝同档命中 ×0.78。

**裂缝生长**：`field`（命中＝径向距离，弹道＝`uv.x`）与 `_Growth` 比较镂空；
`Decal` 用 `Burst()` 揉成一阵一顿，推进满值约 1.47（覆盖抖动 + 层间滞后）。

**熔岩生长（按模式分流）**：

| 项 | 命中（跟随裂缝） | 弹道（先裂后烧） |
|---|---|---|
| `LavaDelay` | **0.08×GrowTime** | **0.65×GrowTime** |
| `LavaGrowMul` | **1.0**（与缝同速） | **1.45**（火更慢） |
| GrowTime | **0.2s**（≈HitReact） | **0.22s** |
| 语义 | 火贴着放射锋面一起长 | 黑缝先张开再烧 |

另有点火散布：`_LavaScatter` / `_LavaCells` 值噪声，几处火口先后烧开再连片。

**熔岩消退（灭点错开，禁止全局同步压暗）**：

1. 寿命前 35% 只生长不熄，亮度维持峰值（可叠明灭）。
2. 之后 `_LavaExtinguish` 0→1（平方曲线）；shader 用 `_LavaFadeCells` 噪声取灭点，
   阈值低的先灭、高的后灭。
3. 每发 `_LavaFadeSeed` 不同；子面片再各加一点种子偏移 → 多段弹道 / 多处命中
   不会齐灭。
4. `LavaLifeRatio≈0.65`，比裂缝先灭完。

色：缝沿暗红 → 缝底 `Palette.Lava` → 缝心白热（`_LavaGradient`）。色相取自
Effect8 `Decal1.mat _TintColor` 等比抬亮。

### 3.2 遮罩（只管形状）

G4 烘 `alpha=裂纹形状`，颜色由调色板定。**R 通道＝熔岩门**：主缝写 1、
枝杈/碎缝写 0，shader 用它门控熔岩 → 只有主缝烧、细枝保持暗缝；
命中遮罩 R 恒 1（整图可燃）。

| 用途 | 文件 | 规则 |
|---|---|---|
| 弹道 | `mask_crack_spine_{0..3}`（1024×256） | **一条蜿蜒主缝**贯通全幅（±40° 游走，约 **7.5~11.5px**）；树杈分叉从主缝长出（约 6~9 根）+ 3~6 条游离细缝。飞行每段抽不同变体。 |
| 命中 | `mask_crack_radial`（512×512） | 10 主缝递归分叉（主缝 UV 宽约 **0.022~0.055**）+ 离心次级 + 中心碎裂 + 短连接缝；**禁止同心环** |

烘制走固定哈希（可复现）；边缘用自写 `EdgeSmooth`（禁止 `Mathf.SmoothStep` 当
HLSL smoothstep，见 P-54）。写出前断言 maxA≥0.5。

### 3.3 每发随机化（去僵硬）

`GroundCrackDecal.Roll()` 每次出场重摇：裂缝推进 0.55~1.9×、熔岩寿命/停留、
整体错峰起裂 0~0.22s、灭点种子、子面片点火滞后。匀速 + 全场同参数＝机关动画
（P-56）。

## 四、骨架与场景

| 模式 | key | 烘出尺寸 | 生长 | 朝向 | 尺寸基准 |
|---|---|---|---|---|---|
| 弹道/轨迹 `Path` | `ground_crack_path` / `_0`~`_3` | 长 2.5 × 宽 1.05 | **驱动式**轴向 | 调用方 yaw | 烘出尺寸；出场随机抽变体 |
| 命中 `Impact` | `ground_crack_hit` | 直径 2.0 | **0.2s** 径向（≈HitReact） | 随机自旋 | **卡宽 ×1.5 × 面积**；**无起步错峰** |

| 场景 | 骨架 | 强度 | 面积 | 触发 | 物 / 魔 |
|---|---|---|---|---|---|
| 弹道裂地 | Path | 档见 config | — | `StrikeSync` 飞行进度驱动 3 段 | **仅物理** |
| 命中裂地 | Impact | 同档 | 默认 ×1；拉满 ×1.5 | `SettleDamage` 同帧 | 同规 |
| 轨迹裂地 T4 | Path | 档 3 | — | 拉满出手突进，`MoveTrailDriver` | 同规 |

### 定位红线

- **卡牌定位圆**（脚下，直径＝卡宽）：
  `ArenaSlotLayout.AnchorCircleCenter / AnchorCircleDiameter`。
  **别拿投影圆**（罩身定径基准）——用在地面痕迹会散虚边。见
  [arena_stage.md](arena_stage.md) §四c。
- 圆心用 `GroundFoot`，禁止 `GroundUnder(卡心)`。
- 弹道必须 `YawAlong`；**起裂与生长都由 `StrikeSync` 弹道实时进度驱动**
  （禁止墙钟等分 / 贴花自走生长，见 P-57 / P-62）。进度 &lt;0.03 不起裂。
- 驱动式只接管「裂缝张开」；透明度与熔岩仍走贴花时钟；`_startDelay` 强制 0。
- 命中裂地只由 `SettleDamage` 触发，模板勿再单独 `PlayHit`。
- 命中直径必须明显大于卡宽（默认 ×1.5）。

存续**不吃倍速**，只吃 `DurationMul`（痕迹类；见 P-48）。

## 五、模块边界

| 层 | 文件 | 职责 |
|---|---|---|
| 触发 | `GroundCrackService.cs` | `Active` / `ShouldPlayHit` / `PathDriver` / `MoveTrailDriver` / `PlayHit` |
| 节拍 | `StrikeSync.cs` / `StrikeBeats.cs` | 飞行进度；突进驱动轨迹 |
| 参数 | `GroundCrackPalette.cs` | 颜色、强度表、模式规格 |
| 动画 | `GroundCrackDecal.cs` | 生长/熔岩/淡出；`ApplyStrength`/`ApplyArea`；`Roll` |
| 烘制 | `GroundCrackComposer.cs`（G4） | 2 件 prefab + 遮罩 |
| 诊断 | `GroundCrackProbe.cs`（G11） | 2 模式 ×3 档并排 |
| 熔岩层（未接） | `StandardizeLavaBurst.cs`（G12） | Effect8→`ground_lava_bloom` 留库 |

红线：形状归模式、烈度归强度、大小归面积，三者不得代偿；想参考厂包观感必须走
`vfx_standardization.md` 逐层去向表。

## 六、落地阶段（历史）

G1–G5 / G10–G14 **完成**；G6 场心大裂地**已废止**（2026-07-28，改只走档3+×1.5）；
G7 三舞台底图跟色、G8 冲击环 **待做**。碎块工具 G3 保留、现行 `ChunkCount=0`。

## 七、采购与红线

不需要为统一语言买包。若日后要更多遮罩形状，只看纯粒子/灰度图包；
**凡 Built-in 深度投影贴花技术一律不买**。

- 颜色禁止写死在 prefab；一律 `Palette`。
- 两骨架共用 `Compose`，只许 `ModeSpec` 分模式。
- 地面件必须 `VfxGroundLayer`。
- KriptoFX Decal **禁止接线**。
- 换底图须重跑 G3（若重新打开碎块）。

## 八、相关踩坑

P-46 漏接模板 · P-47 基色 alpha=0 · P-48 痕迹吃倍速 · P-51 形状与烈度勿混 ·
P-54 SmoothStep · P-55 勿叠厂包熔岩层 · P-56 机关动画 · P-57/P-62 弹道驱动生长。
详见 `docs/discipline/ai_workflow_pitfalls.md`。
