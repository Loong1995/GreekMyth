# Changelog 历史存档（Phase 3 后期 + 客户端框架期，2026-07-09 ~ 07-15）

> 【历史文档/历史快照】从 `changelog.md` 拆分（2026-07-23，500 行红线）。
> 只读存档；现行日志见 `changelog.md`。更早见 `changelog_archive_phase12.md`。

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

