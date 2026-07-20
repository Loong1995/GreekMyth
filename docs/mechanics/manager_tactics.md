# 经理人战术系统（P4-C，schema 1.4.1 / core battle-0.4.1）

> 实现：`battle/tactics.py`（注册表）+ `engine._apply_tactics`（回合头结算）。
> 定案来源：phase4_plan §五（交互模型/服务端改造）与 §二4（首发战术集）。

## 一、模型

- **战术 = 整体倾向 + 额外加成**：全部用现有状态系统表达（duration=1 状态
  逐回合刷新），不动任何结算机制；**不消耗 RNG**。
- **注册表驱动**：新战术 = 一条 `TacticDef(tactic_id, name, validate,
  on_round_start)` 注册进 `TACTIC_REGISTRY`，引擎主流程零特例。
- **配置入口**：`setup.metadata["tactics"]`（随 `setup_metadata` 入战报，
  重放天然闭环）：

```json
{"preset":  {"A": {"tactic_id": "stance", "params": {"level": 1}}, "B": null},
 "changes": [{"team_id": "A", "round": 3,
              "tactic_id": "focus_fire", "params": {"target_id": "哈迪斯"}}]}
```

- **规则红线**：变更最早第 2 回合生效（第 1 回合必走预设）；一局每方最多
  变更 2 次；同队多条变更后者覆盖前者（round ≤ 当前回合的最后一条生效）。
  校验在 `validate_tactics`（simulate 入口调用，无配置零开销）。

## 二、首发三战术（§二4 定案）

| tactic_id | 名称 | params | 效果（每回合施加，duration=1） |
|---|---|---|---|
| `focus_fire` | 集火目标 | `target_id`（敌方；缺省=无偏置） | 目标挂〔集火〕`hit_weight_up_bps=+10000`（受击点数 ×2）；**仍走加权随机与保残兵递减，非强制锁定**；战法指名/最低兵力类选人不受影响；目标阵亡当回合无偏置 |
| `protect` | 保护目标 | `target_id`（我方，必填） | 目标挂〔保护〕减伤 8% + `hot_rate_bps=400` 小额持续治疗（来源=本队主将，随主将智力） |
| `stance` | 攻守倾向 | `level` -2~+2 | 全队挂〔攻守倾向〕造成伤害 +3%/级、受到伤害 ∓3%/级（level=0 不挂） |

## 三、结算时序与确定性

- 每回合 `round_start` 事件后**第一步**结算（早于回合计数器清零/性格
  on_round_start/伤兵损耗）：按 **setup 队伍序**逐队——
  ①本回合恰生效的变更发 `tactic_applied`（payload 见 payloads §26）；
  ②当前生效战术施加当回合状态（status_apply/refresh 挂 round_start 组下）。
- 战术被替换后：旧战术不再逐回合刷新，其状态按 duration 自然到期。
- 战时状态不跨局：变更序列对系列内**每一局**同样生效（第 N 回合口径按局内回合）。
- RNG 零消耗 → 与战术无关的结算序列不受扰动；determinism.md 已登记。

## 四、逐回合重算（「改变战术」服务器流程）

- **实现定案（2026-07-20）**：不做引擎快照/续算原语——确定性下
  「取第 N+1 回合边界快照续算」≡「同 seed + 变更序列**从头重模拟**」
  （回合 1..N 战术输入相同 → 事件流逐字节相同前缀）。模拟成本毫秒级
  （batch_sim 实测 ~4ms/局），重算全量成本可忽略，且无快照字段漏项风险。
- 服务器入口：`tactics.with_change(setup, change)` 追加一条变更（校验
  2 次上限/最早第 2 回合）→ `simulate(new_setup, seed)` 得完整新战报 →
  向客户端下发（客户端以 round_start seq 为切点替换未播放段）。
- 回归固化：`battle/tests/test_tactics.py::
  test_change_emits_tactic_applied_and_prefix_identical`（前缀逐条一致）。
- 断线兜底：客户端手头战报永远是完整版，断线按手头版本播完，
  重连后以服务器最终版校正。

## 五、客户端表现（当前占位口径）

- `tactic_applied` → 非阻塞 cut-in 横幅「X 队变更战术 →「集火目标」」
  （`PerformanceRunner.ApplySilently`），不占时间轴。
- 左侧战术栏 UI（竖直图标列/剩余次数/生效倒计）与替换段播放随
  联网客户端接入（本仓 demo 为离线整报播放，无对局中变更信号源）。
- 战术状态飘字/图标走通用状态表现（中文名：集火/保护/攻守倾向，
  `names.py` 与 `ChineseNames.cs` 已同步）。

## 六、扩展方法

1. `battle/tactics.py` 写 `TacticDef`（校验 + on_round_start 施加逻辑，
   效果只允许状态/权重偏置表达）并注册。
2. `names.py` / `ChineseNames.cs` 加中文名；本文档表格加一行。
3. 客户端战术栏加图标（联网版）。禁止在 engine/Runner 写战术特例。
