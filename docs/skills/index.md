# 战法文档索引（v3.1 池，Phase 3）

> 每战法三段式（效果描述 / 实现口径 / 事件流要点），按阵营分四个文件（≤300 行）。
> 实现：`battle/skills_{gods,men,sea,underworld}.py` + 测试原语 `battle/skills.py`。
> 中文名登记：`battle/names.py`。数值以 `docs/prompts/phase3_battlecomplete.md` 为准。

| 文件 | 阵营（机制标签） | 战法数 |
|---|---|---|
| [gods.md](gods.md) | 神·神示与落雷 | 8 自带 + 8 拆解 |
| [men.md](men.md) | 人·暴击与追加 | 8 自带 + 8 拆解 |
| [sea.md](sea.md) | 海·震荡与节奏控制 | 6 自带 + 6 拆解 |
| [underworld.md](underworld.md) | 冥·吸取与处决 | 6 自带 + 6 拆解 |

通用口径：

- 主动战法触发率见各条（伪随机补偿仅标注者使用；连携按各自触发率）。
- 追击（TIMING_PURSUIT）= 普攻命中后判定；被动（TIMING_PREPARE）= 准备回合必发；
  神谕（is_oracle）= 准备回合主将释放后触发连携。
- 「特殊伤害」= `damage_class=special`：播放但不触发响应，见 `mechanics/damage.md` §六。
- 旧 v0 池战法（gorgon_gaze / delphi_charged_oracle / pursuit_strike /
  pythia_woven_scheme 等）已随 Phase 3 移除。
