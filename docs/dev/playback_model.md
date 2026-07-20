# 播放模型设计文档（playback_model）

> 【历史文档/设计存档】契约设计期论证，概念仍成立；**现行播放实现权威**
> 为 `docs/client/playback_units.md` 与 `client_battle_framework.md`。
> Step A 产出 3/4。说明事件流契约（`docs/schema/battle_events.md`）如何支撑客户端
> 多粒度反演播放。客户端不做任何结算，仅按事件流驱动演出与 UI 状态。

## 1. 播放需求全集（论证）

任务书明确两种粒度，草案论证后共确认 **6 种播放需求**，全部由同一事件流支撑，
无需服务端出多版本数据：

| # | 需求 | 触发场景 | 事件流支撑机制 |
|---|---|---|---|
| 1 | 战法级播放（折叠） | 默认观战节奏 | 按 `group_id` 折叠：一组 = 一个演出单元 |
| 2 | 逐效果播放（展开） | 玩家开「详细模式」/慢速回味 | 组内按 `seq` 逐事件播放，`parent_seq` 提供因果衔接 |
| 3 | 跳过至结果 | 列表页 / 不想看 | 顶层 `result` 自带最终胜负与统计，**零事件解析** |
| 4 | 逐回合快进 / seek | 拖进度条、复盘某回合 | `t=(g,r,p,s)` 字典序=seq 序，二分定位；快照重建见 §4 |
| 5 | 单挑独立演出 | 单挑是核心爽点，需专属运镜 | 单挑独占 `DUEL` 相位（p=2），天然是可整段特殊处理的事件区间 |
| 6 | 逐局播放 / 局间结算页 | 系列连战 1~7 局 | `games[]` 嵌套 + `game_start` 携带残血快照，局为独立播放单元 |

结论：不需要额外事件类型或并行数据结构；`seq + t + parent_seq/group_id + 自洽 payload`
四件套覆盖全部需求。

## 2. 客户端播放器模型（参考实现约定，不写代码）

播放器 = 「本地影子状态 + 事件游标」：

- **影子状态**：每武将的兵力三池、属性、状态列表。由顶层 `teams` 初始化；每消费一个
  事件按 payload 直接赋值（事件带 before/after，无需计算，还能自校验——before 与影子
  不符即数据异常）。
- **事件游标**：按 `seq` 前进。粒度只影响「演出调度」，不影响状态更新——状态永远
  逐事件应用，折叠只是把一组事件的演出合并成一次表演。
- **hint 消费**：`hint.intensity` 等仅选镜头/特效强度，禁止改数值。未知 type/字段
  一律跳过（契约 §7 向前兼容义务）。

## 3. 典型战法示例（三个）

### 3.1 普攻 + 追击连锁（两组，因果衔接）

阿波罗(A1)普攻雅典娜(B1)，命中后自身「猎月」追击触发，再补一段伤害：

```json
[
 {"seq":60,"t":{"g":1,"r":2,"p":4,"s":1},"type":"action_start","parent_seq":0,"group_id":60,
  "payload":{"actor_id":"A1","order_no":2}},
 {"seq":61,"t":{"g":1,"r":2,"p":4,"s":1},"type":"normal_attack","parent_seq":0,"group_id":61,
  "payload":{"actor_id":"A1","target_ids":["B1"],"strike_no":1}},
 {"seq":62,"t":{"g":1,"r":2,"p":4,"s":1},"type":"damage","parent_seq":61,"group_id":61,
  "payload":{"source_id":"A1","target_id":"B1","damage_type":"physical","amount":655,"is_crit":false,
   "troops":{"hero_id":"B1","troops_before":9000,"troops_after":8345,
    "wounded_before":700,"wounded_after":1159,"dead_before":300,"dead_after":496}}},
 {"seq":63,"t":{"g":1,"r":2,"p":4,"s":1},"type":"skill_trigger","parent_seq":62,"group_id":63,
  "payload":{"actor_id":"A1","skill_id":"moon_hunt_pursuit","kind":"cast","target_ids":["B1"]}},
 {"seq":64,"t":{"g":1,"r":2,"p":4,"s":1},"type":"damage","parent_seq":63,"group_id":63,
  "payload":{"source_id":"A1","target_id":"B1","damage_type":"physical","amount":328,"is_crit":true,
   "troops":{"hero_id":"B1","troops_before":8345,"troops_after":8017,
    "wounded_before":1159,"wounded_after":1389,"dead_before":496,"dead_after":594}}}
]
```

- **战法级**：两个演出单元（组 61「普攻」、组 63「追击」），各播一次总伤害飘字；
  追击组根的 `parent_seq=62` 指向普攻伤害 → 播放器直接衔接镜头，无停顿。
- **逐效果级**：61→62→63→64 逐条演出，62 与 64 分别飘字，64 播暴击特效（`is_crit`）。

### 3.2 神谕 + 状态连锁：雷霆神谕（严格对应 `skill_files.py` 标杆语义）

雷霆神谕是**神谕类战法**（非准备型）：准备回合 PREPARE 阶段必发一次，给己方全体
施加【雷霆】状态；此后携带者每次造成非落雷伤害结算后，70% 概率对该受击目标追加
一次谋略【落雷】（每英雄每回合上限 3 次，落雷不再触发雷霆）。

准备回合（r=0），宙斯(A2)发动神谕：

```json
[
 {"seq":20,"t":{"g":1,"r":0,"p":4,"s":0},"type":"skill_trigger","parent_seq":0,"group_id":20,
  "payload":{"actor_id":"A2","skill_id":"thunder_oracle","kind":"cast",
   "target_ids":["A1","A2","A3"]},"hint":{"intensity":"strong"}},
 {"seq":21,"t":{"g":1,"r":0,"p":4,"s":0},"type":"status_apply","parent_seq":20,"group_id":20,
  "payload":{"status":{"instance_id":201,"status_id":"thunder","owner_id":"A1"},
   "source_id":"A2","stacks":1,"duration_rounds":-1}},
 {"seq":22,"t":{"g":1,"r":0,"p":4,"s":0},"type":"status_apply","parent_seq":20,"group_id":20,
  "payload":{"status":{"instance_id":202,"status_id":"thunder","owner_id":"A2"},
   "source_id":"A2","stacks":1,"duration_rounds":-1}},
 {"seq":23,"t":{"g":1,"r":0,"p":4,"s":0},"type":"status_apply","parent_seq":20,"group_id":20,
  "payload":{"status":{"instance_id":203,"status_id":"thunder","owner_id":"A3"},
   "source_id":"A2","stacks":1,"duration_rounds":-1}}
]
```

第 2 回合，A1 普攻命中后【雷霆】70% 判中，追加落雷（状态触发用 `status_tick` 作组根）：

```json
[
 {"seq":80,"t":{"g":1,"r":2,"p":4,"s":0},"type":"normal_attack","parent_seq":0,"group_id":80,
  "payload":{"actor_id":"A1","target_ids":["B1"],"strike_no":1}},
 {"seq":81,"t":{"g":1,"r":2,"p":4,"s":0},"type":"damage","parent_seq":80,"group_id":80,
  "payload":{"source_id":"A1","target_id":"B1","damage_type":"physical","amount":655,"is_crit":false,
   "troops":{"hero_id":"B1","troops_before":9000,"troops_after":8345,
    "wounded_before":700,"wounded_after":1159,"dead_before":300,"dead_after":496}}},
 {"seq":82,"t":{"g":1,"r":2,"p":4,"s":0},"type":"status_tick","parent_seq":81,"group_id":82,
  "payload":{"status":{"instance_id":201,"status_id":"thunder","owner_id":"A1"},
   "source_id":"A1"},"hint":{"intensity":"strong"}},
 {"seq":83,"t":{"g":1,"r":2,"p":4,"s":0},"type":"damage","parent_seq":82,"group_id":82,
  "payload":{"source_id":"A1","target_id":"B1","damage_type":"magic","amount":540,"is_crit":false,
   "troops":{"hero_id":"B1","troops_before":8345,"troops_after":7805,
    "wounded_before":1159,"wounded_after":1537,"dead_before":496,"dead_after":658}}}
]
```

- **战法级**：准备回合组 20 播一次「雷云笼罩全军」演出（三个图标同时出现）；
  第 2 回合普攻组 80 与落雷组 82 是两个演出单元，落雷组根 `parent_seq=81` 指回
  普攻伤害，镜头无缝衔接天雷特效。
- **逐效果级**：21/22/23 逐个上图标；81 普攻掉血 → 82 雷霆触发宣告 → 83 落雷掉血。
- 落雷判中与否、每回合 3 次上限均由服务端结算：未触发就没有 82/83 事件，客户端
  无需理解概率规则。
- 准备型（prepare → release 两段）主动战法另见旧 core 标定技能
  `basic_test_damage_skills.py`（`kind=prepare/release` 的用法同 §6 kind 表），
  其被打断时只出现 `skill_trigger(kind=interrupted)`，release 组永不出现。

### 3.3 单挑（DUEL 相位整段独立演出）

单挑仅第 1 局判定一次（决策 D-03）。第 1 局准备回合最前，A1(武97) 向 B1(武93)
叫阵，B1 接受并落败，四维-10（仅第 1 局有效，scope=game）：

```json
[
 {"seq":10,"t":{"g":1,"r":0,"p":2,"s":0},"type":"duel_challenge","parent_seq":0,"group_id":10,
  "payload":{"challenger_id":"A1","defender_id":"B1","challenger_force":97,"defender_force":93}},
 {"seq":11,"t":{"g":1,"r":0,"p":2,"s":0},"type":"duel_result","parent_seq":10,"group_id":10,
  "payload":{"accepted":true,"winner_id":"A1","loser_id":"B1"}},
 {"seq":12,"t":{"g":1,"r":0,"p":2,"s":0},"type":"attr_change","parent_seq":11,"group_id":10,
  "payload":{"hero_id":"B1","scope":"game","changes":[
   {"attr":"force","before":93,"after":83},{"attr":"intelligence","before":70,"after":60},
   {"attr":"command","before":80,"after":70},{"attr":"speed","before":88,"after":78}]}}
]
```

- 播放器检测到 `t.p==2` 区间 → 切入单挑专属场景（运镜、擂台演出），整组播完再回
  常规战场。拒绝叫阵时只有 `duel_result{accepted:false}`，播「摇头拒绝」小演出。
- 四维-10 由 attr_change 落到影子状态，后续所有伤害数值已由服务端按新属性结算，
  客户端零计算。

## 4. seek / 快进的状态重建

事件带 before/after 的设计使 seek 成本极低：

1. 定位：二分找到目标 `(g,r)` 的第一个事件 seq。
2. 重建：从 `game_start` 快照起**快速空放**（只应用状态、不演出）到该 seq。单局
   ≤1200 事件、每事件为纯赋值，重建耗时可忽略（毫秒级）。
3. 快进 = 演出层加速或跳过，状态层照常逐事件应用，保证任意时刻血条/图标正确。
4. 跨局 seek 更快：直接从目标局的 `game_start` 起步，无需回放之前的局。

## 5. 对 battlecore 底层机制的反向约束（重构第一目的的落点）

事件流以上述模型为准，反推新 core 必须满足：

1. **结算即发事件**：任何兵力/属性/状态变化在发生点同步产出带 before/after 的事件，
   禁止事后汇总补发（否则 before/after 链断裂）。
2. **组根先行**：执行战法/普攻前先发组根事件占位拿到 seq，子效果逐个挂接——引擎的
   执行栈就是事件树，`group_id` 由派发器自动维护，战法代码不感知。
3. **相位状态机显式化**：引擎主循环按 `t` 的六个相位推进，任何结算都发生在明确
   相位内（旧 core 的 timing 概念保留为内部触发机制，但对外时间轴只有 `t`）。
4. **审计与播放分流**：内部触发判定、RNG 记录等不进入 core 级事件流，走独立
   debug 通道（replay_dump 全量档可见）。
5. **失败即中止**：事件数超上限或结算异常 → 抛异常不出报（契约 §6）。
