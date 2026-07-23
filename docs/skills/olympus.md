# 奥林匹斯阵营战法（Phase 4 v4，faction=olympus）

> 实现 `battle/skills_gods.py`；索引见 [index.md](index.md)。
> 赫尔墨斯条目见 [sea_underworld.md](sea_underworld.md)（实现仍在本模块）。

## thunder_oracle 雷霆神谕（宙斯·自带·神谕）

- **效果**：己方全体【雷霆】（整局）：造成非落雷实际伤害后 70% 追加落雷——触发者
  智力 85% 魔法，每人每回合最多 3 次。目标已被原伤害击败时不判定。
- **实现**：伪随机（失败+9%/成功-7%/30%~85%/4 次保底）；kind=`lightning` 不自触发；
  多情分神当回合全队雷霆不触发。
- **事件流**：status_apply(thunder)×N → 触发时 status_tick + damage(magic)。

## zeus_bolt 天雷击（宙斯·拆解·主动 50%）

- **效果**：对敌全体 200% 魔法伤害。
- **实现**：逐目标；收局即停。
- **事件流**：skill_trigger(cast) → damage×N。

## athena_aegis 埃癸斯圣盾（雅典娜·自带·神谕）

- **效果**：①己方【圣盾】：受伤或受控 12% 免疫并反弹给敌方随机存活单位（固定特殊伤害）；
  ②己方统率最低单位单次受伤超受击前兵力 10% → 雅典娜治疗（智力×0.9），每回合最多 2 次；
  ③雅典娜【圣盾·守心】首次硬控消耗免疫。
- **实现**：`reflect_rate_bps` + 控制减免链；明睿旁骛当回合圣盾不生效。
- **演出**：反伤成功时持盾者卡面中央渐变闪 `VFX/icon_aegis`（待上传）。
- **事件流**：reflect damage / status_tick；守心 status_tick。

## athena_guard 神盾格挡（雅典娜·拆解·被动）

- **效果**：己方全体前 3 回合格挡率 +30%（`block_rate_bps`）；**三回合以后**全队统率 +35。
- **实现**：prepare 施加 3 回合格挡率；永久载体 `on_round_start`（round>3）施加统率 +35。
- **事件流**：prepare status_apply → round≥4 attr/status（统率）。

## ares_warfury 战神怒火（阿瑞斯·自带·神谕）

- **效果**：敌我全体【血战】（通用易伤 +20%、暴击伤害 +50%）；己方武力最高【战神之勇】
  （武 +20、速 +20，整局；并列小站位）。
- **实现**：`blood_battle`（`vulnerable_bps` + `crit_damage_up_bps`）/ `ares_might`。
- **事件流**：prepare status_apply。

## ares_frenzy 战争狂热（阿瑞斯·拆解·被动）

- **效果**：自身物理伤害 +30%、暴击率 +15%（整局）。准备阶段入场。
- **实现**：`physical_damage_up_bps=3000` / `crit_rate_bps=1500`。
- **事件流**：prepare status_apply。

## delphi_revelation 德尔斐启示（阿波罗·自带·神谕）

- **效果**：己方全体【神示】四维各 +30（整局，平加）。
- **实现**：`divine_revelation`。
- **事件流**：prepare status_apply×N。

## apollo_blessing 日光祝祷（拆解·主动 45%）

- **效果**：己方全体武/智/统 +25（2 回合，可叠 2 层）。
- **实现**：`sun_blessing` `max_stacks=2`。
- **事件流**：skill_trigger → status_apply×N。

## asclepius_oracle 蛇杖庇护圣谕（自带·神谕）

- **效果**：己方【蛇杖庇护】：受实际伤害后 40% 治疗（0.5% 兵力上限 + 施放者智力×1），
  **每持有者每回合最多 2 次**；施放者【灵蛇看护】回合结束治疗己方兵力比最低者一次（同基数）。
  施放者阵亡全部移除。
- **实现**：伪随机（失败+8%/成功-6%/20%~70%/5 次保底）；`SNAKE_MAX_PER_ROUND=2`。
- **事件流**：status_tick → heal。

## asclepius_kiss 灵蛇之吻（拆解·主动 50%）

- **效果**：驱散己方兵力比最低者 1 种负面，并治疗（智力×2.5，可暴击）。
- **实现**：驱散 + heal。
- **事件流**：skill_trigger → status_remove + heal。

## artemis_hunt 月影狩猎（自带·被动）

- **效果**：整局自身造成伤害 +30%；自由选敌类伤害 60% 优先后排（4~6）；无后排则正常选敌。
- **实现**：`prefer_backline_bps`（targeting.md）。
- **事件流**：prepare 挂载。

## artemis_arrow 猎月之矢（拆解·追击 40%）

- **效果**：普攻后 360% 魔法；若目标为敌方当前兵力比最低者，再追加 100% 魔法。
- **实现**：`TIMING_PURSUIT`。
- **事件流**：skill_trigger → damage（+ 可选追加）。

## nike_wings 胜利羽翼（自带·神谕）

- **效果**：己方武力最高与智力最高各获【胜利羽翼】。每回合开始持有者获 1 次【必胜】；
  击败敌方再获 1 次（**每回合击败额外至多 +1**）。双料最高只发一份；r=0 即带首回合次数。
- **实现**：`forced_crit_charges`；`_consume_forced_crit`。
- **事件流**：round_start / on_kill 充能。

## nike_paean 凯歌（拆解·主动 45%）

- **效果**：己方全体获得【先攻】（**2 回合**）。
- **实现**：`first_strike` duration=2；无对敌犹豫副效果。
- **事件流**：skill_trigger → status_apply(first_strike)×N。
