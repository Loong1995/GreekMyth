# 四阵营视觉规范（faction_style）

> 代码源：`Assets/Scripts/ClientBattle/Units/BattleBoardView.cs` 内
> `FactionColors`（阵营主色）与 `FactionOf`（template_id → 阵营登记表）。
> 改配色改代码常量即可；本表与代码不同步视为任务未完成。

## 1. 阵营划分（英雄登记表，与 `battle/roster.py` v4/A4 同步）

> A4 定稿：`gods→olympus`、`men→heroes`；奥德修斯→sea、赫尔墨斯→underworld。

| 阵营 | 英雄（template_id） | 气质关键词 |
|---|---|---|
| 奥林匹斯 olympus | zeus、athena、ares、apollo、asclepius、artemis、nike | 神圣、雷光、鎏金 |
| 英雄 heroes | achilles、patroclus、heracles、perseus、atalanta、paris、ajax、hector、jason、castor | 血性、青铜、战场 |
| 海域 sea | poseidon、amphitrite、triton、siren、scylla、odysseus、calypso | 深海、浪涌、碧蓝 |
| 冥界 underworld | hades、medusa、persephone、charon、thanatos、cerberus、hermes、hecate | 幽暗、亡魂、冥紫 |

- 新增英雄必须在 `FactionOf` 登记（未登记回退 heroes 配色，无告警）。
- 立绘路径：`Resources/ClientBattle/Portraits/<template_id>.png`，
  缺图回退阵营色块（`PlaceholderFactory`），上传即生效。

## 2. 配色规范（设计稿参考值；运行时以代码 float 常量为准）

| 阵营 | 代码主色（FactionColors） | 设计稿近似 hex | 特效主色倾向 |
|---|---|---|---|
| 奥林匹斯 | (0.85, 0.72, 0.25) | #D9B840 | 暖金光、雷纹 |
| 英雄 | (0.78, 0.28, 0.22) | #C74738 | 橙红、青铜 |
| 海 | (0.22, 0.55, 0.82) | #388CD1 | 浅碧、波浪 |
| 冥界 | (0.55, 0.30, 0.72) | #8C4DB8 | 亮紫、雾气 |

使用规则：

- 卡牌边框底板 = 阵营主色；阵亡整卡灰化压暗（`UnitView.SetDefeated`）。
- 战法专属特效配色在 `PerformanceDatabase.SpecialProfiles` 覆盖
  （tint/资源 key，见 `performance_mechanisms.md`）。
- 主将标识 / 稀有度边框粒子：**未实现**（预留需求，实装时在 `UnitView`
  卡牌结构挂角标/粒子节点并回写本文档）。
