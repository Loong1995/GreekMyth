# 事件类型明细（payload 字段表 / 触发语义 / JSON 实例）

> `battle_events.md` 的附属文档，随契约一同冻结。信封字段（seq/t/type/parent_seq/
> group_id/hint）见总纲 §3，本文只列各 type 的 payload。示例中信封字段从简。
> 通用约定：武将以 `hero_id` 引用；概率/倍率为 bps 整数；兵力三池变化统一用
> TroopsDelta 结构（§0）。

## 0. 公共子结构

**TroopsDelta**（伤害/治疗/损耗等一切兵力变化都携带，客户端据此免推算直接刷新血条）：

| 字段 | pb# | 说明 |
|---|---|---|
| `hero_id` | 1 | 变化的武将 |
| `troops_before` / `troops_after` | 2/3 | 当前可战兵力 变化前/后 |
| `wounded_before` / `wounded_after` | 4/5 | 伤兵池 变化前/后 |
| `dead_before` / `dead_after` | 6/7 | 阵亡池 变化前/后 |

**StatusRef**（状态引用）：`instance_id`（本局内状态实例唯一 id）、`status_id`
（配置 id，客户端查表拿图标文案）、`owner_id`（挂在谁身上）。

## 1. battle_start

系列开始。阵容快照在顶层 `teams`，此处不重复。
payload：`total_max_games`(pb1, 恒 7)。

```json
{"seq":1,"t":{"g":1,"r":0,"p":0,"s":0},"type":"battle_start","payload":{"total_max_games":7}}
```

## 2. game_start

每局开始。含本局初始兵力三池快照（第 2+ 局承接上局残血；战时状态已清空，无需列出）。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `game_no` | 1 | 1~7 |
| `troops` | 2 | TroopsDelta[]（before=after=本局初值，六武将全列，已阵亡者 troops=0） |

```json
{"seq":2,"t":{"g":1,"r":0,"p":1,"s":0},"type":"game_start","payload":{"game_no":1,
 "troops":[{"hero_id":"A1","troops_before":10000,"troops_after":10000,
  "wounded_before":0,"wounded_after":0,"dead_before":0,"dead_after":0}]}}
```

## 3. round_start / 18. round_end

回合边界。payload：`round_no`(pb1)（0=准备回合，1~8=正常回合）。
round_start 是 ROUND_START 相位结算（伤兵损耗 troops_change、DoT status_tick 等）
的组根。round_end 同理承载回合末结算。

```json
{"seq":40,"t":{"g":1,"r":1,"p":3,"s":0},"type":"round_start","payload":{"round_no":1}}
```

## 4. action_start

某武将行动窗口开始。该武将本次行动引发的一切（普攻、主动战法、连锁）都在同一
`t.p=4` 相位、同一 `t.s` 下，但组根是各动作事件而非 action_start（见总纲 §3.2）。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `actor_id` | 1 | 行动武将 |
| `order_no` | 2 | 本回合行动顺位（1 起） |
| `skipped` | 3 | 可选。整轮无法行动（如石化+缄默全禁）时 true，此时本窗口无后续动作事件 |

```json
{"seq":41,"t":{"g":1,"r":1,"p":4,"s":0},"type":"action_start","payload":{"actor_id":"A1","order_no":1}}
```

## 5. normal_attack

普攻宣告（组根）。伤害结果由子 `damage` 事件表达。被缴械/冥锁/石化禁普攻时
不发本事件（无演出）。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `actor_id` | 1 | 攻击者 |
| `target_ids` | 2 | 目标（当前规则单目标，留数组防演进） |
| `strike_no` | 3 | 连击序数：1=第一击，2=连击第二击 |
| `target_select` | 4 | 可选（schema 1.1.0 加法演进）：TargetSelectRecord[]，本次宣告前的受击率选人记录，见 §23 |

```json
{"seq":42,"t":{"g":1,"r":1,"p":4,"s":0},"type":"normal_attack",
 "payload":{"actor_id":"A1","target_ids":["B2"],"strike_no":1,
  "target_select":[{"reason":"basic:A1:1","selected_id":"B2",
   "candidates":[{"hero_id":"B1","hit_bps":4400},{"hero_id":"B2","hit_bps":5000}]}]}}
```

## 6. skill_trigger

战法触发宣告（组根），`kind` 覆盖任务书要求的区分：

| kind | 语义 |
|---|---|
| `cast` | 瞬发主动 / 被动（追击等）正常释放 |
| `prepare` | 准备型战法进入准备（本次不结算） |
| `release` | 准备完成正式释放 |
| `interrupted` | 准备被打断（被缄默/冥锁/石化控制施加时），无子结算事件 |
| `delayed` | 受犹豫影响延后（`delay_rounds` 固定为 1，D-02 二次修订），实际结算时另发一条 kind=release 的事件 |
| `assist` | 连携触发的立即释放 |

| payload 字段 | pb# | 说明 |
|---|---|---|
| `actor_id` | 1 | 施放者 |
| `skill_id` | 2 | 战法配置 id |
| `kind` | 3 | 上表枚举 |
| `target_ids` | 4 | 本次宣告选中的目标（prepare/interrupted/delayed 可为空） |
| `delay_rounds` | 5 | 仅 kind=delayed（D-02 二次修订后恒为 1） |
| `interrupted_by` | 6 | 仅 kind=interrupted：打断来源 StatusRef |
| `target_select` | 7 | 可选（schema 1.1.0）：select_targets 期间的受击率选人记录，见 §23 |

```json
{"seq":50,"t":{"g":1,"r":0,"p":4,"s":0},"type":"skill_trigger","parent_seq":0,
 "payload":{"actor_id":"A1","skill_id":"thunder_oracle","kind":"cast","target_ids":["A1","A2","A3"]},
 "hint":{"intensity":"strong"}}
```

注：雷霆神谕为神谕类，准备回合（r=0）对己方全体施加【雷霆】状态，`kind=cast`；
它不是准备型战法，勿与 `prepare/release` 两段协议混淆。

## 7. damage

一次伤害落地（一定挂在某组根之下）。数值为最终结果，客户端不重算。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `source_id` | 1 | 伤害来源武将（来源已阵亡的 DoT 不存在——阵亡即清状态） |
| `target_id` | 2 | 受击者 |
| `damage_type` | 3 | `physical` / `magic` / `true` |
| `amount` | 4 | 最终伤害值（=troops 减少量） |
| `is_crit` | 5 | 是否暴击（任务书 5.2 硬性要求） |
| `troops` | 6 | TroopsDelta（含 30/70 阵亡伤兵拆分结果） |
| `target_select` | 7 | 可选（schema 1.1.0）：状态响应钩子内的选人（试炼反打/三叉戟震荡等）随其伤害事件带出，见 §23 |
| `mitigation` | 8 | 可选（schema 1.2.0，1.3.1 增 `"reflect"`）：`"block"` / `"evade"` / `"reflect"`。0 结算减免——amount 恒 0，不算受到实际伤害、不触发任何受击响应；客户端据此播格挡/闪避/反弹动画。`reflect`（圣盾）随后跟 status_tick + 子 damage(special) 把本应受伤害反弹给攻击者 |
| `damage_class` | 9 | 可选（schema 1.2.0）：`"special"` 标震荡等特殊伤害——正常播放，但不触发任何产生伤害效果的响应（雷霆/血誓/反打等对其不响应） |

```json
{"seq":51,"t":{"g":1,"r":1,"p":4,"s":0},"type":"damage","parent_seq":50,"group_id":50,
 "payload":{"source_id":"A1","target_id":"B1","damage_type":"magic","amount":712,"is_crit":false,
  "troops":{"hero_id":"B1","troops_before":9200,"troops_after":8488,
   "wounded_before":560,"wounded_after":1058,"dead_before":240,"dead_after":454}}}
```

## 8. heal

一次治疗落地。伤兵转兵力，不复活、不超上限。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `source_id` | 1 | 治疗来源 |
| `target_id` | 2 | 受疗者 |
| `amount` | 3 | 实际转化量（受伤兵池与上限截断后） |
| `is_crit` | 4 | 是否暴击 |
| `troops` | 5 | TroopsDelta |

## 9. status_apply / 10. status_refresh

状态施加 / 已存在时的刷新或叠层（负面默认不可刷新不叠加，被拒绝时**不发事件**——
无状态变化即无播放）。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `status` | 1 | StatusRef |
| `source_id` | 2 | 施加来源武将 |
| `stacks` | 3 | 施加/刷新后的总层数（不可叠加状态恒 1） |
| `duration_rounds` | 4 | 剩余持续回合（-1=整场/整局） |

石化施加若打断准备，配套的 `skill_trigger(kind=interrupted)` 与本事件同组
（parent 同指控制来源的组根），两种特效并列播放（任务书 5.3 缄默行要求）。

```json
{"seq":52,"t":{"g":1,"r":1,"p":4,"s":0},"type":"status_apply","parent_seq":50,"group_id":50,
 "payload":{"status":{"instance_id":301,"status_id":"petrify","owner_id":"B1"},
  "source_id":"A1","stacks":1,"duration_rounds":1}}
```

## 11. status_tick

状态发动宣告（组根），两种情形共用：
① 周期结算（DoT/HoT，通常在 ROUND_START 相位）；
② 事件驱动的状态触发（如【雷霆】携带者造成伤害后判中，追加落雷）。
实际数值由子 `damage` / `heal` 事件表达；事件驱动情形下本事件的 `parent_seq`
指向引发它的结算事件（因果链），`group_id` 为自身（独立演出单元）。
payload：`status`(pb1, StatusRef)、`source_id`(pb2)。

## 12. status_remove

状态移除。payload：`status`(pb1)、`reason`(pb2)：
`expired`（到期）/ `dispelled`（被驱散，parent 指向驱散动作）/
`source_defeated`（来源阵亡清理，parent 指向 hero_defeated）/
`game_end`（局末清空——整批清理不逐条发事件，由 game_end 语义覆盖，此值仅备驱散类演出需要）。

## 13. attr_change

属性修改（单挑败者四维-10、增益/献祭类整场修改）。一次多属性修改发一条。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `hero_id` | 1 | 被修改者 |
| `changes` | 2 | 数组：`{attr: "force"/"intelligence"/"command"/"speed", before, after}` |
| `scope` | 3 | `temporary`（随状态走）/ `game`（本局，如单挑败者四维-10）/ `series`（整系列，预留） |
| `source_status` | 4 | 可选 StatusRef：由状态承载的临时修改 |

```json
{"seq":12,"t":{"g":1,"r":0,"p":2,"s":0},"type":"attr_change","parent_seq":10,"group_id":10,
 "payload":{"hero_id":"B1","scope":"game","changes":[
  {"attr":"force","before":95,"after":85},{"attr":"intelligence","before":70,"after":60},
  {"attr":"command","before":80,"after":70},{"attr":"speed","before":88,"after":78}]}}
```

## 14. troops_change

非伤害/治疗途径的兵力池变化，目前仅：伤兵自然损耗（每回合 ROUND_START 伤兵池
30% 转阵亡，troops 不变）。payload：`reason`(pb1, 枚举，现仅 `wounded_decay`)、
`troops`(pb2, TroopsDelta)。挂在 round_start 组下。

## 15. hero_defeated

兵力归零退出战斗。挂在致死的 `damage` 之下；其引发的状态清理（`status_remove`,
reason=source_defeated）再挂在本事件之下。
payload：`hero_id`(pb1)、`killer_id`(pb2, 可选)、`is_main_hero`(pb3)。

## 16. duel_challenge

单挑叫阵（组根，DUEL 相位）。**仅第 1 局**开局、所有战法执行前判定一次；
第 2 局及以后不再单挑（决策 D-03）。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `challenger_id` | 1 | 高武力方 |
| `defender_id` | 2 | 低武力方 |
| `challenger_force` / `defender_force` | 3/4 | 双方武力（供演出对比） |

## 17. duel_result

拒绝或接受后的结果，挂在 duel_challenge 之下；败者四维-10 由子 `attr_change`
（scope=game，仅第 1 局有效）表达。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `accepted` | 1 | false=拒绝叫阵（此时无 winner，无 attr_change） |
| `winner_id` / `loser_id` | 2/3 | 仅 accepted=true |

```json
{"seq":11,"t":{"g":1,"r":0,"p":2,"s":0},"type":"duel_result","parent_seq":10,"group_id":10,
 "payload":{"accepted":true,"winner_id":"A1","loser_id":"B1"}}
```

## 19. game_end

单局结束。payload：`game_no`(pb1)、`winner_team_id`(pb2, null=平局)、
`reason`(pb3: `main_hero_defeated` / `round_limit`)、`end_round`(pb4)、
`troops`(pb5, TroopsDelta[] 六武将终局快照)。
语义：本事件后本局全部战时状态视为清空（不逐条发 status_remove）。

## 20. battle_end

系列结束。payload：`winner_team_id`(pb1, null=系列平局)、`total_games`(pb2)、
`reason`(pb3: `main_hero_defeated` / `series_limit`)。详细统计在顶层 `result`。

## 21. phase_start

相位开始标记。payload：`phase`(pb1, 总纲 §3.1 枚举)、`round_no`(pb2)。
仅当该相位随后确有事件时发送；ACTION 相位以 `action_start` 代行、不再发
phase_start（避免每武将两条冗余）。

## 22. 发送规则汇总（省体积的三个约定）

1. 空相位不发 phase_start；ACTION 相位由 action_start 代行。
2. 无状态变化不发事件（施加被免疫/拒绝、刷新被默认规则拒绝）——需要「抵抗」演出时
   未来加法式新增 `status_resisted` 类型，本版不含。
3. 局末批量清理不逐条发 status_remove，由 game_end 语义覆盖。

## 23. TargetSelectRecord（公共结构，schema 1.1.0 加法式新增）

受击率选人的过程记录（机制见 `docs/mechanics/targeting.md`）。可选字段
`target_select`（TargetSelectRecord[]）挂在 `normal_attack` / `skill_trigger` /
`damage` 三类事件 payload 上，随「携带该次选人的宣告/结算事件」带出。
客户端可忽略（向前兼容义务）；运维排查与 replay_dump all 档打印使用。

| 字段 | pb# | 说明 |
|---|---|---|
| `reason` | 1 | 选人来源标签（`basic:武将:击序` / `skill:战法id` / 钩子自定义） |
| `candidates` | 2 | 候选池（存活敌方，按站位序）：`hero_id` + 当时受击点数 `hit_bps` |
| `selected_id` | 3 | 加权 roll 命中者 |

## 24. trait_trigger（schema 1.2.0 加法式新增）

性格触发事件（机制见 `docs/mechanics/traits.md`）。仅任务书标注「播放台词」的
触发发送；纯数值静默修正（如速度+10 面板加成）不发事件（省体积三原则）。
客户端收到即弹聊天框播出台词；未知 effect 也必须能播（向前兼容义务）。

| payload 字段 | pb# | 说明 |
|---|---|---|
| `hero_id` | 1 | 性格持有武将 |
| `trait_id` | 2 | 性格 id（`battle/traits.py` 注册表） |
| `effect` | 3 | 触发效果标签（如 `aoman_ignore` 傲慢无视伤害、`haozhan_extra` 好战额外行动） |
| `line` | 4 | 预设台词（后端确定性轮换，不消耗 RNG；可为空串） |

```json
{"seq":88,"t":{"g":1,"r":2,"p":4,"s":1},"type":"trait_trigger","parent_seq":86,
 "payload":{"hero_id":"阿喀琉斯","trait_id":"aoman","effect":"aoman_ignore",
  "line":"凡人的攻击，也配伤到我？"}}
```

挂靠规则：由具体结算引发的触发 `parent_seq` 指向引发事件（同组播放）；
回合级触发（parent_seq=0）自成播放组。
