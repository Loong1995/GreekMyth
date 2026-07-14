# 选人逻辑与动态受击率（targeting）

> 规则来源：旧 core 标定值忠实迁移（`battle/formulas.py`）；事件化表达为 B4 加法式
> 演进（schema 1.1.0，可选字段 `target_select`）。

## 1. 受击点数（动态受击率）

每名武将有一个**受击点数**（bps 权重，非概率本身）：

```
受击点数 = 初始点数(默认 5000) - 损失兵力比例 × 3000
         = max(0, initial_hit_points_bps - (max_troops - troops) × 3000 // max_troops)
```

- **每次选人时从初始值重算**（非累扣）：只看当前 troops 与 max_troops 的比值。
- 兵越少点数越低：满兵 5000 → 兵打空前一刻趋近 2000。被打残的武将更不容易
  继续被集火（保护残兵，旧 core 标定行为）。
- 治疗回兵会使点数回升（点数跟随 troops 实时变化）。
- 实现：`calc_hit_points_bps()`（`battle/formulas.py`）+
  `HeroState.hit_points_bps()`（`battle/heroes.py`）。

## 2. 敌方随机选人流程

`SeriesEngine.select_enemy_by_hit_rate(attacker, reason, exclude_ids)`：

1. 候选池 = 存活敌方，按 hero_order 内（站位, hero_id）确定序排列；
   `exclude_ids` 供多段战法排除已选目标（如戈耳工凝视第二目标不与首目标重复）。
2. 候选池为空 → 返回 None（调用方跳过该段效果）。
3. 每候选取当时受击点数为权重，`rand_weighted_index` 加权 roll 一次
   （RNG source=`target_select`，消费点登记见 determinism.md；
   总权重为 0 时退化为均匀抽取）。

**使用方**：普攻（含连击每一击独立选人）、随机目标类战法（select_targets 内）、
状态响应钩子内的连锁选目标（试炼反打、三叉戟震荡等）。

**非受击率选人**（不走本机制、不消耗 target_select RNG）：

- 己方治疗目标：`select_ally_lowest_troops`，兵力比例（troops/max，bps）最低者，
  并列取遍历序靠前者，无随机。
- 指向性战法（最高武力者、主将、全体等）：由各战法 select_targets 确定性规则决定。
- 单挑对象：双方武力最高者，与受击率无关（duel.md）。

## 3. 事件化表达（target_select 可选字段）

每次受击率选人产生一条 `TargetSelectRecord`（候选池 + 当时点数 + 命中者），
随「携带该次选人的宣告/结算事件」带出（契约 §23，schema 1.1.0 加法式演进）：

| 选人场景 | 记录挂载事件 |
|---|---|
| 普攻（每击） | 该击的 `normal_attack` |
| 战法 select_targets（可多条，含连携/延迟释放/准备释放的重选） | 对应 `skill_trigger`（kind=cast/release/assist） |
| 状态响应钩子内选人（试炼反打、三叉戟震荡等） | 钩子造成的 `damage` |

```json
"target_select":[{"reason":"basic:宙斯:1","selected_id":"哈迪斯",
 "candidates":[{"hero_id":"哈迪斯","hit_bps":4400},{"hero_id":"赫尔墨斯","hit_bps":5000},
               {"hero_id":"蛇杖神","hit_bps":3800}]}]
```

- `reason` 为选人来源标签：`basic:武将:击序`、`skill:战法id`、钩子自定义前缀。
- 客户端可忽略本字段（向前兼容）；主要供运维排查与文本日志 all 档使用。

## 4. 文本日志（all 档）

`battle/textlog.py` 在 all 模式下为携带 `target_select` 的事件追加打印，brief 档
不打印（选人属冗余判定信息，非客户端反演必需）：

```
  [g1 r1] 宙斯 普攻#1 -> 哈迪斯
  [g1 r1]        ·选人[普攻] 受击点数: 哈迪斯 4400 | 赫尔墨斯 5000 | 蛇杖神 3800 → 选中 哈迪斯
```

## 5. 确定性与测试

- 选人 roll 是 RNG 消费点（source=`target_select`），候选序、权重、roll 次序全部
  确定（determinism.md）；`target_select` 字段本身不引入任何新随机。
- 测试：`battle/tests/test_targeting.py`——普攻必带记录且命中者一致、点数与公式
  逐值相符且随损兵单调下降、多段选人排除已选目标、brief 不打印/all 打印。
