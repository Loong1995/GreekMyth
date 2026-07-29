# 客户端特效演出机制总纲（performance_mechanisms）

> 本文是**所有演出机制的唯一总目录**：每条机制给出一句话结论 + 代码位置 +
> 细则文档链接。改任何演出行为前先来这里定位。规格来源：
> `docs/prompts/client_perform.md`（任务书，只读）。

## 一、播放主流程（事件 → 画面）

| # | 机制 | 一句话 | 代码 | 细则 |
|---|---|---|---|---|
| 1 | 事件解析 | 战报 JSON → 强类型事件对象，未知类型降级 UnknownEvent 跳过不崩 | `Events/BattleEvents.cs` | [framework §第1层](client_battle_framework.md) |
| 2 | 分组 | 按 group_id 聚成 EventGroup（root+副事件），再按 root 类型分类 GroupKind | `Events/EventPipeline.cs` | [framework §第2层](client_battle_framework.md) |
| 3 | 分组管线 | 6 processor 依次改写（借刀拆段→反应后置→集体齐发→台词抽取→贯穿打标→节点合并），注册于 `PlaybackWorldBuilder.Build`，全表见 [playback_units §二](playback_units.md) | `Events/Processors/*.cs` | [playback_units.md](playback_units.md) |
| 5 | 三级演出配置 | 特殊配置(每战法一条) → 组默认(主动/普攻/追击/状态/神谕) → 全默认兜底；未配置 LogWarning | `VFX/VFXResolver.cs` + `VFX/PerformanceDatabase.cs` | [framework §第3层](client_battle_framework.md) |
| 6 | 播放主循环 | 逐组协程播放；Speed 调速、SkipToEnd 静默落账快进；主循环在 `PlaybackDirector`，`PerformanceRunner` 作生命周期门面（状态机+HardStop），建世界在 `PlaybackWorldBuilder` | `VFX/PlaybackDirector.cs` + `VFX/PerformanceRunner.cs` | [architecture.md](architecture.md) |
| 7 | 统一落账 | 全客户端唯一事件落账入口 `Apply(ev, ctx, animated)`：兵力/状态/光环/石化/势能/属性/阵亡；animated=true 追加飘字/音效；cut-in 请求统一由此发出（2026-07-22 重构，消除动画版/静默版 4 处平行实现） | `VFX/EventApplyService.cs` | — |

**队列阻塞规则（2026-07-20 改定）**：行动类播放单元（主动/普攻/追击/
状态触发/单挑）占用时间轴，结束后加 `GroupPauseSeconds`；**台词（TraitLine）
是独占播放单元**：`TraitLineExtractProcessor` 从任意组抽出 `trait_trigger`，
按气泡完整时长阻塞（`SayExclusive` 动画与等待同一套 ×DurationMul/Speed），
**泡收起后立刻下一组**（前后不加单元停顿、禁止与伤害/位移重叠）。
回合节点/状态变化/阵亡/神谕宣告仍即时落账。飘字可异步，不阻塞下一组。
细则（叙述+代码机制+红线）：[playback_units.md](playback_units.md)。

**零死帧原则（2026-07-10 定）**：行动组内的每个 yield 等待必须对应一段正在
播放的可见动画（位移 tween / 弹道飞行 / 命中·治疗特效窗口）；禁止"纯定格"
等待垫时长。全场唯一允许的静止是单挑横幅。卡牌待机呼吸
（`UnitView.Update` 立绘错相位浮动）保证画面任意时刻有活物。

**节奏停顿（2026-07-20 调参）**：全局 `DurationMul=2`（动画节拍与停顿
一并 ×2，便于看清战报）；每个英雄行动结束停 0.55s、每个行动类播放单元
结束停 0.35s（`PerformanceRunner.ActionPauseSeconds/GroupPauseSeconds`，
再乘 DurationMul；Inspector 可调，随 Speed 缩放；约束：单元停顿 < 行动停顿）。
停顿期间常驻动画（待机呼吸/光环/飘字/滤镜）照常播放，与零死帧原则不冲突。

**战后结算表（2026-07-20）**：系列播放结束后弹出三谋式分队技能统计
（武将兵力条 + 技能 ×次数 / ⚔杀伤 / +治疗）；多局时顶部 Tab 切换
「第 N 局 / 系列合计」。**带技能归因**与 status→skill 映射见
[settlement_stats.md](settlement_stats.md)；英雄特例见
[hero_specials.md](../mechanics/hero_specials.md)。右上角「打开结算」可重开。
面板绘制已拆为 `Test/SettlementPanel.cs`（Runner 只负责聚合数据并调 Show）。

**响应/触发序（服务器）**：先守后攻、他人施加优先——见
[response_order.md](../mechanics/response_order.md)；播放跟随事件流不重排。

**播放单元完整性（红线，2026-07-12 修）**：初始分组必须按 group_id **全量聚合**
（组序=首次出现序），不能只合并连续段——群攻主动的 N 条伤害之间会被状态触发
（雷霆 tick 等 new_group 事件）插队，连续段合并会把一次群攻切成 N 个碎片，
违反 client_perform「群攻战法以该战法为一个播放单元（一次释放 N 道光）」。
雷霆落雷经 `CollectiveTriggerMergeProcessor` 合并为**一次集体齐发**（同状态
同来源的连续 StatusTrigger 组并组，白名单控制）；圣盾等保持逐次触发。
**借刀例外（2026-07-22）**：代战/披甲（profile.BorrowBlade）每段由不同借手
执行、段间交错响应/追伤，`BorrowBladeSplitProcessor` 按段拆单元并回插事件流
原生位置（段1→响应→追伤→段2…），不受「群攻=一个播放单元」约束——
借刀语义上是 N 次独立出手，不是一次齐射。

**节点组子事件落账（红线）**：round_start / action_start 等节点组的**全部子事件**
（状态到期移除、伤兵损耗、属性回写）必须经 `EventApplyService.Apply(…, animated:false)` 落账——状态到期移除
（石化解除等）挂在节点之下，漏掉会造成图标与石化覆盖层残留。

## 一b、Phase 4 势能/连发/协击/cut-in（B1/B2，2026-07-20）

| 机制 | 一句话 | 代码 |
|---|---|---|
| 势能镜像 | momentum_change → 四轨分值镜像账本（value 取事件权威值，零客户端加法）；**round_start 全体清零**（与服务器静默清零同步；计数单元＝回合） | `Units/MomentumService.cs` |
| 势能条 | **已取消展示（2026-07-25）**：不再画四轨迷你条，势能表现只保留火/金光环/溢出白闪；账本（`TrackTable` 按轨类型跨技能累计）与 `SetMomentum` 接口保留、空转 | `UnitView.SetMomentum` |
| 势能火 | 四轨最高 ≥4 小 / ≥5 / ≥6 / ≥7 满分大；`momentum_fire`←CFXR3 Fire；**一旦点着持续到回合结束**，下回合 `round_start` 前渐灭。生命周期收拢进控制器；hold-off＝抑制同值重挂（值变化即重新点火） | `Units/MomentumFireController.cs` |
| 闪光档（4） | 某轨首次 `value≥Flash(4)`：白闪爆发帧 + punch 缩放（**乙案已定稿**；不采购专属 overflow 包） | `MomentumService.Apply` → `PlayMomentumOverflow` |
| 满档（5）/闪光（4）+ | 四轨最高 ≥4 起挂：**卡上缘火** + **卡后 LightGlow A（无星点）**；同分档轻抬、行动切换同渐灭；≥5 起服务端 `cut_in` | `MomentumFireController` / `MountMomentumGlow` |
| cut-in 通道 | **全屏单人 cut-in**：暗幕 + 阵营色斜带甩入 + 巨幅立绘反向滑入 + 大字标题；触发源①满档轨②巨伤「重创」>3000③追伤第 5 次④战术变更（无主体→OnGUI 文字横幅）。**2026-07-27 统一**：前三类一律走取景独占单元「**推镜→横幅→本组出手命中→撤镜**」（与单挑同构，不飞立绘），判据在**编译期**由 `Events/CutInPlanner` 一处注记（[playback_script.md](playback_script.md)）；**同组只播 1 次**，不做回合级限流（C10）。权威文档 [cutin_stage.md](cutin_stage.md) | `Events/CutInPlanner`（编译期注记）+ `VFX/CutInStage.Play`（`PlaybackDirector.PlayGroup` 读 `EventGroup.CutIn`）；构件 `CutInService.PlaySolo` |
| 满档 cut-in 语义 | **按轨**：某轨已满（≥5）后，**同轨再次进账**的伤害出手前触发；刚满 5 当次不切。**阻塞**：`PlaySoloBlocking` 切完才开打。**标题＝即将造成伤害的技能名**（战法/普攻/协击/状态归因战法）。强化音效 `sfx_attack_empowered`。落账路径不再弹「势能全开·轨名」 | `CutInPlanner`（满档判定＝**势能预演**，编译期）+ `CutInStage.Play` |
| 连发演出 | `skill_trigger.burst_no≥2`：**与首发完全同模板整套重播**（`Classify` 按 burst_no 判为 ActiveSkill——连发 parent 指回首发触发事件，若不判会误分类成追击）；在此之上叠加节拍加速（倍率 `PerformanceProfile.BurstTempoScale`，默认 ×1.35，经 `VFXContext.TempoScale` 生效、播完复位）+ 施法者「连发 ×N」金色角标 | `EventPipeline.Classify` / `PlaybackDirector.PlayGroup` |
| 协击标 | `normal_attack.kind=="coordinated"`：出手前施法者「协击」青色角标，其余复用 Melee 模板 | `DefaultPerformance.Play` |

新增轨/改 tint 只动 `MomentumService.TrackTable` 一处（注册表驱动红线）。

## 一c、B3~B6（BGM 分层 / 飘字手调 / 皇卡演出 / 高光回放，2026-07-20）

| 机制 | 一句话 | 代码 |
|---|---|---|
| BGM 分层 | 4 stem（`BGM/bgm_stem_{drums,bass,melody,other}`）按**全局势能**（=MomentumService.GlobalTotal）三档淡入淡出（0~7/8~15/16+）；**切层对齐小节边界**（登记 Bpm/BeatsPerBar，pending 到小节头生效）；单挑与 cut-in 全层 duck -8dB、0.5s 恢复；stem 缺失回退单曲 `bgm_main`（音量+低通随档），全缺则静默 no-op；`MomentumService` 不再直连 Audio——经 `GlobalMomentumChanged` 回调由 `PlaybackWorldBuilder.Build` 接线（2026-07-22 解耦） | `Audio/BgmLayerService.cs`（StemTable 注册表） |
| 飘字手调 | 字体/字号/颜色/上浮曲线全参数收进 SO（`Resources/ClientBattle/FloatingTextTuning.asset`，缺失用代码默认）；字体放 `Resources/ClientBattle/Fonts/` 填名即换 | `Units/FloatingTextTuning.cs`；操作文档 [floating_text_tuning.md](floating_text_tuning.md) |
| 头像标（皇卡 C1） | profile.PortraitMarkKey：受影响单位头顶短暂浮现指定武将头像——宙斯落雷 `thunder`→zeus（RemoteStrike 落雷节拍内挂）、哈迪斯吸统 `hades_command_drain`→hades | `UnitView.ShowPortraitMark` + `DefaultPerformance` |
| 圣盾反弹/回血（C1） | 反伤 → `cast_oracle`；`aegis_shield` Melee；常驻 `aura_aegis`（投影圆直径重合、贴地）；重击回血 `icon_aegis_heal`；高光 `athena_aegis_reflect` | `MountAegisAura` + `FlashOverlayIcon` |
| 战神之勇光环 | `ares_might` → 画廊 2/8·10/61（Magic Effect18）罩身；跟随＝`VfxShroudFollower`；显隐＝通用 `VfxShroudPresence` + 注册表 `OddRounds`；`HasShroud` 看 `IsPresent`（渐隐后恢复抖动） | `MountShroud` / `SetShroudVisible` |
| 回位微抖 | 每次位移回位或受击顿挫结束重采样 `RestPosition`：边长=区域宽/5，半边由 `StanceLayout.RestJitterHalf` 约束（与卡面尺寸一并反算，保证邻格不叠）；突进/落雷瞄当前休息点 | `UnitView.DOMoveReturnHome` / `HitReact`；`StanceLayout` |
| 高光回放（C2） | 终局扫描：我方行动窗按**观感分**（伤害 + 满势能 cut_in×3000）取最高窗整段重播（窗前静默落账、窗内正常演出；避免「伤害略高但无满势能切入」抢走真高光）；选窗为纯函数，重播复用主循环 `PlaybackDirector.PlayGroupsRange`；入口 `PerformanceRunner.PlayHighlight`；Tester 播放完成后出「高光回放」按钮 | `VFX/HighlightSelector.cs` + `PerformanceRunner.PlayHighlight` |

## 二、演出模板族（每组怎么演）

| 模板 | 触发条件 | 演出 | 代码 |
|---|---|---|---|
| Melee 普攻/近身 | GroupKind=NormalAttack；单体追击；反制类 / 单体近战主动（如镜盾闪击）特殊配置 | 施法者冲至被打者近身 → 命中帧在**被打者身上闪斩击**（`slash`）+ 卡面命中 **`hit_generic`**（`MeleeDefault.HitKey`，≠主动默认的 hit_sword）→ 回位（休息点重采样） | `DefaultPerformance.PlayMelee`；查表 [vfx_config_index](vfx_config_index.md) |
| AoeCenter 群攻 | 主动且互异目标 ≥2 | 施法者移动到棋盘中心 → N 道弹道齐射 → 同帧掉血 → 回位。**近 3D 地面**（`ArenaSlotLayout.GroundActive`）：**物理**弹道裂地 3 段由 `StrikeSync` 进度驱动；**魔法**无弹道裂地。命中裂地与 HitKey 同帧（物魔同规）。档位/面积见 [ground_crack_config.md](ground_crack_config.md)（`prepare_active`→档 2、瞬发档 1；拉满强制轨迹档3+弹道档3+命中档3×面积1.5，**无场心叠缝**）。**唯一入口 `GroundCrackService`** | `DefaultPerformance` → `SettleDamage` → `GroundCrackService` |
| PerSegment 逐段 | 单体主动/多段 | 每段一个节拍：弹道（同走 `StrikeSync`）→ 抵达同帧命中掉血 | `DefaultPerformance.PlayPerSegment` |
| **出手同步（跨模板）** | 任何带弹道的模板 | 一次出手＝**飞行段 + 命中拍**：`StrikeSync.Fly(from, projectiles, aims, flight)` 逐帧广播弹道真实进度给挂上来的 `IFlightDriven`（现有裂地一家），`Run()` 返回＝进度推满＝弹道抵达，调用方**同帧**开命中拍 `SettleDamage`。禁止模板各自 `WaitForSeconds` 拼时序；新表现想跟弹道走只需实现 `IFlightDriven` 并 `Attach` | `VFX/StrikeSync.cs` + `GroundCrackService.PathDriver` |
| RemoteStrike 远程落击 | 雷霆落雷 / 神罚 / 宙斯拆技天雷击 | **施法者不位移**；DR **蓝晕+白芯+分叉+周期闪烁**竖雷（`DrLightningFlicker`）+ 宙斯头像标。卡面**不叠**雷命中件（`HitKey=none`）；受击仍 HitReact + 震屏。神罚另：**抬高震屏** + **档 2 命中裂地**。**禁 RFX4** | `DefaultPerformance.PlayRemoteStrike` / `FireZeusBolt` |
| 主动默认（按伤害类型） | 全部未专配主动 | **物理** Proj=`proj_bolt200` + Hit=`hit_sword`（Impact_Cut_V1 直线金橙刀光，卡心×2.5，定稿）；**魔法** Proj=`magic_bolt`（Magic **Effect1**）+ Hit=`hit_petrify`（CFXR Magical Stars Pink，同倍率）。**默认不播 Cast**。命中解析四级见 [vfx_config_index](vfx_config_index.md) §一 | `ProjectileKeyOf` / `ResolveHitKey` / `SettleDamage` |
| StatusTrigger | 状态触发组 | 默认按目标数走中心齐射/逐段；可特殊配置为 Melee / RemoteStrike | 同上（模板内分派） |
| OracleAura 神谕 | 神谕/被动宣告 | 施法者前摇 → 组内状态一次性落账（同帧挂光环）+ 整盘滤镜 | `OracleAuraPerformance` |
| None | 明确无演出（如蛇杖圣谕） | 只落账 | `PlaybackDirector.ApplyGroupSilently`（`EventApplyService.Apply(animated:false)`） |
| 单挑 | duel_challenge/duel_result + 组内 duel_* 台词 | 压暗渐变 → 号角 → 叫阵气泡 →（拒战｜应战→**单挑舞台 cut-in**→胜者）→ 恢复渐变。舞台＝立绘**出框**飞进虚空展示屏 → 交错+动作 ×`clash_cutins`(1~3) → 定胜负 → 飞回卡框；动作走 flipbook，缺帧退静态立绘占满时长。屏体华饰/氛围（阵营辉光·放射慢转·浮尘·影院黑边·四角纹饰·立绘背光·冲击环·白闪）在 `DuelStageChrome`，**MonoBehaviour 自走 Update**，故编排层插值/等待/放帧时屏上恒有运动（零死帧）；四种周期互质防呆板，全程程序化贴图零预制资源（**不叠 RFX 粒子**）。**总索引 [../mechanics/duel.md](../mechanics/duel.md)** | `DuelPerformance` + `CutInService.DuelClashRoutine` → `VFX/DuelStage.cs` |

**斩击尺寸规则（2026-07-10 定）**：普攻斩击 = 资源基准尺寸 ×1.0；追击 ×1.5；
再乘 profile.StrikeVfxScale（Inspector 可调）。物理组默认 key=`slash`、
魔法=`magic_bolt`，特殊配置可覆盖 ProjectileKey。

## 三、资源与尺寸规范

| 机制 | 一句话 | 代码/位置 |
|---|---|---|
| 占位三级回退 | Resources/ClientBattle/ 同名真资源 → 程序化色块/合成音，永不缺资源 | `Placeholder/PlaceholderFactory.cs`；清单见 [assets_upload_guide](assets_upload_guide.md) |
| 特效尺寸校准 | variant 根缩放按**目视校准**（勿按包围盒归一——拖尾/发射域会把包围盒撑到几十单位，按其缩放核心画面会消失，2026-07-10 教训）；现值：弹道/治疗/命中 1.0、剑击/穿刺 0.35、slash 0.25、光环 0.9~1.4；演出层只做相对缩放（`*=`），**禁止覆盖 localScale** | variant 在 `Assets/Resources/ClientBattle/VFX/`；来源映射见 [assets_upload_guide §一.1](assets_upload_guide.md) |
| 池化与缩放复位 | 特效对象池复用；出生缩放由 VfxOriginalScale 记录，回池自动还原 | `VFX/VFXManager.cs` |
| 特效预热（渲染级） | 开战前全部 VFX prefab 在**离屏 RT 相机前实渲 3 帧**再入池（仅实例化不渲染 warm 不到 shader/贴图）；`PerformanceRunner.PlayLoop` 等 `PrewarmComplete` 再开播 | `VFXManager.Prewarm`（`PlaybackWorldBuilder.Build` 调用） |
| 报告驱动预热 | 开战前扫一遍战报：台词/名字字形、状态图标纹理、合成音效、气泡对象全部前置生成，战斗热路径只剩查缓存 | `PlaybackWorldBuilder.PrewarmFromReport` |
| 飘字零分配动画与预热 | 开战前预建 24 个 TextMesh/动画记录并一次请求全部中文名、数字字形；运行时由服务统一 Update 上浮淡出，不再为每条飘字创建 DOTween Sequence/Tween/闭包，避免动态字体首次扩图与 GC 主线程尖峰 | `FloatingTextService.Prewarm/Update` + `ChineseNames.FloatingTextCharacters` |
| 贴图导入红线 | 特效包贴图 maxSize≤1024 + 压缩 + 关 mipmap（2026-07-10 全量重导，内存 723→258MB）；新导入资源包必须照此设置 | 三包目录 importer 设置 |
| 强度参数 | 滤镜/光环浓度 Intensity(0~3)、特殊图标 ExtraIconScale、斩击 StrikeVfxScale——全在 PerformanceDatabase 改，无需动代码 | `VFX/PerformanceProfile.cs` |

## 四、卡牌与 UI 表现

| 机制 | 一句话 | 代码 |
|---|---|---|
| 卡牌结构 | 卡框(阵营色染色，支持 CardFrames/frame.png)+立绘+血条+名字 | `Units/UnitView.cs` |
| **演出参数收口** | 机位俯角 + 卡牌「怎么动」的全部可调量（卡姿抖动 / 微调圆 / 击退 / 受击颤动 / 三拍 / 残影 / 接地阴影）集中一处，改数字即调参。**禁止再在各表现类里散落 const**。分工：`BattlefieldLayoutConfig`＝舞台几何（含院区比例），`StagePerformanceConfig`＝机位与演出幅度；卡后倾基准仍 `CameraFitter.CardPitchDeg` | `Units/StagePerformanceConfig.cs` |
| **微调圆（站位活动上限，旧名"击打圆"）** | 站位微抖圆同时是运行期活动上限：受击击退与出击后的前进休息点**都截断在圆内**（越界取圆周点，不反弹）。于是卡牌只在自己的圆盘里一进一退地游走——**挨打向后退、出击向前进**——整局站位是活的，又永远不会走进别人的格子。所有裁剪在**地面 XZ 二维**做（`OffsetFromHome`/`AnchorAtOffset`），近 3D 下用世界向量会把纵深错算成高度 | `UnitView.ClampToTuneCircle`；半径 `StagePerformanceConfig.TuneCircleScale` × `StanceLayout.SlotJitterRadius` |
| **卡姿随机后倾** | 每卡后倾角在 **`CameraFitter.CardPitchDeg` ± `CardPitchJitterDeg`** 内随机（45±5 ＝ **40°~50°**），整排卡不像同一块板刷出来的。**只抖视觉**：`GroundPoint`/`GroundFoot`/`CardShadowDepth` 等几何一律仍按基准角算（几度内目视无差），故抖动幅度不宜 >8° | `UnitView.ApplyCardLean` → `_baseLean` |
| **受击表现（三通道，时间上串行）** | **命中拍**＝裂地+HitKey+**定向击退**+**立绘挤压**+震屏，同帧；击退**落定后**接**沿线前后颤**。三条通道各管一件、互不代偿：**击退**＝定位点位移（力的方向），**颤动**＝落定后围绕落点沿同线的**纯动画**前后颤（结束回落点，不改定位点），**挤压**＝立绘形变（肉感）。受击线＝「攻击方站位中心 → 本卡站位中心」，**必须取 `HomePosition` 不得取 `transform.position`**（攻击方突进后就贴在身边，实时位置算出的方向会乱跳甚至反向）。**两段位移都钉在受击线上**：落定点＝以 Home 为起点沿线的随机距离（`KnockbackXxxMin/Max`，单位＝微调圆半径倍数），推开点＝同线过冲（`KnockOvershoot`），越圆即截断到圆边；**落定点不得随机重采样**——旧版回弹奔圆盘随机点去，第二段位移斜出受击线，观感即"击退方向不对"。来源不在场（环境/状态伤）＝不击退，原地沿纵深轴起颤+挤压。**绕身 `IsPresent` 时禁一切卡根位移（击退+颤动）**（位移会甩飞绕身罩，P-58），挤压与红闪照给。**颤动定标（安卓）**：频率按战斗负载 **30 fps 下限** 定（vSync 锁 60，中端机战斗常掉到 30~45）——`HitTrembleFrequency`=**10 Hz**（30fps 下约 3 帧一周期；18 Hz 在 30fps 只剩 1.7 帧＝噪点）。振幅＝微调圆半径 × `HitTrembleAmpCrit/Normal`（0.22/0.13），幂衰减 1.1。颤动是最低优先级：任何 tween 接管 transform 即让位 | `SettleDamage` → `UnitView.HitReact(isCrit, fromHome)` → `KnockBack` →（OnComplete）`StartHitTremble` / `TickHitTremble` / `CardIdleMotion.Punch` |
| ~~受击随机抖动（位移式）~~ | **已废除（2026-07-27）**：`DOShakePosition` 全向随机**位移**抖动读作「震动」而不是「被打」。 | — |
| ~~受击旋转抖动（纯角度）~~ | **已废除（2026-07-27 当日重做）**：近正面卡的面内自旋不改轮廓、俯仰被投影吃掉，角度抖怎么调都读不出来。改为击退落定后的**沿线前后颤**（见上行） | — |
| **出手三拍** | 任何「移动过去打」的模板共用：**预备**（反向蓄力 OutQuad，给出「要打了」的预告）→ **发力**（InQuint 加速冲入，末速最高，命中拍落在最快的一帧，途中留残影）→ **收势**（OutBack 过冲回弹，读作收招而非倒带）。收势落点**沿本次行动方向前移**（`RerollRestPositionToward`），与受击者被推回去互为一对。三拍均为可见位移，不违背零死帧；时长全部经 `ctx.Scaled` | `VFX/StrikeBeats.cs`（`Advance` / `Recover(ctx, mover, towardWorld)`）← `PlayMelee` / `PlayAoeCenter` |
| **突进残影** | 突进期间按**固定间隔**（非 tween 回调，否则低端机上只出两张）拍下卡面快照，逐张淡出+收缩成锥形尾巴。残影是**运行期快照**（含压暗/石化/染色），不可预制，故不走 VFXManager 池，自带环形池，突进期零 Instantiate。order −2 | `Units/AfterImageService.cs` |
| **接地阴影** | 近 3D 舞台每卡一枚地面软椭圆。**没有接触阴影的物体一律被眼睛读作浮空贴纸**——这是「卡牌呆板」最物理的一层来源。长宽取自 `ArenaSlotLayout`（CardWidth / CardShadowDepth），卡尺一改自动跟随；卡牌抬离地面越高影子越小越淡。挂在卡牌**父级**而非卡牌自身（否则继承 45° 后倾与 DOPunchScale 缩放）；LateUpdate 取位（早于呼吸/tween 写入会慢一帧）。正交模式不创建 | `Units/CardGroundShadow.cs` ← `UnitView.Build` |
| ~~命中顿帧~~ | **已删除（2026-07-27 人工否决观感）**：暴击压 `Time.timeScale` 咬住数十毫秒的做法本项目不采用，勿再引入。任何「压全局时间」的手感方案都要先过人工验收 | — |
| **卡面生动性** | 立绘三条通道由**唯一合成器**每帧合成写入（不用 tween：三者作用于同一 Transform，互相 Kill 会让呼吸断掉/立绘停在半路）。① 待机呼吸＝浮动+侧摆+胸腔缩放+微倾，三个互质频率，每卡相位与频率各自失谐（六卡同屏永不同步）；兵力越低越慢越重。② 惯性视差＝立绘比卡框慢半拍，配合景深偏移读出「框里装着一个人」。③ 受击挤压＝阻尼正弦弹性形变。石化/阵亡冻成静止像 | `Units/CardIdleMotion.cs` ← `UnitView.Update` / `SetBreathingFrozen` / `PlayDefeated` |
| 兵力刷新 | 恒取事件 troops_after 权威值，客户端零计算 | `UnitView.SetTroops` |
| 状态图标 | 硬控/冥火卡顶外侧横排（宽≈卡宽 1/5）+ 抖动；**先攻/犹豫不展示图标**；常规上方小图标已关闭 | `Units/StatusIconPanel.cs` + `StatusPresentationRegistry` |
| **状态常驻光环** | status_id → key，前缀分三类挂法：`shroud_*` 罩身（MountShroud+Presence，如战神之勇=`shroud_ares_might`、雷霆=`shroud_thunder`；**罩身默认不影响受击击退与颤动**，要"纹丝不动"语义须在注册表置 `shroudLocksHitMotion`；挂载时 `VfxPhaseDesync` 错相位＝多人同挂不同步闪，参数 `StagePerformanceConfig.ShroudDesync*`）、`ambient_*` **场域氛围**（不挂卡、按 key 全场去重、持有者清零才撤，如雷霆=`ambient_thunder_storm`；**多源**：`StagePerformanceConfig.AmbientFieldSources` 每行一处发生地，默认两处「战场／背景」，均自上而下劈；各自可配位置/尺度/疏密/游走/自转/隐藏层；全局疏密 `AmbientFieldDensity`）、其余普通光环挂卡心；status_apply 挂、status_remove/阵亡/整局重置撤 | `Units/UnitAuraService.cs`；配置收口 `Names/StatusPresentationRegistry.cs`（新状态只加注册表一行） |
| 整盘滤镜 | 程序化全屏呼吸色罩（血红/海蓝/冥紫按 key 取色），不用粒子 prefab（会成棋盘中心固定点）；透明度待真棋盘底图定稿再调 | `OracleAuraPerformance.BoardFilterOverlay` |
| 石化 | 灰色卡框覆盖层淡入淡出（tween 互斥，新 tween 先杀旧）；施加走通用状态音 `sfx_status_petrify`，解除走 `sfx_petrify_off` | `UnitView.SetPetrified` |
| 飘字 | 伤害红/治疗绿/真伤黄/状态蓝灰/属性金紫，同单位纵向堆叠不重叠 | `Units/FloatingTextService.cs` |
| 台词气泡 | **独占 TraitLine**：抽出后仍保留原组 Root（试炼/战法 id），格挡段不丢 Melee；气泡完整时长阻塞、邻组无缝 | `TraitLineExtractProcessor` + `SayExclusive` |
| 音效 | 同帧同 key 去重 + 每帧上限，防爆音 | `Audio/SfxManager.cs` |
| 阵亡 | 变灰倒下保留尸位；主将阵亡横幅强调 | `UnitView.PlayDefeated` |

## 五、机型兼容（红线）

> 细则（含图像槽位缩放与 sorting 层级总表）：[rendering_layout.md](rendering_layout.md)。

| 机制 | 一句话 | 代码 |
|---|---|---|
| 取景权威 | CameraFitter 按宽高比动态调 orthoSize，保安全区（半宽 4.6/半高 5.2）任意机型完整可见，热切换每帧跟随 | `VFX/CameraFitter.cs` |
| 背景 | 无色（纯黑）；上传 UI/board_background.png 自动 cover 铺满不变形 | `Units/BattleBoardView.BuildBackground` + BackgroundFitter |
| OnGUI 缩放 | 横幅/调试按钮按屏幕高度缩放（800px 基准）；横幅白字+黑影双绘保证任何底色可读 | `VFX/BannerService.OnGUI` / `Test/SettlementPanel.OnGUI` / `BattleReportTester.OnGUI` |
| **禁止事项** | 表现层不得写死 orthoSize/像素坐标/屏幕分辨率假设 | — |
| 性能验证 | 以**独立版 + FrameSpikeProbe** 数据为准；编辑器 Play 有环境级 1.7s 周期冻结、远程桌面有传输冻结，均非游戏问题（2026-07-10 定案：独立版 60fps 满帧零长帧） | `Test/FrameSpikeProbe.cs`（Tester 勾 ShowDiagnostics） |

## 六、布局与配色

- 棋盘：上下布局，A 队下、B 队上；六套预设与矩形六等分站位见
  [battlefield_layout.md](battlefield_layout.md)。
- 阵营配色：神金/人红/海蓝/冥紫，唯一源 `Units/BattleBoardView.cs` 内 `FactionColors` 常量（private），
  规范文档 [faction_style.md](faction_style.md)。
- 中文名：`Names/ChineseNames.cs` 与后端 `battle/names.py` 同步（红线）。

## 七、细则文档索引

| 想改什么 | 去哪看 |
|---|---|
| **特效 key 谁用什么 / 普攻命中 / 解析顺序** | **[vfx_config_index.md](vfx_config_index.md)**（总索引） |
| 播放单元/时间轴阻塞/台词独占/管线 processor | [playback_units.md](playback_units.md) |
| 分辨率适配/图像槽位/sorting 层级 | [rendering_layout.md](rendering_layout.md) |
| 战场分区/站位/阵型识别/卡尺 | [battlefield_layout.md](battlefield_layout.md) |
| 近 3D 舞台/相机/地天板 | [arena_stage.md](arena_stage.md) |
| 飘字/气泡/横幅/cut-in/字体调参 | [text_system.md](text_system.md) |
| 状态台词触发点（引擎侧） | [status_voice.md](../mechanics/status_voice.md) |
| 伤害响应谁先触发 / 他人神谕 vs 自身标记 | [response_order.md](../mechanics/response_order.md) |
| 鲁莽/踵之弱台词时点、雷霆圣盾特例 | [hero_specials.md](../mechanics/hero_specials.md) |
| 结算表谁记杀伤 | [settlement_stats.md](settlement_stats.md) |
| 框架分层/数据流向/逐文件职责 | [client_battle_framework.md](client_battle_framework.md) |
| 资源上传路径/命名/尺寸规格/获取分档 | [assets_upload_guide.md](assets_upload_guide.md) |
| 特效配置总索引（默认 HitKey / 查表） | [vfx_config_index.md](vfx_config_index.md) |
| 已购资源包与采购登记 | [assets_upload_guide.md §三](assets_upload_guide.md) |
| 阵营视觉规范 | [faction_style.md](faction_style.md) |
| 事件流契约（字段语义，只读） | `docs/schema/battle_events.md` + `battle_events_payloads.md` |
| 具体战法演出规格（任务书，只读） | `docs/prompts/client_perform.md` |
| 台词内容与性格触发时机 | `docs/mechanics/traits.md` + `battle/traits.py` |
| 单挑规则（服务器侧） | `docs/mechanics/`（duel 相关）+ `VFX/Performances/DuelPerformance.cs` |

## 八、维护红线

1. 新增演出机制：先在本文登记一行，再写代码；行为变更同步更新对应行。
2. 特效缩放只允许相对相乘（`*=`），资源基准尺寸只在 variant 上改。
3. 演出参数（尺寸/强度/资源 key）一律走 PerformanceProfile，不写死在演出代码。
4. 客户端零结算、未知事件优雅跳过、troops_after 权威——契约红线见 [index.md](index.md)。
