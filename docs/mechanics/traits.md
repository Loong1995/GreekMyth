# 性格系统（traits）

> 规则来源：`docs/prompts/phase3_battlecomplete.md` §二/§六（每武将一条性格，
> 增益/发作两面，强制修正战斗机制）。实现：`battle/traits.py`（注册表，同战法模式）。
> 事件：`trait_trigger`（契约 1.2.0 加法演进，payloads §24）。

## 1. 数据模型

- `Trait`（trait_id、name、lines 台词表）+ 钩子方法；`REGISTRY` 注册，
  `HeroTemplate.trait_id` 关联（`battle/roster.py`）。
- 判定一律普通随机（source=`trait`），**不走伪随机补偿**。
- 概率可被 `BattleSetup.metadata["trait_rate_overrides"]`（`"trait_id.key": bps`）
  覆盖——高概率测试版（`gen_reference.py` / `manual_battle.py --trait-override`）。

## 2. 钩子点位（引擎固定调用位）

| 钩子 | 调用位 | 使用性格 |
|---|---|---|
| `attr_bonus` | `effective_attr` 聚合 | 多情/明睿/狡黠/光明/孤月/借宝/逐苹/坚忍/忠勇/护主 |
| `on_round_start` | round_start 逐武将（回合 roll、设旗标） | 多情/明睿/好战/鲁莽/谋深/逐苹/巧射/坚忍/记仇/忠勇/回春 |
| `hesitation_immune` | 犹豫延迟判定前 | 明睿/威权（恒免）、谋深（回合 roll） |
| `force_basic_target` | 普攻/自由选敌选人前（强制目标） | 记仇（怒涛）、好战/逐苹（随机目标） |
| `prefer_target` | 自由选敌候选池收缩 | 狡黠（后排）、鲁莽（统率最高） |
| `damage_out_bonus` | deal_damage 临时增伤 | 记仇 +25%、鲁莽 +15% |
| `damage_in_reduce` | deal_damage 临时减伤 | 魅惑 -10% |
| `crit_damage_bonus` | 暴击乘区 | 巧射 +15%、冷酷 +10% |
| `basic_lifesteal` | 普攻吸血 | 贪食 10%、暴食 8% |
| `heal_up_bonus` | calc_heal 增疗 | 仁心 +15%、师者/柔波 +10% |
| `flip_heal_lowest` | 治疗最低目标选定前 | 仁心（20% 改治疗对面） |
| `forced_crit_on_taken` | 受击暴击判定前 | 踵之弱（傲慢，7.5% 该次必暴） |
| `pursuit_boost_bps` | 追伤最终伤害 | 傲慢（25% ×1.5） |
| `attr_drain_multiplier` | 吸取属性结算 | 威权（20% ×2） |
| `on_kill` | 己方击杀后 | 求胜（四维+10） |
| `on_any_defeat` | 任意阵亡后 | 好战（15% 额外行动一轮） |
| `on_petrify_out` | 石化别人时 | 孤怨（8% 照影自身石化） |
| `block_denied` | grant_block 前 | 坚忍（5% 执拗） |
| `trait_flag` | 回合级旗标 | `oracle_suppressed` / `own_skill_disabled` / `postpone` / `force_target:*` / `random_basic_target` / `lumang_boost` / `block_denied` / `hesitation_immune` |

行动排序：`postpone` 旗标使该武将顺延至回合末（畏战/算计过深）。

## 3. trait_trigger 事件与台词

- 仅任务书标注「播放台词」的触发发 `trait_trigger` 事件
  （payload `{hero_id, trait_id, effect, line}`）；纯数值静默修正不发。
- 台词按触发次数**确定性轮换**（`hero.trait_line_seq[effect] % len(pool)`），
  不消耗 RNG；每性格 2~4 条短台词、正/负面分列。
- 挂靠：结算引发的触发 parent 指向引发事件；回合级触发自成播放组。

## 4. 24 将性格总表

| 阵营 | 武将 | trait_id | 一句话 |
|---|---|---|---|
| 神 | 宙斯 | duoqing 多情 | 每女性存活智力+8；敌女将逐个 8% 独立判定分神（本回合神谕不触发）；每个触发的女将各播一条其专属故事台词（effect=`distract_<template_id>`，池外女将回退通用 `distract`） |
| 神 | 雅典娜 | mingrui 明睿 | 免犹豫、智力+5；8% 匠心旁骛（本回合圣盾不生效） |
| 神 | 阿瑞斯 | haozhan 好战 | 任意阵亡后 15% 额外行动一轮；8% 普攻目标全随机 |
| 神 | 赫尔墨斯 | jiaoxia 狡黠 | 速度+10；30% 自由选敌优先后排 |
| 神 | 阿波罗 | guangming 光明 | 智力+12 |
| 神 | 阿斯克勒庇俄斯 | renxin 仁心 | 治疗+15%；20% 治疗最低前改治疗对面 |
| 神 | 阿尔忒弥斯 | guyue 孤月 | 速度+8 |
| 神 | 尼刻 | qiusheng 求胜 | 己方击杀后自身四维+10 |
| 人 | 阿喀琉斯 | aoman 傲慢 | 目标残兵高于己 25% 追伤×1.5；追伤必播贯穿台词（pierce）；受击 7.5% 踵之弱被必暴 |
| 人 | 赫拉克勒斯 | lumang 鲁莽 | 40% 回合增伤+15%；60% 优先打统率最高 |
| 人 | 奥德修斯 | moushen 谋深 | 20% 回合免犹豫；8% 算计过深顺延回合末 |
| 人 | 珀尔修斯 | jiebao 借宝 | 每神阵营存活友军速度+8 |
| 人 | 阿塔兰忒 | zhuping 逐苹 | 速度+12；6% 金苹果普攻目标随机 |
| 人 | 帕里斯 | qiaoshe 巧射 | 暴伤+15%；8% 畏战顺延回合末 |
| 人 | 大埃阿斯 | jianren 坚忍 | 统率+10；5% 执拗无法获得格挡 |
| 人 | 喀戎 | shizhe 师者 | 治疗+10% |
| 海 | 波塞冬 | jichou 记仇 | 对最后伤害己者+25%；40% 怒涛本回合强制指向其 |
| 海 | 安菲特里忒 | roubo 柔波 | 治疗+10% |
| 海 | 特里同 | zhongyong 忠勇 | 波塞冬在场全属性+10；6% 号角走音禁自带战法 |
| 海 | 塞壬 | meihuo 魅惑 | 受到伤害-10% |
| 海 | 斯库拉 | tanshi 贪食 | 普攻吸血 10% |
| 海 | 卡律布狄斯 | baoshi 暴食 | 普攻吸血 8% |
| 冥 | 哈迪斯 | weiquan 威权 | 免犹豫；吸取属性 20% 翻倍 |
| 冥 | 美杜莎 | guyuan 孤怨 | 石化别人 8% 照影自身石化 1 回合 |
| 冥 | 珀耳塞福涅 | huichun 回春 | 40% 回合开始自回复 60% 智力兵力 |
| 冥 | 卡戎 | — | （无性格，v3.1 表未配） |
| 冥 | 塔纳托斯 | lengku 冷酷 | 暴伤+10% |
| 冥 | 刻耳柏洛斯 | huzhu 护主 | 哈迪斯在场全属性+5 |
