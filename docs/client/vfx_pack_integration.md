# 已购特效包改造与应用方案（VFX Pack Integration）

> **权威范围**：已购第三方特效包（KriptoFX RFX4 / Magic Effects Pack v1、Vefects）
> 在本项目 URP + 近 3D 卡牌舞台下**哪些层能用、哪些要改造、怎么接**。
> 本文档是「包资产 → 我方 key」这条通路的唯一方案与操作手册。
>
> 分工（不重复其它文档）：
> - 资源 key 清单与占位协议 → [assets_upload_guide.md](assets_upload_guide.md)
> - 现行宙斯/雅典娜等具体接线快照 → [vfx_playback_scheme.md](vfx_playback_scheme.md)
> - 裂地专项（三层配方/遮罩烘制/档位）→ [ground_crack_language.md](ground_crack_language.md)
> - 排序层级总表 → [rendering_layout.md](rendering_layout.md)

## 一、结论速览

包里的内容按**渲染层类型**分五类，处境完全不同。以 Magic Pack v1（33 个
Effect）为样本实测统计：

| 类别 | 代表 shader | 规模 | 结论 |
|---|---|---|---|
| 粒子 / 拖尾 | `RFX1_Particle`、`RFX4_UberParticleShader` | 包内绝对主力 | **直接可用**（已在用：hit_lightning 等） |
| 闪电 / 链 | `RFX1_Lightning`、`RFX4_Lightning` | 十余材质 | **直接可用** |
| 网格层（护盾/冲击波/Spikes/岩石） | `RFX1_UberDistortion` 的非扭曲用法等 | 69 材质跨多数 Effect | **需改造**：排序未被托管，会与卡牌抢层 |
| 屏幕扭曲 | `RFX1_UberDistortion`、`RFX4_UberDistortion` | 同上（同 shader 双用途） | **需性能决策**：依赖不透明贴图，移动端默认关 |
| 深度投影贴花 | `RFX1_UberDecal`、`RFX4_UberDecal` | 35 材质跨 20/33 个 Effect | **判定不可用**（2026-07-25 受控实测，见 §二末） |

一句话：**粒子/闪电这两类（包的主体）已经通了；网格层是最高性价比的解锁项；
贴花放弃或降级为平躺面片；扭曲要先做移动端取舍决策。**

包里为 3D 近景角色设计的层（水面、地形起伏投影、脚下涟漪）在 55° 俯视 +
2D 卡牌的舞台上没有承载对象，不计入损失，也不要硬塞。

## 二、硬约束：两套 RP asset 的差异（决策优先级最高）

```20:23:Assets/Settings/Mobile_RPAsset.asset
  m_MsaaSampleCount: 1
  m_RenderScale: 1
  m_RequireDepthTexture: 0
  m_RequireOpaqueTexture: 0
```

`Assets/Settings/PC_RPAsset.asset` 同两项为 `1`；`ProjectSettings/QualitySettings.asset`
里 PC 档的 `excludedTargetPlatforms` 含 Android / iPhone，`m_PerPlatformDefaultQuality`
的 `Android: 0` 指向 Mobile 档。

三条推论（**这是全文最要紧的部分**）：

1. 编辑器里"可靠预览"走的是 PC 档（深度 + 不透明贴图都开）。**编辑器通过
   ≠ 真机通过。**
2. 所有**屏幕扭曲层**在真机上会失效（`_CameraOpaqueTexture` 无来源）。
3. 所有**深度投影贴花**在真机上是双重不可能：既无深度纹理，管线语义也不匹配。

另有一个按材质开关的隐患：软粒子淡出在两个包里都是 shader feature
（Magic `SoftParticles_ON`、RFX4 `_FADING_ON`），启用它的材质在移动端会因缺深度
而淡出失真（表现为与地面/卡牌交界处硬边）。接件时逐材质检查该关键字。

**决策项 D-VFX-1（2026-07-25 已定：开）**：`Mobile_RPAsset` 置
`m_RequireDepthTexture: 1`、`m_RequireOpaqueTexture: 1`、
`m_OpaqueDownsampling: 1`（2x 双线性，把全屏拷贝的带宽压到 1/4）。
换来扭曲层与软粒子在真机上有数据来源。**真机实测尚未做**，若帧率不达标，
回退动作是把两项改回 0 并按"扭曲层一律摘掉"接件。

### 贴花实测结论（决定裂地配方走向）

深度贴花不是"编译不过"，而是"编译得过也画不出来"。受控实测（战斗场景、
`Time.timeScale=0` 定格、相机离屏渲染取帧）四项条件全部满足仍无任何像素：

- shader 可用：`KriptoFX/RFX1/Decal`、`RFX4/Decal` 均 `isSupported=True`，1 pass
- 相机深度：`requiresDepthTexture=True`（PC 档管线默认开）
- 地面写深度：`ArenaGround` = URP/Unlit，`RenderType=Opaque`、`queue=2000`、`ZWrite=1`
- 贴花全显：禁掉曲线脚本、手动置 `_Cutout=0`，`isVisible=True`、包围盒覆盖地面

**易踩的反证陷阱**：在 Magic Pack 可靠预览场景里"看到贴花亮起来了"不能作数 ——
预览场景在循环重播完整 Effect，看到的是它自己那份实例的火光与粒子，不是被单独
留下的贴花层。要判定必须在战斗场景里定格取帧。

因此维持红线：**厂包贴花件一律不接**，裂地继续走自研三层配方；厂包只贡献
粒子/闪电/网格层。`VfxStandardizer` 已会自动摘除 prefab 里残留的死贴花节点。

## 三、改造项清单

按性价比排序。成本为单人工作量估算，不含真机实测。

| 项 | 内容 | 成本 | 风险 | 状态 |
|---|---|---|---|---|
| A | 排序泛化：`EnsureVfxSorting` 覆盖全部 `Renderer` | 0.5 天 | 低 | **已完成** 2026-07-25 |
| B | 尺寸归一：`VfxFitter` 按目标基准折算世界尺寸 | 1 天 | 低 | **已完成** 2026-07-25，全量 52 件 |
| C | 生长 + 自发光配方（cutout 阈值扫描） | 1 天 | 中 | 待做 |
| D | 贴花降级为平躺 quad（`USE_QUAD_DECAL` 分支） | 1~2 天 | 中 | 待定 |
| E | 移动端扭曲取舍（含真机实测） | 0.5 天 + 实测 | — | RP 已开，待真机实测 |

### A. 排序泛化

现状只抬粒子渲染器：

```210:217:Assets/Scripts/ClientBattle/VFX/VFXManager.cs
        static void EnsureVfxSorting(GameObject instance)
        {
            if (instance == null) return;
            if (instance.GetComponent<VfxGroundLayer>() != null) return;
            const int minOrder = 45;
            foreach (var r in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if (r.sortingOrder < minOrder) r.sortingOrder = minOrder;
        }
```

护盾、冲击波、Spikes、锁链、岩石这些是 `MeshRenderer` / `LineRenderer` /
`TrailRenderer`，sortingOrder 停在 0，会与卡牌立绘（`UnitView` 用 0/1/32/55 档）
抢层。改为遍历 `Renderer` 基类即可，`VfxGroundLayer` 豁免规则不变。

**验收**：接一个带护盾+冲击波的 Effect（如 Effect20/24），在最近排与最远排各
放一次，卡牌前后关系稳定、不闪烁。

### B. 尺寸归一

包里效果按 3D 世界尺度做（Effect8 的投影盒 `localScale` 是 10×5×10），我方卡宽
实测 2.04。现状是每接一个都人肉试缩放，这是"接件工时"的主要来源，也是
"尽量完全发挥"的真正瓶颈。

做法与 `GroundCrackDecal.ApplyCardWidth` 同源：组件声明"我要对齐什么基准"
（卡宽 / 逻辑圆半径 / 固定世界值）加一个倍率，出池时按运行时布局折算。
基准量取 `StanceLayout.CardWidth` 与 `ArenaSlotLayout.CircleRadius`。

**验收**：同一 prefab 在窄屏与宽屏下相对卡牌的视觉占比一致（`CameraFitter`
两档取景各截一张对比）。

落地（2026-07-25）：`VfxFitter`（`Assets/Scripts/ClientBattle/VFX/VfxFitter.cs`）
声明 `Reference`（CardWidth / ArenaDiameter / None）+ `Factor` + `BakedBasis`，
出池时按运行时基准折算 `localScale`。

**参照卡宽 ≈ 1.730**（单体制：缩后主战场六等分格 × `CardScaleBoost` 1.5，
`StanceLayout.CardWidth`，设计 16:9、θ=0 基准）。
旧双档已废止：交错 2.041 / 非交错 1.206（见 P-38 / P-44）。布局变更后须跑
「标准化」回填 `BakedBasis`，否则旧基准会把特效按卡宽比错误放大/缩小。
判定归一化是否中性：旧件在真实场景里最终 `lossyScale` 与 Factor=1、
BakedBasis=当前参照卡宽时一致。

三个配套工具（菜单 `GreekMyth/特效/`）：

- **特效画廊（一键）**：全项目可用特效的审核台。把**我方标准件 + 全部厂包**
 放到**真实舞台 + 真实卡牌**上逐件过。判"接进舞台后好不好用"必须用它，
 厂包自带预览判的是另一回事（那里判"这效果本身好不好看"）。
 详见下节。

- **体检 全量 VFX prefab**：查空材质、品红/错误 shader、Built-in 专属
 shader 与组件、缺 `VfxFitter`。判据坑见 P-37。
- **标准化 尺寸归一 + 清理残留**：给非地面件补挂 `VfxFitter`（参照值变更时会
 回填已有件的 `BakedBasis`），清 `Projector` 与死贴花节点。结果：补挂 49 件、
 地面件跳过 3 件、清理死贴花 1 处（`aura_ares_might` 的 `Decal2`）。
 此后体检 **52/52 全绿**。

地面件（带 `VfxGroundLayer`）由 `GroundCrackDecal` 自管尺寸，标准化时跳过，
避免两套缩放叠乘。

### 特效画廊（审核台）

`Assets/Scripts/ClientBattle/Test/VfxGalleryRunner.cs` +
`Editor/GreekMyth/VfxGalleryLauncher.cs`。菜单 `GreekMyth/特效/特效画廊（一键）`。

**覆盖面（2026-07-25，898 件 / 8 组）**：我方标准件 52、Magic Pack v1 61、
RFX4 54、Vefects 连击闪卡 308、Cartoon FX Remaster 170、2D 斩击 119、
彩色系列 132、闪电链 2。厂包 prefab 不在 Resources 下，运行期加载不到，
由启动器用 `AssetDatabase` 编辑期收集后注入 Runner 的序列化字段。

**筛掉非特效件的判据**（比名字黑名单可靠）：必须含 `ParticleSystem` 或
`LineRenderer`/`TrailRenderer`，且不得含 `SkinnedMeshRenderer`。Magic Pack 的
`Character_Effect*` 是一整套 challenger 蒙皮角色，靠这条剔除；`/Demo/`、
`/SceneResources/`、`/Models/`、`/Materials/` 按路径排除。共剔掉 37 件。

**操作**：←→ 切件（PgUp/PgDn ±10）、↑↓/Tab 切包、R 重播、F 切锚点、
T 切目标卡、B 自动弹道、C 定位圆定径（罩身锚点不受 C 影响，它的定径是规格）、
K 慢放 0.25×、**J 卡牌深度代理开关（§8.3 的 A/B 对比）**、G 自动重播、
`-`/`=`/`0` 试穿缩放、**M 记可用 / N 记否决 / P 导出**
（写 `Temp/vfx_audit_marks.txt`，Play 停掉也不丢）。

### 弹道模式（厂包主件唯一能演出来的方式）

厂包分**主件**（`Prefabs/Effects/EffectN`）与**碎片件**
（`Prefabs/EffectParts/EffectN_Collision`）。主件不是"放一点上播"的散件，而是
一整套出手流程：自带位移脚本飞出去，撞到碰撞体时再由 `EffectsOnCollision`
生成自己的命中件。单锚点摆放下这类件必然演不出来 —— 这是"厂包标准化不出
可用组件"的头号误判来源，不是件不可用。

Runner 自动识别（组件类型名含 `TransformMotion` / `PhysicsMotion` 即视为主件，
比按文件名黑名单可靠），自动切弹道：**施法者卡牌定位圆心 → 敌方卡牌定位圆心**。
B 键可关掉自动、F 键里也有手动「弹道→敌卡」档。四件事缺一不可：

| 必备 | 不做的后果 |
|---|---|
| 朝向 `LookRotation(施法者→目标)` | Target 为空时 RFX1 沿自己 local forward 飞，identity 旋转＝朝屏幕深处冲出舞台 |
| 反射写 `Target`（GameObject/Transform 两种签名都要认） | 同上；ClientBattle 是独立 asmdef，引用不到厂包类型，只能按字段名写 |
| `Distance`/`MaxDistnace` 改成实测两脚距离、`Speed = 距离/0.9s` | 厂包默认 `Distance=30`（逻辑圆半径才 8）、`Speed=1`，等于慢慢飞出舞台 |
| 落点有可撞的东西 | 命中件**只在 raycast 命中分支生成** |

**落点碰撞体的演进（2026-07-25）**：原先在落点放一个不可见球（半径＝定位圆）。
`VfxCollisionStage`（§8.1）落地后改为**打真卡牌碰撞盒** —— 弹道起止点也随之从
「定位圆心（贴地）」改成「卡身中心」，因为卡牌碰撞盒在卡面高度，贴地平飞会从
盒下方擦过打到地面，读作"打偏了"。落点标记退化为纯 `Target`（朝哪飞），不再兼职碰撞。

厂包生成的命中件是**场景根节点**（`CollisionEffectInWorldSpace`），不挂在弹道下，
所以 Runner 还要额外给它补排序抬升、并在换件时按 `(Clone)` 后缀清场
（我方件全走池化、不会有这后缀；池化实例都有父节点，不会被误清）。

**碎片件排到主件之后**：纯路径 Ordinal 排序会把 `EffectParts` 排在 `Effects` 前
（'P' < 's'），一进 Magic Pack 就是连续 28 件不能独立成立的命中碎片。

**慢放是这类件的必需项**：整段出手只有 0.9 秒，正常速度下"闪一下就没了"，
判不出弹道形态与命中衔接。

**六个锚点**（同一件在不同锚点上可用性天差地别）：卡牌身上 / 卡牌脚下 /
**脚下平躺**（判能否当地面法阵用；如 `aura_aegis` 自带绕 X 转 270° 的符文环层，
平躺放脚下即成地面法阵）/ 棋盘中心 / 弹道→敌卡 / **罩身**（见下）。

**HUD 就地体检**：显示该 key 现接在哪些战法上（反射扫 `PerformanceProfile`
的全部 string 字段，新增 key 字段无需回来改），并标出该件是否含贴花层
（URP 画不出）、屏幕扭曲层（移动端依赖不透明贴图）、品红 shader。

**播放路径**：我方件走 `VFXManager.PlayAt/PlayOn`（连排序规则一起验）；
厂包件直接实例化并补一次排序抬升（`sortingOrder≥45`），否则会被地面/卡牌盖住，
看不见就无法审核。

**换包默认姿势 + 定位圆定径（2026-07-25）**：切到厂包组时自动切成
「目标＝雅典娜 / 锚点＝卡牌脚下 / 定位圆定径开」，并把【卡牌定位圆】画成一圈青环，
好判有没有对准圆心、有没有溢出。定径＝量实例地面投影的最大边，缩到圆直径
（＝卡牌影子的外接圆直径，定义见 `arena_stage.md` §四c）。
两个实测要点：包围盒只推 `Simulate(0.12s)` 量**起手核心**，推到 0.35s 时冲击件的
碎屑已飞散，据此定径会把主体缩到 ×0.13 直接看不见；缩放钳在 [0.25, 20]，
宁可略溢出也不缩成一个点。定径开关是 C —— 「彩色系列」这类自带 8m 地面光斑的件
定径后只有 ×0.25，要判本体观感需临时关掉。

### 罩身锚点（`VfxShroudFitter` + `VfxShroudFollower`）

「罩身件」＝把整张卡罩在里面的立体件。画廊第 5 锚点只调 `Fit` 审观感；
**战斗挂载**必须再挂 `VfxShroudFollower`（`FitAndFollow`）：整件世界竖直钉在
持有者定位圆心，melee 完全跟随、平时同样严格锚定定位圆；地面 Decal 直径在 Fit
时已钉死。落盘必须走 `VfxPackStandardizer` 的 `VfxUsage.Shroud`（摘折射等，
P-77）；挂载期 Fitter/Follower **不裁层**。去石块/关 Trigger 等个性名单写在
各技能 Wire。个案显隐（如战神之勇奇偶渐隐）另挂组件。

规格三条，**其余一概不补偿**：

1. **世界竖直**（`rotation = identity`）。实测这类件的壳本来就是沿世界 Y 的竖柱：
   Effect31 在 identity 下 Shield 包围盒 2.89 × **8.66(Y)** × 2.89、
   Lightning 2.45 × 7.36(Y) × 2.45、Decal 平躺。**不要补旋转** ——
   跟卡同倾过（为掩盖"卡不畸变、圆畸变"），结果罩子斜着从卡面穿出。
2. **等比缩放，水平切面对齐卡牌定位圆**，底面坐在地面上。
3. **定径基准 = 件里 Y 向最高的那个渲染器（＝壳本体）**，在 `Simulate(0.6s)`
   （壳已成形、碎屑未飞远）时量一次。

三个踩过的坑，都会让这类件被误判为"没效果"：

| 错法 | 后果（实测） |
|---|---|
| 按整件包围盒定径 | 混着世界空间模拟 + 重力飞散的碎石/烟，本地高度 0.12s 时 ~10.5、后期 ~30，量到的是碎屑范围；曾把壳压到地面以下 5 米 |
| 按自带碰撞体定径 | 碰撞体是"被罩住的人形"（Effect31 胶囊 2.0×2.5）而不是罩子（2.89×8.66），罩子会高出卡顶三倍多 |
| 为"顶部齐卡上缘"单独压 y | 8.66 高的壳压到 ×0.29 变成一张薄饼，与同一件在棋盘中心等比展示时观感完全不一致（用户一眼看出"这不是竖直的罩子"） |

摆位用**解析推算**而非二次量测：粒子每帧都在变，量第二遍拿到的是另一个时刻的
盒子，据此对齐会整体推错半个身位。缩放绕 transform 原点做，故盒子相对原点的
偏移同比例缩放即可。（另：`Collider.bounds` 读的是物理场景，刚 Instantiate
+ 摆位的同一帧直接读是过期值，须先 `Physics.SyncTransforms()`。）

圆环本身必须**躺在地面平面里**：LineRenderer 默认 `alignment=View` 会让每段带子
朝相机竖起来，俯角下这圈线是斜立的，读起来"不像画在地上"。改为物体绕 X 转 90°
＋本地坐标下环＋`alignment=TransformZ`。圆心/半径直取 `ArenaSlotLayout`，
**不做任何额外补偿** —— 圆就是地上那个指定圆，看着不对就是画法错了，不是圆错了。

**不给厂包件做"内容底面对齐地面"的抬升**：厂包 `*_Collision` 件的原点是**爆点**、
内容绕原点上下对称（Effect10_Collision 上下各 8 世界单位）。对齐底面等于把爆点
抬到半空（实测抬了 4.1），读作"在空中炸"。正确做法是爆点落在定位圆心、下半截被
不透明地面挡掉 —— 这就是命中该有的样子。只有整件完全在地面以下时才兜底抬一次。

**厂包件必须由审核台自己起播**：很多厂包粒子是 `playOnAwake=false`
（等它自己的控制脚本或示例场景触发），直接实例化后一片空白 ——「彩色系列」
整包 132 件都如此，一度被误判为"整包无效果"。Runner 在实例化后统一
`Clear + Play(withChildren)`（只在根级调，避免子级重复触发）。

### C. 生长 + 自发光配方

包里"酷炫"的手法不在贴图，在两点：`RFX1_ShaderFloatCurve` 用曲线把 `_Cutout`
从 0 推到 1（Effect8 的 `GraphTimeMultiplier=4`），裂缝/焦痕是**沿噪声阈值生长
出来**的；以及缝口 tint 亮度 1.5 的**过曝自发光**。我方 `GroundCrackDecal`
目前只统一推 alpha，没有生长也没有自发光。

这两点自研成本很低，且对裂地与其它地面痕迹通用，是"低级朴素 / 高级酷炫"
分级的核心手法（见 §五）。

### D. 贴花降级为平躺 quad

`RFX1_UberDecal` 自带 `USE_QUAD_DECAL` 分支，走该分支时**完全不采深度**，
退化为一张平躺面片：

```223:232:Assets/KriptoFX/Magic Effects Pack v1/Shaders/RFX1_UberDecal.shader
#if USE_QUAD_DECAL
		float2 uv = i.uv;
		float3 opos = uv.xyy + 0.5;
		float projClipFade = 1;
#else

		float2 screenUV = i.screenUV.xy / i.screenUV.w;
		float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);
		float3 wpos = WorldSpacePositionFromDepth(screenUV, depth, i.inverseVP);
		float3 opos = mul(unity_WorldToObject, float4(wpos, 1));
```

我方地面是纯平面，平躺面片与投影贴花观感等价（`ground_crack_language.md` §五
G9 已判定），所以这条路在观感上无损失。不确定性在于逐材质要验 UV 与混合模式，
以及移动端仍需该 shader 在 URP 下正常编译。

**复测已做（2026-07-25）**，结论比 P-33 更精确：shader 编译通过、`isSupported=True`，
所以品红不是它的常态；它的失败形态是**静默不出图** —— 深度分支在 URP 下重建不出
正确的世界坐标，整片被 clip 掉。既然深度分支已判死，D 若要做只剩 quad 分支一条路，
而 quad 分支等价于"一张平躺面片"，那正是我方 `GroundCrackDecal` 已经在做的事。
**故 D 降级为低优先级**：只有当某个厂包贴花的贴图本身值得单独拿来用时才动，
且届时直接把贴图接进我方面片，不必带上厂包 shader。

### E. 移动端扭曲取舍

两条路：Mobile 档开 `RequireOpaqueTexture` 并真机实测帧耗；或给扭曲层做统一
降级开关（真机换成一层加色雾）。取决于 D-VFX-1。

## 四、后续接件标准流程（每接一个新特效照此走）

> **现行纪律权威已收口到 [vfx_standardization.md](vfx_standardization.md)**：
> 人在画廊点名 → AI 默认标准化并加载；**无 GUI 晋升队列**。
> 下文保留改造期拆层/验收细节，与标准化文档冲突时以标准化文档为准。

1. **预览定件**：特效画廊（或包预览启动器）锁定 Effect；禁止凭商店截图选件。
2. **拆件，不整挂**：只取需要的层。整包直挂必然带进贴花盒、扭曲层、
   `RFX1_Decal`/`Projector` 这些无效或有害组件。命中类优先取 `*_Collision` 件。
3. **逐层过筛**（对照 §一 表）：贴花盒 → 删；扭曲层 → 按 D-VFX-1；
   材质带 `SoftParticles_ON` / `_FADING_ON` → 关掉或接受真机硬边。
4. **落盘**：我方自有 prefab，`Assets/Resources/ClientBattle/VFX/<key>.prefab`，
   key 随 assets_upload_guide。**禁止 Profile 直指包目录。**
5. **可重跑脚本优先于一次性手调**：参数写进 Editor 工具常量（如
   `WireMagicPackZeusAthena`）；不要做「给人点的标准化 GUI」。
6. **接线**：`PerformanceProfile` 三级查找，演出零硬编码。
7. **排序 / 颜色 / 验收**：同 vfx_standardization §三.5 与下文历史条目。
## 五、分级（等级）方案

目标：同一机制按武将/技能档次给不同强度的表现，低档朴素、高档酷炫。

**触发侧的接线已经存在**，不需要改演出代码：

```409:410:Assets/Scripts/ClientBattle/VFX/Performances/DefaultPerformance.cs
        static string GroundKeyOf(string configured, string fallback) =>
            string.IsNullOrEmpty(configured) ? fallback : configured;
```

高档技能的 profile 指向高档 key 即可（`GroundPathKey` / `GroundHitKey` /
`HitVfxKey` 等同理）。要设计的只是"等级"这个维度怎么产出资产：

- **层数差异走烘制期**：`GroundCrackComposer` 这类组合器按**骨架**产出件
  （层数、遮罩、碎块量都在烘制期写死，`GroundCrackPalette.ModeSpec`）。
  裂地已按此办：2 件骨架，**不为等级另烘变体**。
- **强度差异走运行期**：裂地的落地形态是 `GroundCrackDecal.ApplyStrength`
  （缝宽/持续/亮度整档写入）+ `ApplyArea`（大小倍率）。注意 `VFXManager` 池是
  按 key 分桶的，同 key 不同等级共用实例必须在出场时完整复位，否则串味
  （裂地就吃过这个亏：根旋转没复位，上一发的 yaw 留在实例上）。

**推荐**：骨架（层数）走烘制期，微调（强度）走运行期。等级绑技能或武将稀有度、
数据落 `PerformanceDatabase`，**不要绑武将 id**。

## 六、红线

1. 包目录 `Assets/KriptoFX/**` 视为第三方只读资产：可读取、可作为烘制来源，
   **不改其中文件**（改了会被包升级覆盖，且无法复现）。
   两个既定例外：厂商官方 URP 补丁；Unity API 更新器的机械改名（旧 API 弹的
   Script Updating Consent 模态框会卡死编辑器，见 pitfalls P-36，必须同意）。
2. `PerformanceProfile` 禁止直接引用包目录 prefab，必须经我方 `Resources` key。
3. 禁止引入 Built-in 管线专属组件（`Projector`、`RFX1_Decal` 的投影模式）。
4. 地面族颜色唯一真源 `GroundCrackPalette`；碎块必须从舞台底图现切。
5. 任何"编辑器里好了"的结论，涉及扭曲/软粒子/贴花时**不得**写进文档当成品结论，
   必须标注真机未验（成因见 §二）。
6. 新增 key 先登记 assets_upload_guide 再写代码。

## 七、落地阶段表

| 阶段 | 内容 | 状态 |
|---|---|---|
| V0 | 包内资产按渲染层分类盘点（本文档 §一/§二） | **完成** 2026-07-25 |
| V1 | 决策 D-VFX-1：移动端不透明贴图取舍 | 待人工裁定 |
| V2 | 改造 A 排序泛化 | 待做 |
| V3 | 改造 B 尺寸归一 `VfxFitter` | 待做 |
| V4 | 改造 C 生长 + 自发光配方 | 待做 |
| V5 | 分级矩阵（§五）落到裂地三档 | 待做，依赖 V4 |
| V6 | 改造 D 贴花 quad 降级（可选） | 待定，先做半小时复测 |
| V7 | 真机验证扭曲/软粒子结论并回填本文档 | 待做，依赖 V1 |
| V8 | **合成底座三件套（§八）** | **完成** 2026-07-25 |

## 八、合成底座：三件"底层但局部"的改造（2026-07-25 落地）

逐件调参救不回来的那批件，病根不在管线选型，而在**合成模型**：我们的卡牌是
透明 Sprite、舞台没有碰撞体、URP 贴花通道没开。三件事补上后，包件的"完整流程"
才有承载对象。**结论：留在 URP。** 退回 Built-in 虽然能让包 100% 原生，
但要拿整个渲染层 + 未来资源生态去换，明确不划算（详见本节末对比）。

### 8.1 特效专用碰撞层（`VfxCollisionStage`）

厂包主件不是"摆一点上播"的散件，而是**位移 → 撞碰撞体 → 生成命中件**
（`EffectsOnCollision`）的一整套流程；碎石/水花也靠粒子 Collision 模块落地。
舞台底图是特意去掉碰撞体的，全场零碰撞体 → 这类件永远只演前半段。

- 地面：`VfxGroundCollider`，顶面贴齐 `ArenaStageView.GroundY`；
- 每卡：`VfxHitBox`，尺寸取运行期卡面，挂在卡节点下随卡倾斜；
- 层：`VfxCollision`（工程 layer 8），缺层退回 Default 并告警一次；
- **红线**：碰撞体是表现层附属物，**禁止任何逻辑读它做判定**（结算全在服务器）。

实测：Effect12 从雅典娜射向敌卡，`Effect12_Collision` 生成在敌卡碰撞面上
（z≈5.17，卡在 5.46），命中爆裂落在"人"身上而不是隐形球上。

### 8.2 URP 贴花通道（`DecalRendererFeature`）

`Mobile_Renderer` / `PC_Renderer` 的 `m_RendererFeatures` 原本是空的 —— URP
自带的贴花能力一直没开。现已各加一个 `DecalRendererFeature`。

注意边界：**这不会让 KriptoFX 的贴花件直接可用**（那是 Built-in 投影语义，
§六红线 3 不变），但它为"把包里贴花贴图重烘成 URP Decal 材质"这条路开了门，
也是裂地系统从"贴片模拟"升级到真投影贴花的前置条件。

### 8.3 卡牌深度代理（`CardDepthProxy`）

给每张卡补一份**不透明、alpha 裁剪**的同形副本（卡框 + 立绘各一份），画在
Geometry 队列，比卡面略小、略靠后。卡牌因此进入深度图与不透明贴图拷贝，
三类失效层同时恢复：

| 失效层 | 原因 | 补上后 |
|---|---|---|
| 折射壳（`RFX1_UberDistortion`） | 取到"没有卡牌的背景"，等于把卡抹掉 | 折射里能看到卡 |
| 软粒子（`USE_SOFT_PARTICLES`） | 无可淡出表面，硬边穿插 | 正常淡出 |
| 前后穿插 | 透明件只能靠 sortingOrder，穹顶前后半塌成一片 | 卡后的部分被深度裁掉，自动前后分层 |

关键点：**排序策略不用改**。`sortingOrder` 只决定透明件之间的画序，深度测试
依然会裁掉卡牌背后的部分 —— 所以画廊的 `LiftSorting`（order 45）保持原样。

代价与边界：每卡多两次 draw（6 卡共 12 次）；副本不跟随卡面的染色/闪光
（只在深度与折射取样里被看到，肉眼无差异）；副本必须**略小 + 略靠后**，
否则会在卡沿露出一圈重影。画廊按 **J** 键可整场开关做 A/B 对比。

### 8.4 为什么不换框架（一次性结论，勿重复讨论）

| 方案 | 收益 | 代价 | 裁定 |
|---|---|---|---|
| 退回 Built-in RP | 包 100% 原生（贴花/GrabPass 直接可用） | 后处理体系重写、URP-only 材质全废、未来资源包九成只出 URP | **否** |
| 换 HDRP | 无（本项目诉求不在此） | 移动端跑不动 | **否** |
| 改真 3D 游戏 | 只多出"特效与卡牌正确合成"这一条 | 模型/绑定/动作/光照全套美术管线，且与"卡牌 2D"定位矛盾 | **否** |
| 本节三件套 | 上述那一条收益的绝大部分 | 约 3–4 天，每步可独立回滚 | **采用** |

仍拿不到的部分（记账，别当 bug 反复查）：包里的点光对我们的 Unlit 地面与
不受光 Sprite **零效果**，pack demo 里靠灯光营造的氛围需要用发光贴片/地面亮斑
手动伪造。
