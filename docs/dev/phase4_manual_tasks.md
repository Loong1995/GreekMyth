# Phase 4 人工工作清单（采购 / 操作 / 确认）——具体到名字

> 【历史文档/历史快照】Phase 4 已落地；武将池现为 **32 将**（本文 "29 将"
> 为当时口径，A4 后追加 hecate/calypso/patroclus，以 `battle/roster.py` 为准）。
> 采购登记现行权威：`docs/client/assets_upload_guide.md` §三。
>
> 配套 `phase4_plan.md`（其 B 附以本文为准，本文更细）。AI 侧全部占位先行，
> 资源到位放入指定目录即生效、零代码。授权凭证一律登记
> `docs/client/assets_upload_guide.md` §三采购表。
> **采购红线**（2026-07-28 修正）：本工程是 **URP Universal Renderer（3D 前向）**，
> 当时写的"URP 2D Renderer"是错述，现行口径见
> `docs/client/assets_upload_guide.md` §三。购买用与工程一致的
> Unity ID，购后由 AI 在 Package Manager > My Assets 导入。

## 一、拍板确认（已按计划书推荐值开工，如需改判尽早提出）

| # | 事项 | 当前按此执行（具体内容） | 改判截止 |
|---|---|---|---|
| 1 | 阵营终局形态（2026-07-20 A4 已落地） | 保留四阵营，不合并；改名 `gods→olympus`（奥林匹斯）、`men→heroes`（英雄）；成员调换：**奥德修斯→海域**、**赫尔墨斯→冥界**。实现模块文件名沿用历史（skills_gods.py 等） | 已执行 |
| 2 | 武将池 v4（**29 将**，2026-07-20 A4 已落地） | 新增：**赫克托尔、伊阿宋、卡斯托耳**（英雄），**阿尔忒弥斯、尼刻**换新版（奥林匹斯）；下架：**喀戎**（英雄）、**卡律布狄斯**（海域）。终局：奥林匹斯 7、英雄 9、海域 6（含奥德修斯）、冥界 7（含赫尔墨斯，卡戎无性格）——原表"28/英雄10"为口算误差，以 `battle/roster.py` 为准 | 已执行 |
| 3 | 溢出演出 | **乙案定稿**：白闪 + punch（`PlayMomentumOverflow`）；满档仍 rim+流光。不采购专属 overflow 包；日后可选从已购 Vefects 抠共用 burst | 已定 |
| 4 | 首发战术集 | ①集火目标（指定敌方单位/不指定）——**已定案：实现为受击率偏置**（指定目标受击点数×系数，仍加权随机+保残兵，非强制锁定）②保护目标（下回合起小额持续治疗+减伤）③攻守滑杆（-2~+2，输出/生存类战法触发权重小幅偏置） | P4-C 开工前 |
| 5 | 【恐惧】状态口径 | 任务书只点名未定义（刻耳柏洛斯三首噬咬施加）。暂按：**禁普攻+禁追击、造成伤害 -15%、持续 1 回合**（硬控轻量版，A2 已实现） | A3 冥界子批开工前 |

## 二、采购清单（合计约 ¥250~450 + 订阅 1 月；逐项具体候选）

| # | 项 | 首选（点名） | 备选 | 预算 | 何时 | 放置位置 |
|---|---|---|---|---|---|---|
| P1 | AI 音乐订阅 1 个月 | **Suno Pro**（suno.com，$10/月，付费档商用授权） | **Udio Standard**（udio.com，$10/月） | ¥70~80 | B3 开工前 | 产物见 §三.1，选定后可退订 |
| P2 | 「觉醒/充能爆发」特效 | **取消采购**：乙案白闪已够用；可选日后从已购 Vefects 抠共用 burst | — | ¥0 | — | 非必须 |
| P3 | 雅典娜反弹弹道 | **取消**：圣盾反弹走 Melee（持盾者突进），无 `proj_aegis_bounce` | — | ¥0 | — | — |
| P4 | 三皇音效 + 里拉琴 | **已购已导入《Universal Sound FX》**（`Assets/Universal Sound FX/`，2026-07-20）：雷鸣/金属盾鸣/低语由 AI 从本包挑选接 key | 缺项（如里拉琴）去 **freesound.org** 搜 "lyre pluck" 等，筛 **CC0** 授权 | ¥0（已购） | B5 接线 | 改名为下列 key 放 `Assets/Resources/ClientBattle/SFX/`：`sfx_zeus_avatar`、`sfx_aegis_reflect`、`sfx_hades_drain`、`sfx_overflow_lyre` |
| — | 明确不买 | 4~7 档势能特效、逐武将专属资源、立绘级 cut-in 动画、FMOD/Wwise | 皮肤商业化验证后再议 | — | — | — |

选购标准：粒子数 ≤2000/发、贴图 ≤1024、无自定义脚本依赖；
下载后交 AI 做 variant 与缩放校准，勿直接改源包文件。
（P2/P3 已取消，见上表。）

## 三、操作步骤（需要人工动手，逐条可照做）

### 1. BGM 制作（B3；红线：**禁止著名 BGM 的 AI 变调**，属侵权衍生作品）

1. **生成**：Suno/Udio 提示词——
   `epic orchestral battle, greek mythology, war drums, brass, choir, 120 BPM,
   loopable, instrumental, no vocals`。多试稿，选定 1 首（1~2 分钟、节奏稳定）。
2. **拆轨**：下载 WAV → 本地 `pip install demucs` →
   `demucs -n htdemucs 曲子.wav` → 得 drums/bass/other/vocals 四轨。
3. **裁循环**：**Audacity**（免费）导入四轨：对齐起点、裁到整小节循环
   （接缝无咔哒声）、记录 BPM（AI 要用于切层对齐小节）、分别导出
   44.1kHz WAV，命名 `bgm_layer1.wav`（鼓底）~ `bgm_layer4.wav`（高潮层）。
4. **放置**：`Assets/Resources/ClientBattle/BGM/`；把 Suno/Udio 商用授权页
   截图+链接登记 assets_upload_guide §三。
5. **备选路线**（AI 生成不满意时）：
   - 公版古典：**musopen.org** 下载「作曲公版且录音 CC0/PD」版本
     （注意：作曲公版≠录音公版，录音授权逐条核验），同样 Demucs 拆轨。
   - CC-BY 曲库：**incompetech.com**（Kevin MacLeod，署名即商用）、
     **opengameart.org**（筛 loop/adaptive 类）。
   - 最后才考虑委托音乐人出 4 stem（¥300~800）。

### 2. 特效/音效导入（P2~P4 到货后）

1. Asset Store 购买后告知 AI 包名，由 AI 从 My Assets 导入、建 variant、
   定缩放、接 key；人工只验收观感。
2. 音效：从已购 Universal Sound FX（My Assets 导入）或 freesound（CC0）
   挑好后，直接改名为 §二 P4 的 key 放入 SFX 目录即生效
   （44.1kHz、≤3 秒、WAV/OGG）。

### 3. 飘字手调（B4 落地后）

AI 会内置 3~4 款免费商用字体：**思源黑体 Source Han Sans**（SIL OFL）、
**得意黑 Smiley Sans**（OFL）、**站酷酷黑 / 站酷高端黑**（免费商用）。
按 `docs/client/floating_text_tuning.md`（B4 交付）在 Inspector 选字体、
调字号/颜色/上浮曲线，实时生效；调好提交 SO 资产。

### 4. 里程碑人工验收点

| 里程碑 | 你要看什么 |
|---|---|
| M1 | `battle/tests` 全绿；textlog all 档抽查四轨势能分值与连发/协击行 |
| M2 | 宙斯/雅典娜/哈迪斯新版战法 brief 战报可读 + 武将战法文档 v4 皇卡部分逐条核对 |
| M3 | 29 将 batch_sim 1000 场胜率表（AI 产出），确认无离谱失衡再冻结 golden |
| M4 | 独立 Build：四轨满档「神格化」三拍音画同帧、cut-in 频率是否舒适、探针 60fps |
| M5 | BGM 切层对齐小节无爆音、飘字终稿、三皇专属演出（C1）、高光回放入口 |
| M6 | 战术全流程：预设→第 2 回合改判→每方 2 次上限→替换段无缝→断线兜底 |

## 四、登记规则

- 本表随执行滚动更新（AI 每完成一批回填 key 与状态）；
  新增人工事项一律先记这里再执行。
- 采购凭证/授权链接统一登记 `docs/client/assets_upload_guide.md` §三。
