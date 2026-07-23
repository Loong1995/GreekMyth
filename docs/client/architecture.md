# 客户端播放架构（v2.0，2026-07-23 重构版）

> 本文是 `Assets/Scripts/ClientBattle/` 的**架构权威**：模块划分、依赖规则、
> 生命周期与所有权、扩展点、服务端适配点。行为规格见
> [playback_requirements.md](playback_requirements.md)（下称 R-条款）；
> 各子机制细则经 §五索引到专项文档。取代旧 client_battle_framework.md
> 中的框架描述（该文保留为逐文件速查，已指回本文）。

## 一、分层与依赖规则

```
 L1 事件模型   Events/            战报 JSON → 强类型（纯数据，零 Unity 依赖倾向）
 L2 事件管线   Events/Processors/ group_id 聚合 + 播放序加工（纯数据变换）
 L3 演出配置   VFX/Config…        三级 PerformanceProfile 查找（纯查表）
 L4 播放核心   VFX/Playback…      会话/编排/落账/演出模板（协程 + 领域镜像）
 L5 基础设施   VFX·Units·Audio    池/飘字/气泡/音效/BGM/相机/cut-in/横幅（无战斗语义）
 视图         Units/UnitView 等   卡牌与棋盘可视（只被 L4/L5 调用，不知道事件）
```

依赖规则（重构后的强约束，评审红线）：

1. **单向**：L1←L2←L3←L4；L4 调 L5 与视图；L5/视图不得引用 L1~L4 类型
   （BannerService 不再探测 Test 层，CollectiveMerge 经注入的白名单谓词
   解除对 Names 的硬依赖属长期项，现状登记 §七）。
2. **落账唯一**：视图镜像（兵力/状态/势能/阵亡）只能被 `EventApplyService`
   写入（R-7.4）。演出模板产出表现，落账一律回调该服务。
3. **会话集中**：一次播放的可变状态全部挂在 `PlaybackSession`（R-7.3）；
   static 只允许纯配置表（ChineseNames、StatusPresentationRegistry、
   TrackTable、FactionColors）与无状态工具。
4. **tween/协程宿主**（R-7.1/7.2）：单位与特效上的 tween 必须
   `SetLink(宿主GO)`；播放期协程宿主仅限 PerformanceRunner（主时间轴）与
   基础设施服务自身。`DOTween.KillAll` 仅允许出现在硬停止兜底一处。

## 二、L4 播放核心分解（原 PerformanceRunner 拆分）

原 558 行 Runner 按职责拆为四件，公开 API 兼容不变
（`PerformanceRunner` 保留为薄门面，Test/Manual 页无感）：

| 模块 | 文件 | 职责 |
|---|---|---|
| **PerformanceRunner（控制器）** | `VFX/PerformanceRunner.cs` | 状态机（R-1.1）与全部生命周期入口：Play/Skip/Teardown/Highlight/Stop；唯一协程宿主；硬停止（R-1.2）的单一实现 `HardStop()`；类名沿用保证公开 API 兼容 |
| **PlaybackWorldBuilder** | `VFX/PlaybackWorldBuilder.cs` | 建世界：棋盘/单位/服务 Ensure/管线注册/VFXContext 装配/预热；产出 `PlaybackSession` |
| **PlaybackDirector** | `VFX/PlaybackDirector.cs` | 主循环：逐局→管线→逐组调度；组分派（Node/TraitLine/Duel/演出模板）；节奏（ActionPause/GroupPause/TraitLine 无缝）；范围播放（高光窗复用，R-1.6） |
| **CutInPolicy** | `VFX/CutInPolicy.cs` | 满档/高伤/第5追击/战术 cut-in 判定与组去重（R-5.2），阈值集中于此 |
| **PlaybackSession** | `VFX/PlaybackSession.cs` | 会话状态容器（纯字段）：report/board/ctx/resolver/pipeline/演出模板/结算快照/追击计数；重建时整体丢弃引用即作废（cut-in 组去重在 CutInService，会话建立时 `ResetDedup`） |

生命周期迁移表（所有公开入口只能走这些迁移）：

| 入口 | 迁移 |
|---|---|
| PlayBattleReport | 任意态 → HardStop → Building → Prewarming → Playing → Finished |
| 重播（再次 PlayBattleReport） | 同上（R-1.3，会话全弃重建；Manual 页封装为 `ReplayLastReport`） |
| SkipToEnd | Playing → HardStop → 静默全量落账 →（Finished，可选弹结算，R-1.4） |
| TeardownWorld | 任意态 → HardStop → 销毁战场 → Idle（R-1.5） |
| PlayHighlight | 同 PlayBattleReport + [start,end) 窗口参数 |

`HardStop()` 固定次序：停自身协程 → CutIn CancelAll / CameraShaker.Cancel /
UnitAuraService.ClearAll → **Banner.Clear**（系列结束横幅等常驻文案）→
Vfx/Floats/Bubbles CancelAll + Sfx StopAll → `DOTween.KillAll(false)` 兜底 →
BGM StopBattle。棋盘销毁只在 TeardownWorld。

## 三、落账与演出的单一数据流

```
EventGroup ─→ PlaybackDirector.PlayGroup
   ├─ 非演出组 ──────────────→ EventApplyService.Apply(ev, animated:false)
   └─ 演出组 → SkillPerformance.Play（协程，只做表现）
        ├─ 伤害段：命中特效/飘字/顿挫 + EventApplyService.ApplyDamage(ev, ctx)
        ├─ 治疗段：治疗特效/绿字   + EventApplyService.ApplyHeal(ev, ctx)
        └─ 副事件：SettleSideEvent → EventApplyService.Apply(ev, animated:true)
```

- `EventApplyService` 是**唯一**写视图镜像处（R-7.4）：兵力 `SetTroops`、
  状态图标/光环、势能镜像、石化/阵亡、cut-in 请求转发。
- `SkillPerformance.SettleDamage/SettleHeal` 保留为演出原语，但内部
  **不再直接改兵力**，改为"表现 + 委托 ApplyDamage/ApplyHeal"；
  静默路径与演出路径写账代码同源，Skip/重播终态天然一致（R-0.4）。
- 全路径 null-safe（R-7.5）：单位缺失只跳过表现，不断链。

## 四、L5 基础设施与视图约定

- **VFXManager**：池借还唯一入口；出池 `ResetTransform + RestartParticles +
  EnsureVfxSorting`，入池 `DOKill(child) + ps.Clear`（R-6.3）。
- **UnitView**：所有位移/闪烁 tween `SetLink(gameObject)`；对外只暴露表现
  API（HitReact/SetTroops/SetPetrified/…），不读事件类型。
- **服务单例**（VFXManager/Floats/Bubbles/Sfx/Banner/CutIn/Bgm/CameraShaker）：
  经 `VFXContext` 注入给 L4 使用；演出模板禁止直接 `Xxx.Instance`
  （DuelPerformance 已改经 ctx）。均须实现 `CancelAll()` 供硬停止调用。
- **MomentumService / UnitAuraService**：定位为"视图镜像账本"，只被
  EventApplyService 写入；ClearAll 收口到会话建立/销毁两处。

## 五、子机制文档索引

| 机制 | 权威文档 |
|---|---|
| 行为规格总纲（R-条款） | [playback_requirements.md](playback_requirements.md) |
| 演出机制细则/代码位置 | [performance_mechanisms.md](performance_mechanisms.md) |
| 播放单元与 processor 链 | [playback_units.md](playback_units.md) |
| 飘字/台词/横幅/cut-in 文字 | [text_system.md](text_system.md) |
| 渲染层级/机型适配/棋盘布局 | [rendering_layout.md](rendering_layout.md) |
| 战后结算表归因 | [settlement_stats.md](settlement_stats.md) |
| 资源 key 与占位回退 | [assets_upload_guide.md](assets_upload_guide.md) |
| 阵营视觉 | [faction_style.md](faction_style.md) |
| 飘字调参 | [floating_text_tuning.md](floating_text_tuning.md) |
| 手动配阵测试页 | [manual_setup_panel.md](manual_setup_panel.md) |
| 逐文件职责速查 | [client_battle_framework.md](client_battle_framework.md) |
| 事件契约 | `docs/schema/battle_events.md` + payloads + schema.json |

## 六、服务端战报适配点（加法式演进候选，按收益排序）

客户端 6 个 Processor 中 3 个在补服务端未标注的语义，属最复杂/最易与
group_id 语义打架的部分。以下均为**可选字段/新语义的加法演进**
（须走契约演进流程：version.py → md → schema.json → 演进表）：

| 优先 | 候选 | 现由客户端承担 | 建议 |
|---|---|---|---|
| 高 | 响应后置 | ReactionRegroupProcessor 把组内嵌套 status_tick 摘出后置 | 信封加 `playback_phase:"reaction"`，或发射时保证响应 tick 排在主组子事件之后 |
| 高 | 借刀分段 | BorrowBladeSplitProcessor 按直接子伤害切段+重排 | `damage`/`skill_trigger` 加 `segment_no`，或每段独立 group_id |
| 高 | 集体触发 | CollectiveTriggerMergeProcessor + 客户端白名单 | `status_tick` 加 `collective_key`（同 key 客户端直接合并） |
| 中 | 台词独占 | TraitLineExtractProcessor 从混组抽取 | 需阻塞台词统一 `parent_seq=0` 独立组，或加 `presentation:"exclusive_line"` |
| 中 | 模板/分类提示 | Classify 启发式（burst/pursuit/aoe） | 组根加 `template_hint`（hint 域，不参与结算） |
| 中 | 高伤/追伤 cut-in | 客户端阈值 3000 / 计数 | `damage` 可选 `cut_in:true`（服务端统一观感阈值时再做） |
| 低 | 高光窗 | HighlightSelector 全量扫描 | 战报顶加 `highlight_window`（纯优化） |
| 低 | 结算归因 | Aggregator 沿 parent 上溯 + 映射表 | damage/heal 加可选 `attribution`（调试友好） |

落地一条即可删/简化对应 Processor；在此之前客户端加工规则以 R-2.5 为权威。

## 七、遗留债登记（本轮未清，勿当新规范模仿）

- 单 asmdef，层约束靠评审非编译器；长期项：Events 拆独立 asmdef。
- BgmLayerService 仍 DontDestroyOnLoad（跨场景常驻是有意的，但 Teardown
  只 StopBattle 不销毁）。
- PerformanceDatabase 内置默认 ~180 行与 SO 资产并存（R-7.6 目标是 SO 唯一
  权威，本轮保持内置兜底不动）。
- BorrowBlade 判定经 PlaybackWorldBuilder 注入谓词（`Resolver.Resolve(g).BorrowBlade`），
  L2 运行时仍间接依赖 L3。
- UnitView 662 行过载（构建/动画/势能 UI 混杂），后续按"构建器+动画器"再拆。

## 八、扩展点（新机制唯一合法接入面，R-7.7）

1. **纯换演出/资源**：PerformanceDatabase 配置条目 + assets_upload_guide 登记。
2. **新播放序语义**：新增 `IEventProcessor`（在 WorldBuilder 注册，注意链序）。
3. **全新演出形态**：派生 `SkillPerformance` + 新 `PerformanceTemplate` 枚举。
4. **新 cut-in 触发**：只改 `CutInPolicy`。
5. 禁止：在 Director/EventApplyService 里堆技能 id 特判。
