# Changelog

## 2026-07-20 文档大重整（机制类文档「叙述+代码机制」双写）
- 新增 client 三份机制文档：`playback_units.md`（播放单元/时间轴阻塞/台词独占
  三原则/管线四 processor 红线）、`rendering_layout.md`（分辨率适配/图像槽位
  contain-stretch/sorting 层级总表）、`text_system.md`（飘字/气泡/横幅/中文名）。
- 状态台词独立成 `docs/mechanics/status_voice.md`（触发点总表/去重/轮换）；
  hero_specials §3、statuses §8b 改为指针；mechanics/client index 登记。
- pitfalls 追记 P-17~P-21（拆组丢 Root、事件挂靠位置、等待时长失配、
  Resources 路径导入、sortingOrder 遮挡）；playback_model.md 标注历史文档。
- 修 `TraitLineExtract`：抽台词后出击段仍挂原 status_tick/skill Root，
  十二试炼等 Melee 配置不丢；格挡/反弹 SettleDamage 保留命中反馈+轻顿挫。

## 2026-07-20 台词独占播放单元（无缝不重叠）
- `TraitLineExtractProcessor` 抽出混组台词；`SayExclusive` 时长=气泡动画；
  与邻组取消单元停顿，禁止气泡未完就开下一行动组。

## 2026-07-20 状态台词可见 + 落雷贯穿牌面
- 状态台词改为 `parent_seq=0` 独立 TraitLine（此前挂 action_start 被 Node 静默吞掉）；
  Node 防御性也会播挂靠台词；delayed 补「延迟」飘字；brief 打印〔状态〕作祟。
- RemoteStrike 落雷 Y 向拉长，视觉贯穿整张目标牌。

## 2026-07-20 雷霆头像标可见性 + 控制/犹豫/先攻台词
- RemoteStrike：头像标加大、抬高 sortingOrder、先亮后落雷，避免被闪电盖住。
- 新增 `battle/status_voice.py`：缄默/缴械/冥锁/石化/魅惑/恐惧/犹豫/先攻各 3 条，
  仅在真正改写执行时发 `trait_trigger(trait_id=status)`；golden 已重生成。

## 2026-07-20 立绘按槽位等比适配
- `UnitView.FitSpriteToSlot`：立绘/卡框/头像标按 sprite.bounds 等比塞进固定世界槽
  （contain），不同分辨率/PPU 不再忽大忽小。

## 2026-07-20 立绘路径/Sprite 导入说明
- 用户图原在 `Resources/Portraits/` 且非 Sprite → Play 仍色块；已迁到
  `Resources/ClientBattle/Portraits/` 并改 Sprite（含 heracules→heracles）。
  上传指南补路径/导入红线。

## 2026-07-20 取消常规状态上方小图标
- `StatusIconPanel` 只保留控制类中央大图标；增益/神谕等常规状态不再显示卡上方小图标
  （靠光环+飘字）。上传清单同步：勿再做 ~30 张常规 status 图。

## 2026-07-20 取消圣盾弹道采购 / 势能 overflow 不定购
- 文档剔除 `proj_aegis_bounce`（圣盾已 Melee）；`overflow_<track>` 不定购，
  闪光档维持乙案白闪+punch（已购 Vefects 可选日后抠共用 burst，非必须）。

## 2026-07-20 触发序与英雄特殊处理文档
- 新增 `docs/mechanics/response_order.md`（先守后攻 / 他人优先 / 分发点表）；
  `hero_specials.md`（鲁莽·踵之弱台词时点、神谕借手、演出特例）；
  `docs/client/settlement_stats.md`（结算表归因与 status→skill 映射）。
- mechanics/client index、statuses、determinism、traits、performance 交叉链已挂。

## 2026-07-20 鲁莽/踵之弱台词时点 + 结算表神谕归因
- 鲁莽：boost/taunt 台词改到造成伤害前弹出（选人只挂旗，不立刻说话）。
- 踵之弱：判定仍在暴击前，台词延到暴击伤害事件写出后（独立新组）。
- 结算表：status_tick 链伤害/治疗归到状态 `source_id` 的带技能格子
  （如雷霆→宙斯·雷霆神谕，圣盾→雅典娜·埃癸斯）。

## 2026-07-20 性格台词占用 0.5s 时间轴
- 每条 `trait_trigger` 弹气泡后等待 `TraitLinePauseSeconds=0.5`（再乘 DurationMul）；
  独立 TraitLine 组与嵌在行动组内的台词均生效；不另加单元停顿。

## 2026-07-20 镜盾闪击改近战演出
- `perseus_flash` 特殊配置改为 `Melee`（单体主动默认走 PerSegment 弹道，观感不符）。

## 2026-07-20 同持有者触发：他人施加优先于自身
- 单持有者钩子（伤害 taken/dealt、行动开始、伤前、受控）：`source_id≠owner`
  的状态整段先于自身施加，再按 response_priority；跨持有者全局钩子键增
  「他人/自身层」。统一 `_owner_hook_key` / `_global_hook_key`。
- 单测扩 `test_damage_hooks_order`；determinism/statuses/burst 同步；golden 重生成。

## 2026-07-20 伤害响应先守后攻 + 雷霆/圣盾演出
- 引擎 `_dispatch_damage_hooks`：同一次伤害先整段 `on_damage_taken`（守方），
  再整段 `on_damage_dealt`（攻方）；各段内仍按 priority/hero_order/instance。
  determinism/statuses 同步；新增 `test_damage_hooks_order`；golden 因 RNG 消费序重生成。
- 客户端：`thunder`→`RemoteStrike`（不位移，目标宙斯头像+落雷）；`aegis_shield`
  Melee 用 OwnerId 持盾者、Cast 闪光后再突进；StatusTick ActorOf 固定 OwnerId。

## 2026-07-20 阿喀琉斯追伤高光/战斗突进一致
- `achilles_wrath` 特殊配置改为显式 `Melee`（原 StatusTrigger→PerSegment 只飞弹道/
  飘字，高光窗以追伤链为主时观感只剩飘字）；斩击 1.5×；`ActorOf` 空 SourceId 回退 owner。

## 2026-07-20 结算表空键崩溃修复 + 分局 Tab
- `TroopsChangeEvent` 误把整包 payload 当 TroopsDelta → `hero_id` 空 →
  Dictionary 抛 ArgumentNullException；已改为读 `payload.troops`。
- 结算表支持多局 Tab（第 N 局 / 系列合计）；`SetTroops` 对空 heroId 容错。

## 2026-07-20 播放放慢 ×2 + 三谋式战后技能结算表
- 客户端 `DurationMul=2`：动画节拍与行动/单元停顿一并放慢；单元间隙
  0.35s、行动间隙 0.55s（再乘 DurationMul）。
- 系列结束后弹出分队技能统计（×次数 / ⚔杀伤 / +治疗）；
  `BattleSkillStatsAggregator` 从事件流只读归因；Tester「打开结算」可重开。

## 2026-07-20 阿喀琉斯之怒暴击率 35%→25%
- `achilles_wrath` 物理暴击率修正 `physical_crit_rate_bps` 3500→2500；heroes.md 同步。

## 2026-07-20 傲慢贯穿台词误触发修复
- `achilles_wrath` 去掉「每次追伤必播 pierce」：贯穿仅在傲慢判定成功时发出
  （目标残兵比例 > 自身 + 25%）。g1r3 阿喀琉斯伤轻打残血赫克托尔仍连环贯穿属此 bug。

## 2026-07-20 神使戏言持续口径更正（duration=1）
- `hermes_jest` 敌方犹豫改回 `hesitation(10000, 1)`：引擎「持续 1 回合」本就
  按持有者行动窗覆盖下次窗口（计次 1 仍生效），此前误判为会先过期而改成 2。
- 文档：sea_underworld / hesitation.md 与 statuses.md §3 对齐；全技能排查：
  无伤类仅 `perseus_flash`（已补 select_targets）；追击由引擎注入目标；
  其余 duration=2 为设计值，非同类误判。

## 2026-07-20 镜盾闪击无伤修复
- `PerseusFlash` 补 `select_targets`（受击率选敌单体）：此前默认 `[]`，
  发动只叠格挡、永不结算 320% 伤害（manual 战报「镜盾闪击」无↳伤害根因）。

## 2026-07-20 技能文档三阵营重整 + 效果对齐代码
- 文档改为三册：olympus / heroes / **sea_underworld**（海冥合册）；删除
  sea.md、underworld.md、heroes_mech_code.md；roster_v4→roster.md；新增
  code_map.md；index/traits（孤怨 12%、并辔 15%）同步。
- 按效果段改码：阿喀琉斯 +35% 暴/追伤可暴/每回合≤7；狮皮削弱 70%；闪击 55%；
  猛攻 50%；致命一矢暴伤+50%；坚壁 60%/+40 统；金羊伤害叠 2；并辔 15% 且
  不计协击上限；忠烈 +15%/层；神盾后期+35 统；凯歌先攻 2 回合（去犹豫）；
  浪涌洪水抑统；迷魂 35%/220%；撕咬 380%；蛇瞳 2~3；春芽 4 回合回合初治疗；
  镰痕 55%；戏言犹豫 1 回合；孤怨 12%。golden 重生成，208 通过。

## 2026-07-20 英雄阵营机制·代码对照文档
- （已废弃）heroes_mech_code.md 已并入 code_map.md / 三阵营分册。

## 2026-07-20 势能门槛调整：满 5 cut-in / 4 分闪光
- `MOMENTUM_FULL` 8→5；`value≥5` 当次起带 `cut_in`（不再「满后再下一次」）。
- 客户端 `Flash=4` 首次白闪、`Full=5` 常驻流光；计分按轨类型跨技能累计
  （文档强调，逻辑本已如此）。测试/momentum.md/契约文案同步。

## 2026-07-20 连发演出修复：与首发同模板整套重播
- EventPipeline.Classify：`burst_no≥2` 的 skill_trigger 组显式判为 ActiveSkill。
  此前连发组因 parent_seq 指回首发触发事件被误分类为 Pursuit（追击判据是
  parent≠0），导致连发只走追击近身模板/纯飘字，不再重播原战法演出。
- 现在每次连发与首发走完全相同的解析（同 skill_id 特殊配置/组默认）与模板
  （群攻居中弹幕/逐段弹道/施法特效/音效全套），叠加原有 ×1.35 节拍与角标。

## 2026-07-20 Phase 4 验收战报入口（burst_tactics 场景）
- sample.py 新增 `burst_tactics` 场景（波塞冬/特里同/赫克托尔 vs 冥界三将，
  预设战术：A 集火哈迪斯、B 攻势 +1）；seed 42 战报含连发释放 ×10、
  tactic_focus ×17、momentum ×107，已导出 StreamingAssets。
- BattleReportTester 默认 ReportPath 改为 burst_tactics_seed42.json
  （Phase 4 新机制播放验收入口）。全套 208 测试通过。

## 2026-07-20 Phase 4 P4-C（经理人战术系统，schema 1.4.1 / core 0.4.1）
- 新增 `battle/tactics.py`：TACTIC_REGISTRY 注册表（集火 hit_weight×2 /
  保护 减伤8%+HoT / 攻守倾向 ±3%/级），战术=duration 1 状态逐回合刷新，
  不动结算机制、不耗 RNG；配置走 setup.metadata["tactics"]（入战报闭环）。
- 引擎 `_apply_tactics`：round_start 后第一步、setup 队伍序结算；变更
  生效回合发 `tactic_applied`（1.4.1 加法新增，schema json/md/payloads §26
  同步；status_remove.reason 补登 exhausted）。
- **架构定案**：不做引擎快照续算原语——确定性下「第 N+1 回合快照续算」
  ≡「同 seed+变更序列从头重模拟」（毫秒级成本，无快照漏项风险）；
  服务器入口 `tactics.with_change()`。前缀逐字节等价由
  test_tactics.py 固化（8 条新测试，全套 208 通过）。
- 校验红线：变更最早第 2 回合、每方一局至多 2 次、目标敌我归属、
  stance 档位 -2~+2（validate_tactics，simulate 入口调用）。
- 客户端：TacticAppliedEvent 解析 + 非阻塞横幅播报（复用 cut-in 通道）；
  战术栏 UI/替换段播放随联网客户端接入。names/ChineseNames/textlog 同步。
- golden 全量重生成（版本串 1.4.1）+ jsonschema 校验通过 + 带战术战报
  replay_report 字节一致；新文档 docs/mechanics/manager_tactics.md，
  index/determinism 登记。

## 2026-07-20 Phase 4 B3~B6（BGM 分层/飘字手调/皇卡演出/高光回放）
- B3 `Audio/BgmLayerService.cs`：4 stem 按全局势能三档淡入淡出（0~7/8~15/16+，
  GlobalTotal 由 MomentumService 维护回调）、切层对齐小节边界（Bpm 登记）、
  单挑与 cut-in 全层 duck -8dB/0.5s 恢复；stem 缺失回退单曲 bgm_main
  （音量+低通随档），全缺静默 no-op。素材（Suno+Demucs）为人工项。
- B4 `Units/FloatingTextTuning.cs`（SO）：飘字字体/字号/颜色/上浮曲线全参数
  Inspector 可调，缺资产用代码默认；FloatingTextService 保持零 alloc 与
  字形预热；新增操作文档 docs/client/floating_text_tuning.md。
- B5 皇卡演出（C1）：新公共原语 UnitView.ShowPortraitMark（头顶头像标）+
  profile.PortraitMarkKey——宙斯落雷 thunder→zeus、冥域献统
  hades_command_drain→hades；圣盾反弹 aegis_ward 专属回击弹道
  proj_aegis_bounce（占位，P3 到位换资源）。
- B6 高光回放（C2）：PerformanceRunner.PlayHighlight 按我方每武将行动窗
  单窗伤害排行取最大窗重播（窗前静默落账），Tester 播完出「高光回放」按钮。
- 文档：performance_mechanisms 新增 §一c、framework 文件清单、
  assets_upload_guide 增 BGM/字体/新特效音效 key 登记。

## 2026-07-20 Phase 4 B1/B2（客户端势能/连发/协击/cut-in）
- 新增 `Units/MomentumService.cs`：momentum_change 四轨镜像账本（value 取事件
  权威值零加法）+ TrackTable 注册表（轨→tint/标签，加轨即扩展）；action_start
  同步服务器静默清零；SkipToEnd/静默落账路径同样记账。
- UnitView 增四轨势能迷你条（0~3 半亮/4~7 全亮）、满档常驻 rim 流光
  （多轨叠混色+呼吸脉动）、首次满档白闪爆发帧（乙案；甲案待 P2 采购换
  overflow_<track> 特效 key）。
- cut-in 通道（C10 定案）：非阻塞金字横幅 + 轻震屏；触发源=满档轨每次
  cut_in 事件/高伤>3000/行动窗追伤第 5 次；同播放组去重、不做回合级限流。
- 连发演出：burst_no≥2 组节拍 ×1.35（VFXContext.TempoScale）+「连发×N」角标；
  协击 normal_attack.kind=coordinated 出手前挂「协击」青标。
- BattleEvents 补 SkillTriggerEvent.BurstNo / NormalAttackEvent.Kind 解析；
  StreamingAssets 机制验收战报重同步（--export，含 men_gods 连发覆盖）。
- 文档：performance_mechanisms 新增 §一b（势能/连发/协击/cut-in 机制表）、
  client_battle_framework 文件清单登记 MomentumService。

## 2026-07-20 Phase 4 A5（契约 1.4.0 冻结 + golden 全量重生成）
- 版本冻结：schema 1.3.1→**1.4.0**、core battle-0.3.1→**battle-0.4.0**
  （version.py / battle_events.schema.json $id / battle_events.md 演进表定稿）。
- 势能门控收口：`enable_momentum` 默认改为**开启**（metadata 显式 False 可关），
  golden 11 份全量重生成（新增 momentum_change 事件，非势能事件逐条不变，
  test_momentum_enabled_full_battle 固化）；replay_report 字节一致复核通过。
- schema 补漏：status_remove.reason 增补 `exhausted`（充能耗尽摘除，A2 既有
  行为此前未入枚举）；11 份 golden 全部通过 jsonschema Draft2020-12 校验。
- 客户端前向接线：BattleEvents.cs 增 MomentumChangeEvent 强类型解析
  （归 Node 类静默落账，消除 UnknownEvent 告警刷屏），B 批再接切入/UI；
  StreamingAssets 6 份演示战报同步为 1.4.0。
- 全套 201 测试通过；batch_sim 200 种子无异常。

## 2026-07-20 Phase 4 A4（阵营改名/成员调换/属性核对/武将总表）
- faction id 定稿：gods→olympus、men→heroes（roster/traits 借宝判定/客户端
  FactionColors+FactionOf 一次同步；实现模块文件名沿用历史 skills_gods.py 等）。
- 成员调换：奥德修斯→sea、赫尔墨斯→underworld（roster faction + 客户端映射 +
  战法文档条目随迁 sea.md/underworld.md）。终局 29 将 = 7/9/6/7
  （manual_tasks 原"28/英雄10"为口算误差，已更正）。
- 属性对表核对全 29 将：仅珀尔修斯速度基础 96→82 修正，其余与任务书一致；
  faction 不入战报，golden 无需重生成（全套 200 通过）。
- 新增 A5 交付物 `docs/skills/roster_v4.md`（29 将档位/定位/性格/战法/属性
  成长总表）；faction_style.md、project_overview.md §三、effects.md、
  skills/gods.md→olympus.md、men.md→heroes.md（git mv）同步。
- batch_sim 0..1000 全场景无异常（238 局/秒）；standard 场景胜率偏斜
  （A 7.2%）留待 M3 平衡初测统一评估。
- 核对「准备型主动连发不重新准备」口径：引擎既有实现正确
  （_settle_preparing → _cast_active_skill 直接连发），补语义测试
  test_hector_warcry_burst_releases_without_reprepare（全套 201 通过）。

## 2026-07-20 Phase 4 A3 冥界批（v4 重写，A3 四阵营收官）
- 冥域君临：幽影蔽体减伤上限 50%→70%；冥祭献统每友军汲 5→10 统率，改为
  1:1 提统率 + 额外等量智力。冥河汲魂吸取 10→25。
- 石化凝视：吸智 2→15、每回合上限 3 次、来源已石化不刷新石化；蛇瞳一瞥改
  随机 2 人 180% + 石化。春芽改被动（自身+随机友军，减伤 25% + 受击 40%
  回施放者智×0.6，回合上限 2）。渡魂船费新增第三段：对敌最低兵比 200% 魔法。
  摆渡改被动（造成伤害后施【诅咒】）。死神镰痕改即发单体 350%（兵比≤30%
  再+30%）。死亡凝望改盯诅咒（60% 150% 魔法，回合上限 3）。三首噬咬 2→3 次
  并追加【恐惧】1 回合。守门恶犬不变。
- 引擎：新增状态钩子 on_status_inflicted（apply_status 成功施加/刷新后全局
  定序分发，防递归旗标），determinism.md 登记定序规则。
- 场景：men_gods 珀尔修斯→赫克托尔（补准备型战法覆盖）；"准备"机制验收
  golden 从 sea_underworld_seed9 改指 men_gods_seed12。golden 重生成 4 个。
- 测试：test_phase4_underworld.py 8 项；全套 200 通过。文档：
  skills/underworld.md 重写、index/traits/determinism/双端名字表同步。

## 2026-07-20 Phase 4 A3 海域批（v4 重写 + 卡律布狄斯下架）
- 武将池：下架卡律布狄斯（漩涡巨口/吞流/暴食性格全移除，双端名字表与
  阵营映射同步）。
- 改版：三叉戟震荡上限 3→2、移除全队闪避+20%；怒涛削统 -10→-15；潮汐抚愈
  改被动（回合开始全队受疗+10% 1 回合 + 回合结束治疗最低 2 人×1.8）；
  海后之泽改前三回合结束全体治疗×1.8；海嗣号角初始 100%、统率+25、发动率
  衰减下限 20%；魅音 55% 改施加魅惑（原犹豫）；魅惑术改名迷魂之歌；
  六首撕咬孤敌回落对原目标 90%；撕咬追加自身速度+20（2 回合）。
- 性格：忠勇改波塞冬存活时自带战法连发率+30%（burst_rate_bonus）；魅惑 v4
  增「敌方对塞壬同阵营队友伤害+10%」——新 trait 钩子 ally_damage_in_bonus
  （deal_damage 按 hero_order 扫描目标存活队友，叠入攻击方 damage_up）。
- 测试：test_phase4_sea.py 9 项；golden 重生成 3 个（oracle×2/sea_underworld）；
  连携机制验收 golden 从 oracle_seed5 改指 oracle_seed99（seed5 改版后无
  assist）；全套 192 通过。文档：skills/sea.md 重写、index/traits 同步。

## 2026-07-20 Phase 4 A3 英雄批（人阵营 v4 重写 + 新三将）
- 武将池：下架喀戎（战法/性格/花名册全移除）；新增赫克托尔（忠烈·特洛伊战吼
  准备 1 回合+连发不重准备/决死猛攻叠系数≤5）、伊阿宋（号召·英雄远征清醒+
  逐回合连击 buff/金羊号令）、卡斯托耳（并辔·双子协战 50% 协击≤2 次/
  并辔追击 35% ≤1 次+吸血 10%）——协击走 A1 perform_coordinated_attack 原语。
- 改版：阿喀琉斯之怒 80%×5 次可链式（原 120%×3 不链）；狮皮削弱改 50% 判定
  1 回合；镜盾疾袭 60% 1~2 段+格挡（2 回合 ≤2 层）替代闪避层；镜盾闪击改
  主动 65% 格挡+320%；疾风女猎 +35/疾走 +20；七重牛皮盾改统率+20%+2 层格挡
  （≤2）；坚壁改兵力比例最低 2 人。借宝性格改自带战法连发率+15%/神友军。
- 引擎小件：clear_trait_flag（并辔旗标消费）、grant_block 增 duration_rounds
  （限时格挡）。
- 测试：test_phase4_heroes.py 9 项；golden 重生成 3 个（standard×2/men_gods）；
  全套 183 通过。文档：skills/men.md v4 重写、skills/index.md 池状态表、
  traits.md 借宝/新三格接线、names.py + ChineseNames.cs 双端同步。

## 2026-07-20 Phase 4 A3 奥林匹斯批 + 集火战术底层
- A3 奥林匹斯战法 v4 重写（`skills_gods.py`）：圣盾改 15% 免疫反弹到敌方随机
  单位+守心控制格挡+治疗每回合限 2；战神怒火调 20%/+20；血性咆哮下架换
  战争狂热（物伤+30%/暴击+10% 被动）；神使戏言/灵蛇之吻调 50%；灵蛇之吻改
  驱散 1 种；蛇杖治疗每人每回合限 2；月影狩猎改 60% 优先后排
  （prefer_backline_bps）；胜利羽翼改武/智双最高+击杀增益回合限 1；
  凯歌改全体先攻+最低兵 50% 犹豫。性格同步：好战额外行动回合限 1、
  求胜改 3 层状态、狡黠走 is_backline。
- 集火战术底层（P4-C 定案「受击率合理调整」）：新状态修正键
  `hit_weight_up_bps`，选人权重=受击点数×(1+bias)，仍加权随机+保残兵，
  非强制锁定；无偏置逐字节等旧行为。plan/manual_tasks/targeting.md §1b 落档。
- 测试：新增 test_phase4_gods.py 7 项 + 集火权重测试；golden 重生成；
  全套 174 通过，replay 闭环校验通过。文档：skills/gods.md v4 全文重写、
  determinism.md 消费点表补控制减免/反弹落点/优先后排/连发。

## 2026-07-20 Phase 4 A2：新状态/性格原语落地
- Universal Sound FX 人工确认已购，采购文档回填（assets_upload_guide §三、
  manual_tasks §二 P4 改「已购待导入」）。
- 新状态原语（`battle/statuses.py`）：恐惧（禁普攻+追击、伤害-15%，口径临时
  定案 → manual_tasks 拍板项 5）、诅咒（智-20/受伤+10%，负面例外可刷新）、
  必胜（certain_crit 载体 + `grant_certain_crit`，耗尽摘除）、清醒
  （control_immune，CONTROL 施加静默拒绝）；`grant_block` 增 `max_charges`
  持有上限（封顶静默拒绝）。
- 连发率三来源：`effective_burst_rate` = 战法 burst_rate_bps + 状态
  `burst_rate_up_bps` + 性格 `burst_rate_bonus` 钩子。
- 约战注册表（C6）：`traits.DuelBehavior`（必应战/拒绝率加成/低武力叫阵/
  强制搦战）+ 台词 effect（duel_challenge/accept/reject）；`_run_duel`/
  `_duel_champion` 只查表，空表=旧行为。
- 新性格壳注册（A3 接线前零行为差异）：忠烈（自带释放叠连发层，≤2）、
  号召（己方连击后速度+8 叠 4 层+台词）、并辔（10% 设 coord_certain 回合旗标）；
  新引擎钩子 on_skill_cast / on_ally_combo / on_ally_basic（性格先于状态分发）。
- 测试 `test_phase4_primitives.py` 13 项；全套 166 通过，旧 golden 不变。
  文档同步：statuses.md §7、traits.md §5、duel.md §5、mechanics/index、
  names.py 与 ChineseNames.cs 新状态名同步。

## 2026-07-19 Phase 4 开工：A1 服务端底座落地
- 人工工作清单独立成文 `docs/dev/phase4_manual_tasks.md`（4 项拍板按推荐值
  开工、采购 4 项 ¥250~450+订阅、BGM 制作/资源放置/飘字手调操作步骤、
  6 里程碑人工验收点）；随后按人工要求补齐 plan B 附全部内容并**逐项点名**
  （Suno Pro/Udio、Vefects flipbook 系列、Universal Sound FX 先用、
  freesound CC0 关键词、musopen/incompetech 备选、思源黑体/得意黑/站酷字体、
  Demucs htdemucs 命令、SFX key 明细），plan B 附改为指向该文。
- A1 底座（`battle/`）：站位扩展 0~6（4~6=后排，`is_backline` 谓词）；
  主动战法连发（`Skill.burst_rate_bps`，伪随机 key=(hero,skill,"burst")，
  同窗硬上限 7，四释放路径统一走 `_cast_active_skill`，事件带 `burst_no`）；
  四轨势能（`add_momentum`/`momentum_change` 事件/满 8 后 cut_in/自身
  action_start 静默清零/`MOMENTUM_TRACK_OF_KIND` 归轨注册表，
  `setup.metadata.enable_momentum` 门控默认关）；协击
  （`StatusDef.on_ally_basic_attack` 钩子 + `perform_coordinated_attack`
  原语，normal_attack.kind="coordinated"，不连击可追击不连锁）。
- 契约 1.4.0 草案登记（battle_events.md/payloads §25/schema.json：
  momentum_change、burst_no、normal_attack.kind、position 1~6）；version.py
  暂不升版（A 批收口时冻结+重生成 golden）。textlog 补协击/连发/势能格式。
- 新文档 `docs/mechanics/momentum.md`、`burst_coordination.md`，index 登记。
- 测试：新增 `battle/tests/test_phase4_base.py` 9 例（含「开关势能两份战报
  除 momentum_change 外逐事件一致」的纯表现红线固化）；全量 153 passed，
  旧 golden 逐字节不变。

## 2026-07-19 Phase 4 执行计划产出
- 依 `phase4_reply.md` 人工批注定稿 `docs/dev/phase4_plan.md`：10 条已确认决策
  落为需求（势能加分修订/连发减半计/皇卡演出增量/高光回放/约战/BGM 分层/
  经理人指令窗口等）；4 项开工前待拍板（阵营终局形态、28 将名单、
  溢出演出零成本方案、经理人最小指令集）。
- 三批次拆解：P4-A 服务端（底座→原语→四阵营战法→roster v4→契约 1.4.0+golden）、
  P4-B 客户端（势能分级/cut-in/BGM/飘字调参/皇卡演出/高光回放）、
  P4-C 经理人分段模拟；含 6 里程碑与风险对策。未动任何代码。
- phase4_plan 势能定稿：每武将四轨独立计分（主动/被动/神谕/普攻追击），
  各满 8；满档后**该轨**后续每次触发都 cut-in，他轨不连带；主动连发全程
  +1（撤销连发衰减）；C10 取消「单回合至多 1 次」；事件带 track+cut_in。
- phase4_plan 修订（人工纠正与增补）：C6 单挑约战改 trait→duel 注册表驱动；
  A5 新增交付物「完整武将战法文档 v4」；溢出演出补甲案（三拍神格化，仅购
  1 个爆发特效包）；新增采购计划（¥250~450+订阅）与人工工作清单；
  P4-C 全面重写为「左侧战术栏+预设+回合内变更（每方 2 次，下回合生效）+
  服务器逐回合快照续算即时重发完整战报+客户端替换段播放+断线兜底」，
  取消对局快进；全计划补「注册表驱动」可扩展通则。
- B3 补「BGM 素材路线」：禁止著名 BGM AI 变调（侵权红线）；主路线 AI 生成
  （Suno/Udio 商用授权）+ Demucs 开源拆 4 stem，备选公版古典（核验录音授权）
  与 CC-BY 曲库；切层对齐小节、duck -8dB 写入 B3 要点；委托降级为保底。

## 2026-07-15 全量文档校阅 + 新建 docs/discipline 根本规范目录
- 全量校阅 docs 各文档与代码/契约一致性并修复：mechanics（index schema 版本、
  行动顺序补 first_strike、statuses 废字段与钩子分发序、effects RNG 序/测试战法表/
  战法池路径、determinism 窗口顺序与 RNG 登记表、hesitation D-15 引用、
  status_interactions 计次时点）、skills（犹豫参数写法、受击率选人用词、
  镜盾辉映/怒涛命名）、schema（演进表 §7 引用、payloads 压回 ≤300 行）、
  client（框架图补 CollectiveTriggerMergeProcessor、faction_style 全文重写指向
  ClientBattle、石化音效/交叉引用、补 3 个反击 sfx key）、dev（performance/
  v0_analysis/phase3_plan 标注历史；decisions C 系列拆分至
  decisions_client_phase2.md 历史存档，decisions.md 降回 270 行）。
- 新建 `docs/discipline/`（index/project_overview/global_rules/coding_standards/
  doc_standards/ai_workflow_pitfalls 六件）作为任何 AI 开工的根本上下文；
  踩坑录收录 14 条历史教训。`.cursorrules` 重写：阶段更新至 Phase 3 完成/
  schema 1.3.1/ClientBattle 现状，并指向 discipline 目录。
- changelog 超 300 行：2026-07-06 及以前条目拆至 `changelog_archive_phase12.md`。
- 人工修订：文档行数红线 300→**500 行**（doc_standards/index/README/.cursorrules
  同步）；project_overview 新增 §七框架级要求（多分辨率兼容、性能体验、
  商业卖点方向、长期可维护可配置——计划方案与代码实现均须逐条对照）。

## 2026-07-12 鲁莽选人核验 + 反击类演出统一普攻动画
- 核验鲁莽（赫拉克勒斯）"优先选统率最高"：实现即每次选人时用
  effective_attr 实时统率（含 buff/吸取后变动），非入场锁定；探针验证
  统率反超后目标随之切换，无需改动。
- 客户端演出配置补齐反击类：lion_counter（狮皮反击）/ cerberus_guard
  （守门恶犬）/ charybdis_maw（漩涡巨口）统一 Melee 模板走普攻近身动画，
  与已配置的十二试炼反打、圣盾反制口径一致。

## 2026-07-12 客户端：群攻播放单元完整性修复 + 雷霆集体齐发
- 修复群攻（天雷击等）被切碎：初始分组由"连续段合并"改为按 group_id 全量
  聚合——群攻 N 条伤害间被雷霆/试炼等 status_tick（new_group）插队，旧逻辑
  把一次群攻拆成 N 个单元，违反 client_perform「群攻=一个播放单元」。
- 新增 CollectiveTriggerMergeProcessor：同状态同来源的连续 StatusTrigger 组
  合并为一次集体齐发（白名单，当前仅 thunder；圣盾按文档保持逐次触发）。
- performance_mechanisms.md 增补"播放单元完整性"红线。

## 2026-07-12 客户端：石化残留修复 + 节奏放慢
- 修复石化覆盖层/状态图标残留：节点组（round_start/action_start）的子事件
  （状态到期移除、伤兵损耗、属性回写）此前未落账，PlayNode 现遍历全部子事件
  ApplySilently；UnitView.SetPetrified 施加/解除渐变互斥（先杀旧 tween）。
- 节奏调参：行动停顿 0.3→0.45s，播放单元停顿 0.1→0.25s（保持单元<行动）。
- 独立版重打包验证通过（0 错误 0 警告）。

## 2026-07-12 圣盾改反弹 + 减免定序规则 + 去开屏动画
- 雅典娜圣盾伤害反制改为**反弹**：受伤归零，本应受伤害原样反弹给攻击者
  （special 固定量不连锁不可再减免）；反控/重击回复保留。schema 1.3.1
  （mitigation 枚举 +"reflect"，加法演进），core 0.3.1。
- 减免判定定序规则（v3.2）：格挡/闪避/反弹**按状态施加到英雄身上的顺序逐实例
  判定**（同英雄同时点=技能格子顺序即施加序）；单实例内 次数格挡→闪避→
  几率格挡→反弹。golden 全量重生成（RNG 消费序变化），144 测试通过。
- textlog 伤害行标注 被格挡/被闪避/被反弹；客户端飘字支持"反弹!"文案。
- 关闭 Unity 开屏动画（ProjectSettings m_ShowUnitySplashScreen/Logo=0，
  Unity 6 全许可证可用）。

## 2026-07-10 客户端收敛重构 + 文档总纲
- 卡顿最终定案补充：用户系远程桌面观看，冻结为传输层；独立版探针全程 60fps
  零长帧。诊断改为可选（Tester.ShowDiagnostics 开 FrameSpikeProbe），删除
  播放循环内的看门狗日志。
- 独立版体验基线：窗口化 1280x720 可拉伸、vSync 锁刷新率、后台运行不暂停、
  ESC 退出（新 Input System，asmdef 加 Unity.InputSystem 引用；旧 Input 会每帧抛异常）。
- 新增卡牌待机呼吸（立绘错相位浮动）落实"画面任意时刻有活物"。
- docs/client/index.md 新增《总体要求（复现规范）》九条——服务器权威零结算、
  5 层单向数据流、播放单元、零死帧、配置驱动、占位回退、预热前置、机型兼容
  权威、向前兼容；framework/performance_mechanisms 同步本轮全部机制
  （渲染级预热/报告驱动预热/trauma 震动/零死帧原则/性能验证方法）。

## 2026-07-10 卡顿定案：编辑器环境问题，独立版 1380fps 零尖峰
- FrameSpikeProbe 帧探针定位：编辑器 Play 每 3~5 秒一次 ~1.7s 主线程冻结，
  且战斗结束空场（tween=0）仍复现 → 病根在编辑器环境（疑 MCP 桥接周期任务），
  与战斗代码/占位美术无关；Windows 独立版实测平均 ~1380fps、全程无 >66ms 帧。
- 打包曾被 Windows CET（硬件强制堆栈保护）杀掉 UnityLinker/Analytics 进程，
  管理员 Set-ProcessMitigation -Disable UserShadowStack 后打包成功；
  Standalone 已切 Mono + 裁剪 Disabled。
- 零死帧原则落地：DefaultPerformance 删除全部纯定格 WaitForSeconds，节拍全由
  位移/弹道/回身动画时长驱动；VFXManager 预热升级为离屏相机实渲 3 帧
  （shader 编译/贴图上传前移，PlayLoop 等 PrewarmComplete 再开播）；
  报告驱动预热（台词字形/状态图标/合成音效/气泡对象开战前生成）；
  CameraShaker 改 trauma 噪声模型（连抖叠加不瞬移）。诊断工具
  FrameSpikeProbe 保留在 Test/ 供后续机型验证。

## 2026-07-10 客户端节奏：非行动表现零阻塞 + 飘字预热
- 播放队列仅允许主动/普攻/追击/状态触发/单挑等待；回合与局节点、状态变化、
  台词、阵亡、神谕/被动宣告全部即时触发表现后继续，删除其 WaitForSeconds。
- FloatingTextService 开战前预建 24 个 TextMesh 入池，避免第一次密集飘字连续
  创建 GameObject/TextMesh/MeshRenderer 造成主线程尖峰；飘字动画异步，不阻塞队列。
- 飘字动画移除逐条 DOTween Sequence（每条原有 1 Sequence+2 Tween+闭包），改为
  服务统一 Update 驱动并池化动画记录；开战前通过 ChineseNames 全量请求动态字体
  中文/数字字形，避免首次出现新战法名时同步扩充字体纹理造成命中帧卡顿。
- 修正性能判断：Profiler 实测某帧 CPU 49.6ms、GPU 0.32ms、帧分配约 24KB，
  说明用户感知并非 GPU 压力；此前用平均 FPS 判断流畅度的方法无效。

## 2026-07-10 客户端性能：预热入池 + 贴图压缩（手机端准备）
- VFXManager.Prewarm：开战前 25 个特效 prefab 各实例化 1 份入池，消除战斗中
  首次播放的 Instantiate/贴图上传卡顿（此前每个特效第一次触发都会掉帧）。
- 已购三包 280 张贴图全量重导：maxSize 1024 + 压缩 + 关 mipmap，
  贴图内存 723MB→258MB（Vefects 单包 526→106MB）；登记为导入红线。
- OnGUI 的 GUIStyle 每帧 new 改缓存（横幅+调试按钮），消 GC 压力。
- 实测编辑器内平均 73 FPS；此前"卡"另有两个编辑器侧因素：Error Pause
  反复暂停（已关）、特效缩过头视觉误判（已回滚）。

## 2026-07-10 客户端文档整并：资源现状 + 成品化路线
- `assets_upload_guide.md` 重写为唯一资源文档：清单+现状总表（25 特效 key 已配、
  滤镜免上传、图标/立绘/音效/UI 待上传）+ 六步成品化路线（图标→音效→立绘→
  UI→特效精修→配置资产化与验收）+ 采购登记。
- 删除 `to_purchase.md`（内容并入上文 §三），index/performance_mechanisms/
  decisions 引用同步改指。
- 机制文档纠错过期描述：普攻近身现为命中帧闪斩击（非"无刀光"）、
  背景默认无色纯黑（非白底）。

## 2026-07-10 演出：状态常驻光环落地（宙斯闪电缠绕等全量）
- 新增 `Units/UnitAuraService.cs`：status_id→光环 key 表（雷霆闪电缠绕/圣盾/
  战神弱强血红/德尔斐与尼刻阳光/神使与扰心印记），status_apply 挂、
  status_remove/阵亡/整局重置撤；已购包一次性 flipbook 挂载时强制循环+
  补发射密度（≥3/s）+压半透明 0.55，常驻可见且不遮立绘。
- OracleAuraPerformance 不再直挂光环（职责移交光环服务，杜绝重复挂载）；
  光环 variant 目视校准（thunder 0.9 / aegis 1.2 / sunlight·bloodlust 1.1~1.4）。
- client_perform §二~五 特殊演出配置逐条核对齐：25 个 VFX key 全部就位，
  filter_* 三 key 改程序化色罩无需上传。同步 performance_mechanisms /
  client_battle_framework / assets_upload_guide 三份文档。

## 2026-07-10 演出回滚修复：特效不可见 + 棋盘中心固定点
- 昨日"按包围盒归一"缩放过头（拖尾/发射域把包围盒撑到 80+ 单位，按其归一后
  核心视觉缩至不可见）：回滚 10 个 key 到目视校准值（弹道/治疗/命中 1.0、
  剑击/穿刺 0.35、slash 0.25、裂甲图标 0.5），演出全部恢复可见。
- 整盘滤镜弃用粒子 prefab（在棋盘中心常驻成"固定点"）：改为程序化
  BoardFilterOverlay 全屏呼吸色罩（血红/海蓝/冥紫按 key 取色，跟随相机铺满，
  同 key 去重）。透明度待真棋盘底图定稿后人工调。
- 排查发现 Editor Console 的 Error Pause 会被 MCP 截图的 PlayerLoop 报错反复
  触发暂停（仅影响编辑器调试，不影响真机），已关闭该选项。

## 2026-07-10 演出：近身斩击分档 + 演出机制总纲文档
- 普攻/近身与追击命中帧都在被打者身上闪斩击：普攻 ×1.0、追击 ×1.5，
  另乘 profile.StrikeVfxScale 可调；斩击资源基准归一 1.4 世界单位。
- 修缩放覆盖 bug：演出层对特效实例只允许相对相乘（*=），VFXManager 回池
  自动还原出生缩放（VfxOriginalScale），特殊图标同改。
- 新增 `docs/client/performance_mechanisms.md`：全部客户端演出机制总纲
  （一句话结论+代码位置+细则文档索引，模板族/尺寸规范/机型兼容红线），
  index.md 登记为"改演出先看这里"。

## 2026-07-10 客户端：无色棋盘 + 全端分辨率自适配
- 棋盘背景定为无色（相机纯黑，不放底板；上传 `UI/board_background.png` 即自动
  切换为真图 cover 铺满，BackgroundFitter 等比不变形）；相机修正为正交+SolidColor；
  兵力数字中性浅灰、回合横幅白字+阴影双绘，深浅背景都可读。
- 特效尺寸全量归一：逐 variant 模拟 0.5s 实测包围盒，15 个超标项（弹道 80+ 单位、
  剑击 14、光环 10 等）统一缩到 1.8~3 世界单位，滤镜 10；消除"半屏闪烁"。
- 新增 CameraFitter：按屏幕宽高比动态调 orthographicSize，保证设计安全区
  （半宽 4.6/半高 5.2 世界单位）任意机型完整可见，转屏/改分辨率每帧热跟随；
  OnGUI 横幅与调试按钮按屏幕高度缩放。机型兼容红线：表现层不得写死
  orthoSize/像素坐标，一律依赖这两个 Fitter。
- 卡框支持真实资源 `CardFrames/frame.png`（白底图按阵营色染色）；
  assets_upload_guide 补齐卡框/气泡/石化/棋盘背景四件的制作规格与获取来源。
- 修 PlaceholderFactory 跨 Play 会话缓存 Unity 假空引用（PlayOneShot null 告警）；
  斩击包 4 个 variant 缩放归一（0.35~0.5，原尺寸铺半屏）。

## 2026-07-10 客户端资源获取分档方案入文档
- `assets_upload_guide.md` 新增 §二：按立绘/状态图标/特效/音效/UI 五类，
  各给低档（程序占位）/免费（game-icons、kenney、freesound CC0、AI 生图）/
  小成本（Asset Store $5~50）/推荐（已购四包+定制精修）四档获取方法，
  附替换优先级（图标→音效→命中特效→立绘→光环滤镜）。
- 特效 v1 配齐：28 个 VFX key 全部用已购三包做 Prefab Variant 落进
  `Resources/ClientBattle/VFX/`（斩击←2D Slash、雷电/命中/光环/爆炸/滤镜←
  Combat Flipbook、弹道/治疗←四色弹道包），Play Mode 实测渲染正常
  （URP shader 兼容、材质无丢失）。
- `to_purchase.md` 交叉引用该节。另：棋盘布局改上下（A 下 B 上横排），
  单挑对撞位移改为方向无关；Unity 内修 6 个编译错误并新建
  ClientBattleDemo 场景实测战报播放通过。

## 2026-07-09 客户端重构：ClientBattle 战报驱动特效框架
- 依 client_perform.md 全量重构：新框架 `Assets/Scripts/ClientBattle/`（21 文件，
  5 层：事件模型/事件管线/VFXResolver 三级配置/SkillPerformance+Runner/基础设施）。
  默认策略族（群攻中心 AOE/单体逐段/普攻近身/追击/状态触发后置补发重组）、
  神谕整单元光环+整盘滤镜（强度可调）、控制图标居中折行、性格台词聊天气泡、
  全事件头顶飘字、同帧音效去重、未知事件向前兼容。
- 资源全占位（Resources/ClientBattle/ 同名覆盖即生效，程序化色块/合成音兜底）；
  特殊战法演出配置内置 PerformanceDatabase（可资产化 Inspector 维护）。
- 删除旧 Assets/Scripts/Battle 三层实现（29 文件）、旧 Configs 资产、BattleDemo
  场景、CardFX shader；docs/client 旧架构文档 10 份删除，重写 index +
  新增 client_battle_framework.md / assets_upload_guide.md（含待上传资源清单
  与维护方案）。

## 2026-07-09 宙斯·多情分神台词个性化
- 分神仍按敌女将逐个独立判定（RNG 消费序不变）；改为每个触发的女将各发一条
  trait_trigger，台词换成该女将的专属故事台词（effect=`distract_<template_id>`，
  10 名池内女将各配一条，池外回退通用 distract）。
- 重建 1 份受影响 golden（standard_seed20260705），144 测试全绿；traits.md 同步。

## 2026-07-09 客服重放闭环（schema 1.3.0 + replay_report 工具）
- HeroSnapshot 加法补 5 个可选字段（crit_rate_bps/heal_crit_rate_bps/trait_id/
  gender/level，pb# 11~15），顶层加 setup_metadata——战报 JSON 自身即可无损还原
  BattleSetup。schema 1.2.0→1.3.0，battle_events.md/schema.json 同步。
- 新增 `battle/tools/replay_report.py`：战报 JSON→还原 setup→重跑→与原报逐字节
  校验→输出 all 日志（含掷点明细）；旧版战报缺字段回退 roster 模板并告警；
  core_version 不匹配时提示检出对应版本。已用新 golden 验证往返逐字节一致。
- golden 11 份全量重建（快照新字段），144 测试全绿；mechanics/index.md 收口
  工具与日志说明。

## 2026-07-09 all 档日志打印技能掷点明细
- 新增调试侧信道 `report["_debug_rolls"]`（engine 收集，serialize_report 剥除
  下划线开头顶层键，golden/契约不受影响）：记录每次技能触发判定
  （基础率/伪随机补偿后率/roll 值/成败/保底）、连携判定、以及跳过原因
  （禁主动/准备中/号角走音）。
- textlog mode=all 按 anchor_seq 插位打印（⚄ 判定 / ⊘ 跳过），brief 不受影响。
- pseudo_random.roll 增加 debug_sink 出参；不改变 RNG 消费序，144 测试全绿。

## 2026-07-09 阿喀琉斯追伤贯穿台词 + 踵之弱调参
- 傲慢新增 pierce 台词键；`achilles_wrath` 追伤触发时必播贯穿台词
  （挂在 status_tick 下，不消耗 RNG）。
- 踵之弱概率人工调参 15%→7.5%（heel 默认 1500→750 bps）。
- 重建 3 份受影响 golden，测试 144 全绿；docs/mechanics/traits.md 同步。

## 2026-07-09 brief 战报显示性格台词 + golden 重建
- `battle/textlog.py`：trait_trigger 事件在 brief/all 两档均打印
  （★ 英雄 性格〔中文名〕发作（effect）「台词」）。
- 阿喀琉斯之怒追伤系数人工调参 60%→120%（ACHILLES_FURY_RATE_BPS=12000），
  据此重建 3 份 golden（standard_seed42 / standard_seed20260705 / men_gods_seed12），
  测试 144 全绿。

## 2026-07-09 手动 3v3 测试入口
- 新增 `battle/tests/test_manual_3v3.py`：文件顶部直接改 TEAM_A/TEAM_B/SEED 配阵
  （支持池内模板+等级+额外战法，或白板四维+任意战法），直跑输出日志+战报，
  pytest 冒烟断言防配错；确认阿喀琉斯追伤 ignore_defense（统率按 0 代入）逻辑无误。

## 2026-07-09 Phase 3 battle 大修（公式重做 + 性格系统 + 武将池 v3.1）
- 伤害双公式（兵刃 360+武-统 / 谋略 360+智-½统-½智，min=1）、兵力系数 0.5+0.5x、
  独立额外增伤乘区；格挡/闪避 0 结算（damage.mitigation）、震荡 special 不触发响应；
  行动窗口④⑤互换、犹豫计次前移；连携改按副将战法自身触发率。schema 1.1.0→1.2.0
  （+trait_trigger 事件），core 0.3.0，`docs/mechanics/damage.md` 新增。
- 性格系统 `battle/traits.py`：23 条性格全钩子实现 + trait_trigger 台词（确定性轮换）；
  武将池 v3.1（四阵营 24 将、等级成长面板）+ 战法 48 个分四阵营模块；
  `standard_skills.py` 删除，names.py 全量换新。docs/skills 重写为 4 阵营文档+index。
- golden 11 份重建（新增 sea_underworld/men_gods 场景）；reference/（4 场景 + 24 将
  性格 log）；工具 manual_battle.py（手动配阵）/gen_reference.py 新增；
  测试 143 全绿（test_golden 改报差异偏移避免 MB 级 diff 卡死）。
- 新增 `docs/client/frontend_overview.md`：主流程分层、文件地图、8 项关键机制
  （镜像原子绑定/折叠粒度/跳过等价/三层回退/配置驱动/向前兼容/域重载自愈/光环生命周期）、
  测试结构、B4 边界；index.md 登记。
- ops_manual §1 已含 Unity 运行步骤与机制验收战报切换（§6）。

## 更早条目

2026-07-06 及以前（Phase 1 / Phase 2）的全部条目已移至
`changelog_archive_phase12.md`（300 行红线拆分，2026-07-15）。
