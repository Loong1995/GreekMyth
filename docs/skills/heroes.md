# 英雄阵营战法（Phase 4 v4，faction=heroes）

> 实现 `battle/skills_men.py`；索引见 [index.md](index.md)。
> 奥德修斯战法条目见 [sea_underworld.md](sea_underworld.md)。

## achilles_wrath 阿喀琉斯之怒（自带·被动）

- **效果**：自身物理暴击率 +35%（整局）；每次物理/魔法暴击后对原目标追加 80%
  兵刃（无视统帅、**可暴击**），每回合最多 **7** 次；追加可再触发本战法（链式）。
- **实现**：性格·傲慢——无条件 25% 判定成功则追伤系数 ×1.5 + 贯穿台词。
- **演出**：追伤近身突进；裂甲长矛 ExtraIcon **仅贯穿成功时**渐变闪（`ExtraIconRequiresPierceBoost`）。
- **事件流**：status_tick(achilles_wrath) + 子 damage(physical, kind=fury)。

## patroclus_standin 代战（帕特洛克勒斯·自带·被动）

- **效果**：准备阶段自身挂【代战】载体。轮到自身行动窗时，主动/普攻前依次：
  ①我方武力最高→敌方武力最高 **100% 兵刃**；②我方智力最高→敌方智力最高
  **100% 谋略**；③我方速度最高→敌方速度最高 **100%**（该我方武≥智则兵刃，
  否则谋略）。并列取小站位；缺端跳过。缄默/石化不禁止（非主动/普攻）。
- **实现**：`TIMING_PREPARE` 挂 `patroclus_standin`；`on_action_start` 借手
  `resolve_patroclus_matchups`（`source_id`=出手友军）。
- **演出**：`BorrowBlade` Melee——每段由伤害 `source_id` 武将突进斩击。
- **事件流**：prepare status_apply → action_start → status_tick + damage×1~3。

## patroclus_armor 披甲（拆解·主动 55%）

- **效果**：同代战三道结构，系数 **80%**。
- **实现**：`trigger_rate_bps=5500`；受缄默/石化禁止。
- **演出**：同自带，借刀 Melee。
- **事件流**：skill_trigger → damage×1~3。

## achilles_thrust 怒火突刺（拆解·追击 40%）

- **效果**：自身暴击率 +20%（2 回合，刷新不叠加）；对普攻目标 300% 兵刃；
  该击暴击则追加 80% 兵刃（不可暴击）。
- **实现**：`TIMING_PURSUIT`；`achilles_thrust_crit`。
- **事件流**：skill_trigger → status_apply + damage（+ 可选追伤）。

## heracles_trials 十二试炼（自带·被动）

- **效果**：受攻击后 70%：武力 +6、物理吸血 +3%（累计），对两名敌（受击率互斥）
  各 60% 兵刃；每次试炼后下一次兵刃系数 +5%（可叠，消费于下一笔非试炼兵刃）。
  每局 ≤12、每回合 ≤4；持续伤害可触发。
- **实现**：kind=`trial`/`counter` 不触发（防递归）；`on_pre_damage_dealt` 消费
  `next_phys_rate_bps`。
- **事件流**：status_tick → attr + damage×2。

## heracles_counter 狮皮反击（拆解·被动）

- **效果**：受攻击后 70% 反打 45% 兵刃，并使来源伤害 −15%（1 回合；反击成功必挂）。
- **实现**：`LION_COUNTER_STATUS`（`rate_bps=7000`，`weaken_rate_bps=10000`）。
- **事件流**：status_tick → damage(counter) + lion_weaken。

## perseus_relics 镜盾疾袭（自带·主动 60%）

- **效果**：1~2 段（等概率）每段 120% 兵刃；每段 60% 优先后排；段后 1 层格挡
  （2 回合，最多 2 层）。
- **性格·借宝**：每名奥林匹斯存活友军使本自带连发率 +15%；连发播台词。
- **实现**：`prefer_backline` roll；`grant_block(max_charges=2)`。
- **事件流**：skill_trigger（可 burst_no）→ damage + block ×段。

## perseus_mirror 镜盾辉映（隐藏被动）

- **效果**：整局免疫石化（`petrify_immune`）。
- **实现**：roster `hidden_skills` 自动装配。
- **事件流**：prepare status_apply。

## perseus_flash 镜盾闪击（拆解·主动 55%）

- **效果**：自身 1 层格挡 + 敌单体（受击率）280% 兵刃。
- **实现**：`trigger_rate_bps=5500`。
- **事件流**：skill_trigger → block + damage。

## hector_warcry 特洛伊战吼（自带·主动 45%·准备 1）

- **效果**：释放对敌全体 190% 兵刃；每目标独立 50% 缄默 / 50% 缴械（各 1 回合）。
  **连发不重新准备**。
- **性格·忠烈**：统率 +10；自带每次成功释放叠 +15% 连发率（≤2 层）。
- **实现**：`_cast_active_skill` 连发；`zhonglie_burst`。
- **事件流**：prepare → release/cast → damage + silence/disarm；可连发组。

## hector_assault 决死猛攻（拆解·主动 50%）

- **效果**：敌全体 180% 兵刃；每成功释放本战法系数 +20%，最多 5 次
  （`hector_assault_stack`，连发每发都计）。
- **实现**：`trigger_rate_bps=5000`。
- **事件流**：skill_trigger → damage×N + 叠层。

## atalanta_swift 疾风女猎（自带·被动）

- **效果**：整局速度 +35；若本回合先于所有敌军行动，普攻后对敌两人（互斥）各 140% 兵刃。
- **实现**：`TIMING_PREPARE`；先手旗标 + on_damage_dealt(basic)。
- **事件流**：prepare 挂载 → 条件 status_tick + damage×2。

## atalanta_dash 疾走（拆解·被动）

- **效果**：整局速度 +20；前三回合自身伤害 +20%。
- **实现**：`atalanta_dash_speed` / `atalanta_dash_damage`。
- **事件流**：prepare status_apply×2。

## paris_fatal_arrow 致命一矢（自带·被动）

- **效果**：整局暴击率 +30%、**暴击伤害 +50%**；攻击暴击率 ≥50% 的目标必暴。
- **实现**：`crit_damage_up_bps=5000`；`forced_crit`。
- **事件流**：prepare 挂载；伤害结算读 modifiers。

## paris_heelseek 觅踵（拆解·被动）

- **效果**：攻击暴击率 ≥30% 的目标时伤害 +35%。
- **实现**：`on_pre_damage_dealt` → `damage_up_bonus`。
- **事件流**：prepare 挂载。

## ajax_shield 七重牛皮盾（自带·被动）

- **效果**：整局统率 +20%（百分比乘区）；前三回合每回合开始 2 层格挡（最多持有 2）。
- **实现**：坚忍执拗 `block_denied` 当回合不可获格挡并播台词。
- **事件流**：prepare 挂载 → round_start grant_block。

## ajax_bulwark 坚壁（拆解·主动 60%）

- **效果**：己方兵力比最低 2 名各 1 层格挡 + 统率 **+40**（2 回合）。
- **实现**：`trigger_rate_bps=6000`；`command_delta=40`。
- **事件流**：skill_trigger → block + status ×2。

## jason_expedition 英雄远征（自带·被动·准备）

- **效果**：武力最高者【清醒】前 2 回合；每回合开始当前武力最高者连击率 +35%（1 回合重选）。
- **性格·号召**：己方连击后自身速度 +8（≤4 层，每回合首次台词）。
- **实现**：`clear_mind`；`jason_expedition_combo`。
- **事件流**：prepare → clear_mind；round_start combo。

## jason_command 金羊号令（拆解·主动 70%）

- **效果**：武力最高 2 名连击率 +40%（2 回合）；若施加前已有连击率，额外伤害 +10%，
  **最多叠 2 层**（2 回合）。
- **实现**：`jason_command_damage` `max_stacks=2`。
- **事件流**：skill_trigger → combo ± damage 状态。

## castor_twin 双子协战（自带·被动）

- **效果**：队友普攻后 50% 协击普攻（不占行动、不连击、可追击），每回合最多 2 次。
- **性格·并辔**：己方其他单位普攻后 **15%** 使本次协战必成功，**不计入**协击上限
  （`coord_certain`，每回合最多 1 次）。
- **实现**：`perform_coordinated_attack`；`normal_attack.kind=coordinated`。
- **事件流**：on_ally_basic → status_tick → normal_attack(coordinated)。

## castor_chase 并辔追击（拆解·被动）

- **效果**：自身吸血 +10%；队友普攻后 35% 协击，每回合最多 1 次。
- **实现**：与双子共用钩子、独立计数。
- **事件流**：同协击。

## 已下架

- **喀戎**（chiron_* / 师者）：v4 下架，代码与花名册已移除。
