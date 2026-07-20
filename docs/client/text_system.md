# 文字系统（text_system）：飘字 · 台词气泡 · 横幅/cut-in · 中文名

> 战斗中所有「字」的机制汇总：什么时候出现什么字、字怎么排版调参、
> 谁阻塞时间轴谁不阻塞。

## 一、自然语言叙述

一次出手落地时：目标头顶飘出「天雷击 849」红字（暴击更大、带「暴击!」），
同单位连续飘字纵向错开不叠字；若被格挡则飘「技能名 格挡!」蓝灰字。
性格/状态发作时（傲慢、鲁莽、或被缄默跳过主动等），该卡右上弹白底聊天
气泡说一句台词——此刻全场其他演出**等它说完**（独占播放单元）再继续。
回合切换顶部出横幅；势能满档/超高伤时屏幕中上部金字 cut-in 一闪而过
（不阻塞）。

## 二、飘字（FloatingTextService）

- **触发**：伤害（技能名+数值+暴击/减免文案）、治疗（绿）、状态得失（蓝灰）、
  属性升降（金紫）、「协击」「连发 ×N」「延迟」「无法行动」等角标。
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
| 状态台词 | `"status"` | 控制/犹豫/先攻**临产生影响的执行节点**，见 [status_voice.md](../mechanics/status_voice.md) |

播放机制（2026-07-20 定稿）：

1. `TraitLineExtractProcessor` 把台词抽成独立 TraitLine 播放单元
   （出击段保留原 Root，见 [playback_units.md §二](playback_units.md)）。
2. Runner 调 `ChatBubbleService.SayExclusive(unit, line)`：同卡旧气泡先杀，
   立即弹出；返回独占秒数 `ExclusiveSeconds`（弹出 0.12 + 停留 0.9 +
   收起 0.12 ≈ 1.14s），Runner 原样 Wait（×DurationMul）——**时长由服务
   给出，禁止 Runner 自定常数**（P-19）。
3. 排版：9 字折行、底板按行数/字宽拉伸；底板资源
   `Resources/ClientBattle/UI/chat_bubble.png`（缺省白色占位）。
4. sortingOrder 70/71（全场最顶）。

## 四、横幅与 cut-in（不阻塞）

- **横幅**：回合号/局结果/单挑宣告，OnGUI 白字黑影双绘、按屏高缩放。
- **cut-in**：金字大横幅 1.4s 淡出+轻震屏；触发源＝满档轨 `cut_in=true` /
  单笔伤害 >3000 / 行动窗内第 5 次追伤；同播放组去重。
  代码：`PerformanceRunner.RequestCutIn/DrawCutIn`。

## 五、中文名注册表（红线）

- `Names/ChineseNames.cs` 与后端 `battle/names.py` 必须同步（技能/状态/属性）；
  新增战法/状态两边同时加。
- 飘字/结算表/cut-in 的名字全部经此表；缺表项会显示英文 id（即视为漏同步）。

## 六、维护清单

- 台词看不见 → 依次查：事件 `parent_seq` 是否 0（P-18）→ TraitLine 组是否
  生成 → 气泡层级（70）。
- 台词重叠/抢拍 → 只能改 `ChatBubbleService` 的三段时长常量，Runner 不动。
- 飘字改观感 → 只动 Tuning SO；改代码默认值须同步 floating_text_tuning.md。
