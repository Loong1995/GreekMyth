# 状态系统（statuses）

> 一等公民建模（任务书 4.4/5.3）。实现：`battle/statuses.py`（模型）+
> `battle/engine.py`（施加/移除/计次/tick 原语）。Step B2 落地，B3 扩展
> （响应钩子/动态修正/标准控制建造器）。

## 1. 模型

- **StatusDef**（静态定义）：`status_id`、`kind`（buff/debuff/control/special）、
  `duration_rounds`（-1=整局）、`max_stacks`、`refreshable`、`modifiers`、
  `dot_rate_bps` / `hot_rate_bps`；B3 增：响应钩子（§8）、`response_priority`、
  `tick_at_window_end`（犹豫专用计次时点）、`payload`（钩子自定参数）。
- **StatusInstance**（运行时）：`instance_id`（本局内唯一、从 1 递增）、`owner_id`、
  `source_id`（施加来源，随刷新更新为最新施加者）、`stacks`、`action_tick_count`；
  B3 增：`counters`（局内计数，如试炼层数）、`round_counters`（每回合清零，
  如落雷/怒击次数上限）、`dynamic_modifiers`（运行期修正，如冥界支配吸取的统率）。
- 存储在引擎战时记账 `SeriesEngine.statuses`（hero_id → 施加序列表），**随局清空**
  （game_end 语义，不逐条发事件）。

## 2. 施加/刷新/叠层规则（互斥默认规则，任务书 5.3）

| 情形 | 行为 | 事件 |
|---|---|---|
| 目标无同 id 状态 | 新建实例 | `status_apply` |
| 已存在 + 可叠加且未满层 | stacks+1，计次归零 | `status_refresh` |
| 已存在 + 不可叠加但可刷新 | 计次归零 | `status_refresh` |
| 已存在 + 负面默认（不可刷新不可叠加） | **静默拒绝** | 无（契约省流量规则 2） |
| 目标已阵亡 | 拒绝 | 无 |

- 默认规则：`kind in (debuff, control)` → 不可刷新不可叠加；buff/special → 可刷新。
  `refreshable` 显式设置可覆盖默认。

## 3. 持续时间语义

- **行动窗口计次**（与旧 core BEFORE_ACTION 一致）：状态持有者自己的行动窗口开始时
  `action_tick_count += 1`，`> duration_rounds` 即到期移除（`status_remove`,
  reason=expired，挂当次 `action_start` 之下）。
- 「持续 1 回合」= **至少覆盖持有者下一次行动窗口**：第一次计次（=1）不超限，
  该窗口内控制仍生效；第二次计次（=2）到期。
- `duration_rounds = -1`：整局有效，不计次，局末随语义清空。

## 4. 数值修正聚合

- 有效属性 = `max(0, (基础 + Σ平加) × (1 + Σ百分比))`，先平加后百分比（旧 core 分层）。
- 全部修正键见 `battle/statuses.py` 文件头注释；同键跨状态求和，可叠加状态按层数放大；
  `forbid_*` 布尔禁制不乘层数。**动态修正**（`dynamic_modifiers`，B3）与静态 modifiers
  同键相加聚合，但不乘层数（数值已是运行期实值）。
- 类型专属增/减伤（`physical_damage_up_bps` 等）与通用键（`damage_up_bps` 等）相加后
  进入伤害主公式对应乘区（乘区内 clamp 仍按公式全局上限）。B3 增键：
  类型专属易伤/暴击率（`physical_vulnerable_bps`、`physical_crit_rate_bps` 等）、
  吸血（`lifesteal_bps` / `physical_lifesteal_bps`）、连击率（`combo_rate_bps`）。
- Phase 3 增键：`evade_bps`（闪避几率）、`block_rate_bps`（几率型格挡）、
  `extra_damage_up_bps`（独立额外增伤乘区）、`crit_damage_up_bps`（会心/奇谋伤害抬升）、
  `petrify_immune`（石化免疫布尔）、`lock_lowest_target`（选人锁定最低兵力）。
  次数型格挡走 `block` 状态的 `counters["block_charges"]` 计数。

## 5. DoT / HoT

- 每回合 `ROUND_START` 相位、伤兵损耗之后 tick：遍历序 = hero_order × 施加序。
- 事件形态：`status_tick`（挂 round_start 组下）+ 子 `damage`/`heal`。
- DoT 走魔法伤害主公式（来源智力 vs 持有者智力，系数=dot_rate_bps），**不暴击**、
  吃随机系数；可致死（主将被毒死即局终）。HoT 同理走治疗主公式。
- 来源阵亡 → 状态已被清理（见 §6），不存在无主 DoT。

## 6. 阵亡清理（任务书 5.5）

武将兵力归零 `hero_defeated` 后：
1. 其施加给**其他武将**的全部状态立即删除，逐条发 `status_remove`
   （reason=source_defeated，挂 hero_defeated 之下）；
2. 其**自身携带**的状态静默清空（武将已离场，无播放意义）；
3. 阵亡者不可成为目标、不再行动、不可被治疗（不复活）、不可再被施加状态；
4. （B3）其准备中战法与延迟行动登记一并静默作废。

专项边界测试：`battle/tests/test_death_cleanup.py`。

## 7. 标准控制状态（B3，`battle/statuses.py` 建造器）

| 建造器 | 禁制 | 附带 | 备注 |
|---|---|---|---|
| `silence(n)` 缄默 | forbid_active | — | 施加即打断准备中战法 |
| `disarm(n)` 缴械 | forbid_basic | — | 禁普攻即禁追击；不打断准备 |
| `ming_lock(n)` 冥锁 | forbid_active + forbid_basic | — | 全禁；打断准备 |
| `petrify(n)` 石化 | forbid_active + forbid_basic | `vulnerable_bps +1000`（D-01） | 全禁 + 受伤 +10%；打断准备 |
| `hesitation(rate, n)` 犹豫 | —（特殊） | 刷新不叠层、固定延后 1 回合、计次统一前移（Phase 3） | 细则见 hesitation.md |
| `block(n)` 格挡 | —（Phase 3） | `counters["block_charges"]` 次数型 0 结算 | 消耗 1 次伤害置 0；damage.md §五 |
| `charm(n)` 魅惑 | —（Phase 3） | 选敌敌我不分（charm_targeting） | 塞壬魅惑术 |

控制不冻结 DoT/HoT；交互矩阵见 status_interactions.md。

## 8. 状态响应钩子（B3，事件驱动机制的载体）

- `StatusDef` 可挂钩子：`on_apply`（施加成功后）、`on_damage_dealt`
  （持有者造成伤害结算后）、`on_damage_taken`（持有者受到伤害结算后）、
  `on_action_start`（持有者行动窗口开始）；Phase 3 增：`on_round_start` /
  `on_round_end`（回合级）、`on_hero_defeated`（任意阵亡）、`on_control_taken`
  （持有者被施加控制）、`on_pre_damage_dealt`（伤害数字确定前修正 ctx）。
- 伤害响应分发（`_dispatch_damage_hooks`）：一次 damage 结算后，先遍历攻方
  `on_damage_dealt` 持有实例、再守方 `on_damage_taken`，各按
  `(response_priority, hero_order 序, instance_id)` 升序（determinism.md §2）。
- 钩子拿到 `DamageContext`（来源/目标/类型/kind/是否暴击/实际量/事件 seq），
  以 damage 事件为 parent 产出子结算；响应产生的 skill_trigger/status_tick
  **自成新组**（契约连锁跨组）。
- **防递归**：钩子内部造成的伤害传 `dispatch=False` 或用 `kind` 过滤
  （如落雷只响应 kind∈{basic,skill}），配合 `round_counters` 上限双保险。
- 典型应用：雷霆神谕落雷、蛇杖庇护回复、阿喀琉斯之怒暴击追伤、美杜莎凝视反制、
  十二试炼成长——见 `docs/skills/`。

## 9. 属性修改（attr_change）与状态的分工

- **临时修正**（随状态生灭）：走状态 `modifiers`，不改基础面板，`scope=temporary`。
- **本局修改**（单挑败者四维-10、削弱类）：`modify_attr(scope=game)` 直改基础面板并
  事件化，引擎记账、**局末自动回滚**（不发事件，game_end 语义覆盖）。
- **系列修改**：`scope=series`，不回滚（预留）。
- 属性下限 0；回滚按实际生效量（被 0 截断时不会回滚出负数）。
