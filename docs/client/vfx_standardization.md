# 特效标准化协议（画廊点名 → AI 标准化 → 加载接线）

> **权威**：人在画廊里**指出**要用哪件厂包表现后，AI **默认按本纪律**完成
> 标准化并接到可加载 key / 战法演出——**不依赖任何 GUI 晋升工具、不要求
> 人点菜单队列**。
>
> 配套背景：[vfx_pack_integration.md](vfx_pack_integration.md)；
> key 登记：[assets_upload_guide.md](assets_upload_guide.md)；
> 注册点：[extension_points.md](../discipline/extension_points.md)。
>
> 2026-07-26 定稿（明示：无 GUI 晋升流）。本文件 ≤500 行。

## 一、两层货架（勿混淆）

| 层 | 在哪 | 用途 |
|---|---|---|
| **原料** | 厂包目录（画廊里预览的那份） | 只审观感；**禁止**被 `PerformanceProfile` 直接引用 |
| **标准件** | `Assets/Resources/ClientBattle/VFX/<key>.prefab` | 真机/战报可加载；`VFXManager` 池化；尺寸自适应 |

画廊 = 选片台。点名原料 ≠ 已上线；上线必须有同名 Resources 标准件。

## 二、触发句与默认义务（工作纪律）

人说出下列任一意图时，AI **不得只口头答应或只改 Profile 指包路径**，
必须按 §三检查清单做完（或明确列出卡点）：

- 「用画廊里的 EffectN / 某某包某某件」
- 「把某某接到某某武将/战法/命中/光环」
- 「标准化某某并加载」
- **「要 EffectN 那种效果／参考 EffectN 的观感／做到接近 EffectN」**
  —— 点名一件厂包表现当**目标观感**，与直接点名接件同等触发本协议。
  哪怕最终一像素都没用上厂包资产，也必须走 §三：先定件、看清它的层构成、
  逐层判定「可迁移 / 需替代」，再落 Resources 标准件 + 登记 + 接线 + 验收。

> 红线（2026-07-26 定）：**禁止把「参考某 EffectN」理解成「凭印象手搓一个像的」**。
> 「照着感觉调参数」既无法复现观感，也让后来人不知道参照物是什么、差在哪。
> 参照类需求的额外交付物：在 §三.1 定件记录里写明
> **参照件路径 + 逐层去向表**（哪层直接晋升、哪层因 URP/管线不可用被替代、
> 替代方案是什么），并把该表落到对应机制文档（如 `ground_crack_language.md`）。
>
> 常见卡点（先查再动手）：厂包深度投影贴花（shader `KriptoFX/RFX1/Decal`、
> `KriptoFX/RFX4/Decal`）在 URP 下画不出（P-33，画廊逐件横幅也会标注），
> 这类层**只能替代不能晋升**；但同一件里的粒子/光/扭曲层通常可正常晋升
> —— 所以结论永远是「逐层判定」，不是整件一刀切可行/不可行。

**默认交付物**：

1. Resources 下新标准件 prefab（清洗 + `VfxFitter` 或 `VfxGroundLayer`）
2. `assets_upload_guide.md` 登记该 key
3. `PerformanceDatabase` / 对应 `PerformanceProfile` 字段写入 key（任务若只说
   「先入库不接线」则可跳过本步，但须在回复里写明）
4. `docs/dev/changelog.md` 一行
5. 用 MCP/`execute_code` 或菜单体检确认无品红；必要时跑目标战报抽检

**禁止**：

- 做「晋升队列 / 入队按钮 / 给人点的标准化 GUI」
- `PerformanceProfile` 序列化引用 `Assets/KriptoFX/**` 等包目录
- Play 模式里改 Prefab 资产
- 整包不拆层直挂；保留厂包深度贴花节点当「地面效果」
- 演出代码硬编码 key 或路径（必须走 Profile 三级查找）

## 三、AI 标准化检查清单（按序执行）

### 3.1 定件

- 记录：包名、prefab 名、Asset 路径、建议锚点（身/脚/弹道/罩身/地面）、用途
  （hit/proj/aura/cast/ground/shroud）。
- **看清层构成再动手**：厂包件常是「粒子 + 光 + 扭曲 + 投影贴花」混装。
  逐层列出 → 标注可迁移/需替代（判据见 §二 红线）。参照类需求必须产出这张表。
- 注意区分主件与碎件：`EffectN` 是完整表现（含地面层），
  `EffectN_Collision` 只是它的命中碎件——**点名 EffectN 时不要拿碎件替代**。
- 罩身件走 `VfxShroudFitter` 规格；弹道主件须知会命中碎件是否一并晋升。

### 3.2 起 key

- 路径：`Assets/Resources/ClientBattle/VFX/<key>.prefab`
- 命名（snake_case 前缀，与 guide 一致）：

| 前缀 | 用途 |
|---|---|
| `hit_` | 命中 |
| `proj_` | 弹道 |
| `aura_` | 光环/挂身 |
| `cast_` | 前摇 |
| `ground_` | 地面（必须 `VfxGroundLayer`，尺寸归裂地/法阵组件） |
| `shroud_` | 罩身 |

- 例：`Effect19_Collision` → `hit_effect19` 或沿用已有语义名 `hit_lightning`（若替换）。

### 3.3 落盘（编辑器 API / 可重跑脚本，非 GUI）

1. `AssetDatabase.CopyAsset`（或等价）从厂包路径拷到 VFX 目录；**不改包内原件**。
2. 打开拷贝：删除 `Projector`；删除 shader 名为 `KriptoFX/RFX1/Decal` /
   `KriptoFX/RFX4/Decal` 的死贴花节点（与 `VfxStandardizer` 同源）。
3. `ground_` → 挂 `VfxGroundLayer`，去掉 `VfxFitter`；其余 → 挂 `VfxFitter`
   （`Reference=CardWidth`，`Factor` 按画廊观感定，默认 1；`BakedBasis`＝
   **非交错**设计卡宽，见 pitfalls P-38）。
4. 可调用既有 `GreekMyth/特效/标准化 尺寸归一 + 清理残留` 做全量幂等回填
   （这是批处理维护，不是「给人点的晋升 GUI」）。
5. 扭曲层可留；知悉 Opaque 开关与低画质降级（见 pack_integration §E）。

### 3.4 登记与接线

1. `assets_upload_guide.md` §特效表加一行（来源包 + 备注）。
2. 改 `PerformanceProfile` 字段：`HitVfxKey` / `ProjectileKey` / `AuraKey` /
   `CastVfxKey` / `GroundPathKey` / `GroundHitKey` 等（以实字段为准）。
3. 罩身：`VfxShroudFitter.Fit` + `VfxShroudFollower.FitAndFollow`（跟随定位圆）。
   **默认完整件加载，通用类禁止裁层**。去石块/关 Trigger 等只在各技能挂载/
   Wire 名单里单独写（例：战神之勇 `AresMightStripContains` /
   `ApplyAresMightStrip`）。禁止 prefab 写死世界尺度。
4. 加载只经 `VFXManager.PlayAt/PlayOn(key)`。

### 3.5 验收（完成定义）

- [ ] Resources 存在该 key
- [ ] 无 Projector / 死贴花 / 品红
- [ ] 非地面有 `VfxFitter`；地面有 `VfxGroundLayer`
- [ ] guide 已登记；Profile 已填（若本任务要求接线）
- [ ] changelog 已写；回复里给出 **key + 接到哪**

## 四、标准件运行期属性（实现约束）

1. 尺寸自适应唯一入口：`VfxFitter` / `GroundCrackDecal` / `VfxShroudFitter`；
   演出禁止按机型写死 `localScale`。
2. 排序：非地面出池 `EnsureVfxSorting`；地面 `VfxGroundLayer` 豁免。
3. 池化：`OnEnable` 复位自身状态；粒子靠 Manager 重启。
4. ClientBattle **不**引用厂包运行时程序集；反射仅允许 Test/画廊，禁止热路径。
5. 编辑器接件若写成 `MenuItem`，必须是**可重跑的接线脚本**（如既有
   `WireMagicPackZeusAthena`），参数在代码常量里——给人重复点「晋升」的 GUI 不要。

## 五、与画廊的关系

- 画廊继续用于人眼过片（锚点/慢放/标记 M·N 可选）。
- **标准化不经过画廊按钮**；人用自然语言点名即可。
- 标记文件 `Temp/vfx_audit_marks.txt` 只作备忘，不是晋升前置条件。

## 六、相关入口速查

| 动作 | 入口 |
|---|---|
| 预览 | `GreekMyth/特效/特效画廊（一键）` |
| 批处理清洗已有标准件 | `GreekMyth/特效/标准化 尺寸归一 + 清理残留` |
| 体检 | `GreekMyth/特效/体检 全量 VFX prefab` |
| 加载 | `VFXManager` + Profile key |
| 本协议 | 本文件（点名后 AI 默认执行） |
