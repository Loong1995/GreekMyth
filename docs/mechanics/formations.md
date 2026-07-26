# 阵型系统（formations）

> 注册表：`battle/formations.py`（`FORMATION_REGISTRY`）；识别：`detect_formation`。
> 接线：`heroes.build_hero_state` / `engine._apply_formation_buffs`（均经
> `resolve_formation(positions)`，**只按站位**识别）。
> 客户端几何与六区编号：[battlefield_layout.md](../client/battlefield_layout.md)。

## 1. 模型

阵型**不是**配将入参。`TeamSetup` 无 formation 字段可写；只读属性
`TeamSetup.formation` = `detect_formation(站位集合)`。一个 `FormationDef` 规定：

- `positions`：该预设的站位集合（**精确 frozenset 相等**才识别命中）；
- `hit_points_bps`：站位 → 初始受击点数（覆盖默认 5000）；
- `buffs`：站位 → 整场被动 `StatusDef` 工厂（BUFF，`duration=PERMANENT`）。

**识别规则**（前后端一致）：

1. 占用站位集合与某预设**精确相等** → 该 `formation_id`；
2. 否则 → `""`（无阵型加成）。

配将只需改 `HeroSetup.position` / 队级 `positions` 数组。禁止传入 formation
字符串（config / TeamSetup / manual 入口均已移除）。

**整场语义**：战时状态随局清空；引擎在每局 `game_start` 后按 `hero_order`
确定序重挂（`status_apply`，parent_seq=0，不耗 RNG）。无阵型零事件零改动。

## 2. 六套预设

| id | 中文名 | 站位集合 | 加成（本轮） |
|---|---|---|---|
| `yizi` | 一字阵 | {1,2,3} | 骨架（空 buff / 默认受击点） |
| `zhui` | 锥形阵 | {2,4,6} | 骨架 |
| `ji` | 箕形阵 | {1,5,6} | 骨架 |
| `fangyuan` | 方圆阵 | {3,4,5} | 骨架 |
| `yanyue` | 偃月阵 | {1,3,5} | 骨架 |
| `yanxing` | 雁行阵 | {1,2,6} | 见下表 |

废弃客户端旧称：却月≈旧 `{1,2,6}` 展示名（现统一**雁行**）、鹤翼≈旧 `{2,4,6}`
（现**锥形**）、旧方圆 `{1,5,6}`（现**箕形**）。新方圆为 `{3,4,5}`。

## 3. 雁行阵（yanxing）数值

| 站位 | 初始受击点数 | 整场被动 | 状态 id |
|---|---|---|---|
| 1 | 10800 | 受到伤害 -5%（damage_reduce_bps=500） | `formation_yanxing_guard`（雁行·雁翼） |
| 2 | 10800 | 同上 | 同上 |
| 6 | 5400 | 造成伤害 +8%（damage_up_bps=800） | `formation_yanxing_edge`（雁行·雁喙） |

### 数值推导（2026-07-23 求解）

约束：满兵受击率 40/40/20；6 号位兵力趋近 0 时受击率趋近 10%。

- 比例约束 → 点数比 2:2:1，设 6 号位 = a；
- 残兵约束 → (a−3000)/(5a−3000) = 10% → **a = 5400**；
- 推论：1/2 号位残兵（其余满兵）受击率 = 7800/24000 = **32.5%**。

## 4. 扩展与红线

- 新增阵型 = `FORMATION_REGISTRY` 加一项 + `names.py` / `ChineseNames.cs` 登记；
  引擎零改动。集合须与已有预设互异（否则 detect 歧义）。
- 阵型被动是普通状态实例：走既有修正聚合/驱散/事件化管线。
- 测试：`battle/tests/test_formations.py`（六套 detect / 站位属性 /
  点数与受击率 / 逐局重挂）。
