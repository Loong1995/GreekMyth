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


| 钩子                     | 调用位                                              | 使用性格                             |
| ---------------------- | ------------------------------------------------ | -------------------------------- |
| `attr_bonus`           | `effective_attr` 聚合                              | 多情/明睿/狡黠/光明/孤月/逐苹/坚忍/护主/忠烈       |
| `on_round_start`       | round_start 逐武将（回合 roll、设旗标）                     | 多情/明睿/好战/鲁莽/谋深/逐苹/巧射/坚忍/记仇/忠勇/回春 |
| `hesitation_immune`    | 犹豫延迟判定前                                          | 明睿/威权（恒免）、谋深（回合 roll）            |
| `force_basic_target`   | 普攻/自由选敌选人前（强制目标）                                 | 记仇（怒涛）、好战/逐苹（随机目标）               |
| `prefer_target`        | 自由选敌候选池收缩                                        | 狡黠（后排）、鲁莽（统率最高）                  |
| `damage_out_bonus`     | deal_damage 临时增伤                                 | 记仇 +25%、鲁莽 +15%                  |
| `damage_in_reduce`     | deal_damage 临时减伤                                 | 魅惑 -10%                          |
| `crit_damage_bonus`    | 暴击乘区                                             | 巧射 +15%、冷酷 +10%                  |
| `basic_lifesteal`      | 普攻吸血                                             | 贪食 10%（暴食已随卡律布狄斯下架）              |
| `ally_damage_in_bonus` | deal_damage 队友承伤加成（Phase 4，hero_order 序扫描目标存活队友） | 魅惑（敌对塞壬队友 +10%）                  |


> 状态侧新钩子 `on_status_inflicted`（任意状态成功施加/刷新后全局定序分发，
> 防递归）见 `battle/statuses.py`，当前用户：死亡凝望盯【诅咒】。
> | `heal_up_bonus` | calc_heal 增疗 | 仁心 +15%、柔波 +10%（师者已随喀戎下架） |
> | `flip_heal_lowest` | 治疗最低目标选定前 | 仁心（20% 改治疗对面） |
> | `forced_crit_on_taken` | 受击暴击判定前 | 踵之弱（傲慢，7.5% 该次必暴） |
> | `pursuit_boost_bps` | 追伤最终伤害 | 傲慢（25% ×1.5） |
> | `attr_drain_multiplier` | 吸取属性结算 | 威权（20% ×2） |
> | `on_kill` | 己方击杀后 | 求胜（四维+10） |
> | `on_any_defeat` | 任意阵亡后 | 好战（15% 额外行动一轮） |
> | `on_petrify_out` | 石化别人时 | 孤怨（8% 照影自身石化） |
> | `block_denied` | grant_block 前 | 坚忍（5% 执拗） |
> | `burst_rate_bonus` | 连发判定前（`effective_burst_rate`，Phase 4） | 借宝（每神友军+15%）/忠勇（波塞冬存活+30%），均只加成自带战法 |
> | `on_skill_cast` | 主动战法每次成功释放后（含连发每一发，Phase 4） | 忠烈（连发层数） |
> | `on_ally_combo` | 己方任意单位连击触发后（Phase 4） | 号召（速度层数+台词） |
> | `on_ally_basic` | 己方其他单位普攻每击结算后（先于状态协击钩子，Phase 4） | 并辔（coord_certain 旗标） |
> | `on_round_end` | 回合结束（持有者存活） | 羁留（20% 清友军冰锢） |
> | `trait_flag` | 回合级旗标 | `oracle_suppressed` / `own_skill_disabled` / `postpone` / `force_target:*` / `random_basic_target` / `lumang_boost` / `block_denied` / `hesitation_immune` |

行动排序：`postpone` 旗标使该武将顺延至回合末（畏战/算计过深）。

## 3. trait_trigger 事件与台词

- 仅任务书标注「播放台词」的触发发 `trait_trigger` 事件
（payload `{hero_id, trait_id, effect, line}`）；纯数值静默修正不发。
- 台词按触发次数**确定性轮换**（`hero.trait_line_seq[effect] % len(pool)`），
不消耗 RNG；每性格 2~4 条短台词、正/负面分列。
- 挂靠：结算引发的触发 parent 指向引发事件；回合级触发自成播放组。
- **判定与台词错开的性格**（鲁莽 boost/taunt、傲慢 heel）见
  [hero_specials.md](hero_specials.md) §1——不以本节默认「判定即弹」理解。



## 4. 24 将性格总表


| 阵营  | 武将      | trait_id     | 一句话                                                                                                         |
| --- | ------- | ------------ | ----------------------------------------------------------------------------------------------------------- |
| 神   | 宙斯      | duoqing 多情   | 每女性存活智力+8；敌女将逐个 8% 独立判定分神（本回合神谕不触发）；每个触发的女将各播一条其专属故事台词（effect=`distract_<template_id>`，池外女将回退通用 `distract`） |
| 神   | 雅典娜     | mingrui 明睿   | 免犹豫、智力+5；8% 匠心旁骛（本回合圣盾不生效）                                                                                  |
| 神   | 阿瑞斯     | haozhan 好战   | 任意阵亡后 15% 额外行动一轮（**每回合最多 1 次**，trait 旗标限次）；8% 普攻目标全随机                                                       |
| 神   | 赫尔墨斯    | jiaoxia 狡黠   | 速度+10；30% 自由选敌优先后排（`is_backline` 站位 4~6；候选须后排/非后排并存才 roll）                                                  |
| 神   | 阿波罗     | guangming 光明 | 智力+12                                                                                                       |
| 神   | 阿斯克勒庇俄斯 | renxin 仁心    | 治疗+15%；20% 治疗最低前改治疗对面最低单位                                                                                   |
| 神   | 阿尔忒弥斯   | guyue 孤月     | 速度+8                                                                                                        |
| 神   | 尼刻      | qiusheng 求胜  | 己方击杀后自身获【求胜】层（四维各+10，**上限 3 层**，满层不再刷新）                                                                     |
| 人   | 阿喀琉斯    | aoman 傲慢     | 无条件 25%→追伤×1.5 + 贯穿台词（pierce）；受击 7.5% 踵之弱被必暴（台词延到暴击伤害落账后） |
| 人   | 帕特洛克勒斯 | bonong 点将   | 己方武/智/速最高单位造成伤害各 +8%（一人兼多项可叠加）                                                                                    |
| 人   | 赫拉克勒斯   | lumang 鲁莽    | 40% 回合增伤+15%（台词在本回合首次造成伤害前）；60% 优先打统率最高（嘲讽台词在该击造成伤害前） |
| 人   | 奥德修斯    | moushen 谋深   | 20% 回合免犹豫；8% 算计过深顺延回合末                                                                                      |
| 人   | 珀尔修斯    | jiebao 借宝    | v4：每名奥林匹斯（神）存活友军使**自带主动战法**连发率+15%（burst_rate_bonus）                                                        |
| 人   | 阿塔兰忒    | zhuping 逐苹   | 速度+12；6% 金苹果普攻目标随机                                                                                          |
| 人   | 帕里斯     | qiaoshe 巧射   | 暴伤+15%；8% 畏战顺延回合末                                                                                           |
| 人   | 大埃阿斯    | jianren 坚忍   | 统率+10；5% 执拗无法获得格挡                                                                                           |
| 人   | 赫克托尔    | zhonglie 忠烈  | 统率+10；自带主动每次释放叠 1 层连发率+15%（≤2 层，整场）                                                                         |
| 人   | 伊阿宋     | haozhao 号召   | 己方连击触发后速度+8（整场 ≤4 层）；每回合首次播台词                                                                               |
| 人   | 卡斯托耳    | bingpei 并辔   | 己方其他单位普攻后 15% 设 coord_certain（双子协战必成功，消费即清），每回合 ≤1 次                                                        |
| 海   | 波塞冬     | jichou 记仇    | 对最后伤害己者+25%；40% 怒涛本回合强制指向其                                                                                  |
| 海   | 安菲特里忒   | roubo 柔波     | 治疗+10%                                                                                                      |
| 海   | 特里同     | zhongyong 忠勇 | v4：波塞冬存活时自带战法连发率+30%；6% 号角走音禁自带战法                                                                           |
| 海   | 塞壬      | meihuo 魅惑    | v4：敌对己伤害-10%；敌对己方同阵营队友伤害+10%（存活时生效）                                                                         |
| 海   | 斯库拉     | tanshi 贪食    | 普攻吸血 10%                                                                                                    |
| 海   | 卡吕普索    | jiliu 羁留     | 对【冰锢】目标伤害 +12%；回合结束 20% 为受控友军清除冰锢                                                                          |
| 冥   | 哈迪斯     | weiquan 威权   | 免犹豫；吸取属性 20% 翻倍                                                                                             |
| 冥   | 美杜莎     | guyuan 孤怨    | 石化别人 12% 照影自身石化 1 回合                                                                                        |
| 冥   | 珀耳塞福涅   | huichun 回春   | 40% 回合开始自回复 60% 智力兵力                                                                                        |
| 冥   | 卡戎      | —            | （无性格，v3.1 表未配）                                                                                              |
| 冥   | 塔纳托斯    | lengku 冷酷    | 暴伤+10%                                                                                                      |
| 冥   | 刻耳柏洛斯   | huzhu 护主     | 哈迪斯在场全属性+5                                                                                                  |
| 冥   | 赫卡忒     | chalou 岔路    | 对【冥火】目标造成伤害 +10%                                                                                            |




## 5. Phase 4 A2 增补

- **新性格（A3 英雄批已接线到 roster）**：
  - zhonglie 忠烈（赫克托尔）：统率+10；自带主动每次释放得 1 层
  【忠烈·连发】（连发率+15%，整场，最多 2 层，作用于全部主动战法）。
  - haozhao 号召（伊阿宋）：己方连击触发后速度+8（整场，最多 4 层）；
  每回合首次触发播台词（rally）。
  - bingpei 并辔（卡斯托耳）：己方其他单位普攻后 15% 设 `coord_certain`
  回合旗标（双子协战必成功且不计入协击上限，判定消费即清），每回合最多 1 次。
- **约战机械修正已废除**（2026-07-21）：不再有 `DuelBehavior`（必应战/强制搦战/
  拒绝率加成/低武入场）。单挑拒绝与胜负只走武力差公式；单挑台词走
  `voice_lines` 模板双池（非 Trait.lines）。细则见 duel.md。

