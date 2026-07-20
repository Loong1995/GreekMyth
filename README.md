# GreekMyth

希腊神话题材 2D 卡牌对战手游。**服务器权威**结算：服务端（`battle/` 纯 Python core）
完成全部战斗计算并产出确定性事件流战报；客户端（Unity，本仓库根即 Unity 工程）
只按事件流播放演出，不做任何结算。

当前阶段：Phase 1——战斗事件流契约冻结 + BattleCore 重构
（任务书 `docs/prompts/phase1_battlecore.md`）。Step A / B1 / B2 / B3 已人工验收，
B4（工具链 + 收口）已产出。

## 目录约定

    docs/
      prompts/      # 每阶段任务书，只读存档
      schema/       # 事件流契约（冻结，仅加法式演进）+ JSON Schema
      dev/          # v0 分析、播放模型、决策记录(decisions.md)、changelog、性能报告
      mechanics/    # 机制文档：index.md 主文档 + 每机制一个小文件
      skills/       # 每战法一个三段式文档（玩家版/机制版/程序版）
    battle/         # 新 core 实现（simulate(battle_setup, seed) -> battle_report）
      tests/        # 单测 + golden/ 逐字节回归基准
      tools/        # batch_sim 批量模拟 / replay_dump 战报转文本 / gen_golden
      benchmarks/   # 性能基准（bench_simulate）
    battlecore/     # 旧 core，只读，禁止修改
    reference/      # 旧 golden 样例，只读，禁止修改
    Assets/ 等      # Unity 客户端工程

## 快速上手（battlecore）

```bash
python battle/sample.py --scenario standard          # 跑一场演示战 + 中文日志
python -m pytest battle/tests -q                     # 全部测试
python battle/tools/batch_sim.py --seeds 0:200       # 批量模拟胜率/伤害统计
python battle/tools/replay_dump.py <战报.json> --mode brief   # 战报转文本日志
python battle/benchmarks/bench_simulate.py           # 吞吐基准（目标 ≥100 局/秒）
```

所有测试文件支持 `python battle/tests/test_xxx.py` 直接执行。

## 上下文管理规则（长期有效）

- 全局图景只读 `docs/mechanics/index.md`；局部问题只加载对应机制文件。
- 契约文件（前后端唯一共同依赖）：`docs/schema/battle_events.md` + payloads + schema.json；
  冻结后仅允许加法式演进（新增事件类型/新增可选字段）。
- 每个文档文件 ≤500 行，超出拆分并在 index 登记；文档与代码不同步视为任务未完成。
- 确定性最高优先级：同 (setup, seed) 战报逐字节一致；golden 变更必须显式更新并在
  commit message 说明原因。
- 每个工作会话结束追加 `docs/dev/changelog.md`（日期 + 3-5 行）。
- 项目规则细则见 `.cursorrules`（随 Step 更新）。
