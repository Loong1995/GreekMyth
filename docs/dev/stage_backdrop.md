# 天幕屏方案（StageBackdrop：挂相机全屏背景）

> 状态：设计定稿（2026-07-29），随 [stage_skew_impl.md](stage_skew_impl.md)
> 实施步 3 落地。本文回答"天幕屏怎么做"：几何、渲染、动态、画质档、
> 迁移与坑位。相机权威：[../client/arena_stage.md](../client/arena_stage.md)。

## 〇、一句话

天幕屏＝**挂在主相机下的两块 Quad**：远层不透明全屏（永远盖满视野、跟随
滚转与一切运镜，物理上不可能露边/露黑角），近层半透明小面积动态。
取代现行 `ArenaStageView` 的"世界摆放竖直天空板 + 每帧对缝取尺"。

```
Camera.main
 └─ StageBackdrop
     ├─ FarLayer   不透明 Quad，local z = Zfar，盖满视锥截面 ×1.1
     └─ NearLayer  半透明 Quad，local z = Znear，只盖构图指定区域（≤1/3 屏）
```

## 一、为什么挂相机（对比现行世界摆放）

| | 现行竖板（世界摆放） | 天幕屏（相机子物体） |
|---|---|---|
| 对缝 | 每帧反算屏顶高度/斜向宽度对缝（`FitToCamera` ~20 行三角） | 无缝可对：屏就在视野里 |
| 相机滚转 | 露黑角 → 历史上直接禁掉 roll | 跟随滚转，天然全覆盖 |
| 单挑/cut-in 运镜 | 相机一动就可能露边 | 跟随运镜，永不露边 |
| 分辨率热切换 | 依赖每帧取尺 | 只在 fov/aspect 变化帧重算一次 |

这是 [stage_skew_impl.md](stage_skew_impl.md) §二c 滚转解禁的前提条件；§二b 横竖屏支持同样以本方案为前提。

## 二、几何

- **FarLayer**：local position `(0,0,Zfar)`、无旋转。Quad 是 1×1 单位，
  `localScale = (2·Zfar·tan(fov/2)·aspect·M, 2·Zfar·tan(fov/2)·M, 1)`，
  冗余 `M = 1.1`。
- **Zfar 取值**：必须"比场上一切都远、又在远裁剪面内"。相机 pilot 距离量级
  ~15，场地纵深 ~10，取 **Zfar = 60**，并校验 `cam.farClipPlane ≥ 80`
  （不足则抬远裁剪面，透视投影深度精度足够）。
- **NearLayer**：local z 取 **50**（略近于远层即可，视差滚动靠 UV 速度差而
  不是真实深度差）；尺寸/局部偏移由每舞台构图记录给出（如"上侧 1/3 横带"），
  同样按视锥截面比例换算，保证任何宽高比下占屏比例恒定。
- **重算时机**：缓存 `fov/aspect`，`LateUpdate` 里发现变化才重算 scale
  （对比现行每帧全量取尺是净简化）。

## 三、渲染

**FarLayer（不透明）**：

- 材质同 `ArenaStageView.BuildGroundMaterial` 的配方：`URP/Unlit`、
  `_Surface=0`（Opaque）、`ZWrite On`、`Cull Off`、queue = Geometry。
- 不透明队列按前到后排序，天幕最远最后画，被地面/道具的深度自然裁剪，
  **零 overdraw 浪费**；同时它写深度，厂包屏幕空间贴花在"天上"没有表面
  可投也不会出鬼影（有表面总比没有强，P-32 同理）。
- 贴图 wrap = Repeat（UV 滚动需要）、ASTC、关 mipmap 偏移不用管
  （屏幕距离恒定）。**关雾**：材质不受 Fog 影响（URP/Unlit 默认即无雾）。

**NearLayer（半透明）**：

- `URP/Unlit` Transparent（`_Surface=1`），**不写深度**；渲染在透明队列，
  必须排在所有战场 Sprite/粒子之前：挂 `SortingGroup` 或直接
  `MeshRenderer.sortingOrder = -120`（低于现行天空板的 −110，其余体系不动）。
- 面积纪律 ≤1/3 屏（[../client/vfx_mobile_budget.md](../client/vfx_mobile_budget.md)
  半透明 overdraw 红线）；alpha 边缘柔和，禁止全屏。

**动态（UV 滚动）不写自定义 shader**：C# 每帧
`material.mainTextureOffset.x += speed · Time.deltaTime`（远层 ~0.002/s，
近层 ~0.01/s，方向每舞台配置）。两次浮点加法，成本为零；想要"涌动"再
考虑极简扰动 shader，不作为首期内容。offset 用 `Mathf.Repeat` 回卷防
浮点漂移（长局挂机数小时后精度劣化）。

## 四、画质档

跟 `VfxTierScale` 档位联动（同一份档位来源，不另起开关）：

| 档 | FarLayer | NearLayer |
|---|---|---|
| 高 | 滚动 | 滚动 |
| 中 | 滚动 | 静止（保留画面） |
| 低 | 静止 | 关闭（SetActive false） |

## 五、API 与接入

```csharp
// 新文件 Units/StageBackdrop.cs（~60 行）
public class StageBackdrop : MonoBehaviour
{
    // 由 ArenaStageView.TryBuild 在建场时调用；资源 key 来自舞台构图记录
    public static StageBackdrop Attach(Camera cam,
        Sprite far, Sprite near /*可 null*/, BackdropLayout layout);
    public void SetTier(VfxTier tier);
    // LateUpdate: fov/aspect 变化才重算 scale；每帧滚 UV offset
}
```

- `ArenaStageView`：删除 `_sky` 字段、`FitToCamera` 的天空段与
  `SkyMargin`/`GroundFarSeamZ` 对缝依赖；`TryBuild` 里改调
  `StageBackdrop.Attach(Camera.main, ...)`。地面段保持不动
  （高台化是 stage_skew_impl §五的另一条目，两者可分开落地）。
- 资源协议沿用 `Resources/ClientBattle/Arena/sky_<stage>.png`（远层），
  新增 `skynear_<stage>.png`（近层，可缺省＝无近层）。
- 生命周期：跟随 `ArenaStage` 根节点销毁；相机子物体在战斗结束清场时
  显式 Destroy（防挂在常驻相机上泄漏到下一场）。

## 六、坑位

1. **远裁剪面**：Zfar=60 必须 < `farClipPlane`；接入时断言并自动抬升。
2. **只挂主战斗相机**：`Camera.main` 缓存后挂载；若未来出现独立 UI 相机/
   截图相机，天幕不复制（背景只属于战斗相机）。
3. **cut-in 面共存**：cut-in 面也是相机子物体（local z 很近）。天幕远层是
   不透明 Geometry 队列、cut-in 面是透明队列且近得多，先后天然正确；
   近层 sortingOrder=−120 远低于 cut-in，同样无冲突。逐舞台验收仍要目检
   （stage_skew_impl §八）。
4. **单挑推近**：`StageCameraRig` 只改相机位姿不改 fov 时，天幕 scale 不需
   重算（子物体跟走）；若未来运镜改 fov，`LateUpdate` 的变化检测已覆盖。
5. **UV offset 漂移**：长局必须 `Mathf.Repeat(offset, 1)`（§三）。
6. **贴图接缝**：远/近层横向必须无缝（出图指令已约束）；导入端 wrap 设
   Repeat，Clamp 会在滚动时拉出边缘条纹。
7. **色彩空间**：Unlit 直出，AI 出图的暗部在真机 ASTC + 线性空间下会更闷，
   出图后在目标机上核一次暗部层次（妖舞台暗部占七成，最敏感）。
8. **迁移期双轨**：天幕屏落地当场删除旧 `_sky` 路径，禁止两套天空并存
   （旧板留着会在滚转时露出来穿帮）。

## 七、人 · 黎明动态天幕（首个落地件，完整实施）

### 七a、动态设计（黎明语义）

**硬规则：带点源（日盘/明显辉光中心）的远层禁止滚动**——滚动会让太阳
平移穿帮。黎明天幕按此拆动态：

| 层 | 内容 | 动态 |
|---|---|---|
| 远层 `sky_troy` | 黎明海天全景，暖金天光在画面一侧、**日盘弥散化**（不画锐利太阳） | 若无点源：横向缓滚 0.002/s；若保留辉光中心：**静止**，改用"天光呼吸"——材质 color 在 1.00~1.06 间以 ~90s 周期 lerp（黎明微光脉动，一行代码零成本） |
| 近层 `skynear_troy` | 低空薄雾流（alpha，疏密不均） | 横向滚 0.010/s，方向与远层同向略偏（视差感） |

### 七b、美术资源获取（两条路径）

**路径 A（首选，免费 CC0）：Poly Haven 实拍全景裁带**

1. [polyhaven.com/hdris/sunrise-sunset](https://polyhaven.com/hdris/sunrise-sunset)
   全部 CC0（商用免授权）。首选
   [Blouberg Sunrise 1](https://polyhaven.com/a/blouberg_sunrise_1)（海面日出，
   最贴爱琴海黎明）；备选 [Qwantani Sunrise Pure Sky](https://polyhaven.com/a/qwantani_sunrise_puresky)（纯天无地物）。
2. 下载 **4K Tonemapped JPG**（不要 EXR，Unlit 直出用不上 HDR）。
3. 裁取地平线上方横带 → 缩到 2048×1024。等距柱状全景**横向天然无缝**，
   裁切只许上下裁、左右必须保留整周（或整数等分），否则无缝性被破坏。
4. 调色对齐风格卡（stage_plan §四.0 史诗写实）：暖金主调、四角压暗两成
   （UI/跳字可读）、底缘 15% 渐入暖灰雾色（与地面外缘色温缝合，
   [stage_skew_impl.md](stage_skew_impl.md) §三b.2）。
5. 实拍地平线若含城市/船只剪影，用 AI 局部重绘抹平。

**路径 B（备选，AI 生成）**：用 stage_skew_impl §六"人 · 天幕"中文指令
生成 → 无缝化：画布横向平移 50%（wrap）暴露接缝 → AI 局部重绘接缝带 →
再平移回来；交付前用 offset-wrap 预览验一整周。

**近层薄雾**（三选一，成本递增）：Krita/Photoshop 分层云滤镜
（本身可无缝平铺）→ 色阶拉出疏密 → 转 alpha；或 AI 生成 tileable
云雾图；或 CC0 烟雾素材拼接。要求：1024²、alpha 边缘柔和、疏密不均
（均匀雾滚起来像贴图平移）。

### 七c、导入与接线

- 导入：wrap = **Repeat**、sRGB、ASTC 6×6、关 mipmap（屏幕距离恒定，
  省显存）、aniso 1。落位 `Resources/ClientBattle/Arena/sky_troy.png` +
  `skynear_troy.png`（资源协议 §五）。
- 接线：`StageBackdrop.Attach` 读舞台构图记录中的资源 key 与滚速/脉动参数；
  画质档行为按 §四（中档近层静止、低档近层关闭；"天光呼吸"属远层动态，
  低档一并静止）。
- 排期：资源半天（路径 A 拉图+调色）＋接线依赖 StageBackdrop 落地
  （stage_skew_impl §九步 3）＋真机核暗部与帧率半天。

### 七d、专项验收（叠加 §八通用项）

- [ ] 远层无点源平移穿帮（滚动版）或呼吸周期不可察觉突变（静止版）
- [ ] 近层雾流方向与远层视差自然，10 分钟无接缝
- [ ] 暖金暗部在真机 ASTC + 线性空间下层次仍在（§六.7）
- [ ] 神舞台复用同一套黎明天幕 + roll −30° 直接过验收

## 八、验收

- [ ] 三舞台 + 三 roll 角：任意宽高比（4:3 / 21:9）、单挑、cut-in 全程不露边
- [ ] 远层无雾染色、无 mip 糊；近层面积 ≤1/3 屏、边缘无 Clamp 条纹
- [ ] 滚动 10 分钟无接缝跳变、无漂移卡顿（`Mathf.Repeat` 生效）
- [ ] 画质三档切换行为符合 §四表
- [ ] 战斗结束清场后相机下无残留子物体
- [ ] `FrameSpikeProbe` 真机满帧；对比接入前后 overdraw 无回退
