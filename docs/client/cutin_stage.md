# cut-in 横幅机制（权威 · 客户端）

> 一切 cut-in 横幅（满档 / **巨伤「重创」** / 追击不止 / 战术变更）的触发判据、
> 编排形状、运镜与参数，全部登记在本文。改 cut-in 只改本文所列的两个文件：
> `Events/CutInPlanner.cs`（判据，编译期注记）与 `VFX/CutInStage.cs`（编排），
> 禁止在演出层/编排层散落门槛判断。
> 关联：[playback_requirements R-5.2](playback_requirements.md)、
> [performance_mechanisms §一b](performance_mechanisms.md)、
> 单挑特例 [../mechanics/duel.md](../mechanics/duel.md)。

## 一、统一编排形状（2026-07-27 定论）

cut-in 只有一种形状，与单挑同构，**整段独占播放单元**：

```
推镜 → cut-in 横幅 → 本组自身的演出（出手 + 命中）→ 撤镜 → 交棒下一组
```

- **单挑是唯一特例**：它在「横幅」那一拍额外做立绘飞出/飞回（`DuelStage`）；
  其余 cut-in 不飞立绘，其它逻辑完全一致。
- 举例（阿喀琉斯巨伤）：伤害段之前镜头压近 → 「重创」横幅 →
  在近机位打出这一下（突进/弹道/命中特效/裂地/震屏全在近景）→ 命中收束后撤镜。
- 相机唯一写方是 `StageCameraRig`；`CutInStage` 借用后在 `finally` 归还，
  中断路径（HardStop / CancelAll）也不会把战斗留在近机位。

## 二、触发源与判据（`CutInPlanner`，**编译期**逐组注记，运行期只读 `EventGroup.CutIn`）

| 源 | 判据 | 标题 | 加强出手 |
|---|---|---|---|
| 满档轨 | 某轨镜像已 ≥5（Full）后，本组同轨 `momentum_change.cut_in=true` 再次进账；刚满 5 的当次不切 | 即将造成伤害的技能中文名 + `！` | 是（`EmpoweredStrike`） |
| **巨伤「重创」** | 组内首条 `damage.amount > 3000`（`HighDamageThreshold`）且 `mitigation` 为空（被格挡/反弹的 0 伤不算） | `技能名 重创 目标！-伤害` | 否 |
| 追击不止 | 行动窗内第 5 个追击单元（`PursuitCutInAt`） | `追击不止！` | 否 |
| 战术变更 | 无主体武将 → 无运镜，回退 OnGUI 文字横幅（`BannerService.ShowTextCutIn`） | 播报文本 | 否 |

- **优先级**：满档 > 巨伤 > 追击第 5 次；**一组最多切 1 次**（去重在
  `CutInService`，`AlreadyPlayed(groupId)`）。
- **为什么必须播组前预判**：客户端在播一组之前就持有整组事件，能提前知道
  「这一下会打出巨额伤害」。旧的事后回调式（`NotifyDamageSettled` 请求）既
  做不到「伤害前推镜」，暗幕（sorting 80）还会盖住同帧起播的 `hit_massive`
  卡面特效（sorting ≥45）——即坑 **P-72**。该回调现已不挂任何表现。

## 三、运镜参数（`Units/StagePerformanceConfig.cs`）

| 参数 | 默认 | 含义 |
|---|---|---|
| `CutInCameraPitchDeg` | 42 | 推镜抬到的俯角（常规 `PilotPitchDeg`；单挑为 45 更正脸） |
| `CutInCameraDistance` | 46 | 推近后距离（常规 55），≈卡面 1.2×。只缩距离不动 FOV |
| `CutInCameraPushSeconds` | 0.3 | 推镜/撤镜各自时长 |
| `CutInCameraHoldSeconds` | 0.08 | 到位后、切横幅前的极短定格（横幅本身即强停顿） |

**为什么比单挑推得浅**（单挑 45° / 40）：cut-in 之后紧接的是出手与命中，
机位必须留得下突进位移、弹道与脚下裂地；推到只剩一张脸就看不见这一下打在哪。

## 四、屏幕构件与音画

- 构件（暗幕/阵营色斜带/巨幅立绘/大字标题，sorting 80~93）在
  `CutInService.PlaySolo`，挂在相机下随镜头走；层级登记见
  [rendering_layout.md](rendering_layout.md) §四。
- 音效 `sfx_cutin_solo`；BGM 全层 duck −8dB（`BgmLayerService.Duck`）。
- 满档源额外走强化出手音 `sfx_attack_empowered`。

## 五、巨伤的其余联动（同一次「重创」判据 `IsHighDamage`）

| 表现 | 值 | 位置 |
|---|---|---|
| 卡面命中特效 | `hit_massive`（RFX4 Effect15_Collision），压过一切专配 | `SkillPerformance.ResolveHitKey` |
| 震屏 | `MassiveShakeAmp` 0.55 / `MassiveShakeSeconds` 0.48（`CameraShaker.MaxOffset`=0.75） | `SkillPerformance.SettleDamage` |
| 命中裂地 | 强制 3 档 + 面积 ×1.5（与势能加强出手同级） | `GroundCrackService.PlayHit(massive:true)` |

查特效 key 归属统一走 [vfx_config_index.md](vfx_config_index.md)。
