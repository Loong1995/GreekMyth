# Phase 3 Battle 大修执行计划（待人工审核）

> 任务书：`docs/prompts/phase3_battlecomplete.md`（机制+武将池 v3.1）；
> 参考：`docs/prompts/client_perform.md`（客户端演出需求，本阶段只兼容不实现）。
> 本文档经人工审核通过后方可动 `battle/` 代码。分节给出改造项、事件流演进、
> 模块化方案、golden/测试与清理计划。里程碑见 §九。

---

## 一、伤害公式重做（新建 docs/mechanics/damage.md）

现状：统一公式 `(390 + 属性差×8) × 乘区链`。任务书改为两条独立公式：

- **兵刃**：`(360 + 武力 - 统率) × 技能系数 × (1+基础增伤) × (1-减伤) × (1+额外增伤) × 会心伤害率 × 兵力系数`
- **谋略**：`(360 + 智力 - ½统率 - ½智力) × 同乘区链 × 奇谋伤害率 × 兵力系数`
- 技能系数前的核心项 **min=1 安全截断**。
- 乘区语义映射（保持 bps 整数）：
  - 基础增伤 = `damage_up_bps`（通用+类型）；减伤 = `damage_reduce_bps`（上限沿用 8000）；
  - **额外增伤** = 新键 `extra_damage_up_bps`（独立乘区，预留兵种/固士/驻守/同盟等来源，本期只接战法/追击单独加成）；
  - 会心/奇谋伤害率 = 暴击乘区（物理称会心、谋略称奇谋，同一实现）。
- **兵力系数重标定**：锚点 10000→100%、8000→90%、6000→80%、4000→70%。
  线性拟合：`coef = 0.5 + 0.5×(troops/10000)`（10000=1.00、8000=0.90、6000=0.80、
  4000=0.70，完全对齐锚点），替换现行 `0.4+0.6x`。
- 随机系数（9500~10500）与 MIN_DAMAGE=1、30/70 落池保持不变。
- 治疗公式不动。
- 产出：`docs/mechanics/damage.md` 单独机制文档 + `formulas.py` 重写 + 公式标定单测更新。
- ⚠️ 这是标定公式变更，全部 golden 将失效 → 按 §七统一重建并在 commit 说明。

## 二、格挡/闪避（伤害前置查询 + 0 结算信号）

- 结算方在算好伤害数字后、落账前，查询目标特殊状态：**格挡（block）**、**闪避（evade）**，
  预留弱蹱等扩展。有几率则 roll（普通随机，source=`block`/`evade`）。
- roll 中：伤害置 0 落账，仍发 `damage` 事件，payload 新增可选字段
  `"mitigation": "block" | "evade"`（加法演进，schema → 1.2.0），客户端据此播格挡/闪避动画。
- 口径：格挡消耗 1 次格挡计数（`block_charges` 动态计数）；闪避走 `evade_bps` 修正键。
  两者均**不算受到实际伤害**、不触发任何受击响应（D-20 口径推广）。
- 判定顺序：闪避 → 格挡（闪避成功则不消耗格挡次数）。

## 三、行动窗口时序调整 + 犹豫计次前移

现行序：③延迟补结算 → ④准备release → ⑤犹豫roll。任务书要求 ④⑤ **互换**：

```
action_start
  ① 状态计次到期（含犹豫——前移，见下）
  ② on_action_start 钩子（含性格窗口触发）
  ③ 犹豫延迟行动补结算（不受犹豫到期影响，寄存行动照常释放）
  ④ 犹豫延迟判定（一窗一 roll）           ←互换
  ⑤ 准备型战法释放（release 免犹豫判定）    ←互换
  ⑥ 主动战法 → ⑦ 普攻+追击
```

- **犹豫计次改到 action_start 前**（与其他状态统一）：本回合开始犹豫计次超期即移除，
  但**已寄存的延迟普攻/主动不受影响**，继续在 ③ 释放；仅本回合新行动不再进 ④ 判定。
- 更新 `docs/mechanics/hesitation.md`、`index.md` 时序图、`determinism.md` RNG 消费序，
  以及 `test_prepare_hesitation.py`/`test_status_interactions.py` 相关格。

## 四、连携改版 + 震荡口径

- **连携**（更新 `docs/mechanics/assist.md`）：释放率从固定 70% 改为**副将自带战法自身
  trigger_rate_bps**；形式=该副将获得一次在准备阶段正常释放自带战法的机会（准备型免准备）。
  仍为普通随机、不走伪随机补偿、不影响该战法伪随机记账。其余不变。
- **震荡**：新伤害类别 `special`（`kind=trident` 沿用），发 `damage` 事件供播放，
  但**不触发任何产生伤害效果的响应**（雷霆/血誓/试炼/凝视等对震荡一律不响应）。
  实现为 deal_damage 增加 `is_special=True` 短路响应分发。

## 五、性格系统（新增，重点）

每武将一条性格，增益/发作两面，**强制修正**战斗机制，触发点位任意：

- 数据模型：`Trait`（trait_id、hooks、参数、台词表）注册表，同战法 REGISTRY 模式；
  `HeroTemplate.trait_id` 关联。
- 钩子点位（本期覆盖任务书全部性格所需）：`on_round_start`、`on_action_window_start`、
  `on_pre_damage`（傲慢/记仇）、`on_post_damage`、`on_target_select`（狡黠/好战/怒涛/鲁莽）、
  `on_heal_target_select`（仁心）、`on_kill`（求胜/好战）、`on_crit_check`（踵之弱）、
  `on_hesitation_check`（明睿/威权/谋深）、`on_attr_drain`（威权翻倍）、静态面板修正（速度+X 等）。
- **新事件类型 `trait_trigger`**（加法演进）：payload =
  `{hero_id, trait_id, effect, line}`。`line` 为后端预设台词（符合人设、简短），
  客户端收到即弹聊天框播出。仅**任务书标注"播放台词"的触发**发事件带台词；
  纯数值静默修正（如速度+10）不发事件（省体积三原则）。
- 台词表：`battle/traits.py` 内每性格 2~4 条短台词（正/负面分开），确定性轮换
  （按触发次数取模，不消耗 RNG）。
- 测试开关：Trait 概率参数可注入覆盖（高概率测试版），正式默认按表。

## 六、武将/战法池 v3.1 全量替换

- **移除**：现 `roster.py` 全部 12 模板、`standard_skills.py` 中池外战法
  （pythia_woven_scheme、gorgon_gaze、delphi_charged_oracle 等）、`skills.py` 的
  test_* 保留（测试原语仍需）。相关 docs/skills/ 池外文档删除。
- **新池**：29 武将（神 8/人 8/海 6/冥 6 + 新增边角料），每人：性格 + 自带战法 + 拆解战法。
  四维 = `基础值 + 成长×(等级-1)`，等级 1~50，`HeroSetup` 增加 `level`（默认 50）。
  兵力上限仍 10000。
- 战法拆分文件组织（模块化）：`battle/skills_pantheon/` 包，按阵营分 4 文件
  （gods.py/mortals.py/sea.py/underworld.py）+ `common.py`（复用口径：格挡/反打/
  吸取/驱散/先攻/魅惑/炸弹等原语组合），每文件 ≤400 行。
- 新增机制清单（战法驱动）：先攻（行动排序覆盖键）、魅惑（敌我不分选目标）、
  炸弹（延迟起爆状态）、驱散、格挡赋予、吸血、闪避叠层、暴击机会（必暴标记）、
  行动顺延（帕里斯/奥德修斯负面）、额外行动（好战）。各复用/新增状态修正键，
  登记入 `statuses.md`。
- 每战法一份三段式文档 `docs/skills/<skill_id>.md`；`docs/mechanics/index.md` 登记新机制。

## 七、golden / reference / 测试计划

- `battle/tests/golden/` 按新公式+新池重建（旧 9 份废弃重写场景），场景覆盖：
  基础 3v3、单挑、每阵营神谕队、格挡闪避、犹豫时序新序、连携新规、震荡、炸弹、性格。
- **reference/ 新建**（人工审核用，非机器 golden）：
  - `reference/golden/`：关键场景战报 JSON + human log（textlog brief）各一份；
  - `reference/characters/<hero>/`：29 武将逐一性格测试 human log
    （高概率触发配置、50 级、10000 兵，配角自选）。
- **手动测试入口**：`battle/tools/manual_battle.py` —— 命令行/JSON 配置任意双方阵容
  （武将、等级、战法、性格概率覆盖、种子），输出战报+human log。
- 单测：新公式标定、格挡/闪避、时序互换、连携新规、震荡不响应、性格逐条、
  29 武将战法逐个至少 1 用例；确定性/契约测试沿用。

## 八、模块化重构 + 清理

- `engine.py`（1357 行）拆分：`engine.py`（系列/局调度）+ `action_window.py`
  （行动窗口 pipeline）+ `combat.py`（deal_damage/heal 管线：格挡闪避→落账→响应分发）+
  `duel.py` + `assist.py` + `traits.py`。EventWriter/RNG/伪随机/setup/report 复用不动。
- 事件契约加法演进汇总（schema 1.1.0 → 1.2.0）：`damage.mitigation`（可选）、
  `damage.damage_class`（可选，special 标震荡）、新事件 `trait_trigger`。
  更新 battle_events.md/payloads/schema.json，加法项逐条登记。
- 清理：删除池外战法/武将代码与文档、`battle/out/` 过期产物、docs/dev 中间版本文档；
  `names.py` 全量换新中文名。文档纪律：每文件 ≤300 行，index 登记。

## 九、里程碑（供验收）

| # | 交付 | 验收物 |
|---|---|---|
| M1 | 公式+格挡闪避+damage.md | formulas 单测绿；schema 1.2.0 文档 |
| M2 | 时序互换+犹豫计次前移+连携新规+震荡 | 机制文档更新；交互矩阵测试绿 |
| M3 | 引擎模块化拆分 | 全量既有测试绿（golden 暂 skip） |
| M4 | 性格系统+trait_trigger 事件 | 性格逐条单测；台词事件样例 |
| M5 | 武将池 v3.1 + 战法全量 | 29 武将文档+单测；names 更新 |
| M6 | golden 重建 + reference/ + 手动入口 | reference/characters 29 份 log；manual_battle 可用 |
| M7 | 清理+文档收口+changelog | index/机制文档对齐；无用代码删除 |

## 十、待人工确认点

1. 兵力系数取 `0.5+0.5x` 线性（精确过 4 锚点）——确认？
2. 谋略公式 `360+智-½统-½智` 按字面实现（等效 `360+½智-½统`）——确认？
3. `trait_trigger` 事件名与 payload 结构（§五）——确认？
4. 台词由后端预置在 `traits.py`（确定性轮换、不消耗 RNG）——确认？
5. 旧 golden 全量废弃重建（公式变更不可避免）——确认？
6. test_* 测试战法保留（机制原语测试仍需要）——确认？
