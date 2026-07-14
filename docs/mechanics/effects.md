# 效果原语与战法基座（effects & skills）

> 任务书 4.4：所有战法/状态只通过效果原语产生作用。实现：`battle/engine.py`
> （原语）+ `battle/skills.py`（战法基座）+ `battle/pseudo_random.py`（触发补偿）。
> Step B2 落地，B3 扩展（吸血/固定伤害/无视防御/响应钩子/准备协议）。

## 1. 效果原语（`SeriesEngine` 方法）

| 原语 | 职责 | 事件 |
|---|---|---|
| `deal_damage(source, target, damage_type, rate_bps, parent_seq, …)` | 读状态乘区 → 暴击 roll → 随机系数 roll → 主公式 → 三池落账 → 吸血 → 响应链 → 阵亡处理 | `damage`（+`hero_defeated` 链） |
| `heal(source, target, rate_bps, parent_seq, …)` | 治疗暴击 roll → 随机系数 roll → 治疗公式 → min(理论量, 伤兵池, 缺兵量) | `heal`（实际量 0 不发） |
| `apply_status(source, target, StatusDef, parent_seq)` | 施加/刷新/叠层/静默拒绝（规则见 statuses.md §2）；施加成功触发 `on_apply` 钩子并检查准备打断 | `status_apply` / `status_refresh` / 无 |
| `remove_status(instance, reason, parent_seq)` | 驱散/清理类移除（到期由行动窗口计次统一处理） | `status_remove` |
| `modify_attr(target, changes, scope, parent_seq)` | 基础面板直改，scope=game 局末自动回滚 | `attr_change` |
| `adjust_status_attr(instance, changes, parent_seq)`（B3） | 状态携带的属性增减：写入实例 `dynamic_modifiers` 平加层，随状态移除自动消失 | `attr_change`（source_status 标注） |

`deal_damage` B3 扩展参数：

- `ignore_defense=True`：无视防御属性——计算属性差时对方防御属性**直接置 0**
  （阿喀琉斯之怒「无视统帅」；人工裁定 2026-07-05，比 true 伤害的基准 100 更强）。
- `fixed_amount=N`：跳过主公式直接落账固定值（仍分伤兵/阵亡）。
- `kind`：伤害口径标签（`basic/skill/dot/pursuit/chain/fury/...`），写入事件 payload，
  同时供响应钩子做**防递归过滤**（如落雷不响应落雷伤害）。
- `dispatch=False`：跳过伤害响应链（响应钩子内部造成的伤害防止无限连锁）。
- 吸血：结算后按来源 `lifesteal_bps`（+物理伤害限定 `physical_lifesteal_bps`）
  折算治疗，事件挂 damage 之下。

`heal` B3 扩展参数：`fixed_base=N`（以固定值为基数）、`apply_modifiers`
（固定基数是否吃治疗乘区，`formulas.apply_heal_modifiers`）。

不变量：
- 原语对已阵亡目标一律 no-op（不结算、不发事件、不消耗 RNG）。
- RNG 消费顺序固定：暴击 → 随机系数（登记表见 determinism.md §1）。
- 伤害类型 → 属性对：physical=武力 vs 统率；magic=智力 vs 智力；
  true=武力 vs 固定基准 100（无视目标防御属性）。

## 2. 暴击乘区

- 暴击率 = `clamp(面板 crit_rate_bps + Σ状态 crit_rate_bps, 0, 10000)`；率为 0 不 roll
  （不消耗 RNG）。治疗暴击同构（`heal_crit_rate_bps`）。
- 暴击倍率 ×2（20000bps），作为独立乘区参与主公式**同一次连乘一次舍入**
  （非「结果×2」——见 test_formulas.py::test_crit_multiplier_is_independent_zone）。
- DoT/HoT tick 不暴击（`can_crit=False`）。

## 3. 战法基座（B3 全量落地）

- 战法 = `Skill` 子类实例 + `register()` 注册（skill_id → 无状态单例）。
- **三种时机**（`Skill.timing`）：
  - `active`：正常回合行动窗口按装配顺序判定释放；可设 `prepare_rounds > 0`
    成为**准备型**——本窗口发 `skill_trigger(kind=prepare)` 登记，下个行动窗口
    发 `kind=release` 结算；准备期间被施加 forbid_active 控制立即打断
    （`kind=interrupted`，与控制的 status_apply 同组）。准备中仍可普攻。
  - `prepare`：**准备回合（r=0）释放**，神谕与被动的载体；`is_oracle=True`
    的主将自带触发连携（assist.md）。
  - `pursuit`：普攻命中后触发（pursuit_combo.md）。
- 行动窗口完整序见 index.md 机制流；`forbid_active` 禁战法、`forbid_basic` 禁普攻；
  两者皆禁且无事可做时 `action_start.skipped=true`。
- 触发判定：伪随机补偿 `PseudoRandomBook.roll(rng, key, base_rate, params)`，
  key=(actor_id, skill_id) 结构化元组，失败/成功**一局内真累计**（D-09），
  局边界记账整体丢弃。base ≥ 100% 必中不消耗 RNG。
- 战法先 `select_targets`（宣告进 `skill_trigger.target_ids`）再 `execute`；
  `skill_trigger` 为组根，子结算全部挂其下。追击/连携/延迟释放的 skill_trigger
  parent 指向因果事件但自成新组（契约「连锁跨组」规则）。
- **持续型效果通过状态响应钩子表达**（statuses.md §8）：战法在 prepare 时机给
  持有者挂 marker 状态，钩子挂在 StatusDef 上由引擎统一分发——战法类本身保持无状态。

## 4. B2 测试用战法（test_ 前缀，覆盖全部原语）

| skill_id | 触发 | 行为 | 验证点 |
|---|---|---|---|
| `test_blast` | 50%（补偿+保底4） | 随机敌单体 300% 魔法伤害 | 伤害原语/暴击/伪随机 |
| `test_mend` | 50% | 己方兵力比例最低者 150% 治疗 | 治疗原语/治疗暴击 |
| `test_poison` | 60% | 敌单体 2 回合 DoT（50%/回合） | 状态+DoT+来源阵亡清理 |
| `test_war_cry` | 100% | 自身增伤+暴击 buff，可叠 3 层 | 叠层/修正聚合 |
| `test_disarm` | 40% | 敌单体禁普攻 1 回合 | 控制/负面不可刷新 |
| `test_sap` | 40% | 敌单体统率-10（本局） | modify_attr/回滚 |
| `test_pursuit` | 追击 50% | 普攻目标 80% 兵刃 | 追击时机/跨组 |
| `test_combo_drill` | 100% | 自身 2 回合 100% 连击 | 连击 |
| `test_charged_nova` | 100%，准备 1 回合 | 敌全体 150% 魔法 | 准备协议/打断 |
| `test_silence` | 100% | 敌单体缄默 1 回合 | 打断准备 |
| `test_hesitate` | 100% | 敌单体犹豫 2 层 | 延迟行动 |

## 5. 标杆战法（B3 落地，`battle/standard_skills.py`）

`battlecore/skill_files.py` 全部 6 个对位实现 + 新增标杆，武将对位见
`battle/roster.py`，三段式文档见 `docs/skills/`：

雷霆神谕（宙斯）、德尔斐启示（阿波罗）、蛇杖庇护（阿斯克勒庇俄斯）、
冥界支配（哈迪斯）、海啸神谕（波塞冬）、神行神谕（赫尔墨斯）、战争狂热（阿瑞斯）、
十二试炼（赫拉克勒斯）、蛇发凝视（美杜莎）、**阿喀琉斯之怒（阿喀琉斯，验收标杆）**、
石化之瞳（主动）、织谋蓄能（主动）、突击（追击）。
