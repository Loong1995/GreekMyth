# 裂地表现语言（权威）

> 弹道裂地 / 命中裂地 / 场心大裂地共用**同一套材质与熔岩逻辑**，
> 结构为 **模式两类 × 强度三档 + 面积**。
>
> 上位：[performance_mechanisms.md](performance_mechanisms.md)、
> [arena_stage.md](arena_stage.md)、[assets_upload_guide.md](assets_upload_guide.md)、
> [vfx_standardization.md](vfx_standardization.md)。

## 一、现行结论（先读）

1. **厂包深度投影贴花在 URP 下不可用**（KriptoFX RFX1/RFX4/Magic Decal）：地面
   已是不透明写深度网格仍渲成品红盒面 → 裂地走自研平躺面片 + `GroundCrack.shader`。
2. **两件骨架 prefab**：`ground_crack_path` / `ground_crack_hit`。三档通吃、
   任意面积通吃；**不为分档或分大小另烘变体**。场心大裂地＝命中骨架 + 档 3 + 面积 3.2。
3. **颜色唯一真源** `VFX/GroundCrackPalette.cs`；触发唯一入口
   `VFX/GroundCrackService.cs`。演出模板禁止直接 `PlayAt("ground_crack_*")`。
4. **熔岩**：命中三档全开；弹道 **1/2 档关、3 档与命中同亮度**。生长/灭点逻辑共用。
5. **档 3＝档 2 的自然增强**（骨架放大 + 略提亮），禁止叠自带轮廓的厂包熔岩层
   （`ground_lava_bloom` 留库未接线，见 P-55）。

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
| **面积** area | 命中类大小倍率（默认 1＝卡宽×1.5） | `GroundHitArea` / `PlayArena` | `Decal.ApplyArea` |

强度表（`StrengthSpec`；`_MaskGain` 两模式同；熔岩按模式分流；持续按模式分档）：

| 档 | 枚举 | `_MaskGain` | 持续相对档 1 | 命中 `GlowPeak` | 弹道 `GlowPeak` | `SizeScale` |
|---|---|---|---|---|---|---|
| 1 轻 | `Light` | **1.15** | ×1 | 2.1 | **0** | ×1 |
| 2 重 | `Heavy` | **2.55** | 弹道 ×1.25 / 命中 ×1.5 | 3.6 | **0** | ×1 |
| 3 熔岩 | `Blaze` | **3.1** | 弹道 ×1.5 / 命中 ×2 | 4.4 | **4.4**（同命中） | **×1.35** |

命中三档 `_FrontWidth`/`_EmberFloor`：0.14/0.12 · 0.20/0.28 · 0.24/0.34。
弹道档 3 同用 0.24/0.34。

选档与面积（技能级配置见 [ground_crack_config.md](ground_crack_config.md)）：

| 优先级 | 条件 | 弹道/命中档 | 命中面积 |
|---|---|---|---|
| 1 | `EmpoweredStrike` | **3** | **×1.5** |
| 2 | profile `GroundStrengthTier` | 该档 | 专配或 ×1 |
| 3 | 未配 | **1** | ×1 |

配置约定：准备型物理主动群攻默认档 2（特例可抬到 3）；瞬发留 0（＝1）。
势能加强另叠场心大裂地（档 3 + 面积 3.2）。现有：`hector_warcry`＝**档 3**，
`hector_assault`＝档 1。

缝宽走 `_MaskGain`，**不要靠缩放面片**（会把放射骨架拉成椭圆）。

### 3.1 生长 + 熔岩

Shader：`Assets/Shaders/ClientBattle/GroundCrack.shader`（预乘 alpha）。
生长/灭点时序弹道与命中**共用**；弹道 1/2 档 `GlowPeak`=0，档 3 与命中同亮。

**裂缝生长**：`field`（命中＝径向距离，弹道＝`uv.x`）与 `_Growth` 比较镂空；
`Decal` 用 `Burst()` 揉成一阵一顿，推进满值约 1.47（覆盖抖动 + 层间滞后）。

**熔岩生长（跟着骨架）**：

| 项 | 值 | 语义 |
|---|---|---|
| `LavaDelay` | ≈0.12×GrowTime | 缝先黑着裂开，火晚一小步 |
| `LavaGrowMul` | ≈1.15 | 几乎跟着 `_Growth` 爬（喂 `_GlowGrowth`） |
| 抖动 | 起步 0.7~1.35×、爬速 0.85~1.3× | 收窄：不同步主要交给灭点 |

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

G4 烘 `RGB=白 + alpha=裂纹`（`masks/mask_crack_*.png`），颜色由调色板定。

| 用途 | 文件 | 规则 |
|---|---|---|
| 弹道 | `mask_crack_spine_{0..3}`（1024×256） | 2~4 段主缝错落接力（±40°），主缝约 **6.5~10.5px**；树杈式分叉从主缝长出（每段约 2~4 根、长短不一、稀疏）。飞行每段抽不同变体。 |
| 命中 | `mask_crack_radial`（512×512） | 10 主缝递归分叉（主缝 UV 宽约 **0.022~0.055**）+ 离心次级 + 中心碎裂 + 短连接缝；**禁止同心环** |
| ~~毛刺/arena~~ | — | 已并入主遮罩 / 场心改用命中+面积 |

烘制走固定哈希（可复现）；边缘用自写 `EdgeSmooth`（禁止 `Mathf.SmoothStep` 当
HLSL smoothstep，见 P-54）。写出前断言 maxA≥0.5。

### 3.3 每发随机化（去僵硬）

`GroundCrackDecal.Roll()` 每次出场重摇：裂缝推进 0.55~1.9×、熔岩寿命/停留、
整体错峰起裂 0~0.22s、灭点种子、子面片点火滞后。匀速 + 全场同参数＝机关动画
（P-56）。

## 四、骨架与场景

| 模式 | key | 烘出尺寸 | 生长 | 朝向 | 尺寸基准 |
|---|---|---|---|---|---|
| 弹道 `Path` | `ground_crack_path` / `_0`~`_3` | 长 2.5 × 宽 1.05 | 0.16s 轴向 | 调用方 yaw | 烘出尺寸；出场随机抽变体 |
| 命中 `Impact` | `ground_crack_hit` | 直径 2.0 | 0.28s 径向 | 随机自旋 | **卡宽 ×1.5 × 面积** |

| 场景 | 骨架 | 强度 | 面积 | 触发 |
|---|---|---|---|---|
| 弹道裂地 | Path | 档见 config | — | 物理弹道途中按实时进度分 3 段 |
| 命中裂地 | Impact | 同弹道档 | 默认 ×1；势能加强 ×1.5 | 受击者 `GroundFoot` |
| 场心大裂地 | Impact | 档 3 | **3.2** | `EmpoweredStrike` 额外叠一层 |

### 定位红线

- **卡牌定位圆**：`ArenaSlotLayout.CardCircleCenter / Diameter`；略大只加系数。
- 圆心用 `GroundFoot`，禁止 `GroundUnder(卡心)`（俯角下会偏到卡后）。
- 弹道必须 `YawAlong`；进度用弹道实时投影分量映射到 `fromFoot→toFoot`，
  禁止等分插值；进度 &lt;0.05 跳过。
- 命中直径必须明显大于卡宽（默认 ×1.5），否则中心被立绘挡住。

存续**不吃倍速**，只吃 `DurationMul`（痕迹类；见 P-48）。

## 五、模块边界

| 层 | 文件 | 职责 |
|---|---|---|
| 触发 | `GroundCrackService.cs` | `Active` / `PlayPath` / `PlayHit` / `PlayArena` |
| 参数 | `GroundCrackPalette.cs` | 颜色、强度表、模式规格 |
| 动画 | `GroundCrackDecal.cs` | 淡入/生长/熔岩灭点/淡出；`ApplyStrength`/`ApplyArea`；`Roll` |
| 烘制 | `GroundCrackComposer.cs`（G4） | 2 件 prefab + 遮罩 |
| 碎块工具 | `GroundChunkBaker.cs`（G3） | 底图现切（现行未接线） |
| 诊断 | `GroundCrackProbe.cs`（G11） | 2 模式 ×3 档并排 |
| 熔岩层（未接） | `StandardizeLavaBurst.cs`（G12） | Effect8→`ground_lava_bloom` 留库 |

红线：形状归模式、烈度归强度、大小归面积，三者不得代偿；想参考厂包观感必须走
`vfx_standardization.md` 逐层去向表。

## 六、落地阶段

| 阶段 | 内容 | 状态 |
|---|---|---|
| G1 | 地面不透明写深度 | **完成** |
| G2 | 验证厂包贴花 | **完成（否定）** |
| G3 | 碎块现切工具 | **完成**（现行 ChunkCount=0） |
| G4 | 骨架组合器 + 遮罩 | **完成**（持续演进自烘遮罩） |
| G5 | Path/Hit 接线 + 朝向 | **完成** |
| G6 | 场心大裂地 | **部分**（相机抖未接） |
| G10 | 收口服务 + 全模板 | **完成** |
| G11 | 静态探针 | **完成** |
| G12 | Effect8 熔岩层晋升 | **完成（未接线）** |
| G13 | 模式×强度+面积 | **完成** |
| G14 | 熔岩生长/灭点 + 弹道大小缝 + 两模式同熔岩 | **完成** 2026-07-26 |
| G7 | 三舞台底图跟色 | 待底图 |
| G8 | 冲击环 | 待做 |

## 七、采购与红线

不需要为统一语言买包。若日后要更多遮罩形状，只看纯粒子/灰度图包；
**凡 Built-in 深度投影贴花技术一律不买**。

- 颜色禁止写死在 prefab；一律 `Palette`。
- 两骨架共用 `Compose`，只许 `ModeSpec` 分模式。
- 地面件必须 `VfxGroundLayer`。
- KriptoFX Decal **禁止接线**。
- 换底图须重跑 G3（若重新打开碎块）。

## 八、相关踩坑

P-46 漏接模板静默无裂地 · P-47 基色 alpha=0 烤进 prefab · P-48 痕迹吃倍速看不见 ·
P-51 形状与烈度勿混一张表 · P-54 SmoothStep 语义 · P-55 勿叠自带轮廓外部件 ·
P-56 匀速+同参数＝机关动画。详见 `docs/discipline/ai_workflow_pitfalls.md`。
