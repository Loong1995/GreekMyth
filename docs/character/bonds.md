# 羁绊总表（按重要度序位）

> 序位数字越小越优先。登场扫全场时取**双方存活单位之间**序位最小的一条；
> 有目标场景（单挑／连携／击杀）只比较**说话者→目标**方向的羁绊。
> `A→B` 与 `B→A` 可共用同一 `bond id`（互斥叙事），台词分册各自撰写。

## 使用约定

- **id**：`bond.<短名>`，台词池 key 用短名（如 `hector`）。
- **weight**：1=传说级（必做皮肤差分候选），2=主线，3=彩蛋／阵营共鸣。
- **scenes**：该羁绊至少覆盖的场景（分册可追加）。

---

## S1 · 传说级（weight=1）

| id | 双方 | 一句话 | 建议场景 |
|---|---|---|---|
| `bond.achilles_hector` | 阿喀琉斯↔赫克托尔 | 特洛伊墙下的宿命对决 | enter, duel_*, kill |
| `bond.achilles_paris` | 阿喀琉斯↔帕里斯 | 踵之弱与致命一矢 | enter, kill, duel_* |
| `bond.achilles_patroclus` | 阿喀琉斯↔帕特洛克勒斯 | 密友与借甲代战 | enter, combo, duel_*, kill |
| `bond.perseus_medusa` | 珀尔修斯↔美杜莎 | 镜中猎手与被猎的蛇发 | enter, duel_*, kill, combo |
| `bond.hades_persephone` | 哈迪斯↔珀耳塞福涅 | 冥王与春后 | enter, combo, kill |
| `bond.zeus_poseidon` | 宙斯↔波塞冬 | 天雷与海潮的兄弟裂痕 | enter, duel_*, kill |
| `bond.odysseus_poseidon` | 奥德修斯↔波塞冬 | 归途被潮水记仇 | enter, duel_*, kill |
| `bond.hector_paris` | 赫克托尔↔帕里斯 | 城墙与弓弦的兄弟 | enter, combo, kill |
| `bond.athena_medusa` | 雅典娜↔美杜莎 | 圣盾与诅咒同源 | enter, kill, duel_* |

## S2 · 主线（weight=2）

| id | 双方 | 一句话 | 建议场景 |
|---|---|---|---|
| `bond.zeus_athena` | 宙斯→雅典娜 | 从头颅诞生的智慧 | enter, combo |
| `bond.zeus_ares` | 宙斯↔阿瑞斯 | 厌战的父与嗜血的子 | enter, duel_*, kill |
| `bond.apollo_artemis` | 阿波罗↔阿尔忒弥斯 | 日月双生 | enter, combo, kill |
| `bond.apollo_asclepius` | 阿波罗→阿斯克勒庇俄斯 | 光之父与蛇杖之子 | enter, combo |
| `bond.ares_nike` | 阿瑞斯↔尼刻 | 血战与胜利羽翼 | enter, combo, kill |
| `bond.athena_odysseus` | 雅典娜↔奥德修斯 | 智慧庇护的谋王 | enter, combo, kill |
| `bond.perseus_athena` | 珀尔修斯→雅典娜 | 神赐三宝／借宝连发 | enter, combo, duel_*, kill |
| `bond.achilles_ajax` | 阿喀琉斯↔大埃阿斯 | 枪与盾的并肩 | enter, combo |
| `bond.jason_castor` | 伊阿宋↔卡斯托耳 | 远征与并辔 | enter, combo |
| `bond.jason_heracles` | 伊阿宋↔赫拉克勒斯 | 船队里最吵的臂力 | enter, combo |
| `bond.heracles_zeus` | 赫拉克勒斯→宙斯 | 半神认父 | enter, duel_accept |
| `bond.heracles_cerberus` | 赫拉克勒斯↔刻耳柏洛斯 | 第十二功业 | enter, duel_*, kill |
| `bond.artemis_atalanta` | 阿尔忒弥斯↔阿塔兰忒 | 月下女猎同盟 | enter, combo |
| `bond.poseidon_family` | 波塞冬↔安菲特里忒／特里同 | 海族王室 | enter, combo |
| `bond.siren_odysseus` | 塞壬↔奥德修斯 | 桅杆与歌声 | enter, duel_*, kill |
| `bond.scylla_odysseus` | 斯库拉↔奥德修斯 | 海峡的旧债 | enter, kill |
| `bond.hades_cerberus` | 哈迪斯↔刻耳柏洛斯 | 王座与看门犬 | enter, combo |
| `bond.hades_thanatos` | 哈迪斯↔塔纳托斯 | 王命与死神 | enter, combo, kill |
| `bond.charon_thanatos` | 卡戎↔塔纳托斯 | 渡口与镰刀 | enter, combo |
| `bond.hermes_zeus` | 赫尔墨斯→宙斯 | 神使复命 | enter, combo |
| `bond.hermes_hades` | 赫尔墨斯↔哈迪斯 | 亡灵向导与冥君 | enter, combo |

## S3 · 彩蛋／阵营共鸣（weight=3）

| id | 双方 | 一句话 | 建议场景 |
|---|---|---|---|
| `bond.olympus_kin` | 任意两奥林匹斯 | 神座共鸣（无更具体羁绊时） | enter, combo |
| `bond.hero_camp` | 任意两英雄 | 凡人热血 | enter, combo |
| `bond.sea_camp` | 任意两海域 | 潮汐同调 | enter, combo |
| `bond.underworld_camp` | 任意两冥界 | 冥河同渡 | enter, combo |
| `bond.athena_ares` | 雅典娜↔阿瑞斯 | 战略与嗜血互厌 | enter, duel_* |
| `bond.ajax_hector` | 大埃阿斯↔赫克托尔 | 盾墙对城墙 | enter, duel_*, kill |
| `bond.nike_anyone_kill` | 尼刻→任意击杀助攻语境 | 凯歌点名（可选） | kill（仅尼刻侧） |
---

## 登场播放（已落地，2026-07-22）

1. 收集场上存活武将之间**全部**机器羁绊对（S1/S2，`bonds.py`）。
2. 单元总序：`weight`↑ → 跨队伍优先 → 均速↓ → id。
3. 单元内发言：A 队优先 → 速度↓；二人各对对方播 `enter`——**同队友池 / 跨队敌池**。
4. 无任何羁绊：各队主将按队序（A 优先）播 `generic` 登场。
5. 时点：`game_start` 之后、单挑之前；客户端 TraitLine 气泡独占。
6. 分册标记：`**target**（友）` / `**target**（敌）`；抽取为 `target` / `target_foe`。

## 登场扫描伪代码（策划理解用 · 旧稿，实现见上）

```
candidates = []
for other in all_alive except self:
    b = best_bond(self, other)   # 查上表，取 weight 最小且分册有 enter 池者
    if b: candidates.append(b)
if candidates:
    play(min(candidates, key=weight).enter_lines[rotate])
else:
    play(self.generic.enter[rotate])
```

击杀：只查 `killer → victim` 的最佳羁绊击杀池；无则 `generic.kill`。
连携：只查 `self → combo_source`；无则 `generic.combo`。
单挑三态：只查说话者→对方。
