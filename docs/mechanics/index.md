# 机制主文档（index）

> 只读本文件即可建立全局图景；处理局部问题时只加载对应机制文件。
> 每机制 2-4 句精要 + 链接。当前进度：Phase 3（公式重做 + 格挡闪避 + 性格系统 +
> 武将池 v3.1）完成，schema 1.3.0（快照补重放字段，客服重放闭环）。
> 工具：`battle/tools/`（batch_sim 批量统计 / replay_dump 战报转文本 / gen_golden /
> gen_reference 人工审核参考物 / manual_battle 手动配阵入口 /
> replay_report 玩家战报重放排查——战报 JSON→还原 setup→重跑→逐字节校验→all 日志）、
> `battle/benchmarks/`（吞吐基准，报告 `docs/dev/performance.md`）。
> 文字日志 `battle/textlog.py`：brief 主干（含性格台词★）/ all 全量
> （额外插入技能掷点明细 ⚄/⊘，来自 `_debug_rolls` 调试侧信道，不进战报 JSON）。

## 一、整体机制流（一个系列从入口到结束）

```
simulate(battle_setup, seed)                          battle/api.py
 └─ 校验 setup（2 队、每队 1~3 人、主将必设、初始兵力合法）
 └─ SeriesEngine.run()                                battle/engine.py
     ├─ battle_start（第 1 局事件流头部）
     ├─ 逐局（最多 7 局，平局残血续战）：
     │   ├─ game_start（含本局兵力三池快照）
     │   ├─ DUEL 相位：单挑（仅第 1 局，双方均有武力>90 时；D-03）→ duel.md
     │   ├─ 准备回合 r=0：timing=prepare 战法按行动顺序施放（神谕/被动入场）；
     │   │   主将神谕后两副将自带主动按各自触发率连携立即释放（Phase 3）→ assist.md
     │   ├─ 正常回合 r=1..8：
     │   │   ├─ round_start → 回合计数器清零（落雷/追加伤害等每回合上限）
     │   │   │                → 伤兵自然损耗（伤兵池 30% 转阵亡）
     │   │   │                → DoT/HoT 周期 tick（status_tick + 子 damage/heal，可致死）
     │   │   ├─ 行动顺序：队内有效速度排序 + 跨队逐 slot 先手 roll；
     │   │   │   first_strike 状态优先、postpone 性格旗标顺延回合末（Phase 3）
     │   │   ├─ 逐武将行动窗口（Phase 3 序，④⑤ 互换）：
     │   │   │   ① 状态计次到期（含犹豫，统一前移）→ action_start
     │   │   │   ② on_action_start 状态钩子（幽影蔽体/冥祭献统/扰心印记…）
     │   │   │   ③ 犹豫延迟行动补结算（寄存行动照常释放；被控部分作废，目标重选）
     │   │   │   ④ 犹豫延迟判定（一窗一 roll；roll 中则本窗普攻+主动延后 1 回合）
     │   │   │   ⑤ 准备型战法释放（prepare→release；release 免犹豫；被控打断=interrupted）
     │   │   │   ⑥ 主动战法（装配顺序 × 伪随机补偿；准备中不再发起新主动）
     │   │   │   ⑦ 普攻（连击率≥100% 打两次；每击独立触发追击）→ pursuit_combo.md
     │   │   │   伤害结算全程分发响应钩子（雷霆/血誓/蛇杖/试炼/凝视/三叉戟/追加伤害）
     │   │   │   → hero_defeated（+施加状态清理+延迟/准备作废）→ 主将阵亡即本局结束
     │   │   └─ round_end
     │   └─ game_end（胜/负/平 + 终局快照；战时状态/伪随机记账/本局属性修改随局清空回滚）
     ├─ battle_end
     └─ 组装 battle_report（严格符合冻结 Schema）    battle/report.py
```

## 二、机制清单

| 机制 | 精要 | 状态 | 文件 |
|---|---|---|---|
| 确定性规则 | 单一 RNG 流、显式遍历序、整数运算、舍入约定。全项目最高优先级约束 | B1 落地 | [determinism.md](determinism.md) |
| 事件流与播放分组 | 事件即播放协议；seq/t/parent_seq/group_id 四件套 | B1 落地 | 契约 `docs/schema/battle_events.md` |
| 系列连战 | 每局 1 准备 + 8 正常回合；仅主将阵亡判负；平局残血续战最多 7 局 | B1 落地 | 本文件 §一 |
| 行动顺序 | 队内 (速度↓, 站位, id) 排序；跨队按速度差锚点概率 roll 先手（普通随机，D-09） | B1 落地 | `battle/engine.py` |
| 伤害公式 | Phase 3 双公式：兵刃 `360+武-统`、谋略 `360+智-½统-½智`（min=1 截断）；兵力系数 `0.5+0.5x`；新增独立额外增伤乘区 | Phase 3 落地 | [damage.md](damage.md) |
| 格挡/闪避/反弹 | 落账前 0 结算：按状态施加序逐实例判定（次数格挡→闪避→几率格挡→反弹）；damage.mitigation 事件化，不算实际受伤不触发响应；反弹把本应受伤害回敬攻击者（special 不连锁） | v3.2 落地 | [damage.md](damage.md) §五 |
| 震荡/特殊伤害 | is_special 伤害正常播放（damage_class=special）但不触发任何产生伤害的响应 | Phase 3 落地 | [damage.md](damage.md) §六 |
| 性格系统 | 每武将一条性格强制修正机制；trait_trigger 事件带台词（确定性轮换）；概率可测试覆盖 | Phase 3 落地 | [traits.md](traits.md) |
| 治疗公式 | max_troops×5%×系数×智力修正(0.6~1.5)×增减疗×随机×暴击；只回伤兵 | B2 落地 | `battle/formulas.py` |
| 兵力三池 | troops/伤兵/阵亡；受击 30/70 拆分；回合始伤兵 30% 损耗；治疗只回伤兵 | B2 全量落地 | `battle/formulas.py` |
| 受击率选人 | 受击点数 5000 起、按损兵比例扣减（每次重算）；敌方随机目标按点数加权；选人过程事件化（target_select，schema 1.1.0） | B1 落地，B4 事件化 | [targeting.md](targeting.md) |
| 效果原语 | 伤害/治疗/施加/移除状态/属性修改 五入口 + 暴击乘区 + 吸血/固定伤害/无视防御扩展 | B2 落地，B3 扩展 | [effects.md](effects.md) |
| 状态系统 | 一等公民：来源/层数/持续/互斥默认规则/DoT/禁制/修正聚合 + 响应钩子/动态修正 | B2 落地，B3 扩展 | [statuses.md](statuses.md) |
| 死亡清理 | 阵亡即退出：不再行动/不可为目标/施加状态事件化全清/延迟与准备作废/不复活 | B2 落地，B3 补边界 | [statuses.md](statuses.md) §6 |
| 战法架构 | 战法=类+注册；时机 active/prepare/pursuit + 状态响应钩子；全局响应优先级 | B3 全量落地 | [effects.md](effects.md) §3 |
| 伪随机补偿 | 战法触发保底（fail 补偿真累计一局内，D-09）；先手/暴击用普通随机 | B2 落地 | `battle/pseudo_random.py` |
| 数值等价验证 | 新旧核同种子批 1000 场统计对比，胜率差 ≤4pp、均值差 ≤2% | B2 通过 | `docs/dev/numeric_equivalence.md` |
| 单挑 | 第 1 局开局、双方均有武力>90 触发；拒绝率=差×8%封顶80%；胜率=50%+差×5%；负者四维-10 仅第 1 局 | B3 落地 | [duel.md](duel.md) |
| 追击 + 连击 | 追击=普攻命中后时机（禁普攻即无追击）；连击率≥100% 普攻两次、每击独立追击 | B3 落地 | [pursuit_combo.md](pursuit_combo.md) |
| 连携 | 主将神谕释放后两副将自带主动按**各自触发率**立即释放（kind=assist），不占正常释放机会 | Phase 3 修订 | [assist.md](assist.md) |
| 犹豫 | 特殊，刷新不叠层；一窗一 roll 整体延后 1 回合（N→N+1）；计次统一前移至 action_start；已寄存延迟不受影响 | Phase 3 修订 | [hesitation.md](hesitation.md) |
| 准备型战法 | prepare→release 两段协议；forbid_active 施加即打断（interrupted 事件） | B3 落地 | [effects.md](effects.md) §3 |
| 控制状态交互矩阵 | 缄默×准备、石化×暴击、犹豫×冥锁等逐格结算，逐格配测试 | B3 落地 | [status_interactions.md](status_interactions.md) |
| 武将池 v3.1 | 四阵营 24 将（神/人 8+8、海/冥 6+6），每人性格+自带+拆解战法；四维=基础+成长×(等级-1) | Phase 3 落地 | `battle/roster.py`、战法 `battle/skills_{gods,men,sea,underworld}.py` |

## 三、关键决策引用

- 全部已确认决策见 `docs/dev/decisions.md`（D-01~D-14，2026-07-05 批复；
  B3 实现期补录 D-15~D-21、B4 补录 D-22~D-23 待审阅）。
- 数值标定来源与旧 core 分析见 `docs/dev/v0_analysis.md`。
- 事件流契约（冻结）：`docs/schema/battle_events.md` + `battle_events.schema.json`。
