# 特效标准化协议（画廊点名 → AI 标准化 → 加载接线）

> **权威**：人在画廊里**指出**要用哪件厂包表现后，AI **默认按本纪律**完成
> 标准化并接到可加载 key / 战法演出——**不依赖任何 GUI 晋升工具、不要求
> 人点菜单队列**。
>
> 配套背景：[vfx_pack_integration.md](vfx_pack_integration.md)；
> **查谁用什么特效（总索引）**：[vfx_config_index.md](vfx_config_index.md)；
> key 登记：[assets_upload_guide.md](assets_upload_guide.md)；
> 注册点：[extension_points.md](../discipline/extension_points.md)；
> 坑的完整叙述：[ai_workflow_pitfalls.md](../discipline/ai_workflow_pitfalls.md)
> P-33 / P-38 / P-64 / P-65 / P-66 / P-67 / P-68 / P-71。
>
> 2026-07-26 定稿；2026-07-27 依单挑三件实战全面修订，并将落盘统一收口到
> `VfxPackStandardizer` 流水线（本版）。本文件 ≤500 行。

## 〇、第一原则：先证明资产在盘上，再谈观感

「特效完全没效果」的第一嫌疑永远是**标准件根本不存在**——`VFXManager`
对缺失 key 的回退是一个 48px 占位色块，它「有出东西」，极易被误读成
「特效没触发」，然后把排查引向时序/参数/池化，全错（P-66/P-67 实翻过车，
连续两轮）。因此：

1. 验收清单第一条永远是「`Resources/ClientBattle/VFX/<key>.prefab` 在盘上」。
2. 凡是"代码引用了必须由编辑器脚本生成的资产"，接线脚本必须带
   `[InitializeOnLoadMethod]` **自愈检查**：缺件即自动补齐（幂等），
   不允许依赖人点菜单。参考实现：`WireDuelStageVfx.AutoHeal`。

## 一、画廊 ≠ 运行期：七条已知差异（每接一件都要对一遍）

「画廊里挺好、接进去差一大截」不是素材问题，是**两条链路本来就不同**。
2026-07-27 逐行对齐后固化。前五条已在框架层修平（前提是按 §四 挂对组件），
后两条是必须承认的预算差。

| # | 画廊 | 运行期 | 现状 |
|---|---|---|---|
| 1 | 播**原料**（脚本/贴花/灯/音源全在） | 播**标准件**（摘 Projector、摘 RFX Decal、限灯删音源） | 贴花**不可逆**（URP 画不出，P-33）；灯是主动砍的 |
| 2 | 每次 `Destroy`+`Instantiate`，驱动脚本 `Awake/Start` 每次跑 | 池化复用**不重跑 Awake/Start**；且 `Prewarm` 让**第一次**播就已是复用态 | 已修：带 `RFX*` 驱动脚本的件挂 `VfxFreshInstance` 绕过池 |
| 3 | 按了「C 键定径」缩到圆直径 | `VfxFitter` 只做随卡宽浮动，**不改原生尺寸** | 已修：`VfxCircleFit` |
| 4 | 播完自然收尾，中途不砍 | `RecycleAfter` 曾到点直接 `SetActive(false)`＝拦腰砍断 | 已修：先 `StopEmitting` 再等余烬散尽（上限 1.2 s） |
| 5 | 起播只在根级 `Play(withChildren)` | 曾对**每层**都 Play，子发射器相位被打乱 | 已修：只在「最上层」粒子系统起播 |
| 6 | `RescueIfBuried` / `LiftPackSpawns` / 弹道 Target-Speed 反射接线 | 部分 | 埋地救援已进 `VfxCircleFit.RescueIfBuried`；后两条接厂包**弹道主件**时仍需补 |
| 7 | K 键 **0.25× 慢放**（厂包出手件整段仅 0.9 s，审核基本必开） | 1× | **预算差，不可修**。要接近就得给足真实时间（如单挑顺序播）或局部慢放 |

**验收时先自问第 7 条**：你在画廊里拍板的那个印象，是不是慢放下的？
是的话，1× 下永远达不到，得用「给足时间」或「降低期待」解决，不是继续调参数。

## 二、两层货架（勿混淆）

| 层 | 在哪 | 用途 |
|---|---|---|
| **原料** | 厂包目录（画廊里预览的那份） | 只审观感；**禁止**被 `PerformanceProfile` 直接引用 |
| **标准件** | `Assets/Resources/ClientBattle/VFX/<key>.prefab` | 真机/战报可加载；尺寸自适应；池化或绕池（§四） |

画廊 = 选片台。点名原料 ≠ 已上线；上线必须有同名 Resources 标准件（§〇）。

## 三、触发句与默认义务（工作纪律）

人说出下列任一意图时，AI **不得只口头答应或只改 Profile 指包路径**，
必须按 §四 清单做完（或明确列出卡点）：

- 「用画廊里的 EffectN / 某某包某某件」
- 「把某某接到某某武将/战法/命中/光环」
- 「标准化某某并加载」
- **「要 EffectN 那种效果／参考 EffectN 的观感／做到接近 EffectN」**
  —— 点名一件厂包表现当**目标观感**，与直接点名接件同等触发本协议。
  哪怕最终一像素都没用上厂包资产，也必须走 §四：先定件、看清层构成、
  逐层判定「可迁移 / 需替代」，再落标准件 + 登记 + 接线 + 验收。

> 红线（2026-07-26 定）：**禁止把「参考某 EffectN」理解成「凭印象手搓一个像的」**。
> 参照类需求的额外交付物：定件记录里写明**参照件路径 + 逐层去向表**
> （哪层直接晋升、哪层因管线不可用被替代、替代方案是什么），
> 并把该表落到对应机制文档（如 `ground_crack_language.md`、`duel.md`）。

**默认交付物**：

1. Resources 下新标准件 prefab（按 §四.3 全步骤）
2. `assets_upload_guide.md` 登记该 key
3. `PerformanceDatabase` / 对应 `PerformanceProfile` 字段写入 key（任务若只说
   「先入库不接线」则可跳过本步，但须在回复里写明）；
   与武将无关的全局演出件（如单挑三件）例外：key 放对应演出配置
   （`StagePerformanceConfig`），不进按武将/战法查表的 Profile
4. `docs/dev/changelog.md` 一行
5. 用 unityMCP `execute_code` / 菜单体检**逐项验证成品**（§四.5），
   不是只看接线脚本的 log

**禁止**：

- 做「晋升队列 / 入队按钮 / 给人点的标准化 GUI」
- `PerformanceProfile` 序列化引用 `Assets/KriptoFX/**` 等包目录
- Play 模式里改 Prefab 资产
- 整包不拆层直挂；保留厂包深度贴花节点当「地面效果」
- 演出代码硬编码 key 或路径（走 Profile 三级查找或演出配置常量）

## 四、AI 标准化检查清单（按序执行）

### 4.1 定件

- 序号→prefab 的换算**必须离线复算**（`battle/tools/_gallery_index_dump.py`，
  照抄画廊：包 1＝Resources Ordinal；其后＝Launcher.Packs 过滤排序），
  凭印象数必错一两位。
- **包号以人点名为准**。[1/8]=我方标准件；**禁止**「分母 61→Magic=2/8」
  覆盖口头包号（标准件现亦约 61 件，P-71）。点的是 1/8 且 key 已在
  Resources → 只改接线，勿再 Standardize 覆盖。
- 记录：包名、prefab 名、Asset 路径、建议锚点（身/脚/弹道/罩身/地面）、用途。
- **看清层构成再动手**（`battle/tools/_prefab_layer_dump.py` 可离线出报告）：
  厂包件常是「粒子 + 光 + 扭曲 + 投影贴花 + 音源」混装，逐层列出 →
  标注可迁移/需替代。参照类需求必须产出这张表。
- **识别投射物运载器**（2026-07-27 定案，最大的一类原料错配）：
  厂包主件 `EffectN` 常是投射物系统——母件粒子层按**移动距离**发射
  （`rateOverDistance`，静止＝零粒子），位移脚本飞到碰撞点后**实例化
  `EffectN_Collision` / `EffectN_Explosion` 子件**，画廊里看到的那次爆炸
  就是子件。于是按用途选原料：
  - **定点用途**（锚点/卡面/地面原地播）→ 正确原料是**碰撞子件**。
    钉住母件或删其位移驱动都是零粒子，不是"缩水"是零。流水线
    `ResolveAnchorSource` 会沿位移脚本的 `EffectsOnCollision`/`EffectOnCollision`
    字段自动改选，无须人工判断。
  - **弹道用途** → 用母件全套，保留位移驱动，走 Target/Distance/Speed 反射接线。
- 罩身件走 `VfxShroudFitter` 规格。

### 4.2 起 key

- 路径：`Assets/Resources/ClientBattle/VFX/<key>.prefab`
- 命名（snake_case 前缀）：`hit_` 命中 / `proj_` 弹道 / `aura_` 光环挂身 /
  `cast_` 前摇出阵 / `ground_` 地面（必须 `VfxGroundLayer`）/ `shroud_` 罩身。

### 4.3 落盘（唯一入口：`VfxPackStandardizer.Standardize(src, key, usage)`）

2026-07-27 起，落盘一律走统一流水线（`Assets/Editor/GreekMyth/VfxPackStandardizer.cs`），
**接线脚本只允许是清单**（参考 `WireDuelStageVfx`：三行 (源, key, 用途) + AutoHeal +
菜单）。禁止再裸写拷贝脚本——单挑三件连环事故（原料错配/Play 烤入/孤儿驱动）
全是裸写的结构性产物。流水线内部按序做（每步的"为什么"见源码注释）：

1. **拒绝 Play 模式**：Play 中 `InstantiatePrefab` 会进运行场景，脚本 Awake 的
   运行期突变（如 `RFX*_PerPlatformSettings` 按平台降配发射率）会被
   `SaveAsPrefabAsset` **烤进成品**——实锤过一次，Effect28 三层发射率被打了 0.75 折。
2. **原料改选**（`ResolveAnchorSource`）：定点用途遇投射物运载器自动改选碰撞子件
   （§四.1）。
3. **拷贝**：`AssetDatabase.CopyAsset`（复制品天然是独立 Regular prefab），之后全程
   `LoadPrefabContents` 纯资产编辑——不进场景、不跑任何脚本。不改包内原件。
4. **清失效脚本空槽**：`RemoveMonoBehavioursWithMissingScript` 逐节点跑。
5. **摘场景污染件**：`WindZone`（场景级力场，吹歪别的特效）、`RFX*_CameraShake`
   （直接晃 Camera.main，与 CameraShaker/StageCameraRig 打架）、
   `RFX*_PerPlatformSettings`（运行期不确定降配；移动端预算由流水线显式裁）。
6. **移动端裁剪**：音源全删、实时灯 ≤1 盏关阴影。
   **删组件必须连同同节点配对的驱动脚本**（`RemoveWithPairedDrivers`，
   按 Light/Audio/Wind 类型名子串匹配）：曲线脚本在 Awake 里直取同节点组件，
   取不到抛 `MissingComponentException`，异常经 `Instantiate` 传出 → `PlayAt`
   抛错 → **整段演出协程当场死掉**（P-68）。只删**配对**的脚本、不是同节点全部
   RFX 脚本——根节点常挂 `RFX*_EffectSettings` 主驱动，一锅端等于删掉整件的大脑。
   *不要改成「禁用而非删除」*：曲线脚本每帧把 intensity/enabled 写回来，等于没裁。
7. **摘死层**：`Projector`、`KriptoFX/RFX1|RFX4/Decal` 贴花节点（URP 画不出，
   P-33）。**摘掉的观感层必须记录替代方案**（例：Effect8 地面焦痕 →
   自研裂地 `GroundCrackService.PlayHit`）。
8. **掐空转前摇**（`NormalizeStartDelay`）：所有层 startDelay **同时前移**到最早
   会出图的层从 0 起播（整体平移保层间结构）。判"会出图"只认 burst/rateOverTime。
9. **用途组件**：`Ground` → `VfxGroundLayer`；一律挂 `VfxCircleFit`
   （基准=投影圆，地面件开 `RescueIfBuried`）；残留 `RFX*` 脚本 → `VfxFreshInstance`。
   尺寸组件互斥原则不变：`VfxCircleFit` / `VfxGroundLayer`（尺寸归裂地档位时摘
   CircleFit，见 `StandardizeLavaBurst`）/ `VfxFitter` 三选一。
10. **落盘后验证**（`Verify`，一件不过整批报错）：
    ① missing script = 0（P-67）；② **可见性**——至少一层自主发射
    （burst/rateOverTime）或有非粒子渲染器，全否＝运载器选错原料；
    ③ **驱动配对完整**——带 Light/Audio/Wind 字样的 RFX 脚本同节点必须有对应组件
    （编辑器冒烟 Instantiate 挡不住这类问题：厂包 Awake 只在 Play 跑，必须静态查）；
    ④ 能被 Instantiate。
11. **接线清单带 `AutoHeal`**（§〇）。

弹道用途暂不在流水线内（保留位移驱动 + Target/Distance/Speed 反射接线，
接第一件弹道件时把该分支补进流水线，勿另起脚本）。

### 4.4 登记与接线

1. `assets_upload_guide.md` §特效表加一行（来源包 + 逐层去向备注）。
2. key 写入 Profile 字段或演出配置（§三 交付物 3 的区分）。
3. 罩身：`VfxShroudFitter.Fit` + `VfxShroudFollower`；通用类禁止裁层，
   个性裁层进各技能 Wire 名单。
4. 加载只经 `VFXManager.PlayAt/PlayOn(key)`。顺序演出要「播完再走」的，
   等待时长用 `VFXManager.EmitWindow(key, cap)` 运行期探（真实秒，
   **不过 `ctx.Scaled`**，必须配上限），不写死。两种形态自动区分：
   有一次性层取 `delay+duration`；全循环层取 `delay+startLifetime`＝成形时长
   （循环层的 `duration` 是**周期**不是时长，当结束时刻用会得到两不像的数）。
   **上限要照实测值定**：低于素材窗口就会截断，症状是"没播完就进下一拍"。
5. **交拍收势**：含循环层的件在切拍时对实例 `VFXManager.StopEmitting`
   （只掐新粒子、留余烬）。循环层不会自己停，任其全速发射到回收会读作
   "炸到一半被打断"。**顺序感靠收势，不是把等待拉长**。

### 4.5 验收（完成定义；用 unityMCP 逐项验成品，不是看 log）

- [ ] **Resources 存在该 key（第一条，先于一切观感讨论）**
- [ ] `GreekMyth/特效/体检 标准件流水线四项` 全绿（missing/可见性/驱动配对/可实例化，
      即流水线 `Verify` 四项，覆盖全部标准件不只本次新件）
- [ ] 无 Projector / 死贴花 / 品红
- [ ] 尺寸组件三选一挂对（§四.3-9）；画廊定径拍板的必须是 `VfxCircleFit`
- [ ] 含 `RFX*` 驱动脚本 → `VfxFreshInstance` **实际在成品上**（不是只在日志里）
- [ ] 实时灯 ≤1 盏无阴影、`AudioSource=0`、无 `WindZone`
- [ ] 定点用途的件：原料是碰撞子件而不是钉住的运载器（§四.1）
- [ ] 摘掉的观感层有替代方案且已记录
- [ ] 对 §一 七条差异逐条过，尤其第 7 条（是不是慢放下拍的板）
- [ ] guide 已登记；key 已接线（或写明「先入库」）；changelog 已写

## 五、标准件运行期属性（实现约束）

1. 尺寸自适应唯一入口：`VfxFitter` / `VfxCircleFit` / `GroundCrackDecal` /
   `VfxShroudFitter`；演出禁止按机型写死 `localScale`。
2. 排序：非地面出池 `EnsureVfxSorting`（Renderer 基类全遍历）；
   地面 `VfxGroundLayer` 豁免。
3. 池化：`OnEnable` 复位自身状态；粒子由 Manager 重启（只在最上层起播）；
   `VfxFreshInstance` 件绕池（Rent 新建 / Release 销毁 / CancelAll 销毁）。
4. **会被序列化进 prefab 的 MonoBehaviour 必须独立成文件**（类名＝文件名），
   哪怕是空标记类；只在运行期 AddComponent、从不落盘的类才可同文件寄生（P-67）。
5. ClientBattle **不**引用厂包运行时程序集；反射仅允许 Test/画廊，禁止热路径。
6. 编辑器接件写成 `MenuItem` + 自愈检查的**可重跑脚本**，参数在代码常量里。

## 六、坑谱速查（症状 → 先查什么）

| 症状 | 先查 | 坑号 |
|---|---|---|
| 完全没效果 / 只有小色块 | key 对应文件在不在盘上；接线脚本跑过没有 | P-66/67 |
| 文件在、也实例化了，但**一颗粒子不出** | 是不是投射物运载器当定点件（粒子全 rateOverDistance）；体检②可见性 | P-68 |
| 原本好好的，某次裁剪后**整段演出的特效全没了** | 控制台搜 `MissingComponentException`：删了组件却留下同节点配对驱动脚本；体检③ | P-68 |
| 成品比源件**发射率/粒子数矮一截** | 接线是不是在 Play 模式跑的（运行期降配被烤进 prefab） | P-68 |
| 能看见但明显不如画廊 | 有没有 `VfxFreshInstance`（驱动脚本残留态）；是不是慢放下拍的板；**调用方回收时长是不是写死的**（该按 `EmitWindow` 给足，Magic 碰撞件要发射 1~2s） | P-66 |
| 糊满全屏 / 大得离谱 | 尺寸组件是不是 `VfxFitter`（它不改原生尺寸），该换 `VfxCircleFit` | P-66 |
| 地面痕迹没了 | 是不是被摘掉的 Decal 层，替代方案接了没 | P-33 |
| 组件加了但运行期不生效 | 成品 prefab 上 missing script 数；类是否独立成文件 | P-67 |
| 特效一帧消失 | 回收是否走了 StopEmitting 宽限（老代码直接 SetActive(false)） | — |
| 顺序演出"没播完就进下一拍" | ①件有没有空转前摇（`startDelay`）②等待上限是否低于实测窗口 ③循环层有没有在切拍时收势 | — |
| 探到的时长离谱地大 | 件里有循环层，`duration` 是周期不是时长 | — |
| 特效不在落点、粒子乱跑后才炸 | 定点用途却用了运载器母件——改选碰撞子件，别去删位移驱动 | P-68 |
| 别的特效被莫名吹歪 | 某件里带 `WindZone`（场景级力场） | — |
| 第二次播放形状不对 | 子发射器是否被逐层 Play 打乱；池化残留 | — |
| 地面圆对不准 / 溢出一圈 | 用的是定位圆还是投影圆（不同心不同径） | P-65 |
| 尺寸随机型漂移 | `BakedBasis` 是否按非交错设计卡宽回填 | P-38 |

## 七、与画廊的关系 & 入口速查

- 画廊继续用于人眼过片（锚点/慢放/标记可选）；**标准化不经过画廊按钮**。
- 画廊里每多一个「临时按键调整」（定径、抬 spawn、重启粒子、慢放），
  就多一处运行期必须复刻或明示放弃的隐性步骤——要么进标准件，要么进本协议，
  绝不能只活在工具里（P-66 教训）。

| 动作 | 入口 |
|---|---|
| 预览 | `GreekMyth/特效/特效画廊（一键）` |
| **标准化流水线（唯一落盘入口）** | `VfxPackStandardizer.Standardize(src, key, usage)` |
| 流水线四项体检（全量） | `GreekMyth/特效/体检 标准件流水线四项`（报告落 `Temp/vfx_audit.txt`） |
| 批处理清洗已有标准件 | `GreekMyth/特效/标准化 尺寸归一 + 清理残留` |
| 旧版体检（渲染层盘点） | `GreekMyth/特效/体检 全量 VFX prefab` |
| 单挑三件接线（清单参考实现） | `GreekMyth/特效/接线 单挑三件（出阵·加冕·溃败）`（带 AutoHeal） |
| 画廊序号复算 | `battle/tools/_gallery_index_dump.py` |
| 层构成报告 | `battle/tools/_prefab_layer_dump.py` |
| 加载 | `VFXManager` + Profile key / 演出配置 |
