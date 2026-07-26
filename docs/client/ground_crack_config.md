# 裂地技能配置（权威登记表）

> **按技能写裂地档位与面积的唯一登记处。** 表现语言（遮罩/熔岩/shader）见
> [ground_crack_language.md](ground_crack_language.md)；本文件只回答
> 「这个技能的弹道/命中用几档、面积多少」。
>
> 代码入口：`GroundCrackService.ResolveStrength` / `AreaOf`；
> 专配字段：`PerformanceProfile.GroundStrengthTier` / `GroundHitArea`
> （写在 `PerformanceDatabase`）。

## 一、解析优先级（硬规则）

| 优先级 | 条件 | 弹道强度 | 命中强度 | 命中面积 |
|---|---|---|---|---|
| 1（最高） | `ctx.EmpoweredStrike`（主动势能轨满后的加强出手） | **档 3** | **档 3** | **×1.5** |
| 2 | profile 配了 `GroundStrengthTier` ≥ 1 | 该档 | 同档 | `GroundHitArea` 或 ×1 |
| 3（默认） | 未配（字段 = 0） | **档 1** | **档 1** | ×1 |

说明：

- 弹道与命中**同档**（一套 `GroundStrengthTier`）；面积只作用命中类。
- 势能加强时**覆盖**专配：即使技能配了档 2，加强出手仍强制档 3 + 面积 1.5。
- 势能加强出手另叠场心大裂地（命中骨架 + 档 3 + 面积 3.2），与上表 Path/Hit 并存。
- 魔法伤害不裂地（`GroundCrackService.Active`）。

## 二、配置约定（加新物理群攻时按此填）

| 技能类别 | `GroundStrengthTier` | `GroundHitArea` | 效果 |
|---|---|---|---|
| **准备型**物理主动群攻（`prepare_rounds`>0） | **2** | 0（默认） | 档 2 弹道 + 默认面积档 2 命中 |
| **瞬发**物理主动群攻（无准备） | **0**（＝档 1） | 0 | 档 1 弹道 + 默认面积档 1 命中 |
| 其它物理出手（近身/单体等，若接线裂地） | 按观感；默认 0 | 0 | 通常档 1 |
| 特例 | 显式写档与面积 | 可 >1 | 必须在 §三表登记理由 |

默认面积 ×1 ＝ 命中直径 **卡宽 ×1.5**。面积 ×1.5 ＝ 直径卡宽 ×2.25。

## 三、已登记技能

| skill_id | 名称 | 类别 | 弹道档 | 命中档 | 命中面积 | profile 字段 | 备注 |
|---|---|---|---|---|---|---|---|
| `hector_warcry` | 特洛伊战吼 | 准备型物理群攻 | **3** | **3** | ×1 | `GroundStrengthTier=3` | 自带特例抬档；势能加强→3+×1.5 |
| `hector_assault` | 决死猛攻 | 瞬发物理群攻 | 1 | 1 | ×1 | （不配） | 拆解 |

后续每加一个会裂地的物理主动，**必须**在本表追加一行，并同步改
`PerformanceDatabase` 对应 profile。

## 四、怎么加一条

1. 判定类别（准备型群攻 / 瞬发群攻 / 特例）→ 按 §二取档与面积。
2. 在 `PerformanceDatabase` 写 `GroundStrengthTier` / `GroundHitArea`（瞬发群攻可不写）。
3. 在 §三表追加一行。
4. 若改了语言级规则（优先级/默认档），同步改本文件 §一与
   `GroundCrackService` 注释，并在 changelog 留一行。

禁止：在演出模板里写死档位数字；禁止为「更猛一点」另烘 prefab。
