# Changelog

## 2026-07-25 近 3D 默认 45° + 地面 AI 出图指令
- `CameraFitter.PilotPitchDeg=45`、`PilotDistance=12.5`；卡 FaceCamera 后与地面夹角≈45°。
- `docs/dev/near3d_evaluation.md` §七：正俯视地面母版指令 + 神/人/妖主题块 + 负面词 + 验收清单。
- 同步 `rendering_layout` / `vfx_playback_scheme` / `stage_plan` 进度行。

## 2026-07-25 RFX4 粉红修复（导入官方 URP Patch）
- 根因：未导 `Realistic Effects Pack v4/.../URP patch`；Effect22 `Fog.mat` 等为 Built-in Particles/Standard。
- 已应用官方 URP patch 覆盖材质/shader；新增菜单 `GreekMyth/RFX4/导入 URP Patch` + `诊断粉红材质`。
- 验收：重开「RFX4 可靠预览」看 Effect22；若仍粉，跑诊断菜单并确认 URP Depth Texture。

## 2026-07-25 Magic Pack 预览按键跳两格
- 根因：Update(Input System) 与 OnGUI 双通道同帧各 Step 一次；已去掉 OnGUI 按键，只留一条。

## 2026-07-25 Magic Pack 预览按键 + 以谁为准
- 可靠预览=资产真貌；战斗残缺多因未导 URP / 挂载裁剪 / 缩尺，不以战斗为准选材。
- 预览按键：须先点 Game 窗；勿叠双通道（会跳两格）。
## 2026-07-24 近 3D 方案系统性评估文档
- 新增 `docs/dev/near3d_evaluation.md`：结论=做稳健 A+（20° 透视 + 地面 Quad + 分层卡牌），
  伪 3D 舞台物体隔离为独立实验不进主线；含成本表、风险红线与 4 步落地顺序。
- 追加 §5：低模+AI 贴图不可行；稳健替代=AI 立牌（billboard/交叉双板/地面贴片）；
  氛围四层清单（远景入画/立牌神像/RFX4 空气层/光色层）。

## 2026-07-24 战神之勇特效「全灭」修复
- 误关 FireBack 后 URP 下 Fringe 又不可见 → 零画面；恢复 FireBack+放大，仅关 Decal/Audio。

## 2026-07-24 透视试点 A（漂浮 2D 卡 + Effect18 绕身）
- `CameraFitter.PerspectivePilot`：透视+轻俯视；卡跟相机倾角。
- 战神之勇挂载去 FireBack/Decal，保留盾环；背景/滤镜适配透视半高。
- 须导入 Magic URP patch，否则绕身 shader 易失效。

## 2026-07-24 战神之勇←Effect18；雅典娜圣盾回 AllIn1
- `ares_might` 常驻 `aura_ares_might`（Magic Effect18），取消该状态卡框呼吸。
- 雅典娜挂身恢复仅 AllIn1 金描边；反制仍 Effect17_Collision。

## 2026-07-24 Magic Pack 1 一键可靠预览
- 菜单 `GreekMyth/Magic Pack/可靠预览（一键）`：透视+Bloom+Effect1–33+Collision；1/2/3 跳盾/环/雷。

## 2026-07-24 Magic Pack 1：宙斯命中+雅典娜圣盾
- 方案快照 `docs/client/vfx_playback_scheme.md`；采购登记 Magic Pack 1。
- `hit_lightning`←Effect19_Collision；`aura_aegis`←Effect18；`hit_shield_counter`←Effect17_Collision。
- 竖雷仍 DR；菜单 `GreekMyth/Magic Pack/接线…`；验收战报 manual_3v3_seed20260722。

## 2026-07-24 RFX4 一键可靠预览
- 新增菜单 `GreekMyth/RFX4 可靠预览（一键）`：透视相机+HDR Bloom+地面+Effect1–27 循环。
- 废止「拖进 ClientBattleDemo / 开粉红 PC Demo」预览；踩坑见 P-28。

## 2026-07-24 thunder/zeus_bolt 对齐为 DR+hit_lightning
- 两者均无 ProjectileKey：RemoteStrike 走 DR 单道竖雷 + `hit_lightning`。

## 2026-07-24 thunder 与 zeus_bolt 对齐
- （已再改为双 DR，见上条；曾短暂同用 LP02。）

## 2026-07-24 宙斯恢复到稳定方案（DR+LP02+Impact_02）
- 撤回今日 FireVolley / Flow / Discharge Bunch 升级。
- `thunder`：DR 单道竖雷 + `hit_lightning`；`zeus_bolt`：LP02 Directional + `hit_lightning`。
- 删除 `hit_thunder_impact`；零 RFX4。

## 2026-07-24 宙斯技能 Vefects/DR 升级（禁 RFX）
- （已撤回，见上条）

## 2026-07-24 宙斯 RFX4 试看撤回（喷射粒子红线）
- 用户明确禁止喷射粒子；拆除 `hit_thunder_rfx`/`hit_zeus_bolt_rfx`。
- 恢复 `thunder`→`hit_lightning`、`zeus_bolt`→`hit_zeus_discharge`；P-25 升为硬禁。

## 2026-07-24 宙斯 RFX4 命中试看（无门槛）
- （已撤回，见上条）

## 2026-07-24 单挑撤回 RFX4，改 cut-in 白闪
- 胜者帧 Effect25「雷劈」与交错 Effect20 均显廉价，已从 `DuelPerformance` 拆除。
- 交错峰值改 `CutInService` 同层白闪+裂缝扩光+震屏；删 `duel_*_rfx`。
- RFX4 仍保留包与 Bloom，单挑暂不接，待舞台/Magic Pack 1 再选型。

## 2026-07-24 单挑峰值接 RFX4 + 强制 Bloom
- （单挑 RFX 接线已撤，见上条；`BattlePostFx` Bloom 保留。）

## 2026-07-24 纠正 RFX4「整包烟花」误判
- 先前表述过重：RFX4 是史诗峰值粒子包（包内即含雷暴/圣光等），按 stage_plan
  支撑神像触发/单挑/大招换代；须 HDR+Bloom。
- 真正禁的是「用 Effect10/25 整段替换宙斯竖雷几何」；日常竖雷仍 DR/Vefects，
  RFX4 留给峰值加层。

## 2026-07-24 宙斯命中撤回 RFX4，改回 Vefects 电击
- 判定：RFX4 Effect10/25 整段替换竖雷观感不对；日常命中暂回 Vefects Electric_*。
- `thunder` → `hit_lightning`；`zeus_bolt` → `hit_zeus_discharge`；删 `hit_*_rfx`。

## 2026-07-24 宙斯落雷命中换 RFX4 炸点
- （日常竖雷替换方案已废；峰值加层见单挑条）

## 2026-07-24 取消主动 Cast + 修复战吼 prepare 空跑
- 主动默认取消全部 Cast（不再播 Impact_Shockwave / Explosion_01）。
- 根因：`hector_warcry` 写死 AoeCenter，prepare（无伤害）也空跑进中心→像没放技能；
  改为 Auto，且无伤害/治疗组一律只飘技能名+落账、不走位移模板。

## 2026-07-24 主动默认分物理/魔法三件套
- 物理主动：Proj=`proj_bolt200` + Hit=`hit_clash`(Radial_Spiky)（Cast 已取消，见上条）。
- 魔法主动：Proj=`magic_bolt` + Hit=`hit_lightning`(Electric_Impact_02)。
- ActiveDefault 清空 Hit/Proj/Cast，由类型解析；赫克托尔仅 Auto+震屏。

## 2026-07-24 宙斯 LP02 + 战吼中心爆发可见性修复
- 宙斯：`lightning_projectile` ← Vefects Lightning_Projectile_**02** Directional；
  `hit_lightning` ← Electric_Impact_02。
- 赫克托尔看不见：根因是 Cast 冲击波在**挪中心前**播在己方卡位；改为
  `PlayAoeCenter` 进中心后再播 Cast；命中改 `hit_warcry`（Radial_Burst 放大）。
- 候选资源登记：`cast_aoe_burst`（Explosion_01）供魔法群攻默认选型。

## 2026-07-24 宙斯落雷回滚 + 赫克托尔战吼武力化
- **回滚**：宙斯 `lightning_projectile`/`hit_lightning` 恢复 Vefects；`thunder` 恢复
  DR 程序化竖雷；RemoteStrike 恢复飞行弹道逻辑。RFX4 Effect10 判定为烟花粒子，
  **不是**雷电几何，不适用于落雷升级。
- **同逻辑升级候选**（未改，备选）：Vefects `Lightning_Projectile_02_Directional`、
  `Electric_Flow_01_Directional`、命中 `Electric_Impact_02`；被动仍宜走 DR 线状雷。
- 赫克托尔 `hector_warcry`/`hector_assault`：`cast_warcry`←Impact_Shockwave v2、
  `proj_bolt200` 粗束、`hit_clash` 尖刺命中+震屏；AoeCenter 前摇留一拍。

## 2026-07-24 宙斯落雷换 RFX4 Effect10
- （已回滚，见上条）原 Effect10 接线作废。

## 2026-07-24 美术风格基准定论 + 采购清单重规划
- `stage_plan.md` §四.0 定论：**史诗感优先、其次写实精致**；特效走单一出品人
  家族 **kripto289（KriptoFX）**；已购卡通三包定为占位备胎、战斗高频特效
  随舞台落地逐步换代（variant 替换零代码）。
- 必买五包：kripto289 Magic Effects Pack 1（$37 风格锚点）、Realistic Effects
  Pack 4（$42 史诗大招级，URP 需 patch 实测）、Lumen Light FX 2、Water
  Caustics URP（$14.99，替掉与写实冲突的手绘水下包）、Ground Crack URP。
- 总预算 ¥1530~1950；新红线：风格一票否决（卡通/赛璐璐不买）、AI 出图锁定
  写实厚涂史诗神话基调。

## 2026-07-24 舞台加成改阵营制 + 神舞台实施方案
- `stage_plan.md` 加成文本修正为阵营制定数：神（olympus）10% 伤害双倍、
  人（heroes）8% 技能再释放、妖（sea/underworld）10% 负面状态 +1 回合。
- 新增 `docs/dev/stage_olympus_impl.md`：core（stages.py 注册表 +
  on_pre_damage_dealt 钩子 extra_up +10000 bps 精确 ×2 + emit_status_trigger）、
  客户端（分层背景/赫拉 BoardActor/触发动画 ≤1.2s）、S1~S4 步骤与验收。

## 2026-07-24 三舞台战斗场景规划立项
- 新增 `docs/dev/stage_plan.md`（现行计划）：神/人/妖三舞台（奥林匹斯山巅/
  特洛伊角斗场/冥海裂渊），标志神像赫拉/阿特拉斯/克拉肯 → 谋略/武力/敏捷系加成。
- 定「先买特效包定风格 → AI 垫图出静态」工序铁律；预算 ≤¥2000（不含音效）。
- 神像加成定为 core 侧机制（`stages.py` 注册表，同构阵型系统），待实施。
- discipline/index.md 登记该计划为现行文档。

## 2026-07-23 势能光环改回 LightGlow A 并去星点
- `momentum_glow` ← LightGlow A；变体与运行时均剥掉 Star/Spark 子物体。
- 保留 Rays 柔光 + 关 Point Light；分档 1.18~1.65。

## 2026-07-23 势能光环试 Magic Aura Runic
- `momentum_glow` ← **CFXR Magic Aura A (Runic)**（与圣盾同源族，符文层次）。
- 仍卡后 sorting−1、关点光、轻柔化；分档 1.05~1.48。

## 2026-07-23 势能光环去廉价感：香槟柔光
- `momentum_glow` 改挂 **LightGlow A**（暖色底），去掉红底硬染金。
- 关 Point Light、降饱和/发射率；分档缩小为 1.18~1.65，只留边缘余晖。

## 2026-07-23 满档金光环随分档 + 与火同渐灭
- 卡后光环改金染色、略放大增强；挂载并入 `MomentumFireController`。
- 与势能火同分档（≥4/5/6/7）同 Fade/Extinguish/Clear。

## 2026-07-23 满档改卡后外溢光环（撤 All In 1 红描边）
- All In 1 红描边观感不可见，已撤销。
- 满档恢复 `momentum_glow`：挂卡后 sorting−1、放大≈1.5，红光从边缘外侧透出。

## 2026-07-23 满档改 All In 1 卡框红描边
- （已撤销）曾去掉中心 LightGlow 改红描边，观感不足。

## 2026-07-23 先攻/犹豫不展示状态图标
- `hesitation` 去掉 `ControlIcon`；`first_strike` 本来就不展示。
- 卡顶图标仅：缄默/缴械/石化/冰锢/冥锁/魅惑/恐惧/冥火（8）。文档同步。

## 2026-07-23 无伤默认主动补飘技能名
- `hermes_jest`/`jason_command` 等无伤害默认演出原先只飘状态字。
- `DefaultPerformance`：无伤无疗的 `skill_trigger` 在施法者头顶 `ShowSkillName`。

## 2026-07-23 VFX 试换：满档红光晕 + DualBolt 群攻弹道 + Hit_05 命中
- `momentum_glow`←CFXR LightGlow B (Loop, Red)，替 UnitView 满档纯色块。
- `blade_bolt`/`magic_bolt`←030-DualBolt100 Orange/Purple；`hit_generic`←Vefects Hit_05 Once。

## 2026-07-23 台词气泡与时间轴 DurationMul 对齐
- 根因：`Wait(ExclusiveSeconds)` 乘 DurationMul=2，气泡 DOTween 仍用裸 1.14s →
  泡收起后空等约一倍时长（阿喀琉斯贯穿观感）。
- `SayExclusive` 同步缩放动画并返回已缩放秒数；Director/Duel 原样 `WaitForSeconds`，
  泡/满档 cut-in 结束后立刻接行动（仍无 GroupPause）。

## 2026-07-23 异阵对打：逐队识别阵型
- 原两队站位并集推断，方圆 vs 鹤翼等会落到 Grid2x3 失效。
- 改为 `FormationA`/`FormationB` 各自 Detect；落点按本队；卡尺任一方交错则用交错带。

## 2026-07-23 交错阵扩展：却月{1,2,6}、鹤翼{2,4,6}
- 与方圆共用齐边几何：后排卡贴队区上界↔前排区下 1/3 线；前排卡底缘贴中缝。
- `StanceFormation.QueYue` / `HeYi`；`DetectFormation` 按集合匹配。

## 2026-07-23 方圆阵落点修正（穿中缝 + 宽屏列距）
- 根因：① 卡高按 5/6 齐边带极大化后，1 号仍落 1 区几何中心 → 下缘穿入中缝；
  ② `RecalcFromCamera` 误用相机全视野半宽，宽屏三列被撑到 ±ortho×aspect。
- 修正：1 号底缘贴队区内缘；布局锁定设计安全区 4.6×5.2；非方圆/前列回退 Grid2x3；
  重生 manual 战报 positions=[1,5,6]。

## 2026-07-23 站位改为阵型组合：方圆阵 1+5+6
- 不再强制前后排同列叠放（竖向四倍卡高不自然）。首发**方圆阵**{1,5,6}：
  上侧 5/6 上缘贴队区上界、下缘贴 1 区下 1/3 线；1 在 1 区中心；A 侧镜像。
  `StanceFormation` + `DetectFormation`；manual_3v3 默认 positions=[1,5,6]。

## 2026-07-23 站位卡牌按相机视野极大化（机型自适应）
- `StanceLayout.RecalcFromCamera`：用当前正交可见半宽/半高（≥设计安全区）
  极大化卡面；台词带/中缝按比例；抖动吃剩余空间。建棋盘前 Fit 相机。
  修复「固定 5.2 硬塞导致极小」；发布机型与编辑器同一套自适应。

## 2026-07-23 站位卡牌按区域缩放防重叠 + 台词边距
- `StanceLayout`：上下 `LineReserve` 台词带、中缝 `MidClear`；按格反算
  `CardWidth/Height`，使框+chrome+2×抖动仍落在本格内。`UnitView` 按
  `LayoutScale` 缩放卡面与 UI。修复前后排卡面重叠。

## 2026-07-23 test_manual_3v3 接入站位数组
- `TEAM_A_POSITIONS` / `TEAM_B_POSITIONS` 与英雄列表等长（1~6）；优先于条目
  `position`；缺省按序 1..n。冒烟断言校验站位写入。

## 2026-07-23 站位系统（1~6 区域布局 + 初始化传位）
- 客户端 `StanceLayout`：两侧 2×3 区域、前排对前排同列镜面对齐；卡牌落区域
  中心；休息点抖动改为区域宽/5（不再用卡宽/4）。`BattleBoardView`/`UnitView` 接入。
- 配阵 config：英雄 `position` 或队级 `positions[]`；缺省按序 1..n
  （`manual_battle` / `client_battle_bridge`）。ManualSetup 改为每队 6 槽镜面 UI。
- 文档：rendering_layout §五、burst_coordination §三、manual_setup_panel、
  performance_mechanisms 回位微抖。

## 2026-07-23 重播清横幅 + Cursor 开工规则
- 修复重播后「系列结束 — 胜者 B 队」残留：根因是 `HardStop` 未清
  `BannerService`（违反 R-1.2③）；现并入 HardStop，Teardown 去重。同步
  `architecture.md` HardStop 次序。
- 新增 `.cursor/rules/`：`00-session-start.mdc`（alwaysApply，强制先 Read
  discipline/任务相关文档）+ ClientBattle / battle / docs 三份 glob 规则；
  `.cursorrules` 指向该目录。

## 2026-07-23 按 discipline 全量文档核查完善（服务端+客户端）
- 双路核查出 60+ 处滞后：`.cursorrules`/mechanics-index/project_overview 更新为
  Phase 4 已落地、1.4.1/0.4.1、32 将（7/10/7/8）、回合默认打到主将阵亡、势能默认开；
  schema 总纲事件计数 23→24 并加现行版本页眉；payloads exhausted 标注对齐 1.4.1。
- docs/dev：phase4_reply/plan/manual_tasks、numeric_equivalence、hero_proposal、
  changelog_archive 补【历史文档】头标；decisions D-04/D-16 补连携 per-skill 触发率
  修订段；changelog 拆出 phase4 / phase3_client 两份存档（主文件回到 500 行内）。
- docs/client：architecture（PlaybackController→PerformanceRunner、HardStop 实序、
  Session 无 Dispose）、playback_units（六 processor 全列、Director 符号、模板节拍
  0.30/0.38/RemoteStrike 实测值）、performance_mechanisms/index/framework/text_system
  旧 Runner 符号全部迁移；rendering_layout 槽位改 UnitView 实常量；assets_upload_guide
  立绘 24→32（补 calypso/hecate）、控制图标 6→9；settlement_stats 指向
  StatusPresentationRegistry.StatsSkillId；playback_requirements R-1.1 Stopping 语义澄清。
- 代码：names.py 删无引用 styx_blood_oath 并写明与 ChineseNames 同步约定；9 处
  Runner→Director/Builder 注释迁移；CutInService sorting 注释 91→90。
  pytest 244 全绿、Unity 编译零错误。pitfalls 追记 P-23（PowerShell 改中文文件毁编码）。

## 2026-07-23 死代码与过期产物清理
- 删除死类 `FireRimFx`（阿瑞斯火舌已改 SetAresRage 红呼吸）与 `LightningBoltFx`
  （自写折线闪电，已被 DrLightningUtil/ThunderAuraDriver 取代，changelog 早有"暂留"标记）。
- `PerformanceRunner` 移除只写不读的 `_playLoop` 字段。
- assets_upload_guide 光环表去重（aura_aegis/aura_fire/aura_freeze 双行合一）并删 FireRimFx 行。
- 清理 `battle/**/__pycache__`、`battle/out/manual/` 过期战报/探针/桥接一次性输出
  （仅保留最新 manual_3v3_seed20260722 一对）。编译零错误。

## 2026-07-23 客户端播放系统架构重构（文档先行 + L4 拆分）
- 新增 `docs/client/playback_requirements.md`（行为规格书 R-条款）与
  `docs/client/architecture.md`（架构权威：分层依赖规则/生命周期迁移表/服务端适配点）。
- PerformanceRunner（558 行上帝对象）拆为：控制器（状态机+HardStop 唯一硬停止）
  + PlaybackWorldBuilder（建世界→PlaybackSession 会话容器）+ PlaybackDirector
  （主循环/组分派）+ CutInPolicy（cut-in 判定阈值集中）；公开 API 兼容不变。
- 落账单一化：SettleDamage/SettleHeal 兵力写入统一走 EventApplyService.ApplyDamage/
  ApplyHeal（静默与演出路径同源，Skip/重播终态一致）；BannerService 反向依赖
  Test 层改为 Suppressed 开关；DuelPerformance 改经 ctx（CutIns/OnBgmDuck）不抓单例。
- tween 所有权收口：UnitView/DefaultPerformance 全部单位级 tween 补 SetLink；
  KillAll 仅剩 HardStop 兜底一处；域重载孤儿棋盘收养防双棋盘。
- 冒烟全绿：播放→重播→预热中跳过→高光→Teardown 连续操作，棋盘/tween 零残留零报错。

## 2026-07-23 战吼走默认弹道；重播硬停防叠播
- `hector_warcry` 去掉专属 ProjectileKey，走默认物理 `blade_bolt`。
- 重播/跳过：`StopAllCoroutines` + `DOTween.KillAll`；VFX CancelAll 先杀 tween 再灭活。

## 2026-07-23 默认群攻弹道改为 029-Bolt200
- `blade_bolt`→Orange、`magic_bolt`→Purple（替换偏粉无力的 031-Arrow）。

## 2026-07-23 群攻031 / 战吼029 / 天雷击可见性
- 默认弹道确认 031-Arrow；`hector_warcry` → `proj_bolt200`（029）。
- `zeus_bolt` 改 RemoteStrike 竖劈；`lightning_projectile` 放大+sorting≥45（原先 order=0 被卡面盖住）。

## 2026-07-23 宙斯：落雷恢复 DR；拆技天雷击用 Vefects
- `thunder` RemoteStrike 去掉 ProjectileKey，恢复程序化竖雷。
- `zeus_bolt` 群攻弹道改为 `lightning_projectile`（Vefects Directional）。

## 2026-07-23 弹道换款：默认031 / 宙斯Vefects / 战吼021
- `blade_bolt`/`magic_bolt` → 031-Arrow；宙斯 `thunder` → `lightning_projectile`（Vefects Directional）；
  `hector_warcry` → `proj_frontal`（021-Frontal300）。RemoteStrike 优先播 ProjectileKey。

## 2026-07-23 修复默认弹道（blade_bolt）无粒子
- DualBolt 源 Prefab `playOnAwake=false`；`VFXManager.Rent` 出池/新建后 `Clear+Play`。
- 赫克托尔战吼等主动默认群攻弹道因此重新可见。

## 2026-07-23 受击顿挫结束也重采样休息点
- `HitReact` 抖动结束后 `RerollRestPosition`（区域同回位：Home 中心、边长=卡宽/4）。

## 2026-07-23 卡牌回位引入休息点微抖
- 出场固定 `HomePosition`；每次位移回位 `DOMoveReturnHome` 重采样 `RestPosition`
  （正方形边长=卡宽/4，中心=Home）；突进/斩击/落雷瞄当前休息点。

## 2026-07-23 圣盾重击回血：治疗飘字 + 独立回血盾标
- 纯治疗组（无伤害）不再走 Melee/CastKey，避免误闪反弹盾且漏 `SettleHeal`。
- 圣盾重击回血：`FlashOverlayIcon(icon_aegis_heal)` + 绿字治疗量；反伤仍用 `icon_aegis`。

## 2026-07-23 势能满档 cut-in 只提示技能名
- 满档后同轨再进账：出手前阻塞 cut-in，标题=即将伤害的技能名（战法/普攻/协击/状态归因）。
- 去掉落账路径「势能全开·轨名」；`SkillNameOf` 状态触发改走 StatsSkillOf 归因战法名。

## 2026-07-23 普通格挡触发渐变闪 icon_block
- `mitigation=block` 时受击者 `FlashOverlayIcon(icon_block)`，逻辑同圣盾反伤闪；
  资源 `Resources/ClientBattle/VFX/icon_block.png`（未传则蓝灰占位）。

## 2026-07-23 阿喀琉斯裂甲仅贯穿时播；圣盾反伤闪图标
- 阿喀琉斯之怒 `ExtraIconRequiresPierceBoost`：仅傲慢 25% 贯穿（pierce TraitLine）成功时播裂甲图标，并渐变闪入闪出。
- 圣盾反伤（`mitigation=reflect`）：持盾者 `FlashOverlayIcon(icon_aegis)` 渐变闪（资源待传）。
- 管线新增 `AchillesPierceTagProcessor`（TraitLineExtract 之后打标）。

## 2026-07-23 单挑压暗改为微灰，并覆盖全场无关武将
- 压暗亮度约 78%（不再压到 0.4 黑）；立绘/框/名字/血条/势能条一并乘算。
- 阿瑞斯怒火呼吸改走 `ApplyDim`，不再每帧盖掉压暗；阵亡单位跳过。

## 2026-07-23 单挑无关武将灰显/恢复改为渐变
- `UnitView.SetDimmed`：立绘/卡框颜色 DOTween 渐变（默认 0.45s）；
  `DuelPerformance` 开场压暗与收尾恢复均等待过渡完成。

## 2026-07-23 控制类状态图标移到卡顶外侧 + 抖动
- `StatusIconPanel`：缴械等控制图标从卡中央改为上边缘外侧横排；宽≈卡宽 1/5；
  每枚独立相位/频率抖动（正弦+Perlin）；`UnitView.Configure` 注入卡框尺寸。

## 2026-07-22 手动测试网络化 + Windows 独立包（iOS 通信准备）
- 新增 `battle/tools/battle_server.py`：常驻结算 HTTP 服务（stdlib 零依赖，
  /health /catalog /battle /stats，默认 0.0.0.0:8017），复用 client_battle_bridge 逻辑。
- `ManualBattleBridge` 改 HTTP 首选（HttpClient 后台线程，iOS 兼容）+
  编辑器/桌面子进程回退；面板可改服务地址、页脚显通道。
- 新场景 `Scenes/ManualBattle.unity`；出包 `Builds/ManualBattle/GreekMythManual.exe`。

## 2026-07-22 返回配阵拆除战场可视
- `PerformanceRunner.TeardownWorld`：停播并销毁 BattleBoard；ManualSetup「返回配阵」调用，避免棋盘残留透出配阵页。

## 2026-07-22 手动配阵单次战斗补齐播放控件
- 对战 1 次右上角同 BattleReportTester：重播 / 跳到结尾 / 速度 / 高光回放 / 打开结算；
  左上「返回配阵」；关结算不再自动退回配阵（须点返回）。

## 2026-07-22 手动配阵页：修结算叠层 + 拖拽战法伪影
- 播完/结算开着时压住配阵页；「返回配阵」改 StopPlayback（不再 SkipToEnd 二次弹结算）。
- 关结算后自动回配阵；拖拽只认武将卡、掐 HotControl，消除中间自带战法按钮残影。

## 2026-07-22 客户端手动配阵测试页（6 位横排 + 对战 1/100 次）
- 新增 `Test/ManualSetupPanel.cs`：左 3 A 队 / 右 3 B 队；点空位选武将、拖拽换位、
  战法 ◆自带+2 可配格（＋→战法池→装配）、武将/战法详情弹窗（更换/移除/卸下）。
- 桥接 `Test/ManualBattleBridge.cs` 子进程调新增 `battle/tools/client_battle_bridge.py`
  （--catalog 目录 / 单场战报 / --n 百场统计，跨队同模板自动改名「（敌）」）。
- 对战 1 次正常播放+结算；100 次弹标定风格统计（均回合/胜率/死伤余/技能均值）。
- 文档 `docs/client/manual_setup_panel.md`；index 登记。

## 2026-07-22 标定武将属性档 high/mid/low = 300/200/100
- `cal_teams.py`：全维同值属性档；默认 mid=200；`attr_tier` / `attr_tier_a` / `attr_tier_b`。
- `calibrate_batch.py`：`--attr` / `--attr-a` / `--attr-b`；报告头显示双方属性档。
- `test_calibrate.py` 补属性档与分队覆盖断言。

## 2026-07-22 数值标定战法池 + 千场批量脚本；回合上限改回 999
- `ROUNDS_PER_GAME=999`（打到主将阵亡；stalemate 仍 metadata 压 8）；golden 4 个重生成。
- 新增 `skills_cal.py`：减伤三档（全队常驻 10/25/40%）+ 主动/追击/被动伤害三档（期望系数 100/150/250）。
- 新增 `cal_teams.py` 队伍池：`pure` / `regular_low|mid|high`；`tools/calibrate_batch.py` 千场统计（均回合/死伤/技能释放与伤害）。
- 单测 `test_calibrate.py`；全量 242 通过。

## 2026-07-22 单局回合上限 8 → 16（D-06 修订）
- `ROUNDS_PER_GAME=16`（当日先改 999 后定 16）；打满仍平局残血续战。
- `metadata["rounds_per_game"]` 可覆盖；stalemate 测试/演示场景显式压回 8 保留平局续战覆盖。
- golden 显式重生成；237 后端测试全过。

## 2026-07-22 击杀台词落地（执行者→死者）
- `hero_defeated` 后击杀者发 `trait_trigger`（effect=kill，挂 defeat 同组）；羁绊池 key=死者模板→generic；自杀/击杀者已亡静默；轮换确定性不耗 RNG。
- 新增 `voice_lines_kill.py` + `voice_kill_data.py`（抽取工具扩 `kill` 场景，30 将 291 条）；客户端零改动（TraitLineExtract 抽独占气泡）。
- golden 因新增事件显式重生成（6 个）；新增 `test_kill_voice.py`；235 后端测试全过。

## 2026-07-22 登场羁绊友/敌分池
- 同队播 `{target}` 友池、跨队播 `{target}_foe` 敌池；分册 `（友）/（敌）` 标记，抽取工具写入双 key。
- 排查全表 S1/S2 双向：友口吻补敌词、敌口吻补友词；补 athena↔perseus、zeus↔heracles/hermes 反向。
- 单测 `test_enter_ally_vs_foe_*`；character.md / bonds.md 约定同步。

## 2026-07-22 重播 MissingReferenceException 修复（CameraShaker）
- 重播重建场景后旧 ShakeDriver 已随相机销毁，`Cancel()` 用 `?.` 绕过 Unity 假 null 判定直接访问 → MissingReferenceException。改为 `!= null` 判空并丢弃已销毁引用（下次 Shake 自动重挂）。

## 2026-07-22 手动测试支持跨队同名英雄
- `test_manual_3v3.py`：hero_id 是事件流全局主键必须唯一，B 队与 A 队撞名者自动改名「XX（敌）」；羁绊/性格按 template_id 判定不受改名影响。core 校验不变（同队重名仍报错）。

## 2026-07-22 cut-in 文案调整
- 满档 cut-in 标题改为该次即将出手的技能名（Runner `SkillNameOf`：战法中文名/普攻/状态名）。
- 高伤 cut-in 文本末尾补伤害额度（`…重创 X！-金额`）。text_system.md 同步。

## 2026-07-22 重播复位修复（cut-in 去重/势能残账）
- 点「重播」后高伤 cut-in 不再弹：同战报重播 group_id 相同，`CutInService` 组去重记录跨播放残留把切入全吞掉。
- `BuildWorld` 增加重播复位：`cutIn.ResetDedup()` + `MomentumService.ClearAll()` + `UnitAuraService.ClearAll()`（原主循环只在 gameIdx>0 清，第 1 局带残账）。

## 2026-07-22 满档 cut-in 语义修订（按轨/阻塞/强化音效）
- 按轨过滤：轨已满（≥5）后**该轨**再次进账才 cut-in，刚满当次不切、他轨不影响（客户端按落账前镜像值过滤，服务端事件不变）。
- 阻塞出手：动作组出手前 `CutInService.PlaySoloBlocking` 独占时间轴，切完才开打（`PerformanceRunner.FindFullTrackCutIn` 预扫）。
- 强化音效：cut-in 后该次出手主音效换 `sfx_attack_empowered`（`VFXContext.EmpoweredStrike`）。编译零错误、整场回放无报错。

## 2026-07-22 借刀分段播放（BorrowBladeSplitProcessor）
- 代战/披甲多段借手伤害原被 group_id 聚合成一个单元连劈，响应/追伤全挤到单元后。
- 新增 `BorrowBladeSplitProcessor`（管线首位，谓词由 Runner 用 profile.BorrowBlade 注入）：按组根直接子伤害切段、按首事件 seq 稳定重排——段1(借手)→响应→追伤→段2…恢复事件流因果。
- 离线用 manual_3v3_seed20260722 g1r1 验证拆段序正确；Unity 编译零错误、回放至 r2 无报错。docs/client 3 文档同步。

## 2026-07-22 客户端播放系统结构性重构（行为不变）
- 新增 `EventApplyService`（全客户端唯一落账入口，animated 双模式），消除 MomentumChange/状态/阵亡等 4 处平行落账；`SkillPerformance.SettleSideEvent` 转调。
- 新增 `MomentumFireController`：势能火生命周期收拢，Runner 只发相位信号；hold-off 改「抑制同值重挂」——值变化即重新点火，修复 g1r5 响应涨势能无火（探针复现验证通过）。
- 拆出 `BannerService`（横幅+文字 cut-in 回退）、`SettlementPanel`（战后结算）、`DuelPerformance`、`HighlightSelector`；cut-in 去重收口 `CutInService.Request`；`MomentumService` 与 Audio 解耦（GlobalMomentumChanged 回调）。
- Runner 809→503 行（纯编排）；连发倍率/延迟停顿改 PerformanceProfile 字段。Unity 编译零错误、manual_3v3_seed20260722 全场回放零报错；docs/client 5 个文档同步。

## 2026-07-22 worldview.md 立绘美术手册
- 新增 `docs/worldview.md`（外包立绘分发版）：32 将逐条传记/主战法/战斗叙事/羁绊/商业点/立绘风格/台词摘录，含四阵营色彩基调与差分优先级；用户明示豁免 500 行红线。
- 赫卡忒、卡吕普索台词本待补，文中已标注。

## 2026-07-22 登场台词播放落地
- `game_start` 后播全部场上 S1/S2 羁绊登场（weight→跨队→均速；单元内 A 队→速度）；同组 TraitLine。
- 无羁绊时各队主将 `generic` 登场（A 优先）。抽取 `voice_enter_data.py`；golden 因新增事件重生成。

## 2026-07-22 厄里斯→帕特洛克勒斯 + 阿喀琉斯 S1 羁绊
- 池位替换：`eris`→`patroclus`（英雄）；战法 `patroclus_standin`/`patroclus_armor`；性格 `bonong` 更名「点将」。
- 新增 S1 `bond.achilles_patroclus`（bonds.py + 分册双池 enter/duel/kill）；阿喀琉斯台词侧补全。
- 客户端 BorrowBlade / 名表 / FactionOf 同步；单测改名并通过。

## 2026-07-22 势能火 ActionPause 熄灭修复
- ActionPause 时场上所有势能火渐灭并强制销毁；hold-off 至自身下次 action_start，避免账本仍满被 momentum_change 重挂。
- 回合横幅前亦可提前开渐灭（末位行动→下回合）。

## 2026-07-22 势能火 ActionPause 渐灭
- 上一行动窗结束进入 `ActionPauseSeconds` 时，该武将势能火缩放到零；条仍待自身下次 `action_start` 清。

## 2026-07-22 厄里斯借刀 Melee / 冥火图标 / 势能火
- 厄里斯自带/拆技：`BorrowBlade` Melee，每段由伤害 `source_id` 武将突进斩击。
- 冥火改为中央状态图标（`controlIcon`），去掉 `aura_underworld_burn` CFXR。
- CFXR3 Fire 改挂势能：`momentum_fire`，四轨最高 ≥4/5/6/7 分档小→满分大。

## 2026-07-22 冥火/冰锢接 CFXR3
- 冰锢 `aura_freeze`←**CFXR3 Ice Shield**，卡面下方 y≈−0.3。
- （冥火曾挂 CFXR 火；已改为中央图标，火留给势能——见上条。）

## 2026-07-22 三将落地：厄里斯/赫卡忒/卡吕普索
- roster 32 将：`eris`（对位被动+挑拨）、`hecate`（冥火 DoT 可叠可暴击）、`calypso`（冰锢硬控）。
- 新状态 `freeze` / `underworld_burn`；性格拨弄/岔路/羁留；客户端名表与 FactionOf 同步。
- 单测 `test_heroes_eris_hecate_calypso.py` 6 通过；正式 skills 分册已写。

## 2026-07-22 对位×犹豫
- 明确：现行犹豫只延主动+普攻；对位若要同延，须放在犹豫判定后并扩 `_delayed_actions`，不可挂 `on_action_start`。
- 草案默认 E6：对位与出手同包延 1 窗；石化下仍可 roll 犹豫以拖住对位。

## 2026-07-22 纷争对位草案口径
- 去掉「整窗 skipped / 冥锁」误写：石化只禁主动+普攻；`skipped` 仅为 action_start 标记，钩子仍跑；冥锁非现役战法。
- 明确对位被动挂行动窗、不受石化禁止。

## 2026-07-22 武将编制草案（未落地）
- 新增 `docs/dev/hero_proposal_eris_hecate_calypso.md`：厄里斯对位三连、赫卡忒冥火 DoT、卡吕普索冰锢硬控+DoT。
- 选型与三段式效果/实现/事件流 + 拍板表；`skills/index` 已挂链；**未改 roster/代码**。

## 2026-07-22 默认群攻弹道精修
- 飞行弹道不再拖 `slash` Burst：物理 `blade_bolt`（DualBolt Orange）、魔法 `magic_bolt`（DualBolt Purple）。
- `LaunchProjectile`：朝向切线、二次贝塞尔微弧、缩放呼吸；群攻错峰起飞、同帧抵达结算。

## 2026-07-22 石化冻结呼吸
- 石化时停立绘浮动、阿瑞斯红呼吸、圣盾描边呼吸、雷霆驱动与光环粒子，解除后恢复，强化「石像静止」。

## 2026-07-21 阿瑞斯改卡框红呼吸
- 血战/战神之勇常驻：去掉 FireRimFx 火舌，改为 `UnitView.SetAresRage` 卡框红光呼吸（弱档暗红慢、强档更亮更快）。

## 2026-07-21 Antique 立绘可见性
- Antique 框图中心是实心暗底（非透明挖空），立绘改为叠在框前内窗；先前放框后会被完全挡住。

## 2026-07-21 统一 Antique 竖框
- 全武将立绘边框改用 `CardFrames/antique_frame`（Interface Frames **doc view** 1024×1680，非正方形那张）。
- 立绘在框后等比 contain 入内窗；框不染色、不拉伸变形。

## 2026-07-21 石化去遗像感
- 石化不再 100% 灰阶：立绘最多约 40% 暖砂岩叠染，卡框约 68% + 石色描边；保留五官色彩可读。

## 2026-07-21 圣盾降亮度
- 圣盾关掉 All In 1 `GLOW_ON`（整卡加亮是罪魁）；只保留卡框金描边 + 轻微 Outline 呼吸。

## 2026-07-21 All In 1 石化/圣盾 + Animated 闪电
- 宙斯改用 Digital Ruby **Animated** 贴图闪电（Demo 下方那种，`dr_lightning_bolt_anim`）。
- 石化：`SetPetrified` → All In 1 灰阶+石色 tint 渐变（立绘/卡框）；无 shader 回退旧覆盖层。
- 圣盾：`aura_aegis` → All In 1 金描边+呼吸辉光（不再挂粒子光环）。

## 2026-07-21 宙斯闪电接入 Digital Ruby 免费包
- 常驻 `ThunderAuraDriver` / 触发 `RemoteStrike` 改用 `DrLightningUtil` → `LightningBoltScript`。
- prefab：`Resources/ClientBattle/VFX/dr_lightning_bolt`；asmdef 引用 `DigitalRuby.LightningBolt`。
- 自写 `LightningBoltFx` 暂留文件，已不再被常驻/触发路径调用。

## 2026-07-21 闪电/火舌减廉价感 + 常驻稍密
- 闪电：三层（晕/辉/芯）+ 端点收束 + 相关位移折线 + 柔退。
- 火舌：三层 + 宽曲线 + 双频噪声高低火舌；常驻闪电略加密。
- 再上一档需 Bloom/专用火焰贴图条（程序化 LineRenderer 上限在此）。

## 2026-07-21 宙斯常驻恢复长道 + 降频
- 常驻恢复边→边/对角/短弧/竖劈长版形态；透明度 0.7~0.9；频率降低。
- 触发贯穿对面透明度仍为 0.2。

## 2026-07-21 宙斯闪电短道/透明度
- 常驻：控长度（≤0.7），透明度随机 0.6~0.8。
- 触发贯穿对面：透明度 0.2。

## 2026-07-21 宙斯闪电密度/透明度
- 常驻更密（多道同屏 + 更短间隔）；每道透明度随机 0.35~1。
- 触发贯穿对面默认半透明（alpha×0.5）。

## 2026-07-21 宙斯常驻闪电多向乱劈
- 常驻不再只竖直：边→异边 / 对角斜穿 / 短弧跳电 / 少量竖劈 加权混合。
- 对齐常见卡牌雷环策略（落点卡缘采样、方向灵活、偶发分叉）。

## 2026-07-21 程序化闪电 + 卡边火舌（弃用粒子糊脸）
- 宙斯：`LightningBoltFx` 折线闪电；常驻卡面频劈，触发 `StrikeWorld` 贯穿对面整卡。
- 阿瑞斯：`FireRimFx` 卡边火舌带（血战底边弱 / 战神之勇四边）；不再用 CFXR 火粒子。
- 结论：已购粒子包做不出「常见闪电/卡边火舌」，改程序化。

## 2026-07-21 宙斯雷霆：卡面频劈 / 触发贯穿
- 常驻：去掉绕身电弧；`ThunderAuraDriver` 在卡面上高频随机竖劈。
- 触发：RemoteStrike 一道竖雷 Y 拉满贯穿对面整张卡。

## 2026-07-21 宙斯雷霆：自然随机落劈绕身
- 废止 CFXR Hit Electric 糊脸挂法。
- `aura_thunder`←绕身微放电；`aura_thunder_bolt`←竖向落雷；
  `ThunderAuraDriver` 不规则间隔在卡缘随机点播 1~2 道竖劈。

## 2026-07-21 阿瑞斯外侧均匀火带（往外喷）
- 火贴卡缘外侧；关 CFXR「Small fire」柱状大火苗，只用余烬小粒子。
- 扁长 Box 高密度出生 + 本地 +Y 向外速度 → 均匀边火带（消点状）。
- 血战=底边外侧弱带；战神之勇=四边外侧整圈。

## 2026-07-21 阿瑞斯火密度微调
- 血战：尺寸/透明度/发射略加强（仍弱于战神之勇，带状可见）。
- 战神之勇：侧边拆 3 段重叠加密 + 更高发射率，消除点状稀疏。

## 2026-07-21 阿瑞斯火：血战微弱底带 / 战神之勇四边整圈
- `blood_battle`：卡底微弱火带（小尺寸+低压透明度，刚能看出）。
- `ares_might`：卡四边（顶/底/左/右）整圈着火；拆技仍无火。

## 2026-07-21 阿瑞斯火：仅自带，战神之勇更宽
- 火焰仅挂自带【战神怒火】：`blood_battle` 卡底 / `ares_might` 卡顶（半宽 1.05，比血战 0.8 更宽）。
- 拆技【战争狂热】`war_frenzy` 去掉挂身火（注册表 + PerformanceDatabase）。

## 2026-07-21 阿瑞斯火带 + 哈迪斯黑雾极透
- 阿瑞斯：单实例 Fire 沿卡宽 SingleSidedEdge 连续出火（一带火，废止点状 3 簇）。
- 哈迪斯黑雾：alpha×0.12 + 降发射密度，避免整卡变黑。

- 新购 CFXR 导入 `Assets/JMO Assets/`；五神常驻特效重配：
  宙斯雷霆 ← Hit Electric B、雅典娜圣盾 ← Magic Aura A (Runic)、
  阿瑞斯火 ← Fire (No Smoke) 沿边 3 簇（foot/head）、
  波塞冬潮汐 ← LightGlow A (Loop, Blue)（新 key `aura_tide`）、
  哈迪斯冥域 ← Suspicious Cloud (Black)（新 key `aura_underworld`，
  吸血/幽影/献统三状态共用）。
- UnitAuraService 重写：直实例化（不回池）、禁 CFXR_Effect 自毁、
  一次性特效强制循环；不再改粒子形状/染色（此前观感差根源）。
- poseidon_tide / hades_* 状态首次有挂身光环（注册表加 auraKey）。

## 2026-07-21 阿瑞斯常驻特效改为火焰（客户端）
- `blood_battle` / `war_frenzy` → `aura_fire_foot`（卡底持续火）；
  `ares_might` → `aura_fire_head`（卡顶持续火）。
- 新增 Resources variant（复用已购 Explosion Vertical Loop，无需另购）；
  StatusPresentation 增 AuraOffset；UnitAuraService 按偏移挂载 + 橙红染色。
- 着火为卡头/卡底火焰带：单发射器 SingleSidedEdge 全宽随机出火（多柱叠加
  爆红方案废止）；保留 flipbook 原色渐变；直实例化不回池。
- 与技能数值无关；旧 bloodlust 光环不再挂阿瑞斯状态。

## 2026-07-21 阿瑞斯自带/拆解数值定稿
- 【战神怒火】血战：通用易伤 +20% + 暴击伤害 +50%（原物暴 +20% 废止）；
  战神之勇武/速 +20，并列小站位。
- 【战争狂热】自身物伤 +30% + 暴击率 +15%（整局）。
- oracle golden 重生成（血战改暴伤影响结算）。

## 2026-07-21 阿瑞斯拆解【战争狂热】v5
- 改为仅自身暴击率 +15%（整局）；原 v4 物伤+30%/暴击+10% 废止。
- golden 无差异（modifiers 不进事件流）；222 测通过。

## 2026-07-21 全屏 cut-in（单人 + 决斗裂缝交错）
- 新增 `VFX/CutInService`：单人 cut-in＝暗幕+阵营色斜带+巨幅立绘+大字（非阻塞）；
  决斗 cut-in＝中央斜裂缝线分屏，两半屏卡对向滑过 × clash_cutins 次、逐次加速，
  末次中线对峙+VS+白闪弹开（阻塞，PlayDuel 内）。
- RequestCutIn 增 heroId 参数：有主体走全屏 cut-in，战术变更等回退 OnGUI 横幅。
- 层级 80~90 登记 rendering_layout；立绘复用 Portraits 路径；新 sfx_cutin_solo。

## 2026-07-21 单挑台词双池落地（前后端）
- 服务端：`voice_lines` + `voice_duel_data`（分册抽取）；`trait_trigger` 挂 duel 组。
- 客户端：`PlayDuel` 按时点播叫阵/应战/拒战气泡；TraitLineExtract 跳过 Duel 组。
- 改词：改 `docs/character/*.md` → `python battle/tools/_extract_duel_voice.py`。

## 2026-07-21 单挑配对升级（D-03 演进）
- 参赛：武力>智力；队内武序后同序号对位 + S1/S2 羁绊初对；武差线性入池。
- 候选按羁绊→武差取一对真决斗；空池固定叫阵-拒绝；`clash_cutins` 下发。
- **废除**性格约战机械（`DuelBehavior`）；台词 `duel_*` 仍可播。
- **胜率**：高武力方 `50% + d`（百分点），d≥50 必胜（原 `50%+d×5%` / d≥10 废止）。
- 新增 `battle/bonds.py`；客户端 PlayDuel 按段数对撞；golden 重生成。

## 2026-07-21 哈迪斯：血誓→吸血属性；汲魂 150%
- 冥域君临：【冥河血誓】改为己方 `lifesteal_bps+10%`（`hades_lifesteal`）；
  幽影/献统不变。冥河汲魂全体魔法 180%→150%。客户端名表/结算映射同步。

## 2026-07-21 犹豫准备技同窗释放 + 先攻多吃一回合排序
- 延迟补结算进入准备时 `skip_tick`：prepare_rounds=1 不再同窗 release
  （戏言犹豫战吼类：N 延后 → N+1 准备 → N+2 释放）。
- `has_first_strike`：排序仅认 `tick < duration`，duration=1 不再多吃下一回合序。
- manual 202607211 g1r7 同窗「开始准备+释放战吼」即此 bug；hesitation/statuses 文档同步。

## 2026-07-21 奥林匹斯调参（宙斯／阿瑞斯／蛇杖）
- 雷霆落雷：智力 100%→85% 魔法；血战物伤易伤→通用易伤 +20%（物暴仍 +20%）。
- 蛇杖治疗基数：1%→0.5% 兵力上限 + 智力；olympus.md + golden 同步。

## 2026-07-21 英雄批数值调参（阿喀琉斯／试炼／狮皮／闪击／战吼）
- 阿喀琉斯之怒：物暴 +35%、追伤 80%；傲慢去掉残兵比例门槛（无条件 25%）。
- 十二试炼：每次试炼后下一次兵刃系数 +5%（可叠，非试炼兵刃消费）。
- 狮皮反击：70% 反打 45%，反击成功必挂来源伤害 −15%（1 回合）。
- 镜盾闪击 280%、特洛伊战吼 190%；docs/skills/heroes + traits/golden 同步。

## 2026-07-21 全将单挑三态视角通扫
- 四阵营单挑池按「叫阵／应战／拒战」说话者纠偏：删「应约」入叫阵、
  「接招／看好了／留力／别冲动／听令退」等反视角；海族（塞壬应战、
  斯库拉拒战、奥德修斯对雅典娜）同步修正。

## 2026-07-21 单挑拒战视角纠偏（阿喀琉斯样板）
- 拒战=防守方拒绝对方叫阵；阿喀琉斯原词「逃一次／懦夫的选择」是骂对方拒战，已改。
- 同步修正阿瑞斯／赫拉克勒斯／尼刻／阿塔兰忒同类反视角；总则拒战条加反例。

## 2026-07-21 台词本视角修正 + 残血台词删除
- 残血不发台词：29 将 `low_hp` 场景全删（bonds.md 引用同步清理）。
- character.md §2.2 增「说话者→对象」视角总表（应战=对方先叫阵、
  击杀=我杀了你、连携=副将对神谕主将）；修正各分册反视角台词
  （家人/主从羁绊的单挑与击杀池原写成护驾/报仇，改为镜像对阵口径）。

## 2026-07-21 连携台词仅保留自带主动将
- 按 `assist.md`：仅副将自带 `timing=active` 可被神谕连携；删掉其余将
  `combo` 台词；保留 perseus/hector/triton/siren/thanatos，且羁绊池只对神谕源头。

## 2026-07-21 角色传记·羁绊·台词本（character）
- 新增 `docs/character.md` 总则（双池制／场景优先级／单挑三态）与
  `docs/character/`：bonds + olympus/heroes/sea/underworld 共 29 将；
  每场景通用+羁绊各 2~3 条；立绘关键词齐全。供玩家传记与剧情策划落地。

## 2026-07-21 追伤触发即记 passive 势能
- `MOMENTUM_ON_TRIGGER_KINDS` 含 `fury`：追伤伤害落地 +1（不要求再暴击）；
  暴击不双计。`momentum.md` 同步；heroes 单测固化。

## 2026-07-21 高光回放计入满势能 cut_in
- 选窗改为观感分 = 伤害 + cut_in×3000（manual 阿喀琉斯伤最高窗常无满势能）；
  静默落账路径仍会播满档 cut-in 横幅；高光开播重置 cut-in 去重。

## 2026-07-23 魅惑改写选敌初步备选池
- `alive_enemies`：持 `charm_targeting` 时返回除自身外全体；技能互斥/指名等
  仍在池上执行；受击率选人改为池内等概率。撤销不完备的 `select_enemy_side`。
- 文档 statuses/targeting/status_voice 同步；`test_charm_aoe` 覆盖池/全体/指名/互斥。

## 2026-07-23 埃癸斯圣盾调参：反弹 12%→15%、重击回血门槛 10%→8%
- `AEGIS_COUNTER_RATE_BPS` 1200→1500；`AEGIS_HEAL_THRESHOLD_BPS` 1000→800；
  olympus.md 同步；men_gods golden 重生成。

## 2026-07-23 踵之弱 7.5%→20%
- 阿喀琉斯性格 `aoman.heel` 默认 750→2000 bps；traits.md / hero_specials.md 同步。
- 含阿喀琉斯的 golden 因暴击分支变化重生成（standard×2、men_gods）。

## 2026-07-23 阵型系统落地（雁行阵 1/2/6）
- 新增 `battle/formations.py` 注册表：`TeamSetup.formation`（默认空=行为不变）；
  配将按站位覆盖初始受击点数，每局 game_start 后确定序重挂整场被动（PERMANENT）。
- 雁行阵：点数 10800/10800/5400（满兵受击率 40/40/20，6 号残兵→10%、
  1/2 号残兵→32.5%）；1/2 号减伤 5%、6 号增伤 8%。
- 状态名 names.py / ChineseNames.cs 同步；新文档 `docs/mechanics/formations.md`；
  单测 `test_formations.py`（5 例），全量 249 过、golden 无扰动。
- `test_manual_3v3` 接上 `TEAM_A_FORMATION` / `TEAM_B_FORMATION`（A 默认 `yanxing`）。

## 更早条目

按 500 行红线拆分的历史存档：
- 2026-07-19 ~ 07-20（Phase 4 执行期）：`changelog_archive_phase4.md`
- 2026-07-09 ~ 07-15（Phase 3 后期 + 客户端框架期）：`changelog_archive_phase3_client.md`
- 2026-07-06 及以前（Phase 1/2）：`changelog_archive_phase12.md`
