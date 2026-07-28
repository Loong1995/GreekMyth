# 特效移动端预算与红线（接件前必读）

> **权威**：厂包件能不能上线、上线要裁到什么程度，以本文为准。
> 与本文冲突的"为了接近画廊"一律不成立。
> 上游纪律：[vfx_standardization.md](vfx_standardization.md)（怎么落盘）；
> 本文回答的是另一个问题：**落盘时到底该裁掉什么、为什么、裁掉的观感拿什么补**。
>
> 2026-07-28 立：起因是画廊标注里那几条（含贴花层 / 厂包投影贴花 URP 画不出 /
> 含屏幕扭曲层 / 移动端已开不透明贴图）一直只是"提示"，没有形成硬约束，
> 结果 7 件带屏幕折射、20 件带 `playOnAwake` 音源、5 件开着 World 粒子碰撞
> 一路上线（P-79）。本文件 ≤500 行。

## 一、目标机型与硬件模型（一切结论的前提）

| 项 | 取值 | 说明 |
|---|---|---|
| 下限机型 | 中低端 Android（Adreno 6xx 以下 / Mali-G5x 一代），2~3 年前千元机 | 过老（GLES2、无 Vulkan）不考虑 |
| 目标帧率 | 30 fps 稳定（≈33 ms/帧），高端 60 | 卡牌对战不靠高帧率，但**掉帧点集中在特效爆发**，观感最伤 |
| GPU 架构 | **Tile-Based Deferred Rendering（TBDR）** | 与桌面 IMR 的成本模型完全不同，见下 |
| 当前管线 | URP，`Mobile_RPAsset`：RenderScale 0.8、MSAA off、Depth on、**Opaque on** | `Assets/Settings/Mobile_RPAsset.asset` |

**TBDR 的两条成本铁律**（后面所有红线都从这里推出来）：

1. **带宽比算力贵得多**。移动 GPU 把屏幕切成 tile 在片上内存里渲染，只在
   最后写回主存。任何"把整屏拷出来再读回去"的操作（抓帧、全屏 blit）都会
   打断这个流程、强制主存往返，代价与画面复杂度无关，**恒定地贵**。
2. **半透明像素按覆盖面积计价**。加色/混合粒子不写深度、不做 early-Z，
   屏幕上每重叠一层就多一遍像素着色。1000 颗铺满屏的粒子 ≈ 十几倍全屏
   overdraw，这是移动端特效掉帧的**第一死因**，远比 draw call 严重。

## 二、五类高危层：风险、判据、处置

### 2.1 屏幕折射 / 扭曲层（Distortion、Refraction）—— 红线：一律摘

- **机制**：shader 采样 `_CameraOpaqueTexture`。为了让它有内容，URP 必须在
  不透明物体画完后**把整屏拷贝一份**（`m_RequireOpaqueTexture`）。
- **代价**：一次全屏拷贝（1080p×0.8 ≈ 150 万像素、RGBA 半精度约 4~6 MB）
  **每帧都在发生**，与场上有没有折射特效无关——只要 RP Asset 勾着就恒定支出。
  在中低端机上这单项就能吃掉 2~4 ms。
- **收益**：≈0。我们的舞台是低频大理石地面 + 卡牌正面，背后没有高频纹理，
  折射几乎看不出来（P-74 已实测：罩身"没罩住"的观感问题里，折射层完全不参与）。
- **处置**：落盘期 `NeutralizeRefraction` 中和（摘该节点的粒子系统与渲染器，
  **保节点与子层**——折射壳常是别的层的父节点，整删会带走子层，P-78）。
  全部件清零后，`Mobile_RPAsset` 关掉 Opaque Texture，才拿到真正的收益。
- **替代方案**（要"空间被扭曲"的观感时）：加色冲击环 sprite（`hit_*` 现成层）
  + 短促镜头位移（`CameraShaker`）+ 裂地 3 档。这三样合起来读作"冲击波"，
  且都是我们能定量控制的。

### 2.2 投影贴花（Projector / RFX Decal shader）—— 红线：一律摘，替代必须登记

- **机制**：Legacy `Projector` 组件在 URP 下**完全不渲染**（P-33）。厂包自带的
  `KriptoFX/RFX*/Decal` 也是配套 Legacy 管线的。
- **陷阱**：编辑器里（PC RP Asset）有时还能看见一点东西，**打包后直接消失**。
  于是"画廊里有焦痕、真机没有"，排查方向极易跑偏。
- **处置**：`StripDeadLayers` 摘掉整个贴花节点。
- **替代方案**：自研裂地系统 `GroundCrackService` / `GroundCrackDecal`
  （模式×强度×面积，见 [ground_crack_language.md](ground_crack_language.md)）。
  **摘掉的观感层必须在 `assets_upload_guide.md` 该 key 的行里写清替代去向**，
  否则下一个人只会看到"地面痕迹没了"。
- 未来若确实要通用贴花：走 URP **Decal Renderer Feature**（需开 DBuffer 或
  屏幕空间贴花，二者在中低端机都有额外带宽），必须先立项测帧，不得随手打开。

### 2.3 活跃粒子总量 —— 红线：单件估算 ≤ 用途预算，超了**等比稀释**

- **实测（2026-07-28，估算＝Σ(burst + rate×lifetime)）**：

| 件 | 估算活跃粒子 | 大头 |
|---|---|---|
| `aura_ares_might`（旧件） | ≈100 万 | 循环层 rate×寿命失控 |
| `aura_duel_defeat` / `ground_duel_defeat` / `ground_lava_bloom` | 30026 | Explosion + Trails |
| `hit_massive` | 20008 | 两层 burst 各 10000 |
| `cast_duel_launch` | 18766 | ParticlesRingLoop 5000/s × 2.5 s |
| `hit_shield_counter` | 5002 | 单层 burst 5000 |

  这些数字是按 PC 桌面做的原料自带的。单挑出阵双方同放＝再乘 2。
- **预算**（`VfxPackStandardizer.ParticleBudgetOf`）：定点/罩身 1500、
  地面 1500、场域氛围 2000（铺满全场故略宽，但正因为铺满全场，透支的就是填充率）。
- **裁法**：**等比稀释**（burst 数与 rateOverTime 同乘一个系数），不是把
  `maxParticles` 一夹了事——硬夹只是发到上限就不发了，先到的粒子占满、后面的
  整段消失，形状会缺一块；等比稀释保持分布与节奏，只是变稀。
- **要"更炸"的正确做法**：加**大颗但少量**的核心层（一颗大 sprite 顶一百颗碎屑）、
  加裂地档位、加震屏与顿挫（节奏），而不是加粒子数。

### 2.4 粒子碰撞 / 触发模块 —— 红线：一律关

- **机制**：`collision.type=World` 按粒子做物理查询，`quality=High` 是逐粒子
  逐帧；`sendCollisionMessages` 还会跨到托管层回调。
- **收益**：**0**。我们的舞台上**没有任何碰撞体**，算完也撞不到东西。
- **处置**：落盘期一律 `collision.enabled=false` / `trigger.enabled=false`。
  厂包靠碰撞触发的二段爆炸不适用于我们（定点件走碰撞子件原料，见标准化 §四.1）。

### 2.5 实时灯 / 音源 / 平台脚本 —— 红线：灯 ≤1 无阴影、音源 0、脚本 0

- **实时灯**：URP 前向渲染下每盏额外灯让受影响物体多走一遍光照循环；
  厂包动辄 4 盏（`aura_ares_might`）。留 1 盏关阴影是"热度还在、手机能跑"的折中。
  删灯**必须连同同节点的 RFX 灯曲线脚本**一起删，否则 Awake 抛
  `MissingComponentException`，整段演出协程当场死（P-68）。
- **AudioSource**：厂包件普遍 `playOnAwake=true` 自带音效，绕过 SFX 总线、
  与自研音效撞车、每次实例化占一个语音通道。一律删，声音只走 `SfxManager` key。
- **`RFX*_PerPlatformSettings`**：在 Awake 按平台偷改发射率——运行期不确定性，
  且在编辑期误跑会被烤进资产。移动端预算由流水线**显式**裁，不留给厂包脚本。
- **`WindZone` / `RFX*_CameraShake`**：影响本件之外的世界（吹歪别的特效、
  直接晃 `Camera.main` 与 `StageCameraRig` 打架），一律摘。

## 三、提示词红线（每次说"接某某特效"时都成立）

接件请求进来时，AI 必须把下面这段当作请求的一部分执行，**不需要人再说一遍**：

1. **唯一落盘入口**：`VfxPackStandardizer.Standardize(src, key, usage)`。
   禁止 CopyFull / 手拷 / `InstantiatePrefab`+`SaveAsPrefabAsset` 旁路（P-77）。
2. **五摘一夹**：屏幕折射摘、投影贴花摘、粒子碰撞触发关、音源摘、
   平台/风区/震屏脚本摘；活跃粒子等比稀释到用途预算。
   —— 这六项**没有"这次特殊"**。要例外只能扩 `VfxUsage` 并在本文加一节说明。
3. **摘掉的观感层必须给替代方案并登记**（贴花→裂地、折射→冲击环+震屏），
   写进 `assets_upload_guide.md` 该 key 的行。
4. **不得靠"关红线"去接近画廊**。画廊与运行期的差异表见标准化 §一；
   慢放（K 键 0.25×）拍的板 1× 下永远达不到，要么给足时间要么降低期待。
5. **验收看成品，不看 log**：`GreekMyth/特效/体检 标准件流水线四项` 必须全绿
   （现已含移动端预算四项：折射 / 碰撞 / 粒子总量 / 音源灯）。
6. **改预算数字＝改全项目帧率与观感的平衡点**，只能在
   `VfxPackStandardizer` 常量区改，并回来更新本文的实测依据。

## 四、"我就是要那个效果"——需求到方案的对照表

开发者/策划点名要的往往是**观感**，不是那层实现。对照表用于把观感需求翻译成
预算内的做法（左列是常见说法，右列是允许的实现）：

| 想要的观感 | 预算内做法 | 禁止 |
|---|---|---|
| 空间扭曲 / 冲击波 | 加色冲击环 + 镜头位移（`CameraShaker`）+ 3 档裂地 | 保留 Distortion 层 |
| 地面焦痕 / 法阵 / 血迹 | `GroundCrackService` 自研贴花（可配模式/强度/面积） | Projector / 厂包 Decal 层 |
| 更炸的爆发 | 大颗少量核心层 + 裂地升档 + 震屏 + 顿挫节奏 | 提高粒子数 / 放宽预算 |
| 满屏氛围（雷暴/风沙） | 场域氛围件（`ambient_`，钉地面中心、层序压卡下、预算 2000） | 每人挂一份光环 |
| 打击的"热度" | 保留 1 盏无阴影点光 + 加色闪一帧 | 多盏实时灯 |
| 厂包自带的音效 | 提取音频文件 → 走 `SfxManager` key | 留 `AudioSource` 在件上 |
| 慢放下才好看的爆发 | 顺序演出给足真实时间（`EmitWindow`），或降低期待 | 调参数硬追 |

## 五、全局渲染设置的联动（改一次影响全项目）

| 设置 | 现状 | 结论 |
|---|---|---|
| `Mobile_RPAsset` **Opaque Texture** | 开 → 待所有折射清零后**关** | 关掉才拿到 §2.1 的收益；关后任何采样 `_CameraOpaqueTexture` 的材质会出错，因此它与"折射零残留"是**绑定**的 |
| `Mobile_RPAsset` **Depth Texture** | 保持开 | 软粒子（Soft Particles）与自研裂地贴合地面依赖深度；关掉会让粒子与地面出现硬边 |
| RenderScale 0.8 / MSAA off | 保持 | 已是移动端合理档位 |
| `PC_RPAsset` Opaque Texture | 保持开 | 画廊在编辑器里预览**原料**时仍需要它；这也意味着**画廊看到的折射永远比真机多**——属于已知差异，不是 bug |

## 六、现状排查结论（2026-07-28 首轮）

`GreekMyth/特效/体检 标准件流水线四项`：**63 件，30 件不合格**。分布：

| 隐患 | 件数 | 代表 |
|---|---|---|
| 屏幕折射层残留 | 6 | `cast_duel_launch`、`ground_duel_defeat`、`aura_duel_defeat`、`ground_lava_bloom`、`hit_clash`、`hit_shield_counter`（另有旧件 `aura_ares_might`） |
| `playOnAwake` 音源 | 20 | 绝大多数是流水线之前的自研/占位件（`hit_generic`、`aura_*`、`cast_*`…） |
| 粒子碰撞未关 | 5 | `hit_lightning`、`ambient_thunder_storm`、三件 Magic Effect8 系 |
| 活跃粒子超预算 | 6 | `aura_ares_might`≈100 万、三件 30026、`hit_massive` 20008、`cast_duel_launch` 18766 |
| 实时灯超限 | 1 | `aura_ares_might`（4 盏，旧件） |

**根因不是"某几件没做好"**：折射清洗当初只写在 `Shroud` 用途分支里，
Anchor/Ground 用途从头到尾没人摘；粒子预算与碰撞根本没有任何检查；
存量自研件（早于流水线）**从未被任何流程扫过**。见 P-79。

处置：① 清洗规则上移为**全用途**的 `ApplyMobileBudget`；② 体检加四项预算检查；
③ 新增就地清洗菜单 `GreekMyth/特效/清洗 存量标准件（移动端预算…）`
（用途按 key 前缀反推，只做与用途无关的通用清洗，幂等可重跑）；
④ 全部清零后关 `Mobile_RPAsset` 的 Opaque Texture。

## 七、入口速查

| 动作 | 入口 |
|---|---|
| 落盘（唯一入口） | `VfxPackStandardizer.Standardize(src, key, usage)` |
| 全量体检（含预算四项） | `GreekMyth/特效/体检 标准件流水线四项`（报告 `Temp/vfx_audit.txt`） |
| 存量件就地清洗 | `GreekMyth/特效/清洗 存量标准件（移动端预算…）`（报告 `Temp/vfx_clean.txt`） |
| 预算常量 | `Assets/Editor/GreekMyth/VfxPackStandardizer.cs` 顶部常量区 |
| 真机帧率诊断 | `Test/FrameSpikeProbe.cs`（Tester 勾 ShowDiagnostics）；以**独立版**为准 |
