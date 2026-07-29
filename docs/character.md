# 角色传记 · 羁绊 · 台词本（总则）

> **受众**：玩家（传记静默读出战法/剧情）＋剧情策划（台词/交互/立绘可直接落地）。
> **权威**：人名/性格/战法以 `docs/skills/roster.md` 与机制文档为准；本目录只做
> 叙事与台词设计，**不改结算**。
> **分册**：[`character/bonds.md`](character/bonds.md) 羁绊总表｜
> [`character/bond_dialogues_s1.md`](character/bond_dialogues_s1.md)＋
> [`_s2.md`](character/bond_dialogues_s2.md)＋[`_s2b.md`](character/bond_dialogues_s2b.md)
> **羁绊交互问答**（登场/单挑）｜
> [`character/olympus.md`](character/olympus.md)｜
> [`character/heroes.md`](character/heroes.md)｜
> [`character/sea.md`](character/sea.md)｜
> [`character/underworld.md`](character/underworld.md)

---

## 一、商业定位（写什么、卖什么）

1. **传记 = 软性技能说明**：不写「60% 追伤」，写「每一次暴击都像第二矛」——玩家读完
   会预感到战斗爽点。
2. **羁绊 = 付费与长线钩子**：同场触发专属台词＝「我组对了 CP／宿敌」的成就感；
   皮肤/立绘差分优先投高权重羁绊。
3. **台词 = IP 人格**：短、可喊、可截图；每条 ≤18 字优先，偶可至 24 字。
4. **立绘关键词 = 资源单**：给原画/UE 直接拆镜头，禁止空泛「帅气」。

---

## 二、台词触发总规则（策划必遵）

### 2.0 五类台词本与随机（2026-07-28 升级）

台词本只有五类，其余场景一律并入其中：

| 类别 | effect | 时点 | 走羁绊 | 交互式问答 |
|---|---|---|---|---|
| **登场** | `enter` | `game_start` 后、单挑前 | 是 | **是**（问→答） |
| **单挑** | `duel_challenge`/`duel_accept`/`duel_reject` | 单挑相位（第 1 局） | 是 | **是**（叫阵→应战/拒战） |
| **性格** | 各性格 effect | 性格钩子触发处 | 否（自语） | 否 |
| **高光** | `highlight` | 武将专属高光（`hint.cut_in=highlight`） | 对象可专配 | 否 |
| **击杀** | `kill` | `hero_defeated` 后，击杀者→死者 | 是 | 否 |

**巨伤**（`effect=massive`）与高光**共用同一词池**：单条真实落账伤害 >
`engine.MASSIVE_LINE_THRESHOLD`（3000，与客户端「重创」cut-in 同判据）即由出击者
发词，**每武将每回合至多 1 条**。二者的差别在战斗逻辑：高光是武将专属机制的
高光（多数武将为空），巨伤任何武将都可能触发。

**条数与随机**：默认池 3 条等价；交互式羁绊台词**每场景 3 问、每问 3 条等价答**
（登场 9 种问答；单挑应战/拒战各 9 种）。选词走 `battle/voice_rng.py` 的
**seed 派生哈希流**——同 seed 战报逐字节可重放，不同 seed 台词组合不同，
且**不消耗战斗 RNG**（用主 RNG 会改变战斗结果，属确定性红线，见 P-86）。
目前只播气泡，语音包后续接（一条台词＝一个语音 key）。

**交互顺序＝羁绊定义顺序**：`battle/bonds.py` 每条羁绊登记 `first`（发问方）与
`second`（作答方），分册按该方向撰写；播放严格照此，形成有问有答。

### 2.1 双池制

每个**可指定目标**的场景都有：

| 池 | 何时用 |
|---|---|
| **羁绊台词池** key=`→{target_template_id}` | 场上／目标满足该羁绊，且本场景配置了该池 |
| **通用台词池** key=`generic` | 无可用羁绊，或羁绊池未配置该场景 |

池 key 一律用**对方 template_id**（如 `→hector`），与 `bonds.md` 的 `bond.*` id 对照，
勿混用整段 bond 名当池 key。

**登场友/敌分池（2026-07-22）**：同队写 `**hector**（友）`，跨队写 `**hector**（敌）`；
机器表写入 `hector` / `hector_foe`。缺一侧时选词回退另一侧再 generic。
单挑三态恒为敌对，**不必**写友池；连携恒为友军，**不必**写敌池。


**优先级**：有目标 → 先按羁绊重要度（见 bonds.md 序位，数字越小越重要）选
**第一条命中**的羁绊池 → 池内 2~3 条**确定性轮换**；全未命中 → 通用池轮换。
**禁止**「只有通用、没有羁绊」的半成品人设。

### 2.2 场景、说话视角与是否走羁绊

**视角是写词第一原则**：先确认「谁在说、对谁说、此刻发生了什么」，再落笔。
每条台词必须成立于该视角；写反视角（把击杀写成复仇、把应战写成叫阵）一律返工。

| 场景 key | 说话者 → 对象 | 走羁绊？ | 视角说明 |
|---|---|---|---|
| `enter` 登场 | 本人 → 场上某羁绊单位 | **是** | **同队用友池、跨队用敌池**（key=`{target}` / `{target}_foe`）；分册须成对写（友）/（敌），词须点明关系与敌我 |
| `duel_challenge` 叫阵 | 叫阵方 → 防守方 | **是** | **我主动挑衅你**。禁写「应约」（那是应战口吻） |
| `duel_accept` 应战 | 防守方 → 叫阵方 | **是** | **你先叫阵，我回应**。禁写「接招／看好了／为弟也为城」（像自己在叫阵或护队友） |
| `duel_reject` 拒战 | 防守方 → 叫阵方 | **是** | **我拒绝你的挑战**。禁写「逃一次／懦夫／留力破阵／别冲动／听令退」（骂对方拒战或把对方当己方部下） |
| `combo` 连携 | 被连携副将 → 神谕主将 | **是** | **仅自带主动**（`timing=active`）者有此场景，现行 5 人：`perseus` / `hector` / `triton` / `siren` / `thanatos`；说话者是**跟进释放的副将**，对象是**触发神谕的己方主将**（必为友军）；羁绊池只写神谕将 |
| `trait` 性格 | 本人（自语） | **否** | 只谈自身能力／缺陷。默认按性格共享，逐武将专配写 `battle/voice_trait_data.py` 的 `TRAIT_LINE_OVERRIDES`（整池替换，不改结算） |
| `highlight` 高光 | 本人（自语） | **否** | 高光时刻 cut-in，夸自身。池 key 顺序：`{高光名}_{对象}` → **高光名**（如宙斯 `divine_punishment`）→ `{对象}` → `generic`；发词入口 `battle/voice_lines_highlight.py`，机器表 `voice_highlight_data.py`（抽取生成） |
| `massive` 巨伤 | 本人（自语，对象＝被击者） | 对象可专配 | 与高光**共用词池**，池 key 顺序 `massive_{对象}` → `massive` → `{对象}` → `generic`；每武将每回合至多 1 条 |
| `kill` 击杀 | 击杀者 → 死者 | **是** | **我刚亲手杀了你**（含镜像对局杀死羁绊角色——写诀别／痛惜，禁写「为你报仇」） |

- **残血不发台词**（2026-07-21 定论）：`low_hp` 场景已全量删除，勿再新增。
- `duel_win` / `duel_lose` 可选，分册有写则用；无则静默。
- 单挑机制见 `docs/mechanics/duel.md`。性格**不**改写拒绝/胜负（约战机械已废除）；
  台词走 `voice_lines` 模板双池（`duel_challenge` / `duel_accept` / `duel_reject`）。

### 2.3 条数硬性

- 凡标注的场景：**通用池 2~3 条**；每个已声明羁绊在该场景亦 **2~3 条**。
- 不得只写 1 条（避免审美疲劳与轮换失效）。

### 2.4 实现（单挑已落地）

- **羁绊交互问答（2026-07-28）**：文案权威 `docs/character/bond_dialogues_s1.md`
  ＋`_s2.md`，机器表 `battle/voice_bond_data.py` 由
  `python battle/tools/_extract_bond_dialogues.py` 生成（禁止手改机器表）。
  登场取 `enter_foe`/`enter_ally`、单挑取 `duel`；缺该羁绊分册时回退下述武将扁平池。
- **单挑**：`battle/voice_lines.py` + `voice_duel_data.py`（分册抽取）；
  事件 `trait_trigger`（`effect=duel_*`）挂 duel 组；客户端 `PlayDuel` 按时点
  弹气泡。改词改分册后跑 `python battle/tools/_extract_duel_voice.py`。
- **登场**：`battle/voice_lines_enter.py` + `voice_enter_data.py`（同上抽取工具）；
  `game_start` 后、单挑前发 `trait_trigger`（`effect=enter`）。
  **全部**场上 S1/S2 羁绊单元按序播：**跨队优先（先与对方队伍的羁绊，再与本方
  队伍的羁绊）→ 羁绊表定义序**；单元内发言＝定义方向（`first` 问、`second` 答）。
  同羁绊共用 `group_id`（一个 TraitLine）。回退池仍是**同队友池 / 跨队敌池**
  （`{target}` / `{target}_foe`）。**无任何羁绊**时各队主将播 `generic` 登场
  （队序 A 优先）。
- **击杀**（2026-07-22 落地）：`battle/voice_lines_kill.py` + `voice_kill_data.py`
  （同上抽取工具）；`hero_defeated` 后由**击杀者**对**死者**发 `trait_trigger`
  （`effect=kill`，挂 defeat 同组）。羁绊池 key=死者 template_id → generic；
  恒敌对语境**不做**友/敌分池。自杀（击杀者==死者）或击杀者已亡（互杀收尾）静默。
- 连携等场景仍按上表设计，实现可复用同一双池选词骨架。

---

## 三、人物条目格式（分册统一）

```
## {id} · {中文名}
- 阵营 / 档位 / 性格 / 自带·拆解
- 商业钩子（一句话卖点）
### 传记
### 羁绊（重要度↓，bond id）
### 立绘关键词
### 台词本
  登场 / 单挑三态 / 连携（仅自带主动者）/ 性格 / 高光 / 击杀
  （每场景：通用 + 各羁绊子池；无残血场景）
```

---

## 四、分册索引

| 分册 | 将 | 气质卖点 |
|---|---|---|
| [olympus](character/olympus.md) | 7 | 神权、雷光、圣盾、血战、月猎 |
| [heroes](character/heroes.md) | 9 | 特洛伊宿敌、试炼、镜盾、远征 |
| [sea](character/sea.md) | 6 | 海族血脉、木马、魅歌、六首 |
| [underworld](character/underworld.md) | 7 | 冥婚、石化、摆渡、死神、神使 |
| [bonds](character/bonds.md) | — | 全场羁绊序位与触发摘要 |
| [bond_dialogues_s1](character/bond_dialogues_s1.md) | 9 条羁绊 | S1 传说级交互问答（登场敌/友＋单挑） |
| [bond_dialogues_s2](character/bond_dialogues_s2.md)＋[_s2b](character/bond_dialogues_s2b.md) | 22 条羁绊 | S2 主线交互问答（上/下，500 行上限拆分） |

---

## 五、维护

- 新武将：先在 `bonds.md` 登记双向羁绊序位，再写分册条目（禁止只有通用池）。
- 台词改动不碰 `battle/` 数值；若与 `traits.py` 现有性格句冲突，以「性格场景」
  扩展池并存，旧句可保留为子集。
- 本文件与分册均受 `docs/discipline/doc_standards.md` 行数约束；超限再拆
  `*_2.md` 并在此登记。
