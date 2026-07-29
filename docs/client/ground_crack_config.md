# 裂地技能配置（权威登记表）

> **按技能写裂地档位与面积的唯一登记处。** 表现语言（遮罩/熔岩/shader）见
> [ground_crack_language.md](ground_crack_language.md)；本文件只答
> 「谁出裂地、出几档、面积多少」。
>
> 代码入口：`GroundCrackService`（`Active` / `ShouldPlayHit` / `PathDriver` /
> `ResolveStrength` / `AreaOf`）；专配字段：`PerformanceProfile.GroundStrengthTier`
> / `GroundHitArea`（写在 `PerformanceDatabase`）。

## 〇、标签真源（先读）

战法类别**不要再猜**。战报头 `skill_catalog`（schema 1.5.0）已由服务端
`Skill` 定义导出，客户端 `BattleReport.CatalogOf(skillId)` 直读：

| 字段 | 用途（裂地侧） |
|---|---|
| `category` | `prepare_active`＝准备型主动；`active`＝瞬发主动；其余见 schema |
| `prepare_rounds` | >0 与 `prepare_active` 同义（单真源在服务端推导） |
| `damage_type` | `physical` / `magic` / `mixed` / `none`（聚合视图；**逐条**仍读事件 `damage.damage_type`） |
| `tags` | 演出粒度等（`per_target` / `simultaneous`…）；与裂地档位无关 |

填 §二约定、§三登记表时以目录字段为准。专属高光（如神罚）**不进** catalog，
靠 `PerformanceDatabase` 专配。

## 一、四类场景与伤害类型

| 场景 | 骨架 | 物理 | 魔法 | 入口 |
|---|---|---|---|---|
| **命中裂地** | Impact | ✅ | ✅ | `ShouldPlayHit` → `PlayHit`（与 HitKey 同帧） |
| **轨迹裂地 T4** | Path | ✅ | ✅ | `MoveTrailDriver`（仅巨伤 / 势能加强） |
| **弹道裂地** | Path | ✅ | ❌ | `PathDriver`（逐物理 lane；混合组魔法 lane 跳过） |

门槛共通：`Enabled` + `ArenaSlotLayout.GroundActive` + 组内有伤害。
魔法主动飞 `magic_bolt`，**不拖弹道裂缝**；脚下命中/拉满轨迹与物理同规。

## 二、强度 / 面积解析（硬规则，物魔同）

| 优先级 | 条件 | 轨迹 T4 | 弹道强度（仅物理） | 命中强度 | 命中面积 |
|---|---|---|---|---|---|
| 1（最高） | **巨伤**（`ctx.MassiveStrike`） | **有** | **档 3** | **档 3** | **×1.5** |
| 1（并列） | `EmpoweredStrike`（势能满轨加强） | **有** | **档 3** | **档 3** | **×1.5** |
| 2 | profile `GroundStrengthTier` ≥ 1 | 无 | 该档 | 同档 | `GroundHitArea` 或 ×1 |
| 3（默认） | 未配（字段 = 0） | 无 | **档 1** | **档 1** | ×1 |

说明：

- 弹道与命中**同档**（一套 `GroundStrengthTier`）；面积只作用命中类。
- **轨迹 T4**：出击者突进途中踩出档 3 缝；`MoveTrailDriver` + `StrikeBeats.Advance`。
- 势能/巨伤**覆盖**专配：即使配了档 2，拉满仍强制档 3 + 面积 1.5。
- **不另叠场心大裂地**（`PlayArena` 已废止）：拉满只走「轨迹档3 + 弹道档3 + 命中档3×1.5」。
- 默认面积 ×1 ＝ 命中直径 **卡宽 ×1.5**；×1.5 ＝ 卡宽 ×2.25（档 3 另有 `SizeScale`×1.35，见 language）。

## 三、配置约定（加新主动时按此填）

| 技能类别（读 catalog） | `GroundStrengthTier` | `GroundHitArea` | 效果 |
|---|---|---|---|
| **准备型**主动群攻（`prepare_active`） | **2** | 0 | 档 2（物理另有弹道裂地） |
| **瞬发**主动群攻（`active`、无准备） | **0**（＝档 1） | 0 | 档 1 |
| 其它出手（近身/单体/落击等） | 按观感；默认 0 | 0 | 通常档 1 |
| 特例（高光抬档、超大面积等） | 显式写档与面积 | 可 >1 | **必须** §四登记 |

物理/魔法同一张约定表；差别只在「有没有弹道裂地」。

## 四、已登记技能

| skill_id | 名称 | catalog / 性质 | 弹道档 | 命中档 | 命中面积 | profile 字段 | 备注 |
|---|---|---|---|---|---|---|---|
| `hector_warcry` | 特洛伊战吼 | `prepare_active` 物理群攻 | **2** | **2** | ×1 | `GroundStrengthTier=2` | 势能加强→3+×1.5 |
| `hector_assault` | 决死猛攻 | `active` 物理群攻 | 1 | 1 | ×1 | （不配） | 拆解 |
| `zeus_divine_punishment` | 神罚 | 高光·魔法（不进 catalog） | — | **2** | ×1 | `GroundStrengthTier=2` | RemoteStrike 无弹道 |

后续每加一个要抬档/改面积的主动，**必须**在本表追加一行，并同步
`PerformanceDatabase`。瞬发默认档 1、无专配字段的，可不登记。

## 五、怎么加一条

1. 查 `skill_catalog`（或高光专配）→ 按 §三取档与面积。
2. 在 `PerformanceDatabase` 写 `GroundStrengthTier` / `GroundHitArea`（瞬发可不写）。
3. 抬档/特例在 §四追加一行。
4. 若改了语言级规则（优先级/物魔分界），同步本文件 §一§二与
   `GroundCrackService` 注释，changelog 留一行。

禁止：在演出模板里写死档位数字；禁止为「更猛一点」另烘 prefab；
禁止再写「魔法默认不裂地」——已废止。
