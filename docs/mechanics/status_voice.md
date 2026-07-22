# 状态台词机制（status_voice）

> 2026-07-20 新增：控制类状态、犹豫、先攻在**真正改写执行**的节点发一句台词。
> 代码：`battle/status_voice.py`（词库+发送）+ `battle/engine.py`（触发点）；
> 测试 `battle/tests/test_status_voice.py`；客户端播放见
> [text_system.md §三](../client/text_system.md)。

## 一、自然语言叙述

被缄默的武将轮到自己出手、主动战法被封时，卡上弹气泡「无声之境——主动
战法，免谈。」；被缴械跳过普攻、犹豫判定成功延后行动、先攻抢到出手权时
同理各说一句。台词只在状态**产生实际影响的那一刻**出现——挂上状态但没
影响到行为（如缄默者本回合本就不放主动）不说话。

## 二、契约与确定性

- 复用 `trait_trigger` 事件：`trait_id="status"`、`effect=<status_id>`、
  `line=<台词>`；契约加法演进，无新事件类型。
- **必须 `parent_seq=0` 自成组**：客户端把它独立成 TraitLine 播放单元弹气泡；
  挂在 action_start 下会被节点组静默吞掉（坑 P-18）。
- 每类 3 条台词，按 `hero.trait_line_seq["status:<id>"]` 确定性轮换，
  **不消耗 RNG**（与性格台词同机制）。

## 三、触发点总表（引擎侧）

发送与去重统一走 `status_voice.py` 的三个入口（2026-07-20 重构收口，
engine 不再手写状态元组）：`emit_voice_once`（同窗同人同状态一次）、
`emit_forbid_voice`（按候选优先序取持有的第一条）、`pick_skip_voice_id`
（全禁主因，`_SKIP_PRIORITY` 硬度序 petrify>ming_lock>fear>silence>disarm）。

| status_id | 发送时机（engine.py 内节点） | 候选表 / 去重 |
|---|---|---|
| `silence` / `ming_lock` / `petrify` | 因禁主动跳过主动判定时 | `FORBID_ACTIVE_VOICE`；同窗一次 |
| `disarm` / `fear` / `ming_lock` / `petrify` | 因禁普攻跳过普攻时 | `FORBID_BASIC_VOICE`；同窗一次 |
| 全禁主因 | 行动窗整体 `skipped` 时 | `pick_skip_voice_id`；同窗一次 |
| `hesitation` | 判定成功、写出 `skill_trigger kind=delayed` 之前 | `emit_voice_once` |
| `charm` | 魅惑改写选人池前 | `emit_voice_once` |
| `first_strike` | 先攻改序后，该武将 `action_start` 紧随（`_first_strike_voice` 集合，回合首重置） | 每回合一次 |

## 四、文字日志

`battle/textlog.py`：状态台词渲染为「〔状态中文名〕作祟 …台词…」，
与性格台词★区分。

## 五、维护清单

- 新增可发声禁制：`LINES` 加 3 条 + 登记 `FORBID_*_VOICE` / `_SKIP_PRIORITY`
  即可，**engine 零改动**；新触发场景（非禁制类）才需在 engine 影响节点调
  `emit_voice_once`。无需改客户端与契约。
- 台词内容改动会影响 golden（trait_trigger 带 line），重生成须说明原因。
