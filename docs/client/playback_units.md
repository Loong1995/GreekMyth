# 播放单元机制（playback_units）

> 客户端播放的核心抽象：**一场战斗 = 一串播放单元（EventGroup）依次独占时间轴**。
> 本文讲清「一回合在屏幕上如何展开」与背后的分组/拆分/阻塞代码机制。
> 上层总目录：[performance_mechanisms.md](performance_mechanisms.md)。

## 一、自然语言叙述：一个行动窗怎么播

以「赫克托尔行动：先说犹豫台词，随后战法被延迟」与「阿喀琉斯出手、
赫拉克勒斯十二试炼反打（其中一段被格挡）」为例，屏幕上按顺序发生：

1. **行动开始**（action_start 节点）：追伤计数复位；
   若全禁（skipped）头顶飘「无法行动」。节点不占时间轴。
   （势能改按**回合**清零，见 round_start。）
2. **台词单元**（TraitLine）：卡牌右上弹出聊天气泡，整条时间轴**只等气泡**
   弹出→停留→收起（约 1.14s × DurationMul）；播完立刻接下一单元，
   前后**不加**单元停顿——「无缝、不重叠、独占」。
3. **行动单元**（主动/普攻/追击/状态触发）：按演出模板播（近战突进/中心齐射/
   落雷…），组内每个等待都对应可见动画（零死帧）。**格挡/反弹（amount=0）
   同样完整播出击动画**，只是受击顿挫减弱、飘「格挡!/反弹!」。
4. 行动单元结束加 `GroupPauseSeconds`（0.35s）；同一武将行动窗内的所有单元
   播完、下一个 action_start 到来前，再加 `ActionPauseSeconds`（0.55s）。
5. **响应单元**：雷霆落雷、圣盾反弹、试炼反打等状态触发，永远排在
   引发它的主单元**之后**作为独立单元播出；顺序与事件流一致
   （引擎先守后攻，见 [response_order.md](../mechanics/response_order.md)）。
6. **记账节点**（状态增删/属性/势能/阵亡等非行动组）：即时落账不占时间轴。

## 二、播放单元从哪来：分组与管线

数据流（详见 [client_battle_framework.md](client_battle_framework.md)）：

```
事件流 → 按 group_id 全量聚合成 EventGroup → processor 链改写 → 逐组播放
```

processor 链（`PlaybackCompiler.BuildPipeline` 登记顺序即执行顺序，
2026-07-27 起编译期一次跑完，见 [playback_script.md](playback_script.md)）：

| # | Processor | 作用 |
|---|---|---|
| 0 | `BorrowBladeSplitProcessor` | 借刀战法（代战/披甲，profile.BorrowBlade）按「组根直接子伤害」切段，每段自成播放单元并按首事件 seq 回插事件流原生位置——段1(借手突进)→响应→追伤→段2…；不拆会三刀连劈再补账（2026-07-22） |
| 1 | `ReactionRegroupProcessor` | 把主组内的 `status_tick` 子链（按 parent_seq 闭包）摘成独立 StatusTrigger 组，追加在主单元之后（「响应后播」） |
| 2 | `CollectiveTriggerMergeProcessor` | 相邻同状态同来源 StatusTrigger 组合并为一次集体齐发（白名单：`thunder`）；圣盾等保持逐次 |
| 3 | `TraitLineExtractProcessor` | 把混在行动/状态组里的 `trait_trigger` 抽成独立 TraitLine；**出击段保留原组 Root**；**跳过 Duel 组**（单挑台词由 `DuelPerformance` 按时点播） |
| 4 | `AchillesPierceTagProcessor` | 前邻有傲慢 pierce 台词组时给 `achilles_wrath` 追伤组打 `PierceBoost` 标，供裂甲 ExtraIcon 仅在贯穿成功时播；须在 TraitLineExtract 之后 |
| 5 | `NodeMergeProcessor` | 纯记账节点标 `ParallelWithNext`，静默落账不占节拍 |

### 分组三条红线

1. **全量聚合**：group_id 用字典聚合非连续段；连续段合并会把群攻切碎（P-03）。
2. **拆组保 Root**：processor 拆出的战斗段必须以原 skill_trigger/status_tick
   为 Root，否则 `VFXResolver.KeyOf` 查不到专属配置（P-17）。
3. **子事件全落账**：任何组的全部子事件必须经 `EventApplyService.Apply` 落账（P-04）。

## 三、时间轴阻塞规则（谁能占时间、占多久）

| GroupKind | 占用时间轴 | 时长来源 | 单元后停顿 |
|---|---|---|---|
| ActiveSkill / NormalAttack / Pursuit / StatusTrigger / Duel | 是 | 演出协程内可见动画时长 | `GroupPauseSeconds`（0.35s×DurationMul） |
| **TraitLine（台词）** | 是（独占） | `SayExclusive` 返回值（动画与等待同一套 ×DurationMul/Speed；基准≈1.14s） | **无**；且前一行动单元若紧跟台词也**跳过**它的单元停顿；泡收起后立刻下一组 |
| Node（回合/行动开始） | 否 | — | 行动切换时 `ActionPauseSeconds` |
| StatusChange / Defeat / 其它 | 否（即时落账） | — | 无 |

代码：`PerformanceRunner.PlayLoop`（协程宿主）→ `PlaybackDirector.PlaySeries` →
`PlayGroupsRange`（主循环，含 nextTrait 判断与回合边界势能火渐灭；高光回放共用
同一循环）、`PlayGroup`（按 Kind 分派）、`Wait(seconds)`（统一乘 DurationMul/Speed）。

### 台词独占三原则（2026-07-20 定，P-18/P-19）

1. **必须自成组**：引擎发台词一律 `parent_seq=0`（性格台词与状态台词同）；
   挂在 action_start 下会被 Node 静默吞掉。
2. **阻塞时长由表现服务给出**：Runner 等 `SayExclusive` 的返回值（已含
   DurationMul/Speed），用 `WaitForSeconds` 原样等，**禁止再经 `Wait()` 二次相乘**。
3. **无缝**：台词组前后不加 GroupPause；`TraitLinePauseSeconds` 字段已废弃。
   满档 cut-in（`PlaySoloBlocking`）同理：协程播完立刻出手，无额外垫秒。

## 四、演出模板族（行动单元内部怎么演）

| 模板 | 何时 | 时间轴 |
|---|---|---|
| Melee | 普攻/单体追击/反打类（试炼/狮皮/圣盾反弹/镜盾闪击） | 逐段：突进(0.22s)→命中帧斩击+结算→回身(0.24s，休息点重采样)；**每段都突进，与格挡/反弹无关** |
| AoeCenter | 主动且互异目标≥2 | 移中心(0.3s)→N 道弹道齐射(基准 0.38s)→同帧结算→回身（休息点重采样）（`DefaultPerformance.PlayAoeCenter`） |
| PerSegment | 单体主动/多段 | 每段：弹道(0.30s)→结算（`PlayPerSegment`） |
| RemoteStrike | 雷霆 | 施法者不动；目标卡顶头像标先亮(0.06s)→竖长闪电贯穿牌面(0.35s，Y 跨约 ±1.35)→结算（`PlayRemoteStrike`） |
| OracleAura | 神谕/被动宣告 | 前摇特效→全部状态同帧落账挂光环→可选整盘滤镜；协程内不额外 yield（仍占行动单元槽位，几乎瞬过） |
| None | 明确无演出 | 只落账 |

结算原语 `SkillPerformance.SettleDamage`：命中特效（有 HitKey 时格挡也播）、
受击顿挫（减免时轻顿挫不震屏）、飘字（技能名+数值/减免文案）、
`troops_after` 权威刷血、cut-in 高伤门槛回调。

## 五、维护清单

- 新增 processor：想清楚它对 Root/Kind 的影响，违反红线必现「动画丢失/重叠」。
- 新增需要占时间轴的表现（如新台词类）：事件侧 `parent_seq=0` 自成组 +
  客户端在 `PlaybackDirector.PlayGroup` 加分支 + 时长由表现服务返回。
- 调节奏：只动 `DurationMul / ActionPauseSeconds / GroupPauseSeconds /
  ChatBubbleService.HoldSeconds`，不许在演出协程里垫裸等待（零死帧）。
