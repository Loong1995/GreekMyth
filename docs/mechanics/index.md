# 机制主文档（index）

> 只读本文件即可建立全局图景；处理局部问题时只加载对应机制文件。
> 每机制 2-4 句精要 + 链接。当前进度：Phase 3 完成；**Phase 4 已全量落地**
> （连发/协击/四轨势能/站位 1~6、恐惧/诅咒/必胜/清醒/格挡上限、约战注册表、
> 新性格钩子、经理人战术、武将池 v4 共 32 将）。契约 **1.4.1 现行**
> （1.4.0 势能/连发/协击；1.4.1 tactic_applied/exhausted），
> core **battle-0.4.1**（`battle/version.py`）。势能**默认开启**
> （`engine.py` `enable_momentum=True`）。
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
     │   ├─ DUEL 相位：单挑（仅第 1 局；武力>智力参赛 + 配对入池；D-03）→ duel.md
     │   ├─ 准备回合 r=0：timing=prepare 战法按行动顺序施放（神谕/被动入场）；
     │   │   主将神谕后两副将自带主动按各自触发率连携立即释放（Phase 3）→ assist.md
     │   ├─ 正常回合 r=1..（默认打到主将阵亡，兜底 999；rounds_per_game 可覆盖）：
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
| 行动顺序 | 队内 (先攻 first_strike 优先, 速度↓, 站位, id) 排序；跨队按速度差锚点概率 roll 先手（普通随机，D-09），先攻持有者跨队也不 roll 直接先手 | B1 落地，Phase 3 补先攻 | `battle/engine.py` |
| 伤害公式 | Phase 3 双公式：兵刃 `360+武-统`、谋略 `360+智-½统-½智`（min=1 截断）；兵力系数 `0.5+0.5x`；新增独立额外增伤乘区 | Phase 3 落地 | [damage.md](damage.md) |
| 格挡/闪避/反弹 | 落账前 0 结算：按状态施加序逐实例判定（次数格挡→闪避→几率格挡→反弹）；damage.mitigation 事件化，不算实际受伤不触发响应；反弹把本应受伤害回敬攻击者（special 不连锁） | v3.2 落地 | [damage.md](damage.md) §五 |
| 震荡/特殊伤害 | is_special 伤害正常播放（damage_class=special）但不触发任何产生伤害的响应 | Phase 3 落地 | [damage.md](damage.md) §六 |
| 性格系统 | 每武将一条性格强制修正机制；trait_trigger 事件带台词（确定性轮换）；概率可测试覆盖 | Phase 3 落地 | [traits.md](traits.md) |
| 治疗公式 | max_troops×5%×系数×智力修正(0.6~1.5)×增减疗×随机×暴击；只回伤兵 | B2 落地 | `battle/formulas.py` |
| 兵力三池 | troops/伤兵/阵亡；受击 30/70 拆分；回合始伤兵 30% 损耗；治疗只回伤兵 | B2 全量落地 | `battle/formulas.py` |
| 受击率选人 | 受击点数 5000 起（阵型可按站位覆盖）、按损兵比例扣减（每次重算）；敌方随机目标按点数加权；选人过程事件化（target_select，schema 1.1.0） | B1 落地，B4 事件化 | [targeting.md](targeting.md) |
| 阵型系统 | 六套预设精确集合识别（只按站位，无 formation 入参）；雁行阵 1/2/6：点数 10800/10800/5400、1/2 减伤 5%、6 增伤 8%；其余五阵骨架待填 | 2026-07-26 六阵革新 | [formations.md](formations.md) |
| 效果原语 | 伤害/治疗/施加/移除状态/属性修改 五入口 + 暴击乘区 + 吸血/固定伤害/无视防御扩展 | B2 落地，B3 扩展 | [effects.md](effects.md) |
| 状态系统 | 一等公民：来源/层数/持续/互斥默认规则/DoT/禁制/修正聚合 + 响应钩子/动态修正 | B2 落地，B3 扩展 | [statuses.md](statuses.md) |
| 死亡清理 | 阵亡即退出：不再行动/不可为目标/施加状态事件化全清/延迟与准备作废/不复活 | B2 落地，B3 补边界 | [statuses.md](statuses.md) §6 |
| 战法架构 | 战法=类+注册；时机 active/prepare/pursuit + 状态响应钩子；全局响应优先级 | B3 全量落地 | [effects.md](effects.md) §3 |
| **响应/触发序** | 伤害先守后攻；同持有者他人施加优先于自身；跨持有者 priority→hero_order | 2026-07-20 定稿 | [response_order.md](response_order.md) |
| **英雄特殊处理** | 鲁莽/踵之弱台词时点、神谕借手结算归因、雷霆/圣盾等演出特例 | 2026-07-20 | [hero_specials.md](hero_specials.md) |
| **状态台词** | 控制/犹豫/先攻临「产生影响」的执行节点发 trait_trigger（trait_id=status）；每类 3 条确定性轮换、parent_seq=0 自成组 | 2026-07-20 | [status_voice.md](status_voice.md) |
| 伪随机补偿 | 战法触发保底（fail 补偿真累计一局内，D-09）；先手/暴击用普通随机 | B2 落地 | `battle/pseudo_random.py` |
| 数值等价验证 | 新旧核同种子批 1000 场统计对比，胜率差 ≤4pp、均值差 ≤2% | B2 通过 | `docs/dev/numeric_equivalence.md` |
| 单挑 | 第 1 局开局、武力>智力参赛；拒绝率=差×8%封顶80%；胜率=50%+d（d≥50必胜）；负者四维-10 仅第 1 局 | B3 落地 | [duel.md](duel.md) |
| 追击 + 连击 | 追击=普攻命中后时机（禁普攻即无追击）；连击率≥100% 普攻两次、每击独立追击 | B3 落地 | [pursuit_combo.md](pursuit_combo.md) |
| 连携 | 主将神谕释放后两副将自带主动按**各自触发率**立即释放（kind=assist），不占正常释放机会 | Phase 3 修订 | [assist.md](assist.md) |
| 犹豫 | 特殊，刷新不叠层；一窗一 roll 整体延后 1 回合（N→N+1）；计次统一前移至 action_start；已寄存延迟不受影响 | Phase 3 修订 | [hesitation.md](hesitation.md) |
| 准备型战法 | prepare→release 两段协议；forbid_active 施加即打断（interrupted 事件） | B3 落地 | [effects.md](effects.md) §3 |
| 控制状态交互矩阵 | 缄默×准备、石化×暴击、犹豫×冥锁等逐格结算，逐格配测试 | B3 落地 | [status_interactions.md](status_interactions.md) |
| 武将池 v4 | 四阵营 **32** 将（奥林匹斯 7/英雄 10/海域 7/冥界 8），每人性格+自带+拆解战法；四维=基础+成长×(等级-1) | Phase 4 A4 落地 | `battle/roster.py`、战法 `battle/skills_{gods,men,sea,underworld}.py` |
| 连发 + 协击 + 站位 1~6 | 主动战法可配连发率（伪随机、同窗硬上限 7、burst_no 事件化）；on_ally_basic_attack 钩子 + 协击原语（普攻口径、不连击、可追击、不连锁）；position 1~6（4~6 后排） | Phase 4 A1 落地 | [burst_coordination.md](burst_coordination.md) |
| 四轨势能 | 每武将四轨按类型跨技能累计；满 5 当次起同轨 cut_in、4 分客户端闪光；**每回合 round_start 全体清零**；metadata 门控（默认开） | Phase 4 A1 落地 | [momentum.md](momentum.md) |
| 经理人战术 | 注册表驱动（集火/保护/攻守倾向）；回合头逐队结算、变更最早第 2 回合、每方 2 次上限；tactic_applied 事件（1.4.1）；变更重算=同 seed 从头重模拟（前缀逐字节等价） | Phase 4 P4-C 落地 | [manager_tactics.md](manager_tactics.md) |
| A2 原语 | 新状态（恐惧/诅咒/必胜/清醒）+ 格挡上限；连发率三来源（战法+状态+性格）；约战注册表（傲慢应战/好战搦战/谋深拒战）；新性格钩子（忠烈/号召/并辔壳已注册） | Phase 4 A2 落地（A3 接线到武将） | [statuses.md](statuses.md) §7、[traits.md](traits.md) §5、[duel.md](duel.md) §5 |

## 三、关键决策引用

- **全项目根本规范**：`docs/discipline/`（确定性/契约/编码红线总纲，改代码前必读）。
- 全部已确认决策见 `docs/dev/decisions.md`（D-01~D-14，2026-07-05 批复；
  B3 实现期补录 D-15~D-21、B4 补录 D-22~D-23 待审阅；
  Phase 2 客户端 C 系列已移历史存档 `decisions_client_phase2.md`）。
- 数值标定来源与旧 core 分析见 `docs/dev/v0_analysis.md`（历史文档）。
- 事件流契约（冻结）：`docs/schema/battle_events.md` + `battle_events.schema.json`。
