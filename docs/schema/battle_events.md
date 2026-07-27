# 战斗事件流契约（battle_events）

> **现行版本：schema 1.4.1 / core battle-0.4.1**（`battle/version.py`；演进史见 §7）。
> 总纲。事件类型逐一明细见 `battle_events_payloads.md`，
> 机器可校验定义见 `battle_events.schema.json`。
> 本契约一经人工确认即**冻结**，此后仅允许加法式演进（新增事件类型 / 新增可选字段），
> 禁止修改或删除已有字段语义。

## 1. 设计原则

1. **事件流即播放协议**：客户端不做任何结算，只按事件流反演播放。事件流必须自洽完备——
   只看事件流即可完整还原整场战斗的全部数值与状态变化。
2. **多粒度播放**是第一设计约束（论证见 `docs/dev/playback_model.md`），通过
   `parent_seq / group_id` 树状归属实现折叠与展开。
3. **逻辑与表现分离**：事件只携带结算事实；可选 `hint` 字段仅供演出参考，客户端演出
   不得反过来影响结算数据。
4. **事件分级**：契约只定义「播放必需事件」（core 级）。引擎内部审计事件（触发判定、
   RNG 记录、timing 心跳等 debug 级）不进入本契约，不出现在发给客户端的战报中，
   仅在服务端全量 dump 时可见（另行文档，不冻结）。

## 2. 顶层数据包 battle_report

一个 `battle_report` = 一个完整系列（1~7 局连战）。

| 字段 | 类型 | pb# | 说明 |
|---|---|---|---|
| `schema_version` | string | 1 | 本契约语义化版本，如 `"1.0.0"` |
| `core_version` | string | 2 | 生成本战报的 battlecore 版本，如 `"battle-0.1.0"` |
| `battle_id` | string | 3 | 系列唯一 id，外部注入 |
| `rng_seed` | uint64 | 4 | 随机种子。`(rng_seed, teams, core_version)` 可一键复现 |
| `setup_metadata` | map | 8 | 影响结算的 setup.metadata（如 trait_rate_overrides；1.3.0 可选，重放必需） |
| `teams` | TeamSnapshot[2] | 5 | 双方阵容与初始属性快照（进入系列前的原始面板） |
| `games` | Game[] | 6 | 各局，按局序号升序，1~7 个 |
| `result` | SeriesResult | 7 | 系列总摘要，列表页无需解析事件即可展示 |

### 2.1 games[] 嵌套 vs 带局号扁平流（选型论证）

选**嵌套 `games[]`**，理由：

- 客户端典型消费单位是「局」：列表页只读 `result`；播放页按局加载、按局 seek；
  平局续战时下一局初始快照挂在 `game_start`，嵌套结构天然对齐。
- 扁平流的唯一优势是流式追加，但战斗为服务端一次性结算、非直播推流，无此需求。
- 事件 `seq` 仍为**系列内全局单调递增**，`t.g` 亦携带局号，两种视角都保留：
  需要扁平视角时 `concat(games[].events)` 即等价扁平流，无信息损失。

### 2.2 TeamSnapshot / HeroSnapshot

| TeamSnapshot 字段 | 类型 | pb# | 说明 |
|---|---|---|---|
| `team_id` | string | 1 | `"A"` / `"B"` |
| `main_hero_id` | string | 2 | 主将（仅主将阵亡判局负） |
| `heroes` | HeroSnapshot[] | 3 | 1~3 名，按站位顺序 |

| HeroSnapshot 字段 | 类型 | pb# | 说明 |
|---|---|---|---|
| `hero_id` | string | 1 | 战斗内唯一 id |
| `template_id` | string | 2 | 英雄模板（立绘/名字由客户端查表） |
| `position` | int32 | 3 | 站位。1.4.0 起 1~6（4~6=后排）；历史战报 0~2（均前排） |
| `force` / `intelligence` / `command` / `speed` | int32 | 4~7 | 四维初始面板（术语见 v0_analysis §四） |
| `max_troops` | int32 | 8 | 兵力上限（支持 >10000 的 NPC） |
| `initial_troops` | int32 | 9 | 系列开局兵力 |
| `skills` | string[] | 10 | 战法 id，**下标即装配顺序**（0=自带，1~2=配置槽） |
| `crit_rate_bps` | int32 | 11 | 基础暴击率 bps（1.3.0 可选；重放还原用） |
| `heal_crit_rate_bps` | int32 | 12 | 基础治疗暴击率 bps（1.3.0 可选） |
| `trait_id` | string | 13 | 性格 id，空=无性格（1.3.0 可选） |
| `gender` | string | 14 | `"m"`/`"f"`（1.3.0 可选；性格判定用） |
| `level` | int32 | 15 | 等级（1.3.0 可选；四维已按等级预算，仅存档） |

### 2.3 Game 与 SeriesResult

| Game 字段 | 类型 | pb# | 说明 |
|---|---|---|---|
| `game_no` | int32 | 1 | 1~7 |
| `events` | Event[] | 2 | 本局全部播放事件，按 `seq` 升序 |
| `result` | GameResult | 3 | `winner_team_id`（null=平局）、`reason`、`end_round`、双方每武将终局兵力三池快照 |

SeriesResult：`winner_team_id`（null=系列平局）、`total_games`、逐局胜负摘要、
双方剩余兵力、关键统计（总伤害/总治疗/击杀数，供列表页）。字段表见 schema.json。

## 3. 事件公共信封（Event envelope）

| 字段 | 类型 | pb# | 必有 | 说明 |
|---|---|---|---|---|
| `seq` | int32 | 1 | 是 | 系列内全局单调递增，从 1 开始，跨局不重置 |
| `t` | LogicalTime | 2 | 是 | 逻辑时间，见 3.1 |
| `type` | string | 3 | 是 | 事件类型（snake_case，见 §4 清单） |
| `parent_seq` | int32 | 4 | 否 | 直接父事件 seq；顶层事件为 0/缺省 |
| `group_id` | int32 | 5 | 否 | 所属播放组根事件的 seq（=祖先链最顶层的动作事件）；顶层事件为自身 seq。冗余字段，客户端可 O(1) 折叠 |
| `payload` | 按 type | 6 | 是 | 各事件类型专属字段，键名固定（可 pb 映射），见 payloads 文档 |
| `hint` | Hint | 7 | 否 | 演出提示（如 `intensity: "ultimate"`），不参与结算，客户端可忽略 |

### 3.1 逻辑时间 LogicalTime `t`

`{ "g": 局号(1起), "r": 回合号(0=准备回合, 1~8=正常回合), "p": 相位, "s": 相位内序号(0起) }`

相位 `p` 枚举（int，pb 友好）。起止拆分为两端，保证字典序不变量成立
（局末/系列末事件必须排在该局全部回合事件之后）：

| 值 | 名称 | 覆盖内容 |
|---|---|---|
| 0 | `SERIES_START` | battle_start |
| 1 | `GAME_START` | game_start |
| 2 | `DUEL` | 单挑全过程（仅第 1 局开局、所有战法前） |
| 3 | `ROUND_START` | 回合开始结算（伤兵损耗、DoT tick、回合型触发） |
| 4 | `ACTION` | 单个武将的行动窗口（含其引发的全部连锁） |
| 5 | `ROUND_END` | 回合结束结算 |
| 6 | `GAME_END` | game_end |
| 7 | `SERIES_END` | battle_end |

排序不变量：`(g, r, p, s)` 字典序与 `seq` 序完全一致；同一 ACTION 相位内 `s` 相同的
事件属于同一行动窗口，靠 `seq` 定序。客户端 seek 到「第 g 局第 r 回合」= 二分定位
第一个 `t.g==g && t.r==r` 的事件。

### 3.2 播放分组规则（parent_seq / group_id）

- **组根**（`parent_seq=0`）只能是：`normal_attack`、`skill_trigger`、`duel_challenge`、
  `round_start`、`game_start` 等动作/节点事件。
- 一次战法释放产生的全部子效果（多段伤害、治疗、状态施加、连锁引发的追击……）
  `parent_seq` 指向引发者，形成树；`group_id` 恒等于树根 seq。
- **连锁跨组规则**：普攻命中触发的追击是**新组**（追击的 `skill_trigger` 为组根，
  其 `parent_seq` 指向引发它的 `damage` 事件 seq，`group_id` 指向自身）。即
  `parent_seq` 表达因果链、`group_id` 表达播放折叠单元——客户端「战法级播放」
  按 group 折叠，因果动画（追击镜头衔接）按 parent 链。
- 派生事件（`hero_defeated`、受击率无需事件化、`troops_change`）挂到引发它的
  结算事件之下。

## 4. 事件类型清单（core 级，共 24 个）

字段表 / 触发语义 / JSON 实例逐一见 `battle_events_payloads.md`。

| 类别 | type | 语义一句话 |
|---|---|---|
| 节点 | `battle_start` | 系列开始（快照在顶层 teams，不重复携带） |
| 节点 | `game_start` | 每局开始，含本局初始兵力三池快照（承接上局残血） |
| 节点 | `round_start` | 回合开始（r=0 为准备回合） |
| 节点 | `action_start` | 某武将行动窗口开始（含行动顺位） |
| 动作 | `normal_attack` | 普攻宣告（连击时多个，`strike_no` 区分） |
| 动作 | `skill_trigger` | 战法触发宣告，`kind` 区分 cast/prepare/release/interrupted/delayed/assist（连携） |
| 结算 | `damage` | 一次伤害落地（含类型/暴击/兵力三池变化） |
| 结算 | `heal` | 一次治疗落地（伤兵转兵力，含暴击） |
| 结算 | `status_apply` | 状态施加（含层数/持续） |
| 结算 | `status_refresh` | 状态刷新/叠层 |
| 结算 | `status_tick` | 状态周期结算（DoT/HoT 的 tick 本身；掉血挂子 damage 事件） |
| 结算 | `status_remove` | 状态移除（到期/驱散/来源阵亡清理，`reason` 区分） |
| 结算 | `attr_change` | 属性修改（如单挑败者四维-10），临时/整场/整系列 |
| 结算 | `troops_change` | 非伤害治疗途径的兵力池变化（伤兵自然损耗等） |
| 节点 | `hero_defeated` | 武将兵力归零退出战斗 |
| 单挑 | `duel_challenge` | 单挑叫阵（高武向低武） |
| 单挑 | `duel_result` | 拒绝或接受后的胜负（含四维惩罚由子 attr_change 表达） |
| 节点 | `round_end` | 回合结束 |
| 节点 | `game_end` | 单局结束（胜/负/平 + 终局快照） |
| 节点 | `battle_end` | 系列结束（引用顶层 result） |
| 节点 | `phase_start` | 相位开始。**仅在相位含事件时发**，空相位不发（省体积；论证见 payloads §22） |
| 结算 | `trait_trigger` | 性格触发（1.2.0 新增）：`{hero_id, trait_id, effect, line}`，`line` 为预设台词，客户端弹聊天框播出；纯数值静默修正不发 |
| 结算 | `momentum_change` | 四轨势能变化（1.4.0 新增，纯表现记账）：`{hero_id, track, delta, value, reason, cut_in?}`；`track`=`active`/`passive`/`oracle`/`basic_pursuit`（按轨类型跨技能累计）；`value≥5` 当次起同轨带 `cut_in=true`（客户端播切入；4 分闪光仅客户端）。每回合 `round_start` 全体四轨静默清零（不发事件；计数单元＝回合） |
| 节点 | `tactic_applied` | 经理人战术变更生效（1.4.1 新增，round_start 组下）：`{team_id, tactic_id, round_no, change_no, params?}`；客户端播报横幅+更新左侧战术栏。预设战术不发事件（setup_metadata 可查）。机制见 `docs/mechanics/manager_tactics.md` |

对任务书 3.2 最小集的增删论证：`kind=assist/delayed/interrupted` 并入 `skill_trigger`
而非独立类型（同一播放语义族）；新增 `phase_start` 按需发送。未增其他类型。

## 5. Protobuf 映射约定（本阶段不实现，仅约束）

- 每个事件类型的 payload 是**固定键名**的独立 message；本文档与 payloads 文档中的
  `pb#` 列即未来 proto 字段编号，**一经冻结不得复用或改号**；每个 message 预留
  编号段（envelope 预留 8~15，payload 各预留至 31）供加法演进。
- 禁止动态键名（如以 hero_id 作键的 map 快照一律改为带 `hero_id` 字段的数组元素）。
- `type` 在 JSON 用字符串（可读、可 diff）；映射 pb 时对应 `oneof payload` 的分支，
  类型-编号对照表随 schema.json 附带（`x-pb-type-ids`）。
- 枚举一律同时给出字符串形态（JSON）与整数值（pb），映射表冻结在 schema.json 的
  `x-pb-enums`。
- 所有数值为整数：概率/系数用 bps（万分比），兵力/属性为原生整数。无浮点。

## 6. 体积估算与截断策略

估算基准：JSON 紧凑编码（无缩进），平均单事件 ≈ 180 字节（信封 ~70 + payload ~110）。

| 场景 | 事件数估算 | JSON 体积 | gzip 后 |
|---|---|---|---|
| 单局（3v3，8 回合打满） | 节点事件 ~60 + 6 武将×8 回合×(行动 1 组 ≈ 4~8 事件) ≈ 350~500 | 65~90 KB | 8~15 KB |
| 单局（连锁密集阵容上限） | ~1200 | ~220 KB | ~30 KB |
| 7 局满打系列 | ~3500~8000 | 0.6~1.5 MB | 80~200 KB |

截断策略：**不做静默截断**。回合上限（8 回合/局、7 局/系列）已天然封顶体积；
另设每局事件数硬上限（默认 20000，配置项）作为无限连锁 bug 的保险丝——触发即判定
core 内部错误：战斗失败、不产出战报、抛出含完整上下文的异常（遵守任务书 6.3），
绝不输出半截事件流。

## 7. 版本演进规则

- `schema_version` 语义化：新增可选字段升 minor；新增事件类型升 minor；任何不兼容
  变更禁止（若确需，开新 major 并保留旧解析器，需人工决策）。
- 客户端遇到未知 `type` 或未知字段：**必须跳过并继续播放**（向前兼容义务）。
- 战报由 `core_version` 追溯生成器；同一 schema_version 下不同 core_version 的战报
  对客户端完全等价。

### 已发生的加法式演进

| schema_version | 变更 |
|---|---|
| 1.0.0 | 冻结基线（Step A 确认） |
| 1.1.0 | `normal_attack` / `skill_trigger` / `damage` payload 新增可选字段 `target_select`（受击率选人记录，`battle_events_payloads.md` §23；机制见 `docs/mechanics/targeting.md`） |
| 1.2.0 | Phase 3：`damage` payload 新增可选字段 `mitigation`（`"block"`/`"evade"`，0 结算格挡/闪避）与 `damage_class`（`"special"` 标震荡等不触发响应的伤害）；新增事件类型 `trait_trigger`（性格触发台词，payloads §24） |
| 1.3.0 | 客服重放闭环：HeroSnapshot 新增可选字段 `crit_rate_bps` / `heal_crit_rate_bps` / `trait_id` / `gender` / `level`（pb# 11~15）；顶层新增可选字段 `setup_metadata`（影响结算的 setup.metadata，如 trait_rate_overrides）。战报 JSON 自身即可无损还原 BattleSetup（工具 `battle/tools/replay_report.py`） |
| 1.3.1 | `damage.mitigation` 枚举新增 `"reflect"`（圣盾反弹：受伤归零，随后 status_tick + 子 damage(special) 反弹给攻击者），payloads §7 |
| 1.4.0（2026-07-20 冻结，core battle-0.4.0） | 新增事件类型 `momentum_change`（四轨势能，**默认开启**，`setup.metadata.enable_momentum=false` 可关）；`skill_trigger` 新增可选字段 `burst_no`（连发第 N 次释放，2 起，硬上限 7）；`normal_attack` 新增可选字段 `kind`（`"coordinated"`=协击，缺省=普攻）；HeroSnapshot.position 扩展 1~6（4~6=后排）。机制见 `docs/mechanics/momentum.md`、`burst_coordination.md`。golden 已全量重生成 |
| 1.4.1（2026-07-20，core battle-0.4.1，P4-C） | 新增事件类型 `tactic_applied`（经理人战术变更生效，payloads §26）；`status_remove.reason` 枚举补登 `"exhausted"`（充能耗尽摘除，行为 A2 起已存在）；`setup_metadata.tactics` 承载预设/变更序列（重放闭环）。机制见 `docs/mechanics/manager_tactics.md` |
