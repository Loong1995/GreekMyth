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
| 状态图标 | 8 个 | 哈希色块占位；**卡顶外侧横排**（宽≈卡宽 1/5） | 仅下表 8 个待传（可选） |
| 立绘 | 32 | 阵营色块+首字母占位 | 全部待上传 |
| 音效 | ~40 key | 程序化合成哔声占位 | 全部待上传（已购 Universal Sound FX 在手） |
| UI/卡框 | 4 张 | 程序化圆角矩形占位 | 全部待上传 |

### 1. 特效 Prefab `Resources/ClientBattle/VFX/<key>.prefab`

来源为已购三包的 Prefab Variant（斩击/穿刺/裂甲图标 ← Cartoon Coffee 2D Slash；
雷电/命中/对撞/爆炸/光环族 ← Vefects Combat Flipbook；弹道/治疗/石化命中 ←
Assets/VFX 四色弹道包）。**换演出＝替换对应 variant 或改其引用，不动代码。**

| key | 用途 | 备注 |
|---|---|---|
| `slash` | 近身斩击 Burst | 普攻×1.0、追击×1.5；**不作飞行弹道** |
| `blade_bolt` / `magic_bolt` | 默认物理/魔法飞行弹道（AoeCenter/PerSegment） | blade＝**030-DualBolt100** Orange；**magic＝Magic Pack Effect1**（`VfxUsage.Projectile`，`WireMagicBolt`） |
| `proj_bolt200` | （可选）粗束弹道 029；战吼已改走默认 `blade_bolt` | **029-Bolt200** Orange |
| `lightning_projectile` | （备选）宙斯竖劈弹道 | Vefects LP02 Directional；当前 `thunder`/`zeus_bolt` 已改走 DR，未用 |
| `hit_generic` / `heal_generic` | 普攻等兜底命中 / 治疗命中 | hit ← **Vefects Hit_05 Once** |
| `hit_petrify` | **魔法伤害默认**命中（卡面）；亦美杜莎石化反噬 | CFXR **Hit Magical Stars (Pink)**（粉紫星芒+刺环）；`VfxCircleFit×2.5`；`WireDefaultHitVfx` |
| `hit_sword` | **物理伤害默认**命中（卡面）；亦珀尔修斯飞剑命中 | Cartoon Coffee **Impact_Cut_V1**（直线横切金橙刀光，**定稿**）；`VfxCircleFit×2.5`；同上 |
| `hit_wave` | **神谕产生伤害默认**命中（`OracleDefault.HitKey`）；亦海神水浪命中 | 画廊 **[1/8]**（以 key 为准） |
| `hit_massive` | **巨伤命中**（>3000 触发「重创」横幅的伤害，覆盖一切专配＋强制震屏） | 原料 **RFX4 Effect15**（画廊 3/8 件 7/54）→ 碰撞子件 **Effect15_Collision**（P-68）；`WireDefaultHitVfx.WireMassive` |
| `hit_lightning` | （库内备用）宙斯卡面雷命中已取消 | Magic Pack **Effect19_Collision**；现行 `thunder`/`zeus_bolt`/`zeus_divine_punishment` 均 `HitKey=none`（绕身够用），本件留库勿再默接 |
| `hit_zeus_discharge` | （备选）宙斯电击闪 | Vefects Electric_Discharge_02 Bunch（当前宙斯未用） |
| `cast_warcry` | （可选）物理冲击波，**主动默认已取消 Cast** | Impact_Shockwave v2 |
| `cast_aoe_burst` | （可选）魔法中心爆，**主动默认已取消 Cast** | Explosion_01_Pivot |
| `hit_clash` | （库内）Magic Effect22_Collision 标准件；**不再作物理默认** | 画廊 Magic **2/8** 件 45 原料；专配/备用 |
| `proj_bolt200` | **物理主动默认**弹道 | 029-Bolt200 Orange |
| `magic_bolt` | **魔法主动默认**弹道 | Magic Pack **Effect1** 母件（`VfxUsage.Projectile`；摘 Motion/Target，位移归 LaunchProjectile；定径 ×0.7） |
| `hit_warcry` | （可选）Radial_Burst 放大命中 | 战吼已改走默认 `hit_sword` |
| `cast_oracle` / `aura_generic` | 神谕前摇 / 默认光环 | |
| `aura_fire_foot` / `aura_fire_head` | 阿瑞斯血战（脚） | 仅卡框红呼吸 `SetAresRage`（弱）；`aura_fire_head` 备用 |
| `aura_ares_might` | （旧）阿瑞斯战神之勇 Effect18 | 已由 `shroud_ares_might` 取代，可留库 |
| `shroud_ares_might` | 阿瑞斯战神之勇罩身 | 画廊 2/8·**10/61**＝Magic Effect18；落盘 `VfxUsage.Shroud`（摘 Distortion/贴花/PerPlatformSettings，P-77）；挂载 `VfxShroudFollower` + `VfxShroudPresence`（OddRounds） |
| `shroud_thunder` | 宙斯雷霆神谕**罩身**（状态 `thunder`，现行） | 画廊 2/8·**11/61**＝Magic Effect19；落盘 `Standardize(..., VfxUsage.Shroud, keep: ["Fringe"], drop: ["Particles","Point","Fog","ImpactDecal"])`（`WireThunderShroud`）。成品＝`ShieldAdd3`（贴罩面电流）+ `ShieldFringe`（边环），与 `shroud_ares_might` 同构＝**只罩住、不外喷**。摘掉的：折射壳（糊卡面，P-77 实证）、游离电弧（P-78）、一次性喷射爆发（往外喷会读成"正在放技能"，罩身表达的是常驻态）。挂载时按 `StagePerformanceConfig.ShroudDesync*` 错相位（多人同挂不同步闪） |
| `ambient_thunder_storm` | （备用）雷霆**场域氛围**参考件 | 宙斯已改罩身，本件当前未接线，保留为 `VfxUsage.AmbientField` 的参考实现。画廊 2/8·**11/61**＝Magic Effect19；`VfxUsage.AmbientField`：摘人形壳层（Distortion/Fringe，卡尺度看不见还费），留世界空间电弧。全场按 key 去重；源点是**一组**（`AmbientFieldSources`，默认战场一处 + 背景一处，均自上而下、落点大范围游走），几何 `StagePerformanceConfig.AmbientField*`。电弧层轴向由 `WireThunderStorm.OrientAsStorm` 重定为自上而下（原料是护罩朝向，水平层会读成"往镜头劈"）。勿与 `hit_lightning`（Collision）混淆 |
| `cast_duel_launch` | 单挑出阵：双方**定位圆**地面（Effect28） | **RFX4 Effect28**（画廊 3/8 包 19/54 件）；RFX4 深度贴花层 URP 画不出、标准化时摘除（P-33） |
| `aura_duel_victory` | 单挑加冕：胜者**卡面**；亦作出阵双方**卡面追加**（画廊 1/8 件 8/60） | 原料 **RFX4 Effect23** → 碰撞子件 **Effect23_Explosion**（P-68） |
| `ground_duel_defeat` | 单挑溃败：败者**定位圆地面** | 原料 **Magic Pack v1 Effect8** → **Effect8_Collision**；挂 `VfxGroundLayer`；贴花 → 自研裂地补 |
| `aura_duel_defeat` | 单挑溃败**卡面追加**（画廊 1/8 件 32/60 观感） | 同原料 Effect8 → Collision；`VfxUsage.Anchor`（无地面层） |
| `momentum_fire` | 势能火（四轨最高 ≥4/5/6/7 分档） | **CFXR3 Fire (No Smoke)**；卡上缘；非状态光环 |
| `momentum_glow` | 势能卡后柔光（≥4 起） | **CFXR LightGlow A**（**已去 Small Stars**）；关点光；sorting −1；与火同灭 |
| `aura_freeze` | 卡吕普索冰锢挂身 | **CFXR3 Ice Shield**；卡面下方约 y=−0.3 |
| `dr_lightning_bolt_anim` | DR 贴图动画闪电 | Demo 下方 `SimpleLightningBoltAnimatedPrefab` |
| `aura_aegis` / `hit_shield_counter` | 圣盾挂身 / 反制命中 | 挂身＝**AllIn1 金描边**（无 Magic 粒子）；反制 ← **Magic Effect17_Collision** |
| `ground_crack_path` / `_0`~`_3` | **弹道类骨架变体**：蜿蜒主缝 + 树杈；飞行每段抽不同变体；三档熔岩＝同档命中×0.78（仅主缝烧） | G4 生成；兼容 key `ground_crack_path`＝变体 0 |
| `ground_crack_hit` | **命中类骨架**：受击者脚下分形放射裂地；场心大裂地也用它（配大面积+档 3） | 同上；直径＝运行时卡宽 ×1.5 ×面积倍率 |
| `ground_lava_bloom` | 熔岩过曝层，**当前未接线**（熔岩走 shader 沿缝渐变+灭点，见 `ground_crack_language.md`），留库备用 | **Magic Pack v1 Effect8** 晋升，菜单 `GreekMyth/裂地/G12` |
| `chunks_<stage>.png` | 碎块图集（4×3），工具保留、现行 ChunkCount=0 | 菜单 `GreekMyth/裂地/G3` 从 `arena_<stage>.png` 现切 |
| `masks/mask_crack_{spine,radial}.png` | 弹道大小缝混排 / 命中分形放射（自烘） | G4 产出；旧 spur/arena 遮罩不再使用 |

> 裂地族红线（详见 [ground_crack_language.md](ground_crack_language.md)）：
> 模式×强度+面积正交；颜色唯一真源 `GroundCrackPalette`；触发唯一入口
> `GroundCrackService`。**KriptoFX Decal 禁止接线**（URP 下品红盒面）。
> 旧 key `ground_shatter` 已删除。
| 石化 `petrify` | 美杜莎 | All In 1：立绘+卡框灰阶石色渐变（`UnitView.SetPetrified`） |
| `lightning_strike` / `hit_lightning` | （旧）粒子落雷，触发已改程序化 | 命中闪仍可用 hit_lightning |
| `aura_tide` | 波塞冬潮汐挂身（poseidon_tide） | **CFXR LightGlow A (Loop, Blue)** 蓝色呼吸光 |
| `aura_underworld` | 哈迪斯冥域挂身（吸血/幽影/献统） | **CFXR Suspicious Cloud (Black)**；挂载时强制极透（alpha×0.12） |
| `aura_bloodlust_weak` / `aura_bloodlust_strong` | （旧）血红光环，已弃用阿瑞斯挂载 | 资源仍可留作他用 |
| `aura_sunlight` | 呼吸阳光（德尔斐/尼刻复用） | |
| `aura_hermes_mark` | 神使/扰心印记 | |
| `icon_spear_crack` | 阿喀琉斯裂甲图标（超大；**仅傲慢 25% 贯穿成功时播**） | ExtraIconScale=2.6 |
| `icon_aegis` | 雅典娜圣盾**反伤**闪烁图标（卡面中央渐变闪） | 待上传；与 icon_spear 同目录 VFX/ |
| `icon_aegis_heal` | 雅典娜圣盾**重击回血**闪烁图标（与反伤区分） | 待上传；未传则青绿占位 |
| `icon_block` | 普通格挡触发闪烁图标（卡面中央渐变闪，同圣盾逻辑） | 待上传；蓝灰占位 |
| `icon_trojan_bomb` / `hit_explosion_crack` | 木马炸弹图标 / 裂开爆炸 | |
| `proj_flying_sword` | 珀尔修斯飞剑弹道 | |
| `proj_wave` / `hit_wave` | 海神水浪弹道 / 命中 | |
| `hit_pierce` / `hit_clash` | 穿刺 /（库内）对撞备用 | |

> ~~`proj_aegis_bounce`~~：已取消。圣盾反弹走 `aegis_shield` **Melee**（持盾者闪光后突进）；
> 重击回血不走 Melee，闪 `icon_aegis_heal`。不配回击弹道。  

> ~~`overflow_<track>`~~：甲案特效**不必采购**。势能跨 4 档已用乙案
> （`UnitView.PlayMomentumOverflow` 白闪 + punch）；观感够用再考虑从已购
> Vefects 里抠一发 burst 做共用 `overflow_burst`（可选精修，非阻塞）。

尺寸红线：variant 根缩放按**目视校准**（禁止按包围盒归一，拖尾会把包围盒撑到
几十单位）；现值：弹道/治疗/命中 1.0、Bolt200 默认弹道 0.75、
Vefects 雷电弹道 0.9、剑击/穿刺 0.35、slash 0.25、光环 0.9~1.4。演出层只允许相对缩放（`*=`）。

### 2. 状态图标 `Resources/ClientBattle/StatusIcons/<status_id>.png`

- **卡顶外侧横排**（`ControlIcon=true`）：`silence disarm petrify freeze ming_lock charm fear underworld_burn`
- **不展示图标**：`first_strike`（先攻）、`hesitation`（犹豫）——仅飘字 + 状态台词。
- ~~常规状态卡牌上方小图标~~：**已取消**（2026-07-20）。增益/神谕/印记等靠光环
  （`UnitAuraService`）与飘字；冥火走卡顶图标、**不**挂 CFXR 火（火留给势能）。

### 3. 立绘 `Resources/ClientBattle/Portraits/<template_id>.png`

> **路径红线**：必须是 `Assets/Resources/ClientBattle/Portraits/`，
> 放在 `Assets/Resources/Portraits/` **不会生效**。  
> **导入红线**：Inspector 里 Texture Type = **Sprite (2D and UI)**；
> 默认 Default 时 `Resources.Load<Sprite>` 读不到，仍显示色块。  
> 文件名 = roster 英文 `template_id`（`zeus` 不是「宙斯」、`heracles` 不是 `heracules`）。  
> **尺寸**：任意分辨率即可；运行时按卡面槽位等比 contain（`UnitView.FitSpriteToSlot`），
> 不要求统一像素。竖构图更贴卡；极端扁图会留边。

32 名武将（7/10/7/8，与 `battle/roster.py` 同步），文件名 = roster 模板 id：
`zeus athena ares apollo asclepius artemis nike`（奥林匹斯 7）
`achilles patroclus heracles perseus atalanta paris ajax hector jason castor`（英雄 10）
`poseidon amphitrite triton siren scylla odysseus calypso`（海域 7）
`hades medusa persephone charon thanatos cerberus hermes hecate`（冥界 8）
（v4 池：喀戎/卡律布狄斯已下架；奥德修斯/赫尔墨斯 A4 改隶海域/冥界，id 不变）

**头像标复用同一路径（无需另传）**：落雷/吸统等演出的 `PortraitMarkKey`
（如 `thunder`→`zeus`、`hades_command_drain`→`hades`）调用
`UnitView.ShowPortraitMark`，从 `Portraits/<key>.png` 取图缩到目标卡头顶短暂浮现。
上传宙斯立绘后，雷霆落雷会自动显示宙斯小头像；未上传则仍为阵营色占位块。
雷击本身走 DR **主芯+分叉**竖雷（`thunder`/`zeus_bolt`/`zeus_divine_punishment`），
卡面**不叠** `hit_lightning`（`HitKey=none`，绕身 `shroud_thunder` 已够）；神罚另抬高震屏。
与头像标分开。RFX4 **禁止**接到宙斯技能（P-25）。

**全屏 cut-in 复用同一路径（无需另传）**：单人 cut-in（斜带+巨幅立绘）与单挑
舞台 cut-in（立绘出框进虚空展示屏）都取 `Portraits/<template_id>.png` contain
放大展示；立绘越高清 cut-in 越震撼，建议 ≥1024 高。占位时为阵营色块。

**单挑动作 flipbook（可选，逐武将补）**：
`Resources/ClientBattle/DuelAction/<template_id>_<strike|react>_<NN>.png`
（`NN` 从 `00` 连号，断号即停）。缺失则该段用静态立绘占满，时序不变。
**单挑屏华饰（全部可选，不传则程序化生成）**：`UI/duel_screen_bg`（屏底，
**最高性价比，建议 AI 生成，规格与中文提示词见 portrait_cutin_assets.md §5d**）、
`UI/duel_icon`（中央图标）、`UI/duel_rays`（整张放射光芒）、
`UI/duel_corner`（左上角纹饰，其余三角代码镜像）。
一次投入、全部单挑复用，不随武将数量增长。

**构图安全区与 flipbook 制作规格** → [portrait_cutin_assets.md](portrait_cutin_assets.md)；
**单挑系统总索引** → [../mechanics/duel.md](../mechanics/duel.md)。

### 4. 音效 `Resources/ClientBattle/SFX/<key>.wav`

- 默认族：`sfx_active_default sfx_melee_default sfx_pursuit_default
  sfx_status_trigger_default sfx_oracle_default sfx_hit_default sfx_heal_default
  sfx_defeated`
- 状态施加：`sfx_status_<status_id>`（同帧与伤害音效由 SfxManager 去重）
- 专属：`sfx_thunder_strike sfx_aegis_counter sfx_achilles_pierce sfx_trojan_explosion
  sfx_perseus_swords sfx_trident_quake sfx_medusa_gaze sfx_petrify_on sfx_petrify_off
  sfx_duel_horn sfx_duel_clash sfx_duel_win sfx_cutin_solo sfx_trials_counter
  sfx_lion_counter sfx_cerberus_counter
  sfx_oracle_thunder sfx_oracle_aegis sfx_oracle_ares sfx_oracle_apollo
  sfx_oracle_hermes sfx_oracle_nike sfx_oracle_poseidon sfx_oracle_hades
  sfx_hades_drain`（B5 新增：冥域献统头像标）

### 4b. BGM `Resources/ClientBattle/BGM/<key>.ogg|wav`（B3，全部可选资产）

- 分层 stem（Demucs 拆层产物，**同 BPM 同长度**，循环点裁齐）：
  `bgm_stem_drums bgm_stem_bass bgm_stem_melody bgm_stem_other`
- 占位单曲回退：`bgm_main`（stem 缺失时用，音量+低通随全局势能档）；
  全缺则 BGM 静默 no-op，不影响播放。
- 换曲后在 `BgmLayerService` Inspector 登记 Bpm/BeatsPerBar（小节对齐切层用）。
- 素材路线（Suno+Demucs / 公版古典 / CC-BY 库）与授权红线见
  `docs/dev/phase4_manual_tasks.md` 与 phase4_plan §四 B3 附。

### 4c. 字体 `Resources/ClientBattle/Fonts/<名>.ttf|otf`（B4）

免费商用：思源黑体 / 得意黑 / 站酷系；导入后在
`Resources/ClientBattle/FloatingTextTuning.asset` 的 FontName 填资产名即换。
操作文档：`docs/client/floating_text_tuning.md`。

### 5. UI 与卡框

| 文件 | 规格 | 说明 |
|---|---|---|
| `UI/chat_bubble.png` | 256×160，白底圆角+左下尾巴，透明背景 | 台词气泡底板；字号/折行由代码控制，上传底板即可 |
| `UI/board_background.png` | ≥2048×1152（16:9 基准） | 棋盘背景，cover 铺满不变形；**未上传时为无色（纯黑）** |
| `CardFrames/antique_frame.png` | 1024×1680（doc view 竖框） | 统一立绘边框；立绘等比塞入内窗，框盖在立绘上 |
| `CardFrames/petrify.png` | 同外框比例 | 石化覆盖层回退（无 All In 1 时） |

### 5b. 近 3D 舞台 `Resources/ClientBattle/Arena/`（2026-07-25 协议）

所有 arena 相关图片一律放本目录；出图规范 `docs/dev/near3d_evaluation.md` §七。

| 文件 | 规格 | 说明 |
|---|---|---|
| `Arena/arena_<stage>.png` | 16:9 全宽正俯视（≥2048 宽） | 地面：平躺水平板（顶边=远端）；`ArenaStageView` 加载 |
| `Arena/sky_<stage>.png` | 16:9 横构图天穹 | 天空：竖板立于地面远端，底边接缝；cover 铺满 |
| `Arena/statue_<name>.png`（预留） | 竖图透明底 | 神像浮现贴图，叠天空层 |

`<stage>` ∈ `olympus` / `troy` / `abyss`（暂定；现已上传 olympus 两张）。
两图齐备 + `CameraFitter.PerspectivePilot=true` 时自动启用近 3D 舞台
（`Units/ArenaStageView.cs`），否则回退 `UI/board_background` 平面方案。
Texture Type 必须 Sprite（P-20；已预写 .meta）。

## 二、成品化路线（把演出从占位配成成品）

> 原则：一次只换一类资源，换完跑一遍验收（§二.6），改配置不改代码。

### 步骤 1：控制类状态图标（可选，约 1 小时）
仅 8 个 id（§一.2，以 `StatusPresentationRegistry` `ControlIcon=true` 为准；不含先攻/犹豫）。[game-icons.net](https://game-icons.net)（CC BY 3.0）
导出 PNG 放入即可；先攻/犹豫与常规增益图标**不要做**。

### 步骤 2：音效（一天体力活）
Package Manager 导入已购 Universal Sound FX → 按 §一.4 清单逐 key 挑选、
转 wav 改名放入。神谕类可再叠免费圣咏垫底音（freesound.org 筛 CC0）。

### 步骤 3：立绘（观感提升最大）
AI 生图初稿（统一 prompt："greek mythology, 2D card game portrait, bust,
painterly"）全量 32 张先上；后续外包精修主推 8 将（¥200~500/张）逐批同名替换。
**顺带验收头像标**：有 `zeus.png` / `hades.png` 后，打一场带雷霆/冥域献统的战报，
确认目标头顶浮现对应小头像（机制见 performance_mechanisms「头像标 C1」）。

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

> 红线：本项目是 **URP Universal Renderer（3D 前向）**，**不是** 2D Renderer
> （2026-07-28 核实，见 `docs/client/vfx_pack_integration.md` §二）。采购只要求
> **支持 URP**；"不支持 2D 管线"不再是排除理由，但依赖屏幕深度/不透明拷贝的
> 方案要按 §二 的两项开关确认。购买用与工程一致的 Unity ID，购后我在
> Package Manager > My Assets 拉取导入并登记。

| 资产 | 价格 | 购买日期 | 状态/已用于 |
|---|---|---|---|
| 2D Cartoon/Anime Effects - Mobile Friendly | $4.99 | 2026-07-05 | 已导入 `Assets/VFX/`：弹道/治疗/石化命中 variant |
| Combat Flipbook VFX - URP | $39.97 | 2026-07-05 | 已导入 `Assets/Vefects/`：雷电/命中/光环/爆炸 variant |
| 2D Sword Slash VFX | $19.99 | 2026-07-05 | 已导入 `Assets/Cartoon Coffee/`：斩击/穿刺/裂甲 variant |
| Universal Sound FX | ~$40 | 已购已导入（2026-07-20，`Assets/Universal Sound FX/`） | 按 §二步骤 2 逐 key 挑选接线；三皇音效优先从本包出 |
| **KriptoFX Realistic Effects Pack 4** | $42 | 2026-07-24 | 已导入；**须** `GreekMyth → RFX4 → 导入 URP Patch（修粉红）`（2026-07-25 已应用；Effect22 全粉即未导）。**禁止用于宙斯/单挑**。仅舞台远景/神像大场面。**可靠预览**：`GreekMyth → RFX4 可靠预览（一键）`；粉红诊断同菜单下「诊断粉红材质」 |
| **kripto289 Magic Effects Pack 1** | $37 | 2026-07-24 | 已导入；URP patch。**魔法默认弹道**＝`Effect1`→`magic_bolt`；宙斯命中库内 `Effect19_Collision`（现行未默接）；战神之勇罩身＝画廊 2/8·10/61 `Effect18`→`shroud_ares_might`；雅典娜反制=`Effect17_Collision`；圣盾挂身回 AllIn1。**预览**：`GreekMyth → Magic Pack → 可靠预览` |
| （候选）magic aura loop 2D 类 | $10~20 | 未购 | 光环族精修备选（步骤 5.2） |

## 四、维护红线

1. 加新战法/状态：后端 `battle/names.py` → 客户端 `Names/ChineseNames.cs` 同步；
   需专属演出在 `PerformanceDatabase` 加 SpecialProfile（否则组默认+LogWarning）。
2. 新增资源 key：先登记本文件再写代码。
3. 契约演进：后端新事件类型 → `BattleEvents.cs` Factory 加一行；未处理前自动跳过。
4. 本清单与 `PerformanceDatabase` 内置配置必须同步。
