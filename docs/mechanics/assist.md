# 连携（assist）

> 规则来源：任务书 5.4-4 + 决策 D-04；Phase 3 修订释放率
> （`docs/prompts/phase3_battlecomplete.md` §四：改为副将战法自身触发率）。
> 实现：`battle/engine.py::_run_prepare_round / _run_assist`。
> 测试：`battle/tests/test_assist.py`。

## 1. 触发条件

- 主将自带战法（装配位 0）为**神谕**（`Skill.is_oracle=True`）且在准备回合成功释放。
- 释放后立即按**站位顺序**遍历两名存活副将：副将的**自带战法**（装配位 0）为主动
  （timing=active）时，各自独立 roll——**概率 = 该战法自身 `trigger_rate_bps`**
  （Phase 3 修订，取代旧全局固定 70%；触发率 100% 的战法必定连携）。
  普通随机、不走伪随机补偿、不影响该战法的伪随机记账。

## 2. 释放语义

- roll 中 → 该副将立即释放一次自带战法：`skill_trigger(kind=assist)`，
  **新播放组**、parent 指向主将神谕的 skill_trigger（因果链可视化衔接）。
- **准备型主动无需准备直接释放**（任务书已定）：连携时跳过 prepare 段，直接结算
  release 效果。
- **不占用正常释放机会**（D-04）：连携不触碰伪随机记账，本回合轮到该副将行动时
  同一战法仍可正常判定释放。
- 每局每副将至多连携一次（神谕每局在准备回合只放一次）。

## 3. 边界

- 副将自带为被动（timing=prepare）或追击（timing=pursuit）→ 不参与连携、不 roll。
- 副将已阵亡 → 跳过。
- 连携结算致主将/敌方主将阵亡 → 立即收局（与一切结算相同）。
- 主将非神谕自带 → 整个机制不启动。
