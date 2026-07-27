# 单挑（duel）

> **本文是单挑系统的总索引**：规则、事件契约、服务端实现、客户端演出、
> 美术素材，全部从这里进入（§7 索引表）。改单挑任何一端都先读本文。
>
> 规则来源：决策 D-03 演进（2026-07-21 配对升级）；
> 演出重做见 §5b（2026-07-27，立绘出框 + 虚空展示屏 + flipbook 动作）。

## 1. 触发时点

- **仅第 1 局**开局：`game_start` 之后、所有战法（含准备回合神谕）之前，
  独立 DUEL 相位（`t.p=2`）。第 2 局及以后不再判定。

## 2. 参赛与初对

1. **参赛选手**：双方存活且有效武力 **>** 有效智力；队内按
   （武力↓，站位↑，hero_id）排序。
2. **序号对位**：两队同序号 `zip` 到 `min(lenA,lenB)` 形成初对。
3. **羁绊初对**：`weight∈{1,2}`（S1/S2，见 `docs/character/bonds.md` /
   `battle/bonds.py`）且分属两队的模板对，追加为初对。
4. 同一武将可出现在多对；同键 `(id_lo,id_hi)` 保留更佳（更小）羁绊 weight。
5. 任一方无参赛选手 → **不演绎**。

## 3. 入池概率与取对

对每条初对，武力差 \(d=|F_a-F_b|\)：

| \(d\) | 入池率 |
|---|---|
| 0 | 90% |
| 0~50 | 线性：`9000 - d×170` bps |
| ≥50 | 5% |

RNG：`duel_pair`（按初对确定性序逐条 roll）。

- **候选池非空**：按（羁绊 weight↑，无羁绊=99；武力差↑；id）排序，取第 1 对
  → **真决斗**。
- **候选池空、但有初对**：同序取第 1 对 → **固定叫阵-拒绝**（不 roll 胜负、
  无四维惩罚）。

## 4. 真决斗结算

叫阵方 = 该对武力高者（相等则 A 队侧）。

| 步骤 | 公式 | RNG |
|---|---|---|
| 拒绝 | 低武力方拒绝率 = `d × 8%`，封顶 80% | `duel_reject`（d=0 不 roll） |
| 胜负 | 高武力方胜率 = `50% + d`（百分点），d≥50 必胜 | `duel_win` |
| 惩罚 | 负者四维立即 -10（`attr_change scope=game`） | 无 |

性格**不**改写拒绝/胜负判定（约战机械表已废除）。

**台词**（`battle/voice_lines.py`）：按说话者 `template_id` 双池
（对方模板羁绊池 → `generic`），发 `trait_trigger`（`effect=duel_*`），
**挂在 duel 组内**（`parent_seq`→challenge/result）。文案权威
`docs/character/*.md`，机器表 `battle/voice_duel_data.py`
（`python battle/tools/_extract_duel_voice.py` 重抽）。

## 5. 事件与 cut-in

```
duel_challenge（组根：双方 id/武力 + clash_cutins）
 ├─ trait_trigger effect=duel_challenge（叫阵方）
 ├─ trait_trigger effect=duel_accept（接受时，防守方）
 └─ duel_result
     ├─ trait_trigger effect=duel_reject（拒绝时，防守方）
     └─ attr_change（仅 accepted：负者四维-10）
```

客户端 `DuelPerformance`（阻塞播放单元）：压暗非参战单位 → 号角横幅 →
叫阵气泡 →（**拒战**：拒战横幅 + 拒战气泡 | **应战**：应战气泡 →
单挑舞台 cut-in（§5b）→ 胜者横幅 → 负者四维惩罚落账）→ 解除压暗。
`TraitLineExtractProcessor` **不**抽 Duel 组内台词。

`clash_cutins`：武差 ≤10 → 3；≤20 → 2；否则 1。客户端再夹到 1~3。

## 5b. 单挑舞台 cut-in（客户端演出）

> 实现 `Assets/Scripts/ClientBattle/VFX/DuelStage.cs`；
> 入口 `CutInService.DuelClashRoutine`；参数 `StagePerformanceConfig`（`Duel*` 段）。
> 2026-07-27 重做，替代旧「两张半屏卡掠过中央裂缝」。

> **与通用 cut-in 的关系**：全部 cut-in 横幅共用同一形状「推镜 → 横幅 →
> 本组出手命中 → 撤镜」（权威 [../client/cutin_stage.md](../client/cutin_stage.md)）。
> 单挑是**唯一特例**：它在横幅那一拍额外做立绘出框/回框，且推得更近
> （45°/40 对 42°/46）。其余逻辑一致，改运镜规则先读那篇。

**一句话**：把两名参战武将**从各自卡框里揪出来**，扔进中央一块虚空展示屏里
打完，再送回卡框。单挑全场只发生一次（仅第 1 局开局），是整场唯一"停下来看"
的时刻，所以按一段完整的过场来做，而不是一次闪屏。

### 分幕

整段按**四个情感爆点**编排，不是四段等速位移。★ 标的是爆点。

| 幕 | 内容 | 时长参数 |
|---|---|---|
| ⓪ 蓄 | 两张立绘先往卡里"陷"一点（`Pose` 走负值＝反向外插）。**预备动作是爆发力的唯一来源**；少了这 0.16 s，后面飞多快都只读作位移 | `DuelAnticipateSeconds` / `DuelAnticipateDepth` |
| ★1 放 | 两人**定位圆**炸开出阵特效（`cast_duel_launch`＝Effect28）＋双方**卡面追加**（`aura_duel_victory`＝画廊 1/8 件 8/60）＋震屏＋白闪。**此拍真卡立绘仍可见**（替身未亮）；**放完才进下一拍**。立绘憋力发抖作用在 cut-in 替身数值上，出框瞬间才切显 | `DuelLaunchVfxKey` / `DuelLaunchCardVfxKey` / `DuelCoilTremble*` |
| 1.5 推镜 | **独立一拍**：俯角 36→45（垂直卡面）、距离 55→38（**卡面放大 1.45 倍**；再近会裁掉全阵），到位后**定格 0.3 s** | `DuelCameraPushSeconds` / `DuelCameraHoldSeconds` |
| ① 出框 | 立绘从**卡面当前世界姿态**起飞（起点就是卡上那张图，无缝），OutBack 过冲后收住，落到展示屏左右槽位；同时暗幕压下、展示屏由横缝展开（镜头已在上一拍推到位） | `DuelFlySeconds` |
| ② 亮屏 | 中央单挑图标从 1.8 倍"砸"到 1 倍 | `DuelIconSeconds` |
| ★2 静滞 | **末轮之前**两人后撤、图标缩紧。没有这口气，三轮交错就是等速流水账，最后一击也就不成其为最后一击 | `DuelBraceSeconds` / `DuelBraceBack` |
| ③ 交错 ×N | 两人对穿而过（一上弧一下弧），中点白闪 + `onClash`（音效/震屏）；随后弹回本位。**N = `clash_cutins`** | `DuelCrossSeconds` / `DuelCrossReturnRatio` / `DuelCrossArc` |
| ③ 动作 ×N | 每轮交错后各打一段动作：本轮**攻方** `strike`、**守方** `react`，攻守逐轮轮换；**末轮双方同时 `strike`**（对攻高潮，时长 ×`DuelFinalRoundScale`） | `DuelActionSeconds` |
| ★3 定胜负 | 胜者提亮上前放大、败者压暗下沉缩小 | `DuelResultSeconds` / `DuelResultHoldSeconds` |
| ★4 回框 | 暗幕**提前**散，立绘沿**原路反向**飞回卡框（镜头仍在近处，落框看得清） | `DuelFlySeconds` |
| 4.5 撤镜 | **独立一拍**：镜头还原到常规俯视（与推镜对称） | `DuelCameraPushSeconds` |
| ★4′ 落定 | **镜头还位之后**才起胜负特效：**胜者卡面加冕**（`aura_duel_victory`）＋**败者定位圆留痕**（`ground_duel_defeat` + 自研裂地）＋**败者卡面追加**（`aura_duel_defeat`＝画廊 1/8 件 32/60 观感）。**都放完，单挑演出才算结束** | `DuelVfxWaitCap` |

胜者由 `duel_result.winner_id` 下发，客户端**只读不判**（零结算红线）。

### 运镜：独立成拍的推近与撤回（`StageCameraRig`）

推镜与撤镜各是**独立一拍**，不与出框/回框并拍（2026-07-27 改）。
原本两者共用同一条进度，理由是"人飞出来和镜头压过来是一个动作的两面"，
但实测**读不出来**：观众注意力全在飞出去的人身上，镜头等于白推。
给它一拍**只有它在动**，再在到位后**定格 0.3 s**（`DuelCameraHoldSeconds`），
运动结束时的静止才让人确认"到位了、卡面变大了"。

终点俯角 `DuelCameraPitchDeg=45`，**恰好等于卡后倾角**，即光轴垂直卡面：
卡面不再被斜切，是"正脸看着你"的机位。距离由 55 缩到 **38**
（`DuelCameraDistance`）＝**卡面放大 1.45 倍**。推镜已是独立一拍 + 定格，
这点放大量读得出来；曾用 28（1.96×）会把全阵容卡面裁出画面——
**六张牌仍在框内**是硬约束，优先于「尽量放大」。

撤镜排在**回框之后、胜负特效之前**（全序：出阵→推镜→出框→交错→回框→撤镜→胜负特效）：
回框时镜头仍在近处，落框那一下看得清；胜负留痕要在**常视机位**播——
近景里脚下定位圆和整张牌都偏，加冕/溃败读不清。

三条实现约束（都踩过或差点踩）：

1. **只缩距离，不动 FOV**。FOV 是 `CameraFitter` 按安全区反算的取景基准，
   改它等于换镜头畸变（长焦突然变广角脸）。
2. **接管期间 rig 是相机位姿的唯一写方**，`CameraShaker` 切到"只算不写"
   （`Suspended`），偏移由 rig 叠加。两个 `LateUpdate` 的先后在 Unity 里不确定，
   不这么做就会"抖一下不抖一下"。交还时还要**作废抖动缓存的基准位**，
   否则下一次 Shake 会把相机瞬移回推近后的机位。
3. **cut-in 挂点是相机的子物体**（`CutInService.NewRoot`），不是世界坐标。
   否则相机一动整块屏就滑出视野。飞行立绘"卡上那一端"因此必须**每帧重算**
   （`Fighter.SyncCardPose`）而不是缓存一次——挂点在动、卡不动，
   两个空间的关系每帧都在变，缓存会让回框落偏。

**谁接管谁归还**：`Release()` 幂等，正常收尾（`finally`）、
`CutInService.CancelAll`、`PerformanceRunner.HardStop` 三条路径都要走到。

### 厂包特效（标准件）

| 时机 | 位置 | key | 点名（画廊序号）→ 实际原料 |
|---|---|---|---|
| 出框前，两人同时 | 各自**定位圆** | `cast_duel_launch` | RFX4 `Effect28`（3/8 包 19/54 件），定点环形件，直接用 |
| 出框前，两人同时 | 各自**卡面** | `aura_duel_victory`（`DuelLaunchCardVfxKey`） | 画廊 **1/8 我方标准件** 件 8/60；与胜者加冕同 key |
| 撤镜后，胜者 | **卡面**上 | `aura_duel_victory` | 原料 RFX4 `Effect23`＝运载器 → 碰撞子件 `Effect23_Explosion` |
| 撤镜后，败者 | **定位圆**地面 | `ground_duel_defeat` | 原料 Magic Pack v1 `Effect8`＝运载器 → `Effect8_Collision`；挂 `VfxGroundLayer`；贴花由自研裂地补 |
| 撤镜后，败者 | **卡面**上 | `aura_duel_defeat` | 画廊 **1/8** 件 32/60 观感；同原料 Effect8 的 **Anchor** 标准件（无地面层，否则挂卡会被压到卡下） |

**为什么两件用的是碰撞子件（P-68）**：运载器母件的粒子按"移动距离"发射，
定点摆着一颗粒子都不出；画廊里看到的那次爆炸，本来就是母件飞到碰撞点后
实例化的子件。改选子件＝在锚点原地播出画廊同款爆炸，不需要也不允许
去删母件的位移驱动。

位置一律是**定位圆**（脚下那一圈，直径＝卡宽），不是投影圆——两圆之别见
[../client/arena_stage.md](../client/arena_stage.md) §四c。

**顺序播，不重叠**（2026-07-27 定）：出阵放完才推镜/起飞；卡回框→撤镜→再起胜负两件，
两件放完单挑才结束。重叠播的话人已经飞走了、地上还在炸，
读作"两件不相干的事同时发生"，而不是因果。

等多久**不写死**：运行期由 `VFXManager.EmitWindow(key, cap)` 从 prefab 探时长，
分两种形态（2026-07-27 按实际素材校正）：

- **有一次性层**（胜负两件改选碰撞子件后即此形态，burst 一次性爆发）→ 取
  `startDelay + duration` 的最大值＝这一炸放完的时刻，**不含 `startLifetime`**。
  厂包件普遍是「爆发 + 长烟尾」，等烟尾散完观众看到的是一段发呆；
  发射结束＝主体已打完，余烬继续飘不妨碍下一拍。
- **全是循环层** → 循环层没有终点，取
  `startDelay + startLifetime` 的最大值＝**成形时长**（粒子填满那个形状所需）。

`main.duration` 对循环层是**循环周期**不是时长，混进来会得到一个两不像的数
（`cast_duel_launch` 曾因此报 4.0 s，真实爆发只有 1.5 s），故循环层单独处理。

四处配套：
- **原料改选**（取代已废弃的"钉死原地"方案，见上表与 P-68）：胜负两件由
  流水线自动改选碰撞子件；`WindZone` 等场景污染件由流水线统一摘除。
- **掐空转前摇**：厂包按 demo 场景排节奏，`Effect28` 的爆发层 `startDelay=1.00 s`。
  接线时把所有层同时前移到「最早的会出图层从 0 起播」（`NormalizeStartDelay`，
  同时前移而非各自归零，层间先后就是这件的表演结构）。不掐的话前一整秒是死拍，
  等待结束时爆发才刚炸，读作「特效没跑完就飞了」。
- **交拍收势**：出阵件有 5 个循环层，不会自己停。等待结束时对实例
  `VFXManager.StopEmitting`（只掐新粒子、留余烬），下一拍才读作「在余烬中被拽走」
  而非「炸到一半被打断」。**顺序感靠收势，不是把等待拉长**。
- 回收时长另加 `DuelVfxTailSeconds` 让余烬飘完，不会被硬切。

三条约束：
- **等的是真实秒**，不过 `ctx.Scaled`。粒子按真实时间播，把这段乘倍速等于把
  特效拦腰截断，那就不叫"播完再走"。代价是 4× 快进时这三拍不跟着变快，
  所以 `DuelVfxWaitCap`（1.7 s）必须卡住。该值是**照素材定的**：出阵件掐掉
  前摇后窗口 1.5 s，上限必须高于它否则又被截断。换素材要回来核对实测值。
  2026-07-27 原料改选（胜负两件换碰撞子件）后窗口形态变了（循环层→一次性
  burst），验收时重新核对三件实测窗口与 cap 的关系。
- key 未落盘 / 件里没粒子时退 `DuelVfxFallbackSeconds`（0.45 s）保底节拍。
  **不能因为素材缺失把节奏也丢了**——否则缺件时这一拍整个消失，前面的蓄力白蓄。
- 胜负特效起爆前先 `Fighter.Hide()` 再 `Restore()`：替身与真立绘此刻位置完全
  重合，顺序反了会有一帧重影。

### 全程无空等（零死帧的时间版）

**每一拍都必须有主体在动，不只是屏上有东西在动。** 屏上永远有 Chrome 自走
（暗幕/放射/浮尘），但那是氛围；若这一拍的**主角**静止，观众就读成"卡住了"。
逐拍点名谁在动：

| 拍 | 谁在动 |
|---|---|
| 出阵爆发 1.5 s | 脚下 Effect28 + 双方卡面追加 + 立绘憋力发抖 |
| 推镜 0.42 s | 镜头 |
| 定格 0.3 s | 卡牌待机浮动 + 脚下/卡面余烬 |
| 出框 / 回框 0.46 s | 立绘 + 暗幕开合 |
| 撤镜 0.42 s | 镜头还位（常视俯视） |
| 胜负特效 1.7 s | 胜者卡面 + 败者地面/裂地 + 败者卡面追加 |

历史坑：出阵那 1.5 s 原本是 `WaitForSeconds` 干等，脚下在炸而两张立绘纹丝不动，
整段被读成"背景动画"。改成憋力发抖后，同样的时长读作"力量在积蓄"。

### 为什么"画廊里挺好、接进去看不到/不对"

两个独立原因，都已修：

1. **标准件不存在** → `VFXManager` 回退成一个小色块占位（不是不出，是出了个方块）。
   画廊预览的是**厂包原料**，Resources 下没有同名标准件就等于没上线。
   落盘：跑一次 `GreekMyth/特效/接线 单挑三件`。
2. **尺寸没定径**。画廊里看到的观感是按了「C 键定径」缩到圆直径的，
   而 `VfxFitter` 只做"随卡宽等比浮动"，**不改变厂包件的原生尺寸**——
   一个 8 米宽的件挂上 `VfxFitter` 之后还是 8 米宽。于是接进去糊满全屏。
   现在标准件挂 **`VfxCircleFit`**（`VFX/VfxCircleFit.cs`）：运行期用与画廊
   同一判据（`Simulate(0.12s)` 量起手核心包围盒）缩到**投影圆**直径，
   按 prefab 名 + 圆直径缓存，热路径零测量。它与 `VfxFitter` **互斥**
   （都写 `localScale`），标准化工具见到它即跳过补挂。

**与画廊预览唯一有意为之的差异（移动端）**：实时点光每件只留 1 盏并关阴影
（`Effect28` 原件有 5 盏，两人同播＝10 盏，前向渲染下逐光一个 pass），
并删掉厂包自带 `AudioSource`（会绕过我们的 SFX 总线、与 `sfx_duel_*` 撞车）。
粒子层一层没动，主体观感与画廊一致。

**逐层去向**（标准化协议 §3.1 要求）：

- `Effect28`（直接用）：火/火环/爆/拖尾/环状粒子＋扭曲环 → 可迁移；
  `Decal_FireRing`（RFX4 UberDecal）→ **URP 画不出（P-33），标准化时摘除**。
- `Effect23_Explosion`（Effect23 的碰撞子件）：爆发粒子＋点光 → 可迁移；
  `Decal`（RFX4 UberDecal）→ 摘除；`RFX4_CameraShake` → 摘除
  （厂包直接晃 Camera.main，与自研 CameraShaker/StageCameraRig 打架）。
- `Effect8_Collision`（Effect8 的碰撞子件）：
  - **地面件** `ground_duel_defeat`：爆发/拖尾/扭曲粒子＋点光 → 可迁移；
    `DecalCore`/`Decal`（RFX1 UberDecal）→ 摘除。**贴花是这件的地面焦痕**，
    摘掉后地上什么也不留 → 既定替代品是自研裂地 `GroundCrackService.PlayHit`
    （落点同为 `GroundFoot`＝定位圆心），已在 `DuelStage.FireResultVfx` 一并触发；
    `Wind` → 摘除。
  - **卡面件** `aura_duel_defeat`：同原料、`VfxUsage.Anchor`（无 `VfxGroundLayer`），
    挂败者卡面追加；画廊点名 1/8 件 32/60 即此观感。

落盘：接线清单 `GreekMyth/特效/接线 单挑三件（出阵·加冕·溃败）`
（`Assets/Editor/GreekMyth/WireDuelStageVfx.cs`）→ 统一流水线
`VfxPackStandardizer.Standardize`（原料改选/Play 拒绝/配对裁剪/四项验证，
协议见 `../client/vfx_standardization.md` §四.3）。
key 在 `StagePerformanceConfig.Duel*VfxKey`——这三件与"谁参战"无关，
没有可查的 Profile 行，所以不进 `PerformanceProfile`。

> **画廊序号 → prefab 的换算不要靠数**：包 1/8 是我方标准件、组内还做过
> 碎件后置与 Ordinal 排序与「有粒子且无蒙皮网格」过滤。
> 用 `battle/tools/_gallery_index_dump.py`（照抄那段规则离线复算，
> 自带件数自检）。

### 暗幕为什么要延迟

出阵特效炸在**世界里的定位圆**上，而暗幕是 sorting 80 的全屏黑片。
不延迟的话这一炸从第一帧起就被盖住，等于白播。所以暗幕滞后于屏体开合
（`DuelVeilDelay`）：观众先在**真战场**上看见两团爆发，世界才暗下去、屏才接管
——"屏"因此被理解成随后被拉进去的地方，而不是一张凭空的贴图。
回程用同一比例的镜像（暗幕提前散），让胜负特效同样落在看得见的战场上。

### 展示屏的华饰与氛围层（`DuelStageChrome`）

**呆板的病根**：静止的纯色底 + 静止的立绘 = 一张贴纸。人眼判定「活」靠的是
**多个速率不同的运动叠在一起**。所以屏上刻意铺了四种**周期互质**的运动，
任意两帧都不重样：

| 元素 | 运动 | 参数 |
|---|---|---|
| 放射光芒 | 左右**反向**慢转（同向会读成整体在旋，反向才有对抗感） | `DuelRayCount/Radius/Alpha/SpinDegPerSec` |
| 浮尘余烬 | 匀速上升 + 横向正弦摆动，出顶回底；一半排在立绘之前、一半之后，才有纵深 | `DuelEmberCount/RiseSpeed/Alpha` |
| 屏边框 | 呼吸；交错时能量冲高 | `DuelRimBreathHz` |
| 整屏 | 极缓推进（默认 1.045/7 s，单帧察觉不到，连起来是"镜头压过来"） | `DuelPushInScale/Seconds` |
| 立绘 | 待机呼吸，两人**错相位** | `DuelPortraitBreathHz/Amp` |

静态华饰：**影院黑边**（进场压下退场收起——画幅一变观众自己就知道是重头戏）、
**左右阵营辉光**（横向渐变向屏心衰减，兼作阵营识别）、**四角纹饰**、
**立绘背光**（同一张图放大染阵营色垫在身后＝无 shader 的描边发光，
把主体从背景里拔出来）、交错时的**中央冲击环 + 白闪**。

全部程序化生成（纯色/渐变/环/软点四种贴图运行时合成），**零预制资源**。
存在同名真图则自动顶替：`UI/duel_screen_bg`（屏底，**建议 AI 生成一张，
是这块屏性价比最高的升级**；规格与中文提示词见
[../client/portrait_cutin_assets.md](../client/portrait_cutin_assets.md) §5d）、`UI/duel_rays`
（整张放射图，省 N 个渲染器）、`UI/duel_corner`（左上角纹饰，其余三角代码旋转复用）、
`UI/duel_icon`（中央图标）。

> **`DuelStageChrome` 是 MonoBehaviour，靠自己的 Update 走时钟**，不依赖编排层
> 每帧调它。所以 `DuelStage` 在插值、在等 `WaitForSeconds`、在放 flipbook 时，
> 屏上都始终有东西在动——这是零死帧（R-4.1）在这块屏上的兑现方式。
> 飞行立绘的待机呼吸也挂在它的 `OnTick` 上，共用同一条时钟。

### 三条实现红线

1. **出框期间卡面立绘必须藏起**（`UnitView.SetPortraitHidden`），否则读作
   "复制了一张"而不是"拽出来了"。**谁藏谁还**：正常收尾（`finally`）与
   `CutInService.CancelAll`（停播/重播）两条路径都要还原，
   漏一条战场上就会留下没有立绘的空卡框。
2. **出框/回框走同一条插值路径的正反向**（`Fighter.Pose(0..1)`），
   两段各写一套坐标必然错位。
3. **交错弧高不可设 0**：正对穿两张立绘在中点完全重叠，观众只看到闪了一下，
   读不出错身。

### 动作素材 = flipbook，不是视频

路径 `Resources/ClientBattle/DuelAction/{template_id}_{strike|react}_{NN}.png`
（`NN` 从 `00` 连号，断号即停，上限 64 帧）。

选逐帧图而不是 `VideoPlayer` 的两个硬理由：

- 单挑要**两人同屏同时播**，移动端双路视频解码是实打实的风险；
- flipbook 是按 `ctx.Scaled` 算出的帧下标，**天然吃倍速**；`VideoPlayer.playbackSpeed`
  与播放时间轴是两套时钟，2×/4× 下必然对不上。

**缺帧回退**（占位三级回退的本地实例）：整段序列缺失 → 退化为**静态立绘单帧**，
即「图片在 cut-in 屏上占满这段时间」，时序分毫不变。所以素材可以后补，
补一个武将亮一个，不必等齐。制作规格见 `docs/client/portrait_cutin_assets.md`。

## 6. 边界

- 单挑不掉兵、不产生伤害事件。
- 惩罚随第 1 局 game_end 回滚，不带入第 2 局。
- 无词库／空池 → 静默（不发 trait_trigger）。

## 7. 前后端索引

### 服务端（`battle/`）

| 文件 | 职责 |
|---|---|
| `engine.py::_run_duel` | 相位入口：参赛筛选 / 初对 / 入池 roll / 胜负 / 惩罚落账 |
| `bonds.py` | 羁绊表（S1/S2 追加初对、排序权重） |
| `voice_lines.py` | 叫阵/应战/拒战台词选取（双池：对方模板羁绊池 → generic） |
| `voice_duel_data.py` | 台词机器表（由 `tools/_extract_duel_voice.py` 从 `docs/character/*.md` 重抽，**不手改**） |
| `tools/_duel_prob_dump.py` | 入池率/胜率标定核对 |
| `tests/test_duel.py` | 配对、拒战、胜负、惩罚回滚 |

### 事件契约（`docs/schema/`）

`duel_challenge` / `duel_result` 的字段明细见 `battle_events_payloads.md`；
组结构见本文 §5。`clash_cutins` 是**服务端下发的演出参数**，客户端夹 1~3 后
直接当轮数用，不再自行推导。

### 客户端（`Assets/Scripts/ClientBattle/`）

| 文件 | 职责 |
|---|---|
| `VFX/Performances/DuelPerformance.cs` | 单挑播放单元（阻塞时间轴）：压暗 / 横幅 / 台词 / 调用 cut-in / 惩罚落账 |
| `VFX/CutInService.cs` | cut-in 唯一入口：独占仲裁、挂点建毁、中断还原；`ScreenRect` 取景 |
| `VFX/DuelStage.cs` | 单挑舞台演出本体（出框 / 展示屏 / 交错 / 动作 / 定胜负 / 回框） |
| `Units/StagePerformanceConfig.cs` | 全部时长与几何参数（`Duel*` 段），改数字即调参 |
| `Units/UnitView.cs::SetPortraitHidden` | 出框期间藏起卡面立绘 |

### 素材与文档

| 文档 | 内容 |
|---|---|
| `docs/client/portrait_cutin_assets.md` | 静态立绘 + cut-in 动作素材制作规格（尺寸/安全区/交付格式/批次） |
| `docs/client/performance_mechanisms.md` | 演出机制总表（单挑舞台在其中登记） |
| `docs/client/rendering_layout.md` §四 | sorting 层号（cut-in 占 80~90） |
| `docs/character/bonds.md` | 羁绊表文案权威（影响初对） |
