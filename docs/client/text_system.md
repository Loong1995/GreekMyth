# 文字系统（text_system）：飘字 · 台词气泡 · 横幅/cut-in · 中文名

> 战斗中所有「字」的机制汇总：什么时候出现什么字、字怎么排版调参、
> 谁阻塞时间轴谁不阻塞。

## 一、自然语言叙述

一次出手落地时：目标头顶飘出「天雷击 849」红字（暴击更大、带「暴击!」），
同单位连续飘字纵向错开不叠字；若被格挡则飘「技能名 格挡!」蓝灰字。
性格/状态发作时（傲慢、鲁莽、或被缄默跳过主动等），该卡右上弹白底聊天
气泡说一句台词——此刻全场其他演出**等它说完**（独占播放单元）再继续。
回合切换顶部出横幅；势能满档/超高伤时**全屏单人 cut-in**（暗幕+阵营色
斜带+巨幅立绘+大字）甩入甩出，不阻塞时间轴；单挑接受后播**全屏裂缝
交错 cut-in**（两张半屏卡对向滑过中央裂缝线）。

## 二、飘字（FloatingTextService）

- **触发**：伤害（技能名+数值+暴击/减免文案）、治疗（绿）、状态得失（蓝灰）、
  属性升降（金紫）、「协击」「连发 ×N」「延迟」「无法行动」等角标；
  **无伤害/治疗的默认主动**（神使戏言等）在施法者头顶单飘技能名
  （`FloatingTextService.ShowSkillName`，色=AttrUp）。
- **排版**：同单位纵向堆叠 `StackSpacing` 错位；上浮 OutCubic + 淡出 InQuad。
- **性能**：零分配——开战前预建 24 个 TextMesh + 一次性请求全部字形
  （`ChineseNames.FloatingTextCharacters`），运行时服务统一 Update，
  不为单条飘字建 tween/闭包。
- **调参（字体控制机制）**：全部参数收口 SO
  `Resources/ClientBattle/FloatingTextTuning.asset`（字体名/字号/BaseScale/
  时长/上浮距离/堆叠间距/暴击倍率/9 色）；字体放
  `Resources/ClientBattle/Fonts/` 填名即换。操作步骤：
  [floating_text_tuning.md](floating_text_tuning.md)。
- 代码：`Units/FloatingTextService.cs` + `Units/FloatingTextTuning.cs`；
  sortingOrder 60。

## 三、台词气泡（ChatBubbleService，独占单元）

两类台词共用同一通道（事件都是 `trait_trigger`）：

| 类别 | trait_id | 时点（引擎侧） |
|---|---|---|
| 性格台词 | 各性格 id | [traits.md](../mechanics/traits.md)；错开时点特例见 [hero_specials.md §1](../mechanics/hero_specials.md) |
| 单挑台词 | 武将 `trait_id`（或 `voice`） | `effect=duel_challenge/accept/reject`；挂 duel 组，由 `DuelPerformance.Play`（内部 `PlayDuelLines`）播，见 [duel.md](../mechanics/duel.md) |
| 状态台词 | `"status"` | 控制/犹豫/先攻**临产生影响的执行节点**，见 [status_voice.md](../mechanics/status_voice.md) |

播放机制（2026-07-20 定稿）：

1. `TraitLineExtractProcessor` 把台词抽成独立 TraitLine 播放单元
   （出击段保留原 Root，见 [playback_units.md §二](playback_units.md)）。
2. Runner 调 `ChatBubbleService.SayExclusive(unit, line, DurationMul, Speed)`：
   同卡旧气泡先杀，立即弹出；动画与返回秒数同一套缩放（基准 ≈1.14s ×
   DurationMul/Speed）。Runner **`WaitForSeconds(返回值)`，禁止再乘 DurationMul**——
   泡收起后立刻下一单元（无 GroupPause）（P-19）。
3. 排版：9 字折行、底板按行数/字宽拉伸；底板资源
   `Resources/ClientBattle/UI/chat_bubble.png`（缺省白色占位）。
4. sortingOrder 70/71（全场最顶）。

### 击杀台词（2026-07-22）

`hero_defeated` 后服务端发 `trait_trigger`（effect=kill，说话者=击杀者，挂
defeat 同组）；客户端无特判——`TraitLineExtract` 抽成独占 TraitLine 气泡，
自然排在阵亡倒下之后。羁绊池→generic 由服务端选定，客户端只播 `line`。

## 四、横幅与 cut-in

- **横幅**（不阻塞）：回合号/局结果/单挑宣告，OnGUI 白字黑影双绘、按屏高缩放。
- **单人 cut-in**（2026-07-21 全屏化）：暗幕+阵营色斜带甩入+巨幅
  立绘反向滑入+大字标题，约 0.8s；触发源＝满档轨（**2026-07-22 语义**：轨已满
  ≥5 后该轨再次进账才切；出手音效换 `sfx_attack_empowered`）/
  单笔伤害 >3000「重创」/ 行动窗内第 5 次追伤；同播放组去重。
  **2026-07-27 统一**：三者一律**独占**且带取景——推镜→横幅→本组出手命中→
  撤镜（与单挑同构、不飞立绘），判据前移到播组前的 `CutInPolicy.Resolve`；
  权威 [cutin_stage.md](cutin_stage.md)。
  **文案**：满档 cut-in 标题＝即将造成伤害的技能名（战法中文名 /
  「普攻」/「协击」/状态归因战法，`SkillNameOf`）；不再用「势能全开·轨名」。
  高伤 cut-in 文本末尾带伤害额度（`…重创 X！-金额`）。
  无主体播报（战术变更）回退旧 OnGUI 金字横幅。
  代码：入口 `CutInService.Request`（组去重）→ `PlaySolo`；阻塞入口
  `PlaySoloBlocking`；回退 `BannerService.ShowTextCutIn`。
- **决斗裂缝 cut-in**（阻塞，在 DuelPerformance 时间轴内）：中央斜裂缝线分屏，
  两张半屏武将卡（阵营色底+巨幅立绘+名字）一张自上而下、一张自下而上
  对向滑过裂缝算一次交错；`clash_cutins`（1~3）次来回、一次比一次快
  （×0.72），每次穿越裂缝白闪+震屏+`sfx_duel_clash`；末次拉回中线对峙、
  弹 VS、白闪后左右弹开。代码：`CutInService.DuelClashRoutine`。
  层级 80~90 见 [rendering_layout.md §四](rendering_layout.md)。

## 五、中文名注册表（红线）

- `Names/ChineseNames.cs` 与后端 `battle/names.py` 必须同步（技能/状态/属性）；
  新增战法/状态两边同时加。
- 飘字/结算表/cut-in 的名字全部经此表；缺表项会显示英文 id（即视为漏同步）。

## 六、维护清单

- 台词看不见 → 依次查：事件 `parent_seq` 是否 0（P-18）→ TraitLine 组是否
  生成 → 气泡层级（70）。
- 台词重叠/抢拍 → 只能改 `ChatBubbleService` 的三段时长常量，Runner 不动。
- 飘字改观感 → 只动 Tuning SO；改代码默认值须同步 floating_text_tuning.md。
