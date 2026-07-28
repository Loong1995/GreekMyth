# 特效画质分档与移动端纪律（接件前必读）

> 上下文永远是**移动端**。本文回答两件事：接一件厂包特效时**什么可以调、
> 什么必须摘**；以及**编辑器里 Play 看到的怎么和真机一致**。
> 怎么落盘见 [vfx_standardization.md](vfx_standardization.md)。2026-07-28 立。

## 一、总原则：只降强度，不删效果

**已接入的观感，任何机型上都不允许"整个消失"**，只允许按档位降强度。
可以彻底不出现的只有一类：**本来就画不出来或普遍不成立的机制**
（Legacy `Projector` / 厂包 `Decal` 在 URP 下根本不渲染），那是清死层，不是关效果。

理由：战报演出的每一件都承载信息（命中在谁身上、谁有罩身、裂地多大＝这一击多重）。
关掉一类特效＝玩家看不懂在打什么，比掉几帧严重。

| 层 | 处置 | 为什么 |
|---|---|---|
| 屏幕折射（Distortion/Refraction） | **按档降强度**（`VfxTierScale`） | 有效果（火/盾热浪），但在低频大理石舞台上收益有限（P-74）；低端只压不删。**罩身用途例外**：折射会把身后卡面搅糊，玩家读不到兵力/立绘，属正确性问题，一律中和（P-77） |
| 粒子发射量 | **按档降强度** | 半透明加色粒子按屏幕覆盖面积计价，是移动端掉帧第一死因。等比缩放保形状保节奏，只变稀 |
| 实时灯 | 第 1 盏按档降亮度；**第 2 盏起只在高端档点亮** | 灯的开销来自"多一盏多一遍光照循环"，调亮度一分钱省不下来，只能开/关。阴影一律关（舞台没有需要接影的几何；管线级也已全关，见 §二b） |
| Projector / 厂包 Decal | **摘**，替代方案必须登记 | URP 不渲染；编辑器偶尔还能看见点东西、打包即消失（P-33）。观感由自研裂地 `GroundCrackService` 补 |
| Legacy `Particles/Additive*` 挂 LineRenderer | **改** `URP/Unlit` Transparent+Additive | 编辑器可亮、真机/移动端回退成**纯白带**（宙斯 DR 竖雷，P-83 第一层） |
| 加色（Additive）件的**亮度上限** | 封顶，且亮度走**材质颜色**不走顶点色 | 加色与舞台底色相加，HDR 下和 >1 即裁剪成纯白；`URP/Unlit` 还不乘顶点色，`alpha` 会静默失效（P-83 第二层）。DR 竖雷封顶 `MaxIntensity=0.65` |
| 粒子碰撞 / 触发 | **关** | 舞台上没有任何碰撞体，算完也撞不到东西＝纯 CPU 浪费 |
| 件自带 AudioSource | **保留** | 素材音（爆裂/电流/风声）与画面同拍，是这件观感的一部分。`SfxManager` 管的是战斗语义音（技能/命中/状态），两者叠加；要静音走音量总线，不在落盘期删 |
| WindZone / CameraShake / PerPlatformSettings | **摘** | 影响本件之外的世界，或运行期偷改参数制造不确定性 |

## 二、档位（唯一配置点：`ClientBattle/VFX/VfxQuality.cs`）

| 档 | 判据（自动探） | 粒子 | 折射 | 灯亮度 | 第 2 盏起 |
|---|---|---|---|---|---|
| Low | 内存 ≤4G | 0.40 | 0.30 | 0.60 | 关 |
| Mid | 内存 ≤7G | 0.70 | 0.70 | 0.85 | 关 |
| High | 其余（旗舰 / PC） | 1.00 | 1.00 | 1.00 | 开 |

**判据只认内存**，显存只在**安卓**且报了 ≤1G 的可信小值时降一档。
`graphicsMemorySize` 在移动端是估算值，各家 UMA 报法不一（iOS 常报成系统内存的
一个分数），拿它做"或"降级会把 6G 的 iPhone 一并打成低端——
**用最不准的那个数说了算，是探测逻辑最常见的死法**。

- 落盘的成品**始终是厂包满强度**，缩放在运行期由 `VfxTierScale` 完成。
  烤进 prefab 等于中高端机永久损失，且改平衡点要把所有件重接一遍。
- 改平衡点＝改这三行系数，全项目所有件同时生效。

### 玩家可选

档位要挂到游戏内设置面板：**自动 / 低 / 中 / 高**四项，
`VfxQuality.SetUserPreference(tier)`（`null`＝自动），存 PlayerPrefs，
启动时 `LoadUserPreference()`（已在 `PlaybackWorldBuilder` 接好）。

**不提供"关闭某类特效"的开关**——那种开关一旦存在，演出就会开始依赖
"反正玩家能关"，最后所有人都看不懂战况。玩家能调的只有强度档。

> 现状：档位的**读写接口与持久化已经可用**，游戏内设置面板 UI **尚未接**
> （接的时候只需三个按钮调 `SetUserPreference`，回显读 `UserPreference`）。
> 在此之前，发行包里换档只能靠改 `PlayerPrefs` 的 `vfx_tier` 键。

### 调试时怎么切档 / 改参数

| 想做的事 | 怎么做 |
|---|---|
| 编辑器里切档看观感 | 菜单 `GreekMyth/特效/画质档`（低/中/高，打勾回显）。存 EditorPrefs，**不用改代码、不会误提交**；Play 中切立即对下次启用的特效生效 |
| 看当前档与判据 | 菜单 `…/画质档/打印当前判据`，或日志里开场那行 `[VfxQuality] tier=… mem=… vram=… device=…`（`LoadUserPreference` 每场打一次，真机排查全靠它） |
| 改档位系数 | `ClientBattle/VFX/VfxQuality.cs`：逐件三张表 `ParticleFactor` / `RefractionFactor` / `LightFactor`，镜头层三张表 `BloomIntensity` / `BloomThreshold` / `BloomHighQuality`（§二b）。索引一律＝Low/Mid/High |
| 改"第几盏灯起只在高端亮" | `VfxPackStandardizer.MaxLightsPerEffect`（改完跑一次清洗菜单） |
| 改某件的粒子参考预算 | `VfxPackStandardizer.ParticleBudgetOf`（只影响报警阈值，不影响观感） |
| 真机 / 运行期临时切 | `VfxQuality.Override(tier)`（不持久化）或 `SetUserPreference(tier)`（持久化） |

已经在播的那一份实例不会中途变档（`VfxTierScale` 在 `OnEnable` 对拍），
要看完整对比就重开一场。

## 二b、镜头层档位（全屏 pass，不走 VfxTierScale）

2026-07-28 补。**Bloom 的开销与粒子数无关**（它是几遍全屏降采样滤波），
所以逐件缩放的 `VfxTierScale` 管不到它——此前镜头层与档位完全脱钩，
低端机上最贵的几项之一一直按 PC 满配在跑。现由 `BattlePostFx.Apply()`
按当前档直接写 Volume，系数表仍在 `VfxQuality`（唯一配置点）。

| 档 | Bloom 强度 | 阈值 | 高质量滤波 |
|---|---|---|---|
| Low | 0.75 | 1.05 | 关 |
| Mid | 0.95 | 0.95 | 关 |
| High | 1.15 | 0.85 | 开 |

- **低端不关 Bloom**：厂包峰值件按 HDR+Bloom 设计，关掉会塌成廉价喷洒；
  自研裂地的熔岩锋面（HDR 分量 >1）会直接变回一条橙线。那是"删效果"，
  违反 §一 原则。真正省钱的是**关高质量滤波**——它不改变"有没有溢光"，
  只让光晕边缘略糙。
- 抬阈值还有个附带好处：低端只让真正的峰值（熔岩/巨伤）溢光，
  卡面日常亮部不参与，画面对比度反而回来一点。
- **调用顺序红线**：`BattlePostFx.Ensure()` 必须在
  `VfxQuality.LoadUserPreference()` **之后**（`PlaybackWorldBuilder.Build` 已按此排序），
  否则镜头层读到的是上一场残留的档。
- 切档**当帧生效**（全屏后处理，不像逐件缩放要等重新启用）。

### 管线级阴影：已全关（2026-07-28）

两套 RP asset 的 `MainLightShadowsSupported` / `AdditionalLightShadowsSupported` /
`AnyShadowsSupported` 一律置 0。理由：**场上没有任何投影物**——地面显式
`shadowCastingMode.Off`、其余全是 unlit sprite、厂包灯的阴影在落盘期就被关掉。
开着等于白留一张阴影图和一遍 pass。卡牌的"接地阴影"是自绘 sprite
（`CardGroundShadow`），与实时阴影无关，不受影响。

## 三、编辑器 Play ≈ 真机

编辑器的 Standalone 目标屏蔽了 Mobile 质量档，Play 时必然走 `PC_RPAsset`。
两套资产现已在**一切影响观感的项**上对齐，编辑器不再"天然更好看"：

| 项 | 状态 |
|---|---|
| 特效逐件强度（粒子/折射/灯） | 一致：编辑器按 `VfxQuality.EditorTier`（默认 **Mid**＝主力机型）跑同一套系数 |
| 镜头层（Bloom 强度/阈值/滤波） | 一致：同一张档位表，见 §二b |
| 实时阴影 | 一致：两套均已关 |
| MSAA / 不透明拷贝降采样 / 色彩分级模式 | 一致（关 / 2x 双线性 / LDR） |
| sRGB↔线性转换 | 一致：`UseFastSRGBLinearConversion` 两套均开（此前 PC 走精确路径，色彩与真机有细微偏差） |
| **RenderScale** | **仍有差**：真机 0.8、编辑器 1.0 |

RenderScale 是**故意保留**的唯一差异：把编辑器也压到 0.8 会让美术评审在一张
更糊的画面上做判断，得不偿失。代价是**锐度不能在编辑器里验收**——
`RenderScale 0.8` 的实际清晰度、填充率掉帧、发热，一律以**独立版**为准
（`Test/FrameSpikeProbe.cs`）。

- 想看低端观感：菜单 `GreekMyth/特效/画质档/低端`，不需要真机。
- 开场那行 `[VfxQuality]` 日志现在把粒子/折射系数与 Bloom 参数一并打出来，
  真机上"看到的到底是哪一档"看这一行即可。

## 四、"我就是要那个效果"——需求到方案

| 想要的观感 | 做法 | 别做 |
|---|---|---|
| 空间扭曲 / 冲击波 | 保留折射层（按档降）+ 加色冲击环 + 震屏 + 3 档裂地 | 为省性能把折射摘掉 |
| 地面焦痕 / 法阵 | 自研裂地（模式×强度×面积） | Projector / 厂包 Decal 层 |
| 更炸的爆发 | 大颗少量核心层 + 裂地升档 + 震屏 + 顿挫节奏 | 单纯堆粒子数 |
| 满屏氛围 | 场域氛围件（`ambient_`，钉地面中心、层序压卡下） | 每人挂一份光环 |
| 慢放下才好看的爆发 | 顺序演出给足真实时间（`EmitWindow`）或降低期待 | 调参数硬追画廊 |

## 五、验收与入口

体检项（`GreekMyth/特效/体检 标准件流水线四项`，一件不过整批报错）：

- 根上有 `VfxTierScale`（粒子总闸）；折射层与每盏灯各自挂了 `VfxTierScale`
- 无 Projector / 厂包 Decal；粒子碰撞与触发全关；灯无阴影
- **音源不查**：件自带音源一律保留，任何"AudioSource＝0"的旧口径已作废
- 活跃粒子估算超参考预算（定点/罩身 1500、地面 1500、场域 2000）只**报警**
  不判死——降档靠系数，要不要换素材由人定。报警单列在报告的「警告（不判死）」段
- 估算**按 `maxParticles` 截断**：厂包普遍"发射参数写到天上、靠 maxParticles 兜底"
  （`aura_ares_might/Shield` rate=100000 而 max=1），不截断会报出百万级假警报，
  把真正的大户淹掉
- 清洗菜单**幂等**：无改动时报告写「改动 0 件」且不重新落盘。
  重跑一遍还在改，说明某步不幂等（起播平移、用途组件都属此类，故不进清洗）

| 动作 | 入口 |
|---|---|
| 落盘（唯一入口） | `VfxPackStandardizer.Standardize(src, key, usage)` |
| 全量体检 | `GreekMyth/特效/体检 标准件流水线四项`（`Temp/vfx_audit.txt`） |
| 存量件就地补挂档位缩放 | `GreekMyth/特效/清洗 存量标准件（挂档位缩放 + 清死层/碰撞，音源保留）`（`Temp/vfx_clean.txt`） |
| 档位系数（逐件 + 镜头层） | `Assets/Scripts/ClientBattle/VFX/VfxQuality.cs` |
| 镜头层落地（Bloom 按档写 Volume） | `Assets/Scripts/ClientBattle/VFX/BattlePostFx.cs` |

## 六、存量排查（2026-07-28 首轮，63 件）

| 问题 | 件数 | 处置 |
|---|---|---|
| 件自带音源 | 23 件 25 个 | **保留**（首轮曾误摘，已 git 回滚复原） |
| 屏幕折射层 | 6 | 补挂档位缩放（罩身用途改中和） |
| World 粒子碰撞开着 | 5 | 关 |
| 实时灯 4 盏 | 1 | 保留，第 2 盏起挂 MinTier=High |
| 活跃粒子超参考预算 | 3 | 见下表（估算修正后；此前 6 件是没截 `maxParticles` 的假警报） |

收尾状态（第二轮，音源保留版）：**63 件全过，0 不合格，3 件带警告，清洗重跑改动 0 件**。

| 件 | 估算 | 性质 | 结论 |
|---|---|---|---|
| `cast_duel_launch` | 10266 | 单挑出阵，一次性 burst，全场独占 | 接受：低端档 ≈4100，且同屏无其他演出 |
| `hit_massive` | 8008 | 巨伤命中，两层各 4000 burst，瞬时 | 接受：低端档 ≈3200，巨伤本就该是全场最重的一击 |
| `hit_shield_counter` | 5002 | 圣盾反弹命中 | 接受：低端档 ≈2000，接近预算 |

三件都是**一次性 burst 的瞬时峰值**（不是稳态驻留），且都发生在单占播放单元里，
故只登记不裁。真机实测掉帧时的处置顺序是：先降档 → 再换素材，**不改成品强度**。

根因见 P-79：折射清洗当初只写在 `Shroud` 分支里，其余用途从头到尾没人管；
粒子与碰撞根本没有体检项。**任何一条纪律，只在某个分支里执行、又没有对应
体检项，就等于不存在。**

## 七、机型覆盖现状（2026-07-28 结论）

工程侧配置：安卓 Vulkan + GLES3 / minSdk 26；iOS Metal / target 15.0；Linear 色彩空间；
`Mobile_RPAsset` RenderScale 0.8、MSAA 关、Opaque+Depth 开（折射靠 Opaque，故必须开）。

**结构上有保证的**（有代码硬约束或体检项兜着，不靠人记得）：

- 不会有"编辑器有、真机没有"的层：URP 画不出的 Projector/厂包 Decal 一律在落盘期摘，体检查；
  LineRenderer 禁挂 Legacy Particles（DR 竖雷已迁 URP/Unlit，P-83）
- 不会因缺组件在设备上抛异常打断演出：孤儿驱动、missing script、可实例化三项体检全过（63/63）
- 不会有"低端机根本扛不动"的件悄悄上线：超预算逐件登记在 §六，且低端档自动稀释
- 折射不会变品红/黑块：Opaque Texture 在 Mobile RP 上是开的（这也是折射不清零的代价）

**只能上真机确认、本档位系统不负责的**：填充率导致的掉帧与发热、shader 变体裁剪后的
实际包体与首次编译卡顿、各家 GLES3 驱动的个别 bug。上机顺序建议：一台 4G 内存安卓
（走 Low）+ 一台主力机（Mid）+ 一台 iPhone（High/Mid），每台跑一遍含单挑与巨伤的战报，
先看开场那行 `[VfxQuality]` 判据对不对，再看有无品红/缺层，最后才看帧率。
