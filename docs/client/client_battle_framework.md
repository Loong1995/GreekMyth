# ClientBattle 战报驱动特效框架（当前唯一客户端实现）

> 依据 `docs/prompts/client_perform.md` 重构。代码全部在
> `Assets/Scripts/ClientBattle/`（asmdef: ClientBattle，依赖 Newtonsoft + DOTween）。
> 旧 `Assets/Scripts/Battle/`（Playback/Presentation/Demo）已整体删除替换。
> 资源占位与上传方式见 `assets_upload_guide.md`。
> **架构权威**（分层依赖规则/生命周期/扩展点/服务端适配点）见
> [architecture.md](architecture.md)；行为规格见
> [playback_requirements.md](playback_requirements.md)。本文保留为逐文件速查。

## 一、数据流向图（文字版）

```
后端战报 JSON（schema 1.4.x）
   │  File / Inspector 粘贴（BattleReportTester）
   ▼
【第1层 事件模型】Events/
   BattleReportModel.Parse ──→ BattleReport{teams 快照, games[]}
   BattleEventParser       ──→ List<BattleEvent>（type 多态；未知类型→UnknownEvent 跳过）
   ▼
【第2层 事件流处理管线】Events/EventPipeline.cs
   按 group_id 初始分组 → processor 链：
     BorrowBladeSplitProcessor       借刀组按段拆单元并回插事件流原生位置
                                     （段→响应→追伤→下一段；谓词由 PlaybackWorldBuilder 注入）
     ReactionRegroupProcessor        把组内 status_tick 子链（雷霆/圣盾/试炼/震荡…）
                                     摘出为独立 StatusTrigger 组，追加在主单元之后
     CollectiveTriggerMergeProcessor 相邻同状态同来源的 StatusTrigger 组合并
                                     （白名单：thunder，雷霆集体齐发一次播出）
     TraitLineExtractProcessor       台词抽成独占 TraitLine 组（拆组保 Root）
     AchillesPierceTagProcessor      傲慢贯穿 TraitLine → 后续追伤组 PierceBoost 标记
     NodeMergeProcessor              纯节点组标记 ParallelWithNext（静默落账不占节拍）
   ──→ List<EventGroup>（播放单元：Kind + Root + Events）
   ▼
【第3层 特效解析】VFX/VFXResolver.cs
   三级优先级：特殊配置(PerformanceDatabase.SpecialProfiles，按 skillId/statusId)
     → 组默认(主动/普攻/追击/状态触发/神谕) → 全默认
   未配置 skillId 首次 LogWarning；任何情况必有 profile 返回
   ▼
【第4层 播放核心】（2026-07-23 拆分，见 architecture.md §二）
   PerformanceRunner    控制器：状态机 + 生命周期入口 + 唯一协程宿主 + HardStop
   PlaybackWorldBuilder 建世界 → PlaybackSession（会话状态容器）
   PlaybackDirector     主循环/组分派/节奏；CutInPolicy 集中 cut-in 判定与阈值
   落账统一走 EventApplyService（含伤害/治疗镜像写入 ApplyDamage/ApplyHeal）
   横幅 → BannerService；cut-in → CutInService.Request
   战斗动作组 → SkillPerformance.Play(group, profile, ctx) 协程：
     DefaultPerformance     群攻中心 AOE / 单体逐段 / 普攻近身 / 状态触发
     OracleAuraPerformance  神谕：施加完所有单位后一次性挂光环 + 整盘滤镜
     DuelPerformance        单挑全流程（压暗/号角/台词/裂缝交错 cut-in/胜负）
   ▼
【第5层 基础设施】
   VFXManager(对象池 PlayAt/PlayOn/Release + 离屏实渲预热) CameraShaker(trauma 噪声震动)
   CameraFitter(机型分辨率自适配·唯一取景权威) BackgroundFitter(背景 cover 铺满，BattleBoardView 内嵌类)
   FloatingTextService(飘字) ChatBubbleService(台词气泡) SfxManager(同帧去重)
   BattleBoardView/UnitView/StatusIconPanel(卡牌 GameObject 树)
   UnitAuraService(状态→常驻循环光环: 施加挂/移除撤/阵亡清/整局重置清)
```

## 二、文件清单与职责

| 文件 | 职责 |
|---|---|
| `Events/BattleEvents.cs` | BattleEvent 基类 + 全部派生事件 + BattleEventParser（多态反序列化） |
| `Events/BattleReportModel.cs` | 战报顶层模型（阵容快照/逐局事件），schema 1.x 校验 |
| `Events/EventPipeline.cs` | EventGroup / IEventProcessor / 管线（注册式，可加自定义分析器） |
| `Events/Processors/ReactionRegroupProcessor.cs` | 状态触发子链拆组后置（补发重组） |
| `Events/Processors/CollectiveTriggerMergeProcessor.cs` | 相邻同状态同来源 StatusTrigger 组合并（雷霆集体齐发） |
| `Events/Processors/NodeMergeProcessor.cs` | 静默节点并行标记 |
| `Events/Processors/BorrowBladeSplitProcessor.cs` | 借刀分段：BorrowBlade 组按直接子伤害切段、按首事件 seq 稳定重排恢复与响应/追伤的原生交错（2026-07-22） |
| `VFX/VFXResolver.cs` | 三级配置查找 + 未配置告警 |
| `VFX/PerformanceProfile.cs` | 单条演出配置（模板 + 资源 key + 强度参数） |
| `VFX/PerformanceDatabase.cs` | 配置库 SO；缺资产时代码内置全部特殊战法配置 |
| `VFX/SkillPerformance.cs` | 演出抽象基类 + 结算事件→表现公共原语；跨层通知走 `VFXContext.OnDamageSettled/OnCutInRequested` 回调（禁止反向引用 Runner 单例） |
| `VFX/Performances/DefaultPerformance.cs` | 默认策略族（AOE 中心/逐段/近身/状态触发） |
| `VFX/StrikeBeats.cs` | **出手三拍唯一实现**：预备（反向蓄力）→ 发力（加速突进 + 残影）→ 收势（过冲回位）。`PlayMelee` / `PlayAoeCenter` 共用；模板禁止自拼 `DOMove` 节奏 |
| `VFX/StrikeSync.cs` | **出手时间轴唯一真源**：飞行段按弹道真实位置广播进度给 `IFlightDriven`，`Run()` 返回＝抵达＝调用方同帧开命中拍。裂地经 `GroundCrackService.PathDriver` 挂上；细则见 ground_crack_language / performance_mechanisms |
| `VFX/Performances/OracleAuraPerformance.cs` | 神谕整单元宣告 + 程序化整盘滤镜 BoardFilterOverlay（Intensity 可调；光环本体由 UnitAuraService 按状态挂） |
| `VFX/Performances/DuelPerformance.cs` | 单挑播放单元（压暗/号角/duel_* 台词/裂缝交错 cut-in/胜负横幅/败者惩罚落账），2026-07-22 自 Runner 拆出 |
| `VFX/EventApplyService.cs` | **唯一落账入口**（R-7.4）：`Apply(ev, ctx, animated)` 刷视图镜像；`ApplyDamage/ApplyHeal` 为伤疗兵力写入唯一实现（演出路径 SettleDamage/SettleHeal 亦经此，静默/演出写账同源） |
| `VFX/PlaybackSession.cs` | 会话状态容器（R-7.3）+ PlaybackState 状态机枚举 + IPlaybackPacing 节奏只读口 |
| `VFX/PlaybackWorldBuilder.cs` | 建世界唯一实现：棋盘/管线注册/VFXContext 装配/镜像清零/报告驱动预热/BGM 起播 |
| `VFX/PlaybackDirector.cs` | 主循环与组分派（PlaySeries/PlayGroupsRange/PlayGroup/PlayNode）；无生命周期职责 |
| `VFX/CutInPolicy.cs` | cut-in 判定与阈值集中地（高伤 3000/第 5 追击/满档轨判定/技能标题） |
| `VFX/BannerService.cs` | 顶部横幅 + 无主体 cut-in 的 OnGUI 文字回退（2026-07-22 自 Runner 拆出） |
| `VFX/HighlightSelector.cs` | 高光选窗纯函数（观感分=伤害+满势能 cut_in×3000），重播复用 `PlaybackDirector.PlayGroupsRange` |
| `Units/MomentumFireController.cs` | **势能火生命周期唯一管理**：Refresh/Fade/Extinguish/Clear + 棋盘级相位信号；hold-off=抑制同值重挂（值变化即重新点火，2026-07-22 修 g1r5） |
| `Test/SettlementPanel.cs` | 战后结算面板 OnGUI 绘制（三谋式分队/分局 Tab），2026-07-22 自 Runner 拆出 |
| `Units/UnitAuraService.cs` | **状态常驻光环**：注册表驱动；`shroud_*` → `MountShroud` + `VfxShroudPresence`；`HasShroud`＝视觉 `IsPresent`；`SetShroudVisible` 任意时机 |
| `Units/MomentumService.cs` | **四轨势能镜像账本**（Phase 4 B1/B2）：momentum_change 落账、TrackTable 注册表（轨→tint/标签）、满档溢出触发、**round_start 全体清零**；BGM 经 `GlobalMomentumChanged` 回调解耦；细则见 performance_mechanisms §一b |
| `VFX/PerformanceRunner.cs` | **播放控制器**（2026-07-23 瘦身）：PlaybackState 状态机、Play/Replay/Skip/Teardown/Highlight/Stop 全部入口、唯一协程宿主、HardStop 硬停止单一实现；主循环/建世界/策略已拆出 |
| `VFX/VFXManager.cs` | 特效池 + 离屏实渲预热（Prewarm：全部 prefab 在离屏 RT 相机前实渲 3 帧，shader 编译/贴图上传压进加载期，PlayLoop 等 PrewarmComplete 再开播） |
| `VFX/CameraShaker.cs` | trauma 噪声模型震动：连抖累加封顶、Perlin 偏移、衰减自动复位（升级点：Cinemachine Impulse） |
| `Units/StagePerformanceConfig.cs` | **舞台演出参数唯一收口**：机位俯角 / 卡姿抖动 / 微调圆 / 击退 / 受击颤动 / 三拍 / 残影 / 接地阴影。改数字即调参；表现类禁止另写调参 const |
| `Units/AfterImageService.cs` | 突进残影：卡面运行期快照的环形池（order −2），`HardStop` 统一收。非 prefab，故不入 VFXManager 池 |
| `Units/CardGroundShadow.cs` | 卡牌接地阴影（order −3，近 3D 舞台才建）：软椭圆随卡尺自适应，抬离地面越高越小越淡。挂卡牌父级，`LateUpdate` 取位 |
| `Units/CardIdleMotion.cs` | **卡面生动性唯一写入者**：待机呼吸 / 惯性视差 / 受击挤压三通道合成为立绘的 pos+scale+rot，每帧一次写入、零 alloc。要动立绘 Transform 的新表现一律走它，禁止另起 tween 抢同一组件 |
| `VFX/CutInService.cs` | **全屏 cut-in**：请求入口 Request（组去重+主体分发+duck）；单人斜带+巨幅立绘（PlaySolo，非阻塞）；决斗裂缝交错（DuelClashRoutine，阻塞，两半屏卡对向滑过裂缝线×clash_cutins） |
| `VFX/CameraFitter.cs` | **机型兼容唯一权威**：按宽高比动态调 orthoSize 保安全区（半宽 4.6/半高 5.2），分辨率热切换每帧跟随；表现层禁止写死 orthoSize/像素坐标 |
| `Units/BattleBoardView.cs` | 建棋盘（A 下 B 上、阵型落点）、unitId→UnitView、背景（默认无色纯黑，上传底图则 BackgroundFitter cover 铺满）、整盘滤镜挂点 |
| `Units/UnitView.cs` | 卡牌 GameObject：立绘/血条/受击/石化边框渐变/压暗/阵亡/待机呼吸（立绘错相位浮动，画面永远有活物）/四轨势能迷你条+满档流光 |
| `Units/StatusIconPanel.cs` | 硬控/冥火卡顶外侧横排（宽≈卡宽 1/5）+ 抖动；先攻/犹豫不展示 |
| `Events/Processors/AchillesPierceTagProcessor.cs` | 傲慢贯穿 TraitLine → 标记随后 achilles_wrath 追伤组（裂甲图标闸门） |
| `Units/FloatingTextService.cs` | 所有伤害/治疗/状态头顶飘字（技能名+数值，硬性要求） |
| `Units/ChatBubbleService.cs` | 台词独占气泡（`SayExclusive` 时长对齐时间轴） |
| `Audio/SfxManager.cs` | 音效池 + 同帧同 key 去重（状态与伤害音效不重复） |
| `Audio/BgmLayerService.cs` | **BGM 分层混音**（B3）：4 stem 随全局势能三档淡入淡出、小节对齐切层、单挑/cut-in duck；占位单曲回退（音量+低通）；素材路线见 phase4_manual_tasks |
| `Units/FloatingTextTuning.cs` | 飘字调参 SO（B4）：字体/字号/颜色/上浮曲线，Inspector 实时调；操作文档 floating_text_tuning.md |
| `Placeholder/PlaceholderFactory.cs` | 占位资源三级回退最后一层（程序化色块/合成音） |
| `Names/ChineseNames.cs` | 战法/状态/属性中文名（与 battle/names.py 同步维护） |
| `Names/StatusPresentationRegistry.cs` | **状态表现注册表**：光环 key/控制图标/结算归因/集体齐发/`ShroudVisibility`；各服务只读本表 |
| `Test/BattleReportTester.cs` | 测试入口：文件或粘贴 JSON、调速/跳过/重播按钮；vSync/后台运行/窗口化基线、ESC 退出（新 Input System） |
| `Test/FrameSpikeProbe.cs` | 诊断（默认关）：帧尖峰日志 + 心跳转子；性能验证以独立版探针数据为准 |

## 三、关键机制

1. **播放单元与补发重组**：后端把状态触发结算挂在主动作组内；
   `ReactionRegroupProcessor` 按 parent_seq 传递闭包把每个 `status_tick` 子链拆成
   独立组追加在原组后 → 「群攻主动播完后补发雷霆集体触发/圣盾逐次触发」，
   且特殊状态触发动画永远在其他单元之后播。
2. **三级表演策略**：默认（组内默认模板）→ 组优化（ActiveDefault 等 5 条）→
   特殊配置（SpecialProfiles 按 id 精确匹配，最高优先级）。全部数据在
   `PerformanceDatabase`（SO 可视化编辑；无资产时用代码默认，两者字段一致）。
3. **事件为准，零客户端结算**：兵力恒取 `troops_after`；表现层只读事件。
   未知事件类型/未知字段跳过继续播（向前兼容义务）。
4. **占位三级回退**：`Resources/ClientBattle/<类别>/<key>` 有则用（上传即生效，
   零代码改动），无则程序化占位（色块特效/哈希配色/合成提示音），必能播出。
5. **默认策略要点**（client_perform §一逐条落实）：
   群攻(≥2 目标)＝一个单元、施法者移中心、N 道刀光/魔法光（按伤害类型选 key）；
   单体主动按伤害段数逐段；普攻近身命中帧闪斩击（×1.0，追击 ×1.5）；
   追击群攻走主动、单体走普攻;
   硬控/冥火状态图标卡顶横排（先攻/犹豫不展示）；同帧状态+伤害音效去重；
   状态触发飘状态来源战法名；性格台词气泡当场弹出。
6. **扩展点**：`EventPipeline.Register` 加自定义分析器；`SkillPerformance` 派生新
   演出模板（进场动画/死亡动画后续在 `PlaybackDirector.PlayGroup` 的 Defeat/
   game_start 分支挂新模板，落账仍走 `EventApplyService`）。

## 四、运行方式

1. 新建空场景 → 空物体挂 `BattleReportTester`（旧 BattleDemo 场景已删除）。
2. `ReportPath` 默认 `battle_reports/burst_tactics_seed42.json`（Phase 4 验收场景）
   （`Assets/StreamingAssets/battle_reports/` 下 6 份演示战报，可用
   `python battle/tools/gen_golden.py` 重新生成后拷入）。
3. Play：自动播放全场；右上角按钮＝重播 / 跳到结尾 / 调速。
