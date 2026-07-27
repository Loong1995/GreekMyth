# 特效配置总索引（查「谁用什么特效」从这里进）

> **权威入口**：本文件只回答「查哪里 / 默认是什么 / 解析顺序」。
> key 资产细节、厂包接线、演出模板行为分别见下方分文档——**不要**在多处
> 复制同一张「现状表」；改接线只改代码真源 + 本索引对应行 + 分文档。

## 〇、先答：普攻卡面受击特效是什么？

| 项 | 值 |
|---|---|
| **key** | `hit_generic` |
| **原料** | Vefects Combat Flipbook **Hit_05 Once** |
| **为何不是 hit_sword** | 普攻走组默认 `MeleeDefault`，其 `HitKey` **非空**＝`"hit_generic"`，`ResolveHitKey` 不会落到「按伤害类型」分支（巨伤仍会被 ① 覆盖成 `hit_massive`） |
| **代码真源** | `PerformanceDatabase.BuildDefaults` → `MeleeDefault.HitKey`；结算 `SkillPerformance.SettleDamage` → `ResolveHitKey` |
| **预览** | 特效画廊 **[1/8] 我方标准件**，按 name Ordinal 找到 `hit_generic` |

同口径还用 `hit_generic` 的：追击组默认、状态触发组默认、全局兜底、
帕特洛克勒斯借刀等（见 `PerformanceDatabase.SpecialProfiles`）。

## 一、命中 key 解析顺序（唯一，四级）

```
1. 巨伤覆盖：CutInPolicy.IsHighDamage（>3000，与「重创」横幅同判据）
     → hit_massive（RFX4 Effect15_Collision），压过一切专配；
       同帧强制震屏（0.55/0.48s，不吃 CameraShakeOnHit；MaxOffset=0.75）；
       同帧命中裂地强制**档 3 + 面积 ×1.5**（`PlayHit(..., massive)`）；
       **重创 cut-in 于本组出手前**取景播出（推镜→横幅→出手命中→撤镜，
       见 cutin_stage.md），故暗幕不会盖住命中拍的卡面特效（P-72）
2. PerformanceProfile.HitKey 非空 → 直接用（专配战法 / 组默认）
3. 否则按 damage_type：
     magic  → hit_petrify
     其他   → hit_sword
4. damage 为空 → hit_generic
```

实现：`SkillPerformance.ResolveHitKey`（唯一入口，模板禁止绕过）。
巨伤震屏在 `SettleDamage` 与命中拍同帧。  
Profile 从哪来：`VFXResolver` 三级查找——

```
SpecialProfiles[skill/status id]
  → 组默认（Active / Melee / Pursuit / StatusTrigger / Oracle）
  → GlobalDefault
```

| 组 | 字段 | 现行默认 HitKey | 说明 |
|---|---|---|---|
| 普攻 `NormalAttack` | `MeleeDefault` | **`hit_generic`** | 本表〇 |
| 追击 | `PursuitDefault` | **空串** | **同步主动逻辑**：按伤害类型走 ③ |
| 主动（未专配） | `ActiveDefault` | 空串 | 走 ③ 按伤害类型 |
| 状态触发 | `StatusTriggerDefault` | `hit_generic` | 可被 Special 覆盖 |
| 神谕（Passive 组） | `OracleDefault` | **`hit_wave`** | 神谕产生的伤害默认命中 |
| 全局兜底 | `GlobalDefault` | `hit_generic` | |

> 画廊 **[1/8] 我方标准件** 的序号随入库件数漂移（新 key 会插位），
> 文档一律以 **key 名**为准；查序号临时用 `_gallery_index_dump.py`。

**改默认命中**：改 `PerformanceDatabase` 对应字段，或改 `ResolveHitKey` 分支；
同步本文件 §〇/§二 + [assets_upload_guide](assets_upload_guide.md) 对应 key 行。

## 一b、弹道 key 解析顺序（唯一，**逐条伤害**）

```
1. profile.ProjectileKey 非空 → 专配优先（珀尔修斯飞剑、海神浪…）
2. damage_type == "magic" → magic_bolt（画廊 1/8 件 54/62）；**不带弹道裂地**
3. 其余（物理）        → proj_bolt200；**带弹道裂地**（档位见 ground_crack_config）
```

实现：`DefaultPerformance.ProjectileKeyOf(profile, damage)`；近身斩击同构走
`StrikeKeyOf`（物理 `slash` / 魔法 `magic_bolt`）。

- **主动技能的默认弹道即物理系**（`proj_bolt200`），物理系主动才有「弹道 +
  默认裂地」这一整套；纯魔法伤害的主动走 `magic_bolt`，**全程无裂地**
  （裂地是「砸在地上」的语言，见 `GroundCrackService.IsPhysical`）。
- **逐条判、不按组第一条判**（2026-07-27 改）：同组混合伤害时，魔法那一路飞
  `magic_bolt` 且不出裂缝，物理那一路飞 `proj_bolt200` 且出裂缝。弹道裂地侧
  由 `FlightPathCracks` 逐 lane 同判据把魔法 lane 整条跳过。
- 远程落击（雷霆）无 `ProjectileKey` 时走程序化竖雷，不套本表。

## 二、常用默认一览（卡面 / 弹道）

| 场景 | 弹道 / 斩击 | 卡面命中 HitKey |
|---|---|---|
| **巨伤（重创横幅）** | 按原模板 | **`hit_massive`**＋强制震屏＋**档3×1.5命中裂地**；命中拍先露脸再 cut-in（P-72） |
| 普攻近身 | `slash`×1.0（Melee 命中帧） | **`hit_generic`** |
| 追击（同步主动） | 群攻走主动、单体近身 | 按伤害类型：`hit_sword` / `hit_petrify` |
| 主动·物理（未专配） | `proj_bolt200`＋弹道裂地 | `hit_sword` |
| 主动·魔法（未专配） | `magic_bolt`（1/8 件 54/62）**无裂地** | `hit_petrify` |
| 神谕产生的伤害 | 按模板 | `hit_wave`（`OracleDefault.HitKey`） |
| 雷霆 / 天雷击 | DR 竖雷（无弹道） | `hit_lightning`（Special；被巨伤覆盖时除外） |
| 治疗 | — | `heal_generic`（`SettleHeal` 写死） |

专配战法（阿喀琉斯穿刺、珀尔修斯飞剑、圣盾反制…）一律看
`PerformanceDatabase.SpecialProfiles`，本表不逐条展开。

## 三、查配置去哪里（总→分）

| 想查什么 | 去哪 | 性质 |
|---|---|---|
| **命中/弹道默认、解析顺序、普攻是谁** | **本文**（§一 命中 · §一b 弹道） | 总索引 |
| 某 key 原料/路径/画廊序号 | [assets_upload_guide.md](assets_upload_guide.md) | key 登记唯一清单 |
| 三级 Profile / 专配列表 | `Assets/Scripts/ClientBattle/VFX/PerformanceDatabase.cs` | **代码真源** |
| 解析 API | `VFXResolver.cs` + `SkillPerformance.ResolveHitKey` | 代码 |
| 模板什么时候播 Hit（Melee/AOE/…） | [performance_mechanisms.md](performance_mechanisms.md) §模板族 | 演出总纲 |
| 宙斯/雅典娜等包分工快照 | [vfx_playback_scheme.md](vfx_playback_scheme.md) | 方案快照 |
| 画廊点名 → 标准化落盘 | [vfx_standardization.md](vfx_standardization.md) | 接件纪律 |
| 厂包层可用性 / URP | [vfx_pack_integration.md](vfx_pack_integration.md) | 包改造 |
| 画廊序号 → prefab | `python battle/tools/_gallery_index_dump.py 包:件` | 定件工具 |
| 单挑出阵/胜负特效 | [../mechanics/duel.md](../mechanics/duel.md) + `StagePerformanceConfig` | 跨端 |
| 裂地档位（非卡面粒子） | [ground_crack_config.md](ground_crack_config.md) | 地面 |
| 舞台运镜/定位圆 | [arena_stage.md](arena_stage.md) | 几何 |
| 受击击退/颤动参数 | `StagePerformanceConfig` + performance_mechanisms §受击 | 手感 |

## 四、改接线检查单（最短路径）

1. 定件：画廊序号 → `_gallery_index_dump.py`（包 1＝我方标准件，禁分母纠偏，P-71）。
2. 若 key 已在 `Resources/ClientBattle/VFX/` → **只改** Profile / `ResolveHitKey`；
   若原料是厂包 → 走 [vfx_standardization](vfx_standardization.md) 流水线落盘。
3. 登记 [assets_upload_guide](assets_upload_guide.md)；更新本文 §〇/§二 若动到默认。
4. changelog 一行。

## 五、维护红线

1. **禁止**在 `DefaultPerformance` 等模板里写死命中 key（除 `SettleHeal` 的
   `heal_generic` 这类明确全局原语）。
2. 本文与 `PerformanceDatabase` / `ResolveHitKey` 冲突时以代码为准，并当次修本文。
3. 分文档可详述，但「普攻用谁 / 主动默认用谁」只在本文 §〇§二 维护一份结论。
