# BattleCore 设计总纲 v2.0

## 一、定位

BattleCore 是一个面向 SLG 战斗的确定性、事件驱动、数据驱动、可回放战斗核心原型。

- 3v3 武将战斗。
- BASIC / ACTIVE / COMMAND / PURSUIT 技能框架。
- 顺序 Effect 执行与目标关联。
- ATTR / DAMAGE_REDUCE / CONTROL / SPY / REGULAR 等状态类型与响应机制。
- 伤害、治疗、阵亡、伤兵模型。
- DAMAGE_SETTLED / HEAL_SETTLED 结算信号。
- 受击点数 / 实时受击率模型（归一法，当前仅日志，尚未参与目标选择）。
- 伪随机动态概率。
- 准备阶段神谕类全体增益。
- 受击治疗、伤害后追加落雷、行动前刷新减伤等 State 响应。

战法总分类：
神谕技能：


核心目标：

- 同一输入和 seed 必须得到完全一致的结果。
- 所有随机、伤害、治疗、状态变化都必须可审计。
- 所有关键行为都通过配置表达，避免把技能写死在主循环中。
- 所有状态变化都通过 `BattleContext` API 发生，便于迁移到服务端或其他语言。

### 1.1 详细参考文档


| 文档                                                               | 何时查阅                                              |
| ---------------------------------------------------------------- | ------------------------------------------------- |
| [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)     | State 分类、`TriggerMode`、REGULAR/SPY 响应顺序、生命周期、配置建议 |
| [EVENT_SIGNAL_REFERENCE.md](./EVENT_SIGNAL_REFERENCE.md)         | 全量 `EventType` / 战法 Signal、`dispatch_events` 时序   |
| [TARGET_SELECTION_REFERENCE.md](./TARGET_SELECTION_REFERENCE.md) | Effect `TargetPolicy` 与选人规则                       |


### 1.2 核心机制概要

**① 确定性与事件驱动**

同一 `BattleInput` + `seed` → 同一 `event_stream` → 同一结果。随机仅经 `DeterministicRNG`；关键行为均发 `BattleEvent` 可审计。

**② 主循环 = Timing 时间片**

全局 Timing（`PREPARE`、`ROUND_START`…）与每武将行动 Timing（`BEFORE_ACTION → ACTIVE → BASIC → AFTER_ACTION`）驱动 Skill 判定。详见 **第四节**。

**③ Skill 触发流水线**

`try_trigger_triggerable`：规则检查 → 概率 roll →（Skill）发 Signal → 顺序 execute Effect → 每段后 `dispatch_events()`。主动战法机制见 **第六节**。

**④ State 两条响应通路**（详见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)）

- **REGULAR**：挂在 `trigger_timings`，在 `run_timing` 按 `RegularGroupConfig` 顺序执行（demo：`BEFORE_ACTION` 幽影蔽体 → 冥祭献统）。
- **SPY**：挂在 `listen_event_types`，在 `dispatch_events` 按 `SpyGroupConfig` 顺序响应（demo：`DAMAGE_SETTLED` 冥河 → 蛇杖 → 雷霆 → 追击）。
- **被动 ATTR / DAMAGE_REDUCE**：`trigger_mode=NONE`，不进索引，只被伤害/属性模型读取。

**⑤ 结算信号 Applied vs Settled**

伤害/治疗先 `*_APPLIED`（日志），再 `*_SETTLED`（payload 含实际量）。SPY 连锁监听 **Settled**；`damage=0` 时多数受伤 SPY 不触发，雷霆仍可判定。

**⑥ 目标选择**

Effect 层按 `TargetPolicy` 选人；追击用 `SAME_AS_SOURCE_EVENT` 读源事件目标。详见 [TARGET_SELECTION_REFERENCE.md](./TARGET_SELECTION_REFERENCE.md)。

**⑦ 伪随机**

面板概率走统一伪随机表（`failCount` / `successStreak`）；与 `TRIGGER_SUCCESS` 事件审计分离。

## 二、模块职责

### `api`

对外入口。

- `run_battle(input, config_db=None)`：运行一场战斗并返回 `BattleResult`。
- 返回内容包括 summary、event_stream、human_logs、replay_data。

### `config`

数据配置层。

- `schema.py`：定义 `BattleInput`、`HeroConfig`、`SkillConfig`、`EffectConfig`、`StateConfig`、`BattleSummary`。
- `config_db.py`：构建 demo 配置库；挂载 `spy_groups` / `regular_groups`（State 响应顺序）。
- `chain_reaction_config.py`：REGULAR / SPY 分组顺序配置（见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)）。
- `skill_files.py`：复杂技能配置文件，每个技能必须先写自然语言描述，再写结构化配置。
- `validation.py`：配置与输入校验。

### `domain`

领域对象层。

- `Hero`：运行时武将状态，包括兵力、伤兵、阵亡、技能、状态、统计值，以及受击点数（`hit_points_bps`）、初始受击点数（`initial_hit_points_bps`）、实时受击率（`realtime_hit_rate_bps`）。
- `Skill`：技能实例，负责 timing、概率、顺序 effects 和执行记录。
- `Effect`：原子效果，负责单次伤害、治疗、施加状态等。
- `State`：运行时状态实例；支持被动 ATTR、REGULAR 定时触发、SPY 事件响应。详见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)。
- `enums.py`：所有枚举协议。

### `engine`

战斗执行层。

- `BattleEngine`：从 input 构建 context 并执行战斗。
- `BattleContext`：主循环、事件派发、技能触发、状态触发、目标选择、伤害治疗结算、受击率重算。
- `damage_calculator.py`：伤害、治疗、伤兵、阵亡数学模型。
- `hit_rate.py`：受击点数与实时受击率计算公式。

### `event`

事件层。

- `BattleEvent`：战斗事件结构。
- `EventBus` / event stream：保存按 `event_id` 递增的事件序列。
- `event_codec.py`：事件序列化。

### `rng`

确定性随机。

- `DeterministicRNG`：所有随机必须通过这里产生。
- RNG history 记录 source、reason、roll_bps，便于回放和审计。

### `tests`

验证层。

- 单测可以直接运行。
- 测试会打印并保存 human logs。
- batch 测试会输出胜率、伤亡、技能与 SPY state 实际生效概率。

## 三、战斗输入

`BattleInput` 包含：

- `battle_id`
- `seed`
- `max_rounds`
- `config_version`
- `team_a_heroes`
- `team_b_heroes`

`HeroConfig` 包含：

- `hero_id`
- `name`
- `team_id`
- `role`
- `position`
- `max_troops`
- `force`
- `intelligence`
- `command`
- `speed`
- `skill_ids`

## 四、主循环 Timing

> **维护约定**：本节主流程伪代码必须与 `battlecore/engine/battle_context.py` 实现保持同步。每次修改战斗引擎或触发机制时，须同步更新本节。

### 4.1 战斗总流程 `run_battle`

```text
emit BATTLE_STARTED
dispatch_events()
rebuild_indexes()                    # skill_timing_index / regular_state_timing_index / spy_state_event_index
run_timing(BATTLE_START)
run_timing(HIT_RATE_INIT)              # 全局受击率初始化（凌驾于 PREPARE 与所有武将战法之前）
run_timing(PREPARE)                    # 指挥战法、开局状态等
check_battle_finish()

for round_no in 1..max_rounds:
    if battle_finished: break
    round_no = round_no
    reset_round_counters()
    prepare_round_action_order(round_no)   # 按速度伪随机决定本回合行动顺序
    emit ROUND_STARTED(payload=行动顺序)
    dispatch_events()
    run_timing(ROUND_START)              # 伤兵转死兵、ROUND_START 状态/技能，结束时打印有效四维
    if battle_finished: break

    for hero_id in speed_order:
        if battle_finished: break
        actor = heroes[hero_id]
        if actor.exited or not actor.is_alive(): continue
        current_actor_id = actor.instance_id

        for timing in [BEFORE_ACTION, ACTIVE, BASIC, AFTER_ACTION]:
            if battle_finished or actor.exited or not actor.is_alive(): break
            run_timing(timing, actor)
            check_battle_finish()

    if battle_finished: break
    run_timing(ROUND_END)
    tick_states(ROUND_END)             # 仅处理显式配置为 ROUND_END 结算的状态
    emit ROUND_ENDED
    dispatch_events()
    check_battle_finish()

if not battle_finished:
    finish_by_remaining_troops()       # 按剩余总兵力判胜，相同则 DRAW
run_timing(BATTLE_END)
emit BATTLE_FINISHED
dispatch_events()
return summary()
```

### 4.2 单个 Timing `run_timing(timing, actor?)`

```text
if battle_finished and timing != BATTLE_END: return
current_timing = timing
emit TIMING_STARTED
dispatch_events()

if timing == ROUND_START:
    for hero in alive_heroes:
        apply_wounded_to_dead(hero)        # 在场武将伤兵池 30% 转死兵（已阵亡者跳过）
    # ROUND_START 状态/技能 …
    log EffectiveAttrs table             # 在场武将有效四维（含 ATTR 修正）

if timing == BEFORE_ACTION and actor:
    tick_states_before_actor_action(actor)   # 控制/属性等状态按目标 BEFORE_ACTION 计数
    dispatch_events()
    if check_battle_finish(): return

# 1) REGULAR 状态（trigger_mode=REGULAR，挂在 timing 索引上）
for state in sort_regular_states_for_dispatch(regular_state_timing_index[timing], …):
    try_trigger_triggerable(state, timing)
    dispatch_events()
    if battle_finished: break

# 2) 技能
if timing == ACTIVE and actor:
    _advance_all_active_preparing(actor)   # 按 source_skill_id 推进各战法独立 state
    dispatch_events()
    if battle_finished: return
for skill_id in skill_timing_index[timing]:
        skill = skill_instances[skill_id]
        if skill.owner.exited: continue
        if actor and skill.owner != actor: continue
        if not actor and timing not in global_timings: continue
        try_trigger_triggerable(skill, timing)
        dispatch_events()
        if battle_finished: break

emit TIMING_ENDED
dispatch_events()
```

`global_timings` = `BATTLE_START | HIT_RATE_INIT | PREPARE | ROUND_START | ROUND_END | BATTLE_END`。

### 4.3 准备型主动战法 `_advance_all_active_preparing`

每个准备战法配置**独立** `StateConfig`（如 `delphi_charged_preparing_state` / `pythia_woven_preparing_state`），`payload.source_skill_id` 标识归属。tag 均为 `active_preparing`，但**互不混用、互不阻塞**其他主动战法。

每次轮到该英雄 `ACTIVE` 时机，先按 `source_skill_id` 排序推进所有准备 state：

```text
for preparing in sorted(所有 active_preparing state, by source_skill_id):
    prepare_ticks += 1
    log「战法 {skill_name}【{state.name}】进度 ticks/rounds」
    if ticks < prepare_rounds: continue
    remove_state(preparing)
    _release_preparation_skill(对应 skill)
# 然后照常遍历 skill_timing_index，各战法独立判定
```

`can_trigger_at`：仅当**同一战法**已持有自己的准备 state 时返回 `ACTIVE_PREPARING`；其它 ACTIVE 战法（含其它准备战法）不受影响。

控制状态（`forbid_active`）在 `add_state` 时调用 `_interrupt_active_preparing`，移除目标身上**全部**准备 state（各战法均怕控制）。

### 4.4 触发入口 `try_trigger_triggerable`

```text
check = can_trigger_at(timing)
emit PRE_TRIGGER_CHECK
if not check.allowed:
    record_trigger_fail → emit TRIGGER_FAIL → return False

probability = roll_probability()
if not probability.allowed:
    record_trigger_fail → emit TRIGGER_FAIL → return False

# 分支 A：准备型主动战法（is_preparation_active）
if Skill and is_preparation_active:
    return _try_trigger_preparation_active()    # 见 4.5，不计 success_count

# 分支 B：普通技能 / SPY 状态
record_trigger_success
if Skill: emit BEFORE 信号
emit TRIGGER_SUCCESS
if Skill:
    emit ON 信号 → execute_skill(全部 effect) → emit AFTER 信号
else:
    state.execute()
emit POST_TRIGGER
return True
```

### 4.5 准备型主动战法信号与计数

> 主动战法完整机制、技能信号含义、控制打断流程见 **第六节「主动战法（ACTIVE）机制」**。


| 阶段       | 触发条件        | 技能信号                                          | TRIGGER_SUCCESS.payload.phase | success_count |
| -------- | ----------- | --------------------------------------------- | ----------------------------- | ------------- |
| **进入准备** | ACTIVE 概率成功 | 仅 `BEFORE_*_SIGNAL`，`trigger_phase=PREPARE`   | `PREPARE`                     | **不增加**       |
| **释放伤害** | 吟诵进度满       | `BEFORE → ON → AFTER`，`trigger_phase=RELEASE` | `RELEASE`                     | **+1**        |


进入准备时：执行 `prepare_effect_ids`（施加【神谕吟诵】），发 `POST_TRIGGER`（`effective=false`），**不**发 ON/AFTER 信号。

释放时：`record_trigger_success` 在 `execute_skill(release_effect_ids)` 之前执行。

### 4.6 事件派发 `dispatch_events`（SPY 响应）

> 完整 SPY 顺序、REGULAR 顺序、State 分类与配置指南见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)。

```text
while event_queue not empty:
    event = dequeue
    if超过 max_events_per_step / max_chain_depth: 中止或跳过

    eligible = 过滤 SPY 状态（should_trigger_by_event 等）
    ordered = sort_spy_states_for_dispatch（SpyGroupConfig + state_unconfigured_sort）
    for state in ordered:
        try_trigger_triggerable(state, current_timing, source_event=event)
    for step in chain_group.skill_steps:
        _try_trigger_chain_skill_step(event, step)
    check_battle_finish()
```

`DAMAGE_SETTLED` demo 固定顺序（配置 `chain_reaction_config.DAMAGE_SETTLED_SPY`）：

1. 【冥河血誓】`styx_blood_oath`
2. 【蛇杖庇护】`snake_staff_protection`
3. 【雷霆】`thunder_oracle`
4. 【突击】追击 `PURSUIT`（kind=SKILL）
5. 未列入 steps 的 SPY → `state_unconfigured_sort`

典型路径：伤害落地 → `DAMAGE_SETTLED` → 按上序连锁 → 治疗/落雷/追击等。

Effect 执行中每段 effect 结束后会 `dispatch_events()`，使 SPY 可在下一段 effect 之前响应。

### 4.7 行动内顺序

每个武将行动时，`ACTIVE` 在 `BASIC` 前执行；**无独立 `PURSUIT` 窗口**。

追击（`SkillCategory.PURSUIT`）不在 `skill_timing_index` 中登记，而是在同次普攻 `apply_damage` 发出 `DAMAGE_SETTLED` 后、由 `dispatch_events` 按 SPY 组配置**第 4 步**判定（冥河 → 蛇杖 → 雷霆 → 追击），仍在同一 `BASIC` 时间片内。

```text
run_timing(BASIC):
  try_trigger_triggerable(basic_attack)
    execute_skill(basic_attack)
      apply_damage → DAMAGE_APPLIED / DAMAGE_SETTLED
      dispatch_events()                     # 连锁：冥河→蛇杖→追击→雷霆…
      POST_EFFECT_EXECUTE（普攻伤害 effect）
      dispatch_events()
```

要点：

- 普攻被【冥锁】等 `forbid_basic` 拦截 → 无伤害 apply、无 `DAMAGE_SETTLED`、无 `POST_EFFECT_EXECUTE` 成功链 → **追击自然不触发**。
- 普攻 effect 概率失败 / 无目标 → 不进入 `EXECUTED` → 不触发追击。
- 伤害 apply 但 `actual_damage == 0` → 仍发 `DAMAGE_SETTLED`（damage=0）；蛇杖/冥河/追击不触发，雷霆仍可判定。
- 追击自身仍受 `forbid_pursuit` 控制；信号仍用 `BEFORE_PURSUIT_SIGNAL` / `PURSUIT_SIGNAL` / `AFTER_PURSUIT_SIGNAL`，`timing` 保持为 `BASIC`。

## 五、Skill / Effect / State 模型

### Skill

`SkillConfig` 表达：

- 技能 id、名称、类别。
- 触发 timing。
- 面板概率。
- effect 顺序。
- 每回合 / 每场触发次数上限。
- 有效回合范围。
- 策划描述和伪随机参数。

Skill 不直接修改战斗状态，而是顺序执行 Effect。

### Effect

Effect 是原子动作。

支持：

- DAMAGE / TRUE_DAMAGE
- HEAL
- CONTROL_APPLY
- SPECIAL_STATE_GRANT
- DOT_APPLY / DEBUFF 等预留类型

Effect 可以通过 `params` 保存目标别名：

- `store_targets_as`
- `target_from_effect_alias`
- `exclude_effect_aliases`

这使多段技能可以表达“第二段复用第一段目标”“第三段排除第一段目标”。

### State

> **完整说明**（分类、`TriggerMode`、生命周期、REGULAR/SPY 顺序配置、新增指南）见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)。

State 是挂在武将身上的运行时实例，与 Skill 共用触发框架，但语义为「持续 payload + 可选响应」。

**按语义（`StateType`）**


| 类型              | 作用                               | demo         |
| --------------- | -------------------------------- | ------------ |
| `ATTR`          | 四维 / 增伤 / 易伤等                    | 【神示】【献祭武力】   |
| `DAMAGE_REDUCE` | 减伤乘区                             | 【幽影蔽体】       |
| `CONTROL`       | `forbid_basic` / `forbid_active` | 【冥锁】         |
| `SPECIAL`       | 监听或定时特殊逻辑                        | 【蛇杖庇护】【冥祭献统】 |


**按调度（`TriggerMode`）**


| 模式        | 触发入口                                    |
| --------- | --------------------------------------- |
| `NONE`    | 不触发；被动参与数值汇总                            |
| `REGULAR` | `run_timing`，按 `RegularGroupConfig` 排序  |
| `SPY`     | `dispatch_events`，按 `SpyGroupConfig` 排序 |


持续回合默认在**持有者自己的 `BEFORE_ACTION`** tick，而非 `ROUND_END`。

## 六、事件协议

> **完整触发表**（全部 `EventType`、Signal 阶段、Applied/Settled 区别、典型时序图）见 [EVENT_SIGNAL_REFERENCE.md](./EVENT_SIGNAL_REFERENCE.md)。
>
> **State 如何监听与响应事件**见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md) §七。

核心事件：

- `BATTLE_STARTED`
- `ROUND_STARTED`
- `TIMING_STARTED`
- `PRE_TRIGGER_CHECK`
- `TRIGGER_SUCCESS`
- `TRIGGER_FAIL`
- `POST_TRIGGER`
- `PRE_EFFECT_CHECK`
- `EFFECT_CHECK_SUCCESS`
- `EFFECT_CHECK_FAIL`
- `PRE_EFFECT_EXECUTE`
- `POST_EFFECT_EXECUTE`
- `DAMAGE_APPLIED`
- `DAMAGE_SETTLED`
- `HEAL_APPLIED`
- `HEAL_SETTLED`
- `STATE_ADDED`
- `STATE_DURATION_TICKED`
- `STATE_REMOVED`
- `HERO_EXITED`
- `HERO_EXITED_SETTLED`
- `MAIN_HERO_EXITED`
- `BATTLE_FINISHED`

### 主动战法（ACTIVE）机制

主动战法在**每个武将自己的行动窗口**里、于 `BEFORE_ACTION` 之后、`BASIC` 之前，进入 `run_timing(ACTIVE, actor)` 时参与判定。

同一 `ACTIVE` 时机内，该英雄身上所有 `trigger_timings` 含 `ACTIVE` 的技能会按索引顺序依次调用 `try_trigger_triggerable`；**每判定一个就 `dispatch_events()`**，因此先触发的战法产生的事件（伤害、施加控制等）可能影响后触发的战法。

#### 触发前检查 `can_trigger_at`

在 roll 概率之前，引擎先做规则检查并发出 `PRE_TRIGGER_CHECK`。主动战法相关失败原因：


| 原因                                               | 含义                             | 是否 roll 概率 | 是否发技能信号 |
| ------------------------------------------------ | ------------------------------ | ---------- | ------- |
| `TIMING_NOT_MATCH`                               | 非 ACTIVE 时机                    | 否          | 否       |
| `OWNER_EXITED`                                   | 持有者已退出                         | 否          | 否       |
| `ROUND_NOT_VALID` / `COOLDOWN` / `MAX_TRIGGER_*` | 回合、冷却、次数上限                     | 否          | 否       |
| `CONTROL_FORBID_ACTIVE`                          | 身上有 `forbid_active` 的控制（如【冥锁】） | 否          | 否       |
| `ACTIVE_PREPARING`                               | 该战法自身已在准备中，禁止重复判定              | 否          | 否       |


规则失败只发 `TRIGGER_FAIL`（`failure_kind=CONTROL` 或 `RULE`），**不**更新伪随机 `successStreak`，**不**发 `BEFORE_ACTIVE_SIGNAL` 等技能信号。

概率失败发 `TRIGGER_FAIL`（`failure_kind=PROBABILITY`），同样无技能信号，但会累计伪随机 `failCount`。

#### 两类主动战法


|                 | **即时主动战法**                      | **准备型主动战法**                                                          |
| --------------- | ------------------------------- | -------------------------------------------------------------------- |
| 识别              | `params.prepare_rounds` 未配置或为 0 | `prepare_rounds > 0`，且配置 `prepare_effect_ids` / `release_effect_ids` |
| 示例              | 【戈耳工凝视】                         | 【德尔斐蓄谕】                                                              |
| 概率判定次数          | 每次 ACTIVE 各判一次                  | 仅**无准备 state**时判一次；有准备 state 时跳过 trigger，只推进进度                       |
| `success_count` | 每次概率成功并完整发动 +1                  | 仅**释放**时 +1；进入准备不计                                                   |
| 吟诵状态            | 无                               | 各战法独立 state（`source_skill_id` 区分）                                    |


---

### 技能信号是什么

技能信号是一组**专供战法连锁监听**的 `BattleEvent`，与 `TRIGGER_SUCCESS` / `POST_TRIGGER` 等审计事件并列存在。

设计目的：

- 让被动、指挥、SPY 状态或未来配置能挂在「战法发动前 / 时 / 后」做响应（如增伤、反击、打断）。
- 与 `TRIGGER_SUCCESS` 区分：后者记录概率判定结果与统计；信号事件表达**战法执行流水线阶段**。
- 规则禁止或概率失败时**不发射**任何技能信号，避免「没发动却触发了发动前被动」。

对 `SkillCategory.ACTIVE`，三个信号事件类型与内部阶段 `phase` 的对应关系：


| 事件类型                   | 内部 `payload.phase` | 俗称    | 时机                                |
| ---------------------- | ------------------ | ----- | --------------------------------- |
| `BEFORE_ACTIVE_SIGNAL` | `BEFORE`           | 战法发动前 | 效果执行之前                            |
| `ACTIVE_SIGNAL`        | `ON`               | 战法发动时 | `execute_skill` 之前、刚通过判定          |
| `AFTER_ACTIVE_SIGNAL`  | `AFTER`            | 战法发动后 | 本段 `execute_skill` 全部 effect 完成之后 |


（`BASIC` / `PURSUIT` 同理，只是事件名换成 `BEFORE_BASIC_SIGNAL` 等。）

信号 payload 常用字段：

- `phase`：`BEFORE` / `ON` / `AFTER`
- `skill_category`：如 `ACTIVE`
- `skill_instance_id`：运行时技能实例 id
- `source_event_id`：若由 SPY 连锁触发，指向源事件
- `trigger_phase`（可选）：`PREPARE` / `RELEASE`，**仅准备型主动战法**使用，区分「进入准备」与「准备完成释放」

监听方应同时看 `event_type`（类别）和 `payload.phase` / `trigger_phase`（子阶段）。

#### 判定成功后，三件事不要混为一谈

一次「概率判定成功」会牵涉**三套互不替代的数据**，文档和战报里不要混用：


| 层次             | 发生时机                       | 更新什么                                                              | 是否进 event_stream                                         |
| -------------- | -------------------------- | ----------------------------------------------------------------- | -------------------------------------------------------- |
| **① 伪随机 roll** | `roll_probability()` 内部    | `pseudo_random_states` 的 `failCount` / `successStreak`            | 否（结果写入后续 `TRIGGER_SUCCESS` payload）                      |
| **② 技能统计**     | `record_trigger_success()` | 技能实例 `success_count`、`trigger_count_`*、武将 `skill_trigger_success` | 否（仅 summary 用）                                           |
| **③ 触发结果事件**   | `emit TRIGGER_SUCCESS`     | 无状态写入，只发可回放事件                                                     | 是，含 `roll_bps` / `threshold_bps` / `pseudo_random_key` 等 |


失败时对称：`roll` 已更新伪随机 → `record_trigger_fail` 更新技能 `fail_count` → `emit TRIGGER_FAIL`。

`**TRIGGER_SUCCESS` 不是重复计数**，而是事件流里的**审计记录**：告诉回放/日志「这次触发通过了概率」，并把 roll 细节带给监听方。`success_count` 是战后 summary 用的**发动成功次数**（准备型战法仅在释放时 +1）。

当前实现里即时战法的顺序是：`roll` → `record_trigger_success` → `BEFORE` 信号 → `TRIGGER_SUCCESS` 事件 → `ON` → `execute` → `AFTER`。`record_trigger_success` 在 `TRIGGER_SUCCESS` **之前**，属于实现顺序；语义上两者都表示「本次触发已成功」，一个写内存统计、一个写事件流。

---

### 即时主动战法：全流程与信号

以【戈耳工凝视】为例，`ACTIVE` 时机单次成功发动的完整事件序：

```text
PRE_TRIGGER_CHECK          allowed=true
roll 概率                  成功 → 伪随机 failCount/successStreak 在此刻更新
record_trigger_success     技能 success_count += 1（无事件，仅 summary 统计）
BEFORE_ACTIVE_SIGNAL       phase=BEFORE（无 trigger_phase）
TRIGGER_SUCCESS            事件流记录：roll_bps / threshold_bps / pseudo_random_key 等
ACTIVE_SIGNAL              phase=ON
execute_skill              顺序执行全部 effect（选目标→伤害→施加【冥锁】…）
  每段 effect 后 dispatch_events()  → SPY 可响应 DAMAGE_SETTLED 等
AFTER_ACTIVE_SIGNAL        phase=AFTER
POST_TRIGGER
```

要点：

- **三个技能信号都会发**，且都发生在同一次概率成功之后。
- 伪随机 streak 在 `**roll` 时**已更新，不是在 `record_trigger_success` 里。
- `record_trigger_success` 与 `TRIGGER_SUCCESS` **各管各的**：前者写技能统计，后者写可回放事件；不是两次「成功判定」。
- `record_trigger_success` 在 `BEFORE_ACTIVE_SIGNAL` **之前**（与准备型「释放」阶段一致）。
- Effect 内的控制施加走 `add_state`；若目标正在吟诵，见下文「控制打断」。

失败路径（规则或概率）：

```text
PRE_TRIGGER_CHECK → TRIGGER_FAIL → 结束（无 BEFORE/ON/AFTER，无 success_count 变化）
```

---

### 追击战法（PURSUIT）：全流程与信号

以【突击】（`pursuit_strike`）为例。追击**不是**独立 timing，而是普攻 `DAMAGE_SETTLED` 连锁配置中的 `kind=SKILL` 步骤。

#### 登记与触发入口

- `SkillCategory.PURSUIT`；`trigger_timings=[]`，`rebuild_indexes` 时**跳过**，不进入 `skill_timing_index`。
- `run_battle` 行动循环无 `PURSUIT` 窗口（仅 `BEFORE_ACTION → ACTIVE → BASIC → AFTER_ACTION`）。
- 触发入口：`apply_damage` 发出 `DAMAGE_SETTLED` 后 `dispatch_events()`；`SpyGroupConfig` 第 4 步调用 `_try_trigger_chain_skill_step`（需 `is_basic_damage_settled_signal` 且 `damage>0`）。见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)。

#### 前置条件（全部满足才进入追击判定）


| 条件                                                | 不满足时                                 |
| ------------------------------------------------- | ------------------------------------ |
| 普攻 `try_trigger_triggerable` 未被 `forbid_basic` 拦截 | 无 execute_skill、无伤害、无追击              |
| 普攻伤害 effect 概率成功且有目标                              | 无 `EXECUTED`、无 `POST_EFFECT_EXECUTE` |
| `DAMAGE_SETTLED` 且 `damage > 0`                   | 不发结算信号、不触发追击                         |
| 追击 `can_trigger_at` 通过（含 `forbid_pursuit`）        | `TRIGGER_FAIL`，无追击信号                 |


【冥锁】同时设 `forbid_basic`：普攻被拦，整条链止于 BASIC，追击自然不发生（无需单独拦追击）。

#### 成功发动事件序（同一 `timing=BASIC`）

```text
# —— 普攻段 ——
PRE_TRIGGER_CHECK          skill=basic_attack, allowed=true
TRIGGER_SUCCESS
BEFORE_BASIC_SIGNAL
BASIC_SIGNAL
PRE_EFFECT_CHECK / EFFECT_CHECK_SUCCESS
PRE_EFFECT_EXECUTE
  apply_damage
    DAMAGE_APPLIED
    DAMAGE_SETTLED（damage>0）
    dispatch_events()        → 连锁：冥河→蛇杖→追击（配置序）→ 雷霆等
POST_EFFECT_EXECUTE        skill=basic_attack, effect=basic_attack_damage

# —— 追击段（已在上方 dispatch 内完成，仍属 BASIC timing）——
PRE_TRIGGER_CHECK          skill=pursuit_strike
roll 概率
TRIGGER_SUCCESS
BEFORE_PURSUIT_SIGNAL      phase=BEFORE
PURSUIT_SIGNAL             phase=ON
execute_skill              effect=pursuit_strike_damage（目标=SAME_AS_SOURCE_EVENT，读 source_event.target_ids）
  … DAMAGE_APPLIED / DAMAGE_SETTLED（若伤害>0，可再触发 SPY，但不会再次触发追击：非 BASIC 来源）
AFTER_PURSUIT_SIGNAL       phase=AFTER
POST_TRIGGER
dispatch_events()
```

#### 失败路径

- 普攻失败：无 `DAMAGE_SETTLED`（damage>0）→ 连锁不进入追击步骤。
- 追击概率失败或 `CONTROL_FORBID_PURSUIT`：`TRIGGER_FAIL`，无 `BEFORE/ON/AFTER_PURSUIT_SIGNAL`。

#### 统计与伪随机

- `success_count` 在 `record_trigger_success` 时 +1（与即时战法相同）。
- 伪随机 key 独立：`…|pursuit_strike|*|*|SKILL_TRIGGER`（与普攻 roll 分离）。

---

### 准备型主动战法：全流程与信号

准备型战法的信号规则可以概括为：

- **进入准备**（概率成功）：只发 `BEFORE_ACTIVE_SIGNAL`（`trigger_phase=PREPARE`），**不发** `ACTIVE_SIGNAL` / `AFTER_ACTIVE_SIGNAL`。
- **释放**（吟诵进度满）：发完整 `BEFORE → ACTIVE → AFTER`（`trigger_phase=RELEASE`）。`ACTIVE_SIGNAL` **只在释放时出现**，不在进入准备时出现。

以【德尔斐蓄谕】（`prepare_rounds=1`）为例，跨回合时间线：

**回合 N — 首次 ACTIVE，概率成功，进入准备：**

```text
PRE_TRIGGER_CHECK
roll 概率                  成功 → 伪随机在此刻更新；不 record_trigger_success
BEFORE_ACTIVE_SIGNAL       phase=BEFORE, trigger_phase=PREPARE   ← 仅发「发动前」
TRIGGER_SUCCESS            payload.phase=PREPARE，含 roll_bps 等（记录「进入准备的概率成功」）
execute_skill(prepare_effect_ids)   → 施加【神谕吟诵】，prepare_ticks=0
POST_TRIGGER               trigger_phase=PREPARE, effective=false（非完整发动结束）
# 无 ACTIVE_SIGNAL / AFTER_ACTIVE_SIGNAL；success_count 不变
```

**下一回合**（`prepare_rounds=1` 时）同一英雄 `ACTIVE` — 吟诵进度已满，释放：

```text
run_timing(ACTIVE):
  handled = _advance_all_active_preparing(actor)   # 先于 skill 遍历
    对该英雄每个独立准备 state（按 source_skill_id 排序）:
      prepare_ticks += 1
      若未满: 将该 skill_id 记入 handled，结束
      若已满:
        remove_state(准备 state, reason=PREPARE_COMPLETE)
        _release_preparation_skill(skill)          # 完整 RELEASE 信号链 + release effects
        将该 skill_id 记入 handled

  for skill in skill_timing_index[ACTIVE]:
    若 skill 为准备型且 _should_skip_preparation_skill_trigger(skill, handled):
      continue                                     # 不发 PRE_TRIGGER / roll / TRIGGER_FAIL
    try_trigger_triggerable(skill)                 # 仅无准备 state 且本轮未占用的准备战法会走到这里
```

若 `prepare_rounds>1`，中间回合仅对该 state `prepare_ticks += 1` 并记入 `handled`，无 roll、无 trigger 事件；**不阻塞**其他战法的 ACTIVE 判定。

同一 ACTIVE 内释放完成后，该战法已在 `handled` 中，**不会**当场再次进入准备。

---

### 控制状态与主动战法

控制通过 `State.payload` 的 `forbid_active` / `forbid_basic` 等标志生效，由 `Skill.can_trigger_at` 读取持有者身上所有状态。

#### 1. 禁止发动（静默 / 冥锁等）

英雄已带 `forbid_active` 时，每次 `ACTIVE`：

```text
can_trigger_at → CONTROL_FORBID_ACTIVE
TRIGGER_FAIL (failure_kind=CONTROL)
不发技能信号，不 roll，不改动该战法的伪随机 streak（规则禁止路径）
```

#### 2. 准备战法占用 ACTIVE 时间片

该战法自身已持有对应准备 state，或本轮已由 `_advance_all_active_preparing` 推进/释放时：

```text
_should_skip_preparation_skill_trigger → continue
不发 PRE_TRIGGER_CHECK / TRIGGER_FAIL / 技能信号，不 roll
由 _advance_all_active_preparing 独占该战法在本轮 ACTIVE 的行为（tick 或 RELEASE）
```

其它 ACTIVE 战法（含其它准备战法、即时战法）不受影响。

`can_trigger_at` 中的 `ACTIVE_PREPARING` 仅作防御性兜底；正常流程下准备中战法不会进入 `try_trigger_triggerable`。

#### 3. 控制打断准备（吟诵中被打断）

当任意 `StateType.CONTROL` 且 `forbid_active=true` 的状态**被施加到目标身上**时（`add_state` 末尾）：

```text
_interrupt_active_preparing(target, reason=CONTROL_INTERRUPT)
  → remove_state(该英雄全部准备 state，各战法独立)
  → 日志「战法 {skill_name} 已被打断」
  → 本次准备战法不会进入释放阶段
  → success_count 不增加（从未 record_trigger_success）
```

典型场景：敌方在吟诵期间用【戈耳工凝视】施加【冥锁】；冥锁落地瞬间打断吟诵，下回合英雄因 `CONTROL_FORBID_ACTIVE` 也无法发动任何 ACTIVE。

打断与释放的区别：


|                      | 打断                  | 正常释放               |
| -------------------- | ------------------- | ------------------ |
| 移除状态原因               | `CONTROL_INTERRUPT` | `PREPARE_COMPLETE` |
| 是否执行 release effects | 否                   | 是                  |
| 是否发 RELEASE 信号       | 否                   | 是                  |
| `success_count`      | 不变                  | +1                 |


#### 4. 即时战法被控制

即时战法在 `can_trigger_at` 阶段即被 `CONTROL_FORBID_ACTIVE` 拦截，整条信号链（BEFORE→ON→AFTER）都不会出现。

若战法**已经**在 `execute_skill` 过程中对目标施加控制，目标若正在吟诵，则在该 `add_state` 时刻被打断（见上 3）。

---

### 技能信号速查表

对 BASIC / ACTIVE / PURSUIT，成功触发时可能发射：

- `BEFORE_BASIC_SIGNAL` / `BASIC_SIGNAL` / `AFTER_BASIC_SIGNAL`
- `BEFORE_ACTIVE_SIGNAL` / `ACTIVE_SIGNAL` / `AFTER_ACTIVE_SIGNAL`
- `BEFORE_PURSUIT_SIGNAL` / `PURSUIT_SIGNAL` / `AFTER_PURSUIT_SIGNAL`

**即时战法**：概率成功 → 三个信号全发 → `TRIGGER_SUCCESS` 夹在 BEFORE 与 ON 之间。

**准备型战法**：


| 阶段   | BEFORE                    | ON (`ACTIVE_SIGNAL`) | AFTER | `TRIGGER_SUCCESS.phase` | `success_count` | `POST_TRIGGER`    |
| ---- | ------------------------- | -------------------- | ----- | ----------------------- | --------------- | ----------------- |
| 进入准备 | ✓ `trigger_phase=PREPARE` | ✗                    | ✗     | `PREPARE`               | 不变              | `effective=false` |
| 释放   | ✓ `trigger_phase=RELEASE` | ✓                    | ✓     | `RELEASE`               | +1              | `effective=true`  |


规则禁止或概率失败：只发 `TRIGGER_FAIL`，不发上述技能信号。

#### 准备型战法：连锁监听约定

准备型战法一次完整发动跨越多回合，监听方**必须按阶段过滤**，不可把两段事件当成两次完整发动：


| 监听目标                   | 准备段（进入吟诵）                                 | 释放段（伤害落地）                                     | 即时战法              |
| ---------------------- | ----------------------------------------- | --------------------------------------------- | ----------------- |
| `BEFORE_ACTIVE_SIGNAL` | 只响应 `trigger_phase=PREPARE`               | 释放段另有 `trigger_phase=RELEASE`（常规「发动前」被动通常不响应） | 无 `trigger_phase` |
| `TRIGGER_SUCCESS`      | 只响应 `payload.phase=PREPARE`（含 roll，表概率成功） | `payload.phase=RELEASE`（无 roll）               | 无 `phase` 字段      |
| `ACTIVE_SIGNAL`        | **不发射**                                   | 只响应 `trigger_phase=RELEASE`                   | 无 `trigger_phase` |
| `AFTER_ACTIVE_SIGNAL`  | **不发射**                                   | 只响应 `trigger_phase=RELEASE`                   | 无 `trigger_phase` |
| `POST_TRIGGER`         | `effective=false`，**不算**战法发动完成            | `effective=true`，**唯一**有效的发动结束点               | `effective=true`  |


典型误用：监听 `POST_TRIGGER` 统计「主动战法发动次数」时，若不过滤 `effective`，会把进入准备也算一次——应只认 `effective=true`（或准备型只认 `trigger_phase=RELEASE`）。

### 结算信号

`DAMAGE_APPLIED` 与 `DAMAGE_SETTLED` 区分：

- `DAMAGE_APPLIED`：伤害已经修改兵力、阵亡、伤兵，是战报结果事件（含 damage=0）。
- `DAMAGE_SETTLED`：每次 `apply_damage` 日志后均发射，payload 记载本次实际伤害；SPY 监听并按规则过滤（蛇杖/冥河要求 damage>0，雷霆不要求）。

`HEAL_APPLIED` 与 `HEAL_SETTLED` 同理。

### 受击率模型

> **当前阶段**：受击率仅计算并写入战报，**尚未**参与伤害/治疗的目标选择或概率 roll。后续接入时，应以 `realtime_hit_rate_bps` 为权重依据。

#### 属性

每名武将有三个运行时字段（均为万分比点数，`10000 = 100%`）：


| 字段                       | 含义          | 初始值                                          |
| ------------------------ | ----------- | -------------------------------------------- |
| `initial_hit_points_bps` | 开局锁定的初始受击点数 | 5000（`HIT_RATE_INIT` 时从 `hit_points_bps` 快照） |
| `hit_points_bps`         | 当前受击点数      | 5000                                         |
| `realtime_hit_rate_bps`  | 实时受击率（归一结果） | 0                                            |


#### 全局 Timing：`HIT_RATE_INIT`

- 位于 `BATTLE_START` 之后、`PREPARE` 之前，**凌驾于所有武将战法阶段之前**。
- 对每个仍在场的武将：
  1. 快照 `initial_hit_points_bps = hit_points_bps`（默认 5000）。
  2. 按归一法计算并写入 `realtime_hit_rate_bps`。
- 此阶段**不**按兵力扣减受击点数（全员满兵开局）。

#### 受击点数公式（非累扣）

每次重算时，**被减数始终是开局初始受击点数**，不是上一时刻的累积值：

```text
损失兵力比例 = (最高兵力 - 当前兵力) / 最高兵力
扣减量       = 损失兵力比例 × 3000          # 区间 [0, 3000]
受击点数     = max(0, initial_hit_points_bps - 扣减量)
```

示例（`initial = 5000`，`max_troops = 1000`）：


| 当前兵力 | 损失比例 | 扣减   | 受击点数 |
| ---- | ---- | ---- | ---- |
| 1000 | 0%   | 0    | 5000 |
| 700  | 30%  | 900  | 4100 |
| 400  | 60%  | 1800 | 3200 |
| 0    | 100% | 3000 | 2000 |


治疗回满后受击点数回到 5000，因为是按**当前**损失比例从初始值重算。

#### 实时受击率（归一法）

对**本方仍在场**的武将集合求和作分母：

```text
场上本方武将 = alive 且 not exited 的同队成员
team_sum     = Σ 场上本方武将.hit_points_bps
实时受击率   = 自身.hit_points_bps / team_sum × 10000    # 整数除法，万分比
```

#### 重算触发点


| 触发                    | 时机              | 行为                                     |
| --------------------- | --------------- | -------------------------------------- |
| `HIT_RATE_INIT`       | 战斗开始            | 快照初始点数 + 全队归一                          |
| `DAMAGE_SETTLED`      | 信号发出后           | `damage>0` 时受击目标重算受击点数 → 同队全员重算实时受击率   |
| `HEAL_SETTLED`        | 实际治疗 > 0 且信号发出后 | 受治疗目标重算受击点数 → 同队全员重算实时受击率              |
| `HERO_EXITED_SETTLED` | 武将阵亡退出信号发出后     | **退出者移出分母**；剩余场上本方武将仅重算实时受击率（不修改其受击点数） |


兵力结算路径（`DAMAGE/HEAL_SETTLED`）伪代码：

```text
on_troop_settlement(target):
    if target.exited or not target.is_alive(): return
    target.hit_points_bps = initial - (损失比例 × 3000)   # 从 initial 重算，非累扣
    team_sum = sum(场上本方.hit_points_bps)
    for ally in 场上本方:
        ally.realtime_hit_rate_bps = ally.hit_points_bps / team_sum × 10000
    log 目标 + 同队同步日志
```

阵亡退出路径（边缘鲁棒）：

```text
mark_hero_exited(hero):
    hero.exited = true
    emit HERO_EXITED
    emit HERO_EXITED_SETTLED
    on_hero_exited_hit_rate(hero):
        remaining = 本方 alive 且 not exited
        team_sum = sum(remaining.hit_points_bps)    # 退出者不再计入
        for ally in remaining:
            ally.realtime_hit_rate_bps = ally.hit_points_bps / team_sum × 10000
        log「{hero} 退出，归一分母改为 {team_sum}（场上 N 人）」+ 各剩余武将受击率
```

注意：

- 若伤害导致同时 `mark_hero_exited` 与 `DAMAGE_SETTLED`，退出者已从 `_team_hit_rate_allies` 排除，**不再**走兵力结算重算；仅走 `HERO_EXITED_SETTLED` 路径为剩余武将归一。
- 敌方队伍不受本方退出的直接影响（分母各自独立）。

#### 战报日志格式

```
[受击率·初始化] A-副1 实时受击率=3333 (3333=5000/15000*10000)
[受击率·DAMAGE_SETTLED] A-副2 实时受击率=... (公式) 受击点数 5000->4100 (初始5000-(900)=4100, 扣减=(损失300/1000)*3000=900)
[受击率·HERO_EXITED_SETTLED] B-副将 退出，归一分母改为 9100（场上 2 人）
[受击率·HERO_EXITED_SETTLED] B-主将 实时受击率=5494 (5494=5000/9100*10000)
```

实现文件：`battlecore/engine/hit_rate.py`（公式）、`battlecore/engine/battle_context.py`（触发与日志）。

## 七、伪随机动态概率

所有概率点统一通过 `BattleContext.roll_pseudo_random_probability`。

目标：

- 保留概率不确定性。
- 降低连续失败挫败感。
- 限制连续成功爆发。
- 长期统计尽量接近面板概率。
- 服务端可复现、可审计、可写日志。

### 动态概率公式

```text
currentRate = clamp(
    baseRate + failCount * bonusPerFail - successStreak * penaltyPerSuccess,
    minRate,
    maxRate
)
```

如果 `failCount >= guaranteeCount`，本次强制成功，原因记为 `GUARANTEE_TRIGGER`。

成功后：

- `failCount = 0`
- `successStreak += 1`

失败后：

- `failCount += 1`
- `successStreak = 0`

### 状态隔离 Key

```text
battleId|casterId|skillId|effectId|targetId|triggerType
```

隔离效果：

- 不同技能互不污染。
- 不同 effect 互不污染。
- 不同目标互不污染。
- SPY state 按**状态持有者**与目标独立累计（每名英雄各自的 fail/success streak）。

### 必定触发

`baseRate >= 10000` 时：

- 不 roll。
- 不消耗 RNG。
- 不创建或更新伪随机状态。
- 日志显示 `reason=ALWAYS_TRIGGER`。

### 规则禁止

`can_trigger_at` 禁止时：

- 不 roll。
- 不更新伪随机状态。
- 日志只显示规则原因，例如 `CONTROL_FORBID_BASIC`。

## 八、伤害 / 治疗 / 伤兵模型

所有公式使用整数万分比。

### 基础值与结算层

**默认原则：技能描述只写基础值；暴击与 modifiers 在结算层统一处理。**

除非技能描述或配置 payload 明确声明例外，所有伤害/治疗都分两层：


| 层级        | 职责                   | 典型来源                                                                                                             |
| --------- | -------------------- | ---------------------------------------------------------------------------------------------------------------- |
| **技能基础层** | 系数、固定分量、专用公式         | `EffectConfig.coefficient_bps`、State payload（如 `heal_max_troop_bps`）、技能注释中的自然语言公式                                |
| **结算层**   | 暴击、随机系数、属性修正、增减伤/增减疗 | `BattleContext.apply_damage` / `apply_heal` 调用 `calc_damage` / `calc_heal` / `apply_heal_settlement_adjustments` |


示例：

- **普攻 / 戈耳工伤害 Effect**：描述中的「100% 系数」是基础；兵力系数、攻防差、增减伤、暴击在 `calc_damage` 结算。
- **【蛇杖庇护】**：描述「1% 最大兵力 + 1×神谕持有者智力」为基础量（`calc_snake_staff_base_heal`）；治疗暴击与治疗增减在 `apply_heal` 结算，不在基础公式里重复。
- **【雷霆】落雷**：`damage_coefficient_bps=10000` 为基础倍率；完整伤害模型在 `apply_damage` 的 state 路径经 `calc_damage` 结算。

例外机制：State payload 可设 `skip_heal_modifiers: true` 等显式开关，仅在该技能/状态描述中注明时使用。

### 伤害

```text
Damage =
    round(
        (BaseDamage + AttrDiff × AttrDiffCoef)
        × TroopCoef
        × DamageUpCoef
        × DamageReduceCoef
        × VulnerableCoef
        × RestrainCoef
        × RandomCoef
        × CritCoef
        × skill_rate
    )
    + FixedExtraDamage

BaseDamage = BASE_DAMAGE（默认 390，与兵力无关）
skill_rate = coefficient_bps / 10000
AttrDiff = map(raw) 其中 raw = AttackAttr - DefenseAttr
  - 正差：1:1（raw ≥ 1000 时安全截顶为 +1000）
  - 负差：raw ≥ -30 时 1:1；(-200,-30] 分段压缩；raw ≤ -200 → -45
AttrDiffCoef = ATTR_DIFF_COEF（当前默认 8）
TroopCoef = 0.4 + 0.6 × (current_troops / MAX_TROOPS)，MAX_TROOPS 为全局配置（默认 10000），不截断
```

#### 伤害公式各项 Clamp


| 项                            | 说明                                        | Clamp / 边界                                                      |
| ---------------------------- | ----------------------------------------- | --------------------------------------------------------------- |
| `skill_rate`                 | `coefficient_bps / 10000`                 | 无，由配置给定                                                         |
| `BaseDamage`                 | `BASE_DAMAGE`（默认 390）                    | 无                                                               |
| `AttackAttr` / `DefenseAttr` | 有效武力/智力/统率（ATTR 状态修正后）                    | ≥ 0                                                             |
| `raw`                        | `AttackAttr − DefenseAttr`（真伤时 Defense=0） | 映射前无 clamp                                                      |
| `AttrDiff`                   | `_map_attr_diff(raw)`                     | 正差：raw≥1000 → +1000；负差：≥−30 为 1:1，(−200,−30] 锚点分段插值，≤−200 → −45 |
| `AttrDiffCoef`               | 固定 8                                      | 无                                                               |
| `CoreDamage`                 | `BaseDamage + AttrDiff×8`                 | ≥ 0                                                             |
| `TroopCoef`                  | `0.4 + 0.6×current/MAX_TROOPS`            | **不截断**；`ignore_troop_coef` 时固定 1.0                             |
| `DamageUpCoef`               | `1 + damage_up_bps/10000`                 | 增伤 bps ∈ [0, 10000]（+100%）                                      |
| `DamageReduceCoef`           | `1 − damage_reduce_bps/10000`             | 减伤 bps ∈ [0, 8000]（−80%）                                        |
| `VulnerableCoef`             | `1 + vulnerable_bps/10000`                | 易伤 bps ∈ [0, 10000]（+100%）                                      |
| `RestrainCoef`               | 兵种克制等外部传入                                 | ≥ 0，无上限                                                         |
| `RandomCoef`                 | 确定性 RNG，默认 1.0                            | ∈ [0.95, 1.05]                                                  |
| `CritCoef`                   | 默认 1.0，暴击 2.0                             | ≥ 0，无上限                                                         |
| `FixedExtraDamage`           | 固定追加伤害                                    | 无                                                               |
| **最终伤害**                     | 万分比连乘取整后                                  | ≥ 1（`MIN_DAMAGE`）；≤ 目标当前兵力；目标兵力≤0 返回 0                          |


`apply_damage` 结算层（非公式倍率）：


| 项               | Clamp                            |
| --------------- | -------------------------------- |
| `actual_damage` | `min(理论伤害, current_troops)`，≥ 0  |
| `dead_ratio`    | 默认 3000 bps（30% 阵亡），∈ [0, 10000] |


核心参数：

- `skill_rate_bps`
- `ATTR_DIFF_COEF`（属性差固定伤害系数）
- 当前兵力系数 `TroopCoef`
- damage up / damage reduce / vulnerable
- restrain coef
- deterministic random coef
- fixed extra damage

伤害会拆分为：

- `dead_troop`
- `wounded_troop`

默认：

- 30% 阵亡。
- 70% 伤兵。

回合开始伤兵损耗（`ROUND_START`）：

- 每回合 `ROUND_STARTED` 之后、`run_timing(ROUND_START)` 时，**仍在场**的全体武将统一将自身 `wounded_troop` 的 **30%**（`WOUNDED_TO_DEAD_RATIO_BPS=3000`）转为 `dead_troop`。
- **已阵亡（`exited`）或兵力为 0 的武将不参与**此结算。
- `current_troop` 不变；仅伤兵池减少、死兵池增加。
- 与受伤瞬间的 `DEAD_RATIO_BPS` 拆分独立：前者是「回合开始自然损耗」，后者是「受击瞬间」的阵亡/伤兵分配。

回合开始有效四维（`ROUND_START` 收尾）：

- `run_timing(ROUND_START)` 处理完伤兵转死兵与 ROUND_START 状态/技能后，打印 `EffectiveAttrs` 表。
- 仅包含未 `exited` 的武将；武力/智力/统率/敏捷取 `get_effective_attr`（本体 + ATTR 状态 `*_delta` / `*_bps`）。
- 战报表头英文为 **Speed / Might / Hex / Guard**（中文仍为敏捷/武力/智力/统率）；内部字段名不变（`speed` / `force` / `intelligence` / `command`）。

武将阵亡（`exited`）：

- 兵力归零时 `mark_hero_exited`：标记退出、禁用其全部战法。
- **立即移除**该武将身上的一切状态，以及 `source_actor_id == 其 instance_id` 时在他人身上施加的状态（含 ATTR / SPY / 控制等）。**不按** `source_skill_id` 配置 id 全局匹配。
- 发出 `HERO_EXITED` 与 `HERO_EXITED_SETTLED`；清理在 `HERO_EXITED` 时同步完成。
- `apply_damage` 中若本次伤害致死，**先** `mark_hero_exited`，**再** 发出 `DAMAGE_SETTLED` 并 `dispatch_events`。因此同一击触发的 SPY（如【雷霆】）看到的目标若已阵亡，会在 state 执行前判定目标无效并打日志跳过，不再追伤。
- 阵亡后不参与行动、不响应事件、不在回合开始参与伤兵转死兵。

### 治疗

治疗只能恢复伤兵：

- 不复活阵亡。
- 不超过最大兵力。
- 使用智力属性系数。
- 可受治疗提升 / 受到治疗提升 / 治疗降低影响。

### ATTR 属性修正

伤害 / 治疗读取有效属性时，汇总 `StateType.ATTR` 的 payload。

支持：

- `force_delta`
- `intelligence_delta`
- `command_delta`
- `speed_delta`
- `force_bps`
- `intelligence_bps`
- `command_bps`

非 ATTR 状态即使带有同名 payload，也不会进入属性汇总模型。

## 九、当前已实现技能

### Basic Attack

- BASIC 阶段触发。
- 必定触发。
- 对随机敌方单体造成 PHYSICAL 伤害。
- 使用 force vs command。

### 戈耳工凝视

- ACTIVE 阶段触发。
- 面板概率 35%。
- 对敌方两名单体分别造成 MAGIC 伤害。
- 每个目标独立 45% 概率施加【冥锁】。
- 【冥锁】禁止 ACTIVE 和 BASIC。
- 控制状态按目标 `BEFORE_ACTION` 计数。

### 德尔斐启示

- PREPARE 阶段触发。
- 己方全体获得【神示】。
- 【神示】是 ATTR 状态。
- 四维属性 +10。
- 持续整场战斗。

### 阿斯克勒庇俄斯圣谕

- PREPARE 阶段触发。
- 己方全体获得【蛇杖庇护】。
- 【蛇杖庇护】是 SPY 状态，监听 `DAMAGE_SETTLED`。
- 携带者受伤后按 40% 动态概率治疗。
- **基础治疗量** = 受击者最大兵力 1% + 神谕持有者智力 × 1（`heal_source_intelligence_bps=10000`）。
- 治疗暴击与治疗增减在结算层处理（见「基础值与结算层」）。
- 治疗只能恢复伤兵。

### 雷霆神谕

- PREPARE 阶段触发。
- 己方全体获得【雷霆】。
- 【雷霆】是 SPY 状态，监听 `DAMAGE_SETTLED`。
- 携带者造成任意非落雷伤害后，60% 概率对本次受击目标追加一次【落雷】。
- 【落雷】是 MAGIC 伤害。
- 默认伤害系数 100%，可配置 `damage_coefficient_bps`。
- 伤害计算使用【雷霆神谕】持有者的 intelligence。
- 落雷不会再次触发落雷，避免无限递归。

### 德尔斐蓄谕

- ACTIVE 阶段触发，面板概率 50%，准备 1 回合。
- 独立准备 state【神谕吟诵】（`delphi_charged_preparing_state`）。
- 准备完成时对敌军单体造成 300% MAGIC 谋略伤害（施法者智力）。
- 准备机制见「四、4.5 准备型主动战法」；控制（`forbid_active`）可打断准备。

### 皮提亚筹谋

- ACTIVE 阶段触发，面板概率 50%，准备 1 回合。
- 独立准备 state【筹谋酝酿】（`pythia_woven_preparing_state`），与【德尔斐蓄谕】互不混用、互不阻塞。
- 准备完成时对敌军单体造成 250% MAGIC 谋略伤害（施法者智力）。
- 准备与控制规则同准备型主动战法通用约定。

### 突击（追击战法）

- 类别 `PURSUIT`；**无** `trigger_timings`，不进入 `skill_timing_index`，无独立 `PURSUIT` 行动窗口。
- 连锁触发：`DAMAGE_SETTLED` 后 `dispatch_events` 中 SPY 组第 4 步（冥河→蛇杖→雷霆→**追击**）；`damage>0` 且为普攻 SETTLED 信号；仍在同一 `BASIC` 时间片内。
- 面板概率 100%；对**本次普攻 DAMAGE_SETTLED 的 target_ids[0]** 追加 50% 武力 PHYSICAL 伤害（`SAME_AS_SOURCE_EVENT`）。
- 普攻被 `forbid_basic`（如【冥锁】）拦截 → 追击不触发；追击自身仍受 `forbid_pursuit` 控制。
- 信号与事件流见「四、4.7」与「六、追击战法机制」。

## 十、胜负和退出

Hero 不会从列表中删除，只会标记：

- `exited=True`
- `exit_round`
- `exit_reason`

主将退出：

- 立即触发 `MAIN_HERO_EXITED`。
- 战斗结束。
- 对方获胜。

最大回合结束：

- 按双方剩余总兵力判胜。
- 相同则 DRAW。

## 十一、测试与输出

测试要求：

- 可通过 `pytest` 全量运行。
- 单个测试文件可直接运行。
- 控制台打印 human logs。
- 输出文本保存到 `tests/output/`。

当前重点测试：

- 普攻确定性。
- 事件流可序列化。
- 主将退出胜负。
- 戈耳工凝视多段 effect、目标关联和控制持续。
- 伤害 / 治疗 / 伤兵模型。
- ATTR 属性状态影响数值。
- 神谕技能与 SPY 状态。
- 雷霆神谕落雷追击与防递归。
- batch 队伍强度、技能概率、SPY state 概率统计。

## 十二、扩展规范

新增技能建议流程：

1. 在 `skill_files.py` 先写自然语言描述。
2. 用 `SkillConfig` 表达触发 timing、概率、effect 顺序。
3. 用 `EffectConfig` 表达原子效果。
4. 用 `StateConfig` 表达状态类型、监听事件、payload。
5. 在 `config_db.py` 注册 skill / effect / state。
6. 为技能写独立测试。
7. 新增 SPY / REGULAR 响应或多实例同事件排序：在 `chain_reaction_config.py` 声明相对顺序（见 [STATE_RESPONSE_REFERENCE.md](./STATE_RESPONSE_REFERENCE.md)）。
8. 如果新增数值修正，优先使用 `StateType.ATTR` + `trigger_mode=NONE`。
9. 如果新增概率，必须走统一伪随机系统。
10. 如果新增递归型连锁，必须在监听过滤中明确防止自触发循环。
11. **同步更新 `DESIGN_V2.md` 第四节主流程伪代码**（及第六节事件协议、第九节技能说明等受影响章节）。

## 十三、当前边界

当前版本仍是原型，不包含：

- 完整配置外部加载器。
- 多队伍或多阵营。
- 地形、兵种、距离、怒气等系统。
- 复杂 DOT / HOT 周期伤害。
- 控制抗性递增模型。
- 服务端数据库落盘。
- 客户端表现层。

但当前架构已经为这些扩展预留了入口：

- 配置可迁移到 JSON / YAML / Excel。
- 事件流可落盘用于回放。
- RNG history 可审计。
- State SPY / REGULAR 机制可扩展更多响应。
- ATTR 状态可扩展更多数值修正。
- Effect 顺序执行可支持复杂技能脚本。

