# 确定性规则（determinism）

> 全项目最高优先级约束（任务书 4.1）：同一 `(rng_seed, battle_setup, core_version)`
> 输入，任何机器任何时间产出**逐字节相同**的事件流。本文件是唯一权威清单，
> 新增任何机制前先核对本文件，违反即 bug。

## 1. 随机源

- **单一 RNG 流**：整个系列（1~7 局）共用一个 `DeterministicRNG`（`battle/rng.py`），
  种子由 `simulate(setup, seed)` 外部注入，core 内部禁止产生种子。
- 算法：PCG 变体（64 位 LCG 状态 + xorshift/rotate 输出 32 位），与旧 core 完全一致；
  `seed=0` 时使用固定默认状态 `0x853C49E6748FEA9B`。
- **禁止**：系统时间、`random` 标准库、os 熵、浮点运算、依赖内存地址的任何值。
- 每次取随机数必须带 `source/reason` 标注（audit 档记录审计史，不影响序列）。
- RNG 消费点登记（新增消费点必须在此登记，消费顺序即代码执行顺序）：

| 消费点 | 方法 | 说明 |
|---|---|---|
| 跨队先手 | `rand_bps("action_order", ...)` | 每 slot 一次；概率 0/10000 时不消费 |
| 敌方目标加权 | `rand_weighted_index("target_select", ...)` | 权重=受击点数；普攻与战法选目标共用 |
| 战法触发判定 | `rand_bps("skill_trigger", ...)` | 经 PseudoRandomBook（补偿/保底）；率 ≥100% 或保底命中时不消费 |
| 暴击判定（伤害/治疗） | `rand_bps("crit", ...)` | 暴击率为 0 时不消费；DoT/HoT tick 不判暴击 |
| 伤害/治疗随机系数 | `rand_index(1001, "random_coef", ...)` | 0.95~1.05，每次 deal_damage/heal 各一次（含 DoT/HoT tick） |
| 单挑拒绝（B3） | `rand_bps("duel_reject", ...)` | 仅第 1 局至多一次；武力差 0 时不消费 |
| 单挑胜负（B3） | `rand_bps("duel_win", ...)` | 接受后一次；差 ≥10 必胜不消费 |
| 连携触发（B3） | `rand_bps("assist", ...)` | 主将神谕后按站位序逐副将；不走伪随机补偿 |
| 犹豫延迟判定（B3） | `rand_bps("hesitation", ...)` | 每行动窗口至多一次；全禁无可延行动时不消费 |
| 连击判定（B3） | `rand_bps("combo", ...)` | 连击率 ≥100% 或 =0 时不消费 |
| 状态响应内随机（B3） | 各钩子自带 reason | 如美杜莎凝视石化 roll；顺序=响应分发序 |

行动窗口内消费顺序固定：〔到期清理〕→〔on_action_start 响应（分发序见 §2）〕→
〔延迟行动补结算（登记序）〕→〔准备型 release〕→〔犹豫 roll（如需）〕→
〔战法1 触发 → 选目标 → 暴击 → 随机系数 → 伤害响应链〕→〔战法2 …〕→
〔连击 roll（如需）→ 逐击：普攻选目标 → 暴击 → 随机系数 → 伤害响应链 → 追击触发 →
追击内部〕。伪随机记账（fail/streak）为战时状态，随局清空。

## 2. 遍历与排序规则（全部显式，禁止依赖隐式顺序）

| 集合 | 排序键 | 位置 |
|---|---|---|
| 队伍 | `team_id` 字典序 | `SeriesEngine.__init__` |
| 全局武将序（快照/损耗/统计等一切遍历） | 队伍序 → `position` 升序 | `hero_order` |
| 队内行动队列 | `(-有效速度, position, hero_id)` | `_team_queue` |
| 目标候选池 | `hero_order` 相对序（=站位序） | `_alive_enemies` |
| 破平链（通用） | 站位序（0→2）→ 队伍序（A→B），决策 D-08 | 各处 |
| 同武将多主动战法 | 装配顺序（HeroSetup.skills 下标） | `_run_action_window`（B2 落地） |
| 状态遍历（DoT tick/清理/修正聚合） | hero_order × 施加序（instance_id 升序） | `_tick_periodic_statuses` 等 |
| 伤害/行动响应钩子分发（B3） | `(response_priority, 持有者 hero_order 序, instance_id)` 升序；先攻方全部 → 后守方全部 | `_dispatch_damage_hooks` |
| 追击战法分发（B3） | 攻击者装配顺序 | `_dispatch_pursuit` |
| 延迟行动补结算（B3） | 登记先后序（FIFO） | `_settle_delayed_actions` |
| 连携副将遍历（B3） | 站位序 | `_run_assist` |
| 准备回合施法序（B3） | 与正常回合行动顺序同构（速度 + 先手 roll） | `_run_prepare_round` |

Python dict 虽保证插入序，但**一律不得作为语义依据**——任何影响结算顺序的遍历
必须走上表的显式排序键。

## 3. 数值运算

- 全部整数运算；概率与系数一律 bps 万分比（10000=100%）。禁止浮点参与结算
  （展示层格式化除外，且展示不得回流结算）。
- **舍入约定**：伤害乘区连乘保持旧 core 语义——分子一次性连乘后对 `10000^8`
  做一次四舍五入 `(num + den//2) // den`。中间量最大约 10^32，Python 无限精度
  整数下精确；**跨语言迁移约定**：必须用大整数（128 位以上或 bigint）等价实现，
  或逐乘区两两约分并证明与一次舍入逐值等价后方可替换——不得默默改变舍入点
  （会破坏与已标定公式的逐值一致）。
- 除法一律显式写明取整方向：`//`（向下）或 `(x + d//2) // d`（四舍五入）。

## 4. 事件流确定性

- `seq` 由 `EventWriter` 单调分配；`t=(g,r,p,s)` 字典序与 seq 序一致，写入时强校验
  （回退即抛 `BattleCoreError`）。
- 战报序列化唯一规范形态：`json.dumps(report, ensure_ascii=False, separators=(",",":"))`
  且保持字段插入序（`battle/report.py::serialize_report`）。逐字节对比以此为准。
- payload 字段写入顺序在代码中固定，禁止用 dict 推导等不定序方式拼装。

## 5. 纯函数边界

- `simulate` 不读写任何全局可变状态；模块级常量运行期只读（禁止旧 core
  `bench_basic.py` 式的运行时改常量）。
- `audit` 开关只增加旁路记录，不得改变 RNG 消费或事件产出（有测试保证）。

## 6. 验证手段

- `battle/tests/test_determinism.py`：同种子 100 次逐字节一致；不同种子发散；
  seed=0 合法；audit 不影响输出。
- 跨平台验证方式：CI 在 Windows/Linux 双平台跑同一批种子比对序列化输出
  （纯整数 + 纯 Python 语义，无平台相关行为；B4 接入 CI 时落地）。
- 任何 PR 改变 golden 输出必须显式更新 golden 并说明原因（B4 建立）。
