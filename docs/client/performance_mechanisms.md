# 客户端特效演出机制总纲（performance_mechanisms）

> 本文是**所有演出机制的唯一总目录**：每条机制给出一句话结论 + 代码位置 +
> 细则文档链接。改任何演出行为前先来这里定位。规格来源：
> `docs/prompts/client_perform.md`（任务书，只读）。

## 一、播放主流程（事件 → 画面）

| # | 机制 | 一句话 | 代码 | 细则 |
|---|---|---|---|---|
| 1 | 事件解析 | 战报 JSON → 强类型事件对象，未知类型降级 UnknownEvent 跳过不崩 | `Events/BattleEvents.cs` | [framework §第1层](client_battle_framework.md) |
| 2 | 分组 | 按 group_id 聚成 EventGroup（root+副事件），再按 root 类型分类 GroupKind | `Events/EventPipeline.cs` | [framework §第2层](client_battle_framework.md) |
| 3 | 反应后置 | 响应类 status_tick（雷霆/圣盾反制等）从主组拆出，排到主组之后独立播放 | `Events/Processors/ReactionRegroupProcessor.cs` | 同上 |
| 4 | 节点合并 | 纯记账节点（自然损耗等）标记 ParallelWithNext 静默落账不停顿；大节点（回合/单挑/终局）独立演出 | `Events/Processors/NodeMergeProcessor.cs` | 同上 |
| 5 | 三级演出配置 | 特殊配置(每战法一条) → 组默认(主动/普攻/追击/状态/神谕) → 全默认兜底；未配置 LogWarning | `VFX/VFXResolver.cs` + `VFX/PerformanceDatabase.cs` | [framework §第3层](client_battle_framework.md) |
| 6 | 播放主循环 | 逐组协程播放；Speed 调速、SkipToEnd 静默落账快进 | `VFX/PerformanceRunner.cs` | [framework §第4层](client_battle_framework.md) |

**队列阻塞规则（2026-07-10 定）**：只有行动类播放单元（主动/普攻/追击/
状态触发/单挑）可以占用时间轴；回合与局节点、状态/属性变化、飘字、性格台词、
阵亡、神谕/被动宣告均即时触发表现后继续，不得添加 `WaitForSeconds`。
飘字动画可异步持续，但不能阻塞下一组。

**零死帧原则（2026-07-10 定）**：行动组内的每个 yield 等待必须对应一段正在
播放的可见动画（位移 tween / 弹道飞行 / 命中·治疗特效窗口）；禁止"纯定格"
等待垫时长。全场唯一允许的静止是单挑横幅。卡牌待机呼吸
（`UnitView.Update` 立绘错相位浮动）保证画面任意时刻有活物。

**节奏停顿（2026-07-12 调参）**：每个英雄行动结束停 0.45s、每个行动类播放单元
结束停 0.25s（`PerformanceRunner.ActionPauseSeconds/GroupPauseSeconds`，
Inspector 可调，随 Speed 缩放；约束：单元停顿 < 行动停顿）。停顿期间常驻动画
（待机呼吸/光环/飘字/滤镜）照常播放，与零死帧原则不冲突。

**播放单元完整性（红线，2026-07-12 修）**：初始分组必须按 group_id **全量聚合**
（组序=首次出现序），不能只合并连续段——群攻主动的 N 条伤害之间会被状态触发
（雷霆 tick 等 new_group 事件）插队，连续段合并会把一次群攻切成 N 个碎片，
违反 client_perform「群攻战法以该战法为一个播放单元（一次释放 N 道光）」。
雷霆落雷经 `CollectiveTriggerMergeProcessor` 合并为**一次集体齐发**（同状态
同来源的连续 StatusTrigger 组并组，白名单控制）；圣盾等保持逐次触发。

**节点组子事件落账（红线）**：round_start / action_start 等节点组的**全部子事件**
（状态到期移除、伤兵损耗、属性回写）必须 ApplySilently 落账——状态到期移除
（石化解除等）挂在节点之下，漏掉会造成图标与石化覆盖层残留。

## 二、演出模板族（每组怎么演）

| 模板 | 触发条件 | 演出 | 代码 |
|---|---|---|---|
| Melee 普攻/近身 | GroupKind=NormalAttack；单体追击；反制类特殊配置 | 施法者冲至被打者近身 → 命中帧在**被打者身上闪斩击** → 回位 | `DefaultPerformance.PlayMelee` |
| AoeCenter 群攻 | 主动且互异目标 ≥2 | 施法者移动到棋盘中心 → N 道刀光/魔法光齐射 → 同帧掉血 | `DefaultPerformance.PlayAoeCenter` |
| PerSegment 逐段 | 单体主动/多段 | 每段一个节拍：弹道 → 命中掉血 | `DefaultPerformance.PlayPerSegment` |
| StatusTrigger | 状态触发组 | 走主动逻辑，飘字用状态来源战法名 | 同上（模板内分派） |
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
| 特效尺寸校准 | variant 根缩放按**目视校准**（勿按包围盒归一——拖尾/发射域会把包围盒撑到几十单位，按其缩放核心画面会消失，2026-07-10 教训）；现值：弹道/治疗/命中 1.0、剑击/穿刺 0.35、slash 0.25、光环 0.9~1.4；演出层只做相对缩放（`*=`），**禁止覆盖 localScale** | variant 在 `Assets/Resources/ClientBattle/VFX/`；来源映射见 [assets_upload_guide §3](assets_upload_guide.md) |
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
| 状态图标 | 常规状态卡上方横排折行；控制类（沉默/缴械/石化…）卡中央大图标 | `Units/StatusIconPanel.cs` |
| **状态常驻光环** | status_id → 光环 key 表（雷霆闪电缠绕/圣盾/血红/阳光/神使印记…）；status_apply 挂、status_remove/阵亡/整局重置撤；一次性 flipbook 粒子强制循环+补发射密度+压半透明 | `Units/UnitAuraService.cs`（想给新状态配光环只加表里一行） |
| 整盘滤镜 | 程序化全屏呼吸色罩（血红/海蓝/冥紫按 key 取色），不用粒子 prefab（会成棋盘中心固定点）；透明度待真棋盘底图定稿再调 | `OracleAuraPerformance.BoardFilterOverlay` |
| 石化 | 灰色卡框覆盖层淡入淡出 + 石化开/解音效 | `UnitView.SetPetrified` |
| 飘字 | 伤害红/治疗绿/真伤黄/状态蓝灰/属性金紫，同单位纵向堆叠不重叠 | `Units/FloatingTextService.cs` |
| 台词气泡 | trait_trigger 推送即播；9 字折行、按行数拉底板、同单位排队 | `Units/ChatBubbleService.cs`；台词配置在 `battle/traits.py` |
| 音效 | 同帧同 key 去重 + 每帧上限，防爆音 | `Audio/SfxManager.cs` |
| 阵亡 | 变灰倒下保留尸位；主将阵亡横幅强调 | `UnitView.PlayDefeated` |

## 五、机型兼容（红线）

| 机制 | 一句话 | 代码 |
|---|---|---|
| 取景权威 | CameraFitter 按宽高比动态调 orthoSize，保安全区（半宽 4.6/半高 5.2）任意机型完整可见，热切换每帧跟随 | `VFX/CameraFitter.cs` |
| 背景 | 无色（纯黑）；上传 UI/board_background.png 自动 cover 铺满不变形 | `Units/BattleBoardView.BuildBackground` + BackgroundFitter |
| OnGUI 缩放 | 横幅/调试按钮按屏幕高度缩放（800px 基准）；横幅白字+黑影双绘保证任何底色可读 | `PerformanceRunner.OnGUI` / `BattleReportTester.OnGUI` |
| **禁止事项** | 表现层不得写死 orthoSize/像素坐标/屏幕分辨率假设 | — |
| 性能验证 | 以**独立版 + FrameSpikeProbe** 数据为准；编辑器 Play 有环境级 1.7s 周期冻结、远程桌面有传输冻结，均非游戏问题（2026-07-10 定案：独立版 60fps 满帧零长帧） | `Test/FrameSpikeProbe.cs`（Tester 勾 ShowDiagnostics） |

## 六、布局与配色

- 棋盘：上下布局，A 队下、B 队上，队内按站位从左到右横排自动居中。
- 阵营配色：神金/人红/海蓝/冥紫，唯一源 `Units/BattleBoardView.FactionColors`，
  规范文档 [faction_style.md](faction_style.md)。
- 中文名：`Names/ChineseNames.cs` 与后端 `battle/names.py` 同步（红线）。

## 七、细则文档索引

| 想改什么 | 去哪看 |
|---|---|
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
