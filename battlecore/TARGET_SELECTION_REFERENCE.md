# 目标选择参考

> 说明 Effect 执行时的目标选取机制。设计总纲见 [DESIGN_V2.md](./DESIGN_V2.md) §1.2⑥。
> 选人只发生在 **Effect 层**；Skill 层仅负责 `choose_actor`（施法者），不直接选人。

实现入口：

| 函数 | 文件 | 职责 |
|------|------|------|
| `resolve_effect_targets` | `battle_context.py` | Effect 选目标总入口（含 params 覆盖） |
| `select_targets` | `battle_context.py` | 按 `TargetPolicy` 从候选池选人 |
| `_select_random` | `battle_context.py` | 随机类策略 |
| `_select_by_troop_ratio` | `battle_context.py` | 按兵力比例排序 |

配置字段：`EffectConfig.target_policy`、`EffectConfig.target_count`（默认 `RANDOM_ENEMY` / `1`）。

---

## 1. 执行流程

```
execute_skill
  └─ for each effect（按配置顺序）
       ├─ resolve_effect_targets(actor, effect, runtime_cache)
       │    ├─ params 覆盖？→ 直接读缓存目标
       │    └─ 否则 → select_targets(actor, target_policy, target_count, runtime_cache)
       ├─ _validate_effect_targets(targets)   # 阵亡 / 兵力为 0 → INVALID_TARGET
       ├─ roll_probability → execute_effect
       └─ 写入 runtime_cache（previous_effect_targets、别名、普攻目标缓存等）
```

选人完成后发出 `TARGET_SELECTED` 事件（payload 含 `target_policy`、`target_count`）。

---

## 2. 候选池通用规则

所有「从队伍里挑人」的策略，候选人均来自：

```text
get_alive_heroes(team_id) 且 instance_id ∉ exclude_target_ids
```

| 条件 | 说明 |
|------|------|
| `is_alive()` | `troops > 0` 且 `not exited` |
| `exclude_target_ids` | 由 Effect `params.exclude_effect_aliases` 注入，仅本次 `select_targets` 调用有效 |
| 排序 tie-break | 兵力比例类按 `(兵力比, position, instance_id)`；随机类先按 `(position, instance_id)` 定序再抽样 |

**不在候选池内的人**：已阵亡退出（`exited`）、兵力为 0、被 `exclude_effect_aliases` 排除。

---

## 3. TargetPolicy 一览

### 3.1 自身

| 策略 | 行为 |
|------|------|
| `SELF` | 施法者本人（若仍存活） |

### 3.2 己方 — 确定性

| 策略 | 行为 |
|------|------|
| `ALLY_ALL` | 己方全部存活武将，取前 `target_count` 个（队伍注册顺序） |
| `ALLY_LOWEST_TROOPS` | 己方兵力比例**最低**的 `target_count` 人 |
| `ALLY_HIGHEST_TROOPS` | 己方兵力比例**最高**的 `target_count` 人 |

兵力比例：`troops * 10000 // max_troops`（整数万分比）。

### 3.3 己方 — 随机

| 策略 | 行为 |
|------|------|
| `ALLY` | 从己方存活候选中**均匀随机**（无放回），最多 `target_count` 人 |
| `RANDOM_ALLY` | 与 `ALLY` **实现相同**（均匀随机） |

### 3.4 敌方 — 确定性

| 策略 | 行为 |
|------|------|
| `ENEMY_ALL` | 敌方全部存活武将，取前 `target_count` 个 |
| `ENEMY_LOWEST_TROOPS` | 敌方兵力比例最低的 `target_count` 人 |
| `ENEMY_HIGHEST_TROOPS` | 敌方兵力比例最高的 `target_count` 人 |

### 3.5 敌方 — 随机（受击率加权）

| 策略 | 行为 |
|------|------|
| `ENEMY` | 从敌方存活候选中按**实时受击率加权随机**（无放回） |
| `RANDOM_ENEMY` | 与 `ENEMY` **实现相同** |

权重：直接读取各候选 `hero.realtime_hit_rate_bps`（由 `HIT_RATE_INIT` / `DAMAGE_SETTLED` / `HEAL_SETTLED` / `HERO_EXITED_SETTLED` 维护，选人时**不重算**）。

多目标时：每轮从剩余池中再抽一人，仍用各人当前已维护的 `realtime_hit_rate_bps`。

权重全为 0 时：`DeterministicRNG.rand_weighted_index` 退化为均匀随机。

战报日志示例：

```text
[选人·RANDOM_ENEMY] A-Main 选中 候选权重: B-Main=6849 | B-D1=3150 → B-Main
```

### 3.6 缓存 / 关联目标

| 策略 | 行为 |
|------|------|
| `SAME_AS_PREVIOUS_EFFECT` | 读本技能本次 `execute_skill` 内**上一个已执行 Effect** 的目标列表；为空则回退 `RANDOM_ENEMY` |
| `SAME_AS_SOURCE_EVENT` | 读本次 `execute_skill` 传入的 `source_event.target_ids`（追击连锁用）；无则空列表 |

**同技能内上一 Effect 目标**（`runtime_cache["previous_effect_targets"]`）：

- 每个 Effect 执行成功后更新
- 仅在同一次 `execute_skill` 调用内有效

---

## 4. Effect params 覆盖（不走 TargetPolicy）

除 `target_policy` 外，可通过 `Effect.params` 直接引用已选目标：

| params 键 | 作用 |
|-----------|------|
| `target_from_effect_alias` | 读取 `store_targets_as` 别名对应的目标列表 |
| `target_from_effect_id` | 按 effect `config_id` 读取本次技能内该 Effect 曾选中的目标 |
| `exclude_effect_aliases` | 将别名对应目标的 `instance_id` 加入 `exclude_target_ids`，再执行 `target_policy` |
| `store_targets_as` | 本 Effect 执行成功后，把目标列表存为别名供后续 Effect 引用 |

典型用法（见 `DESIGN_V2.md` 多段技能）：第一段 `RANDOM_ENEMY` + `store_targets_as: "main"`；第二段 `SAME_AS_PREVIOUS_EFFECT` 或 `target_from_effect_alias`；第三段 `exclude_effect_aliases: ["main"]` + `RANDOM_ENEMY`。

---

## 5. 选人后的校验

`select_targets` **不**过滤已阵亡缓存目标；校验在 `_validate_effect_targets`：

| 情况 | 结果 |
|------|------|
| 目标 `exited` | `INVALID_TARGET`（如「XX已阵亡」） |
| 目标 `troops <= 0` | `INVALID_TARGET` |
| 全部无效 | Effect 不执行，发 `EFFECT_CHECK_FAIL` |
| 部分无效 | 当前实现：任一无效则整段 Effect 失败（非只打有效目标） |

`SAME_AS_SOURCE_EVENT` 指向已阵亡目标时：仍会选中该 Hero 对象，随后在验证阶段失败并打日志（不执行 `execute_effect`）。

---

## 6. 随机与确定性对比

```text
                    ┌─ 均匀随机 ── ALLY / RANDOM_ALLY
         己方 ──────┤
                    └─ 确定性 ─── ALLY_ALL / ALLY_LOWEST_TROOPS / ALLY_HIGHEST_TROOPS

                    ┌─ 受击率加权随机 ── ENEMY / RANDOM_ENEMY
         敌方 ──────┤
                    └─ 确定性 ─── ENEMY_ALL / ENEMY_LOWEST_TROOPS / ENEMY_HIGHEST_TROOPS

         自身 ─────── SELF

         关联 ─────── SAME_AS_PREVIOUS_EFFECT / SAME_AS_SOURCE_EVENT
                      （失败回退 RANDOM_ENEMY）
```

| 维度 | 均匀随机（己方） | 受击率加权随机（敌方） | 兵力比例 | 全员 / 自身 / 缓存 |
|------|------------------|------------------------|----------|-------------------|
| 是否用 RNG | 是 | 是 | 否 | 否（缓存策略回退时用 RNG） |
| 权重来源 | 等权 | `realtime_hit_rate_bps` | `troops/max_troops` | — |
| 多目标 | 无放回 | 无放回 | 取排序前 N | 列表截取或单人 |

RNG 均走 `DeterministicRNG`，与战斗 `seed` 可复现。

---

## 7. 配置示例（仓库内）

| 技能 / 场景 | target_policy | 说明 |
|-------------|---------------|------|
| 普攻、多数伤害 | `RANDOM_ENEMY` | 受击率加权随机敌方 |
| 追击 `pursuit_strike` | `SAME_AS_SOURCE_EVENT` | 触发连锁的 `DAMAGE_SETTLED.target_ids` |
| 戈耳工凝视第二段 | `SAME_AS_PREVIOUS_EFFECT` | 同技能上一 Effect 目标 |
| 神示 / 冥河誓约 | `ALLY_ALL` | 己方全体 |
| 哈迪斯指挥吸取 | `SELF` | 自身 |
| 德尔斐神示等 | `SELF` + 后续 `RANDOM_ENEMY` | 先 buff 自己再打敌方 |

---

## 8. 相关文档

- 受击率计算与维护：`DESIGN_V2.md`「受击率模型」、`hit_rate.py`
- 事件审计：`EVENT_SIGNAL_REFERENCE.md` → `TARGET_SELECTED`
- 多段 Effect 目标关联：`DESIGN_V2.md` Effect `params` 与追击节
