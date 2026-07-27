# 扩展点与特例登记（extension_points）

> 全项目「通用机制 vs 特殊机制」的唯一账本（2026-07-20 架构重构确立）。
> **加法式演进守则**：新增内容优先走左列注册表（只加一行/一个类，引擎与
> 播放器零改动）；确实进不了注册表的特例必须登记到 §三，并注明代码位置——
> 未登记的硬编码特例视为技术债，review 时拒绝。

## 一、通用机制：后端注册表/钩子（battle/）

新增下列内容时**只改注册点，不改 engine.py**：

| 要加什么 | 注册点 | 方式 |
|---|---|---|
| 战法 | `skills.REGISTRY` | 新类 + `register()`（`skills_<阵营>.py`；标定用 `skills_cal.py`） |
| 性格 | `traits.REGISTRY` | 子类 + `register()`；钩子见 traits.py 基类 |
| 经理人战术 | `tactics.TACTIC_REGISTRY` | `TacticDef` |
| 阵型 | `formations.FORMATION_REGISTRY` + `detect_formation` | 只按站位集合自动识别（`TeamSetup.formation` 只读属性；禁止配将传 formation 字符串）；`FormationDef` 可选受击点数/整场被动；状态中文名同步 names.py / ChineseNames.cs；几何权威 `docs/client/battlefield_layout.md` |
| 持续/响应状态 | `StatusDef` 钩子字段（on_damage_* / on_action_start / on_round_* / on_hero_defeated / on_control_taken / on_ally_basic_attack / on_status_inflicted / on_pre_damage_dealt / mitigation_gate） | 新 StatusDef + handler |
| 状态施加免疫 | `StatusDef.immune_when_forbid`（目标持有该 forbid 键则静默拒绝） | 建 status 时声明（例：石化↔`petrify_immune`） |
| 施加成功回调 | `StatusDef.on_applied_to_other`（对他人施加/刷新后；对自己不回调防递归） | 建 status 时声明（例：美杜莎孤怨照影） |
| 状态台词 | `status_voice.LINES` + `FORBID_ACTIVE_VOICE` / `FORBID_BASIC_VOICE` / `_SKIP_PRIORITY` | 加 3 条词 + 登记候选序 |
| 单挑台词 | `voice_duel_data.DUEL_LINES`（分册抽取）+ `voice_lines.emit_duel_line` | 改 `docs/character/*.md` 后重跑 `_extract_duel_voice.py` |
| 登场台词 | `voice_enter_data.ENTER_LINES` + `voice_lines_enter.emit_enter_dialogues` | 同上抽取；全羁绊单元按序 / 无羁绊主将 generic |
| 势能归轨 | `engine.MOMENTUM_TRACK_OF_KIND` | kind→轨表项 |
| 标准控制 | `statuses.py` builder（复用现有 forbid_* 键则引擎零改动） | 新 builder |

**钩子分发统一原语**（engine 内，重构后唯一实现，勿再手写循环）：
`_collect_global_hooks(hook_name)`（全场收集+全局键定序）、
`_run_hook_entries(entries, invoke)`（逐实例：局分胜负终止/持有者阵亡或
实例被移除跳过）。排序键 `_owner_hook_key` / `_global_hook_key` 语义见
[response_order.md](../mechanics/response_order.md)。

## 二、通用机制：客户端注册表/配置（ClientBattle/）

新增下列内容时**只改注册点，不改 Runner/演出代码**：

| 要加什么 | 注册点 |
|---|---|
| 战法/状态演出（模板/资源 key/强度） | `PerformanceDatabase`（三级：特殊→组默认→全默认） |
| 战场分区/卡尺/旋转/浮空微调 | `Units/BattlefieldLayoutConfig.cs`（静态字段） |
| 裂地档位/触发/关停（三档 T1-T3） | **`VFX/GroundCrackService.cs`**（唯一入口；参数在 `GroundCrackPalette`，演出模板禁止直调） |
| 某战法的裂地强度（缝宽+持续+亮度，1/2/3） | `PerformanceDatabase` 的 `GroundStrengthTier`；规则与登记表见 `docs/client/ground_crack_config.md`（准备型物理群攻＝2，瞬发＝0→1；势能加强强制 3） |
| 某战法的命中裂地面积倍率 | `GroundHitArea`（0→1＝卡宽×1.5）；势能加强强制 1.5；详见 ground_crack_config |
| 想让某表现**跟着弹道飞行进度**走（沿途生长/爬升/蓄光…） | 实现 **`VFX/IFlightDriven`** 并 `StrikeSync.Fly(...).Attach(...)`；不改 StrikeSync 与演出模板。飞行段结束＝弹道抵达，调用方同帧开命中拍 `SettleDamage`。禁止在模板里 `WaitForSeconds` 自拼时序 |
| 状态的客户端表现（光环 key / 控制大图标 / 结算归因战法 / 集体齐发） | **`Names/StatusPresentationRegistry.cs`**（2026-07-20 收口，一状态一行；原 UnitAuraService/StatusIconPanel/StatsAggregator/CollectiveMerge 四处散表已合并） |
| 势能轨样式 | `MomentumService.TrackTable` |
| BGM 分层 stem | `BgmLayerService.StemTable` |
| 飘字观感 | `FloatingTextTuning` SO |
| 中文名 | `Names/ChineseNames.cs`（与 `battle/names.py` 同步红线） |
| 事件流改写（拆组/合并/重排） | `EventPipeline.Register` 新 `IEventProcessor`（红线见 [playback_units.md §二](../client/playback_units.md)） |
| 真实资源 | `Resources/ClientBattle/<类别>/<key>`（占位回退，零代码） |
| 罩身件挂载（定径+跟随定位圆） | `VfxShroudFollower.FitAndFollow`（定径 `VfxShroudFitter.Fit`）；**默认不裁层** |
| 罩身厂包完整拷贝 | `WireShroudEffect.CopyFull`（strip 参数默认 null） |
| 绕身显隐（出现/渐隐） | **`VfxShroudPresence`** + 注册表 `ShroudVisibility`（Always/OddRounds/EvenRounds/Manual）；任意时机 `UnitAuraService.SetShroudVisible`；`HasShroud`＝`IsPresent`（渐隐后恢复受击抖动） |
| ~~某罩身个案显隐~~ | ~~`AresMightShroudPulse`~~ → 已收进通用 Presence（2026-07-26） |

**编排层与执行层边界**：`SkillPerformance` 族不得引用 `PerformanceRunner`
单例；跨层通知走 `VFXContext.OnDamageSettled / OnCutInRequested` 回调
（Runner 建 ctx 时注入）。

## 三、特殊机制登记（进不了注册表的特例，改动前先查此表）

| 特例 | 位置 | 原因 |
|---|---|---|
| 清醒免疫硬控（kind=CONTROL 一律拒绝） | `engine.apply_status` | 按 kind 而非 status_id 的通用规则 |
| 控制减免链（控制格挡/控制反弹） | `engine._check_control_mitigation` | 与伤害减免同构的引擎序 |
| 犹豫/准备/连发/协击编排 | `engine._run_action_window` 等 | 行动窗时序本体 |
| 连携固定取副将 `skills[0]` | `engine._run_assist` | 设计定案（D 系列） |
| 号角走音禁自带战法（`slot_idx==0`） | `engine._run_action_window` | 槽位语义 |
| 踵之弱台词（`heel_line_pending` → 挂暴击 `damage_seq`） | `engine.deal_damage` | 台词紧随暴击伤害；禁止 parent=0（hero_specials §1.2） |
| traits 内 `poseidon`/`hades`/`gender=="f"` 判定 | `battle/traits.py` | 性格语义本身指名道姓 |
| 石化覆盖层视觉（`statusId=="petrify"`） | `SkillPerformance.ApplyStatusVisual` / Runner 落账 | 全项目唯一带专属卡面覆盖层的状态 |
| 协击角标（`Kind=="coordinated"`） | `DefaultPerformance` | 契约字段驱动，非 id 白名单 |
| 圣盾反打 Actor=OwnerId | `DefaultPerformance.ActorOf(StatusTick)` | 事件语义（持有者出手） |
| 单挑整套演出时间轴 | `PerformanceRunner.PlayDuel` | 唯一专属演出，暂不模板化 |
| cut-in 三触发源（满档/高伤3000/追伤第5次） | `PerformanceRunner` | C10 定案的编排层门槛 |

## 四、维护规则

1. 想加特例前先自问：能否表达为 §一/§二 的注册表行或新钩子字段？
2. 确需特例：实现 + 本文 §三 登记一行（位置/原因）+ 相关机制文档同步。
3. 删除/注册表化某特例：从 §三 移除并在 changelog 说明。
4. 双份数据红线：同一份映射禁止在两处维护（本次已消灭：状态表现四处散表、
   engine 内台词元组两份）；发现新的双份即按 P-01 处理。
