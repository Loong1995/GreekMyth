# 战后技能结算表（settlement_stats）

> 客户端只读聚合战报，零重算。实现：
> `Assets/Scripts/ClientBattle/Events/BattleSkillStatsAggregator.cs`；
> UI：`Test/SettlementPanel.cs`（系列结束后 Runner 调 Show 自动弹表 /
> Tester「打开结算」重开）。
> 带技能借手口径见 [hero_specials.md](../mechanics/hero_specials.md) §2。

## 1. 表结构

- 分局 Tab：`第 N 局`；多局时追加「系列合计」。
- 每队按阵容序列出武将：兵力条 + 技能行（×触发 / ⚔杀伤 / +治疗）。
- 技能行顺序：该局首次出现序（触发或首次分到杀伤/治疗）。

## 2. 归因规则

### 2.1 主动 / 普攻 / 追击

沿 `parent_seq` 上溯至 `skill_trigger` 或 `normal_attack`：

- 武将 = 事件 `actor_id`
- 技能键 = `skill_id`（普攻=`basic_attack`，协击=`coordinated`）

### 2.2 状态触发（神谕 / 被动挂状态）

上溯至 `status_tick` 即停（**不再**上溯到引发伤害的主动，以免雷霆记到天雷击）：

- 武将 = `source_id`（空则 `status.owner_id`）——**施法者**，非必然出手者
- 技能键 = `MapStatusToSkill(status_id)`（见下表；未登记则用 status_id）

例：阿喀琉斯持雷霆出手 → 落雷伤害进 **宙斯 · 雷霆神谕**；
圣盾反弹进 **雅典娜 · 埃癸斯圣盾**。

### 2.3 次数

- `skill_trigger`（cast/release/prepare）→ 该 actor 技能 +1 触发
- `normal_attack` → 普攻/协击 +1
- `status_tick` → 施法者带技能 +1（与杀伤同键）

仅统计 `amount>0` 且无 mitigation 的伤害；治疗 `amount>0`。

## 3. Status → 带技能映射（登记表）

未列出的 status_id 默认等于自身（如 `heracles_trials`、`medusa_gaze`）。

| status_id | 带技能 skill_id |
|---|---|
| `thunder` | `thunder_oracle` |
| `aegis_shield` / `aegis_ward` | `athena_aegis` |
| `snake_staff_protection` / `snake_staff_tender` | `asclepius_oracle` |
| `blood_battle` / `ares_might` | `ares_warfury` |
| `war_frenzy` | `ares_frenzy` |
| `divine_revelation` | `delphi_revelation` |
| `hermes_confusion_mark` / `hermes_herald_mark` | `hermes_oracle` |
| `poseidon_tide` | `poseidon_oracle` |
| `hades_lifesteal` / `shadow_veil` / `hades_command_drain` | `hades_underworld_dominion` |
| `lion_counter` | `heracles_counter` |
| `trojan_bomb` / `trojan_scheme` | `odysseus_trojan` |
| `perseus_mirror` | `perseus_relics` |
| `achilles_thrust_crit` | `achilles_thrust` |

以上为常见例；完整映射以 `Names/StatusPresentationRegistry.cs` 的
`StatsSkillId` 字段为准（`nike_wings`、`patroclus_standin`、
`underworld_burn`/`hecate_torch` 等亦已登记），未登记的 status 归因到
status_id 自身。新增「status_id ≠ 带技能 id」时：在 StatusPresentationRegistry
对应条目填 `StatsSkillId`（`BattleSkillStatsAggregator.MapStatusToSkill`
委托 `StatsSkillOf()` 查此表），并同步本表。

## 4. 显示名

`ChineseNames.Skill(skillKey)`，否则 `ChineseNames.Status`——与
`battle/names.py` 同步。
