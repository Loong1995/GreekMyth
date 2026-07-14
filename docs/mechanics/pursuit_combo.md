# 追击与连击（pursuit & combo）

> 规则来源：任务书 5.4-1/2 + 决策清单遗留问题 §四（每击独立追击）。
> 实现：`battle/engine.py::_perform_basic_attack / _dispatch_pursuit`。
> 测试：`battle/tests/test_pursuit_combo.py` + 交互矩阵。

## 1. 追击（pursuit）

- 定义：`timing=pursuit` 的被动战法，触发时机 = **己方普攻命中后**（damage 结算完）。
- 分发规则：按攻击者装配顺序逐个判定（伪随机补偿，同主动战法一致）；
  普攻目标已被该击打死 → 不 roll、无追击（决策 D-17）。
- **禁普攻即无追击**：攻击者带 forbid_basic（缴械/冥锁/石化）或 forbid_pursuit 时
  不分发。含中途被反制控制的场景（普攻命中 → 目标反制石化攻击者 → 追击被封锁）。
- 事件结构（契约 §3.2 连锁跨组）：

```
normal_attack（组根 A）
 └─ damage                    ← 普攻伤害，组 A
     └─ skill_trigger（kind=cast，组根 B，parent 指回该 damage）
         └─ damage             ← 追击伤害，组 B
```

- v3.1 实现：`achilles_thrust 怒火突刺`/`perseus_flash 镜盾闪袭`/`scylla_maw 六首撕咬`
  等（见 docs/skills/）；测试实现：`test_pursuit`（50% 触发）。

## 2. 连击（combo）

- 武将连击率 = 状态修正 `combo_rate_bps` 聚合（基础面板 0，由战法/状态提供）。
- 行动窗口普攻段：连击率 ≥100% 必打两次；(0,100%) 区间 roll 一次（`combo` 消费点）；
  两次普攻**各自独立选目标、独立结算、独立触发追击**（同一追击战法可同窗口两触发）。
- 第二击前重新检查 forbid_basic 与存活（第一击的反制可能改变局面）。
- 事件：两个 `normal_attack` 组根，`strike_no=1/2` 区分。
- 测试实现：`test_combo_drill`（自身挂 2 回合 100% 连击 buff）。

## 3. RNG 消费顺序（determinism.md 同步登记）

〔连击 roll（如需）〕→ 逐击：〔选目标 → 暴击 → 随机系数 → 追击触发 roll →
追击内部（暴击 → 随机系数）〕。
