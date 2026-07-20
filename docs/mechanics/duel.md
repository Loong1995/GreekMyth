# 单挑（duel）

> 规则来源：任务书 5.4-3 + 决策 D-03（2026-07-05 批复）。
> 实现：`battle/engine.py::_run_duel`；测试：`battle/tests/test_duel.py`。

## 1. 触发条件与时点

- **仅第 1 局**开局判定一次：`game_start` 之后、所有战法（含准备回合神谕）之前，
  独立 DUEL 相位（`t.p=2`）。第 2 局及以后不再判定。
- 双方队内各取「武力 > 90 的最高武力者」（用**有效武力**；开局无状态时=面板）。
  任一方无人达标 → 不单挑、无事件。
- 并列破平：同队多人武力并列取站位靠前（D-08）；双方冠军武力相等时 A 队为叫阵方
  （队伍序破平）。

## 2. 判定公式（d = 高武力 − 低武力 ≥ 0）

| 步骤 | 公式 | RNG |
|---|---|---|
| 拒绝判定 | 低武力方拒绝概率 = `d × 8%`，封顶 **80%**（差 ≥10 仍留 20% 接受） | `duel_reject`（d=0 时不 roll） |
| 胜负判定 | 接受后高武力方胜率 = `50% + d × 5%`，d≥10 时 100% 必胜（不 roll） | `duel_win` |
| 惩罚 | 负者四维**立即 -10**（`attr_change scope=game`，惩罚只存在第 1 局，局末自动回滚） | 无 |

## 3. 事件化（全程一个播放组，支持单挑独立演出）

```
duel_challenge（组根：challenger/defender + 双方武力）
 └─ duel_result（accepted=false → 到此为止）
     └─ attr_change（仅 accepted=true：负者四维-10, scope=game）
```

## 4. 边界

- 1v1、2v2 同样适用（只要双方各有 >90 武力者）。
- 单挑不掉兵、不产生伤害事件——纯属性惩罚 + 演出。
- 惩罚随第 1 局 game_end 回滚，不带入第 2 局（D-03 已定）。

## 5. 性格约战注册表（Phase 4 C6，注册表驱动）

实现：`battle/traits.py::DuelBehavior / DUEL_BEHAVIORS / register_duel_behavior`；
单挑流程只查表、不写死 if——**空表 = 上述旧行为**（旧 golden 保障）。
测试：`battle/tests/test_phase4_primitives.py`（约战组）。

| 字段 | 语义 | 典型性格（A3/A4 接线时注册） |
|---|---|---|
| `always_accept` | 作为被叫阵方必应战：**跳过拒绝判定，不 roll、不消耗 RNG** | 傲慢 |
| `reject_bonus_bps` | 拒绝率加成，叠加后仍受 80% 封顶 | 谋深 |
| `challenge_below_threshold` | 武力 ≤90 也进入叫阵候选（仍按武力高者优先） | 好战 |
| `force_duel` | 作为叫阵方强制搦战：对方不得拒绝（跳过拒绝判定） | 好战 |

- 台词：性格 `lines` 表配置 `duel_challenge` / `duel_accept` / `duel_reject`
  三个 effect 即自动发 `trait_trigger`（未配置则静默；台词确定性轮换不耗 RNG）。
- RNG 口径：跳过拒绝判定的路径**不消耗** `duel_reject` 流；胜负判定不受注册表影响。
- 扩展方法：新性格只需 `register_duel_behavior(trait_id, DuelBehavior(...))`，
  禁止在 `_run_duel` 内新增 trait_id 特判。
