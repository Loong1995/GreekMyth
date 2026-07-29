# 播放编译（PlaybackCompiler / 播放流）权威

> 2026-07-27 客户端逻辑解析重整定论：**播放需求的全部逻辑解析在开播前一次
> 做完**，运行期只顺序消费编译产物。本文是编译层（L2 出口）的唯一权威；
> 分层总纲见 [architecture.md](architecture.md)，行为规格见
> [playback_requirements.md](playback_requirements.md)。

## 一、数据流

```
战报 JSON ─ BattleReport.Parse ─→ 强类型模型（含 skill_catalog）
      │
      ▼  PlaybackCompiler.Compile（开播前一次，PlaybackWorldBuilder 调）
  逐局： EventPipeline.Run（分组 + processor 链） → CutInPlanner.Annotate
      │
      ▼
 CompiledPlayback（逐局 List<EventGroup>，运行期只读）
      ├─ PlaybackDirector.PlaySeries      主循环
      ├─ PerformanceRunner 高光回放        同一份产物按窗口播
      └─ Editor 菜单导出 .playback.json    离线审阅（见 §四）
```

三个消费方读**同一份**编译产物；任何一方再自行跑管线/推断语义即违规。
SkipToEnd 静默落账走原始事件流（终态等价，与分组无关）。

## 二、定义期标签：skill_catalog（1.5.0）/ status_catalog（1.5.2）

战法标签在**服务端定义处**声明（`battle.skills.Skill` 的
`damage_type`/`tags` 字段 + `category` 推导，register 强校验），经
`battle/skill_catalog.py` 进战报头。客户端 `BattleReport.SkillCatalog`
直读，编译层用途：

| 用途 | 位置 | 说明 |
|---|---|---|
| 追击 vs 主动分类 | `EventPipeline.Classify` | `category=="pursuit"` 直判，删 parent_seq 启发式（连发/借刀会让 parent 语义打架）|
| 伤害类型 | 演出层现读 `damage.damage_type`（逐条），目录为聚合视图/配阵页展示 | |
| 演出粒度特例 | `EventPipeline.AnnotateSkillTags` → `EventGroup.ForcePerTarget/ForceSimultaneous` | 标签 `per_target`＝群攻也逐目标演；`simultaneous`＝多段并成一拍齐射 |

`status_catalog`（1.5.2）同理来自 `StatusDef.playback_tags`，编译层用途只有一个：
`BatchTriggerMergeProcessor` 判断同批次的多次同状态触发能否并成一个播放单元
（`simultaneous` 跨持有者可并 / `sequential` 禁并 / 缺省同持有者可并）。
旧战报无该目录时回落 `StatusPresentationRegistry.CollectiveMerge`。

旧战报（<1.5.0，无目录）回落启发式并 LogWarning 一次；**不做向后兼容承诺**，
排查前先用 bridge / gen_golden 重新生成战报。

## 三、编译期 pass 清单（链序即语义，唯一登记处 `PlaybackCompiler.BuildPipeline`）

```
分组（group_id 全量聚合 + Classify + 因果批次 BatchId + 战法标签注记）
→ BorrowBladeSplitProcessor      借刀按段拆单元（L3 谓词注入）
→ ReactionRegroupProcessor       响应 tick 摘出后置
→ BatchTriggerMergeProcessor     同批次同状态触发并成一个播放单元（落雷齐发）
→ TraitLineExtractProcessor      台词独占组抽取
→ AchillesPierceTagProcessor     傲慢贯穿图标闸门
→ NodeMergeProcessor             节点合并
→ CutInPlanner.Annotate          取景 cut-in 判定注记（非重排，只写 EventGroup.CutIn）
```

### CutInPlanner（原 L4 CutInPolicy 下沉）

- 判据与阈值全在 `Events/CutInPlanner.cs`：巨伤 >3000（mitigation 非空不算）、
  行动窗第 5 次追击、满档（cut_in 事件 + **势能预演**已满轨）。
- **势能预演**：满档判据需要「落账前镜像值」。势能事件自带落账后 `value`，
  预演按组序重放 (hero,track)→value，判定读应用本组之前的值——与运行期
  MomentumService 镜像逐组等价（同一事件流同一次序）。轨有效性谓词由 L4
  注入（`MomentumService.TrackTable`，避免轨表双真源）。
- 运行期 `PlaybackDirector.PlayGroup` 只读 `group.CutIn`
  （HeroId/Title/Empowered/Massive），不再持有追击计数、不再查镜像。
  编排形状（推镜→横幅→出手→撤镜）仍在 [cutin_stage.md](cutin_stage.md)。

## 四、导出 .playback.json（排查入口）

菜单 `GreekMyth → 播放 → 导出 PlaybackScript`：选一份战报 JSON，
在旁边落 `<名>.playback.json` —— 逐局逐组列出
kind / root_seq / **batch（因果批次）** / 配置匹配 key / 事件清单 / cut-in 注记 /
并行与贯穿标记。排「这两组为什么没并成一个单元」先看 batch 是否相同。
与运行期完全同源（同一 `Compile` 调用），所见即所播；
排「为什么这组这么演」先看导出文件，不需要进 Play 模式断点。

### 4.1 每回合用时（`timing`，解析模型 `analytic-v2`）

导出时一并写入根字段 `timing`，算法在 `VFX/PlaybackDurationModel.cs`。
**不是启发式**：播放时间轴上每一拍的时长都是配置值，模型逐组照抄演出协程的
时长算术，模板判定与 `DefaultPerformance.Play` 同源（同一份
`PerformanceProfile`，由 `VFXResolver` 用运行期同一份 `PerformanceDatabase` 解析）。

| 时长来源 | 真源 |
|---|---|
| 单元/行动停顿 | `PerformanceRunner.GroupPauseSeconds` / `ActionPauseSeconds` × `DurationMul/Speed`（`PlaybackDirector.Wait01`）|
| 出手三拍 | `StagePerformanceConfig.Windup/Strike/RecoverSeconds` |
| 弹道飞行 / 落雷 / 治疗间隔 | `DefaultPerformance` 内基准秒（0.38 齐射 / 0.30 逐段 / 0.42 竖雷 / 0.3 治疗）|
| 取景 cut-in | `CutInCameraPushSeconds`+`HoldSeconds` + `CutInService.SoloRoutine`（0.16/0.5/0.14）|
| 单挑 | `DuelPerformance` + `DuelStage` 分幕常量（`Duel*Seconds`，轮数＝`clash_cutins`）|
| 台词独占 | `ChatBubbleService.ExclusiveSeconds`（1.14）|
| 连发加速 | `profile.BurstTempoScale`（`BurstNo≥2` 组内除以它）|

两处不能纯靠配置算，按**实测**建模：

1. **出手三拍的预备/收势**：两拍都用 DOTween `tween.WaitForCompletion()` 等待，
   而它不等满时长——实测 0.24s 预备只阻塞 ~0.05s、0.52s 收势只阻塞 ~0.10s
   （位移在后台继续，与下一拍重叠）。照配置算会让每段近身高估一倍（P-84）。
   若哪天改成真阻塞，模型里 `TweenWait*Seconds` 一并回到配置值。
2. **单挑厂包特效发射窗**（`DuelStage.Burst` / `FireResultVfx`）：运行期
   `VFXManager.EmitWindow` 探测、上限 `DuelVfxWaitCap=1.7`；离线取实测 1.2s。

另计入帧边界补偿（每拍半帧）。**预热与结算面板不计入**（不属战斗期）。

### 4.2 标定（模型改动后必做）

模型准不准只能拿真播的秒数比：`PlaybackDirector.OnGroupPlayed`（静态钩子，
默认 null 零开销）逐组回调真实秒数。流程：

1. Play 模式下挂钩子录一场 → 落 `<名>.measured.tsv`（`root_seq/kind/sec`）；
2. 导出 `.playback.json`（逐组带 `est_sec`）；
3. `python battle/tools/compare_playback_timing.py <playback.json> <measured.tsv>`
   —— 按 kind 给出 模型/真值 比值，偏离 1 的那一类就是算错的那一拍。

2026-07-28 基线（`manual_3v3_seed20260722`，DurationMul=2）：逐组比值 **0.99**，
各 kind 均 0.97~1.01，单组最大偏差 0.9s（单挑）；逐回合与真值差 ≤1.2s，
全场 216.6s vs 真值 220.6s。

### 4.3 人工校验：实时计时条（`PlaybackTimelineBar`）

屏幕下方一条实时计时条，用于**边看边对时**（`Test/PlaybackTimelineBar.cs`，
`BattleReportTester.ShowTimelineBar` 开关，右上角「计时条」按钮可切）：

- 游标＝`PerformanceRunner.TimelineSeconds`（真实秒，**预热不计入**）；
- 刻度点＝模型算出的各回合起始秒（`RoundTiming.StartSeconds`，`开` ＝开场段）；
- 读数「12.3s / 预估 216.6s 第 2 回合（预估 95.5~149.6s）」；
- **点刻度点 → 跳到该回合**：走 `PerformanceRunner.PlayFrom(gameIndex, startSeq,
  timelineOffset)`，起点之前的局与组静默落账（终态等价，同 SkipToEnd 口径），
  时钟直接对齐到该刻度秒数 —— 于是可以只校验某一回合，不必等前面播完。

对时判据：回合横幅弹出的那一刻，游标应正好压在对应刻度上（当前实测差 ≤1.2s）。

### 4.4 停播/重播必须清全场表现

`PerformanceRunner.ClearAllPresentation()` 是 R-1.2③ 的唯一实现处，
HardStop（含每次重播/跳播）都走它。**逐个走全局单例**而不是只走
`_session.Ctx`：重播会重建会话，旧 ctx 里的引用可能已丢，只清 ctx 就会留下
「上一场的台词气泡还挂在场上」（2026-07-28 实测残留）。清单：cut-in / 震屏 /
运镜还位 / 残影 / 光环 / 横幅 / **台词气泡** / 飘字 / 特效 / 音效，末尾
`DOTween.KillAll` 兜底。新增任何常驻表现服务，必须在此登记一行。

节奏参数：场上有 `PerformanceRunner` 时读它的实时值，否则用 Inspector 默认
（正常速度＝`DurationMul=2` / `Speed=1` / 行动 0.55 / 单元 0.35）。
`round_no=-1`（`label=开场`）＝首个 `RoundStart` 之前的登场/台词/单挑；
`0+` 与战报 `round_no` 对齐。每行另给 `pause_sec`（其中多少是编排停顿）。
菜单 `GreekMyth → 播放 → 估算回合用时` 只打 Console 不落盘。

离线打印：

```
python battle/tools/estimate_playback_rounds.py path/to/report.playback.json
```

（若只给原始战报路径且同目录已有 `*.playback.json` 则读后者。）

## 五、扩展纪律（加法式接入）

1. 新播放序语义 → 新 `IEventProcessor`，在 `BuildPipeline` 登记（注意链序）。
2. 新 cut-in 触发 → 只改 `CutInPlanner`。
3. 需要新的编译期决策（预演/快照类）→ 新增独立 pass 类 + 在 `Compile` 登记，
   产物写 EventGroup 注记字段；**禁止**在 Director/演出层运行期推断。
4. 需要新战法标签 → 服务端 `Skill.tags` 加法演进（客户端未知标签必须忽略）。
5. 某状态的触发要「一起播 / 必须逐次播」→ 改服务端 `StatusDef.playback_tags`
   （定义处一行），**不要**在客户端加白名单。
