# 资源清单·现状·成品化路线（ClientBattle 唯一资源文档）

> 覆盖：需要哪些资源、每类现在是什么状态、怎么一步步配成成品。
> 上传规则：真实资源按下表路径与文件名放进 `Assets/Resources/ClientBattle/`，
> **同名即自动生效，零代码改动**；找不到文件自动回退程序化占位，永不缺资源。
> 采购登记也在本文（§三），旧 `to_purchase.md` 已并入删除。

## 一、资源清单与现状（2026-07-10）

| 类别 | 数量 | 现状 | 成品缺口 |
|---|---|---|---|
| 特效 Prefab | 25 key | **已购三包配齐 v1**（variant 已目视校准尺寸） | 光环/前摇质感一般，可后续换专属 |
| 整盘滤镜 | 3 key | 程序化色罩（BoardFilterOverlay），**无需上传** | 真棋盘底图定稿后调透明度 |
| 状态图标 | ~30 | 哈希配色色块占位 | 全部待上传 |
| 立绘 | 24 | 阵营色块+首字母占位 | 全部待上传 |
| 音效 | ~40 key | 程序化合成哔声占位 | 全部待上传（已购 Universal Sound FX 在手） |
| UI/卡框 | 4 张 | 程序化圆角矩形占位 | 全部待上传 |

### 1. 特效 Prefab `Resources/ClientBattle/VFX/<key>.prefab`

来源为已购三包的 Prefab Variant（斩击/穿刺/裂甲图标 ← Cartoon Coffee 2D Slash；
雷电/命中/对撞/爆炸/光环族 ← Vefects Combat Flipbook；弹道/治疗/石化命中 ←
Assets/VFX 四色弹道包）。**换演出＝替换对应 variant 或改其引用，不动代码。**

| key | 用途 | 备注 |
|---|---|---|
| `slash` / `magic_bolt` | 默认刀光 / 魔法光弹道 | 普攻斩击×1.0、追击×1.5（代码规则） |
| `hit_generic` / `heal_generic` | 默认命中 / 治疗命中 | |
| `cast_oracle` / `aura_generic` | 神谕前摇 / 默认光环 | |
| `aura_thunder` | 雷霆神谕：闪电缠绕（常驻） | aura_* 挂载时代码强制循环+补发射密度+压半透明（UnitAuraService），一次性特效也能常驻 |
| `lightning_strike` / `hit_lightning` | 落雷弹道 / 命中 | 雷霆触发专属 |
| `aura_aegis` / `hit_shield_counter` | 圣盾光环 / 反制命中 | |
| `aura_bloodlust_weak` / `aura_bloodlust_strong` | 战神怒火弱/强血红 | 另有 Intensity 参数 |
| `aura_sunlight` | 呼吸阳光（德尔斐/尼刻复用） | |
| `aura_hermes_mark` | 神使/扰心印记 | |
| `icon_spear_crack` | 阿喀琉斯裂甲图标（超大） | ExtraIconScale=2.6 |
| `icon_trojan_bomb` / `hit_explosion_crack` | 木马炸弹图标 / 裂开爆炸 | |
| `proj_flying_sword` / `hit_sword` | 珀尔修斯飞剑弹道 / 命中 | |
| `proj_wave` / `hit_wave` | 海神水浪弹道 / 命中 | |
| `hit_pierce` / `hit_petrify` / `hit_clash` | 穿刺 / 石化反噬 / 单挑对撞 | |

尺寸红线：variant 根缩放按**目视校准**（禁止按包围盒归一，拖尾会把包围盒撑到
几十单位）；现值：弹道/治疗/命中 1.0、剑击/穿刺 0.35、slash 0.25、光环 0.9~1.4。
演出层只允许相对缩放（`*=`）。

### 2. 状态图标 `Resources/ClientBattle/StatusIcons/<status_id>.png`

- 控制类（卡牌中央大图标，优先做）：`silence disarm hesitation petrify ming_lock charm`
- 常规状态（卡牌上方小图标）：`thunder divine_revelation aegis_shield blood_battle
  ares_might sun_blessing snake_staff_protection moon_hunt nike_wings first_strike
  achilles_wrath heracles_trials lion_counter trojan_scheme trojan_bomb perseus_mirror
  poseidon_tide flood styx_blood_oath shadow_veil medusa_gaze …`
  （全量 id 见 `Names/ChineseNames.cs`，未上传自动哈希配色色块）

### 3. 立绘 `Resources/ClientBattle/Portraits/<template_id>.png`

24 名武将，文件名 = roster 模板 id：
`zeus athena ares hermes apollo asclepius artemis nike`
`achilles heracles odysseus perseus atalanta paris ajax chiron`
`poseidon amphitrite triton siren scylla charybdis`
`hades medusa persephone charon thanatos cerberus`

### 4. 音效 `Resources/ClientBattle/SFX/<key>.wav`

- 默认族：`sfx_active_default sfx_melee_default sfx_pursuit_default
  sfx_status_trigger_default sfx_oracle_default sfx_hit_default sfx_heal_default
  sfx_defeated`
- 状态施加：`sfx_status_<status_id>`（同帧与伤害音效由 SfxManager 去重）
- 专属：`sfx_thunder_strike sfx_aegis_counter sfx_achilles_pierce sfx_trojan_explosion
  sfx_perseus_swords sfx_trident_quake sfx_medusa_gaze sfx_petrify_on sfx_petrify_off
  sfx_duel_horn sfx_duel_clash sfx_duel_win sfx_trials_counter
  sfx_oracle_thunder sfx_oracle_aegis sfx_oracle_ares sfx_oracle_apollo
  sfx_oracle_hermes sfx_oracle_nike sfx_oracle_poseidon sfx_oracle_hades`

### 5. UI 与卡框

| 文件 | 规格 | 说明 |
|---|---|---|
| `UI/chat_bubble.png` | 256×160，白底圆角+左下尾巴，透明背景 | 台词气泡底板；字号/折行由代码控制，上传底板即可 |
| `UI/board_background.png` | ≥2048×1152（16:9 基准） | 棋盘背景，cover 铺满不变形；**未上传时为无色（纯黑）** |
| `CardFrames/frame.png` | 512×692（1.7:2.3），白/灰阶底 | 通用卡框，代码按阵营色染色 |
| `CardFrames/petrify.png` | 同 frame 尺寸 | 石化覆盖层（灰石纹+裂缝），代码做淡入淡出 |

## 二、成品化路线（把演出从占位配成成品）

> 原则：一次只换一类资源，换完跑一遍验收（§二.6），改配置不改代码。

### 步骤 1：状态图标（半天，收益/成本最高）
[game-icons.net](https://game-icons.net)（CC BY 3.0，需署名）按 §一.2 清单逐个
导出 PNG（128px、统一底色风格），按 status_id 命名放入目录即生效。

### 步骤 2：音效（一天体力活）
Package Manager 导入已购 Universal Sound FX → 按 §一.4 清单逐 key 挑选、
转 wav 改名放入。神谕类可再叠免费圣咏垫底音（freesound.org 筛 CC0）。

### 步骤 3：立绘（观感提升最大）
AI 生图初稿（统一 prompt："greek mythology, 2D card game portrait, bust,
painterly"）全量 24 张先上；后续外包精修主推 8 将（¥200~500/张）逐批同名替换。

### 步骤 4：UI/卡框
kenney.nl UI Pack（CC0）或 Asset Store fantasy card frame（$10~25）改色；
石化版拿正式卡框去色+叠裂纹（opengameart 搜 crack，CC0）。棋盘底图定稿后
回调整盘滤镜透明度（`BoardFilterOverlay` 的 baseAlpha 系数）。

### 步骤 5：特效精修（最后打磨）
1. 已购包内换款：想换某个演出，在源包里挑新 prefab，替换
   `Resources/ClientBattle/VFX/` 对应 variant 的引用（保持文件名不变）。
2. 光环族升级：补购 "magic aura loop 2D"（$10~20）类循环光环包，
   替换 aura_* variant（循环包无需 UnitAuraService 的强制循环也可用）。
3. 新尺寸校准：Play 模式目视 → 改 variant 根缩放 → 记录到 §一.1 尺寸红线行。

### 步骤 6：配置资产化与调参（成品前必做）
1. 菜单 Assets→Create→GreekMyth→Performance Database 建
   `Resources/ClientBattle/PerformanceDatabase.asset`（不建则用代码内置默认，
   字段一致；**正式期用资产，代码默认只作兜底**）。
2. Inspector 调参：滤镜/光环浓度 Intensity(0~3)、裂甲图标 ExtraIconScale、
   斩击 StrikeVfxScale、每战法的 VFX/SFX key 覆盖——全在资产里改。
3. 验收：跑 `Test/BattleReportTester`，标准＝Console 无「无特殊演出配置」警告、
   无合成哔声、每类战法（普攻/群攻/追击/神谕/状态触发/单挑）肉眼过一遍。

## 三、采购登记（原 to_purchase.md 并入）

> 红线：本项目是 URP 2D Renderer，商店页写明 "NOT with the 2D render
> pipeline" 的包一律不买。购买用与工程一致的 Unity ID，购后我在
> Package Manager > My Assets 拉取导入并登记。

| 资产 | 价格 | 购买日期 | 状态/已用于 |
|---|---|---|---|
| 2D Cartoon/Anime Effects - Mobile Friendly | $4.99 | 2026-07-05 | 已导入 `Assets/VFX/`：弹道/治疗/石化命中 variant |
| Combat Flipbook VFX - URP | $39.97 | 2026-07-05 | 已导入 `Assets/Vefects/`：雷电/命中/光环/爆炸 variant |
| 2D Sword Slash VFX | $19.99 | 2026-07-05 | 已导入 `Assets/Cartoon Coffee/`：斩击/穿刺/裂甲 variant |
| Universal Sound FX | ~$40 | 2026-07-05 | **待导入**（成品化步骤 2） |
| （候选）magic aura loop 2D 类 | $10~20 | 未购 | 光环族精修备选（步骤 5.2） |

## 四、维护红线

1. 加新战法/状态：后端 `battle/names.py` → 客户端 `Names/ChineseNames.cs` 同步；
   需专属演出在 `PerformanceDatabase` 加 SpecialProfile（否则组默认+LogWarning）。
2. 新增资源 key：先登记本文件再写代码。
3. 契约演进：后端新事件类型 → `BattleEvents.cs` Factory 加一行；未处理前自动跳过。
4. 本清单与 `PerformanceDatabase` 内置配置必须同步。
