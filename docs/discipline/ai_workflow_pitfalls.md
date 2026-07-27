# AI 工作流踩坑录（持续追记）

> 记录历史会话中 AI 实际踩过的坑与纠正结论。开工前通读；踩新坑当次会话内追记。
> 格式：`## P-序号 标题`＋现象/根因/正确做法，3-6 行。

## P-01 文档漂移：机制改了文档没跟

现象：schema 升 1.3.1、圣盾改反弹、计次时点前移后，多份文档仍写旧口径
（index 写 1.3.0、statuses 留废字段、determinism 顺序写反）。
根因：改代码时只改「显然相关」的一份文档。
正确做法：改机制时全文检索该机制关键词（中文名+id+常量名），全部命中文档一起改；
大版本后做全量校阅。

## P-02 golden 当挡箭牌或随手重生成

现象：测试红了想改公式/改 golden 让它过。
正确做法：先判断语义权威（skills 文档/D 系列决策）在哪边；golden 只在**语义
确实变更**时用 gen_golden --write 重生成，且必须说明原因。禁止为过测试改标定公式。

## P-03 事件流分组按「连续段」想当然

现象：EventPipeline 最初按连续 group_id 段分组，群攻被穿插的 status_tick
切碎成多个播放单元（天雷击一炮拆三炮）。
正确做法：group_id 是**全量聚合键**，用字典聚合非连续段；契约里
「一个播放单元 = 一次释放」是硬语义。

## P-04 节点组子事件漏落账

现象：石化到期的 status_remove 挂在 round_start/action_start 节点组下，
播放器只看组根，覆盖层永久残留。
正确做法：任何组的**全部子事件**必须 ApplySilently 落账；镜像状态与表现
原子绑定，落账遗漏第一时间表现为「残留」。

## P-05 远程桌面卡顿误诊为引擎掉帧

现象：用户远程观看反馈「卡死」，实测 FrameSpikeProbe 60fps 稳定。
正确做法：先上探针（帧尖峰日志+心跳转子）拿数据再动手优化；
区分引擎冻结（转子停）与显示/传输问题（转子转、内容停）。
性能结论以独立 Build 探针数据为准，编辑器/远程观感不作数。

## P-06 特效缩放按包围盒归一化翻车

现象：按 prefab 包围盒统一归一缩放，拖尾/发射域把包围盒撑到几十单位，
核心画面缩没了（“基本看不到任何特效”）。
正确做法：特效尺寸**目视校准**逐个定值（见 assets_upload_guide）；
演出层只做相对乘法，回池复位。

## P-07 Windows 构建 CET 崩溃（UnityLinker/il2cpp/Analytics.exe）

现象：Build 时 CLR `AreShadowStacksEnabled` 弹窗反复出现。
正确做法：管理员 PowerShell 对相应 exe 执行
`Set-ProcessMitigation -Name <exe> -Disable UserShadowStack`；
弹窗期间不要反复重发 Build 指令。

## P-08 增量构建产出陈旧 exe

现象：Build 出的 exe 无开屏无内容——增量构建混入旧文件。
正确做法：架构级改动后（删目录/换 asmdef/换场景）一律 clean build，
并确认场景列表与启动脚本在 Build Settings 内。

## P-09 PowerShell 环境的 shell 语法坑

现象：heredoc（`python - <<EOF`）、单引号嵌套、`&&` 旧版兼容等在
Windows PowerShell 下报错；控制台中文输出 UnicodeEncodeError/乱码。
正确做法：复杂文本处理写成临时 .py 文件执行（用完删除）；
输出走 utf-8 safe_print；路径含空格加引号。

## P-10 临时探针脚本用真实 API 前先核对签名

现象：探针里 `hero_setup()` 少参数、事件字段拿 `e["kind"]`（实为 `e["type"]`）
连续报错返工。
正确做法：写探针前先读目标函数签名与契约字段表；探针也要一次写对。

## P-11 Unity 编辑器状态误判

现象：播放「冻结」查了半天，实际编辑器处于 Pause；MCP 截图工具触发
PlayerLoop internal 递归报错被当成游戏 bug。
正确做法：先检查编辑器 Play/Pause 状态与已知工具副作用，再查代码。

## P-12 「随机」与「受击率选人」混写

现象：文档把 select_enemy_by_hit_rate 写成「随机」，误导平衡分析。
正确做法：均匀随机与受击率加权是两种机制，文档用词必须区分（doc_standards §三）。

## P-13 决策编号/章节号引用凭记忆

现象：hesitation 文档把 D-15 写成 D-16；schema 演进表把 payloads §7 写成 §8。
正确做法：写交叉引用时打开目标文档核对编号，不凭记忆。

## P-14 旧架构描述残留误导后续工作

现象：faction_style/decisions C 系列仍指向已删除的 Battle/Presentation 体系，
后续 AI 若照文档找代码会全部落空。
正确做法：删除/重构代码时同步处理指向它的所有文档——要么更新、
要么头部标注【历史文档】并指向现行权威。

## P-15 伤害响应「全局 priority 合并」≠ 先守后攻

现象：旧 `_dispatch_damage_hooks` 把攻守钩子按 response_priority 全局升序合并；
攻方雷霆(30)会插在守方低优响应之后、但若攻方 priority 更小会先于守方反打。
产品口径是「A 打 B 双方都响应 → 先 B 后 A」。
正确做法：先整段 `on_damage_taken`，再整段 `on_damage_dealt`；各段内再按
priority。客户端播放序跟随事件流，勿另造一套排序。

## P-16 同持有者触发：他人挂的优先于自己的

现象：A 身上既有队友神谕挂的触发（雷霆）又有自己的追伤标记时，只按
response_priority 排序，可能让自身低 priority 插在他人神谕之前。
正确做法：单持有者分发用 `_owner_hook_key`（他人层 → 自身层 → priority）；
跨持有者用 `_global_hook_key`（priority → hero_order → 他人/自身层）。

## P-17 管线拆组丢了组根 → 演出配置失效

现象：TraitLineExtract 初版把台词后的伤害段拆成以 DamageEvent 为 Root 的新组，
`VFXResolver.KeyOf` 拿不到 `heracles_trials`，十二试炼反打不走 Melee（像没出手）。
正确做法：任何 processor 拆分组时，出击段必须**保留原组 Root**
（skill_trigger/status_tick），只有台词自身可以另立 TraitLine 组。

## P-18 事件挂靠位置决定客户端是否播出

现象：状态台词初版 `parent_seq=action_seq`，挂进 action_start 节点组；
客户端把 Node 组静默落账，气泡从未弹出（引擎侧其实发了事件）。
正确做法：需要占用时间轴的表现型事件（台词等）一律 `parent_seq=0`
自成组；发新事件前先确认客户端对目标 GroupKind 的处理路径。

## P-19 时间轴等待时长 ≠ 表现实际时长 → 重叠

现象：气泡动画 1.6s+，Runner 只等 0.5s，台词与下一单元的斩击/位移重叠。
正确做法：阻塞时长由表现服务给出（`ChatBubbleService.SayExclusive` 返回值），
Runner 不得自定常数；「独占单元」= 等够动画完整时长、前后不加额外停顿。
动画 DOTween 与返回秒数必须同一套 `DurationMul/Speed` 缩放——只乘 Wait、
不乘气泡会在泡收起后空等一截（2026-07-23 阿喀琉斯贯穿台词观感）。

## P-20 Resources 路径/导入类型错 → 上传资源不生效

现象：立绘放 `Resources/Portraits/`（缺 `ClientBattle/` 段）且 Texture Type
是 Default，`Resources.Load<Sprite>` 返回 null，Play 仍是占位色块。
正确做法：真实资源必须放 `Assets/Resources/ClientBattle/<类别>/<key>`；
贴图 Inspector 选 Sprite (2D and UI)；文件名=英文 key（template_id）。

## P-21 叠加特效被 sortingOrder 压住 → 看似没播

现象：宙斯头像标 sortingOrder=8 < VFX 池默认 40，被落雷粒子盖住，
误判为「头像没显示」。
正确做法：先查 sorting 层级表（见 docs/client/rendering_layout.md）再查逻辑；
新表现物先定 sortingOrder 档位并登记。

## P-22 单挑台词不要 parent_seq=0 / 不要从 Duel 组抽出

现象：按 P-18 把 duel_* 台词改成独立组，或 TraitLineExtract 抽走 Duel 组内
`trait_trigger` → 播放顺序变成「整段单挑演完才说叫阵」，或 challenge/result
与台词错位。
正确做法：单挑台词**挂在 duel 组内**；`DuelPerformance` 按时点（号角后叫阵 →
应战/拒战）播气泡；`TraitLineExtractProcessor` **跳过** `GroupKind.Duel`。

## P-23 PowerShell 文本替换会毁掉 UTF-8 中文文件

现象：用 `Get-Content | -replace | Set-Content` 批量改含中文注释的 .cs 文件，
输出被按本地代码页重编码，中文全部变乱码（2026-07-23 PerformanceRunner 中招，
被迫整文件重写恢复）。
正确做法：改文件一律用编辑器级工具（StrReplace/Write）；确需脚本批处理时用
Python `io.open(..., encoding="utf-8")` 读写，禁止 PowerShell 管道改中文文件。

## P-24 棋盘布局半宽 ≠ 相机可见半宽

现象：宽屏 Game 视图里三列卡牌横向间距夸张、分布「完全不对」
（2026-07-23 方圆阵）。
根因：`StanceLayout.RecalcFromCamera` 用 `orthoSize × aspect` 当布局半宽，
与 `CameraFitter`「安全区固定、宽屏两侧只铺背景」冲突。
正确做法：落点/卡尺锁定设计安全区（半宽 4.6 / 半高 5.2）；相机只负责取景。
改阵型几何后必须重生/重载对应战报（旧 `positions` 会触发错误阵型或同列叠放）。
异阵对打必须**逐队** Detect，禁止两队站位并集推断。

## P-25 RFX4 禁止接到宙斯/单挑（喷射粒子红线）

现象：宙斯/单挑接 RFX4 Effect10/20/25 后观感是烟花喷射，用户多次否决（2026-07-24）。
根因：RFX4 是 3D HDR 粒子爆发包，不是电弧几何/2D flipbook 电击。
**硬性禁令**：宙斯 `thunder`/`zeus_bolt`、单挑 cut-in **禁止**再接 RFX4 任何 Effect。
竖雷=`thunder`/`zeus_bolt` 均 DR 单道；命中一律 `hit_lightning`（Electric_Impact_02）；
单挑=cut-in 白闪。
RFX4 仅可作舞台远景/神像大场面候选，且须人工点名批准，禁止「试看」擅自接线。
## P-26 群攻 Cast 不能在进中心前播

现象：赫克托尔战吼冲击波「经常看不到」（2026-07-24）。
根因：共用入口在 `PlayAoeCenter` 之前于**己方卡位**播 CastKey，随后武将才挪到
棋盘中心齐射；冲击波落点错误且被卡面/时机淹没。
## P-27 准备型战法专配 AoeCenter 会空跑

现象：赫克托尔战吼「第一回合放技能没播」（2026-07-24）。
根因：`prepare` 组无伤害，但 FindSpecial 仍返回 `Template=AoeCenter`，演出空跑
进中心再回位，真实 `release` 齐射被观感淹没或误判为未播。
正确做法：群攻专配用 `Auto`（有伤害且目标≥2 才升 AoeCenter）；无伤害/治疗组
在 DefaultPerformance 入口直接飘技能名+落账并 yield break。

## P-28 RFX4 不可用「拖进 ClientBattleDemo」预览

现象：PC Demo 粉红村、Game 全白/全黑、Effect 一闪没有（2026-07-24）。
根因：Demo 村是 Built-in 材质；战斗场景正交白底+无 Bloom；Scale 0.2 过小；
弹道 Effect 高速飞走；粒子播完不重生。
正确做法：只用菜单 **`GreekMyth → RFX4 可靠预览（一键）`**（`Rfx4Preview` 场景：
透视、深色底、Bloom、地面、Effect1–27 自动循环）。禁止再手拖进战斗 Demo 当预览。

## P-29 Magic Pack「预览好看、战斗残缺」与按键无反应

现象：可靠预览有绕身/Fringe，战斗或接线后只剩火烟；←→ 按了没切换（2026-07-25）。
根因：① 未导入 URP Patch → Distortion/Fringe 粉红或消失；② Resources 变体缩尺+关灯，
`MountAresMightAura` 再裁剪组件；③ 项目仅 New Input System，焦点不在 Game 窗则收不到键。
正确做法：选材以 **`GreekMyth → Magic Pack → 可靠预览`** 为准；战斗差异先导 URP Patch
再查挂载。Play 后**先点 Game 窗口**再按 ←→ / 1/2/3。

## P-30 RFX4 Effect22 全粉 / 部分特效带粉红成分

现象：可靠预览里 Effect22 整片洋红，其它 Effect 局部粉块（2026-07-25）。
根因：项目是 URP，但未导入官方 `HDRP and URP patches/URP patch.unitypackage`；
`Effect22/Fog.mat` 等材质落到 Built-in `Particles/Additive` / `Standard`，URP 无法编译 → 洋红。
正确做法：菜单 **`GreekMyth → RFX4 → 导入 URP Patch（修粉红）`**（或等价应用该 unitypackage）；
再用 **`诊断粉红材质`** 确认 `Effects/Materials` 下无 Built-in/InternalError shader；
重开可靠预览验收。禁止手改单个材质去「猜」URP 替代 shader（以官方 patch 为准）。

## P-31 Play 中热重编译产生「幽灵卡」双影 + 旧程序集假验收

现象：卡牌/名字双层错位叠影，多出的 UnitView `Hero==null`（2026-07-25）；
另：改完代码直接截图验收，跑的还是旧逻辑（改动看似无效）。
根因：Play 模式中 refresh → domain reload 清空非序列化状态（`_units` 字典、
runner 会话），但场上 GameObject 仍在；重播时 `Clear()` 只按字典删卡 → 孤儿残留。
且 Play 挂起时 Unity 不换新程序集，`refresh_unity` 返回成功≠新代码已加载。
正确做法：① `BattleBoardView.Clear` 已改为 `GetComponentsInChildren<UnitView>`
兜底全删（勿回退成只清字典）；② 验收流程固定 **stop → refresh → play**，
改常量后可用反射读值确认新程序集已加载再截图。

## P-32 厂包地面贴花在近 3D 舞台上"播了但全空"

现象：接了 RFX4 `DecalCrackBorder` / Magic `Effect11_Collision` 的 `Decal`，
实例存活、renderer enabled、位置在屏幕内，画面上就是什么都没有（2026-07-25）。
根因：这些贴花是**屏幕空间深度投影**（`RFX1_UberDecal` / `KriptoFX/RFX4/Decal`：
`Cull Front` + `ZWrite Off` + `SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture)`），
而舞台地面是 `URP/2D/Sprite-Unlit-Default`、renderQueue 3000 的透明 Sprite，
**不写深度** → 贴花没有可重建的表面。把 `_Cutout` 拉满、按命中法线
`LookRotation(up)` 摆正都无效，这不是参数问题。
正确做法：① 要用厂包贴花，先把地面换成不透明写深度的网格（见
`docs/client/ground_crack_language.md` G1）；② 在那之前，地面特效只能用自建
平躺 Sprite 面片（`VFX/GroundCrackDecal.cs`）；③ 排查此类"看不见"先查
**目标表面是否写深度**，再查 alpha/朝向/排序，顺序反了会浪费很多轮。
附带教训：`AssetDatabase.CopyAsset` 后同帧 `LoadAssetAtPath` 拿到 null，
必须先 `ImportAsset(..., ForceSynchronousImport)`。

## P-33 「地面不写深度」只是表层，KriptoFX 贴花在 URP 下根本不可用

现象：按 P-32 的结论把地面改成不透明写深度网格（G1）后，Magic
`Effect2_Collision` 的 `DecalCrater` 依然不对 —— 且不是"看不见"，而是渲成
悬空的品红亮盒面（2026-07-25）。
根因：`KriptoFX/RFX1/Decal`、`KriptoFX/RFX4/Decal` 是 **Built-in 管线 shader**。
URP 会把无 `LightMode` 的 pass 当 `SRPDefaultUnlit` 画出来，但深度重建那套
built-in 宏/矩阵不成立 → 投影盒立方体被原样加色渲染。地面写不写深度无关。
正确做法：① KriptoFX 三个包的 Decal 组件**一律不接线**，只取粒子部分；
② 地面投影走 URP 官方 Decal Renderer Feature + DecalProjector（它才真需要
G1 的不透明+深度前提），或自建平躺面片；③ 采购红线：任何基于 KriptoFX
贴花技术的裂地包都不要买。
方法论教训：诊断出一个必要条件（地面写深度）不等于它是充分条件。
G1 这类"大前提改造"应当先用**最小探针**验证收益再全量推进 —— 本次 G1 改造
本身仍是对的（URP 贴花、遮挡关系都依赖它），但预期收益换了对象。
探针要点：`Time.timeScale=0` + 关掉粒子 + 强制 `_Cutout=0`，才能把
"贴花本身长什么样"从战斗画面里隔离出来；`cam.Render()` 手动渲染绕不出
URP 完整路径，验收截图用 `ScreenCapture.CaptureScreenshot`。

## P-34 地面特效"看不见"的三个真凶（都不是 alpha 不够）

2026-07-25 落地三档裂地时逐个撞上，排查顺序按此表走能省很多轮：

| 现象 | 真因 | 修法 |
|---|---|---|
| 遮罩铺上去是**整块方块**或**全透明** | 厂包裂纹图**明暗极性不统一**（RFX4 `Crack` 白线黑底、Magic `Crack1` 与 RFX4 `CrackHeight` 都是黑线白底），直接设成 Sprite 必错 | 自己烘遮罩，逐图指定 invert + 灰度重映射区间，统一成 RGB 白 + alpha |
| 裂纹**在大地面上糊没了** | 原始线宽只占 1.4% 像素，铺到 3.4 世界尺寸就细到看不见 | 烘制时 alpha 做**最大值膨胀**（分离式两趟）加粗到 10% 量级 |
| 受击者脚下的裂纹**完全看不到** | 卡牌是竖立 billboard，**它脚下的地面本就被自己挡住**；裂纹直径 2.2 < 卡宽 1.6 的投影覆盖 | 直径放大到明显超出卡轮廓（3.4）；验证时先把探针放到**空地**上再判断"到底有没有渲出来" |

第四个坑（不是看不见，是看见了不该看见的）：地面尘雾被
`VFXManager.EnsureVfxSorting` 的"粒子排序下限 45"一把抬到卡牌之前，
把英雄立绘整片压灰。→ 加 `VfxGroundLayer` 标记豁免排序抬升。

另一条方法论：高度图（`*Height.png`）**不是**裂纹遮罩。它的黑缝外围有一大圈
柔性渐变，低阈值会把渐变一并留下，渲成一团灰雾而不是裂缝。

验收取图教训：裂地是 1~2.6 秒的瞬时演出，靠 MCP 轮询"活实例再截图"命中率极低
（该战报里物理群攻整场只触发一次）。可行做法是先加一条临时 `Debug.Log` 确认
**接线是否触发**并拿到真实坐标/朝向，再用这些坐标复现出静态画面
（长 `Hold` + `ParticleSystem.Simulate` 定格）来看观感。

## P-35 「编辑器里效果对了」不等于真机对了：两套 RP asset 能力不同

2026-07-25 盘点厂包可用性时查出：`Assets/Settings/PC_RPAsset.asset` 的
`m_RequireDepthTexture` / `m_RequireOpaqueTexture` 都是 1，而
`Mobile_RPAsset.asset` 两项都是 0；`QualitySettings` 里 PC 档
`excludedTargetPlatforms` 含 Android/iPhone，Android 默认档指向 Mobile。

后果：编辑器（Windows 平台）走 PC 档，屏幕扭曲（`_CameraOpaqueTexture`）、
深度投影贴花、软粒子淡出（`SoftParticles_ON` / `_FADING_ON` 关键字）都"看着正常"，
换到真机全部失去数据来源。**凡涉及这三类的验收结论，只有真机截图能作数**；
在文档里写这类结论必须标注真机是否已验。

推论方法：判断一个厂包层能不能用，先看它的 shader 采样了什么全屏纹理
（`_CameraDepthTexture` / `_CameraOpaqueTexture`），再看目标平台的 RP asset
有没有开对应开关 —— 比逐个试播快得多。详见
`docs/client/vfx_pack_integration.md` §二。

## P-36 Unity「假死」优先怀疑模态弹窗，而不是死锁

2026-07-25 两次「编辑器无响应、MCP ping 不回、CPU 归零」，第一次误判为域重载
死锁并重启了 Unity，第二次才看到真凶：**Script Updating Consent 模态框**
（RFX4 三个物理脚本用了 `Rigidbody.velocity` / `drag` / `FindObjectsOfType`
等改名 API，每次包重导入后域重载都会重新弹）。模态框霸占主线程消息循环，
外部工具的一切请求都会超时，从外面看与死锁完全一样。

判据：进程 `Responding=True`、CPU 停止增长、日志不再追加 —— 这三条同时成立时
**先让人看一眼编辑器窗口**，不要直接重启（重启会丢掉未保存的场景状态，且解决
不了问题，下次照弹）。

处置：选「Yes, for these and other files that might be found later」。窄选项
（just for these files）下次还会弹。厂包脚本的这类改名是机械且可复现的
（包可从 Asset Store 重新导入），因此这是「厂包目录只读」红线的既定例外。

补记（同日彻底关掉）：点 No 不省硬盘 —— 更新器是**就地改文件、不生成备份**，
弹窗里那句"建议先备份"是让人自己用版本控制兜底。既然点 No 只换来反复卡编辑器，
就直接把触发源改掉：`RFX4_PhysXSetImpulse` / `RFX4_PhysicsMotion` /
`RFX4_PhysicsForceCurves` 三处 `velocity`→`linearVelocity`、
`drag`→`linearDamping`、`angularDrag`→`angularDamping`。改完不再弹。
注意 `ParticleSystem.Particle.velocity` 同名但**没有**弃用，不要跟着改。

## P-37 体检工具的判据必须先证伪，否则"35 个有问题"全是假警

2026-07-25 首次跑全量 VFX 体检报出 52 件里 35 件有问题，逐条看下去 67 条
「材质空」全是误报，真问题只有 1 条。两个误判来源都出在"按 `Renderer` 通用写法
查材质"：

- `ParticleSystemRenderer.sharedMaterials` 第二槽是拖尾材质，**拖尾模块关着时
 本来就是 null**；要么只看 `sharedMaterial`，要么先判 `ps.trails.enabled`。
- 厂包普遍用一个空粒子系统当**容器节点**（`renderer.enabled=false`、
 `renderMode=None`），它没有材质是设计如此。

教训：体检类工具第一版跑出来的高数量级报警，先假设是判据错而不是资产烂；
拿一个具体条目 dump 出组件与渲染器状态验证过再动资产。修正判据后
52 件里真问题 1 件（`aura_ares_might` 藏了一层画不出来的厂包贴花）。

## P-38 尺寸归一的「参照基准」必须与特效当初被肉眼调好的那套布局同档

2026-07-25 给 49 个特效补挂 `VfxFitter` 时，参照卡宽取自
`RecalcForTeams(DesignHalfWidth, DesignHalfHeight, FangYuan, FangYuan)` = **2.041**，
而实际战斗（雁行阵）跑出来的卡宽是 **1.206**。于是全部特效被静默缩到 59%，
`aura_ares_might` 从 0.22 缩成 0.13。

根因：`StanceLayout` 的卡宽只取决于**交错 / 非交错**两种阵型 regime
（交错 = 方圆/却月/鹤翼，纵向可用跨度取 `spanBand`；非交错 = 雁行/经典 2×3，
取更小的 `CellHeight`），两档相差 1.69 倍。与分辨率、宽高比无关 ——
`RecalcFromCamera` 其实并不读相机尺寸，只用设计常量。

红线：归一化这类「把手调值换算成公式」的改造，参照值必须**实测**自那批资产
被调好时的运行环境，不能顺手拿一个看起来权威的设计常量。改造完必须在真实
场景里比对一件旧资产的最终 `lossyScale` 是否与改造前一致（等于 1 倍才叫中性）。

也记一条正面经验：这个 bug 是**特效画廊**（菜单 `GreekMyth/特效/特效画廊`）
第一次跑起来就当场暴露的 —— 批量改造后必须有一个「把全部资产在真实舞台上
逐件过一遍」的入口，否则这种全局性缩放错误在单点验收里看不出来。

## P-39 「厂包整包没效果」先查 playOnAwake，别急着判包不可用

2026-07-25 审核台里「彩色系列」132 件全部空白，差点整包否决。真因是这批
prefab 的粒子系统一律 `playOnAwake=false`（原设计等它自己的控制脚本或示例场景
触发），直接 `Instantiate` 后没人调 Play。补一次根级 `Clear + Play(withChildren)`
即全部正常。

配套三条同类判据（都实测踩过）：

- **不能只看粒子数**：`particleCount>0` 但屏幕空白，多半是尺寸被定径缩过头，
 或截图时机在特效已播完之后（详见下一条）。
- **静态取证要先定格**：MCP 分两次调用「先 Spawn、再截图」时，短效件早已播完。
 正确姿势是同一次调用里 Spawn → 对所有粒子 `Simulate(t, true, true)`（会暂停）
 → `ScreenCapture`，等于按 t 定格取证。
- **厂包"主件"不是散件,单点摆放必然演不出来**：`Prefabs/Effects/EffectN` 是一整套
 出手流程(自带位移 + 撞到碰撞体才生成自己的命中件),而 `EffectParts/EffectN_Collision`
 才是散件。把主件当散件摆在一个锚点上,它会沿自己的 local forward 飞出舞台、命中件
 永不生成,看起来就是"这包没有可用组件"。要演出来必须给两点(起点/目标)、写 `Target`、
 把 `Distance`/`Speed` 从厂包默认值(30 / 1)改成本项目尺度,并在落点放碰撞体 ——
 我们的舞台地面是特意去掉碰撞体的底图,全场一个碰撞体都没有。
- **厂包脚本会在 `OnEnable` 里抛**：`RFX1/RFX4_ShaderFloatCurve` 与
 `ShaderColorGradient` 只在 `Awake` 建 `MaterialPropertyBlock`，某些实例化路径下
 `OnEnable/Update` 先跑而抛 `ArgumentNullException`，异常从 `Instantiate` 冒出来
 会把调用方（审核台）一起带停。已把这 4 个脚本改为惰性初始化，并给审核台的
 实例化加 try/catch —— 批量过资产的工具，禁止让单件把整轮审核带崩。

## P-40 Play 模式下 Unity 会推迟编译，改完代码不停 Play 就是在测旧代码

2026-07-25 改画廊定径规则后连续两轮"改了没用"：反射读回的字段值明明是新设的，
表现却完全是旧逻辑。真因是 Unity 默认不做 Recompile-And-Continue —— **Play 模式
中触发的 refresh 只把编译排队，脚本仍是进 Play 那一刻的版本**。

固定动作：改了 `Assets/**` 下的运行期脚本 → **stop → refresh → 等编译 → play**。
判据：在 Play 里调新加的方法/字段若抛 `MissingMethodException`，或行为与新代码
明显不符而字段值又对得上，就是这条。别再去怀疑反射、序列化或 MCP 通道。

## P-41 「特效在卡牌上看不见」先查定径基准，别查渲染

2026-07-25 Effect31 在卡身锚点下完全不可见，一路查了 URP 不透明贴图、场景灯光、
深度代理是否误裁 —— 全是无关项。真因：它的核心包围盒是 2.9 宽 × 8.7 高的**柱状**，
而当时的定径规则按"地面投影最大边缩到卡宽"，把整件缩到 1.15m 宽，
**比卡还窄、整件缩进卡牌轮廓里**，屏幕上就读作"没东西"。

排查顺序（省时间）：
1. 先把同一件**原样**（不定径、不换父、scale 1）扔到棋盘中心 —— 能看见就是
 我方处理链的问题，看不见才去查管线/材质；
2. 再打印 `renderer.bounds.size` 与定径后的 `localScale`，跟卡面尺寸对比；
3. 立体柱状件按**壳本体**定径（见 P-42）；贴地件才按地面投影定径。

## P-42 厂包件的"尺寸"不是一个数：整件包围盒会随时间暴涨，别拿它定径

2026-07-25 罩身件连错三轮，每轮都是定径基准选错：

| 基准 | 为什么错 |
|---|---|
| 整件渲染器包围盒 | 混着**世界空间模拟 + 重力飞散**的碎石/烟。Effect31 本地高度 0.12s 时 ~10.5、后期 ~30，量早了量晚了都不是壳的尺寸 |
| 多时刻包围盒并集 | 更糟，直接量成"整场余波" |
| 自带碰撞体 | 那是**被罩住的人形**（2.0×2.5），不是罩子（2.89×8.66），差 3.5 倍 |
| 正解：件里 Y 向最高的那个渲染器 | 壳必然是最高部件；壳类粒子的 SizeOverLifetime 通常是关的，发射后尺寸不变 |

配套三条：
1. **同一帧读 `Collider.bounds` 是过期值** —— 物理场景默认延后同步，
 刚 `Instantiate` + 摆位就读会漏掉子节点的本地偏移（实测底面差 0.25），
 须先 `Physics.SyncTransforms()`；
2. **不要量第二遍**来对齐位置：粒子每帧都变，第二遍是另一个时刻的盒子。
 缩放绕 transform 原点做，按比例**解析推算**新的 min/center 即可；
3. **不要为对齐某条边而非等比缩放**：8.66 高的壳压到 ×0.29 会变成薄饼，
 一眼就看出"这不是竖直的罩子"。等比 + 允许超出，才是厂包该有的比例。

## P-43 特效标准化不要做成「给人点的 GUI 晋升」

现象：一度要做画廊 U 入队 + 菜单「晋升队列」，把标准化变成用户操作流。
根因：把「审核台」和「交付流水线」绑死；用户实际要的是**点名后 AI 按纪律交付**。
正确做法：权威见 `docs/client/vfx_standardization.md`——人指出画廊/厂包件，
AI 默认 Copy→清洗→Fitter/Ground→guide→Profile；禁止 Profile 指包目录；
批处理 Standardizer / Wire* 脚本可留，**不要**晋升队列 GUI。

## P-44 阵型站位革新：双档卡宽与旧阵名一次性废止

2026-07-26 用矩形六等分替换逻辑圆径向站位后：

1. **卡宽只剩一档**（设计布局 ≈1.889）。旧「交错 2.041 / 非交错 1.206」
   （P-38）与阵型 regime 绑定的算法已删。改 `BattlefieldLayout` / `Recalc`
   后必须跑「GreekMyth/特效/标准化」回填 `BakedBasis`（本次回填 49 件），
   否则运行时 `CardWidth/BakedBasis` 会把特效整体放大或缩小。
2. **阵名**：却月/鹤翼/旧方圆{1,5,6} 禁止再写进现行文档与注释；对应现称
   雁行{1,2,6} / 锥形{2,4,6} / 箕形{1,5,6}；新方圆为 {3,4,5}。
3. **新脚本无 .meta**：Unity 未导入前其它文件会报 `BattlefieldLayout does not
   exist`；force refresh 生成 meta 后再编译。勿以为是命名空间写错。
4. **阵型禁止传字符串**：`TeamSetup.formation` 是只读属性（按站位 detect）；
   配将 / config / manual 入口均不接受 formation 字段。改站位即改阵型。
5. **站位矩形禁止手拍设计常量**：首版拍了 W=23/D=17（近缘 −7），而相机
   45°/焦段 55 只拍到 z≥−2.97，A 队后排整排出屏。定案是反过来：先解析求
   「相机正好拍全」的地面板（屏底视线落地 = 近缘、屏侧边在接缝处 = 半宽），
   分区在这块板上做。凡"世界矩形要在屏幕上完整展示"的布局，一律从视锥
   反算矩形，不许先定矩形再指望相机拍全。另注意卡牌向后倾，判可见要算
   **卡顶**是否越过屏幕上沿，不只是脚点。

## P-45 弹道裂地「几道独立」= CrackGroup 朝向被烤歪

现象：物理群攻 T1 `ground_crack_path` 三段不跟弹道，读成互不相关的独立裂地。
根因：G4 `AddComponent<GroundCrackDecal>` 立刻 OnEnable，此时 `RandomizeSpin`
仍是默认 true；在已平躺（俯仰 90°）上读改 `localEulerAngles` 触发万向节锁，
错误 yaw（实测 path 偏 52°）被 `SaveAsPrefabAsset` 烤进 prefab。之后再设
`RandomizeSpin=false` 已晚。调用方 `YawAlong` 正常，叠在歪子节点上等于白给。
正确做法：① 组装时先 `SetActive(false)`，配完 `RandomizeSpin` 等字段、写死
`Euler(90,0,0)` 再激活；② OnEnable 禁止读改 euler，随机自旋用
`Quaternion.Euler(90,0,z)`；③ 改组合器后必须重跑 G4 重生三档 prefab。

## P-46 「特效完全不显示」先查有没有接线，再查渲染

现象：单体弹道技能全程无裂地，误报为"渲染坏了"。
根因两条，都不是渲染：① 裂地只接在 `PlayAoeCenter`（还要求互异目标≥2），
单体技能走 `PerSegment`，整条链没有任何裂地调用 —— 属于**静默缺失**；
② 实例存活时长按倍速换算（`ctx.Scaled`），而 `GroundCrackDecal` 的
淡入/驻留走真实秒，4 倍速下刚淡入就被回池，看起来也像"没播"。
分辨手段：不要靠肉眼看战斗。先数**池里有没有实例**（有实例＝接线通、问题在渲染
或时长；无实例＝压根没调用），再用探针（`GroundCrackProbe`）把件摆到**空地**上
强制点亮截图。本次靠"池里 path×9/hit×6 但画面无感"一步定位到时长换算不同步。
方法论：任何"按倍速缩放生命期"的特效，其内部动画必须收同一把尺
（本次加 `DurationScale`）；两把尺就是迟早会撞的坑。
系统性修法：把触发判据与三档入口收进 `GroundCrackService` 单一模块，
新增演出模板时漏接会集中在一处而不是散落各模板。

## P-47 编辑器组装工具激活组件会把「运行时初态」烤进 prefab

现象：裂地三档触发日志全对、池里有实例，但面片 alpha 永远 0，肉眼全透明。
根因：G4 组合器为了让 `OnEnable` 里的平躺基准生效，存 prefab 前
`SetActive(true)`——`OnEnable → Apply(0)` 把所有 SpriteRenderer 的
alpha 清成 0，`SaveAsPrefabAsset` 原样烤进资产；运行期 `Collect()`
把这个 0 当基色缓存，`基色a × 动画a` 恒等于 0。
教训：编辑器工具里**激活过带生命周期组件的对象，存盘前必须把被
OnEnable/Update 改过的可视状态复原**（本次：颜色 alpha 归 1、旋转归
平躺基准）。运行时侧再加自愈（基色 alpha≈0 视作满）作第二道防线。
另：用 MCP 隔秒查瞬态动画毫无意义——工具往返动辄几十秒，动画早播完；
要么把驻留临时拉长再查，要么在同一次 execute 里查。

## P-49 「参考某 EffectN 的观感」必须走标准化协议，且要逐层判定

坑：把「第 3 档参考 Magic Pack Effect8 的熔岩裂开」理解成「照感觉把亮度调高」。
这既复现不出观感，也让后来人不知道参照物与差距在哪。
正解（已写进 `vfx_standardization.md` §二 与 `global_rules` §四.8）：
参照类需求与直接点名接件**同级触发标准化协议**，必须先定件、看清层构成，
逐层给出「晋升 / 替代」去向表并落到机制文档。Effect8 实例：粒子/扭曲/雾/光
可直接晋升（→ `ground_lava_bloom`，G12），只有 `KriptoFX/RFX1/Decal` 贴花层
URP 画不出必须替代（自研 `GroundCrack.shader` 推 `_Growth` 复现厂包推
`_Cutout` 的手法）。结论永远是「逐层判定」，不是整件一刀切可行/不可行。
附带：`EffectN` 与 `EffectN_Collision` 是不同件（主件含地面层、后者只是命中碎件），
不得互相替代。厂包件里常混着 `RFX1_Target` / `TransformMotion` 等弹道脚本，
晋升时必须摘掉，否则与池化播放抢 transform。

## P-48 裂地存续不吃倍速（地面痕迹类特效的通例）

4 倍速下若把裂地生命期同比压缩，整段只剩 ~0.4s，等于没播。裂地不
yield、不阻塞节拍，存续与播放倍速解耦（只吃 DurationMul 放慢倍率），
快进时痕迹以常速淡出即可。凡「留在场上的痕迹」类特效同理。

## P-50 装饰性子面片必须探出主体轮廓外，否则等于没加

给弹道裂地加毛刺分叉时，毛刺摆在主缝中线（y=0）、长度只有主缝 0.2~0.4，
从画面上完全看不出分叉 —— 整根埋在主缝那条带子里，用户直接反馈"没看到毛刺"。
凡「在主体旁边加细节」的活（毛刺、飞溅、边缘碎屑），落位第一原则是
**必须有一段伸到主体轮廓之外**；先算它能露出多少，再谈角度和数量。
验收也要配套：拉一台临时相机贴近拍特写，远景全局图看不出这类差别。

## P-51 「档位」不要把形状和烈度混成一张表

裂地早期把「弹道/命中/全局」当三档，同一张表里既有遮罩形状又有亮度与尺寸。
后果是想让某个技能"更猛"只能新造一档、并为它另烘 prefab 变体。
正解是拆成正交维度：**形状骨架**（烘制期定型，件数=骨架数）×
**烈度档**（运行期整档写入）+ **大小参数**（运行期）。
判据：如果新需求只是"更大/更亮/更久"，就绝不该产生新资产。

## P-52 粒子「无贴图＝软方块」在俯视舞台上不是软的

Unity 默认粒子材质没有贴图时是白方角 billboard。俯视近 3D 舞台上它不会
「糊成一团烟」，而是立着/平躺的半透明方块，用户直接叫「方块贴图」。
地面烟雾必须自带软圆（或烟雾）alpha 贴图，并优先 HorizontalBillboard 贴地；
URP 下用 `URP/Unlit` 透明吃贴图 alpha，不要假设 `Particles/Unlit` 一定乘 alpha。

## P-53 parent_seq=0 的「独立台词组」会被阵亡事件挤到末尾

`GroupByGroupId` 按 group_id **首次出现序**排列播放单元；同组后续事件
（即便 seq 更晚）仍追加进已出现的组。若台词 `parent=0` 另开组插在
`damage` 与 `hero_defeated` 之间，阵亡写回伤害原组 → 播放序变成
「整段出击含死亡 → 再播台词」。踵之弱必须 `parent_seq=damage_seq`，
由 `TraitLineExtract` 抽成夹在伤害与后续之间的独占组。

## P-54 Unity `Mathf.SmoothStep` ≠ HLSL `smoothstep`

`Mathf.SmoothStep(from, to, t)` 是把 t∈[0,1] 映射成 from→to 的平滑插值；
HLSL `smoothstep(edge0, edge1, x)` 才是「低于 edge0 为 0、高于 edge1 为 1」。
自烘裂缝遮罩误用前者当边缘函数，整图 alpha≈0，弹道裂地完全看不见。
数据贴图边缘一律自写 `EdgeSmooth`，并在写入前断言 maxA≥0.5。

## P-55 「最高档」不能靠叠一张自带形状的外部贴图来堆

裂地档 3 曾在自研裂缝上叠厂包熔岩层 `ground_lava_bloom` 并把亮度堆到 4.5，
人工验收判为「地上贴了张发光图，完全不是档 1/2 的自然裂缝」。
分档是**同一语言的连续台阶**：高档应沿用同一骨架（放大 + 加宽 + 加亮 +
沿缝色彩渐变），外部件只有在**不自带轮廓**（纯加光/纯噪声）时才可叠。
新增最高档前先问：把它和低一档并排放，读起来是同一种东西吗？

## P-56 「参数正确」不等于「不僵硬」：匀速 + 全场同参数 = 机关动画

裂地一次群攻同时出 3~5 处，若推进速度、起火时刻、停留时长全按表里的常数走，
每处播的是同一段动画的复读，人工验收直接判「僵硬」。自然现象类演出必须给
**每次出场现摇的抖动**（本项目 `GroundCrackDecal.Roll`）+ **非匀速曲线**
（脆性扩展是走走停停）+ **层间滞后**（附属层晚于主体）。
同理：几何骨架靠「N 条等长直线」一定读作图标，要递归分叉 + 粗细起伏。

## P-57 弹道裂地按墙钟等分 ≠ 跟球走

现象：落点用了弹道实时投影，但起裂时刻仍是 `WaitForSeconds(flight * s/N)`，
缓动/错峰下裂缝领先或落后球；命中裂地又在模板里先于 HitKey 单独 PlayHit，
观感「先裂后炸」或双拍。
根因：把「进度用来算落点」和「进度用来触发」拆开；命中裂地未收进 SettleDamage。
正确做法：`PlayPath` 每帧读弹道进度，越过 1/N…阈值才戳；`PlayHit` 只在
`SettleDamage` 与 HitKey 同帧；模板勿再单独 PlayHit。

## P-58 绕身罩存在时受击抖动会拖罩乱晃

现象：战神之勇等 `shroud_*` 钉定位圆跟随，受击 `DOShakePosition` 把卡牌抖开，
罩身 LateUpdate 追着抖，读作「罩在甩」。
正确做法：`UnitAuraService.HasShroud`（=`VfxShroudPresence.IsPresent`）为真时
`HitReact` 只红闪；**渐隐收干净后 IsPresent=false，恢复抖动**（勿用「状态仍挂着」当闸）。

## P-59 命中裂地随机错峰会拆开命中拍

现象：SettleDamage 同帧调了 PlayHit / HitKey / HitReact，但裂地晚出 0~0.22s。
根因：`GroundCrackDecal.Roll` 的 `_startDelay` 本为弹道多段去齐射感，命中档也吃了。
正确做法：Impact `_startDelay=0`、`FadeIn=0`、GrowTime≈HitReact；错峰只留给 Path。

## P-60 战神之勇偶数隐后奇数再显「残缺」

现象：第 3 回合（奇数）绕身只剩薄雾/半透壳，不像画廊完整件。
根因：渐隐把粒子 `startColor` alpha 压到 0 后 `SetActive(false)`；再显时
`Cache()` 把已压暗颜色锁成新基色。
正确做法：基色只锁一次；隐前 `RestoreBases`；再显只刷新组件引用不重锁基色。
回合中途挂载按 `_currentRound` 奇偶立刻 `SetShown`。

## P-61 势能火随行动窗灭 ≠ 回合计数单元

现象：火苗刚起，换人行动就灭；用户要求「回合内累计、回合结束才清」。
根因：清零挂在 `action_start`，火挂在 `ActionPause` 渐灭。
正确做法：`round_start` 全体静默清零；火仅回合边界渐灭；行动切换只留节奏停顿。

## P-62 「起裂时刻跟球」不等于「生长跟球」

现象：P-57 已把**起裂时刻**改成读弹道实时进度，观感仍是「球先到、缝后裂」。
根因：只管了戳缝的时刻，贴花**生长仍走自己的时钟**（GrowTime 0.22s + 出场
错峰 0~0.22s）。末段在弹道抵达那一刻才刚起裂，等它长完，命中特效/受击抖动/
命中裂地早已过去半拍——同步的是「开始」，不是「结束」。
正确做法：时间轴收进唯一真源 `StrikeSync`（飞行段广播进度 → 抵达 → 命中拍），
弹道贴花切 `EnableFlightDriven`，第 i 段生长 = `InverseLerp((i-1)/N, i/N, 进度)`，
末段推满那一刻＝弹道抵达＝`SettleDamage` 同帧。驱动式必须把 `_startDelay` 清 0，
且进度到顶要直接给足 `full`（Burst 抖动在末端会差一口气，那口气就是半拍）。
教训：对「同步」类需求，先问同步的是**起点还是终点**；跨模板的时序一律收成
一个可 Attach 的同步器，别在各模板里 `WaitForSeconds` 拼。


