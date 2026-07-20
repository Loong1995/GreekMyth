# 客户端特效演出机制总纲（performance_mechanisms）

> 本文是**所有演出机制的唯一总目录**：每条机制给出一句话结论 + 代码位置 +
> 细则文档链接。改任何演出行为前先来这里定位。规格来源：
> `docs/prompts/client_perform.md`（任务书，只读）。

## 一、播放主流程（事件 → 画面）

| # | 机制 | 一句话 | 代码 | 细则 |
|---|---|---|---|---|
| 1 | 事件解析 | 战报 JSON → 强类型事件对象，未知类型降级 UnknownEvent 跳过不崩 | `Events/BattleEvents.cs` | [framework §第1层](client_battle_framework.md) |
| 2 | 分组 | 按 group_id 聚成 EventGroup（root+副事件），再按 root 类型分类 GroupKind | `Events/EventPipeline.cs` | [framework §第2层](client_battle_framework.md) |
| 3 | 反应后置 | 响应类 status_tick 拆到主组之后；序=事件流=引擎先守后攻，同持有者他人施加先于自身 | `Events/Processors/ReactionRegroupProcessor.cs` | 同上 |
| 4 | 节点合并 | 纯记账节点（自然损耗等）标记 ParallelWithNext 静默落账不停顿；大节点（回合/单挑/终局）独立演出 | `Events/Processors/NodeMergeProcessor.cs` | 同上 |
| 5 | 三级演出配置 | 特殊配置(每战法一条) → 组默认(主动/普攻/追击/状态/神谕) → 全默认兜底；未配置 LogWarning | `VFX/VFXResolver.cs` + `VFX/PerformanceDatabase.cs` | [framework §第3层](client_battle_framework.md) |
| 6 | 播放主循环 | 逐组协程播放；Speed 调速、SkipToEnd 静默落账快进 | `VFX/PerformanceRunner.cs` | [framework §第4层](client_battle_framework.md) |

**队列阻塞规则（2026-07-20 改定）**：行动类播放单元（主动/普攻/追击/
状态触发/单挑）占用时间轴，结束后加 `GroupPauseSeconds`；**台词（TraitLine）
是独占播放单元**：`TraitLineExtractProcessor` 从任意组抽出 `trait_trigger`，
按气泡完整时长（`ChatBubbleService.ExclusiveSeconds`≈1.14s，再乘 DurationMul）
阻塞时间轴，**与邻组无缝衔接**（前后不加单元停顿、禁止与伤害/位移重叠）。
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

**响应/触发序（服务器）**：先守后攻、他人施加优先——见
[response_order.md](../mechanics/response_order.md)；播放跟随事件流不重排。

**播放单元完整性（红线，2026-07-12 修）**：初始分组必须按 group_id **全量聚合**
（组序=首次出现序），不能只合并连续段——群攻主动的 N 条伤害之间会被状态触发
（雷霆 tick 等 new_group 事件）插队，连续段合并会把一次群攻切成 N 个碎片，
违反 client_perform「群攻战法以该战法为一个播放单元（一次释放 N 道光）」。
雷霆落雷经 `CollectiveTriggerMergeProcessor` 合并为**一次集体齐发**（同状态
同来源的连续 StatusTrigger 组并组，白名单控制）；圣盾等保持逐次触发。

**节点组子事件落账（红线）**：round_start / action_start 等节点组的**全部子事件**
（状态到期移除、伤兵损耗、属性回写）必须 ApplySilently 落账——状态到期移除
（石化解除等）挂在节点之下，漏掉会造成图标与石化覆盖层残留。

## 一b、Phase 4 势能/连发/协击/cut-in（B1/B2，2026-07-20）

| 机制 | 一句话 | 代码 |
|---|---|---|
| 势能镜像 | momentum_change → 四轨分值镜像账本（value 取事件权威值，零客户端加法）；action_start 该武将四轨清零（与服务器静默清零同步） | `Units/MomentumService.cs` |
| 势能条 | 每卡 HP 下四条迷你轨条（按**轨类型**跨技能累计，非单技能独立条；注册表 `TrackTable`：主动暖金/被动铜绿/神谕雷紫/普攻追击赤红；0~3 半亮、≥4 全亮） | `UnitView.SetMomentum` |
| 闪光档（4） | 某轨首次 `value≥Flash(4)`：白闪爆发帧 + punch 缩放（**乙案已定稿**；不采购专属 overflow 包） | `MomentumService.Apply` → `PlayMomentumOverflow` |
| 满档（5） | `value≥Full(5)`：常驻 rim 流光（多轨叠混色+呼吸）；服务端同档起每次 `cut_in=true` | `UnitView.RefreshGlow` / `add_momentum` |
| cut-in 通道 | 非阻塞金字横幅（屏幕 30% 高度，1.4s 淡出 + 轻震屏）；触发源①满档轨每次触发（事件 `cut_in=true`，满 5 当次起）②高伤 >3000 ③行动窗内追伤第 5 次；**同一播放组只播 1 次**去重，不做回合级限流（C10 定案） | `PerformanceRunner.RequestCutIn/DrawCutIn` |
| 连发演出 | `skill_trigger.burst_no≥2`：**与首发完全同模板整套重播**（`Classify` 按 burst_no 判为 ActiveSkill——连发 parent 指回首发触发事件，若不判会误分类成追击）；在此之上叠加节拍 ×1.35 加速（`VFXContext.TempoScale`，播完复位）+ 施法者「连发 ×N」金色角标 | `EventPipeline.Classify` / `PerformanceRunner.PlayGroup` |
| 协击标 | `normal_attack.kind=="coordinated"`：出手前施法者「协击」青色角标，其余复用 Melee 模板 | `DefaultPerformance.Play` |

新增轨/改 tint 只动 `MomentumService.TrackTable` 一处（注册表驱动红线）。

## 一c、B3~B6（BGM 分层 / 飘字手调 / 皇卡演出 / 高光回放，2026-07-20）

| 机制 | 一句话 | 代码 |
|---|---|---|
| BGM 分层 | 4 stem（`BGM/bgm_stem_{drums,bass,melody,other}`）按**全局势能**（=MomentumService.GlobalTotal）三档淡入淡出（0~7/8~15/16+）；**切层对齐小节边界**（登记 Bpm/BeatsPerBar，pending 到小节头生效）；单挑与 cut-in 全层 duck -8dB、0.5s 恢复；stem 缺失回退单曲 `bgm_main`（音量+低通随档），全缺则静默 no-op | `Audio/BgmLayerService.cs`（StemTable 注册表） |
| 飘字手调 | 字体/字号/颜色/上浮曲线全参数收进 SO（`Resources/ClientBattle/FloatingTextTuning.asset`，缺失用代码默认）；字体放 `Resources/ClientBattle/Fonts/` 填名即换 | `Units/FloatingTextTuning.cs`；操作文档 [floating_text_tuning.md](floating_text_tuning.md) |
| 头像标（皇卡 C1） | profile.PortraitMarkKey：受影响单位头顶短暂浮现指定武将头像——宙斯落雷 `thunder`→zeus（RemoteStrike 落雷节拍内挂）、哈迪斯吸统 `hades_command_drain`→hades | `UnitView.ShowPortraitMark` + `DefaultPerformance` |
| 圣盾反弹（C1） | `aegis_shield` Melee：Actor=持盾者（OwnerId）；CastKey 圣盾闪光后再突进反打；`aegis_ward` 控挡闪光 | `PerformanceDatabase` + `ActorOf(StatusTick)→OwnerId` |
| 高光回放（C2） | 终局扫描：我方每武将行动窗（action_start 分界）按单窗伤害排行，最大窗整段重播（窗前静默落账、窗内正常演出）；Tester 播放完成后出「高光回放」按钮 | `PerformanceRunner.PlayHighlight` + `BattleReportTester.OnGUI` |

## 二、演出模板族（每组怎么演）

| 模板 | 触发条件 | 演出 | 代码 |
|---|---|---|---|
| Melee 普攻/近身 | GroupKind=NormalAttack；单体追击；反制类 / 单体近战主动（如镜盾闪击）特殊配置 | 施法者冲至被打者近身 → 命中帧在**被打者身上闪斩击** → 回位 | `DefaultPerformance.PlayMelee` |
| AoeCenter 群攻 | 主动且互异目标 ≥2 | 施法者移动到棋盘中心 → N 道刀光/魔法光齐射 → 同帧掉血 | `DefaultPerformance.PlayAoeCenter` |
| PerSegment 逐段 | 单体主动/多段 | 每段一个节拍：弹道 → 命中掉血 | `DefaultPerformance.PlayPerSegment` |
| RemoteStrike 远程落击 | 雷霆等特殊配置 | **施法者不位移**；目标头顶头像标 + 自上而下命中特效齐发 → 掉血 | `DefaultPerformance.PlayRemoteStrike` |
| StatusTrigger | 状态触发组 | 默认按目标数走中心齐射/逐段；可特殊配置为 Melee / RemoteStrike | 同上（模板内分派） |
| OracleAura 神谕 | 神谕/被动宣告 | 施法者前摇 → 组内状态一次性落账（同帧挂光环）+ 整盘滤镜 | `OracleAuraPerformance` |
| None | 明确无演出（如蛇杖圣谕） | 只落账 | Runner 直接静默 |
| 单挑 | duel_challenge/duel_result | 压暗非参战者 → 横幅 → 三次对撞 → 胜负宣告（方向无关位移） | `PerformanceRunner.PlayDuel` |

**斩击尺寸规则（2026-07-10 定）**：普攻斩击 = 资源基准尺寸 ×1.0；追击 ×1.5；
再乘 profile.StrikeVfxScale（Inspector 可调）。物理组默认 key=`slash`、
魔法=`magic_bolt`，特殊配置可覆盖 ProjectileKey。

## 三、资源与尺寸规范

| 机制 | 一句话 | 代码/位置 |
|---|---|---|
| 占位三级回退 | Resources/ClientBattle/ 同名真资源 → 程序化色块/合成音，永不缺资源 | `Placeholder/PlaceholderFactory.cs`；清单见 [assets_upload_guide](assets_upload_guide.md) |
| 特效尺寸校准 | variant 根缩放按**目视校准**（勿按包围盒归一——拖尾/发射域会把包围盒撑到几十单位，按其缩放核心画面会消失，2026-07-10 教训）；现值：弹道/治疗/命中 1.0、剑击/穿刺 0.35、slash 0.25、光环 0.9~1.4；演出层只做相对缩放（`*=`），**禁止覆盖 localScale** | variant 在 `Assets/Resources/ClientBattle/VFX/`；来源映射见 [assets_upload_guide §一.1](assets_upload_guide.md) |
| 池化与缩放复位 | 特效对象池复用；出生缩放由 VfxOriginalScale 记录，回池自动还原 | `VFX/VFXManager.cs` |
| 特效预热（渲染级） | 开战前全部 VFX prefab 在**离屏 RT 相机前实渲 3 帧**再入池（仅实例化不渲染 warm 不到 shader/贴图）；PlayLoop 等 `PrewarmComplete` 再开播 | `VFXManager.Prewarm`（BuildWorld 调用） |
| 报告驱动预热 | 开战前扫一遍战报：台词/名字字形、状态图标纹理、合成音效、气泡对象全部前置生成，战斗热路径只剩查缓存 | `PerformanceRunner.PrewarmFromReport` |
| 飘字零分配动画与预热 | 开战前预建 24 个 TextMesh/动画记录并一次请求全部中文名、数字字形；运行时由服务统一 Update 上浮淡出，不再为每条飘字创建 DOTween Sequence/Tween/闭包，避免动态字体首次扩图与 GC 主线程尖峰 | `FloatingTextService.Prewarm/Update` + `ChineseNames.FloatingTextCharacters` |
| 贴图导入红线 | 特效包贴图 maxSize≤1024 + 压缩 + 关 mipmap（2026-07-10 全量重导，内存 723→258MB）；新导入资源包必须照此设置 | 三包目录 importer 设置 |
| 强度参数 | 滤镜/光环浓度 Intensity(0~3)、特殊图标 ExtraIconScale、斩击 StrikeVfxScale——全在 PerformanceDatabase 改，无需动代码 | `VFX/PerformanceProfile.cs` |

## 四、卡牌与 UI 表现

| 机制 | 一句话 | 代码 |
|---|---|---|
| 卡牌结构 | 卡框(阵营色染色，支持 CardFrames/frame.png)+立绘+血条+名字 | `Units/UnitView.cs` |
| 受击表现 | 抖动+红闪（暴击更强）+相机震动（profile 可关）；震动为 trauma 噪声模型，连抖叠加封顶不瞬移 | `UnitView.HitReact` + `VFX/CameraShaker.cs` |
| 待机呼吸 | 存活卡牌立绘正弦浮动，相位按位置错开；阵亡停止 | `UnitView.Update` |
| 兵力刷新 | 恒取事件 troops_after 权威值，客户端零计算 | `UnitView.SetTroops` |
| 状态图标 | **仅控制类**卡中央大图标；常规上方小图标已关闭 | `Units/StatusIconPanel.cs` |
| **状态常驻光环** | status_id → 光环 key 表（雷霆闪电缠绕/圣盾/血红/阳光/神使印记…）；status_apply 挂、status_remove/阵亡/整局重置撤；一次性 flipbook 粒子强制循环+补发射密度+压半透明 | `Units/UnitAuraService.cs`（想给新状态配光环只加表里一行） |
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
| OnGUI 缩放 | 横幅/调试按钮按屏幕高度缩放（800px 基准）；横幅白字+黑影双绘保证任何底色可读 | `PerformanceRunner.OnGUI` / `BattleReportTester.OnGUI` |
| **禁止事项** | 表现层不得写死 orthoSize/像素坐标/屏幕分辨率假设 | — |
| 性能验证 | 以**独立版 + FrameSpikeProbe** 数据为准；编辑器 Play 有环境级 1.7s 周期冻结、远程桌面有传输冻结，均非游戏问题（2026-07-10 定案：独立版 60fps 满帧零长帧） | `Test/FrameSpikeProbe.cs`（Tester 勾 ShowDiagnostics） |

## 六、布局与配色

- 棋盘：上下布局，A 队下、B 队上，队内按站位从左到右横排自动居中。
- 阵营配色：神金/人红/海蓝/冥紫，唯一源 `Units/BattleBoardView.cs` 内 `FactionColors` 常量（private），
  规范文档 [faction_style.md](faction_style.md)。
- 中文名：`Names/ChineseNames.cs` 与后端 `battle/names.py` 同步（红线）。

## 七、细则文档索引

| 想改什么 | 去哪看 |
|---|---|
| 播放单元/时间轴阻塞/台词独占/管线 processor | [playback_units.md](playback_units.md) |
| 分辨率适配/图像槽位/sorting 层级/布局 | [rendering_layout.md](rendering_layout.md) |
| 飘字/气泡/横幅/cut-in/字体调参 | [text_system.md](text_system.md) |
| 状态台词触发点（引擎侧） | [status_voice.md](../mechanics/status_voice.md) |
| 伤害响应谁先触发 / 他人神谕 vs 自身标记 | [response_order.md](../mechanics/response_order.md) |
| 鲁莽/踵之弱台词时点、雷霆圣盾特例 | [hero_specials.md](../mechanics/hero_specials.md) |
| 结算表谁记杀伤 | [settlement_stats.md](settlement_stats.md) |
| 框架分层/数据流向/逐文件职责 | [client_battle_framework.md](client_battle_framework.md) |
| 资源上传路径/命名/尺寸规格/获取分档 | [assets_upload_guide.md](assets_upload_guide.md) |
| 已购资源包与采购登记 | [assets_upload_guide.md §三](assets_upload_guide.md) |
| 阵营视觉规范 | [faction_style.md](faction_style.md) |
| 事件流契约（字段语义，只读） | `docs/schema/battle_events.md` + `battle_events_payloads.md` |
| 具体战法演出规格（任务书，只读） | `docs/prompts/client_perform.md` |
| 台词内容与性格触发时机 | `docs/mechanics/traits.md` + `battle/traits.py` |
| 单挑规则（服务器侧） | `docs/mechanics/`（duel 相关）+ Runner `PlayDuel` |

## 八、维护红线

1. 新增演出机制：先在本文登记一行，再写代码；行为变更同步更新对应行。
2. 特效缩放只允许相对相乘（`*=`），资源基准尺寸只在 variant 上改。
3. 演出参数（尺寸/强度/资源 key）一律走 PerformanceProfile，不写死在演出代码。
4. 客户端零结算、未知事件优雅跳过、troops_after 权威——契约红线见 [index.md](index.md)。
