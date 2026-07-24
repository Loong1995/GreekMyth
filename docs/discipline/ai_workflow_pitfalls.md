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
异阵对打必须**逐队** Detect，禁止两队站位并集推断（并集会落入 Grid2x3）。
