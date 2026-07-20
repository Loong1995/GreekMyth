# 海域·冥界战法（Phase 4 v4，文档第三阵营）

> **文档分册**：与奥林匹斯、英雄并列的第三册（震荡/节奏 + 吸取/处决）。
> 实现仍分模块：`battle/skills_sea.py`（faction=`sea`）+
> `battle/skills_underworld.py`（faction=`underworld`）；奥德修斯战法在
> `skills_men.py`，赫尔墨斯神谕/戏言在 `skills_gods.py`。索引见 [index.md](index.md)。

---

# 一、海域 sea（震荡与节奏控制）

## poseidon_oracle 海神三叉戟（波塞冬·自带·神谕）

- **效果**（v4）：己方全体【海神】（整局）：造成非震荡实际伤害后逐次 70% 判定
震荡（首次判失即停）——对原目标的一名未被本次震荡命中过的存活友军
（首个必异于受击目标）造成原伤害 50% 的固定震荡伤害，**单次伤害最多 2 次**。
震荡继承物/魔类型、不暴击、不吃增伤/易伤/减伤乘区、不触发吸血、不再触发海神。
- **实现**：震荡 = 特殊伤害（`damage_class=special`）；kind=`trident` 不自触发。
- **事件流**：status_tick(poseidon_tide) + 子 damage（带 target_select）。



## poseidon_torrent 怒涛（拆解·追击 45%）

- **效果**：普攻目标施加【怒涛】（`flood`：受伤 +10%、统率 −15，2 回合），
并追加 2 次 140% 兵刃。
- **实现**：`TIMING_PURSUIT`；`status_id=flood`。
- **事件流**：skill_trigger → status_apply(flood) + damage×2。



## amphitrite_tide 潮汐抚愈（安菲特里忒·自带·被动）

- **效果**：准备阶段挂自身：①每回合开始己方全体受治疗 +10%（1 回合重挂）；
②每回合结束治疗己方兵力比例最低 2 人（智力 ×1.8，可暴击）。
- **实现**：`TIMING_PREPARE`；回合钩子分发。
- **事件流**：prepare 挂载 → round_start/end 子 heal / status_apply。



## amphitrite_grace 海后之泽（拆解·被动）

- **效果**：前三回合结束时治疗己方全体（施放者智力 ×1.8，可暴击）。
- **实现**：`on_round_end`，`round_no≤3`。
- **事件流**：status_tick → heal×N。



## triton_horn 海嗣号角（特里同·自带·主动·初始 100% 递减）

- **效果**：己方全体 1 层格挡 + 统率 +25（整场可叠）；每成功释放发动率 −10%，
最低 20%（`trigger_rate_for`）。
- **性格·忠勇**：波塞冬存活时本自带连发率 +30%；6% 号角走音禁自带；连发/禁用播台词。
- **实现**：动态触发率；`burst_rate_bonus`（忠勇）。
- **事件流**：skill_trigger → grant_block + status（统率）×N；可连发。



## triton_surge 浪涌（拆解·被动）

- **效果**：我军全体【浪涌】（3 回合）：前三回合敌方带【怒涛/洪水】`flood` 时
全体统率 −20；我方每回合开始 70% 获 1 层格挡。
- **实现**：`on_round_start` 格挡 roll；洪水联动见敌方 `flood` 实例存在时施
加/刷新统率 debuff（前三回合）。
- **事件流**：prepare 挂载 → round_start grant_block / status（统率）。



## siren_song 魅音（塞壬·自带·主动 55%）

- **效果**：对敌方武力最高单体 350% 魔法 +【魅惑】1 回合。
- **性格·魅惑**：敌对塞壬伤害 −10%；敌对同阵营队友易伤 +10%（塞壬存活）。
- **实现**：选武力最高；`charm` 状态。
- **事件流**：skill_trigger → damage + status_apply(charm)。



## siren_charm 迷魂之歌（拆解·主动 35%）

- **效果**：对敌 2 人（受击率互斥）各 220% 魔法并【魅惑】1 回合（charm_targeting）。
- **实现**：`pick_distinct_enemies(..., 2)`；rate 220%。
- **事件流**：skill_trigger → damage + charm ×2。



## scylla_maw 六首撕咬（斯库拉·自带·追击 100%）

- **效果**：普攻后对一名**其他**敌军 180% 兵刃；仅 1 名存活敌军时对原目标 180%。
- **实现**：`TIMING_PURSUIT`；fallback 同系数。
- **事件流**：skill_trigger(parent→damage) → damage。



## scylla_bite 撕咬（拆解·追击 35%）

- **效果**：自身速度 +20（2 回合），并对普攻目标 380% 兵刃。
- **实现**：`scylla_bite_speed`；追击伤害 380%。
- **事件流**：skill_trigger → status_apply + damage。



## odysseus_trojan 木马奇谋（奥德修斯·自带·被动）

- **效果**：第 3 回合开始：敌方全体【犹豫】+【木马炸弹】；第 4 回合持有者行动前
起爆——智力 100% 魔法 + 缄默 1 回合。
- **实现**：炸弹 `on_action_start`（priority=5），起爆前 `reason=consumed`；源阵亡移除。
- **事件流**：round_start 施加 → action_start 起爆 damage + silence。



## odysseus_feint 声东击西（拆解·主动 40%）

- **效果**：对敌 2~3 人（等概率）各 220% 魔法，各 40%【犹豫】。
- **实现**：`skills_men.py`；`trigger_rate_bps=4000`。
- **事件流**：skill_trigger → damage + 可选 hesitation。

---



# 二、冥界 underworld（吸取与处决）



## hades_underworld_dominion 冥域君临（哈迪斯·自带·神谕）

- **效果**：己方【冥河血誓】（实际伤害 10% 固定自疗）+【幽影蔽体】（损兵比例×70%
减伤，上限 70%）；自身【冥祭献统】（行动开始从每名其他友军吸统率 10，1:1 转
自身统率并等量智力）。
- **实现**：威权 20% 吸取翻倍；源阵亡清理 attr。
- **事件流**：action_start attr_change（hades_command_loss / hades_int_gain）。



## hades_soul_drain 冥河汲魂（拆解·主动 40%）

- **效果**：吸敌全体各 25 统率与智力（2 回合可刷新可叠），随后全体 180% 魔法。
- **实现**：成对 attr 状态 + AoE damage。
- **事件流**：skill_trigger → attr_change×N → damage×N。



## medusa_gaze 石化凝视（美杜莎·自带·被动）

- **效果**：受敌攻存活后 70%：吸来源 15 智力（整场）+ 石化 1 回合；每回合 ≤3 次；
已石化仍吸智不刷新；源亡不触发；美杜莎亡移除智削。
- **实现**：性格·孤怨 12% 照影自身石化。
- **事件流**：status_tick → attr + petrify。



## medusa_glance 蛇瞳一瞥（拆解·主动 35%）

- **效果**：对敌随机 2~3 人（受击率互斥）各 180% 魔法并石化 1 回合。
- **实现**：`2 + rand_index(2)` 选人数。
- **事件流**：skill_trigger → damage + petrify ×N。



## persephone_seasons 冬春轮转（珀耳塞福涅·自带·被动）

- **效果**：奇数回合结束治疗己方全体（智力 ×1.0）；偶数回合结束敌全体 120% 魔法。
- **实现**：`on_round_end` 奇偶分支。
- **事件流**：status_tick → heal×N 或 damage×N。



## persephone_sprout 春芽（拆解·被动）

- **效果**：准备阶段使自身与随机 1 名友军获【春芽】（4 回合）：受伤 −25%；
每回合开始 60% 恢复施放者智力 ×0.6 兵力。
- **实现**：`damage_reduce_bps`；`on_round_start` 治疗判定（非受伤触发）。
- **事件流**：prepare 施加 → round_start 可选 heal。



## charon_ferry 渡魂船费（卡戎·自带·被动）

- **效果**：任意武将阵亡：①卡戎智力 +15；②治疗己方兵力比最低（智力 ×2.5）；
③对敌兵力比最低 200% 魔法。同亡按 hero_order。
- **实现**：`on_hero_defeated`；kind=`ferry` 不触发摆渡诅咒。
- **事件流**：hero_defeated 钩子 → attr + heal + damage。



## charon_ferryman 摆渡（拆解·被动）

- **效果**：对敌实际伤害后施加【诅咒】（2 回合：智 −20、受伤 +10%；刷新不叠）。
- **实现**：A2 `curse`；ferry 伤害不自触发。
- **事件流**：on_damage_dealt → status_apply(curse)。



## thanatos_scythe 死神镰痕（塔纳托斯·自带·主动 55%）

- **效果**：对敌兵力比最低 350% 魔法；目标兵力比 ≤30% 时本次 +30%（extra_damage_up）。
- **实现**：`trigger_rate_bps=5500`；pre_damage 条件增伤。
- **事件流**：skill_trigger → damage。



## thanatos_gaze 死亡凝望（拆解·被动）

- **效果**：敌方被成功施加【诅咒】时 60% 对其 150% 魔法；每回合 ≤3 次，同次施加只一次。
- **实现**：`on_status_inflicted`；防递归旗标。
- **事件流**：status_inflicted → status_tick → damage。



## cerberus_bite 三首噬咬（刻耳柏洛斯·自带·追击 40%）

- **效果**：普攻后对目标追加 3×110% 兵刃；全部结算后存活则【恐惧】1 回合。
- **实现**：`TIMING_PURSUIT`；A2 `fear`。
- **事件流**：skill_trigger → damage×3 → fear。



## cerberus_guard 守门恶犬（拆解·被动）

- **效果**：整局受伤 −15%；受击后 20% 反打 60% 兵刃（狮皮口径，无削力）。
- **实现**：复用 `LionCounter` 钩子，无 weaken。
- **事件流**：status_tick → damage(counter)。



## hermes_oracle 赫尔墨斯神谕（自带·神谕）

- **效果**：敌方【扰心印记】（仅第 1 回合行动窗 50%【犹豫】延迟 50%、持续 2 回合）；
我方【神使印记】（整局回合开始 50% 先攻 1 回合）。赫尔墨斯亡印记/犹豫随源清。
- **实现**：在 `skills_gods.py`；`first_strike`。
- **事件流**：prepare 挂印 → round/action 触发 hesitation / first_strike。



## hermes_jest 神使戏言（拆解·主动 50%）

- **效果**：我方速度最高获【先攻】（1 回合）；敌方速度最高获【犹豫】——
**下次行动窗必延后**其主动与普攻（整体延 1 回合，不 roll 失败）。
- **实现**：`hesitation(delay_rate_bps=10000, duration_rounds=1)`。
  「持续 1 回合」按持有者自身行动窗计次：覆盖其**下一次**行动窗口
  （该窗计次=1 仍生效并做犹豫判定；再下一窗计次=2 到期），见 statuses.md §3。
- **事件流**：skill_trigger → first_strike（友）+ hesitation（敌）。

---



## 已下架

- **卡律布狄斯**（charybdis_* / 暴食）：v4 下架，代码与花名册已移除。

