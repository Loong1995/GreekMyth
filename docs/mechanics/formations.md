# 阵型系统（formations）

> 2026-07-23 落地。注册表：`battle/formations.py`（`FORMATION_REGISTRY`）；
> 接线：`setup.py`（校验）→ `heroes.build_hero_state`（初始受击点数）→
> `engine._apply_formation_buffs`（每局 game_start 后重挂整场被动）。

## 1. 模型

阵型是**队伍级配置**（`TeamSetup.formation`，默认空 = 无阵型，行为与历史
逐字节一致）。一个 `FormationDef` 规定：

- `positions`：合法站位集合（配将时校验，站位不在集合内报 `SetupError`）；
- `hit_points_bps`：站位 → 初始受击点数（覆盖默认 5000，配将时写入
  `HeroState.initial_hit_points_bps`，动态衰减公式不变，见 targeting.md §1）；
- `buffs`：站位 → 整场被动 `StatusDef` 工厂（BUFF，`duration=PERMANENT`）。

**整场语义**：战时状态随局清空（契约 §19），引擎在每局 `game_start` 后按
`hero_order` 确定序重挂（`status_apply` 事件，parent_seq=0，来源=持有者自身，
不耗 RNG）。无阵型队伍零事件零改动（golden 保障）。

## 2. 雁行阵（yanxing，站位 1/2/6）

| 站位 | 初始受击点数 | 整场被动 | 状态 id |
|---|---|---|---|
| 1 | 10800 | 受到伤害 -5%（damage_reduce_bps=500） | `formation_yanxing_guard`（雁行·雁翼） |
| 2 | 10800 | 同上 | 同上 |
| 6 | 5400 | 造成伤害 +8%（damage_up_bps=800） | `formation_yanxing_edge`（雁行·雁喙） |

### 数值推导（2026-07-23 求解）

约束：满兵受击率 40/40/20；6 号位兵力趋近 0 时受击率趋近 10%。

- 比例约束 → 点数比 2:2:1，设 6 号位 = a；
- 残兵约束 → (a−3000)/(5a−3000) = 10% → **a = 5400**；
- 推论：1/2 号位残兵（其余满兵）受击率 = 7800/24000 = **32.5%**——高基数
  下固定衰减 3000 占比小，坦位残血仍显著吸仇恨；6 号位保残效果强（20%→10%）。

## 3. 扩展与红线

- 新增阵型 = `FORMATION_REGISTRY` 加一项 + `names.py` / 客户端
  `ChineseNames.cs` 登记状态中文名（双份同步红线）；引擎零改动。
- 阵型被动是普通状态实例：走既有修正聚合/驱散/事件化管线。若某阵型被动
  设计为**不可驱散**，需在 StatusDef 上另加豁免键（当前雁行阵未做豁免，
  理论上可被驱散，本局内不回补——如需改口径先记决策）。
- 测试：`battle/tests/test_formations.py`（校验/点数与受击率/逐局重挂/
  无阵型零事件）。
