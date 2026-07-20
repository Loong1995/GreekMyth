# Changelog 历史存档（Phase 1 / Phase 2，≤2026-07-06）

> 从 `changelog.md` 拆分（2026-07-15）。只读存档。

## 2026-07-06 单挑 T6 修复（折叠粒度整段播放）
- 根因：折叠模式下 `duel_challenge`/`duel_result` 同 group，旧逻辑只播宣战、
  `ProcessSideEvents` 跳过 result，对撞从未出现。
- `CardBattleView.SeqDuelStage`：整段 challenge→result→attr_change；非参战者压暗、
  武力对比条、3 次对撞、胜负定格、惩罚飘字时机修正。
- `CardView.SetDimmed` 支持擂台暗场。ops_manual §6/FAQ 区分 1v1（拒绝）与
  standard_seed20260705（接受对撞，默认战报）。

## 2026-07-06 机制分项验收用例（连携/单挑/中毒/控制/追击/准备）
- 新增 `battle/tests/test_client_mechanics.py`：六机制 golden 事件覆盖校验，
  支持 `--list` 打印对照表、`--export` 同步 StreamingAssets；`python xx.py` 直接可跑。
- 新增 Unity `CardBattleMechanicsTests`：每项机制独立 PlayMode 测试
  （事件预检 + 前端播完镜像终态一致），与 Python 脚本同源 golden 对照表。
- 机制→战报：连携 oracle_seed5 / 单挑 1v1_seed7 / 中毒 skills_seed11 /
  控制+准备 standard_seed42 / 追击 standard_seed20260705。
- ops_manual §6 登记机制验收路径。PlayMode 10/10 全绿。

## 2026-07-06 B3 收尾补丁：卡住修复 + 连携武将登记
- 修复「播放卡住」根因：Play 模式中保存脚本触发域重载，Runner 协程与 _director
  被清空。BattleDemoRunner 增加自愈：Update 检测 _director 丢失 → 清残留视图
  → 自动重启播放（并告警提示播放期间勿保存脚本）。
- 修复域重载连带 bug：FactionCardShape/ProcTex 静态缓存持有已销毁 Sprite
  假引用导致卡框/特效贴图消失，缓存判空改用 Unity null 语义自动重建。
- 连携（oracle）局武将登记：阿波罗=神、皮提亚=人、斯忒诺=冥界补入
  FactionStyle（faction_style.md 同步）；其连携战法 pythia_woven_scheme/
  gorgon_gaze 演出表此前已配。三人立绘暂缺回退色块，补
  Resources/Portraits/<中文名>.png 即生效。oracle_seed5/standard_seed42
  拷入 StreamingAssets 供切换验收（连携/打断演出）。
- 结算面板实机验证通过（oracle_seed5 播至 battle_end：胜负+逐局+武将统计）。
  EditMode 11/11 + PlayMode 3/3（含全 9 golden 逐播）全绿。

## 2026-07-06 Phase 2 B3 收尾：对照任务书补齐缺口
- 连携 assist：「连携!」横幅 + 号角 + 发起者金色光环，前置于战法模板。
- 打断双表达（任务书 5.3）：蓄力破碎特效 +「打断!」暴击样式飘字（施法者侧），
  与打断来源的控制状态特效（status_apply 侧）分开表达；prepare 增加充能特效。
- 犹豫延迟槽：卡牌角落 ⏳×层数 标记，由镜像 DelayedSlots 驱动
  （随跳过/续局/补结算持久正确），release 后原子移除。
- battle_end 系列结算面板：胜负 + 逐局比分 + 武将统计（伤害/治疗/击杀/余兵），
  数据全部取战报顶层 result，播完停留（任务书 §4）。
- 事件覆盖表收口：event_mapping §6 契约 21 类逐条标注（已实现/刻意不表现+理由/
  B4 打磨项），无遗漏。
- 模板参数化验证：同一 MeleeDash 模板差分出战神怒火（火焰斩+橙）与十二试炼
  （爪击+暴击命中）；同一 UltimateCutIn 差分出雷霆神谕（蓝雷）与冥域君临（紫柱）。
- 测试：新增 PlayMode 全 golden（9 份）逐一播完镜像终态断言——覆盖单挑
  （1v1/standard/oracle）、犹豫+连携（oracle_seed5/99）、打断（standard_seed42），
  无需另造专用战报。.cursorrules 当前 Step 更新至 B3 待验收。

## 2026-07-05 Phase 2 B3：演出模板 T1~T7 + 三采购包接入 + 运维手册（机器验证通过）
- 演出模板序列器（CardBattleView 重写）：T1 突进斩（拖尾+贴身 1.15+采购刀光）、
  T2 弹道、T3 全体弹幕（压暗+错峰天降）、T4 增益光环、T5 大招 cut-in
  （黑幕+速度线+立绘三段横扫）、T6 单挑小剧场（双主将中央对撞×2）、
  T7 天降（来源头像标+状态表可换特效，C-07）。
- 三个采购特效包全部接入：2D Sword Slash VFX（刀光/对撞）、Combat Flipbook
  VFX URP（命中/爆炸/电击/循环光环）、2D Cartoon/Anime Effects（施法/天降/
  增益/削弱，四色系）。VfxLibrary 30 键全登记真实 prefab；VfxPlayer 池化播放
  + 循环句柄；key 缺失回退 VfxKit 程序化原语并告警（播放永不中断）。
- 三张配置资产（Resources/Configs）：VfxLibrary / SkillVfxTable（26 战法）/
  StatusVfxTable（15 状态），菜单 GreekMyth→Build Vfx Configs 一键生成/补全
  （不覆盖人工修改）。状态常驻光环生命周期管理（apply 挂/remove 停/跳过对齐镜像）。
- 阵营差异化卡形（FactionCardShape：神拱顶/人盾形/海波浪/冥哥特 + SpriteMask
  裁切立绘）；合成占位音效 SfxService（11 键，Resources/SFX/key.wav 优先）；
  修复无声根因：场景缺 AudioListener，SfxService 自动补挂。
- 战斗 UI 最小集：回合/比分/状态行 + 倍速 x1/x2 + 粒度切换 + 跳过本局/跳到结果。
- 验证：EditMode 11/11 + PlayMode 2/2 全绿；BattleDemo 完整播放无错误、
  无未登记特效告警；截图 Logs/b3_demo_screenshot*.png。
- 文档：ops_manual.md（非程序员运维手册，重点验收物）+ presentation_flow.md
  （机制流+代码地图）；index 更新。决策 C-10。

## 2026-07-05 Phase 2 B2：卡牌视觉系统 + 打击感地基（机器验证通过）
- CardFX.shader（代码 shader，C-09 备案）：视差伪 3D/呼吸缩放/foil 流光/
  受击闪白/状态色调（石化去饱和、中毒绿），SpriteRenderer 与 UI 通用。
- CardView 卡牌视图：阵营边框（FactionStyle 四阵营配色）+ 兵力条 + 状态图标栏
  （层数角标）+ 主将★ + 立绘 Resources/Portraits/<hero_id>.png 换皮零代码。
- 打击感三件套：ImpactService（HitStop 真实时间编排 + 自研震屏）+ 闪白，
  四档分档集中 ImpactConfig.asset；飘字对象池（C-03 样式，暴击弹跳/DoT 小字）。
- CardBattleView 接替占位前端（Runner 开关保留 placeholder）；PlayMode 冒烟
  2/2 通过（播完 golden 终态一致 + 池无泄漏 + 中途跳过安全收敛）。
- 文档：art_pipeline / faction_style / card_shaders；决策 C-09。
- 待 B2 收尾项：状态图标正式图、头像资产（Avatars/）——随 B3 状态特效一并做。

## 2026-07-05 Phase 2 B1：播放调度层代码完成（待 Unity 内验收）
- 数据层：ReportLoader（schema major/seq 递增/t 字典序校验，失败一律
  ReportFormatException）+ 事件树重建 + PayloadReader 类型化读取。
- BattleMirror：事件驱动纯赋值 + before 自校验（troops/attr 篡改立即报错）；
  PlaybackDirector：折叠/展开粒度、倍速、跳过=Apply-only 快进、未知类型告警。
- headless 中文播放日志（对齐 replay_dump brief）+ 占位前端（方块+文字，上下排）
  + BattleDemoRunner 主入口 + BattleDemo 场景 + StreamingAssets 样例战报。
- EditMode 单测 9 条（逐 golden 跑通/跳过等价/粒度等价/非法战报显式报错），
  golden 直接复用 battle/tests/golden。文档 docs/client/playback_director.md。
- 验收方式：Unity 打开工程编译，Test Runner 跑 EditMode，运行 BattleDemo 场景。
- 同日验证：Unity 内编译零错误；EditMode 10/10 通过（修复一处镜像缺口：局边界
  引擎静默回滚局内属性修正，镜像同步复位到建队快照）；BattleDemo 场景 Play Mode
  冒烟完整播完 2 局并停在结算，日志落盘 Logs/battle_playback_sample_standard.log。
  另备 tools/client_check（dotnet 壳）可在 Unity 外跑同源校验。

## 2026-07-05 人工修订 C-07：机制触发伤害默认天降特效 + 演出配置手册
- 自带战法机制触发的伤害（组根 status_tick，非主动非追击）默认**不播卡牌突进
  动画**，改为 T7「天降特效伤害」：上方特效坠落 + 目标头顶来源头像标 + 飘字；
  新增 T7 模板规格（vfx_templates.md，共 7 套），T2 收窄为主动弹道专用。
- 逐战法/状态演出可配置：SkillVfxTable / StatusVfxTable 两表为唯一定制入口
  （状态表含触发伤害演出覆盖、头像标开关、常驻特效、色调）。新建维护手册
  `docs/client/vfx_config.md`（查找顺序/字段表/示例配置/维护纪律）。
- event_mapping §2.3、decisions C-07、client/index 同步更新。

## 2026-07-05 Phase 2 Step A 人工确认 + 采购清单
- 决策批复：C-01 修订为横屏+卡牌上下占位（敌上我下，中央演出通道）；C-02 确认
  4.0s 独立预算且单挑演出允许付费采购；C-03（飘字不缩略）/C-04（跳过不播摘要）/
  C-05（时长终值）按提案确认。
- 新建 `docs/client/to_purchase.md`：首批采购清单（2D Cartoon/Anime Effects
  $4.99 必买、Combat Flipbook VFX URP $39.97 必买、2D Sword Slash VFX / 音效包
  推荐），含 URP 2D Renderer 兼容性红线、用户购买操作步骤、免费占位方案与
  放置规范。vfx_templates T1 轨道描述改为纵向突进（对齐 C-01）。
- 等待用户完成购买后进入 Step B1。

## 2026-07-05 Phase 2 Step A：客户端架构设计四文档产出
- 通读 phase2_client.md 任务书 + 冻结契约 + playback_model + golden 样例，产出
  Step A 四份文档：`docs/client/event_mapping.md`（契约 21 类事件逐条表现职责/
  消费字段/阻塞性/粒度归属 + hint 四档打击感 + 平局续局与犹豫延迟表现方案）、
  `docs/client/architecture.md`（数据/调度/表现三层单向依赖 + BattleMirror 状态
  镜像原子绑定 + 播放控制定稿 + asmdef 划分 + BattleDemoRunner 主入口规格）、
  `docs/client/vfx_templates.md`（6 套 Timeline 模板：突进斩/弹道/全体弹幕/增益
  光环/大招 cut-in/单挑小剧场；SkillVfxTable 映射 + 兜底 + StatusVfxTable 特效
  扩展位；时长预算论证）。
- decisions.md 追加 C-01~C-07（横屏布局提案/单挑 4s 独立预算/飘字不缩略/跳过
  不播摘要/时长终值/状态特效扩展/头像标判定），前五条待人工确认。
- 新建 `docs/client/index.md` 主文档；.cursorrules 切换至 Phase 2 Step A。
- STEP A 完成，等待架构确认。确认前不编写任何 Unity 场景与 C# 实现。

## 2026-07-05 Step B4：选人事件化 + 工具链 + golden 冻结 + 收口
- 选人/受击率事件化（D-22）：`select_enemy_by_hit_rate` 记录候选池受击点数与命中者，
  以可选字段 `target_select` 随 normal_attack/skill_trigger/damage 带出（契约加法式
  演进，schema 1.1.0，core 0.2.0）；textlog all 档打印「·选人[普攻] 受击点数: …→选中」，
  brief 不打印。新建 `docs/mechanics/targeting.md` + `test_targeting.py`（4 测试）。
- 工具链（任务书 4.2/6.3）：`battle/tools/batch_sim.py`（阵容池×种子范围，胜率/局数/
  每武将伤害治疗分位数统计，可存 JSON）；`battle/tools/replay_dump.py`（战报 JSON →
  中文文本日志，brief/all 两档）。
- 性能基准：`battle/benchmarks/bench_simulate.py` + 报告 `docs/dev/performance.md`——
  纯模拟 315（standard 全机制）~847（纯普攻）局/秒，目标 ≥100 达标。
- golden 冻结（D-23，任务书 4.3 第 3 层）：`battle/tools/gen_golden.py` 生成 9 份
  覆盖各机制阵容的战报入库 `battle/tests/golden/`；`test_golden.py` 逐字节回归。
- 收口：新建根 `README.md`（目录约定+快速上手+上下文管理规则）；.cursorrules 更新至
  B4；契约文档登记 1.1.0 演进并修正 delayed 描述（延后恒 1 回合）。

## 2026-07-05 三项规则人工修订：犹豫二次修订 / 赫尔墨斯限前 2 回合 / 无视统帅置 0
- 犹豫（D-02 二次修订）：延后固定 1 回合（N→N+1 回合窗口最前释放，释放后才进入
  新一轮判定）；重复施加**刷新不叠层**（stacks 恒 1）；已登记延迟行动不受刷新影响。
  `statuses.hesitation()` 去叠层、引擎 delay_rounds 固定 1。
- 赫尔墨斯神谕（D-19 修订）：扰心印记仅前 2 回合生效（duration_rounds=2，
  覆盖第 1、2 回合行动窗口后到期）。
- 阿喀琉斯之怒（D-20 修订）：`ignore_defense` 语义改为**属性差计算时对方防御属性
  置 0**（原按基准 100 作废），追加伤害显著增强。
- 测试改造：延迟恒 1 回合断言、新增刷新不叠层测试与印记前 2 回合测试（替换叠层
  测试）；119 个全绿。文档同步：hesitation.md 重写、effects/statuses/
  status_interactions/index、两篇战法文档、决策 D-02/D-19/D-20 修订落档。

## 2026-07-05 日志中文化 + brief/all 双粒度 + 众神对决演示战
- 新建 `battle/names.py`（战法/状态 id → 中文名注册表）与 `battle/textlog.py`
  （全项目日志文本唯一出口：`format_report(report, mode)`，brief=主干 /
  all=全量，中文战法名）；`battle/sample.py` 打印逻辑迁入 textlog 并新增
  `--mode` 参数，文本落盘带模式后缀。
- 新建 `battle/tests/test_showcase_gods.py`（人工指定验收阵容：宙斯+阿喀琉斯+阿瑞斯
  对 哈迪斯+赫尔墨斯+阿斯克勒庇俄斯，装配覆盖暴击/控制/DoT/治疗/追击/连击/准备型）：
  直接执行输出中文战斗日志（brief+all 双份落盘 battle/out/），pytest 断言机制覆盖
  与中文化生效。测试 118 个全绿。

## 2026-07-05 Step B2 验收通过 + Step B3：五大高级系统、标杆战法与示例武将
- 五大高级系统落地（`battle/engine.py`）：单挑（DUEL 相位，D-03）、追击/连击
  （每击独立追击）、连携（k=70%，普通随机不占记账）、犹豫（整体延后 N=层数、
  窗口末计次，D-02 修订版）、准备型战法（prepare/release/interrupted 协议）。
- 状态系统升级：四类响应钩子（on_apply/on_damage_dealt/on_damage_taken/
  on_action_start）+ 全局响应优先级分发；动态修正/局内外计数器；标准控制建造器
  （缄默/缴械/冥锁/石化/犹豫）；deal_damage 扩展（无视防御/固定量/kind 防递归/
  吸血）；adjust_status_attr 新原语。
- 标杆战法 14 个全部实现（`battle/standard_skills.py`）：skill_files.py 全对位
  （含 5 个仅描述未实现的）+ 人工新增验收标杆**阿喀琉斯之怒**（物理暴击+20%、
  暴击追伤 60% 无视统帅每回合 3 次）；示例武将花名册（`battle/roster.py`）。
- 测试 116 个全绿（新增 54：逐系统 + 交互矩阵逐格 + 逐标杆战法 + 标准阵容确定性）。
- 文档：新建 duel/assist/pursuit_combo/hesitation/status_interactions 五机制文件 +
  docs/skills/ 三段式战法文档 14 篇；effects/statuses/determinism（RNG 消费点与
  排序规则登记）/index 全量更新；决策补录 D-15~D-21（待审阅）。
- sample 新增 standard/oracle 场景（`python -m battle.sample --scenario oracle`）。

## 2026-07-05 Step B1 验收通过 + Step B2：效果原语、状态系统与数值迁移
- 效果原语五入口落地（deal_damage/heal/apply_status/remove_status/modify_attr，
  `battle/engine.py`）+ 暴击乘区（伤害/治疗，率聚合面板+状态、DoT 不暴击）；
  治疗公式迁移（标定 500/400/1073 逐值锚定）。
- 状态系统（`battle/statuses.py`）：kind/层数/持续/来源；负面默认不可刷新不可叠加
  （静默拒绝）；行动窗口计次到期；DoT/HoT 回合始 tick；修正聚合先平加后百分比；
  forbid_* 禁制。阵亡鲁棒清理：施加状态事件化全清、不复活、不可为目标（专项边界测试）。
- 战法基座（`battle/skills.py`）：类+注册+装配顺序；伪随机补偿真累计
  （`battle/pseudo_random.py`，D-09，一局内记账）；6 个 test_ 战法覆盖全部原语。
- 测试 62 个全绿（新增公式单测/状态/阵亡清理/伪随机/战法端到端 45 个）；全部测试
  文件支持 `python xx.py` 直接执行。数值等价验证：新旧核同种子 1000 场统计对比
  全指标容差内通过（`docs/dev/numeric_equivalence.md`）。
- 新建 `docs/mechanics/statuses.md`、`effects.md`；index/determinism（RNG 消费点
  登记表扩充）同步更新。sample 新增 `--scenario skills` 演示。

## 2026-07-05 Step A 人工确认 + Step B1：引擎骨架与确定性地基
- 决策批复落档（decisions.md）：犹豫消耗改行动回合计次；单挑拒绝封顶 80%、仅第 1 局
  一次且惩罚仅第 1 局（scope=game）；连携 k=70% 且不占用当回合释放机会；D-05/D-06
  及其余各条按提案确认。契约修订：相位枚举拆分起止端（0~7）修复 t 字典序不变量。
- 新建 `battle/`：`simulate(setup, seed)` 纯函数入口；系列→局→回合→行动状态机
  （准备回合 + 8 正常回合 + 最多 7 局残血续战）；EventWriter（seq/t/分组/体积保险丝，
  异常即战斗失败）；单一 PCG RNG 流；普攻/兵力三池/受击率公式忠实迁移。
- 测试 17 个全绿：同种子 100 次逐字节一致、战报结构逐条核对冻结契约（seq/t 序、
  分组继承、兵力链连续）、系列平局编排、初始兵力与超编 NPC、输入校验。
- 新建 `docs/mechanics/index.md`（机制总图）与 `determinism.md`（RNG/排序/舍入/
  纯函数红线 + RNG 消费点登记表）。

## 2026-07-05 Step A：分析与契约草案产出
- 完整通读旧 core（engine/domain/config/event/rng 全部源码 + 全部设计文档），产出
  《旧 core 分析报告》`docs/dev/v0_analysis.md`（资产清单/文档-代码核对/五维度重构问题/属性术语定论）。
- 产出《战斗事件流契约草案》：`docs/schema/battle_events.md`（总纲：battle_report 结构、
  逻辑时间、分组机制、pb 映射、体积估算）+ `battle_events_payloads.md`（21 种事件字段表
  与 JSON 实例）+ `battle_events.schema.json`（JSON Schema 2020-12）。
- 产出《播放模型设计文档》`docs/dev/playback_model.md`（6 种播放粒度论证 + 3 个典型
  战法事件流示例）与《待人工确认决策清单》`docs/dev/decisions.md`（14 条决策）。
- 修正：雷霆神谕示例按 `skill_files.py` 标杆语义改写（神谕施加全队【雷霆】状态，
  非准备型两段协议）；status_tick 语义扩展覆盖事件驱动的状态触发。
- 建立仓库根 `.cursorrules`。Step A 完成，等待人工验收。
- 并行分析子代理返回后交叉核对，向 v0_analysis 补两条：旧 core 无法指定进场初始兵力
  （troops 恒=max_troops，系列续战需新增 initial_troops 入口）；bench_basic.py 运行时
  改写模块常量破坏纯函数性（O5）。
