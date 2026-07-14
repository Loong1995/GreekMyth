# 旧 core 分析报告（v0_analysis）

> Step A 产出 1/4。分析对象：`battlecore/`（只读）。所有结论以代码实际行为为准。
> 本文档四部分：①可保留资产 ②文档与代码 match 核对 ③必须重构的问题 ④属性术语统一表。

## 一、可保留的资产（逐条，注明源文件）

### 1.1 数值公式与平衡参数（已标定，全部保留）

| 资产 | 内容 | 源文件位置 |
|---|---|---|
| 伤害主公式 | `Damage = round((BaseDamage + AttrDiff×8) × TroopCoef × 增伤 × 减伤 × 易伤 × 克制 × 随机 × 暴击 × skill_rate) + 固定追加`；最终 `min(max(1, D), 目标当前兵力)` | `battlecore/engine/damage_calculator.py::calc_damage` |
| 基础伤害 | `BASE_DAMAGE = 390`（与兵力无关，不随 skill_rate 缩放） | 同上，常量区 |
| 属性差映射 | 正差 1:1（≥1000 截顶 +1000）；负差 ≥-30 时 1:1，(-200,-30] 按锚点 (-30,-30)(-40,-33)(-50,-36)(-100,-41)(-200,-45) 分段插值，≤-200 固定 -45 | `damage_calculator.py::_map_attr_diff` |
| 属性差系数 | `ATTR_DIFF_COEF = 8` | 同上 |
| 兵力系数 | `TroopCoef = 0.4 + 0.6 × (current / 10000)`，分母是全局 MAX_TROOPS=10000 而非武将自身上限，不截断 | `damage_calculator.py::calc_troop_coef` |
| 乘区上限 | 增伤 ≤+100%、减伤 ≤80%、易伤 ≤+100%、随机 ∈[0.95,1.05]、暴击默认 2.0 | 同上，常量区 |
| 攻防属性选取 | 兵刃=武力 vs 统率；谋略=智力 vs 智力；真伤=武力 vs 0 | `damage_calculator.py::_get_attack_defense_attrs` |
| 治疗主公式 | `Heal = healer.max_troop × 0.05 × heal_rate × HealAttrCoef × 治疗增减 × 随机 × 暴击 + 固定`；HealAttrCoef=1+（智力-100)/10×10%，clamp[0.6,1.5] | `damage_calculator.py::calc_heal` |
| 伤兵/阵亡拆分 | 受击伤害 30% 阵亡、70% 伤兵；治疗只回伤兵、不复活、不超上限 | `damage_calculator.py::apply_damage / apply_heal` |
| 伤兵自然损耗 | 每回合 ROUND_START 在场武将伤兵池 30% 转死兵，current 不变 | `damage_calculator.py::apply_wounded_to_dead` |
| 速度先手概率 | 速度差 0→50%、1→55%、5→70%、10→80%、≥20→100%，锚点线性插值 | `battlecore/engine/action_order.py::calc_speed_first_probability_bps` |
| 受击率模型 | 受击点数初始 5000；重算=初始-损失兵力比例×3000（非累扣）；实时受击率=自身点数/本方在场点数和（归一）；阵亡者移出分母 | `battlecore/engine/hit_rate.py` + `battle_context.py` 受击率区 |
| 伪随机动态概率 | `current = clamp(base + fail×bonus - streak×penalty, min, max)`；fail≥guarantee 时保底成功；base≥10000 不 roll 不耗 RNG | `battle_context.py::roll_pseudo_random_probability` |
| 伪随机隔离 key | `battleId|casterId|skillId|effectId|targetId|triggerType`，State 按持有者独立累计 | 同上 |
| 标定验收标准 | 30000v30000 纯普攻四象限队伍（高武低统 8 回合主将必死等 4 条） | `battlecore/readme.txt`（约 493-499 行） |

### 1.2 机制设计中合理、应继承的部分

| 设计 | 要点 | 源位置 |
|---|---|---|
| 事件驱动 + 双通路状态响应 | REGULAR（timing 驱动）/ SPY（事件驱动）/ NONE（纯数值被动）三分法清晰 | `domain/skill.py::State`、`STATE_RESPONSE_REFERENCE.md` |
| 响应顺序配置化 | 同一事件多个监听者的顺序由 `SpyGroupConfig/RegularGroupConfig` steps 显式声明，未配置者有稳定 tie-break（position→owner_id→state_id），回放确定 | `config/chain_reaction_config.py`、`engine/chain_reaction.py` |
| 追击=连锁配置而非独立 timing | PURSUIT 作为 DAMAGE_SETTLED 连锁的 kind=SKILL 步，普攻被禁则追击自然不触发 | `chain_reaction_config.py` + `battle_context.py::_try_trigger_chain_skill_step` |
| Applied/Settled 两段结算信号 | APPLIED=数值落地（战报权威），SETTLED=打开连锁窗口（SPY 过滤 damage>0 与否自便） | `battle_context.py::apply_damage/apply_heal` |
| Effect 原子化 + 目标别名 | 多段战法拆为顺序 Effect；`store_targets_as / target_from_effect_alias / exclude_effect_aliases` 表达目标关联 | `battle_context.py::execute_skill/resolve_effect_targets` |
| 准备型主动战法两段协议 | PREPARE 段只发 BEFORE 信号、不计 success；RELEASE 段发完整信号链；控制施加时打断全部准备 state | `battle_context.py` 准备型区、`DESIGN_V2.md` §4.5/六 |
| 状态持续按目标 BEFORE_ACTION 计数 | 「持续 1 回合」=至少覆盖目标下一次行动窗口，SLG 语义正确 | `battle_context.py::tick_states_before_actor_action` |
| 阵亡清理规则 | 按 owner/source_actor_id 运行时 id 清理（不按 skill 配置 id 全局删），永久转化类（献祭武力）例外 | `battle_context.py::_purge_hero_battle_presence` |
| RNG 全记录 | 每次随机记录 index/source/reason/roll，可审计 | `rng/deterministic_rng.py` |
| 配置书写纪律 | 每个技能先自然语言描述再结构化配置；文档维护约定写进文件头 | `config/skill_files.py` 文件头注释 |
| 示例战法资产 | 已实现 9 个战法 + 20 个标定用测试战法（瞬发/追击/准备三类 × 物理/谋略）；哈迪斯冥域君临是复杂战法范本 | `config/skill_files.py`、`config/basic_test_damage_skills.py` |
| 英雄模板 | 宙斯/阿波罗/阿斯克勒庇俄斯/哈迪斯四模板 + 自带战法绑定 + 3 可配置槽 | `config/hero_files.py` |

## 二、旧文档与代码 match 程度核对

总体：`DESIGN_V2.md` 与四份 REFERENCE 维护质量较高，主体与代码一致；以下为核对出的偏差。

### 2.1 文档写了但代码未实现（新 core 需全新实现）

| 机制 | 描述所在 | 代码现状 |
|---|---|---|
| 战神怒火（阿瑞斯，全场血战：物理易伤+30%、物理暴击+20%、我方最高武力+5武+5速） | `skill_files.py` ARES_DESCRIPTION | 仅描述，无 build 函数、未注册 |
| 十二试炼（赫拉克勒斯，受击 70% 概率：武力+2、吸血+2%、对随机两敌 60% 伤害，上限 12 次） | 同上 HELAKEKLEOS_DESCRIPTION | 仅描述 |
| 石化凝视（美杜莎，受击 70% 概率：吸取来源 2 智力整场 + 石化来源 1 回合） | 同上 MEDUSA_DESCRIPTION | 仅描述；「石化」状态本身也不存在 |
| 赫尔墨斯神谕（敌方每次行动有几率陷入犹豫 2 回合） | 同上 HERMES_DESCRIPTION | 仅描述；「犹豫」机制不存在 |
| 海神三叉戟 / 沧海潮震（伤害后概率震荡传递，衰减、不重复目标、上限 3 次） | 同上 POSEIDON_DESCRIPTION、`readme.txt` 177-182 行 | 仅描述 |
| 连携 / 单挑 / 连击 | `readme.txt` 多处 | 完全无实现 |
| 系列连战（1 准备回合 + 8 正常回合 × 最多 7 局，残血续战） | `战斗框架v1.0`（GBK 文档）硬性要求 | 无实现：旧 core 单局制，打满按剩余兵力判胜（`finish_by_remaining_troops`），与系列规则直接冲突 |
| 缄默 / 缴械 / 石化 / 中毒 / 燃烧等状态 | 任务书 5.3、readme.txt | 只有冥锁（forbid_basic+forbid_active）；StateType 的 BUFF/DEBUFF/DOT/HOT 是空枚举 |
| 犹豫定义冲突 | `readme.txt` 484 行写「犹豫不可叠加刷新、增加一回合准备时间」 | 任务书 5.3 定义为「可叠加、延后回合数=层数」。**以任务书为准**，差异已记入 decisions.md |
| 指定初始兵力 | 任务书 4.4：`battle_setup` 须支持指定武将初始兵力（系列残血续战、>10000 兵 NPC） | `HeroConfig` 无 `initial_troops`，`build_from_input` 恒 `troops=max_troops` 满编进场；新 core 必须新增该入口（新 Schema 的 `HeroSnapshot.initial_troops` 已定义） |

### 2.2 实现与文档不符（以代码为准）

| 条目 | 文档说法 | 代码实际 |
|---|---|---|
| 受击率是否参与选人 | `DESIGN_V2.md` §一/§六写「仅日志，尚未参与目标选择」 | `battle_context.py::select_targets` 中 ENEMY/RANDOM_ENEMY 已按 `realtime_hit_rate_bps` 加权抽取（`_select_random(weight_by_hit_rate=True)`）；`TARGET_SELECTION_REFERENCE.md` 是对的，DESIGN_V2 过期 |
| 雷霆触发概率 | `DESIGN_V2.md` §九、`skills.md` 写 60% | 配置 `thunder_state.payload.probability_bps=7000`（70%），`skill_files.py` 描述也是 70% |
| 蛇杖庇护治疗量 | `skills.md` 写「智力 × 0.6」 | 配置 `heal_source_intelligence_bps=10000`（×1.0），`skill_files.py` 描述与代码一致 |
| DAMAGE_SETTLED 连锁顺序 | `EVENT_SIGNAL_REFERENCE.md` §3.6 写「冥河→蛇杖→追击→未配置 SPY」；`DESIGN_V2.md` §4.7 写「冥河→蛇杖→追击→雷霆」 | `chain_reaction_config.py` 实际为 冥河→蛇杖→**雷霆→追击**；`STATE_RESPONSE_REFERENCE.md` §7.5 是对的 |
| Effect 目标部分无效 | `TARGET_SELECTION_REFERENCE.md` §5 写「任一无效则整段 Effect 失败」 | `battle_context.py::execute_skill`：仅全部无效才失败，部分无效时对有效子集照常执行 |
| Timing 枚举 | 文档主循环只用 10 个 timing | `enums.py` 还有 `AFTER_BASIC`、`PURSUIT` 两个枚举值，主循环从不进入（死枚举） |
| max_rounds | `战斗框架v1.0` 要求 8 回合硬上限 | `BattleInput.max_rounds` 由调用方传入，无 8 上限约束 |
| 队伍人数 | 任务书允许「可少于 3 人」 | `config/validation.py` 强制每队恰好 3 人 |
| 任务书提到 `reference/golden_replays/` | 任务书 Step A 入口上下文 | 仓库中不存在该目录；实际样例在 `battlecore/tests/output/`（32 个 txt）与 `battlecore/battlecore/references/`（1 个 txt），已按此替代 |

### 2.3 核对结论

- 引擎机制文档（STATE_RESPONSE / TARGET_SELECTION / EVENT_SIGNAL）与代码 match 度约 95%，可作为新 core 机制设计的可信输入。
- `DESIGN_V2.md` 个别小节（受击率、雷霆概率、连锁顺序）滞后于代码。
- `skills.md`、`readme.txt` 是随手笔记性质，数值以 `skill_files.py` 配置为准。
- 「连携/单挑/犹豫/连击/系列连战」确认从未实现，属于新 core 的全新工作量（Step B3）。

## 三、必须重构的问题（五维度，附方案摘要）

### 3.1 确定性

| # | 问题 | 重构方案摘要 |
|---|---|---|
| D1 | 大整数中间量：`calc_damage` 乘区连乘用 `BPS**8`（10^32），依赖 Python 无限精度整数，迁移 C#/Rust 必然溢出 | 新 core 逐乘区两两相乘、每步除以 10000 并统一舍入；舍入规则写入 determinism.md 并配跨语言等价单测 |
| D2 | 伪随机状态用拼接字符串作 key（f-string），键空间与文化区域敏感、序列化开销大 | 改为结构化元组 key（int/enum id），字符串仅用于日志展示 |
| D3 | `_pseudo_random_params` 按 `base_rate_bps == 3500` 等魔数分支选默认参数，隐式规则不可审计 | 伪随机参数全部显式写进战法/效果配置，禁止按面板概率猜档位 |
| D4 | 遍历顺序依赖 dict 插入序（`skill_instances.values()` 等），Python 下确定但属隐式约定 | 新 core 所有可遍历集合定义显式排序键（装配顺序/实例 id），写入机制文档 |
| D5 | 行动顺序速度竞速走伪随机补偿系统，但 key 含 round+slot 每回合都是新 key，补偿机制实际无效、白白复杂 | 新 core 先手判定直接用普通 RNG roll，一次一记录 |

### 3.2 事件流兼容（本次重构第一目的）

| # | 问题 | 重构方案摘要 |
|---|---|---|
| E1 | 事件无分组结构：一次战法释放产生的伤害/连锁/施状态散平在流中，客户端无法折叠为「战法级播放」 | 新 Schema 引入 `group_id/parent_seq` 树状归属（见契约草案） |
| E2 | 无逻辑时间：只有 `round_no + timing`，无局号、无相位内序号，无法支撑系列连战与 seek | 事件带 `t = (game, round, phase, step)` 复合逻辑时间 |
| E3 | 审计事件与播放事件混杂：PRE_TRIGGER_CHECK/TIMING_STARTED 等每 timing 每技能都发，单局事件数千条，客户端要全量下载解析 | 事件分级（播放必需 core / 审计 debug），序列化时可按档输出；brief 档只含播放必需 |
| E4 | `payload` 完全自由 dict，无 schema、无字段编号预留，不能映射 Protobuf | 冻结 JSON Schema，字段命名/编号约定固定，加法式演进 |
| E5 | human_logs 与事件流并行两套输出，战报文本不由事件流推导，两者可能漂移 | 新 core 只产事件流；人读日志由 `replay_dump` 从事件流生成 |
| E6 | 死亡即时 finish 与 defer 分支（`defer_battle_finish`）导致 BATTLE_FINISHED 前后仍可能追加事件，语义绕 | 统一「结算完当前原子效果→统一终局检查」管线，终局后仅允许 BATTLE_END 收尾事件 |

### 3.3 性能

| # | 问题 | 重构方案摘要 |
|---|---|---|
| P1 | `rebuild_indexes()` 在每次 add/remove state 时全量重建（O(全部技能+状态)），战斗内高频 | 增量维护索引；或注册时静态分桶+失效标记 |
| P2 | 日志字符串（f-string 中文战报）无条件构造，纯模拟批跑时白耗 | 逻辑层不产字符串；文本由工具离线从事件流生成（同 E5） |
| P3 | `responded_event_ids` 集合无限增长；`rng.history`、`effect_execution_records` 全量留存 | 审计记录可开关；批量模拟档关闭历史留存 |
| P4 | `_find_skill_instance` 线性扫全部技能实例 | owner→skills 建索引 |
| P5 | 无性能基准 | Step B4 建 `battle/benchmarks/`，目标 ≥100 局/秒 |

### 3.4 可维护性

| # | 问题 | 重构方案摘要 |
|---|---|---|
| M1 | `BattleContext` 上帝类（~2200 行）：主循环、触发、选人、结算、受击率、日志、伪随机全在一个类 | 拆分：回合状态机 / 触发调度器 / 结算管线 / 目标选择器 / RNG 服务，context 只持状态 |
| M2 | 战法特殊逻辑按 tag 硬编码在 `State.execute / should_trigger_by_event`（styx/snake/thunder/shadow_veil/hades 五个 if 分支） | 战法=类+注册（任务书 4.4）：每个战法自持订阅与结算逻辑，引擎只提供时机与原语 |
| M3 | 兼容别名堆积：`chain_reaction.py`/`chain_reaction_config.py` 尾部大段旧名 alias；`ATTR_DIFF_COEF_PER_STEP_BPS` 等文档名与代码名不一致 | 新 core 不带兼容层，命名一次定死 |
| M4 | `_result_for_winner` 硬编码 `"team_a"/"team_b"` 字符串 | 结果用 winner_team_id 直接表达，不做枚举映射 |
| M5 | 死代码：`Timing.AFTER_BASIC/PURSUIT` 枚举、`event_bus.py`（未被引擎使用）、`action_order.py` 顶部重复的 CRIT 常量、`victory_resolver.py`/`timing_dispatcher.py`/`target_selector.py` 一行转发壳 | 新 core 不迁移死代码 |
| M6 | chain_depth 机制形同虚设：`emit_event(chain_depth=…)` 无人传非 0 值，max_chain_depth 永不触发；防递归实际靠 responded_event_ids 和各战法自过滤 | 新 core 连锁深度由派发器自动递增维护，上限行为明确定义并测试 |

### 3.5 运维排查

| # | 问题 | 重构方案摘要 |
|---|---|---|
| O1 | 事件队列超限时仅打一条日志继续出结果（「事件派发中止」），静默产出半截战报 | 违反任务书 6.3：结算异常必须使战斗失败并输出完整上下文；超限=致命错误 |
| O2 | 战报无 core_version / schema_version（BattleInput 只有 config_version） | 战报顶层带 `schema_version + core_version + rng_seed`，客诉一键复现 |
| O3 | 无 replay_dump 工具，排查靠 tests/output 的副产品 txt | Step B4 实现 replay_dump 全量/brief 两档 |
| O4 | golden 样例是测试打印副产品，无冻结与 CI 对比机制 | Step B4 新 golden 入库 + 「改 golden 必须显式说明」CI 规则 |
| O5 | 标定脚本 `bench_basic.py` 运行时改写 `damage_calculator` 模块级常量（BASE_DAMAGE 等），污染同进程后续战斗，破坏纯函数性 | 新 core 平衡参数经参数对象注入，批量模拟/标定工具不改全局状态 |

## 四、属性术语统一表（此后全项目唯一口径）

旧 core 内部字段（`domain/hero_attrs.py`、`domain/hero.py`）与本项目此后统一术语：

| 内部字段（代码/配置/事件流） | 中文（策划/玩家） | 英文展示 | 用途定论 |
|---|---|---|---|
| `force` | 武力 | Might | 兵刃（物理）伤害的进攻属性；真伤进攻属性 |
| `intelligence` | 智力 | Hex | **单一字段三用**：谋略（魔法）伤害的进攻属性、谋略伤害的防御属性、治疗结算属性。旧 core 中「谋略」与「智力」就是同一字段，本项目沿用此定论，不拆分 |
| `command` | 统率 | Guard | 兵刃伤害的防御属性 |
| `speed` | 敏捷 | Speed | 行动顺序与先手竞速 |

配套定论：

- 「四维」= force / intelligence / command / speed，单挑败者「四维 -10」即这四个字段各 -10。
- 伤害类型术语：`PHYSICAL`=兵刃伤害（受武力）、`MAGIC`=谋略伤害（受智力）、`TRUE`=真实伤害（视对方防御为 0）。任务书中「魔法伤害受谋略属性影响」的「谋略属性」即 `intelligence`。
- 兵力池三分：`troops`（当前可战兵力）/ `wounded_troop`（伤兵，可被治疗）/ `dead_troop`（阵亡，战中不可逆）。
- 万分比：全项目概率与系数一律整数 bps（10000=100%），沿用旧 core。
- 事件流对外字段名使用内部字段名（force/intelligence/command/speed），中英文标签仅用于展示层。

## 五、结论

数值层（公式、锚点、标定参数）与机制概念层（Timing 主循环、REGULAR/SPY 双通路、Effect 原子化、Applied/Settled、伪随机补偿、受击率模型）整体成熟，**照单迁移**；工程层（上帝类、字符串日志耦合、事件流无分组无版本、tag 硬编码战法、静默截断）**全部重写**。新 core 以「事件流即播放协议」为第一设计约束，详见《战斗事件流契约草案》与《播放模型设计文档》。
