# Changelog

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
