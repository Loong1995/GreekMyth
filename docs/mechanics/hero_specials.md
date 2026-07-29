# 英雄机制特殊处理（hero_specials）

> 汇总「与默认钩子/默认演出不同」的武将口径：性格台词时点、神谕借手触发、
> 客户端专属模板。战法效果正文仍以 [skills/](../skills/index.md) 为准；
> 整体触发序见 [response_order.md](response_order.md)。

## 1. 性格台词时点（与默认「临执行前」可错开）

默认：`emit_trigger` / `emit_status_voice` 在**产生影响的执行节点前**发
`trait_trigger`（parent 指引发事件或自成组）。判定与出手可错开的性格见下表。

### 1.1 赫拉克勒斯 · 鲁莽（`lumang`）

| effect | 判定 | 台词弹出 |
|---|---|---|
| `boost` | `on_round_start` 40% 挂 `lumang_boost` | **本回合首次** `damage_out_bonus`（造成伤害前） |
| `taunt` | `prefer_target` 60% 缩候选并挂 `lumang_taunt` | **该击** `damage_out_bonus`（造成伤害前） |

实现：`battle/traits.py::_Lumang`；旗标 `lumang_boost` / `lumang_taunt` /
`lumang_boost_said`。选人阶段不再说话，避免台词早于出手。

### 1.2 阿喀琉斯 · 傲慢（`aoman`）

| effect | 判定 | 台词弹出 |
|---|---|---|
| `pierce` | 追伤前无条件 25% | 判定成功当下（追伤结算链） |
| `heel` | 受击暴击判定前 20% → 必暴 + 挂 `heel_line_pending` | **暴击伤害事件写出后**（`amount>0`）；`parent_seq=damage_seq`（同组紧随） |

实现：`forced_crit_on_taken` 只判定不弹词；`engine.deal_damage` 在
`damage` 落账后清旗并发 `heel`（挂本条伤害）。未真正打出暴击伤害则吞台词。
**禁止** `parent_seq=0`：另开组会被客户端排到整段出击（含阵亡）之后。

客户端：`TraitLineExtract` 把同组内的 heel 抽成独占 `TraitLine`，夹在
伤害段与后续事件之间（`SayExclusive`，见 [text_system.md §三](../client/text_system.md)）。

## 2. 神谕 / 被动「借手」触发

状态挂在队友身上、由队友出手触发时：

| 规则面 | 口径 |
|---|---|
| 战斗逻辑 | 持有者触发 `on_damage_*`；排序上「他人施加」优先于持有者自身状态 |
| 事件 | `status_tick.source_id` = 施法者（宙斯/雅典娜…）；`status.owner_id` = 持有者 |
| 结算表 | 杀伤/治疗/触发次数归 **施法者** 的**带技能**格子（非出手单位） |
| 播放 Actor | 默认 `StatusTick` → `OwnerId`（持盾反打用持有者）；雷霆用 RemoteStrike 不位移 |

细则与 status→skill 映射：[settlement_stats.md](../client/settlement_stats.md)。

典型：

- **宙斯 · 雷霆神谕**：`thunder` 落雷 → 统计进宙斯「雷霆神谕」；演出
  `RemoteStrike`（目标头顶宙斯头像标先亮，再落雷；头像 sortingOrder>特效，
  施法者不进中心）。
- **雅典娜 · 埃癸斯**：`aegis_shield` 反弹/重击治疗（统率×0.9）→ 雅典娜「埃癸斯圣盾」；
  主将 `aegis_main_command`（统率+30）；反伤≥1500 → `athena_aegis_reflect` 高光。
  反弹演出持盾者 Melee（Cast 闪光后突进）。

## 3. 状态台词（控制 / 犹豫 / 先攻）

已独立成机制文档：[status_voice.md](status_voice.md)（触发点总表/去重/
确定性轮换）；客户端独占播放见
[text_system.md §三](../client/text_system.md)。

## 4. 客户端演出特例（相对组默认）

| skill/status | 模板 / 要点 | 配置 |
|---|---|---|
| `thunder` | `RemoteStrike`，DR 程序化竖雷 | `PerformanceDatabase` |
| `zeus_bolt` | `RemoteStrike` + Vefects Directional | 同上 |
| `hector_warcry` / `hector_assault` | `Auto`（群攻→AoeCenter）；资源走主动物理默认；prepare 无伤害不进中心 | 同上 |
| `aegis_shield` | `Melee` + `cast_oracle`；Actor=`OwnerId` | 同上 |
| `athena_aegis_reflect` | PerSegment + cut-in；Hit=`cast_oracle` | 反伤≥1500 高光 || `perseus_flash` | 单体主动 `Melee`（非弹道） | 同上 |
| `achilles_wrath` | 追伤 `Melee` | 同上 |

总纲：[performance_mechanisms.md](../client/performance_mechanisms.md)。

## 5. 与文档 / 代码对照

| 主题 | 文档 | 代码 |
|---|---|---|
| 触发序 | [response_order.md](response_order.md) | `engine._dispatch_damage_hooks` |
| 性格总表 | [traits.md](traits.md) | `battle/traits.py` |
| 战法效果 | [skills/](../skills/index.md) | `skills_*.py` |
| 结算归因 | [settlement_stats.md](../client/settlement_stats.md) | `BattleSkillStatsAggregator` |
