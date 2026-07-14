# State 响应机制参考

> **维护约定**：与 `battlecore/domain/skill.py`（`State`）、`battlecore/config/chain_reaction_config.py`（顺序配置）、`battlecore/engine/chain_reaction.py`（排序）、`battlecore/engine/battle_context.py`（索引 / `run_timing` / `dispatch_events`）保持同步。
>
> 事件语义见 [EVENT_SIGNAL_REFERENCE.md](./EVENT_SIGNAL_REFERENCE.md)；设计总纲见 [DESIGN_V2.md](./DESIGN_V2.md)。

---

## 一、State 是什么

`State` 是挂在武将身上的**运行时状态实例**，继承 `Triggerable`，与 `Skill` 共用触发检查、概率、次数上限等框架，但语义不同：

| 对比 | Skill | State |
|------|-------|-------|
| 来源 | 武将配置的技能槽 | Effect 施加、准备型战法、运行时累加 |
| 典型行为 | 顺序执行 Effect | 改 payload、监听事件、提供数值乘区 |
| 复杂流程 | 适合多段 Effect | 应把复杂逻辑拆成 Effect + 简单 State |

**设计原则：**

- 数值修正 → `StateType.ATTR` / `DAMAGE_REDUCE` + `trigger_mode=NONE`（被动读取）
- 按 Timing 主动刷新 → `trigger_mode=REGULAR`
- 按 Event 被动响应 → `trigger_mode=SPY`
- 多个 State 同时满足条件时，**顺序由配置文件决定**，与 `rebuild_indexes` 注册先后无关

---

## 二、分类体系

### 2.1 `StateType`（状态语义）

| 类型 | 用途 | 是否进触发索引 | demo 示例 |
|------|------|----------------|-----------|
| `ATTR` | 四维 / 增伤 / 易伤 / 治疗修正等属性 payload | `NONE` 时不进；`REGULAR`/`SPY` 可进 | 【神示】【被汲取统率】【献祭武力】 |
| `DAMAGE_REDUCE` | 减伤乘区 `damage_reduce_bps` | 同上 | 【幽影蔽体】 |
| `CONTROL` | `forbid_basic` / `forbid_active` 等控制 | 通常 `NONE` | 【冥锁】 |
| `SPECIAL` | 监听型特殊逻辑（治疗、落雷、献祭等） | 通常 `SPY` 或 `REGULAR` | 【蛇杖庇护】【雷霆】【冥祭献统】 |
| `BUFF` / `DEBUFF` / `DOT` / `HOT` | 预留扩展 | — | 暂无 demo |

### 2.2 `TriggerMode`（如何被引擎调度）

| 模式 | 含义 | 索引 | 触发入口 |
|------|------|------|----------|
| `NONE` | 不主动触发；仅被数值模型或其它流程读取 | 不进 timing / event 索引（`ATTR`/`DAMAGE_REDUCE` 默认） | — |
| `REGULAR` | 在指定 **Timing** 主动执行 `state.execute()` | `regular_state_timing_index` | `run_timing()` |
| `SPY` | 监听 **EventType**，事件派发时响应 | `spy_state_event_index` | `dispatch_events()` |

**注意：** `TriggerMode` 与 `Timing.ACTIVE`（主动战法时间片）、`SkillCategory.ACTIVE` 是不同概念。

### 2.3 推荐组合（配置决策）

```text
需要改四维 / 增伤 / 易伤？
  └─ state_type=ATTR, trigger_mode=NONE
     （整场被动；也可 REGULAR/SPY 动态改 payload）

需要减伤乘区？
  └─ state_type=DAMAGE_REDUCE
     静态：trigger_mode=NONE
     每行动前刷新：trigger_mode=REGULAR, trigger_timings=[BEFORE_ACTION]

需要禁止普攻 / 主动战法？
  └─ state_type=CONTROL, trigger_mode=NONE
     payload: forbid_basic / forbid_active

受伤后治疗 / 造成伤害后追加效果？
  └─ state_type=SPECIAL, trigger_mode=SPY
     listen_event_types=[DAMAGE_SETTLED]（或 HEAL_SETTLED 等）

行动前刷新数值 / 献祭友军？
  └─ state_type=SPECIAL, trigger_mode=REGULAR
     trigger_timings=[BEFORE_ACTION]
```

---

## 三、`StateConfig` 字段

定义于 `battlecore/config/schema.py`：

| 字段 | 说明 |
|------|------|
| `state_config_id` | 配置 id |
| `name` | 显示名 |
| `state_type` | 见 §2.1 |
| `trigger_mode` | 见 §2.2 |
| `trigger_timings` | `REGULAR` 必填；`SPY` 可留空（由事件驱动） |
| `listen_event_types` | `SPY` 必填；监听的事件类型列表 |
| `duration_rounds` | 持续回合（见 §五） |
| `max_stack` | 最大叠层 |
| `dispellable` / `purifiable` | 驱散 / 净化（预留） |
| `tags` | 逻辑标签；**顺序配置按 tag 匹配** |
| `payload` | 运行时参数：`probability_bps`、数值系数、控制标志等 |

运行时 `State` 额外字段：`responded_event_ids`（SPY 防同事件重复响应）、`source_actor_id` / `source_skill_id`（来源追踪）。

---

## 四、响应机制总览

BattleCore 有两条独立的 State 响应通路，各自有**分组顺序配置**：

```text
┌─────────────────────────────────────────────────────────────┐
│  REGULAR（Timing 驱动）                                      │
│  run_timing(timing, actor)                                   │
│    → 收集 regular_state_timing_index[timing]                 │
│    → sort_regular_states_for_dispatch(RegularGroupConfig)      │
│    → try_trigger_triggerable(state, timing)                    │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  SPY（Event 驱动）                                           │
│  dispatch_events()                                           │
│    → 收集 spy_state_event_index[event_type]                  │
│    → should_trigger_by_event 过滤                            │
│    → sort_spy_states_for_dispatch(SpyGroupConfig)            │
│    → try_trigger_triggerable(state, timing, source_event)    │
│    → SpyGroup 内 kind=SKILL 步（如 PURSUIT 追击）            │
└─────────────────────────────────────────────────────────────┘
```

**回放关键：** 索引只作候选池；**最终顺序**由 `chain_reaction_config.py` 在 dispatch / run_timing 时决定。

配置文件路径：`battlecore/config/chain_reaction_config.py`  
挂载：`ConfigDB.spy_groups`、`ConfigDB.regular_groups`、`ConfigDB.state_unconfigured_sort`

实现：`battlecore/engine/chain_reaction.py`

---

## 五、生命周期与持续规则

### 5.1 施加 `add_state`

- 由 Effect `CONTROL_APPLY` / `SPECIAL_STATE_GRANT` 或准备型战法施加
- 控制状态同 config_id 刷新持续时间；其它类型同来源 actor 可叠层/刷新
- 控制状态落地时若 `forbid_active`，打断目标全部准备型战法 state
- 发出 `STATE_ADDED` 或 `CONTROL_STATE_APPLIED`

### 5.2 持续时间 tick

**默认规则（控制、多数 buff）：**

- 在**持有者自己的 `BEFORE_ACTION`** 时 `action_tick_count += 1`
- 计数 **>** `duration_rounds` 时移除
- 「持续 1 回合」= 获得后经历 1 次自己的行动前仍生效，第 2 次行动前过期

**例外：** `payload.duration_tick_mode == "ROUND_END"` 时在 `ROUND_END` 统一 tick（预留）。

### 5.3 移除 `remove_state`

- 过期、驱散、阵亡清理
- 武将 `exited`：移除其身上全部 state，以及 **`source_actor_id == 阵亡者 instance_id`** 施加在他人身上的 state（【献祭武力】等永久 ATTR 例外）
- 清理**仅**按 `owner.instance_id` / `source_actor_id`（均为运行时 instance_id）匹配，**不得**按 `source_skill_id` 配置 id 全局删除（否则双方同名技能会误删他队状态）

### 5.4 数值读取（被动 State）

| 读取点 | 类型 | 说明 |
|--------|------|------|
| `get_effective_attr` | `ATTR` | 四维 `*_delta` / `*_bps` |
| 伤害/治疗公式 | `ATTR` | 增伤、易伤、治疗提升等 |
| `calc_damage_reduce_bps` | `DAMAGE_REDUCE` | 减伤乘区汇总 |

`trigger_mode=NONE` 的 `ATTR` / `DAMAGE_REDUCE` **不进触发索引**，只参与数值汇总。

---

## 六、REGULAR 响应（Timing 驱动）

### 6.1 配置结构

```python
BEFORE_ACTION_REGULAR = RegularGroupConfig(
    group_id="before_action",
    timing=Timing.BEFORE_ACTION,
    steps=(
        TriggerStepConfig("shadow_veil", state_tags=("shadow_veil",)),
        TriggerStepConfig("hades_command_drain", state_tags=("hades_command_drain",)),
    ),
)
```

### 6.2 派发流程

1. `run_timing` 进入某 timing（如 `BEFORE_ACTION`）
2. 从 `regular_state_timing_index[timing]` 收集当前 actor 的 `trigger_mode=REGULAR` 状态
3. `find_regular_group(regular_groups, timing)` 找对应组
4. `sort_regular_states_for_dispatch()`：
   - 在组内 `steps` 匹配到的 → 按 steps 顺序
   - 未匹配的 → 组内配置步全部处理完后，按 `state_unconfigured_sort` 排序
5. 逐个 `try_trigger_triggerable` → `state.execute(context)`（无 `source_event`）
6. 每触发一次 `dispatch_events()`（SPY 可嵌套响应）

### 6.3 demo：`BEFORE_ACTION`

| 序 | tag | 实例 | 行为 |
|----|-----|------|------|
| 1 | `shadow_veil` | 【幽影蔽体】 | 按损失兵力刷新 `damage_reduce_bps` |
| 2 | `hades_command_drain` | 【冥祭献统】 | 献祭友军统率，累加自身武力 ATTR |

---

## 七、SPY 响应（Event 驱动）

### 7.1 配置结构

```python
DAMAGE_SETTLED_SPY = SpyGroupConfig(
    group_id="damage_settled",
    listen_event_types=(EventType.DAMAGE_SETTLED,),
    steps=(
        TriggerStepConfig("styx_blood_oath", state_tags=("styx_blood_oath",)),
        TriggerStepConfig("snake_staff_protection", state_tags=("snake_staff_protection",)),
        TriggerStepConfig("thunder_oracle", state_tags=("thunder_oracle",)),
        TriggerStepConfig("pursuit", kind="SKILL", skill_category=SkillCategory.PURSUIT),
    ),
)
```

### 7.2 `TriggerStepConfig`

| 字段 | 含义 |
|------|------|
| `step_id` | 配置内标识 |
| `kind` | `STATE` 或 `SKILL`（`SKILL` 仅用于 SPY 组） |
| `state_tags` | 匹配 `State.tags` 中任一 tag |
| `skill_category` | `kind=SKILL` 时匹配持有者战法（如 `PURSUIT`） |

### 7.3 派发流程

1. `emit_event` 入队 → `dispatch_events` 取出事件
2. 收集 `spy_state_event_index[event.event_type]` 中 `trigger_mode=SPY` 的状态
3. `should_trigger_by_event` + `responded_event_ids` + `is_state_battle_active` 过滤
4. `sort_spy_states_for_dispatch()`（规则同 REGULAR，用 `SpyGroupConfig`）
5. 依次 `try_trigger_triggerable(state, current_timing, source_event=event)`
6. 组内 `kind=SKILL` 步：`_try_trigger_chain_skill_step`（如普攻后追击）

### 7.4 防重与上限

| 机制 | 作用 |
|------|------|
| `responded_event_ids` | 同一 State 不重复响应同一 `event_id` |
| `max_events_per_step` | 单轮 dispatch 事件数上限 |
| `max_chain_depth` | 连锁深度上限 |
| tag 内过滤 | 如落雷不触发落雷、冥河要求 `damage>0` |

### 7.5 demo：`DAMAGE_SETTLED`

| 序 | step_id | 类型 | 实例 | 要点 |
|----|---------|------|------|------|
| 1 | `styx_blood_oath` | STATE | 【冥河血誓】 | 攻击者==持有者，`damage>0` |
| 2 | `snake_staff_protection` | STATE | 【蛇杖庇护】 | 持有者在 `target_ids`，`damage>0` |
| 3 | `thunder_oracle` | STATE | 【雷霆】 | 攻击者==持有者；排除落雷自递归 |
| 4 | `pursuit` | SKILL | 【突击】 | 普攻 SETTLED + `damage>0`；目标=`source_event.target_ids` |

典型时序：

```text
apply_damage → DAMAGE_APPLIED → DAMAGE_SETTLED → dispatch_events()
  ① 冥河 → ② 蛇杖 → ③ 雷霆 → ④ 追击 → ⑤ 未配置 SPY
```

Effect 每段执行后也会 `dispatch_events()`，因此多段技能中间可插入 SPY。

---

## 八、未配置排序 `UnconfiguredStateSortConfig`

未列入 `steps` 的 State，在**已配置步全部处理完后**按以下键稳定排序（从前到后比较）：

| 键 | 含义 |
|----|------|
| `owner_position` | 持有者阵位升序 |
| `owner_instance_id` | 持有者 id 字典序 |
| `state_instance_id` | 状态实例 id 字典序 |

无对应 `SpyGroupConfig` / `RegularGroupConfig` 时，该 timing / event 下全部 State 仅按此规则排序。

---

## 九、触发检查与执行

State 与 Skill 共用 `try_trigger_triggerable` 流水线：

```text
can_trigger_at(timing, source_event?)
  REGULAR: timing ∈ trigger_timings
  SPY: trigger_timings 可为空；timing 由 current_timing 传入
PRE_TRIGGER_CHECK → 概率 roll → TRIGGER_SUCCESS → state.execute(source_event?)
POST_TRIGGER → dispatch_events()
```

`State.execute` 中按 tag 分支实现具体效果；复杂逻辑应优先拆到 Effect，State 只做响应式收尾。

---

## 十、demo 实例速查

| 名称 | state_type | trigger_mode | 监听 / timing | 顺序组 |
|------|------------|--------------|---------------|--------|
| 【神示】 | ATTR | NONE | — | — |
| 【幽影蔽体】 | DAMAGE_REDUCE | REGULAR | BEFORE_ACTION | `BEFORE_ACTION_REGULAR` 第 1 步 |
| 【冥祭献统】 | SPECIAL | REGULAR | BEFORE_ACTION | 第 2 步 |
| 【冥锁】 | CONTROL | NONE | — | — |
| 【蛇杖庇护】 | SPECIAL | SPY | DAMAGE_SETTLED | `DAMAGE_SETTLED_SPY` 第 2 步 |
| 【雷霆】 | SPECIAL | SPY | DAMAGE_SETTLED | 第 3 步 |
| 【冥河血誓】 | SPECIAL | SPY | DAMAGE_SETTLED | 第 1 步 |
| 【神谕吟诵】/【筹谋酝酿】 | SPECIAL | NONE | — | 准备型战法内部 state |
| 【献祭武力】 | ATTR | NONE | — | 运行时累加，队友阵亡不删 |

---

## 十一、如何新增 State

### 11.1 被动数值（ATTR / DAMAGE_REDUCE）

1. `skill_files.py` 写描述 + `StateConfig(state_type=ATTR, trigger_mode=NONE, payload={...})`
2. 无需改 `chain_reaction_config.py`
3. 测试 `get_effective_attr` / 伤害结算

### 11.2 REGULAR 响应

1. `trigger_mode=REGULAR`，`trigger_timings=[Timing.xxx]`
2. 在对应 `RegularGroupConfig.steps` **插入** `TriggerStepConfig`（位置即相对顺序）
3. 实现 `State.execute(context)`（无 source_event 分支）
4. 若同 timing 多实例，务必配置顺序以保证回放一致

### 11.3 SPY 响应

1. `trigger_mode=SPY`，`listen_event_types=[EventType.xxx]`
2. 实现 `should_trigger_by_event` / `execute(context, source_event)`
3. 在对应 `SpyGroupConfig.steps` 插入 STATE 步
4. 防递归：在过滤中排除自触发（如【雷霆】排除落雷 state）
5. 连锁战法：`kind=SKILL` + `SkillCategory.PURSUIT` 等

### 11.4 注册

`config_db.py` 注册 state_config → 战斗开始时 `rebuild_indexes()` 重建索引。

---

## 十二、相关代码与文档

| 路径 | 职责 |
|------|------|
| `domain/skill.py` | `State` 类、过滤与 execute |
| `config/chain_reaction_config.py` | SPY / REGULAR 顺序配置 |
| `engine/chain_reaction.py` | 排序与 SPY 组 SKILL 步 |
| `engine/battle_context.py` | 索引、`run_timing`、`dispatch_events` |
| `config/skill_files.py` | demo State 配置与注释 |

| 文档 | 内容 |
|------|------|
| [DESIGN_V2.md](./DESIGN_V2.md) | 战斗总纲与主循环 |
| [EVENT_SIGNAL_REFERENCE.md](./EVENT_SIGNAL_REFERENCE.md) | EventType / Signal / Timing |
| [TARGET_SELECTION_REFERENCE.md](./TARGET_SELECTION_REFERENCE.md) | Effect 选人 |

---

*文档版本：与 `TriggerMode.REGULAR` / `SpyGroupConfig` / `RegularGroupConfig` 分组配置对齐。*
