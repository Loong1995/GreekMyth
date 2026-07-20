# 响应 / 触发序（response_order）

> 权威清单：同一次结算内「谁先响应、同人身上谁先触发」。
> 实现：`battle/engine.py`（`_owner_hook_key` / `_global_hook_key` /
> `_dispatch_damage_hooks` 等）；确定性表见 [determinism.md](determinism.md) §2；
> 状态钩子语义见 [statuses.md](statuses.md) §8。
> 客户端播放序跟随事件流，不另造排序（[performance_mechanisms](../client/performance_mechanisms.md)）。

## 1. 一句话总则

1. **A 对 B 造成实际伤害后**：先整段守方 `on_damage_taken`，再整段攻方
   `on_damage_dealt`（先守后攻）。
2. **同一持有者**身上多条触发：`source_id ≠ owner_id`（他人施加，如队友神谕）
   整段先于 `source_id == owner_id`（自身施加）；各段内再按
   `response_priority` 升序 → `instance_id`。
3. **跨持有者**全局钩子：先 `response_priority`，再持有者 `hero_order`，再他人/自身层，
   再 `instance_id`。

## 2. 伤害结算点（最常用）

```
deal_damage 落账（amount>0、非 mitigation、非 special）
 └─ _dispatch_damage_hooks
     ├─ 守方 B：全部 on_damage_taken   ← _owner_hook_key
     └─ 攻方 A：全部 on_damage_dealt   ← _owner_hook_key
```

例：宙斯给阿喀琉斯挂【雷霆】，阿喀琉斯打赫拉克勒斯且赫有【十二试炼】——
先试炼（守方 taken），再雷霆（攻方 dealt，且雷霆为他人施加，优先于阿喀琉斯自身追伤标记）。

减免成功（格挡/闪避/反弹）**不分发**伤害响应；反弹伤害为 special，不连锁。

## 3. 分发点一览

| 分发 | 键 | 代码 |
|---|---|---|
| 伤害 taken / dealt | 先守后攻；各段 `_owner_hook_key` | `_dispatch_damage_hooks` |
| 行动开始 `on_action_start` | `_owner_hook_key` | `_dispatch_action_start` |
| 伤前 `on_pre_damage_dealt` | `_owner_hook_key` | `deal_damage` 公式段 |
| 受控 `on_control_taken` | `_owner_hook_key` | `apply_status` |
| 回合起/末 `on_round_*` | `_global_hook_key` | `_dispatch_round_hooks` |
| 协击 `on_ally_basic_attack` | `_global_hook_key`（性格钩子先于状态） | `_dispatch_ally_basic_attack` |
| 施加 `on_status_inflicted` | `_global_hook_key`；防递归 | `apply_status` |
| 阵亡 `on_hero_defeated` | `_global_hook_key` | `_handle_defeat` |

他人/自身层：`_source_tier` —— `source_id` 非空且 ≠ `owner_id` → 0（他人）；否则 → 1（自身）。

## 4. 常用 response_priority（升序先响应）

| priority | 状态（示例） | 钩子侧 |
|---|---|---|
| 5 | 木马炸弹 | action_start |
| 10 | 冥河血誓 / 幽影 | dealt / action |
| 15 | 埃癸斯圣盾 | taken（治疗） |
| 20 | 蛇杖庇护 | taken |
| 25 | 阿喀琉斯之怒 | dealt |
| 30 | 雷霆 / 扰心印记 | dealt / action |
| 35 | 疾风追击 / 船费 | dealt |
| 40 | 海神震荡 / 双子协战 | dealt / ally_basic |
| 45~50 | 凝视 / 试炼 / 狮皮 | taken |

同侧、同源时 priority 仍生效；**他人层整段压过自身**，即使自身 priority 更小。

## 5. 与播放 / 结算的关系

- **播放**：`status_tick` 等响应事件按引擎写出序进入战报；客户端
  `ReactionRegroup` 后置拆组，**不重排**多方响应相对序。
- **结算表**：带技能（神谕挂状态）杀伤归施法者技能格——见
  [settlement_stats.md](../client/settlement_stats.md) 与
  [hero_specials.md](hero_specials.md) §结算归因。
- **性格台词时点**（非状态钩子）：见 [hero_specials.md](hero_specials.md) §性格台词。

## 6. 测试

- `battle/tests/test_damage_hooks_order.py`：先守后攻；他人施加先于自身；
  行动开始同规。
