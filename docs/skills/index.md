# 战法文档索引（Phase 4 v4 现行权威）

> 文档按**三阵营分册**（奥林匹斯 / 英雄 / 海域·冥界）。每战法三段式
> （**效果** / **实现** / **事件流**）。实现模块仍为
> `battle/skills_{gods,men,sea,underworld}.py`（roster `faction` 仍分 sea /
> underworld，仅文档合册）。中文名：`battle/names.py` ↔ `ChineseNames.cs`。
> **语义权威**：各分册「效果」段。

| 文件 | 内容 |
|---|---|
| [roster.md](roster.md) | 29 将总表（三阵营展示：olympus / heroes / 海冥） |
| [olympus.md](olympus.md) | 奥林匹斯·神示与落雷 |
| [heroes.md](heroes.md) | 英雄·暴击/追加/连击/协击 |
| [sea_underworld.md](sea_underworld.md) | **海域·冥界**（第三册：震荡/节奏 + 吸取/处决） |
| [code_map.md](code_map.md) | skill_id / status_id / trait_id / timing·kind 对照 |

通用口径：

- 主动触发率见各条（bps）；伪随机补偿仅标注者使用。
- `TIMING_PURSUIT` = 普攻命中后追击；`TIMING_PREPARE` = 准备回合必发。
- 神谕（`is_oracle`）= 主将准备回合释放后可连携。
- 特殊伤害 = `damage_class=special`，见 `mechanics/damage.md`。
- 连发 / 协击 / 后排：见 `mechanics/burst_coordination.md`。
