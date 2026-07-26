# 战场分区与站位几何（battlefield_layout）

> **唯一权威**：地面贴图矩形 → UI 侧栏 + 主战场 + 院区 + 隔离带 → 六等分格心。
> **微调入口**：`Assets/Scripts/ClientBattle/Units/BattlefieldLayoutConfig.cs`
> （静态字段；改数字后重新进 Play 生效）。
> 代码：`BattlefieldLayoutConfig` + `BattlefieldLayout` + `ArenaSlotLayout` + `StanceLayout`。
> 服务端阵型注册与加成见 [formations.md](../mechanics/formations.md)。

## 一、名词（与代码统一）

| 名词 | 定义 |
|---|---|
| 地面贴图区 | 近 3D 舞台不透明地面板世界矩形（宽 W、纵深 D₀） |
| UI 配置区 | 贴图左右各 **W/4**，不参与站位 |
| 逻辑主战场区 | 中间宽 **W/2**、纵深 **D=D₀×4/5** 的条带（远侧已抽出院区；可逻辑旋转） |
| 战场院区 | 原主战场远侧横条，纵深 **原 D₀/5**，主战场远缘 → 地天接缝，过渡天际线，无站位 |
| 逻辑隔离带 | 缩后主战场中央横贯，厚度 **D/8**（D=缩后主战场纵深），无站位 |
| 矩形站位区 | 隔离带近侧 = A、远侧 = B 的矩形 |
| 站位格 / 站位点 | 站位区六等分后的格子及其**中心** |
| 站位序列 | 队伍占用的 `position` 列表（1–6） |
| 预设阵型 | 六套精确集合；命中则 `formation_id` 非空，可挂整场被动 |

**废弃**：逻辑圆径向站位（`FrontRowRadial` / `BackRowRadial` / 弦长落点）；
客户端旧称却月/鹤翼/前列横排/六区格心作为阵型名；旧方圆 `{1,5,6}`（现称**箕形**）。

装饰用地面大圆可贴图保留（`ArenaSlotLayout.CircleRadius`），**不驱动站位**。

## 二、地面矩形 = 相机「正好拍全」动态反算（2026-07-26 定稿）

地面板尺寸**不吃设计常量**，由 `BattlefieldLayout.Recalc(aspect)` 按
`CameraFitter` 姿态常量解析求解，保证地面边缘恰好卡在屏幕边缘：

- **近缘 z** = 屏幕下沿视线与地面的交点（地面下侧卡屏幕下沿）；
- **半宽** = 屏幕左/右边缘视线在**地天接缝**深度处的半宽（侧边在接缝处
  恰好卡屏幕边缘；更近处板略宽于视野被裁掉，全程不露黑）；
- **远缘 z** = 地天接缝 `GroundFarSeamZ = 10`（天空板所在）。

站位分区在**缩后主战场**上做（远侧院区不占站位）→ 站位天然全部入画。

| 常量 | 默认值 | 含义（均在 `BattlefieldLayoutConfig` 可改） |
|---|---|---|
| `UiSideFraction` | 0.25 | 每侧 UI = W/4 |
| `CourtyardDepthFraction` | 0.2 | 院区 = 原主战场纵深 D₀/5 |
| `BeltDepthFraction` | 0.125 | 隔离带厚 = 缩后主战场 D/8 |
| `GroundFarSeamZ` | 10 | 地天接缝（远缘） |
| `DesignAspect` | 16:9 | 无相机/编辑期（VFX 基准）宽高比 |
| `CardScaleBoost` | 1.5 | 卡牌相对格尺放大 |
| `SlotJitterRadiusFactor` | 1/6 | 站位微抖半径 = 卡宽 × 此值 |
| `HoverRatio` | 0.2 | 浮空 × 卡高 |
| `RotationDeg` | 0 | 主战场逻辑旋转 |

参考值（俯角 45°、距离 55、FOV≈11.7°）：地面纵深 D₀≈13.0 → 院区 ≈2.6、
缩后主战场 ≈10.4；隔离带中心略偏近端（相对地面中心向相机移 D₀/10）。

## 二b、逻辑旋转（`RotationDeg`，默认顺时针）

主战场区（含隔离带）可绕**缩后主战场中心**整体逻辑旋转，调参入口
`BattlefieldLayout.RotationDeg`（度，正 = 俯视顺时针，接收 |θ| < 90）。
院区不参与旋转分区。卡牌只随格心平移、**不自转**。

- **修正规则**：V = (MainDepth/2 − M·|sinθ|) / cosθ；θ=0 退化为 MainDepth/2。
- **隔离带**只旋转、不修正（厚度相对缩后主战场 D 的 1/8）。
- 卡尺锁定 θ=0 格尺（`CardCellDepth`）；旋转不改卡尺寸。

## 三、六区编号（每队局部，语义不变）

```
        隔离带
   ┌───┬───┬───┐
   │ 1 │ 2 │ 3 │  ← 前排（朝向隔离带 / 敌方）
   ├───┼───┼───┤
   │ 4 │ 5 │ 6 │  ← 后排
   └───┴───┴───┘
     左  中  右
```

- A = 近（相机侧），B = 远；B 队局部前排仍朝向隔离带（镜面）。
- 列：左 = 1·4，中 = 2·5，右 = 3·6。

## 四、落点规则

1. `BattlefieldLayout.SlotCenterXZ(teamIdx, position)` → **原站位点**（格心）(x, z)。
2. **实际站位点** = 以原站位点为圆心、半径 `CardWidth/6` 的圆盘内均匀随机点
   （`StanceLayout.SampleSlotDiskOffset`；建卡与回位重采样同源）。
3. `ArenaSlotLayout.GroundPoint`：卡牌**下边缘中点**投到实际站位点（浮空 + 后倾补偿）。
4. 正交回退：`StanceLayout.SlotCenter` 将设计 z 映到 XY 平面，同一六等分 + 圆盘抖动。
5. `HomePosition` = 原站位点（格心）；`RestPosition` / 初始姿态 = 圆盘采样点。

## 五、预设阵型（精确集合相等）

| id | 中文名 | 站位集合 |
|---|---|---|
| `yizi` | 一字阵 | {1,2,3} |
| `zhui` | 锥形阵 | {2,4,6} |
| `ji` | 箕形阵 | {1,5,6} |
| `fangyuan` | 方圆阵 | {3,4,5} |
| `yanyue` | 偃月阵 | {1,3,5} |
| `yanxing` | 雁行阵 | {1,2,6} |

客户端：`StanceLayout.DetectFormation`；服务端：`formations.detect_formation`。
任意传入序列均可；不匹配 → `None` / `formation=""`（无加成）。
配将只改站位，服务端 `TeamSetup.formation` 为只读自动识别。

## 六、单体制卡尺

- `StanceLayout.Recalc`：按单格宽高与垫缝反算 `CardWidth` / `CardHeight`（**一档**）。
- 设计布局下参照卡宽 ≈ **1.730**（θ=0 缩后主战场格尺 × `CardScaleBoost` 1.5）。
- 屏幕上行距随格纵深缩小；同列前后排同时占位仍可能遮挡。
- VFX：`VfxFitter.BakedBasis` 由菜单「标准化」回填为当前参照卡宽。

## 七、与其它文档

- 舞台/相机/地天板：[arena_stage.md](arena_stage.md)
- sorting / 机型适配：[rendering_layout.md](rendering_layout.md)
- 结算侧阵型加成：[formations.md](../mechanics/formations.md)
