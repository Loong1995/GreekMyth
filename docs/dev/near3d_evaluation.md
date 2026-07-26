# 近 3D 战斗表现方案评估（默认卡-地夹角 45°）

> 决策文档：近 3D 呈现 + 舞台静态资源出图规范。
> **定稿（2026-07-25）**：俯角 45°；地/天**分图正交**；图**横向全宽铺满**（UI 叠两侧，不裁掉舞台）；
> 竞技场仍在画面**水平正中**；卡只落中区；天空垫地面定稿；神=正午 / 人=黎明 / 妖=暗夜。
> §七 = 可粘贴指令。

## 〇、结论（TL;DR）

A+：45° + 地/天正交分图 + 分层卡。**防左右突兀 ≠ 把舞台裁成中间条**，而是：
**天空/地面都按横屏全宽生成并铺满**，左右 1/4 只叠半透 UI；卡与技能演出仍只在中区竞技场。

## 一～六（方案摘要）

- 相机：`PilotPitchDeg=45`；卡 FaceCamera。
- **铺满**：地、天贴图 cover 全屏宽；UI 浮在左右，不挖空底图。
- **中区**：水平中央 ≈50% 宽为竞技法院（卡只在此）；左右翼是同材质地面延伸元素（不是空、不是大看台）。
- **竖向**：合屏上≈1/3 天、下≈2/3 地+卡；地天接缝靠同色雾/软边，禁止硬切。
- 云：画进天空底图（神/人必有；妖改雾气微粒）；滚动云带可选后期加。
- 神像：天空层独立贴图，有几率淡入。

## 六b、资源协议与落地进度（2026-07-25）

- **目录协议**：所有 arena 图片 → `Assets/Resources/ClientBattle/Arena/`；
  命名 `arena_<stage>` / `sky_<stage>` / `statue_<name>`（stage=olympus/troy/abyss）。
- **代码**：`Units/ArenaStageView.cs`——地面平躺板（y=-5.2，z∈[-7,14]，sprite 顶边=远端）
  ⊥ 天空竖板（z=14，底边接缝，cover 铺满）；两图齐备且透视模式即自动替换平面背景。
- **工作计划**：
  1. ✅ olympus 地/天上传 + 拼接组件 + 卡牌照常落中央（本次）。
  2. 接缝调优：雾带/渐变遮接缝；GroundFarZ/相机距按截图微调。
  3. 神像 `statue_hera` 天空层淡入（几率触发，接舞台机制）。
  4. troy / abyss 两舞台复用同管线出图接入。

## 七、主题天空 / 主题地面 · AI 出图指令

### 7.0 使用约定

| 资产 | 空间 | 画幅 | 职责 |
|---|---|---|---|
| 主题地面 | 水平 Quad | **16:9 横屏**正俯视 | **全宽**地面；中央竞技场；左右翼铺地面元素 |
| 主题天空 | 竖直板（⊥地面） | **16:9 横屏** | **全宽**天穹；含云/雾；底边软过渡 |
| 神像 | 天空层叠加 | 竖图透明 | 不进底图 |

铁律：
1. 地、天分两次生成；画幅均为横向铺满用的 16:9（如 1920×1080 / 3840×2160）。
2. 地面正俯视正交；天空平视/微仰；**禁止**天空里画地板。
3. **竞技法院在地面图水平正中**；卡运行时只落此区。
4. 左右不是留白：用同主题地面元素填满（石径、浅浮雕、稀疏裂缝等），供 UI 半透叠上。
5. 地↔天：色板连续 + 接缝雾化；远近用大气透视做景深（见母版）。

---

### 7.1 主题地面 · 通用母版

```
Mobile card-battler STAGE FLOOR, FULL-WIDTH landscape plate. Aspect 16:9 (e.g. 3840x2160). Orthographic TOP-DOWN (zenith), NO vanishing point, NO horizon, NO sky, NO side-view elevation.

Layout (horizontal — critical):
- CENTER ~50% width = COMBAT COURT (where floating cards will sit). Large, calm, low-contrast paving. This is the hero of the image.
- LEFT ~25% + RIGHT ~25% = continuous GROUND DRESSING in the SAME material family (paths, sparse inlays, faint cracks, soft plaza stones) — fill the wings completely. NOT empty bars, NOT UI panels, NOT a second arena, NOT wide bleachers.
- Thin court boundary ring only (~3–5% of court radius). Outer “stands” if any = ultra-thin edge accent, never dominating.

Depth / near-far on this top-down plate (for 45° game camera later):
- Image TOP edge = FAR (meets the sky cyclorama): slightly cooler, softer contrast, finer or faded detail, gentle misty desaturation toward the sky palette.
- Image BOTTOM edge = NEAR (toward player): slightly larger material scale, sharper, a touch more contrast.
- Smooth continuous gradient between near and far — no hard horizontal seam. Suggest aerial depth without true perspective distortion of the slabs.

Court still ~70%+ of the VERTICAL usable court area (center band), calm enough for 6 cards + VFX. Wings may be a bit busier than center but stay quieter than a city map.

Lighting: soft, even, match stage time-of-day mood (noon / dawn / night via theme block). No character shadows, no baked spotlights.

EXCLUDE: wide spectator bowls, majority bleachers, characters, cards, UI, text, statues, upright pillars, sky, clouds.

Style: epic mythic realistic, KriptoFX / Magic Pack 1 family. Output: full-bleed 16:9 floor albedo for a horizontal ground quad.
```

### 7.2 主题地面 · 舞台主题块

**神 · 奥林匹斯**
```
Olympus FULL-WIDTH marble summit plaza, 16:9 top-down. CENTER: vast ivory/pale-gold combat court, almost plain, thin gold inlay ring. LEFT/RIGHT wings: same marble continuing as processional stone paths and sparse gold geometry — filled, not blank. FAR(top) edge cooler misty marble; NEAR(bottom) clearer slabs. No Hera statue. Reject if court is a small island or wings are empty/bleacher-heavy.
```

**人 · 特洛伊**
```
Trojan FULL-WIDTH arena apron, 16:9 top-down. CENTER: large calm packed-sand court, thin sandstone curb. WINGS: continuous sand/stone apron, faint ruts and bronze grit — filled ground, not stands bowl. FAR edge dustier/softer; NEAR edge clearer sand grain. No Atlas, no corpses.
```

**妖 · 冥海裂渊**
```
Abyssal FULL-WIDTH reef floor, 16:9 top-down. CENTER: large calm black-stone court, thin bioluminescent green vein ring. WINGS: wet obsidian and sparse tide-texture continuing outward — filled, not empty. FAR edge darker vapor-soft; NEAR edge wetter specular stone. No kraken, no idols.
```

### 7.3 主题天空 · 工序 + 通用母版

**工序**
1. 先定稿 16:9 全宽地面。
2. 天空生成时**垫该地面图**（只借色板/材质/光感，不抄俯视构图）。
3. 参考权重中偏低；出现地板圆环 → 降权重出。

**通用母版**
```
Mobile card-battler STAGE SKY, FULL-WIDTH landscape 16:9 cyclorama (e.g. 3840x2160). Stands ORTHOGONAL to the ground quad. Cover the entire screen width (UI will overlay the sides later — do NOT leave blank side bars in the art).

USE attached STAGE FLOOR only as color/material/mood reference. Do NOT copy top-down court, rings, or wing layout into the sky.

Eye-level or slight low-angle distant sky WITH atmospheric DEPTH:
- FAR elements (distant peaks, haze, high clouds): smaller, softer, lower contrast, more aerial perspective.
- NEARER sky elements (larger cloud masses if any, lower mist): slightly clearer but still background — never foreground props.
- Soft continuous depth, no cardboard cutout layers.

CLOUDS / ATMOSPHERE (required by theme block): include them as painted sky content. Keep the horizontal CENTER of the upper sky relatively calm for optional floating statue overlays.

BOTTOM EDGE TRANSITION (critical — avoid hard cut with the ground plate):
- Lowest ~15–20% of the image = soft fog / mist / dust / vapor in colors sampled from the referenced floor’s FAR-edge tint.
- NO arena floor, pavement, sand court, or marble slabs.
- No hard horizon line cutting across like a stripe; dissolve into atmosphere so the vertical sky plate can meet the horizontal ground without a visible seam.

No UI, text, cards, heroes, large baked statues. Style matches reference floor + KriptoFX family.

Output: full-bleed 16:9 sky plate for a vertical quad.
```

### 7.4 主题天空 · 舞台主题块（时段 + 云）

**神 · 正午**
```
HIGH NOON Olympus heaven, full-bleed 16:9. Deep daytime azure, bright zenith light. CLOUDS: crisp sunlit cumulus / layered clouds with sharp lit edges and soft bases — present but not covering the whole sky; leave center-upper calmer for statue overlays. Distant peak haze only (far = softer). Bottom: soft bright midday mist tinted toward ivory/gold floor far-edge — seamless to ground, no floor geometry. Reject night or blue-hour.
```

**人 · 黎明**
```
DAWN Trojan sky, full-bleed 16:9. Upper cool blue, low rose-gold/amber. CLOUDS: required — long dawn clouds with warm undersides and cooler tops; gentle volume, not storm wall. Far silhouettes softer. Bottom: soft dusty dawn fade matching sand/bronze floor far-edge. No sand floor in frame. Reject harsh noon or pitch night.
```

**妖 · 暗夜**
```
DEEP NIGHT abyssal vault, full-bleed 16:9. No daylight. Skip fluffy white clouds; use dark vapor sheets, wet mist, sparse bioluminescent teal motes (echo floor veins). Far cavern rim softer/darker. Bottom: dissolve into dark vapor matching black/teal floor far-edge. Reject blue daylight sky or sunrise gold.
```

### 7.5 负面词

**地面**
```
square 1:1 crop, empty side bars, blank left right margins, UI panels, wide stands, large bleachers, seating bowl, small center court island, perspective vanishing point, horizon, sky, clouds, characters, cards, text, watermark, cartoon, anime, hard horizontal seam, busy center court
```

**天空**
```
arena floor, pavement, marble slabs, sand court, top-down layout, empty side bars, hard horizon stripe, abrupt bottom cut, characters, large statue, cards, UI, text, cartoon, anime, wrong time of day, flat cardboard clouds without depth
```

分舞台追加——神：`night, dusk, moonlight`；人：`high noon, pitch black`；妖：`daylight, blue sky, bright white cumulus, sunrise`

### 7.6 验收清单

**地面**
1. **16:9 全宽**；中央≈半宽是冷静竞技场；左右翼有同材质地面元素（非空、非大看台）。
2. 上远下近：有柔和景深/大气差，无硬缝。
3. 正俯视无天空；中区能稳托 6 卡。

**天空**
1. **16:9 全宽**；垫了对应地面；神/人含云，妖含暗雾微粒。
2. 底边雾色贴地远缘，合屏地天不硬切；远近云/雾有大气透视。
3. 时段正确；中上留空给神像。

**合屏**
1. 地天全宽铺满；左右 UI 半透叠上，不挖空舞台。
2. 卡与战斗 VFX 只在中央竞技区；翼区不落卡。

