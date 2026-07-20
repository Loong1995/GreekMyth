# ClientBattle 战报驱动特效框架（当前唯一客户端实现）

> 依据 `docs/prompts/client_perform.md` 重构。代码全部在
> `Assets/Scripts/ClientBattle/`（asmdef: ClientBattle，依赖 Newtonsoft + DOTween）。
> 旧 `Assets/Scripts/Battle/`（Playback/Presentation/Demo）已整体删除替换。
> 资源占位与上传方式见 `assets_upload_guide.md`。

## 一、数据流向图（文字版）

```
后端战报 JSON（schema 1.3.x）
   │  File / Inspector 粘贴（BattleReportTester）
   ▼
【第1层 事件模型】Events/
   BattleReportModel.Parse ──→ BattleReport{teams 快照, games[]}
   BattleEventParser       ──→ List<BattleEvent>（type 多态；未知类型→UnknownEvent 跳过）
   ▼
【第2层 事件流处理管线】Events/EventPipeline.cs
   按 group_id 初始分组 → processor 链：
     ReactionRegroupProcessor        把组内 status_tick 子链（雷霆/圣盾/试炼/震荡…）
                                     摘出为独立 StatusTrigger 组，追加在主单元之后
     CollectiveTriggerMergeProcessor 相邻同状态同来源的 StatusTrigger 组合并
                                     （白名单：thunder，雷霆集体齐发一次播出）
     NodeMergeProcessor              纯节点组标记 ParallelWithNext（静默落账不占节拍）
   ──→ List<EventGroup>（播放单元：Kind + Root + Events）
   ▼
【第3层 特效解析】VFX/VFXResolver.cs
   三级优先级：特殊配置(PerformanceDatabase.SpecialProfiles，按 skillId/statusId)
     → 组默认(主动/普攻/追击/状态触发/神谕) → 全默认
   未配置 skillId 首次 LogWarning；任何情况必有 profile 返回
   ▼
【第4层 演出执行】VFX/PerformanceRunner.cs（单例，一键 PlayBattleReport(json)）
   节点/单挑/台词/阵亡 → Runner 内置演出
   战斗动作组 → SkillPerformance.Play(group, profile, ctx) 协程：
     DefaultPerformance     群攻中心 AOE / 单体逐段 / 普攻近身 / 状态触发
     OracleAuraPerformance  神谕：施加完所有单位后一次性挂光环 + 整盘滤镜
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
| `VFX/VFXResolver.cs` | 三级配置查找 + 未配置告警 |
| `VFX/PerformanceProfile.cs` | 单条演出配置（模板 + 资源 key + 强度参数） |
| `VFX/PerformanceDatabase.cs` | 配置库 SO；缺资产时代码内置全部特殊战法配置 |
| `VFX/SkillPerformance.cs` | 演出抽象基类 + 结算事件→表现公共原语 |
| `VFX/Performances/DefaultPerformance.cs` | 默认策略族（AOE 中心/逐段/近身/状态触发） |
| `VFX/Performances/OracleAuraPerformance.cs` | 神谕整单元宣告 + 程序化整盘滤镜 BoardFilterOverlay（Intensity 可调；光环本体由 UnitAuraService 按状态挂） |
| `Units/UnitAuraService.cs` | **状态常驻光环表**：status_id→aura key（雷霆/圣盾/血红/阳光/神使印记）；粒子强制循环+补密度+半透明 |
| `Units/MomentumService.cs` | **四轨势能镜像账本**（Phase 4 B1/B2）：momentum_change 落账、TrackTable 注册表（轨→tint/标签）、满档溢出触发、action_start 清零；细则见 performance_mechanisms §一b |
| `VFX/PerformanceRunner.cs` | 播放主循环单例：PlayBattleReport / SkipToEnd / OnAllComplete；开战前 PrewarmFromReport（字形/图标/音效/气泡按战报内容前置生成） |
| `VFX/VFXManager.cs` | 特效池 + 离屏实渲预热（Prewarm：全部 prefab 在离屏 RT 相机前实渲 3 帧，shader 编译/贴图上传压进加载期，PlayLoop 等 PrewarmComplete 再开播） |
| `VFX/CameraShaker.cs` | trauma 噪声模型震动：连抖累加封顶、Perlin 偏移、衰减自动复位（升级点：Cinemachine Impulse） |
| `VFX/CameraFitter.cs` | **机型兼容唯一权威**：按宽高比动态调 orthoSize 保安全区（半宽 4.6/半高 5.2），分辨率热切换每帧跟随；表现层禁止写死 orthoSize/像素坐标 |
| `Units/BattleBoardView.cs` | 建棋盘（A 下 B 上按站位横排）、unitId→UnitView、背景（默认无色纯黑，上传底图则 BackgroundFitter cover 铺满）、整盘滤镜挂点 |
| `Units/UnitView.cs` | 卡牌 GameObject：立绘/血条/受击/石化边框渐变/压暗/阵亡/待机呼吸（立绘错相位浮动，画面永远有活物）/四轨势能迷你条+满档流光 |
| `Units/StatusIconPanel.cs` | 仅控制类大图标卡中央居中折行；常规上方小图标已关闭 |
| `Units/FloatingTextService.cs` | 所有伤害/治疗/状态头顶飘字（技能名+数值，硬性要求） |
| `Units/ChatBubbleService.cs` | 台词独占气泡（`SayExclusive` 时长对齐时间轴） |
| `Audio/SfxManager.cs` | 音效池 + 同帧同 key 去重（状态与伤害音效不重复） |
| `Audio/BgmLayerService.cs` | **BGM 分层混音**（B3）：4 stem 随全局势能三档淡入淡出、小节对齐切层、单挑/cut-in duck；占位单曲回退（音量+低通）；素材路线见 phase4_manual_tasks |
| `Units/FloatingTextTuning.cs` | 飘字调参 SO（B4）：字体/字号/颜色/上浮曲线，Inspector 实时调；操作文档 floating_text_tuning.md |
| `Placeholder/PlaceholderFactory.cs` | 占位资源三级回退最后一层（程序化色块/合成音） |
| `Names/ChineseNames.cs` | 战法/状态/属性中文名（与 battle/names.py 同步维护） |
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
   控制类状态图标居中折行；同帧状态+伤害音效去重；
   状态触发飘状态来源战法名；性格台词气泡当场弹出。
6. **扩展点**：`EventPipeline.Register` 加自定义分析器；`SkillPerformance` 派生新
   演出模板（进场动画/死亡动画后续在 Runner 的 Defeat/game_start 分支挂新模板）。

## 四、运行方式

1. 新建空场景 → 空物体挂 `BattleReportTester`（旧 BattleDemo 场景已删除）。
2. `ReportPath` 默认 `battle_reports/standard_seed20260705.json`
   （`Assets/StreamingAssets/battle_reports/` 下 6 份演示战报，可用
   `python battle/tools/gen_golden.py` 重新生成后拷入）。
3. Play：自动播放全场；右上角按钮＝重播 / 跳到结尾 / 调速。
