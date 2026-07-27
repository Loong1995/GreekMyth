# 特效播放方案快照（现行权威）

> 状态：2026-07-24 固化。改宙斯/雅典娜/包分工前先读本文 + [performance_mechanisms.md](performance_mechanisms.md)。
> 验收战报：`battle/out/manual/manual_3v3_seed20260722.json`（队 A：宙斯/雅典娜/赫克托尔）。

## 一、包分工（硬规则）

| 包 | 岗位 | 禁区 |
|---|---|---|
| **DigitalRuby Lightning（DR）** | 宙斯 `thunder` / `zeus_bolt` **竖雷几何**（单道） | — |
| **Magic Effects Pack 1**（kripto289） | 卡面命中/圣盾反制/光环；神舞台赫拉光束（待接） | 不当竖雷几何；不替 RFX4 远景 |
| **RFX4** | 舞台远景/地面大场面 | **禁**宙斯技能、单挑 cut-in（P-25） |
| Vefects / Cartoon Coffee / 四色弹道 | 其余英雄占位；随 Magic 换代 | — |
| CFXR | 势能火/柔光、冰锢等 | 圣盾粒子已改 Magic |

## 二、宙斯 / 雅典娜（本快照）

| skill / status | 模板 | 竖雷 | 命中 / 反制 | 挂身 | 资源 key |
|---|---|---|---|---|---|
| `thunder` | RemoteStrike | DR 单道 | Magic `Effect19_Collision` | — | Hit=`hit_lightning`；PortraitMark=`zeus` |
| `zeus_bolt` | RemoteStrike | DR 单道 | 同上 | — | 同上 |
| `thunder_oracle` | OracleAura | — | — | DR 乱劈调度 | `aura_thunder`（非 Magic） |
| `athena_aegis` | OracleAura | — | — | **AllIn1 金描边**（无 Magic 粒子） | Aura=`aura_aegis` |
| `aegis_shield` | Melee | — | Magic `Effect17_Collision` | 持盾者突进反弹 | Cast/Hit=`hit_shield_counter` |
| `aegis_ward` | StatusTrigger | — | 同上闪光 | — | 同上 |
| `ares_might` | OracleAura | — | — | 画廊 2/8·10/61 Magic `Effect18` 罩身；奇显偶隐 | Aura=`shroud_ares_might` |
| `blood_battle` | OracleAura | — | — | 卡框红呼吸（弱） | Aura=`aura_fire_foot` |

代码：`PerformanceDatabase.CreateBuiltin`；落雷节拍 `DefaultPerformance.PlayRemoteStrike`；
圣盾 `UnitAuraService.MountAegisAura`（仅 AllIn1）；战神之勇 `MountShroud` + `VfxShroudPresence`。

## 三、通用主动默认（未专配）

| 伤害类型 | 弹道 | 命中 |
|---|---|---|
| 物理 | `proj_bolt200` | `hit_sword`（画廊 [1/8] 件 45/61） |
| 魔法 | `magic_bolt` | `hit_petrify`（画廊 [1/8] 件 41/61） |

**默认不播 Cast**。群攻 `Auto`→目标≥2 升 `AoeCenter`。

## 四、单挑 / 舞台

- 单挑交错：cut-in 白闪 + 裂缝扩光 + 震屏；**零 RFX / 零 Magic 喷射**。
- 舞台：远雷→RFX4；赫拉光束→Magic（未接线）；裂地→贴花方案另见 stage_plan。
- 静态：地/天分图正交；**16:9 全宽铺满**；指令 `docs/dev/near3d_evaluation.md` §七。

## 五、预览入口

| 包 | 入口 |
|---|---|
| RFX4 | 菜单 `GreekMyth → RFX4 可靠预览（一键）`；粉红 → `GreekMyth → RFX4 → 导入 URP Patch（修粉红）` |
| Magic Pack 1 | 菜单 `GreekMyth → Magic Pack → 可靠预览（一键）` |
| 战斗验收 | `ClientBattleDemo`，`ReportPath`＝manual_3v3 上列路径 |

**以谁为准**：可靠预览 = 资产真貌。战斗残缺先查 URP Patch / 挂载裁剪。
按键：Play 后先点 Game 窗再按 ←→。

粉红：Magic / RFX4 各自「导入 URP Patch」菜单；RFX4 可用「诊断粉红材质」。

## 七、透视默认（近 3D）

- `PerspectivePilot`；卡牌后倾 `CardPitchDeg=45`（离竖直）、相机俯角
  `StagePerformanceConfig.PilotPitchDeg=35`（与卡解耦；`CameraFitter.PilotPitchDeg`
  只转发）；每卡再随机 ±5°（＝40~50°，`CardPitchJitterDeg`，只抖视觉）。
  理由与废案见 `arena_stage.md` §四b；脚下/罩身件的定位定径基准见 §四c。
- 地/天 **16:9 横向全宽铺满**（UI 半透叠两侧）；卡只在中央竞技区。
- 神/人天空含云，妖用暗雾；地天底边同色雾过渡。详见 `near3d_evaluation.md` §七。
- 关正交：`PerspectivePilot=false`。

