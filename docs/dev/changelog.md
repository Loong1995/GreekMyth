
# Changelog

## 2026-07-28 魔法默认弹道 → Magic Effect1；竖雷加闪电感

- `magic_bolt` ← Magic Pack **Effect1**（`VfxUsage.Projectile` 新用途：保留母件、
  摘 Motion/Target，位移归 LaunchProjectile；`WireMagicBolt` + AutoHeal）。
- 竖雷去灰白：外晕饱和宝蓝 + 细白青芯 + 分叉；`DrLightningFlicker` 周期重
  Trigger（重算折线+推进贴图行），避免整段静止灰线。

## 2026-07-28 宙斯竖雷纯白修复·第二轮（亮度通道，P-83 第二层）

- 迁 URP 后**仍纯白**的真因：`URP/Unlit` 不乘顶点色，LineRenderer 的 `alpha`
  （colorGradient）静默失效 → 每道雷恒满亮，加色与舞台底色相加在 HDR 下裁剪成白。
- 亮度改写**实例材质 `_BaseColor`** 并封顶 `MaxIntensity=0.65`；端点收细改 `widthCurve`。
- 参数重定标：主芯亮度 0.52（神罚 0.62）、宽度 0.58（0.68），分叉更细更暗。
  离屏实测底色 0.55 上峰值 0.952（已白）→ **0.688**。
- 每道雷的实例材质随 `DrLightningUtil.Release` 一起销毁，不再逐次泄漏。
- `ThunderAuraDriver` 同步降亮降宽。

## 2026-07-28 宙斯竖雷纯白修复（DR → URP Unlit，P-83 第一层）

- 根因：DR 材质挂 Legacy `Particles/Additive*`，URP 移动端回退成白带。
- 三材质迁 `URP/Unlit` Transparent+Additive（`WireDrLightningUrp` + AutoHeal）；
  贴图 ≤1024、关 mipmap；`DrLightningUtil` 运行期兜底。

## 2026-07-28 宙斯雷系：取消卡面雷命中 + 竖雷加粗分叉 + 神罚强震

- 卡面 `hit_lightning` 取消：`thunder` / `zeus_bolt` / `zeus_divine_punishment`
  均 `HitKey=none`（绕身 `shroud_thunder` 已够；巨伤仍可覆盖 `hit_massive`）。
- 竖雷：旧单道 alpha0.2 灰线 → DR **主芯粗亮 + 两侧分叉**（神罚再加一道）；
  时长略拉长。禁 RFX4；Vefects 竖劈弹道仍备选（`LaunchSkyBolt` 直线下落）。
- 神罚受击：显式抬高震屏（Amp 0.42 / 0.38s）+ 照常 HitReact（击退/沿线颤）。

## 2026-07-28 魔法默认命中改 Magical Stars Pink

- 物理 `hit_sword`＝Impact_Cut_V1 **定稿**不动。
- 魔法 `hit_petrify` ← CFXR **Hit Magical Stars (Pink)**（星芒+刺环+拖尾，
  比 Hit Light 更密更魔幻）；定径仍 ×2.5。

## 2026-07-28 默认命中：Impact_Cut 直线刀光 + Hit Light Pink

- 物理 `hit_sword` 改回最早 **Impact_Cut_V1**（Cone 横切≈直线，非环形）。
- 魔法 `hit_petrify` ← CFXR **Hit Light C (Air, Pink)**。定径仍 ×2.5。

## 2026-07-28 默认命中：同 Slash 换色 + 放大一倍

- 物理/魔法共用 Fire Slash：`hit_sword` 金红；`hit_petrify` 同件染紫粉。
- `VfxCircleFit` 倍率 1.25→**2.5**（放大一倍）。

## 2026-07-28 默认命中：金红刀光 + 紫粉魔法光

- 物理 `hit_sword` ← Cartoon Coffee **Fire Slash v1**（金/橙刀身、红刃缘）。
- 魔法 `hit_petrify` ← CFXR **Hit Light C (Air, Pink)**（紫粉环+刺光）。
- 仍卡心 PlayOn、`VfxCircleFit×1.25`；弃用上一轮 Vefects Spiky/Burst。

## 2026-07-28 默认命中改接 Vefects（弃 Magic Collision）

- Magic Effect18/19_Collision 卡面观感不佳，改回 Vefects：
  **物理** `hit_sword` ← `Radial_Spiky_Hit_01 Random_Rotate_Bunch`（4/8·308）；
  **魔法** `hit_petrify` ← `Radial_Burst_01 Random_Rotate_Bunch`（4/8·294）。
- 形体分型：刺击 vs 爆发（不是横光换色）。`VfxCircleFit×1.35`；仍卡心 PlayOn。

## 2026-07-28 物理/魔法默认命中改 Magic Collision + 卡心放大

- `hit_sword` ← Magic **Effect18_Collision**；`hit_petrify` ← **Effect19_Collision**
  （画廊 2/8 件 40/41；走 `VfxPackStandardizer`，`WireDefaultHitVfx`）。
- 尺寸：`VfxCircleFit` 定投影圆后再 ×**2.5**（厂包 Collision 贴卡偏小）。
- 挂载：`SettleDamage` HitKey 改 **`PlayOn(卡根)`**，跟击退/颤动，不再世界定点。
- 宙斯 `hit_lightning` 仍为同原料 Effect19_Collision 的专配件，未改。

## 2026-07-28 画廊 [1/8] 序号漂移：清备份污染 + 过滤对齐

- **根因**：我方标准件按 name Ordinal；`_bak_*` / `*_pre_magic` 曾在 Resources
  里插位，把后续件号整体顶偏（文档写 41/61≠画廊实号）。另：入库真 key 仍会漂——
  这是 Ordinal 的固有行为，点名必须以 key 为准。
- **修复**：5 件备份/过渡迁出 `Assets/_Archive/ClientBattleVFX/`；画廊
  `EnsureOwnGroup`、预热、`_gallery_index_dump.py` 共用 `VfxResourcesFilter`；
  [1/8] HUD 提示「点名请用 key」。P-82。

## 2026-07-28 渲染器错述更正 + 关无用阴影 + 画质档接到镜头层

- **渲染器类型更正**（P-81）：实际是 **URP Universal Renderer（3D 前向）**，
  非 2D Renderer（`{PC,Mobile}_Renderer.asset`＝`UniversalRendererData`，无
  `lightBlendStyles`）。三处现行文档的采购红线口径改为「只要求支持 URP」；
  权威收口到 `vfx_pack_integration.md` §2.0，并说明为何**不可**换回 2D Renderer
  （`CardDepthProxy` 靠深度排罩身前后、折射靠不透明拷贝）。
- **实时阴影两套全关**：场上无任何 shadow caster（地面 `shadowCastingMode.Off`、
  全 unlit sprite、厂包灯阴影落盘期已关），白留一张阴影图和一遍 pass。
- **画质档接到镜头层**（此前完全脱钩）：Bloom 是全屏 pass，`VfxTierScale` 管不到，
  改由 `BattlePostFx.Apply()` 按 `VfxQuality` 落；新增 `BloomIntensity/
  BloomThreshold/BloomHighQuality` 三张档位表。低端**不关 Bloom**（会塌成喷洒＝删效果），
  只降强度 + 关高质量滤波（真正的开销）+ 抬阈值（顺带还回一点对比度）。
  顺带修调用顺序：`LoadUserPreference` 必须先于 `BattlePostFx.Ensure`。
- **编辑器 ≈ 真机**：PC RP 对齐 `UseFastSRGBLinearConversion`（色彩路径）与阴影；
  逐件强度、镜头层、MSAA、拷贝降采样、色彩分级模式全部一致；**仅剩 `RenderScale`
  一项有差**（真机 0.8/编辑器 1.0，故意保留，锐度只能以独立版验收）。
  编辑器画质档菜单切档即刻重写镜头层；开场 `[VfxQuality]` 日志加打系数与 Bloom 参数。

## 2026-07-28 宙斯神罚：`hit_lightning` + 档 2 命中裂地

- 神罚卡面命中与天雷击共用 `hit_lightning`（Effect19_Collision 喷射粒子）；
  落雷仍走魔法默认 `hit_petrify`。
- `GroundStrengthTier=2`：命中拍出档 2 裂地。`ShouldPlayHit`：魔法默不裂，
  profile 显式档位 ≥1 才放行命中裂地（弹道/轨迹仍只跟物理）。

## 2026-07-28 播放单元按「因果批次」重组（schema 1.5.2 status_catalog）

- **因果批次**：`EventGroup.BatchId`＝引发本组的那次行动（沿 parent 上溯，
  **止于最近的行动组**，否则一个行动窗内互不相干的两次普攻会被算成一批）。
  processor 拆组统一走新的 `EventGroup.Fork()` 复制注记，漏带批次＝该并的并不起来。
- **BatchTriggerMergeProcessor** 取代 `CollectiveTriggerMergeProcessor`：旧口径要求
  「相邻 + 客户端白名单」，中间夹一个节点或别人的响应就并不起来；新口径按
  **同批次 + 同状态**并组（群攻打三人 → 下一个单元是三人的落雷一起劈，实测
  `hector_warcry n=11` → `thunder n=6` 各一个单元）。跨持有者只有标了
  `simultaneous` 才并——「持有者突进」型演出并组会变成一个人替所有人挥刀。
- **标签真源回到服务端定义处**（契约加法演进 1.5.2）：`StatusDef.playback_tags`
  定义期自注册 → `battle/status_catalog.py` 导出战报头 `status_catalog`（只收带
  标签的状态）。落雷 `simultaneous`、圣盾反制/代战借刀 `sequential`。战法侧同理
  用 `skill_catalog.tags` 的 `per_target` / `simultaneous` 作「群攻＝一个单元齐射」
  的特殊配置口子（编译期注记 `ForcePerTarget/ForceSimultaneous`）。
- 准备型战法的**宣告**单元改飘「蓄势·X」：它常紧跟在释放单元后（打完接着蓄下一发），
  干飘技能名会读成「同一个技能放了两遍」（赫克托尔战吼即此例）。
- **群攻被台词切碎**（同日实测追修）：`TraitLineExtractProcessor` 原按「一条伤害
  一段」切，于是**别人挨打时说了句话**就能把群攻打碎——manual 0722 第二回合赫克托尔
  战吼里混进阿喀琉斯「踵」受击台词，整组被切成 4 段逐个飞。改为：齐射组整组不切
  （台词按位置提到组前/压到组后），逐段组只在台词处切段。实测该组 4 段 → `n=10`
  一个单元；十份战报编译前后事件总数逐份不变（无漏账/重复落账）。
- 受击颤动振幅翻倍（`HitTrembleAmp` 0.22/0.13 → 0.44/0.26，×微调圆半径）：
  实测只看得见击退、看不出震。
- 导出的 `.playback.json` 增 `batch` 字段；「这两组为什么没并」先看 batch。
  golden 全量重生成（仅战报头新增字段，事件流零变化）。

## 2026-07-28 宙斯【神罚】＋专属高光 cut-in 通道（schema 1.5.1）

- **神罚**（`skills_gods.py`）：每回合内敌方**单个**单位被落雷打满 3 次 → 宙斯对
  敌方**兵力最低**单位 100% 魔法。计数按受击者记在宙斯自己的【雷霆】实例
  `round_counters["punish:<id>"]`（引擎回合头统一清零，不另建回合容器）；宙斯
  阵亡/无雷霆则不判定；神罚伤害 kind=`lightning` 防连锁。新助手
  `skill_common.lowest_troops_enemies`（绝对兵力口径，区别于既有比例口径）。
- **专属高光通道**（可复用，后续核心卡逐个接）：契约加法演进 **1.5.1** ——
  `skill_trigger.kind="highlight"` + `hint.cut_in="highlight"`。高光归因 id
  （`zeus_divine_punishment`）**不进 REGISTRY / skill_catalog**，只在
  `names.py` / `ChineseNames.cs` 各加一条中文名。客户端 `CutInPlanner` 新增
  最高优先级触发源，读注记即取景——**阈值不在客户端**：「算不算高光」是玩法语义。
- 台词：分册新增「高光 highlight」抽取场景（池 key＝高光名，缺则 generic），
  产出 `voice_highlight_data.py` + 发词入口 `voice_lines_highlight.py`；宙斯补
  专属高光词一条，神罚前独立组根发，客户端 TraitLine 播完才进 cut-in。
- 落雷受击改走**魔法类默认** `hit_petrify`（`thunder` Special 的 HitKey 留空），
  与其他魔法伤害同一套受击语言；`zeus_bolt` 仍用 `hit_lightning`。
- golden 全量重生成：本次改动使宙斯输出显著上升，standard 两局系列长度变化
  （事实变更，非"改 golden 迁就测试"）；schema 版本号变更导致其余 golden 头部同步。
  `test_skill_catalog` 的版本断言改为下界（`>= "1.5.0"`），免得每次小版本都改测试。

## 2026-07-28 罩身默认不锁受击；雷霆神谕改回罩身
- **罩身默认不影响受击**（人工定案）：`HitReact` 原来只要有罩身在场就禁一切卡根
  位移（P-58 怕把罩甩出去），代价是罩身回合完全没有打击反馈。罩由
  `VfxShroudFollower` 跟着卡走、甩不出去，故默认改为照常击退+颤动；
  要"纹丝不动"语义的罩身在注册表置 `shroudLocksHitMotion`（新字段，默认 false）。
  判定入口 `UnitAuraService.HasHitMotionLock`（`HasShroud` 保留作显隐查询）。
  战神之勇随默认走。
- 宙斯雷霆神谕改回**罩身** `shroud_thunder`（Effect19），不再走全局氛围。
  流水线新增加法式参数 `Standardize(..., keepLayers)`：罩身默认摘游离电弧
  （卡面尺度＝全屏乱电 P-78），但这件的主视觉就是电弧，摘完只剩被中和的
  折射壳＝什么都看不见，故豁免 `LightningTrails` / `Fringe`（`WireThunderShroud`）。
  折射壳曾一并豁免以求"完整厂件观感"，实测糊卡面到不可接受，**当场否决**——
  P-77 是实测结论不是保守。
- 流水线加对称旋钮 `dropLayers`（精确名匹配，`keepLayers` 是子串）：点名摘
  **观感语义与用途冲突**的层。雷霆罩身据此摘掉一次性喷射爆发
  （`Particles`/`Point`/`Fog`/`ImpactDecal`）——往外喷的雷电线读作"正在放技能"，
  而罩身表达的是常驻态。成品＝`ShieldAdd3` + `ShieldFringe`，与
  `shroud_ares_might`（加色壳+边环+背火）同构。体检 64 件 0 不合格。
- 新增 `VfxPhaseDesync`（通用）：常驻件挂载时随机快进 0~1.6s + 速度失谐 ±12%。
  三人同挂一件时逐帧同步闪＝"一个动画复制三份"；预演与失谐缺一不可（只预演，
  相位差恒定，久看仍是整齐地错开）。参数 `StagePerformanceConfig.ShroudDesync*`。
- 排查教训入 P-79 段：接件后"什么都没看到"，真因是 Unity 停在 Play 模式、
  流水线拒绝写资产，标准件压根没落盘。第一步永远是先证明文件在盘上。
- `ambient_thunder_storm` 与 `WireThunderStorm` 留库，作为 `AmbientField` 用途的
  参考实现，当前未接线。

## 2026-07-28 雷暴改自上而下 + 三源加密
- 症状「雷往镜头方向劈」：原料是绕人护罩，`LightningTrails` 轴朝 +Y、
  `LightningTrailsBottom` 轴朝 **+Z 水平横喷**——护罩的朝向语义在场域里不成立。
  `WireThunderStorm.OrientAsStorm` 把两层轴改 -Y 并抬到 2.5 高，改为自上而下扎地。
  个性几何放接线脚本、不进流水线（同罩身个性裁层的分工）。
- 加密：全局 `AmbientFieldDensity` 1 → **1.8**；新增每源 `Yaw` 自转，
  避免多处看出是同一动画复读。
- 收成两源（人工定案）：战场一处（游走 1.3×半宽 / 0.5s）+ 背景一处
  （推到主战场外 1.7、抬 7、游走 2.0×半宽 / 0.45s、Yaw 180），都自上而下劈。
  两处游走半径都 ≥1 倍半宽＝落点铺满各自那一带，钉在小圈里会被读成
  "一台在原地循环的机器"。

## 2026-07-28 雷暴场域改多源：中心下劈 + 背景天空，密度可配
- `StagePerformanceConfig.AmbientFieldSources`：场域件从"钉地面中心一处"改为
  **一组源**，每行可配位置（按战场尺度折算，非世界硬数）/尺度/疏密/随机游走
  半径与间隔/要隐藏的层。默认两处：中心下劈（游走 0.45×半宽、0.8s 换点）与
  背景天空（推到主战场外 1.7、抬 7、尺度 1.35、疏密 0.55、游走更勤更大，
  并关掉 `ImpactDecal` 地面接触层——悬空的接触痕是半空光斑）。
- 新增全局 `AmbientFieldDensity` 与新组件 `AmbientFieldWander`（源在圈内换点，
  钉死一点会被读成"中心有个循环动画"而不是打雷）。
- 疏密与画质档正交：档位是设备维度、密度是演出维度，两者相乘。密度在
  `VfxTierScale` 之后施加，故不会被档位那一步按原始值覆盖。

## 2026-07-28 特效自带音源一律保留（P-79 第三次修正）
- 上一版把件自带 `AudioSource` 当"不该在件上的机制"摘掉，人工否决：素材音
  （爆裂/电流/风声）与画面同拍，属这件观感的一部分；`SfxManager` 管的是战斗
  语义音，两者叠加，要静音走音量总线，禁止在落盘期删。
- `git checkout` 回滚 63 件 prefab 复原音源（23 件共 25 个），流水线
  `TrimAudio` → `KeepAudio`（只统计不删），体检删掉 `AudioSource=0` 项，
  清洗菜单更名为「…清死层/碰撞，音源保留」。重跑：63 件 0 不合格。
- 顺带记入 P-79：`AudioChorusFilter` 等滤镜带 `RequireComponent(AudioSource)`，
  滤镜未摘时删音源会静默失败（组件还在、日志却写"已摘"）。
- 文档：`vfx_mobile_budget.md` / `vfx_standardization.md` 验收项 /
  `vfx-standardization.mdc` 同步改口径。
- 全量清洗改造收尾（63 件）：粒子估算改为按 `maxParticles` 截断（此前百万级
  假警报把真大户淹了）；清洗把"观察"与"改动"分记，无改动不落盘（现重跑
  改动 0 件＝幂等可验）；体检报告新增「警告（不判死）」段。
- 探档改内存为主：显存只在安卓且 ≤1G 时降一档（`graphicsMemorySize` 移动端是
  估算值，iOS 常报成内存的一个分数，原 `||` 规则会把 6G iPhone 打成低端）。
  开场打一行 `[VfxQuality] tier/mem/vram/device` 判据供真机排查。
- 新增菜单 `GreekMyth/特效/画质档`（低/中/高 + 打印判据，存 EditorPrefs，
  Play 中即时生效），调试切档不用改代码。`vfx_mobile_budget.md` 补
  §二 调试表 + §七 机型覆盖现状（含尚未接的设置面板 UI）。
- 现状：63 件 0 不合格，3 件带警告并已在 `vfx_mobile_budget.md` §六 逐件登记
  接受理由（`cast_duel_launch` 10266 / `hit_massive` 8008 / `hit_shield_counter` 5002，
  均为单占播放单元里的瞬时 burst）。验收清单加两条，约束后续新件同样登记。

## 2026-07-28 特效画质分档：只降强度不删效果（P-79）
- 新机制 `VfxQuality`（Low/Mid/High 三档系数：粒子/折射/灯亮度）+ `VfxTierScale`
  （根＝粒子总闸、折射层与每盏灯各自挂）：成品保留厂包满强度，缩放在运行期，
  改平衡点只改三行系数。档位开放给玩家（自动/低/中/高，PlayerPrefs 持久化，
  `PlaybackWorldBuilder` 启动时载入）；**不做「关闭某类特效」的开关**。
- 流水线 `ApplyMobileBudget` 全用途执行：挂档位缩放 + 关粒子碰撞/触发；
  灯不再删（改关阴影 + 第 2 盏起 MinTier=High）；折射只在**罩身**用途中和
  （糊卡面＝读不到战况，正确性问题 P-77），其余用途保留可调。
  死层/污染层照旧摘（Projector/Decal/音源/WindZone/PerPlatformSettings）。
- `Verify` 体检改为查「档位缩放挂齐 + 死层清干净 + 碰撞关」；活跃粒子超参考
  预算只报警不判死。新增 `GreekMyth/特效/清洗 存量标准件（挂档位缩放…）`。
- 编辑器一致性：编辑器按 `VfxQuality.EditorTier`（默认 Mid）跑同一套系数，
  Play 看到的粒子密度/折射/灯与真机中端一致；分辨率层面差异结构性存在，已写明。
- 全量排查 63 件：20 件 playOnAwake 音源、6 件折射未挂档、5 件 World 粒子碰撞、
  6 件粒子远超参考预算（`cast_duel_launch` 18766、`hit_massive` 20008）。
- 新文档 `docs/client/vfx_mobile_budget.md`（精简版）；规则 `00-session-start` /
  `vfx-standardization.mdc` / `client-battle.mdc`；standardization §四.3-7b/§四.5；
  client index、extension_points、pitfalls P-79。

## 2026-07-28 新特效类：场域氛围件（雷霆神谕改全屏雷暴）
- Effect19 罩身部分在卡尺度上不可见（屏幕抓帧折射，P-74/P-78 后续），改判用途：
  新增 `VfxUsage.AmbientField` + `ambient_` key 前缀＝**不挂卡、钉主战场地面中心、
  按世界尺度铺满视野**的整场氛围；清洗规则与罩身相反（摘人形壳、留世界空间游离层）。
- 挂载：`UnitAuraService` 加 `ambient_` 分流，全场按 key 去重 + 持有者引用计数
  （多人有【雷霆】只一份雷暴，清零才撤）；几何全在 `StagePerformanceConfig.AmbientField*`
  （Scale/Lift/SortingOrder，层序压卡牌之下防糊立绘）。
- `shroud_thunder` → `ambient_thunder_storm`（`WireThunderStorm` + AutoHeal）；
  Registry / thunder_oracle Profile 同步。命中件 `hit_lightning` 不变。
- 文档：vfx_standardization §四.1/4.2/4.4、extension_points、guide、olympus、
  performance_mechanisms、vfx_config_index、vfx_playback_scheme、pitfalls P-78 后续。

## 2026-07-28 罩身清洗 P-78：中和折射勿毁节点（修雷霆全屏乱电）
- Effect19 层级 `Shield`(Distortion)→`ShieldAdd3`：旧流水线 Destroy 整节点带走罩面，
  只剩 LightningTrails →「全屏乱电、无绕身罩」。改为中和折射（保子层）+ 摘
  LightningTrails*（喷射层提根）。`shroud_thunder` / `shroud_ares_might` 重接线。
- 文档：vfx_standardization §一/§四.1；pitfalls P-78；guide 行更新。

## 2026-07-28 雷霆神谕改罩身 Effect19（shroud_thunder）
- 宙斯施加的【雷霆】状态挂身：由旧 `aura_thunder`（DR 乱劈）改为罩身
  `shroud_thunder`＝画廊 2/8·11/61 Magic Effect19；落盘 `VfxUsage.Shroud`
  （`WireThunderShroud` + AutoHeal）；Registry / thunder_oracle Profile 同步。
- 命中 `hit_lightning` 仍用同原料 Collision 子件，与罩身母件分工不混淆。
- 文档：guide / olympus / performance_mechanisms。

## 2026-07-27 特效纪律防旁路：P-77 根因收口 + 自动加载补强
- **为何踩坑**：不是「没读标准化」，而是权威文档把 `CopyFull`「完整件旁路」
  登记成罩身正确做法，与「只走 VfxPackStandardizer」并存——按文档做仍系统性
  绕过清洗（折射折糊卡面 + PerPlatformSettings 真机降配）。
- **纪律补强**：`vfx_standardization` 增 §〇.1 唯一落盘禁令、§一第 8 条、§八
  「自动加载充分性」；pitfalls P-77 扩写工作流层根因；`extension_points` 删
  CopyFull 行改为 `VfxUsage.Shroud`；guide/olympus/pack_integration/index 清
  「完整件」残留。
- **Cursor 自动加载**：`00-session-start` 表行显式含罩身+禁旁路；
  `vfx-standardization.mdc` 扩 glob 到 Units 光环/罩身 + 禁止 CopyFull 清单；
  `client-battle.mdc` 同步（顺带 CutInPolicy→CutInPlanner）。
- 踩坑录超 500 行：P-01～P-49 拆入两份 archive，现行文件保留 P-50 起。

## 2026-07-27 罩身收编流水线：修卡面折糊（P-77）
- 战神之勇罩身模糊根因：原「完整件原样拷贝」（WireShroudEffect.CopyFull）残留
  两层 `RFX1/Distortion` 屏幕折射，罩在卡前把卡面整块折糊；`PerPlatformSettings`
  残留还会真机偷降发射率（「与画廊不一样」的第二来源）。
- `VfxPackStandardizer` 新增 `VfxUsage.Shroud`：不做运载器改选、不挂
  VfxCircleFit（尺寸归 VfxShroudFitter），专属清洗摘折射层 + CollisionTrigger；
  `WireAresMightShroud` 改走流水线，CopyFull 删除。重接线后四项验证通过
  （成品仅 ShieldAdd/Fringe/Bottom/FireBack 四可见层 + 1 灯）。
- 文档：vfx_standardization §四.1/4.3/4.4 补 Shroud 用途；pitfalls 追记 P-77。

## 2026-07-27 播放编译重整（schema 1.5.0 + PlaybackCompiler）
- **服务端**：`Skill` 新增定义期标签 `damage_type`（register 强校验）/`tags`/
  `category`（推导 property），32 将 + 标定/测试战法全量补标；新增
  `battle/skill_catalog.py`，战报头出 `skill_catalog`（出场战法标签目录，
  含固定条目 basic_attack），bridge 配阵页复用同一真源。
  schema **1.5.0** / core **battle-0.4.2**，golden 11 份全量重生成，
  新增 `test_skill_catalog.py`（267 测全绿）。
- **客户端 asmdef 拆分**：`ClientBattle.Names`（纯表）与 `ClientBattle.Core`
  （Events+Processors+Compiler）独立编译单元，L1/L2 禁反向依赖由编译器强制
  （清掉 architecture §七第一条遗留债）。
- **播放编译**：新增 `Events/PlaybackCompiler`——开播前一次「管线+决策」编译为
  `CompiledPlayback`，主循环/高光读同一份产物；processor 链序唯一登记处从
  WorldBuilder 收编至 `BuildPipeline`。分类读 `skill_catalog`
  （追击 vs 主动直判，删 parent_seq 启发式；旧战报回落+告警）。
- **cut-in 判定下沉**：`VFX/CutInPolicy` 删除，改 `Events/CutInPlanner`
  编译期逐组注记（`EventGroup.CutIn`）；满档判据用**势能预演**（按组序重放
  momentum value，读落账前值，与运行期镜像逐组等价）；Director 只读注记，
  Session 删 PursuitCountInWindow。
- **排查入口**：Editor 菜单「GreekMyth→播放→导出 PlaybackScript」把战报编译为
  `.playback.json`（逐组 kind/key/事件/cut-in 注记），与运行期同源。
- StreamingAssets 示例战报全量刷成 1.5.0；manual_3v3 冒烟断言随站位改
  zhui 修正。文档：新增权威 `docs/client/playback_script.md`；architecture/
  playback_requirements（R-2.7）/playback_units/cutin_stage/
  performance_mechanisms/text_system/framework 同步；schema md+json 加 §2.2b。

## 2026-07-27 罩身定径修正（P-74）+ 弹道逐条解析
- 罩身「没罩住」根因：`VfxShroudFitter.Measure` 把 **Decal**（随后必被 Pin 钉死）
  与**纯折射层**算进定径。Effect18 的 Decal2 宽 8.97、Distortion 6.34 宽，
  把 k 压到 0.52 → 可见壳顶 1.49 < 卡上缘 2.16。
- 修：定径只取**可见壳粒子**（跳过 shader 含 Distortion，全折射时回落），
  Decal 一律排除；bodyTop 同步跳过折射层。实测 k=1.20、顶 3.45、横向 2.60
  ≈投影圆×1.21。新增坑 P-74。
- 弹道解析改**逐条伤害**：`ProjectileKeyOf(profile, damage)`——专配 >
  魔法 `magic_bolt`（画廊 1/8 件 54/62，无裂地）> 物理 `proj_bolt200`（带裂地）。
- 裂地同步逐 lane：`Active` 改为「本组有无物理」，`FlightPathCracks` 逐 lane
  判物理，混合组里魔法那一路整条跳过（原按 `damages[0]` 整组走）。
- 文档：vfx_config_index 新增 §一b 弹道解析；ground_crack_config 补逐条判定。

## 2026-07-27 战神之勇罩身改接画廊 2/8·10/61（Effect18）
- `shroud_ares_might` 原料由 Magic Effect31 改为 **Effect18**（画廊 Ordinal 10/61）；
  `WireAresMightShroud` 完整件重拷 + AutoHeal；挂载 key / OddRounds 不变。
- 勿与「件 18/61＝Effect25」混淆。assets_upload_guide / olympus /
  performance_mechanisms / vfx_playback_scheme 同步。

## 2026-07-27 轨迹裂地 T4 + 巨伤整组拉满
- 新增 T4 轨迹裂地：**拉满出手**（势能加强或巨伤）时出击者突进途中踩出档 3
  裂缝。入口 `GroundCrackService.MoveTrailDriver` / `MoveTrail`（判据与档位全在
  裂地服务，演出层只递 damages）；`StrikeBeats.Advance` 按**实际位移占比**
  逐帧驱动（突进 InQuint 加速，按时间等分会让裂缝跑到脚前面），抵近同帧 `Finish`。
- 起点取蓄力**之后**的站点，否则裂缝从空处开始。
- 新增 `VFXContext.MassiveStrike`（PlaybackDirector 一组一置一复位）：巨伤组
  裂地整段拉满＝轨迹 T4 + 弹道 T1 + 命中 T2 全档 3（命中另 ×1.5）。
  场心大裂地仍只跟势能加强（那是「势能全开」的专属语言）。
- 文档：ground_crack_config §一优先级表加轨迹列与巨伤整组说明；
  ground_crack_language 场景表加 T4 行。

## 2026-07-27 cut-in 统一取景：推镜→横幅→出手命中→撤镜
- 定论：一切 cut-in 与单挑同构，**整段独占播放单元**；单挑是唯一在横幅拍
  额外飞立绘的特例。新增 `VFX/CutInStage.cs`（借 `StageCameraRig`、finally 还位）。
- 判据前移：新增 `CutInPolicy.Resolve(group, pursuitCount)` 一处定满档/巨伤/
  追击第 5 次（优先级同序），播组**之前**判——客户端本就持有整组事件。
  `FindHighDamage` 排除 mitigation 非空的 0 伤（格挡/反弹不算重创）。
- 事后回调式作废：`PerformanceRunner.NotifyDamageSettled` 空实现，
  删 `HighDamageCutInDelay` 与挂起协程；P-72 由延迟缓解升级为结构性修复。
- 追击第 5 次横幅由非阻塞升为取景独占单元，与巨伤/满档同形。
- 运镜参数入 `StagePerformanceConfig`：`CutInCamera{PitchDeg=42,Distance=46,
  PushSeconds=0.3,HoldSeconds=0.08}`（比单挑 45/40 浅，留出突进/弹道/裂地取景）。
- 文档：新增权威 `docs/client/cutin_stage.md` 并登记 client/index；
  playback_requirements 加 R-5.2b、改 R-5.2；performance_mechanisms §一b 改写。

## 2026-07-27 巨伤震屏加强 + 档3×1.5 命中裂地
- 震屏：`CameraShaker.MaxOffset` 0.3→0.75（远机位原封顶等于没震，P-73）；
  巨伤 Shake 0.55/0.48s。
- 裂地：`PlayHit(..., massive)` 与重创同判据 → 强制档 3 + 面积 ×1.5
  （同势能加强规格，不叠场心大裂地）。ground_crack_config / vfx_config_index 同步。

## 2026-07-27 重创 cut-in 延后，露出 hit_massive（P-72）
- 根因：manual 0722 r3 怒火突刺 3048 已正确解析 `hit_massive`，但
  `NotifyDamageSettled` 同帧起播 solo 暗幕（sorting 80）盖住卡面特效（≥45）。
- 修：`HighDamageCutInDelay=0.45`×DurationMul 后再请求重创 cut-in；
  HardStop 清挂起协程。vfx_config_index / pitfalls P-72 同步。

## 2026-07-27 命中解析四级：巨伤/追击/神谕默认
- `ResolveHitKey` 改四级：①巨伤（>3000＝重创横幅同判据）→ `hit_massive`
  （RFX4 Effect15_Collision，画廊 3/8 件 7 的碰撞子件）覆盖一切专配，
  `SettleDamage` 同帧强制震屏 0.34/0.3s；②专配/组默认；③伤害类型；④兜底。
- 追击 `PursuitDefault.HitKey` 置空＝受击同步主动逻辑；神谕伤害默认
  `OracleDefault.HitKey=hit_wave`（画廊 1/8 件 47 定件）。
- `WireDefaultHitVfx` 增 hit_massive 标准化+AutoHeal；vfx_config_index /
  performance_mechanisms / assets_upload_guide 同步（序号漂移改以 key 为准）。

## 2026-07-27 特效配置总索引；澄清普攻命中
- 新建 `docs/client/vfx_config_index.md`：命中解析顺序、默认表、查配置入口；
  `client/index` + `performance_mechanisms` 挂链。
- 明确：普攻卡面受击＝`hit_generic`（Vefects Hit_05；`MeleeDefault`），
  与主动默认 `hit_sword`/`hit_petrify` 分流。

## 2026-07-27 默认命中改回画廊 [1/8] 我方标准件（P-71）
- 魔法默认＝件 **41/61** `hit_petrify`；物理默认＝件 **45/61** `hit_sword`
  （`ResolveHitKey`）。此前误用「分母61→Magic」接到 Effect19/22。
- `_gallery_index_dump.py` 补齐包 1 Resources Ordinal；废止分母纠偏启发式；
  `WireDefaultHitVfx` 改为存在性体检，不再厂包覆盖。

## 2026-07-27 命中件回收窗口按 EmitWindow 给足（修「不如画廊」）
- 排查：`hit_lightning` 一次性发射窗口实测 **2.0s**、`hit_clash` 1.0s，
  而 `SettleDamage` 写死 `ctx.Scaled(0.5f)` 就收势——魔法件后 3/4 层
  没发射完就被掐，观感远逊画廊预览。
- 修：命中回收时长＝`max(Scaled(0.5), EmitWindow(key, 2.5))`（真实秒），
  不阻塞时间轴，只让实例活到自然放完；`performance_mechanisms.md` 同步。

## 2026-07-27 魔法命中→Effect19；单挑出阵不藏立绘；特效规则加固
- 魔法默认命中改画廊 Magic **2/8** 件 **41/61**＝`Effect19_Collision`→`hit_lightning`；
  物理仍件 45＝`Effect22_Collision`→`hit_clash`（`WireDefaultHitVfx`）。
- **P-69**：单挑 `Fighter.Make` 不再提前藏真立绘；替身先熄灭，出框瞬间
  `ConcealCardsForFlyOut` 再切换——修出阵卡面特效阶段「立绘没了」。
- **P-70** + `.cursor/rules/vfx-standardization.mdc`：点名接件强制先读
  `vfx_standardization.md`，禁止裸改 key / 手搓；`00-session-start` 表行加严。

## 2026-07-27 默认命中：物理 Effect22_Collision / 魔法 Effect30_Collision
- 画廊 Magic Pack **2/8**（人说 1/8，按分母 **61** 纠偏）：件 45→`hit_clash`，
  件 24→`hit_lightning`。走 `VfxPackStandardizer` + `WireDefaultHitVfx`（AutoHeal）。
- **Effect30 特例**：母件无 TransformMotion（ShieldCollisionTrigger 出子件），
  流水线自动改选抓不到 → 清单直接点 `Effect30_Collision`。Effect22_Collision 直接用。
- ResolveHitKey 仍按 damage_type；Profile.HitKey 优先。assets / playback / mechanisms 同步。

## 2026-07-27 单挑特效：恢复原胜负 + 开场/败者卡面追加
- **撤销**「胜负都改卡面」：恢复 `ground_duel_defeat` 定位圆地面 + 裂地；
  胜者仍 `aura_duel_victory` 卡面加冕。
- **出阵追加**：地上 Effect28 不变，双方卡面再挂画廊 1/8 件 8/60
  （`DuelLaunchCardVfxKey`＝`aura_duel_victory`）。
- **败者卡面追加**：画廊 1/8 件 32/60 观感 → `aura_duel_defeat`（同 Effect8
  原料的 Anchor 件，避免 GroundLayer 压到卡下）；与地面溃败同时播。
- `WireDuelStageVfx` 四件清单；duel.md / assets_upload 同步。

## 2026-07-27 单挑胜负特效改为双方卡面（画廊 1/8 件 8+32）
- （已撤销，见上条）曾把败者改为纯卡面并改名 `aura_duel_defeat`。

## 2026-07-27 单挑推镜距离 28→38（保全阵在框）
- `DuelCameraDistance` 28→**38**（55→38 ≈ 1.45×）。28（1.96×）把全阵容
  卡面裁出画面；推镜已是独立一拍+定格，1.45× 仍可读，硬约束是六张牌在框内。
- `duel.md` 运镜段同步。

## 2026-07-27 单挑拍序：回框→撤镜→胜负特效
- 全序改为：出阵地面特效 → 推镜 → 飞入 cut-in → 回框 → **撤镜还位** →
  **胜负地面/卡面特效**。旧版胜负特效夹在回框与撤镜之间，近景里加冕/溃败读不清。
- `DuelStage.Run` 对调 `PullOut` / `FireResultVfx`；`duel.md` 分幕表与运镜段同步。

## 2026-07-27 击退钉线 + 抖动改沿线前后颤 + "击打圆"更名"微调圆"
- **击退不沿线的元凶**：旧版推开点在受击线上、回弹却奔 `RerollRestPosition`
  的圆盘随机点去——第二段位移斜出受击线。现落定点＝Home 沿线随机距离、
  推开点＝同线过冲（`KnockOvershoot`=1.25），**两段全钉在受击线上**，
  越微调圆即截断；落定点即新定位点（`RestPosition`）。
- **旋转式抖动废除**（当日两次调参仍读不出：面内自旋不改轮廓、俯仰被投影吃）。
  改为**击退落定后**（`seq.OnComplete`）围绕落点**沿同线前后颤**的纯动画：
  10 Hz（安卓 30fps 下限 ≈3 帧/周期）、振幅＝微调圆半径×0.22/0.13、
  幂衰减 1.1、结束回落点；任何 tween 接管 transform 即让位。
  绕身在场＝禁一切卡根位移（击退+颤动，P-58），只留挤压+红闪。
- "击打圆"更名**微调圆**：`TuneCircleRadius`/`ClampToTuneCircle`/
  `TuneCircleScale`（旧 `HitCircleScale`）。performance_mechanisms /
  battlefield_layout / extension_points / client_battle_framework 同步。

## 2026-07-27 受击抖动按安卓 30fps 重定 + 改可读轴
- **帧率事实**：独立版 vSync 锁屏刷（多为 60），但中端安卓战斗负载常掉到
  **30~45 fps**。频率必须按 30 下限定，不能按编辑器满帧 60。
- 频率 18 → **10 Hz**（30fps ≈ 3 帧/周期；旧 18 Hz @30fps 只剩 1.7 帧＝噪点）。
- **轴权重才是"只见击退不见抖"的主因**：近正面卡面内自旋（Z）几乎不改轮廓；
  改成 Pitch/Yaw/Roll = **0.90/0.60/0.30**（俯仰/偏航改透视）。
- 峰值角 12°/8°，时长 0.36/0.28（略长于击退回弹），衰减 1.1。
  `performance_mechanisms.md` 同步。

## 2026-07-27 受击抖动重标（"几乎看不见"）
- 峰值角 3.4/1.9° → **9/5.5°**（暴击/普通）。卡牌后倾 45°，绕自身 Z 轴滚 2°
  投影到屏上不足 1.5°，旧值在近 3D 俯视下等于没抖。
- 频率 24 → **18 Hz**。**这条上限由帧率定、不由手感定**：60 fps 下 24 Hz ＝
  2.5 帧一个来回，已逼近采样极限，摆动被采成随机噪点——振幅调多大都读不出
  「震」。18 Hz ＝ 3.3 帧一周期。
- 衰减由硬编码平方改为可配 `HitShakeDecayPower`=1.4：平方使平均可见振幅只剩
  峰值三成（起手一帧最猛、之后塌掉）；1.4 保留起手形状但中段仍看得见在摆。
- 时长 0.26/0.18 → 0.30/0.22 s。三通道分工不变（击退＝位移、抖动＝纯角度、
  挤压＝立绘形变）。`performance_mechanisms.md` 受击表现行补定标依据。

## 2026-07-27 厂包标准化收口为统一流水线（单挑三件连环事故复盘与重构）
- **根因三连**（P-68）：①胜负两件点名的 Effect23/Effect8 是**投射物运载器**
  （粒子按移动距离发射，定点＝零粒子；画廊里的爆炸是其碰撞子件），此前
  "钉死原地"删位移驱动等于拔掉发射器→完全不演出；②上次接线在 **Play 模式**
  下跑，`RFX*_PerPlatformSettings.Awake` 的降配（发射率 ×0.75）被烤进
  `cast_duel_launch` 成品→缩水；③删 Light/Audio 留下同节点曲线脚本→运行期
  Awake 抛异常经 Instantiate 传出，**整段演出协程死掉**→"所有特效全消失"。
- **新增 `VfxPackStandardizer`（唯一落盘入口，pack 无关，兼容 RFX1/RFX4）**：
  拒绝 Play 模式；定点用途自动改选碰撞子件（`ResolveAnchorSource` 沿
  `EffectsOnCollision`/`EffectOnCollision` 字段）；`CopyAsset`+`LoadPrefabContents`
  纯资产编辑；按类型名**配对**裁剪驱动脚本；摘 WindZone/CameraShake/
  PerPlatformSettings；落盘后四项静态验证（missing/可见性/驱动配对/可实例化）。
  `WireDuelStageVfx` 瘦身为三行清单；`StandardizeLavaBurst` 改走流水线
  （旧版搬的全是运载器层，实际零粒子，全量体检抓出）。
- **运行期加固**：`VFXManager.Build/Prewarm` 捕获实例化异常，坏件降级占位
  **不打断演出协程**（客户端"任何情况必能播出"的兜底层）。
- 新体检菜单「体检 标准件流水线四项」（报告落 `Temp/vfx_audit.txt`）：
  60/60 通过。三件重接：`aura_duel_victory`←Effect23_Explosion、
  `ground_duel_defeat`←Effect8_Collision、`cast_duel_launch` 发射率复原。
- 文档：`vfx_standardization.md` §四.1/§四.3/§四.5/§六/§七 按流水线重写；
  `duel.md` 来源表/逐层去向/EmitWindow 形态更新；`assets_upload_guide.md`
  三行更新；pitfalls 新增 **P-68**。

## 2026-07-27 单挑重排节拍：运镜独立成拍 + 全程无空等 + 特效钉死原地
- **顺序改为** 出阵爆发 → 推镜 → 出框 → 交错 → 回框 → 胜负特效 → 撤镜。
  运镜不再与出框并拍：并拍时注意力全在飞出去的人身上，镜头等于白推。
  撤镜移到最后，此时胜负余烬还在烧，屏上不空。
- **推镜给足量**：距离 34→**28**（常规 55，即卡面放大 **1.96 倍**；旧值仅 1.6 倍
  且被并拍淹没，实测读不出镜头动过），到位后**定格 0.3 s**
  （`DuelCameraHoldSeconds`）——运动结束时的静止才让人确认"到位了"。
  新增 `DuelCameraPushSeconds`=0.42。
- **消除空等**：出阵那 1.5 s 原是 `WaitForSeconds` 干等，脚下在炸而两张立绘
  纹丝不动，整段被读成背景动画。改为立绘**持续下沉 + 11 Hz 憋力发抖**
  （`DuelCoilTrembleHz/Amp`）。确立**零死帧的时间版**：每一拍都必须有
  **主体**在动，不能只靠 Chrome 自走撑场。duel.md 补逐拍"谁在动"表。
- **`StripMotionDrivers`（新）**：三件里两件带厂包位移驱动（胜者
  `RFX4_PhysicsMotion`、败者 `RFX1_TransformMotion`+`RFX1_Target`）——厂包主件
  设计上是**投射物**，起播后飞出去再炸，当定点特效用就是"粒子乱跑到别处才爆炸"；
  且粒子全是 `simulationSpace=World`，transform 一动还拖尾。接线时摘位移并把
  节点归零，顺带摘 `WindZone`（场景级力场会吹歪**别的**特效，已确认自身粒子
  未开 External Forces）。实测摘除：败者 3 个、胜者 1 个。
- 文档：`duel.md` 运镜章重写 + 新增「全程无空等」逐拍表 + 钉死原地条目；
  `vfx_standardization.md` 落盘第 8 步 + 验收两项 + 坑谱两条。

## 2026-07-27 单挑出阵特效时机校正（"没跑完就飞出去"）
根因是三个叠加问题，全部照实测素材而非拍脑袋修：
- **空转前摇**：`Effect28` 唯一的一次性爆发层 `startDelay=1.00 s`（厂包按 demo
  场景排的节奏），即前一整秒屏上什么都没有，我们只等 1.2 s ⇒ 正好在爆发炸开的
  瞬间起飞。接线新增 `NormalizeStartDelay`：所有层**同时前移**到最早的可出图层
  从 0 起播（同时前移而非各自归零，层间先后是这件的表演结构）。实测前移 -1.00 s。
- **`EmitWindow` 误算循环层**：`main.duration` 对 `loop=true` 层是**循环周期**，
  当结束时刻用得到两不像的数（该件因此报 4.0 s，真实爆发仅 1.5 s）。改为
  一次性层取 `delay+duration`；全循环层（胜负两件）取 `delay+startLifetime`
  ＝成形时长，比退到通用保底 0.45 s 贴合得多。
- **切拍不收势**：出阵件有 5 个循环层不会自停，等待结束后仍全速发射。新增
  `VFXManager.StopEmitting`，交拍时只掐新粒子留余烬 ⇒ "在余烬中被拽走"。
  **顺序感靠收势，不是把等待拉长。**
- `DuelVfxWaitCap` 1.2→**1.7 s**（须高于实测窗口 1.5 s，否则又被上限截断）。
  实测终值：出阵 1.50 / 加冕 1.70 / 溃败 1.70 s，三件均未截断。
- 文档：`duel.md` 顺序播规则重写、`vfx_standardization.md` 落盘步骤加"掐前摇"
  与"交拍收势"、坑谱速查加两条。

## 2026-07-27 标准化协议全面修订（把三天踩的坑固化成流程）
- `docs/client/vfx_standardization.md` 重排为七节：**§〇 第一原则**（先证明资产
  在盘上再谈观感 + 接线脚本必须带 AutoHeal 自愈）、§一 画廊≠运行期七条差异表、
  §四 落盘八步按单挑三件实战校正（解嵌套→清失效脚本槽→摘死层并记录替代→
  尺寸组件三选一→池化判定 VfxFreshInstance→移动端裁剪→StandardizeAll→AutoHeal）、
  **§六 坑谱速查**（9 条「症状→先查什么」映射 P-33/38/65/66/67）。
- 验收改为「用 unityMCP 逐项验**成品**而不是看接线 log」，新增两条硬项：
  成品 missing script=0（组件可能在保存环节丢）、标记组件实际在成品上。
- 运行期约束新增：会序列化进 prefab 的 MonoBehaviour 必须独立成文件（P-67）。


## 2026-07-27 单挑三件实际落盘（此前从未接线）+ 接线流程自愈化
- **「完全没效果」的真相**：三件标准件从未落盘——接线脚本写好了但菜单没人点过，
  运行期一直在播占位小方块。经 unityMCP 执行菜单落盘 3/3 并逐项验证
  （missing=0 / RFX 驱动脚本 16/4/8 保留 / 灯 1 盏 / 音源 0 / CircleFit+Fresh+Ground 全在）。
- **`WireDuelStageVfx.AutoHeal`（新）**：`[InitializeOnLoadMethod]` 检测三件缺失
  即自动接线。凡"代码引用了必须由编辑器脚本生成的资产"，生成一步不允许依赖人手。
- **`VfxFreshInstance` 挪独立文件**：原先塞在 VFXManager.cs 里，Unity 只按
  「类名＝文件名」解析 prefab 里的 MonoBehaviour，存盘即变 missing script——
  绕池标记静默丢失、整套不池化逻辑失效（见 pitfalls P-67）。
- 接线脚本补「清失效脚本空槽」（GameObjectUtility，GetComponentsInChildren
  拿不到 missing 槽）。
- 实测三件发射窗口 4~5 s（持续发射型），`DuelVfxWaitCap`=1.2 s 生效：
  顺序播三拍共增加约 2.4 s，可控。

## 2026-07-27 补平「画廊预览 vs 运行期」的四条工程债
逐行对齐两条链路，固化七条差异表于 `vfx_standardization.md` §〇。修平前四条：
- **`VfxFreshInstance`（新）+ `VFXManager` 绕池**：厂包件的观感很大一部分由自带
  `RFX*` 驱动脚本在 `Awake/Start` 初始化后逐帧驱动，而**池化复用不重跑
  `Awake/Start`**；又因 `Prewarm` 开局入池，战斗里**第一次播就已是复用态**，
  那套初始化整局只在离屏预热区跑过一次 → 脚本驱动的层全是残留状态。
  症状「能看见但就是不如预览」，最易被误判为素材不行。带驱动脚本的件改为
  每次 Instantiate、播完 Destroy（接线脚本按 `RFX*` 前缀自动判定，白名单制）。
- **回收不再硬切**：`RecycleAfter` 到点先 `Stop(StopEmitting)`，再等余烬自然
  消亡（上限 1.2 s）后入池。原先直接 `SetActive(false)`，屏上正飘的火星一帧消失。
- **`RestartParticles` 只在"最上层"粒子系统起播**：`Play(true)` 本就级联到子孙，
  逐层再 Play 会重复触发子发射器、打乱相位（画廊只在根级播，故两边不一样）。
  `Clear` 仍逐层（幂等且必须清深层残留）。`VfxCircleFit` 量测后重启同规则。
- **埋地救援搬进 `VfxCircleFit.RescueIfBuried`**：与定径**共用同一次 Simulate**，
  零额外开销；抬升在 `LateUpdate` 施加——`PlayAt` 是先激活后写 position，
  在 `OnEnable` 里改位置会被静默抹掉。接线脚本给地面件自动打开。
- 承认且不修的预算差：画廊审核惯用 **0.25× 慢放**（厂包出手件整段仅 0.9 s），
  1× 下不可能等同；RFX Decal 层 URP 画不出（P-33）不可逆。
- 文档：`vfx_standardization.md` §〇 七条差异表 + 验收清单扩项、pitfalls P-66 续。

## 2026-07-27 单挑三件改顺序播 + 定径复刻画廊观感 + 移动端裁剪
- **顺序播（原为重叠）**：出阵件在两人**定位圆**放完**才起飞**；立绘**落回卡框后**
  才起胜负两件，**放完单挑才结束**。重叠播读作"两件不相干的事同时发生"而非因果。
- **等待时长不写死**：新增 `VFXManager.EmitWindow(key, cap)`，运行期从 prefab 探
  **发射窗口**（各 `main.duration` 最大值，**不含 startLifetime**）——厂包件多是
  「0.4 s 爆发 + 3 s 烟尾」，等烟尾散完观众看到的是发呆。回收另加
  `DuelVfxTailSeconds` 让余烬飘完。等的是**真实秒**（粒子不吃 `ctx.Scaled`，
  乘倍速＝把特效拦腰截断），故 `DuelVfxWaitCap`=1.2 s 兜底；探不到时退
  `DuelVfxFallbackSeconds`=0.45 s 保底节拍，**不因缺素材丢节奏**。
- **`VFX/VfxCircleFit.cs`（新）**：修"画廊里挺好、接进去糊满全屏"。`VfxFitter`
  只做"随卡宽等比浮动"，**不改厂包原生尺寸**；画廊观感是另按了一次定径。本组件把
  画廊那一步搬到运行期（`Simulate(0.12s)` 量起手核心 → 缩到**投影圆**直径），
  按 prefab 名 **+ 圆直径**缓存。与 `VfxFitter` **互斥**，`VfxStandardizer` 见到即跳过。
- **移动端裁剪**（`WireDuelStageVfx.TrimForMobile`）：每件实时灯留 1 盏且关阴影
  （`Effect28` 原有 5 盏，两人同播＝10 盏，前向渲染逐光一 pass）、删 `AudioSource`
  （绕过 SFX 总线且与 `sfx_duel_*` 撞车）。粒子层不动，主体观感与画廊一致。
- 回框落定后 `Fighter.Hide()` **先于** `Restore()`：替身与真立绘此刻完全重合，
  顺序反了有一帧重影。
- 文档：`duel.md`（顺序播规则 + 为何画廊接不进去）、`vfx_standardization.md`
  （新增定径与移动端裁剪两条交付项+验收）、pitfalls **P-66**。

## 2026-07-27 定位圆/投影圆拆名 + 单挑推镜与四个情感爆点 + 三件厂包接线
- **术语拆名（破坏性重命名）**：`ArenaSlotLayout.CardCircle*` → `ProjectionCircle*`
  （**投影圆**＝整卡竖直投影外接圆，心在卡心正下方，罩身件用），新增
  `AnchorCircle*`（**定位圆**＝下边缘端点绕下边缘中点转一周，心＝接地点、
  **直径＝卡宽**，地面痕迹/裂地/法阵用）。两圆约差 1.4 倍且**不同心**。
  改 `VfxShroudFitter/Follower`、`VfxGalleryRunner`（画廊同屏画**青=投影/黄=定位**
  两环）。名实不符是历史混用根源，见 P-65。
- **`VFX/StageCameraRig.cs`（新）**：演出性运镜。单挑出框时把俯角 35→**45**
  （＝`CardPitchDeg`，光轴**垂直卡面**）、距离 55→**34**，回框还位。
  **只动俯角与距离不动 FOV**（FOV 是安全区反算的取景基准）。接管期间它是相机
  位姿唯一写方，`CameraShaker` 切「只算不写」（`Suspended` + `CurrentOffset`）
  由 rig 叠加——两个 `LateUpdate` 顺序不定，否则"抖一下不抖一下"。
  归还三条路径：`finally` / `CutInService.CancelAll` / `PerformanceRunner.HardStop`。
- **cut-in 挂点改为相机子物体**（`CutInService.NewRoot`），否则一运镜整块屏
  滑出视野；`ScreenRect` 退化为只返回半宽半高。飞行立绘的"卡上那一端"随之改为
  **每帧重算**（`Fighter.SyncCardPose`）——挂点在动、卡不动，缓存会让回框落偏。
- **单挑四个情感爆点**：⓪蓄（立绘先往卡里陷，`Pose` 走负值＝预备动作）→
  ★1 放（定位圆炸开+震屏+白闪，OutBack 过冲出框）→ ★2 末轮前静滞（后撤+图标
  收紧，运动量骤降制造预期）→ ★3 定胜负 → ★4 回框落进自己的特效里。
  **暗幕延迟压下 / 提前散**（`DuelVeilDelay`）：出阵与胜负特效炸在世界里，
  不留这段窗口会被 sorting 80 的暗幕整个盖住，等于白播。
- **三件厂包特效接线**（画廊点名 → 标准化，协议 §三全走完）：
  RFX4 `Effect28`→`cast_duel_launch`（两人定位圆·出阵）、
  RFX4 `Effect23`→`aura_duel_victory`（胜者卡面·加冕）、
  Magic v1 `Effect8`→`ground_duel_defeat`（败者定位圆地面·溃败，挂
  `VfxGroundLayer`）。落盘脚本 `Assets/Editor/GreekMyth/WireDuelStageVfx.cs`
  （菜单 `GreekMyth/特效/接线 单挑三件`，可重跑）。逐层判定：`Effect23` 无贴花层
  整件可迁移；另两件的 RFX1/RFX4 UberDecal 层 URP 画不出被摘（P-33），
  `Effect8` 丢的正是地面焦痕 → 既定替代品自研裂地 `GroundCrackService.PlayHit`
  （落点同为 `GroundFoot`），已一并触发。key 在 `StagePerformanceConfig.Duel*VfxKey`
  （这三件与"谁参战"无关，无 Profile 行可查）。
- 新增 `battle/tools/_gallery_index_dump.py`：把画廊「包 i/N 件 j/M」离线复算成
  prefab 路径（照抄 `VfxGalleryLauncher` 的排序/过滤规则，件数自检 61/54）。
  **点名厂包件不要靠肉眼数序号**——组内做过碎件后置与 Ordinal 排序。
  另 `_prefab_layer_dump.py` 离线看层构成（供标准化 §3.1 定件）。
- 文档：`arena_stage` §四c 重写为两圆对照表 + 新增 §四d 运镜；`duel.md` §5b
  补分幕爆点表/运镜三约束/三件去向表/暗幕延迟原因；`portrait_cutin_assets.md`
  §5b 提示词**全改中文** + 新增 §5d **cut-in 屏底图 AI 生成规格**（2048×1024、
  左右留人位、整体压暗、正反向中文提示词）；`assets_upload_guide` 登记三 key；
  `extension_points` 加「选哪个圆」「怎么推镜」两行；`ground_crack_language`
  改指 `AnchorCircle*`；`rendering_layout` 更新挂点与俯角说明。P-65 入坑录。
- **待人工执行**：Unity MCP 桥当前不可用，三件标准件尚未落盘——
  进编辑器点一次 `GreekMyth/特效/接线 单挑三件` 即可（脚本幂等）。

## 2026-07-27 单挑展示屏华饰层 + flipbook AI 生成流程入文
- 新增 `VFX/DuelStageChrome.cs`（MonoBehaviour，**自走 Update**）：影院黑边、
  左右阵营辉光、放射光芒慢转（左右反向）、浮尘余烬（立绘前后各一半）、
  四角纹饰、屏边框呼吸、整屏极缓推进、中央冲击环+白闪。四种周期**互质**——
  这是治「呆板」的药方：静态底+静态立绘=贴纸，人眼判活靠多速率运动叠加。
  贴图全程序化合成（纯色/渐变/环/软点），零预制资源；同名真图自动顶替
  （`UI/duel_screen_bg` `duel_rays` `duel_corner` `duel_icon`，全部可选）。
- `DuelStage` 收缩为纯编排；飞行立绘加**背光**（同图放大染阵营色＝无 shader
  描边发光）与**错相位待机呼吸**，挂 Chrome 的 `OnTick` 共用自走时钟 →
  插值/等待/放帧期间屏上恒有运动（R-4.1 零死帧）。
- **重排 cut-in sorting 80~93**（原新增装饰与立绘撞号会随机闪）：
  80 暗幕/81 屏边框/82 屏底/83 辉光/84 放射/85 纹饰/86 后浮尘/87 背光/
  88 立绘/89 前浮尘/90 图标/91 冲击环/92 白闪/93 黑边。屏边框必须低于屏底。
- `portrait_cutin_assets.md` 补 §5b/§5c：flipbook 的**唯一正确做法是 i2v 抽帧**
  （逐张生成必身份漂移），提示词四条必写（锁镜头/主体不出画/纯绿背景/
  写一次完整动作）、ffmpeg chromakey+抽帧命令、绿边与断号三坑。

## 2026-07-27 单挑舞台 cut-in 重做（立绘出框 + 虚空展示屏 + flipbook）
- 新增 `VFX/DuelStage.cs`：立绘从卡框**出框**飞入中央虚空展示屏 → 交错+动作
  ×`clash_cutins` → 定胜负 → 飞回卡框。取代旧「两张半屏卡掠过中央裂缝」。
  出框期间卡面立绘藏起（`UnitView.SetPortraitHidden`），正常收尾与
  `CutInService.CancelAll` 两条路径都还原。`DuelPerformance` 起传 `winner_id`。
- 动作素材＝**flipbook** `Resources/ClientBattle/DuelAction/{id}_{strike|react}_{NN}`
  （连号，断号即停）。缺帧退静态立绘占满时长，故资源可逐个补。选逐帧不选
  VideoPlayer：两人同屏双路解码有风险，且 flipbook 天然吃 `ctx.Scaled` 倍速。
- **修 P-64**：`CutInService.ScreenRect` 原按 `(cam.x, cam.y, 0)` 无旋转摆放，
  相机俯角 35° 后整个 cut-in 离光轴 35°（FOV≈12°）飞出视锥。改为挂在相机
  正前方 12 单位、随相机旋转的平面上；单人 cut-in 同时受益。
- 参数入 `StagePerformanceConfig.Duel*` 段（时长/几何全部可调，几何写成半宽
  半高倍数）。`docs/mechanics/duel.md` 扩为**单挑前后端总索引**（§5b 演出分幕
  ＋§7 服务端/契约/客户端/素材四张索引表），mechanics/client 双 index 登记；
  `portrait_cutin_assets.md` 由「cut-in 视频」改写为 flipbook 规格。

## 2026-07-27 立绘与 cut-in 视频制作规格书
- 新增 `docs/client/portrait_cutin_assets.md`：定方案＝卡面浮动立绘（静态 PNG）
  ＋ 全屏 cut-in 播短视频；**卡内攻击视频不做**（0.54s 窗口最拥挤、视频不吃
  `ctx.Scaled` 会脱拍，论证入文 §八，后续想动卡内用序列帧）。
- 规格含：cut-in 构图安全区（按 `CutInService` 实测换算——单人立绘槽 55%×75%
  屏、主体偏右、左下留标题；决斗槽 45%×80% 屏、下部 15% 被名字压），
  时长 ≥2s 且**必须无缝循环**（窗口随 DurationMul 变）、首帧须等于静态立绘
  （故须图生视频）、须带 alpha、单片 ≤1.5MB。
- 回退契约：`CutIn/<id>` 无 → 回退静态立绘 → 回退色块，两类资源可分批上。
  代码侧待办（VideoPlayer 接入/alpha 方案/Prepare 预热/HardStop 释放）列入 §七。
- index.md 与 assets_upload_guide.md §3 登记交叉引用。

## 2026-07-27 俯角入 StagePerformanceConfig=35°；院区×1.5
- 相机俯角数值迁入 `StagePerformanceConfig.PilotPitchDeg`（现行 **35**）；
  `CameraFitter.PilotPitchDeg` 改为只转发。卡后倾仍 45°。
- `BattlefieldLayoutConfig.CourtyardDepthFraction`：0.2 → **0.3**（院区扩大 1.5 倍）。
- 文档同步 arena_stage / battlefield_layout / rendering_layout /
  vfx_playback_scheme / performance_mechanisms / extension_points。

## 2026-07-27 相机俯角 → 30°（与卡后倾解耦）
- `CameraFitter.PilotPitchDeg`：**30**（当日曾试 60，再改回更近平视的 30），
  不再 `= CardPitchDeg`。卡后倾仍 45°（几何/影子/定位圆不变）。
- 文档同步：arena_stage §一/§三/§四b、rendering_layout、vfx_playback_scheme。

## 2026-07-27 击打圆约束 + 受击纯角度抖动 + 卡姿随机 + 演出参数收口
- 新增 `Units/StagePerformanceConfig.cs`：舞台演出参数**唯一收口**（卡姿抖动/
  击打圆/击退/受击抖动/三拍/残影/接地阴影），各表现类的调参 const 全部迁入。
- **击打圆**：受击击退与出击后的落点都截断在站位微抖圆内（`HitCircleScale`）。
  裁剪统一走 `OffsetFromHome`/`AnchorAtOffset`/`ClampToActionCircle` 地面二维三件套。
- 受击加**纯角度抖动**（`TickHitShake`，高频阻尼摆，零位移）：位移归击退、
  顿挫归抖动、肉感归立绘挤压，三通道互不代偿。绕身在场时只禁击退。
- 卡牌后倾角每卡在**基准 ± `CardPitchJitterDeg`(5°)** 内随机＝ **40°~50°**
  （只抖视觉，几何仍按基准角）；出击收势落点沿行动方向前移
  （`RerollRestPositionToward`）。
- 击退距离改为**沿受击线的随机距离**（`KnockbackXxxMin/Max`，单位＝击打圆半径
  倍数）：后退点以 Home 为起点落在受击线上，不从当前位置累加（累加会让连击
  把卡牌斜着漂出受击线）。抖动与击退完全独立，各自触发。
- 勘误：基准后倾角**是 45° 不是 30°**（07-25 changelog 标题与
  vfx_playback_scheme 残留 30，已订正；`arena_stage.md` 与代码一直是 45）。

## 2026-07-27 动作感一期：出手三拍 / 定向击退 / 突进残影 / 接地阴影
- 新增 `VFX/StrikeBeats.cs`：出手三拍（预备后仰 → InQuint 加速突进 → OutBack
  收势）唯一实现，`PlayMelee` 与 `PlayAoeCenter` 改为调用它，不再各自拼 `DOMove`。
- 受击 `DOShakePosition` 全向随机抖动**废除**，改为定向击退：方向取
  「攻击方站位中心 → 本卡站位中心」连线（`HomePosition`，非实时 transform——
  突进后攻击方就贴在身边，实时位置算出的方向会乱跳）；推开后 OutBack 弹回。
- 新增 `Units/AfterImageService.cs`（残影，order −2，自带环形池，`HardStop` 收）
  与 `Units/CardGroundShadow.cs`（接地阴影，order −3，仅近 3D 舞台，随卡尺自适应）。
- `SettleDamage` 改传 `fromHome`；文档同步 performance_mechanisms /
  rendering_layout（层级表补 −2/−3）/ client_battle_framework / extension_points。

## 2026-07-27 卡面生动性一期：呼吸/惯性视差/受击挤压/命中顿帧
- 新增 `Units/CardIdleMotion.cs`：立绘三通道合成器（唯一写入者，零 alloc）。
  呼吸改为浮动+侧摆+胸腔缩放+微倾三频叠加、每卡相位与频率失谐；残血更慢更重。
- `HitReact(isCrit, fromWorld)`：卡根位移与立绘挤压拆成两条通道——
  绕身罩在场时禁位移但挤压照给（原来只红闪，命中没有肉感）。
- 立绘加景深 `PortraitDepth`；踩坑记 P-63（多方 tween 抢同一 Transform）。
- 命中顿帧（`HitStopService`/`VFXContext.HitStop`）当次实现后**人工否决并删除**，
  文档留档禁止再引入；勘误：上一条曾误判 `antique_frame.png` 缺失，实际存在。
- `project_overview.md` §一 增「产品定位：三支柱」（动作游戏感／策略深度／
  下一代卡牌感），定论当前短板为动作感与下一代感；`.cursorrules` 同步注入。

## 2026-07-27 出手同步器 StrikeSync：飞行段 → 抵达 → 命中拍
- 新增 `VFX/StrikeSync.cs` + `IFlightDriven`：一次出手的时间轴唯一真源，
  逐帧广播弹道**真实位置**换算的进度，`Run()` 返回＝抵达；模板不再自拼时序。
- `GroundCrackService.PlayPath`（协程）→ `PathDriver`（`IFlightDriven`）：
  第 i 段在进度区间 [(i-1)/3, i/3] 内起裂并**推满生长**，末段推满＝弹道抵达。
- `GroundCrackDecal` 加驱动式生长（`EnableFlightDriven`/`DriveGrowth`）：
  弹道贴花不再自走时钟，只接管裂缝张开轴，熔岩与淡出仍各自现摇。
- 命中拍（命中特效+受击抖动+命中裂地）与抵达同帧；踩坑记 P-62。

## 2026-07-26 绕身显隐通用化：Presence + IsPresent
- 新增 `VfxShroudPresence`（出现/渐隐唯一实现）；删 `AresMightShroudPulse`。
- 注册表 `ShroudVisibility`（Always/OddRounds/EvenRounds/Manual）+ `SetShroudVisible`。
- `HasShroud` 改为看 `IsPresent`：渐隐后恢复受击抖动。

## 2026-07-26 势能按回合累计；战神之勇再显完整
- 势能清零改 `round_start` 全体静默；火/金光环点着后持续到回合结束。
- 战神之勇：基色只锁一次，偶数隐后再显不再残缺；挂载按当前回合奇偶对拍。
- schema/mechanics/客户端文档同步；golden 需 `--write`（value 序列变化）。

## 2026-07-26 命中拍：裂地＝特效＝抖动同拍
- 命中档取消 `_startDelay` / FadeIn；GrowTime 0.2s 对齐 HitReact。
- `SettleDamage` 明确命中拍（裂地+HitKey+抖动）；RemoteStrike 不再提前 HitKey。

## 2026-07-26 弹道/命中裂地时序对齐 + 绕身不抖
- `PlayPath`：按弹道实时进度过阈值起裂（跟球），不再墙钟等分时刻戳缝。
- 命中裂地收进 `SettleDamage`，与 HitKey 同帧；模板去掉重复 `PlayHit`。
- 持有 `shroud_*` 时 `HitReact` 只红闪不抖动（`UnitAuraService.HasShroud`）。

## 2026-07-26 战神之勇罩身恢复完整件，只留渐隐
- `shroud_ares_might` 重拷 Effect31 全层（含 Rock/Trigger/Audio）；挂载不再裁层。
- `AresMightShroudPulse`：渐隐收干净时 `SetActive(false)`，满显清 MPB。

## 2026-07-26 罩身默认完整加载；裁层仅个案名单
- `WireShroudEffect.CopyFull`：同构厂包件默认不删任何成分；strip 仅可选参数。
- 去 Rock / 关 Trigger·Audio 只留在战神之勇 Wire/Mount 名单；Follower/Fitter 不裁层。

## 2026-07-26 罩身跟随收进通用 VfxShroudFollower
- 新增 `VfxShroudFollower`：Fit 后世界空间钉定位圆，melee/平时一律跟随持有者。
- 战神之勇只留 `AresMightShroudPulse` 奇偶显隐；挂载走 `FitAndFollow`。

## 2026-07-26 战神之勇罩身跟 melee 移动 + 渐隐清黑雾
- 罩身 cell 脱父到世界空间，`LateUpdate` 钉 `CardCircleCenter(unit.position)`，melee 整件跟随。
- 渐隐：粒子 startColor/emission 同步压；t=0 时 `StopEmittingAndClear` + 关 Renderer，杜绝 Smoke 残雾。

## 2026-07-26 罩身地面圈严格锚定定位圆
- `VfxShroudFitter.Fit` 后 `PinGroundRingToCardCircle`：Decal 水平直径钉死
  `CardCircleDiameter`、xz 收至圆心；壳/火仍可按竖向补高，不被连带缩小。

## 2026-07-26 战神之勇罩身去掉漂浮石块
- `shroud_ares_might` 删除 `RockParticles1/2`；壳/火/烟/电/贴花保留。接线脚本同步。

## 2026-07-26 战神之勇改挂 Effect31 罩身（画廊原样）+ 奇偶回合显隐
- `shroud_ares_might` ← Magic Effect31 完整件**原样拷贝**（画廊 2/8·25/61）；挂载路径
  与画廊一致：Instantiate → `VfxShroudFitter.Fit` → ForcePlay → 排序抬升。
- 奇数回合渐显、偶数渐隐（`AresMightShroudPulse`；满显清 MPB 不改厂包材质）。
- 取代常驻 `aura_ares_might`（Effect18）；Registry / Profile / olympus 同步。

## 2026-07-26 命中熔岩跟随裂缝生长（弹道仍先裂后烧）
- 命中：`LavaDelay` 0.08、`LavaGrowMul` 1.0 —— 火贴着放射锋面与缝同长。
- 弹道仍 0.65 / 1.45；`ApplyStrength` 按 Mode 分流写入。

## 2026-07-26 弹道三档全面开熔岩（同档命中 ×0.78）
- 弹道 Light/Heavy 不再关熔岩；`GlowPeak`/`Ember`＝同档命中 ×0.78（约 1.64/2.81/3.43）。
- 主缝 R 门控不变；language / index / assets_upload 同步。

## 2026-07-26 裂地先裂后烧时序拉长（弹道/命中共用）
- `LavaDelay` 0.12→**0.65**、`LavaGrowMul` 1.15→**1.45**：裂缝过半才点火，火爬得更慢。
- GrowTime 弹道 0.16→0.22、命中 0.28→0.36；`ApplyStrength` 运行时刷写，防 prefab 旧值。

## 2026-07-26 弹道裂地终点落到原站位点；战吼取消档 3 特例
- `PlayPath` 终点改用目标 `HomePosition` 脚点；末段强制 progress=1（节拍改
  `s/PathSteps`），裂痕带到卡牌原站位点，不再停在半路或跟 Rest 微抖。
- `hector_warcry` 取消档 3 特例，按准备型约定回档 2；config / language 同步。

## 2026-07-26 弹道改单条蜿蜒主缝 + 熔岩 R 通道门控 + 档 3 再加宽
- 弹道遮罩重做（参考图语义）：一条蜿蜒主缝贯通全幅（7.5~11.5px，±40° 游走），
  树杈分叉 6~9 根 + 3~6 条游离细缝；不再是 2~4 段接力。
- 遮罩 R 通道＝熔岩门：主缝写 1、枝杈写 0，`GroundCrack.shader` 用 texel.r
  门控 heat → 熔岩只顺主缝烧；命中遮罩 R 恒 1 行为不变。
- 档 3 `_MaskGain` 3.1→**3.8**。重跑 G4，ground_crack_language 同步。

## 2026-07-26 弹道 1/2 关熔岩 + 档 1 变细 + 战吼档 3
- 弹道 Light/Heavy `GlowPeak`/`Ember`=0；档 3 仍与命中同亮（4.4）。
- `_MaskGain` 档 1：1.55→**1.15**；档 2 仍 2.55。`hector_warcry`→档 3。

## 2026-07-26 三档裂地缝宽再抬
- `_MaskGain` → 1.55 / 2.55 / 3.1；遮罩主缝弹道 6.5~10.5px、命中 UV 0.022~0.055。重跑 G4。

## 2026-07-26 弹道/命中缝宽加粗 + 弹道同亮度熔岩
- `_MaskGain` 1.2/2.0/2.4；弹道熔岩三档与命中同（Glow 2.1/3.6/4.4）。
- 遮罩主缝加粗：弹道 4.8~8.0px、命中 UV 0.016~0.042；重跑 G4。

## 2026-07-26 弹道改树杈分叉 + 主缝变细
- 主缝宽 8.5~13 → **3.6~6.2**（对齐命中主缝量级）；段间留缝错位。
- 去掉漂浮平行毛刺层；改为从主缝长出的树杈（每段 2~4 根、长短三档、可二级小杈）。
- 重跑 G4。

## 2026-07-26 弹道毛刺短缝加码
- 贴身短叉从本体长出：更密更粗更长，可双侧/三叉簇；周遭短缝贴得更近、更粗。
- 散落细缝 8~14 条、少收尖。重跑 G4。

## 2026-07-26 弹道大缝 ±40° + 周遭短缝
- 大缝偏角放到约 ±40°、折拐更勤，去掉「太齐整」。
- 大缝生长时沿途在周遭撒短小缝；另加 4~8 条散落细缝。重跑 G4。

## 2026-07-26 弹道大缝改接力链 + 分段不复读
- 遮罩：2~4 条大缝沿 +X 接力（厚段重叠 55~120px、两端弱羽化），禁双轨贯通；
  角度约 ±20°。运行：`PickPathKeys` 同弹道三段各抽不同变体。重跑 G4。

## 2026-07-26 弹道大缝改「轴向推进 + Y 奔放」
- 极坐标步长易竖折成塔、丢弹道感；改为强制 +X 前进，斜率吃 dir（±~55°）。
- 双缝间距 12~32px，第二条起步偏角反号；重跑 G4。
- 变体长度/条数规则不变；出场仍 `PickPathKey` 抽 `path_0`~`_3`。

## 2026-07-26 弹道遮罩多变体：长度/条数/间距去僵硬
- 根因：单张 spine 遮罩固定哈希 → 每次出手同一张「两道大缝」。
- G4 烘 4 套 `ground_crack_path_{0..3}`；出场 `PickPathKey` 随机抽。
- 大缝：55%/45% 一或两条；长度大幅拉开；两缝间距 14~38px；方向更奔放。

## 2026-07-26 弹道熔岩：1/2 关、3 档微熔岩
- `BuildSpec`：弹道 Light/Heavy 的 GlowPeak/EmberFloor=0；Blaze=1.8/0.14
  （约命中档 3 的 40%）。命中三档熔岩不变。文档 language / assets_upload。

## 2026-07-26 裂地技能配置规则 + 登记文档
- 规则：准备型物理群攻＝档 2；瞬发＝档 1；`EmpoweredStrike` 强制档 3 弹道
  + 命中面积 ×1.5（覆盖专配）。`hector_warcry` 3/1.5 → 2；assault 仍默认 1。
- 新建 `docs/client/ground_crack_config.md`（按 skill 登记）；同步 language /
  index / extension_points / performance_mechanisms。

## 2026-07-26 弹道接回熔岩 + 裂地权威文档重写
- `BuildSpec` 弹道/命中共用 GlowPeak/EmberFloor（生长跟着骨架、灭点消退同逻辑）。
- `ground_crack_language.md` 重写为现行权威（≤500 行）：两维结构、熔岩时序、
  遮罩规则、模块边界、G14；同步 index / assets_upload / performance_mechanisms。

## 2026-07-26 弹道大缝收至 1~2 条并放开方向
- 大缝 3~4→**1~2**；起步偏角 ±40°、急折更勤/更大，clamp 0.95；大缝改极坐标步长。
- 小缝 5~8。重跑 G4。

## 2026-07-26 弹道改大小缝混排 + 命中熔岩灭点消退
- 弹道：`BakePathMask` 烘 3~4 大缝 + 6~9 小缝进一张遮罩；`PathMode.Spurs=0`，
  不再挂独立毛刺面片。重跑 G4。
- 命中熔岩：生长收紧跟着骨架（Delay 0.12、GrowMul 1.15）；消退改 shader
  `_LavaExtinguish` 多灭点噪声渐灭（每发 `_LavaFadeSeed`），禁全局同步压暗。

## 2026-07-26 熔岩收归命中类
- `BuildSpec` 弹道模式 `GlowPeak`/`EmberFloor` 归零：弹道裂地只留暗缝，
  熔岩只出现在命中处（避免一路烧红抢走落点的分量）。缝宽/持续/放大照常分档。

## 2026-07-26 裂纹大随机化 + 多发裂地彻底错峰
- 命中：主缝 8→10，起点在中心 0~0.09 内随机偏移并加恒定 curl（去"四射星"），
  长度差拉大，新增 4 处离心次级裂源；角度抖动定在 ±0.3rad（±0.55 会聚到一侧）。
- 弹道：遮罩 128→256px、`bakedWidth` 0.55→1.05；改按 x 等步 + y 斜率游走
  （极坐标步长转角一大就铺不满全宽），回中力只在越过半幅 60% 时介入。
- 去同步：Roll 区间放宽（推进 0.55~1.9×、熔岩起步 0.35~2.4×、爬速 0.65~2.0×），
  子面片点火滞后 0~0.45，新增每发 0~0.22s 整体错峰起裂。
- 重跑 G4。

## 2026-07-26 弹道主缝再随机化 + 熔岩亮度整列上调
- 主缝折转 0.34→0.55rad，加 18% 概率的急折与 0.6~1.4 倍不等段长（回中力 0.75
  保住不跑偏）；重跑 G4。
- 三档熔岩亮度整列上调：1.6/2.8/3.5 → 2.1/3.6/4.4（只准整列动，保台阶间距）。

## 2026-07-26 遮罩去直线 + 熔岩多火口去同步
- 弹道主缝/毛刺改折线生成（新 `RasterizeSegments`）：34 段折转 + 半数段带短分叉，
  摆动收敛（折转 ≤0.34rad、回中 0.7）；命中主缝步数 7→10、折转幅度加大。
- 熔岩火口：shader 加 `_LavaScatter`/`_LavaCells` 值噪声，点火时刻散 ±0.2、强度
  散 0.55~1.35；`Decal` 每子面片再随机 0~0.28 滞后；熔岩推进满值 1.47→1.95。
- 重跑 G4（spine/spur/radial 全部重烘）。

## 2026-07-26 命中骨架改自烘分形裂纹 + 熔岩/推进去同步去匀速
- 命中遮罩弃用厂包 Crack1（自带同心环）：自烘 `mask_crack_radial`，8 主缝递归
  分叉 3 级 + 中心碎裂短缝 + 不闭合短连接缝；角度抖动收到 ±0.22rad 防聚簇。
- 熔岩独立时间轴：shader 加 `_GlowGrowth`，火晚 0.35×GrowTime 起步、慢 1.7 倍爬，
  寿命只占裂缝 0.6 且涨退重叠，比缝先灭。
- 去僵硬：`Decal.Roll()` 每发重摇推进/熔岩/停留倍率，推进过 `Burst()` 走走停停，
  熔岩叠双频明灭；毛刺/缝底层间滞后 0.22/0.08（推进满值 1.25→1.47）。
- 重跑 G4；文档 ground_crack_language。

## 2026-07-26 裂地档 3 改为「档 2 的自然增强」（去独立贴图感）
- 取消档 3 叠厂包熔岩层 `ground_lava_bloom`（自带形状 → 读成独立发光贴图）；
  晋升件留库备用，`StandardizeLavaBurst` 不动。
- 档 3 重定：缝宽 2.6→2.05、亮度 4.5→3.5、锋面 0.28→0.24、余烬 0.42→0.34，
  新增 `StrengthSpec.SizeScale`＝1.35（同一骨架整体放大，裂得更远）。
- Shader 加 `_LavaGradient`：缝沿暗红→缝底熔岩→缝心白热，并沿生长方向由热到凉。
- 文档：ground_crack_language / assets_upload_guide；踩坑录 P-55。

## 2026-07-26 修复弹道裂地完全隐形（SmoothStep 误用）
- 根因：自烘主缝用了 `Mathf.SmoothStep(from,to,t)`（插值），当成 HLSL
  `smoothstep(edge0,edge1,x)`，整条脊 alpha≈0（PNG maxA=0.004）。
- 改为自写 `EdgeSmooth`；主缝加粗、带宽 0.55；烘制前校验 maxA≥0.5。
- 重跑 G4，spine maxA=1、实心约 19%。踩坑录 P-54。

## 2026-07-26 弹道裂地改为「主缝 + 短毛刺」干净骨架
- 弃用厂包 Crack.png 作弹道遮罩（锯齿拉宽后杂乱）；G4 自烘 `mask_crack_spine`
  + `mask_crack_spur`；带宽 0.9→0.42；毛刺 5→4、浅角短枝。
- 重跑 G4。

## 2026-07-26 去碎块烟雾 + 裂地持续按模式分档 + 弱踵台词紧绑暴击
- 命中漂浮「烟雾块」＝碎块粒子（Dust 已关仍在）：两模式 `ChunkCount=0`；
  熔岩层 G12 去掉 GroundFog。重跑 G4/G12。
- 持续：弹道档 3＝档 1×1.5，命中档 3＝档 1×2（中间档插值）；`SpecOf(strength, mode)`。
- 弱踵：`heel` 改挂 `parent_seq=damage_seq`（禁止 parent=0），避免台词组排到
  整段出击（含阵亡）之后；单测 `test_aoman_heel_line_immediately_after_crit_damage`。

## 2026-07-26 取消裂地尘雾 + 档 3 亮度下调
- 命中类 `ImpactMode.Dust=false`，不再烘 Dust 层（烟雾抢戏/方块感，直接关掉）。
- 档 3 `Blaze`：GlowPeak 6.0→4.5、FrontWidth 0.34→0.28、EmberFloor 0.55→0.42。
- 重跑 G4。

## 2026-07-26 命中尘雾去方块 + 赫克托尔战吼档 3×1.5 面积
- 命中类尘雾根因：`BuildDust` 用无贴图白材质，粒子 billboard 渲成方角竖牌
  （雅典娜受击时读作「方块烟雾」）。改为 G4 自烘软圆 `tex_ground_dust` +
  `URP/Unlit` 透明（Particles/Unlit 不吃贴图 alpha）+ HorizontalBillboard 贴地。
- `hector_warcry`：`GroundStrengthTier=3`（弹道/命中同档）+ `GroundHitArea=1.5`
  （命中直径＝卡宽×1.5×1.5）。重跑 G4。

## 2026-07-26 裂地重组为「模式两类 × 强度三档 + 面积」
- 两维正交（`GroundCrackPalette` 重写）：**模式** `PathMode/ImpactMode` 只管形状骨架
  （遮罩/生长/朝向/毛刺/碎块/尺寸基准），**强度** `Strength.Light/Heavy/Blaze` 一档同时
  定缝宽+持续+亮度，**面积** 为命中类调用参数。prefab 由 3 件降为 2 件，
  场心大裂地＝命中类骨架 + 档 3 + 面积 3.2（删 `ground_crack_arena`）。
- 缝宽走新 shader 参数 `_MaskGain`（抬遮罩 alpha），不缩放面片，避免放射骨架被拉椭圆；
  `GroundCrackDecal` 新增 `ApplyStrength/ApplyArea`；profile 字段改
  `GroundStrengthTier` + 新增 `GroundHitArea`。
- 亮光与裂缝同步渐变：`Glow()` 只熄到余烬水平后维持，最终消失交给淡出（glow 乘 alpha）。
- 毛刺可见度修复：首版整根埋在主缝带内，改为侧向偏移 + 外指角度 + 长度 0.34~0.60。
- 重跑 G4，G11 探针改成 2 模式 ×3 档一次摆全；实测毛刺清晰、三档台阶拉开、
  驻留期亮光在场、淡出期亮与缝同步消失。文档 §3.0.1/§四/§四.五 重写。

## 2026-07-26 T1 加毛刺分叉 / T2 直径放大到 1.5 卡宽
- 弹道裂地不再是单一长条：`Tier.Spurs=5`，`GroundCrackComposer.BuildSpurs` 把毛刺
  装成裂缝组子面片（共用材质与生长/熔岩/淡出），布局写死不随机，辉度压 0.7 保主次。
- 命中裂地 `CardWidthFactor` 1.15 → 1.5（实测卡宽 1.582 → 直径 2.37）。
- 重跑 G4；Play 中摆件确认毛刺可读、命中圆明显大于卡牌。

## 2026-07-26 裂地强度改为「件自带三档 + 场景选档」
- 结构分离：裂地 prefab 三档通吃（`GroundCrackDecal.ApplyGlow` 出场现写强度），
  用哪档由场景选——档位基线（T1/T2 默认档 1，T3 恒档 3）+ 战法专配
  `PerformanceProfile.GroundGlowTier`，解析在 `GroundCrackService.ResolveGlow`
  （只升不降）。不为分档另烘 prefab 变体。
- 取值：赫克托尔自带 `hector_warcry` 配档 3 熔岩过曝（先配 2，同日观感试看提到 3，
  会叠 `ground_lava_bloom`）；拆解技 `hector_assault` 与其它物理群攻不配＝档 1 微光。
- 实测重播：默认技能全部 `glow=Ember`，`hector_warcry` 解析为 `Molten`。
  extension_points 增登记行；ground_crack_language §3.0.1 重写为选档规则表。

## 2026-07-26 裂地发光分三档 + Effect8 熔岩层按标准化协议晋升
- 纪律先行：`vfx_standardization.md` §二 新增触发句「参考/做到接近某 EffectN」，
  与直接点名接件同级——必须逐层判定「晋升/替代」并把去向表落文档，禁止凭印象
  手搓；`global_rules` §四.8、`00-session-start` 表同步；踩坑录 P-49。
  另明示 `EffectN` 与 `EffectN_Collision` 是不同件，不得互相替代。
- 发光改三级台阶 `GroundCrackPalette.Glow`（微光/明亮/熔岩过曝）：亮度、锋面宽、
  余烬三个 shader 参数统一派生，`Tier` 只声明档位；T1/T2/T3 依次为档 1/2/3。
- 第 3 档观感目标 Effect8：新增 G12 `StandardizeLavaBurst`（菜单 `GreekMyth/裂地/G12`）
  逐层晋升成 `ground_lava_bloom`（留 Particles/Trail/GroundDistortion/GroundFog/Light，
  摘 Decal1 死贴花 + Wind + Audio + 弹道脚本），只跟 T3 叠播。
- 重跑 G4；实测 T3 熔岩裂地可见。guide 登记新 key，ground_crack_language §3.0.1
  记录三档表与逐层去向表。

## 2026-07-26 裂地全透明真因：G4 把 alpha=0 烤进 prefab（P-47/48）
- 赫克托尔群攻裂地「完全看不到」最终定位：G4 存盘前 `SetActive(true)` 触发
  `OnEnable→Apply(0)`，面片 alpha=0 被烤进三档 prefab；运行期 `Collect()`
  把 0 当基色，恒全透明。修：G4 存盘前把 SpriteRenderer alpha 归 1 并重跑；
  `GroundCrackDecal.Collect` 加自愈（基色 alpha≈0 视作满）。
- 裂地存续改为不吃倍速（只吃 DurationMul）：痕迹类特效不阻塞节拍，
  4 倍速同比压缩只剩 ~0.4s 等于没播；快进时以常速淡出。
- 重播实测：path/hit 三档均触发且肉眼可见。踩坑录 P-47、P-48。

## 2026-07-26 裂地收口成 GroundCrackService + 补全模板接线
- 新增 `VFX/GroundCrackService.cs`：裂地唯一入口（`Active` 判据 +
  `PlayPath/PlayHit/PlayArena`），朝向/进度/分段节拍全在内；`DefaultPerformance`
  不再持有裂地私有方法，也不得再直调 `GroundCrackPalette`。
- 修「裂地完全不显示」两个真因：① 裂地此前只接 `PlayAoeCenter`（还要目标≥2），
  单体弹道走 `PerSegment` 全程无调用 → 现 PerSegment 每段一条 T1 + 命中 T2，
  Melee 近身补 T2；② 实例存活按倍速换算而贴花动画走真实秒，高倍速下刚淡入
  就回池 → 新增 `GroundCrackDecal.DurationScale`，两者同一把尺。
- 新增编辑器探针 `GroundCrackProbe`（菜单 `GreekMyth/裂地/G11 静态探针`）：
  Play 中把三档摆到空地并延长驻留，用于分辨「没接线」与「渲不出来」。
- 文档：ground_crack_language 新增 §四.五 模块边界 + G10 阶段；
  extension_points / performance_mechanisms 同步；踩坑录 P-46。

## 2026-07-26 修弹道裂地朝向：G4 烤坏 CrackGroup 旋转
- 根因：`AddComponent<GroundCrackDecal>` 时 `RandomizeSpin` 仍默认 true，
  OnEnable 在俯仰 90° 读改 `localEulerAngles` 万向节锁，把错误 yaw 烤进
  `ground_crack_path`（实测偏 52°）→ 三段裂地读成互不相关。
- 修：组装前先 `SetActive(false)` 再配参；OnEnable 用 `Euler(90,0,z)` 写死平躺；
  重跑 G4。PlayGroundCrack 每次清根旋转再上 yaw。

## 2026-07-26 修复 arena_olympus 导入类型（换图后全黑）
- 换贴图后 meta 被重置为 Default → `Load<Sprite>` 失败舞台放弃；强制改回
  Sprite(Single) 并重跑 G3 碎块图集。以后换图勿删 meta，只覆盖 png。

## 2026-07-26 战场分区/卡尺改为静态配置 BattlefieldLayoutConfig
- 去掉 `BattlefieldLayoutTuning` SO / Resources 资产；改用
  `Units/BattlefieldLayoutConfig.cs` 静态字段调 UI/院区/隔离带比例、接缝、
  旋转、CardScaleBoost、浮空、站位微抖等。改数字后重新进 Play 生效。

## 2026-07-26 战场院区：主战场远侧抽出 D/5 过渡天际线
- `CourtyardDepthFraction=0.2`：原主战场纵深 D₀ 的远侧横条作院区（无站位）；
  站位/隔离带改在缩后主战场（D₀×4/5）内，旋转原点改 `MainCenterZ`。

## 2026-07-26 阵型只按站位自动识别（禁止 formation 字符串入参）
- `TeamSetup` 去掉 `formation` 字段；改为只读属性 `detect_formation(站位)`。
- `resolve_formation(positions)` 仅吃站位；setup / manual_battle / bridge /
  `test_manual_3v3` 不再接受 formation 配置。改站位即改阵型。

## 2026-07-26 站位逻辑旋转 + UI 侧栏 W/4
- `BattlefieldLayout.RotationDeg`：主战场区（含隔离带）绕贴图中心逻辑旋转
  （正 = 顺时针，|θ|<90）；卡牌只平移不自转。
- 修正规则：旋转侧边与地面上/下缘截点回推站位区半纵深
  V=(D/2−M·|sinθ|)/cosθ；隔离带只旋转不修正。六等分在旋转系内做，
  `LocalToWorld` 转回世界；15°/30° 实测格心全部在地面板内。
- `UiSideFraction` 0.2 → **0.25**（UI 侧栏 W/5 → W/4）；θ=0 卡尺仍 1.442
  （高受限，VFX 基准不变）；Standardizer 设计基准锁 θ=0。

## 2026-07-26 地面板「正好拍全」动态反算（修 6 号位出屏根因）
- 根因：站位矩形吃死设计常量 W=23/D=17（近缘 −7），而相机 45°/焦段 55 实际
  只拍到地面 z≥−2.97 → A 队后排整排掉出屏底。
- 定案：`BattlefieldLayout.Recalc(aspect)` 解析反算「相机正好拍全」矩形——
  近缘 = 屏底视线落地、半宽 = 屏侧边在地天接缝处的半宽、远缘 = 接缝 z=10；
  地面下侧/左右恰好卡屏幕边缘（`EdgeGuard` 0.05），站位分区同源 → 天然全入画。
- 纵深恒 13.0、宽随宽高比（差异被 UI 侧栏吞掉）；卡尺 1.889 → **1.442**
  （由格纵深决定、与宽高比无关）；VfxStandardizer 回填 BakedBasis×49。
- `ArenaStageView` 地面板改用同源矩形（删 GroundNearMinZ/WidthMargin 冗余）；
  `CameraFitter.PilotFovFor` 收口 FOV 公式；正交回退纵深压缩映射进安全区。

## 2026-07-26 阵型站位系统革新（矩形六等分 + 六套预设）
- **站位权威**新建 `docs/client/battlefield_layout.md`：地面 W×D → UI 各 W/5 →
  主战场 3W/5 → 隔离带 D/8 → A/B 矩形六等分格心；卡下缘中点贴格心。
  废除逻辑圆径向站位（`Front/BackRowRadial`）；装饰圆可留。
- **客户端**：`BattlefieldLayout` + 改写 `ArenaSlotLayout` / `StanceLayout` /
  `BattleBoardView`；单体制卡宽 ≈1.889；`VfxStandardizer` 回填 BakedBasis×49。
- **服务端**：六阵注册 + `detect_formation` / `resolve_formation`；显式 id 须与
  站位集合一致；空 id 按站位自动识别。雁行数值保留，其余五阵骨架。
- **名词**：一字/锥形/箕形/方圆{3,4,5}/偃月/雁行；废却月/鹤翼/旧方圆{1,5,6}。
  文档同步 arena/rendering/formations/manual_setup/index/extension_points；P-44。

## 2026-07-26 特效标准化纪律定稿（点名即交付，无 GUI 晋升）
- 新增权威 `docs/client/vfx_standardization.md`：画廊原料 → Resources 标准件 →
  Profile key；用户指出哪件后 AI 默认按清单标准化并加载。
- **明确不做**画廊入队/晋升菜单等 GUI；已删拟议 `VfxPromoteTool`。
- 登记：`client/index`、`discipline/index`+coding_standards+extension_points、
  `.cursor/rules`（00-session-start / client-battle）、pack_integration §四指向新权威；
  pitfalls P-43。

## 2026-07-26 极长焦 55 + 舞台按视锥拉大 + 罩身观感余量；卡尺/站位回原
- **焦段** `PilotDistance = 55`（FOV ≈12°）；去掉旧 FOV 下限 28°（否则长焦名存实亡）。
  地面板近缘改为每帧按屏底射线求交外扩，盖住长焦下的"桌沿"黑框。
- **卡牌尺寸** `CardScaleBoost` 试过 2 已回 **1**；**站位** `Front/BackRowRadial`、
  `SpreadCap`、弦端余量一并恢复原值（0.31/0.44/1.1/0.75）。
- **罩身** `TopOvershoot=1.6`、`WidthOvershoot=1.2`：几何齐平在长焦俯视下仍读作
  "差一点"，必须额外超出才观感平齐；折射壳出厂全黑轮廓时补菲涅尔。
- 画廊：`*_Collision` 命中碎件自动贴脚下、加快重播、HUD 标明"一闪即逝"；
  警告文案改为层风险（可贴花摘掉 / 扭曲已开不透明贴图待真机），避免误读成整件废件。
- 文档：`arena_stage` 心智模型与关键几何表同步到 45°/55 焦段与原站位。

## 2026-07-25 角度链定稿（卡后倾 45° / 相机垂直卡面）+ 定位圆重定义 + 罩身特效
> 勘误（2026-07-27）：本条原标题与下一行写作 30°，与当日最终落地的代码不符。
> 当日在 30 与 45 之间反复后**定稿 45**（`CameraFitter.CardPitchDeg = 45`），
> `arena_stage.md` 是权威值。此处已改正，勿再按 30 引用。
- **术语定论**：「后倾 θ 度」一律指**离竖直** θ 度，实现即 `Euler(θ)`。
  唯一真源 `CameraFitter.CardPitchDeg = 45`，派生 `CardLeanDeg = 45`（与地面夹角），
  `PilotPitchDeg = CardPitchDeg`（光轴垂直卡面）。当日先按"与地面 30°"
  （Euler 60 + 俯角 60）实现过，试废：影子纵深 3.13 > 卡宽 2.04 把定位圆撑到
  1.8 倍卡宽（读作"圆被相机拉歪"），且 8.7 米高的竖立件在陡俯角下收敛成
  "指着相机的柱子"。45/55 解耦方案同废。
- **定位圆重定义**＝卡牌影子（**竖直**投影，非相机投影）的外接圆：
  圆心 = 卡心正下方，半径 = √(卡宽² + `CardShadowDepth`²)/2，
  `CardShadowDepth` = 卡高×sin(后倾角)。旧定义（下缘投影为心、卡宽为直径）
  与卡的真实足迹无关。新增 `ArenaSlotLayout.CardTopY`（卡上缘高度）。
- **新增罩身类特效规格** `VFX/VfxShroudFitter`：世界竖直 + 等比 + 水平切面对齐
  定位圆 + 底面坐地；定径基准取件里 **Y 向最高的渲染器（壳本体）**，
  `Simulate(0.6s)` 量一次，摆位解析推算。画廊加第 5 锚点「罩身」，
  `ShroudKeys` 名单（现 `effect31`）自动选中；撤掉旧的「包裹卡牌」定径与 V 键同倾开关。
- 实测支撑：Effect31 在 identity 下 Shield 2.89×**8.66(Y)**×2.89、Lightning
  2.45×7.36(Y)×2.45、Decal 平躺 —— 资源本来就是竖柱，任何补旋转都是错的。
  另查明其壳用 `RFX1_UberDistortion` 折射壳（`_UseMainTex=0`，靠采样
  `_CameraOpaqueTexture` 成像），厂包预览之所以好看还因为 demo 会在罩内放角色、
  并持续朝罩发射弹体产生涟漪 —— 这两件我们暂时都没有，低端机上性价比低，
  建议罩身视觉素材改用自发光壳，Effect31 只作高配加料。
- 文档：arena_stage §一/§二/§三改写 + 新增 §四b 角度链 / §四c 定位圆与罩身；
  rendering_layout、vfx_playback_scheme §七 同步；vfx_pack_integration 新增
  「罩身锚点」小节（含三个错法对照表）。

## 2026-07-25 卡牌倾角与相机俯角解耦：修「卡不畸变、定位圆畸变」的割裂感（当日已被上条取代）
- 病根是几何而非 bug：卡牌倾角旧实现直接取 `cam.eulerAngles.x`，卡面因此
  **严格平行于成像平面** → 透视只对它等比缩放、永不斜切；而定位圆躺在水平面里
  必然投影成椭圆。两种畸变不同族，同框就别扭。
- 新增 `CameraFitter.CardLeanDeg = 45f`（卡牌与地面固定夹角），与
  `PilotPitchDeg = 55f`（相机俯角）彻底解耦。差 10° → 卡牌出现左右对称的轻微
  梯形畸变，与地面圆同族；卡面压缩 cos10°≈0.985，肉眼无损。
- `UnitView.FaceCameraIfPilot` → `ApplyCardLean`（固定倾角，不跟相机）；
  `ResetForNewGame` 改为回到固定卡姿（原来置 identity）。
  `ArenaSlotLayout.GroundPoint`/`GroundFoot` 的倾斜补偿改用 `CardLeanDeg`。
  红线：禁止再拿 `cam.eulerAngles.x` 当卡牌倾角。
- 画廊「包裹卡牌」件改为**跟卡同倾**、原点放卡牌**下缘中心**（沿卡自身向下
  半个卡高，非地面投影），修"椭球升起方向跟卡牌完全不是一个方向"；
  贴地件仍世界竖直。V 键切两种朝向对比。
- 文档：arena_stage 新增 §四b「畸变一致性」并改写心智模型/常量表。

## 2026-07-25 合成底座三件套：碰撞层 + URP 贴花通道 + 卡牌深度代理
- 定调「不换框架」：厂包水土不服的病根在**合成模型**（卡牌是透明 Sprite、
  舞台零碰撞体、URP 贴花通道未开），不在管线选型。退回 Built-in / 改真 3D 的
  收益/代价比明确不划算，结论写入 vfx_pack_integration §8.4，勿再重复讨论。
- **B 碰撞层** `VfxCollisionStage`：新建 layer `VfxCollision`（slot 8）；地面一块
  顶面贴齐 `GroundY` 的 BoxCollider + 每卡一块随卡倾斜的 `VfxHitBox`（取运行期卡面）。
  画廊弹道随之改为**打真卡牌碰撞盒**（起止点从贴地定位圆心改为卡身中心，
  贴地平飞会从盒下擦过），落点标记退化为纯 Target。实测 Effect12 命中件
  生成在敌卡碰撞面上。碰撞体是表现层附属物，禁止逻辑读它做判定。
- **C URP 贴花通道**：`Mobile_Renderer`/`PC_Renderer` 的 `m_RendererFeatures` 原为空，
  各加一个 `DecalRendererFeature`。这不让 KriptoFX 贴花件直接可用（红线不变），
  但为"重烘成 URP Decal"与裂地升级真投影开了门。
- **A 卡牌深度代理** `CardDepthProxy`：每卡补一份不透明 alpha-clip 同形副本
  （卡框+立绘，Geometry 队列，略小略靠后），卡牌因此进深度图与不透明贴图。
  折射壳不再把卡抹掉、软粒子正常淡出、卡后的特效被深度裁掉自动前后分层
  （**排序策略不变**：sortingOrder 只管透明件画序，深度测试照旧生效）。
  画廊 J 键整场开关做 A/B。
- 画廊定径修正：卡身锚点改用「包裹卡牌」规则（只按宽度缩到 1.35×卡宽，
  高度放开、2.6×卡高兜底，原点放定位圆心且**不按包围盒中心对齐**）。
  原按地面投影定径会把柱状件缩得比卡还窄、整件缩进卡牌轮廓 → 误判"没效果"。
- pitfalls 追加 P-40（Play 模式推迟编译，改完必须 stop→refresh→play）、
  P-41（"特效在卡上看不见"先查定径基准，附三步排查顺序）。

## 2026-07-25 画廊补弹道模式：厂包主件（出手全流程）终于能演出来
- 病根：厂包主件（`Prefabs/Effects/EffectN`）自带位移脚本，单锚点摆放下会沿
  自己的 local forward 飞出舞台，且命中件永不生成 —— 被误判为"标准化不出可用件"。
- Runner 新增弹道模式（自动识别 `*TransformMotion`/`*PhysicsMotion`，B 键开关，
  F 键也有手动档）：施法者定位圆心 → 敌方卡定位圆心，反射写 `Target`、
  `Distance`/`MaxDistnace` 改实测距离、`Speed = 距离/0.9s`，落点放不可见碰撞体
  （半径＝定位圆）让厂包自己生成命中件。实测 Effect1/Effect10 弹道贴地飞至目标、
  落点自动生成 `Effect1_Collision`。
- 厂包生成的命中件是场景根节点：补排序抬升 + 换件时按 `(Clone)` 清场
  （池化件都有父节点，不会误清）。
- 新增慢放 0.25×（K）；碎片件排到主件之后（原 Ordinal 排序让 28 件命中碎片占据
  Magic Pack 开头）。
- 文档：vfx_pack_integration 新增「弹道模式」四条必备项与失败后果表。

## 2026-07-25 定名「卡牌定位圆」+ 厂包件在地面定位圆内审核
- 新增权威 API `ArenaSlotLayout.CardCircleCenter/CardCircleRadius/CardCircleDiameter`：
  **卡牌定位圆** = 以卡牌接地中心（`GroundFoot`）为心、运行时卡宽为直径的平躺圆，
  今后所有「落在某张卡脚下」的演出共用此基准（裂地 T2 即其 1.15 倍）。
- 画廊换到厂包组时自动切「目标=雅典娜 / 锚点=卡牌脚下 / 定位圆定径」，
  并把定位圆画成青环；C 键开关定径。定径按 `Simulate(0.12s)` 的起手核心量
  地面投影（推到 0.35s 会把碎屑算进去、主体缩到 ×0.13 看不见），钳在 [0.25,20]。
- 修「彩色系列 132 件全无效果」：整包粒子 `playOnAwake=false`，审核台改为
  实例化后根级 `Clear+Play(withChildren)` 起播。
- 修厂包脚本崩审核台：`RFX1/RFX4_ShaderFloatCurve`、`ShaderColorGradient` 四个
  脚本的 `MaterialPropertyBlock` 改惰性初始化（原只在 Awake 建，OnEnable 先跑就抛），
  画廊实例化再加 try/catch 兜底。
- 定位圆圆环改为躺在地面平面里（物体绕 X 转 90° + 本地下环 + `alignment=TransformZ`）；
  原默认 `alignment=View` 让带子朝相机竖起，55° 俯角下看着"不像画在地上"。
  圆心/半径仍直取 `ArenaSlotLayout`，不做任何补偿。
- 厂包件不做"内容底面对齐地面"抬升：`*_Collision` 原点是爆点、内容上下对称，
  对齐底面会把爆点抬到半空（实测 +4.1）；正确是爆点落圆心、下半截被地面挡掉。
  只保留「整件完全在地面以下」的兜底抬升。
- 文档：ground_crack_language 定位圆红线、vfx_pack_integration 画廊篇补定径与起播、
  pitfalls 新增 P-39（整包无效果先查 playOnAwake / 定格取证 / 单件不得带崩工具）。

## 2026-07-25 特效画廊扩到全项目 898 件 + 审核标记
- 画廊改为按包分组：我方标准件 52 / Magic Pack 61 / RFX4 54 / Vefects 308 /
  Cartoon FX 170 / 2D 斩击 119 / 彩色系列 132 / 闪电链 2，合计 898 件。厂包件
  由启动器编辑期用 AssetDatabase 收集注入（不在 Resources 下，运行期加载不到）。
- 非特效件筛除判据：须含粒子或线/拖尾渲染器且不得含蒙皮网格，剔掉 37 件
  （Magic Pack 的 `Character_Effect*` 是整套 challenger 角色）。
- 新增：↑↓/Tab 切包、PgUp/PgDn ±10 跳件、`-`/`=`/`0` 试穿缩放、
  **M 记可用 / N 记否决 / P 导出到 `Temp/vfx_audit_marks.txt`**。
- 新增锚点「脚下平躺」（判能否当地面法阵用；`aura_aegis` 自带平躺符文环层，
  放脚下即成地面法阵）。HUD 就地标出该件是否含贴花层/扭曲层/品红 shader。
- 厂包件直接实例化时补排序抬升，否则被地面与卡牌盖住无法审核。

## 2026-07-25 特效画廊 + 修正尺寸归一参照（全部特效被缩 41%）
- 新增 `Assets/Scripts/ClientBattle/Test/VfxGalleryRunner.cs` 与菜单
  `GreekMyth/特效/特效画廊（一键）`：用真战报建**真实舞台 + 真实卡牌**，把
  `Resources/ClientBattle/VFX` 全部 52 件逐个过。←→ 切件 / R 重播 / F 切锚点
  （卡牌身上·卡牌脚下·棋盘中心）/ T 切目标卡 / G 自动重播 / P 导出清单。
  HUD 反射扫 `PerformanceProfile` 的 string 字段，显示该 key 现接在哪些战法上。
- 画廊首跑即抓出改造 B 的错：参照卡宽误取交错阵型的 2.041，而实战雁行阵是
  非交错的 1.206，49 件特效被静默缩到 59%。改 `VfxStandardizer` 取非交错档并
  回填全部 `BakedBasis`，旧件 `lossyScale` 恢复原值（归一化中性）。
- 画廊对站位重号的旧战报（Position 是 0/1/2 下标而非 1~6 格号）就地摊到
  雁行 1/2/6，避免两张卡压同一格挡住特效。
- pitfalls 追加 P-38（归一化参照必须实测自资产被调好时的环境；批量改造必须有
  「全资产在真实场景逐件过」的入口）。

## 2026-07-25 裂地默认改为「裂缝生长 + 熔岩锋面」（改造 C）
- 新增自研 `Assets/Shaders/ClientBattle/GroundCrack.shader`：按生长场推进阈值镂空
  （命中/全局径向外扩、弹道沿 uv.x 推进），锋面一条 HDR 熔岩亮带 + 余烬，
  预乘 alpha 混合让「近黑裂缝盖地面」与「熔岩加光」共存于一个 pass。
- `GroundCrackDecal` 增 `GrowTime / GrowthMode / GlowPeak / GlowDecay`，
  经 MaterialPropertyBlock 逐帧写 `_Growth`/`_GlowIntensity`；缝底层辉度压 0.35。
- `GroundCrackPalette` 增 `Lava`（HDR 2.4/0.55/0.12，偏红避开宙斯金雷）与三档
  生长参数；物理群攻的弹道档/命中档**默认开**（0.16s 轴向 2.0 / 0.28s 径向 2.6）。
- 首版翻车并修正：余烬铺满 + 锋面过宽 + 两层等亮 → 橙色爆点盖住裂缝；
  改为锋面/余烬按 mask² 聚拢、`_FrontWidth` 0.16→0.10、`_EmberFloor` 0.22→0.07。
- `ground_crack_language.md` 增 §3.0 记录配方、红线（_Growth 必须推过 1.25）与调参教训。

## 2026-07-25 厂包标准化落地（改造 A/B）+ 贴花判死 + 关掉 API 弹窗
- D-VFX-1 定案为「开」：`Mobile_RPAsset` 置深度/不透明贴图为 1，
  `m_OpaqueDownsampling: 1`（2x 双线性压带宽）。真机帧耗待实测。
- 改造 A：`VFXManager.EnsureVfxSorting` 由只抬 `ParticleSystemRenderer` 改为遍历
  `Renderer` 基类（护盾/冲击波/锁链等网格层不再与卡牌抢层），`VfxGroundLayer` 豁免不变。
- 改造 B：新增 `VfxFitter`（CardWidth / ArenaDiameter / None 基准 + Factor +
  BakedBasis）与两个菜单工具 `GreekMyth/特效/体检·标准化`。全量跑完 52 件：
  补挂 49、地面件跳过 3、清理死贴花 1（`aura_ares_might` 的 `Decal2`），体检 52/52 全绿。
  设计期基准卡宽 2.041 由布局常量复算得出，与运行期一致。
- 厂包深度贴花受控复测（shader 可用 + 相机深度开 + 地面不透明写深度 + 强制全显）
  仍零像素，判定失败形态是「静默不出图」而非品红；红线维持"贴花件一律不接"，
  改造 D 降级。裂地继续走自研三层配方。
- 关掉反复弹的 Script Updating Consent：直接改掉 RFX4 三处弃用 API
  （`velocity`/`drag`/`angularDrag` → `linearVelocity`/`linearDamping`/`angularDamping`）。
- pitfalls 追加 P-36 补记（点 No 不省硬盘，更新器就地改文件不备份）与 P-37
  （体检工具首版高数量级报警要先证伪判据：粒子拖尾槽与空容器节点造成 67 条假警）。

## 2026-07-25 已购特效包改造与应用方案成文
- 新文档 `docs/client/vfx_pack_integration.md`（已在 client/index 登记）：厂包按
  渲染层分五类盘点可用性（粒子/闪电直接可用；网格层缺排序托管；扭曲依赖不透明
  贴图；深度贴花不可用 —— Magic Pack 实测 35 个贴花材质跨 20/33 个 Effect）。
- 查出关键约束：`PC_RPAsset` 深度/不透明贴图都开，`Mobile_RPAsset` 都关，
  Android 默认走 Mobile 档 → 扭曲层、软粒子淡出、深度贴花在真机上全部失效，
  "编辑器验收通过"对这三类不成立。立为决策项 D-VFX-1（移动端是否开不透明贴图）。
- 文档含改造项 A~E（排序泛化 / 尺寸归一 VfxFitter / 生长+自发光配方 /
  贴花 quad 降级 / 移动端扭曲取舍）含成本与验收、接件标准流程九步、分级方案
  （层数走烘制期、强度走运行期，触发侧 GroundKeyOf 覆盖点已存在）、红线、V0~V7 阶段表。
- pitfalls 追加 P-35（两套 RP asset 能力差异，及"先看 shader 采什么全屏纹理"的判定法）。

## 2026-07-25 弹道裂痕带收口到命中裂地圆心
- T1 落点不再取弹道正下方（那是卡心投影，比接地中心深一个 halfCardH·sin(俯角)，
  裂痕带会停在 T2 圆心后方、与命中圆断成两截），改为按弹道实时进度映射到
  `fromFoot→toFoot` 连线取点 → 带子终点正好收在 T2 圆心。
- 新增 `DefaultPerformance.GroundProgress`：弹道地面投影在「出膛点→瞄准点」上的
  投影分量（0~1）。仍完全由实弹位置驱动，保留缓动与错峰差异；<0.05 视为未起飞。
- 实测一局：命中裂地 6 个圆心与 6 张卡接地中心逐一吻合（误差 ≤0.05）；
  弹道裂痕带三点与两端接地中心共线、间距随缓动非均匀（如 z=1.36/0.48/-0.55 → 终点 -0.98）。

## 2026-07-25 命中裂地圆心改取卡牌接地中心
- 新增 `ArenaSlotLayout.GroundFoot(卡心)`：倾斜卡下缘中心的地面投影 =「卡在地板上
  的中心点」。原先用 `GroundUnder(卡心)`，因卡按俯角倾斜、卡心比下缘深
  halfCardH·sin(俯角)，55° 下实测偏远端 1.48 世界单位（≈一个卡宽），圆整体退到卡后。
- T2 命中裂地圆心改用 `GroundFoot`；T1 的朝向端点（受击端与施法端）同步改用它，
  出膛判定仍按卡心投影（弹道实例生在卡心）。
- 实测六个站位偏差一致为 1.48 → 修正后裂纹沿卡下缘向四周放射，不再被卡身吞掉。
- 文档同步 ground_crack_language §4（新增圆心红线，四条红线）。

## 2026-07-25 弹道裂地跟随实弹 + 命中裂地按卡宽定径
- T1 起裂点改取弹道实例的实时 `transform.position`（`LaunchProjectile` 现返回
  实例，`PlayPathCracks` 接 `Transform[]`），不再用施法点→目标的插值猜点：
  多目标错峰弹道各走各的落点，插值会让裂纹与眼睛看到的球错开；未起飞的弹道
  跳过，免得在施法者脚下堆一坨。
- T2 直径从写死 3.4 改为运行时 `StanceLayout.CardWidth × 1.15`：
  `GroundCrackPalette.Tier.CardWidthFactor` 声明倍率，`GroundCrackComposer` 把
  烘出尺寸与倍率写进 prefab，`GroundCrackDecal.ApplyCardWidth` 开播时折算缩放。
- 实测验收：卡宽 2.041 → 裂地边长 2.347（误差 0）；一局战报生成 14 个裂地实例，
  T1 三组各带独立 yaw（91/154/35）沿各自弹道递进，T2 精确落在 6 个站位点；
  T3 实例仍停在预热坐标 x=30（该局未出势能全开加强出手），非缺陷。
- 文档同步 ground_crack_language §4/§5（新增 T1/T2 两条红线的成因与调节旋钮）。

## 2026-07-25 裂地统一语言落地（G3~G6）
- 用户指定方案评估：命中用 Magic `Effect18_Collision` 可行（纯粒子），
  但弹道用 RFX4 `Effect3` 的裂地部分**不可行**——它的裂地全部来自
  `DecalCrackBorder`/`DecalBlackCore`/`DecalCore` 三个 Built-in Decal（P-33）。
  用户裁定：回到原计划的统一三层配方。
- G3 `GroundChunkBaker`：从 `arena_olympus.png` 竞技区中央现切 4×3 碎块图集
  （随机凸多边形镂空 + 断口压暗，舞台名派生稳定种子）→ 碎块与地面天生同色。
- G4 `GroundCrackPalette`（颜色/档位唯一真源）+ `GroundCrackComposer`：产出
  `ground_crack_{path,hit,arena}`，各含 L1 裂缝 / L2 缝底 / L3 碎块（＋T3 尘雾）。
  三张遮罩自烘统一极性（三张源图明暗各不相同）并做 alpha 膨胀加粗。
- G5 接线：`PlayPathCracks` 传 `YawAlong` 弹道朝向（T1 线形沿弹道拉长）；
  命中帧 T2 直径 2.2→3.4（2.2 会被卡牌立绘整块盖住）。
- G6 T3：`ctx.EmpoweredStrike` 的物理群攻在场心起大裂地（相机抖动未接）。
- `GroundCrackDecal` 改为驱动整组 SpriteRenderer；新增 `VfxGroundLayer` 让
  `VFXManager` 不把地面层粒子排序抬到卡牌之上（尘雾曾把立绘压灰）。
- 旧实现清除：`WireMagicPackZeusAthena` 不再写 `ground_*`，删 `ground_shatter`。
- 文档同步 ground_crack_language（§3/§4/§5/§7 全面重写）、assets_upload_guide、
  performance_mechanisms、client/index、pitfalls P-34（地面特效看不见的三个真凶）。

## 2026-07-25 G1 地面改不透明网格 + G2 证否厂包贴花
- `ArenaStageView` 地面由 `SpriteRenderer` 换成 Quad `MeshRenderer`：新增
  `BuildGroundMaterial`（`URP/Unlit` Opaque、`ZWrite On`、`_Cull=0`、queue 2000），
  贴图仍是同一张 `arena_<stage>`，UV 按 `sprite.textureRect` 归一化；
  `FitToCamera` 因 Quad 为 1×1 单位而直接用世界尺寸。**观感像素级不变**，
  且天空板/卡牌与地面的遮挡关系变正确。
- G2 探针实测**证否**"厂包贴花会因此解锁"：`KriptoFX/RFX1/Decal` 等是 Built-in
  管线 shader，URP 下不做深度重建，把投影盒渲成悬空品红亮块。三包的 Decal
  组件列入红线永久禁用，只取粒子部分；地面投影改走 URP DecalProjector
  （`PC_Renderer`/`Mobile_Renderer` 尚未挂 Decal Renderer Feature，列为 G2b）。
- 同步 `docs/client/ground_crack_language.md`（§一.3/§二/§五/§七）与
  `ai_workflow_pitfalls.md` P-33（必要条件≠充分条件；探针验收法）。

## 2026-07-25 裂地表现语言方案定稿
- 定位根因：两个已购包的裂地**全是深度投影贴花**（`RFX1_UberDecal` /
  `RFX4/Decal`，`SAMPLE_DEPTH_TEXTURE`+`Cull Front`），舞台地面是不写深度的
  透明 Sprite → 必然全空。逐条实测 RFX4 `DecalCrackBorder`、Magic
  `Effect11/Decal` 均如此。结论：买包不解决问题，先改地面。
- 新文档 `docs/client/ground_crack_language.md`：三档裂地（弹道/命中/全局大）
  统一三层配方（裂缝/缝底/碎块）、**碎块贴图从 `arena_<stage>.png` 现切**
  使其与底图构造上同色、地面改不透明写深度网格为前置（G1），阶段 G1~G7。
- 采购结论：统一语言不需买包；遮罩形状不足时可选 Game VFX - Ground Crack &
  Explosion（≈$11），但须先完成 G1 否则买了也不显示。
- 过渡实现落地：自建平躺裂纹面片 `VFX/GroundCrackDecal.cs`（绕开深度投影）
  + Magic `Effect11_Collision` 碎石；路径/命中同源、以缩放与发射量分档。

## 2026-07-25 地面崩坏族风格统一
- 路径与命中改**同源** Effect11_Collision，只以缩放分层级（1.0 / 0.5）；
  弃用 Effect9_Collision（`Lava` 岩浆缝＝火系语义，与物理群攻不符，且与路径件割裂）。
- 实测记录：Magic Pack 1 Collision 件里的 `Decal` 不是裂纹而是暗尘印，按命中法线
  `LookRotation(up)` 摆正后在亮色大理石地面上仍几乎不可见 → 当前"裂地"实际只有
  碎石抛飞，无地面持久裂纹。项目内唯一真裂纹＝RFX4 `Effect3` `DecalCrack`（受红线）。

## 2026-07-25 裂地换真·地面崩坏件
- 原 `ground_shatter` 误接 Effect18_Collision（内部只有 ShieldCollision/Fire/Trails，
  护盾撞击火花，**无裂地贴花与碎石**）→ 观感几乎不可见。
- 命中脚下改 **Effect9_Collision**（Decal/DecalCore 焦坑 + RocksBig/Small +
  Lava×3 + SmokeExplosion），scale 0.35→1.0、生命期 0.6→1.6s；
  路径新增 `ground_crack_path` ← **Effect11_Collision**（Rocks + Decal），scale 0.7，
  去掉原 0.55× 二次缩放。`GroundKeyOf` 改为路径/命中各自回退。
- 未采用 RFX4 Effect3（DecalCrack）/Effect5/24：RFX4 红线限舞台远景与神像大场面。
- Play 内实拍验收通过（路径碎石横贯战场、脚下焦坑烟尘）。

## 2026-07-25 修舞台图片消失（导入类型回退）
- BattleReportTester 播放全黑无舞台：`arena_olympus.png` 导入类型被重建为
  Default，`Resources.Load<Sprite>` 为 null → `ArenaStageView.TryBuild`
  地天任一缺失即整体放弃。已改回 Sprite(Single) 并验证恢复。
- 排查口径：舞台全黑先查 `Resources/ClientBattle/Arena/` 贴图 Texture Type。

## 2026-07-25 物理群攻地面裂地特效
- 物理群攻主动（AoeCenter）：弹道飞行期间沿「施法点→各受击者」地面投影分 3 段
  播裂地（0.55× 小档）；命中帧在每个受击者脚下地面再播一发。
- 新 profile 字段 `GroundPathKey`/`GroundHitKey`，默认回退 `ground_shatter`
  ← Magic Effect18_Collision（接线菜单已加，scale=0.35，关点光）。
- 仅近 3D 透视舞台生效：新增 `ArenaSlotLayout.GroundActive` / `GroundUnder`
  （贴地 +0.05 防 z-fighting）；正交模式不播。文档同步 performance_mechanisms /
  assets_upload_guide。

## 2026-07-25 透视站位落地面 + manual 双方雁行阵
- 修站位错乱：透视模式下卡牌原仍按 XY 平面 z=0 摆放，与地面板脱节悬空。
- 新增 `ArenaStageView.MapSlotToGround`：布局 y→深度 z、卡下缘贴地抬升（cos45°×半卡高）；
  `BattleBoardView.Build`/`Center` 在 `_arenaMode` 下走该映射，中线=地面 z≈0。
- `test_manual_3v3` 双方已配 `yanxing` [1,2,6]，重跑生成 `manual_3v3_seed20260722.json`。
- 修观感「整阵向玩家平移」：卡 45° 倾斜后下缘偏 -sin45°×半卡高，映射时推 z 补偿，
  使接地点严格镜面对称于地面中线。
- 站位点重规划：新增战斗圆概念（CircleCenterZ=1.5 / R=6.5，对准地面图中央法院），
  `ArenaSlotCenter` 按半圆几何布点——前排 ±0.26R 贴中线、后排 0.62R 径向深入，
  横向展开取弦长与 DesignHalfWidth×0.85 较小者；替代旧「布局 y 直映 z」。
- 微透视（桌面扭转语义定稿）：`PilotYawDeg`=15° 试后定 **8°**。相机始终正面 45° 俯视——
  真转相机（无论 pitch+yaw 合成、绕圆心竖直轴、还是旋转棋盘节点，三版均废弃）
  都会让地台远边一头高一头低并露黑角。最终：仅 `ArenaSlotCenter` 站位逻辑圆
  绕圆心旋转 8°，卡牌不自转；圆形竞技场旋转不变、地平线水平。
- 逻辑圆放大对齐底图大圆：CircleRadius 6.5→8；站位更分散（横向展开钳制
  0.85→0.95×半宽）；径向前 0.26/后 0.44×R（后排再大会被屏幕下缘裁掉，
  0.60/0.72 两档试后回收）。Play 截图验收通过。
- 取消势能四轨迷你条展示（UnitView 不再建条，SetMomentum 空转；火/金光环/
  白闪保留）；站位横向钳制再放宽 0.95→1.1×半宽。
- 卡牌浮空 1/5 卡高（GroundPoint 抬 y）；两队前排径向 0.26→0.31×R 拉开间距。
- 逻辑圆倾斜最终取消：`PilotYawDeg` 8°→0°（旋转机制保留，改常量即可复开）。
- 修「幽灵卡」双影：Play 中热重编译清空 _units 字典但卡 GameObject 残留，
  `BattleBoardView.Clear` 改为 GetComponentsInChildren 兜底全删（P-31）。
- 俯角 45°→55°（"站起来看"）：机位抬高，近远排透视大小差收敛；卡后仰、
  贴地补偿同源 `PilotPitchDeg` 自动跟随。
- 舞台表现模块化：站位逻辑从 ArenaStageView 拆出为 `Units/ArenaSlotLayout.cs`
  （逻辑圆唯一权威），ArenaStageView 只管地/天底图；魔法数具名
  （HoverRatio/SpreadCap/ChromeFactor 公开化）；GroundY/圆心 z 同源
  CameraFitter（PilotGroundY/PilotPivotZ 唯一定义）。回归截图一致。
- 新文档 `docs/client/arena_stage.md`（近 3D 舞台表现权威：桌面比喻、模块
  职责表、定稿常量表、站位规则、已废弃方案清单），index/rendering_layout 登记。

## 2026-07-25 Arena 资源协议 + 奥林匹斯地/天拼接落地
- 协议：`Resources/ClientBattle/Arena/arena|sky|statue_<key>.png`；登记 assets_upload_guide §5b。
- 新增 `Units/ArenaStageView.cs`：地面平躺板 ⊥ 天空竖板（45° 相机），两图齐备自动替换平面背景；
  `BattleBoardView.BuildBackground` 接入，Clear 同步清理；已预写两张图 Sprite .meta（P-20）。
- 已在 ClientBattleDemo Play 截图验收：地面圆环托卡、近大远小成立、接缝≈屏高 2/3（GroundFarZ 调 14→10）。
- 修：天空只按「接缝→屏顶」需要高度取尺（蓝天入画，不再只见雾底）；地面宽度冗余 1.15→1.45 消透视黑角。
- 修：天空宽度按 45° 斜向光路补偿（×2），消除两上角黑缺口（原「圆角」观感）。
- 计划推进：见 near3d_evaluation §六b（下一步接缝雾带/神像淡入/其余两舞台）。

## 2026-07-25 舞台全宽铺满 + 云/地天过渡/景深写入提示词
- 防突兀=地天 16:9 横向全宽铺满，UI 半透叠两侧；卡仍只落中央竞技区。
- 地面：中央≈50% 法院，左右翼同材质填满；上远下近大气景深。
- 天空：垫地面；神/人必有云、妖暗雾；底边雾色接地面远缘。

## 2026-07-25 天空出图：垫地面参考 + 时段定稿
- 天空必须垫该舞台已定稿地面图（只借色板/材质/光感，不抄俯视构图）。
- 时段：神=正午、人=黎明、妖=暗夜；§7.3–7.6 与分舞台 Negative 已写明。

## 2026-07-25 地面竞技区比例修正（看台不得过半）
- 出图失败模式：中心场太小、观众席纹理占多半；§7.1 改为法院≈70–75%、看台仅边缘 10–15% 窄圈。
- 主题块与 Negative/验收同步：看台大于场地一律作废重出。

## 2026-07-25 舞台地/天分图 + 竞技场感地面指令
- 废止一体式与「纯材质 swatch」地面；天空竖板 ⊥ 地面水平 Quad。
- `near3d_evaluation.md` §七重写：地面须含中心静区/内环/外环看台 footprint/轴线；天空不画地板。
- 横屏布局目标：左右各≈1/4 UI；中间上1/3天、下2/3地+卡。

## 2026-07-25 近 3D 默认 45° + 地面 AI 出图指令
- `CameraFitter.PilotPitchDeg=45`、`PilotDistance=12.5`；卡 FaceCamera 后与地面夹角≈45°。
- （§七 已被同分日「地/天分图」条目取代，勿再用旧正俯视材质板指令。）

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
