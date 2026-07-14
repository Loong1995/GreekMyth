# 伤害公式（Phase 3 标定版）

> 实现：`battle/formulas.py`（纯整数）+ `battle/engine.py` `deal_damage`。
> 标定单测：`battle/tests/test_formulas.py`。禁止为通过测试擅改本文标定值。
> 来源任务书：`docs/prompts/phase3_battlecomplete.md` §二。

## 一、核心项（技能系数前，min=1 安全截断）

| 类型 | 公式 | 说明 |
|---|---|---|
| 兵刃 physical | `max(1, 360 + 攻方武力 - 守方统率)` | `calc_core_physical` |
| 谋略 magic | `max(1, 360 + 攻方智力 - (守方统率+守方智力)//2)` | 半值合并后整数下取整，`calc_core_magic` |
| 真实 true | `max(1, 360 + 攻方武力 - 100)`；`ignore_defense` 时防御基准取 0 | 固定防御基准 `TRUE_DAMAGE_DEFENSE_BASE=100` |

属性取 `effective_attr`（面板 + 状态增减 + 性格静态加成）。

## 二、主公式（一次舍入）

```
Damage = round( 核心
              × TroopCoef                 兵力系数
              × (1 + damage_up_bps)       基础增伤（通用+类型，clamp 0~100%）
              × (1 - damage_reduce_bps)   减伤（clamp 0~80%）
              × (1 + extra_damage_up_bps) 额外增伤（独立乘区，clamp 0~100%）
              × (1 + vulnerable_bps)      易伤（clamp 0~100%）
              × random                    随机系数（9500~10500 bps）
              × crit                      会心/奇谋伤害率（暴击 20000 bps）
              × skill_rate_bps )          技能系数
       + fixed_extra_damage
```

- 全部 bps 万分比整数；乘区分子连乘后对 `10000^8` **一次四舍五入**（跨语言口径见
  `determinism.md`）。
- 结果 clamp：`MIN_DAMAGE=1 ≤ Damage ≤ 目标当前兵力`；目标已无兵返回 0。
- **额外增伤**（`extra_damage_up_bps`）是 Phase 3 新独立乘区：战法/追击单独加成，
  预留兵种/固士/驻守/同盟来源。
- 会心（物理）与奇谋（谋略）为同一实现（`crit_multiplier_bps`），默认 2.0 倍，
  受 `crit_damage_up_bps` 修正键抬升。

## 三、兵力系数 TroopCoef

`coef = 0.5 + 0.5 × (攻方当前兵力 / 10000)`，分母是**全局基准 10000**（与武将
自身上限无关，超编 NPC 系数 >1 是设计意图，决策 D-05 不截断）。

锚点（精确命中）：10000→100%、8000→90%、6000→80%、4000→70%。

## 四、落账拆分与最小语义

- 受击瞬间：`dead = floor(damage×30%)`，其余 70% 进伤兵池（`split_damage`）。
- 回合开始伤兵自然损耗：伤兵池 30% 转阵亡（`wounded_decay`），事件 `troops_change`。

## 五、格挡 / 闪避 / 反弹（0 结算，schema 1.2.0 / 1.3.1）

结算方在伤害落账**之前**查询目标特殊状态。**判定顺序（v3.2 定序规则）：按状态
施加到英雄身上的先后逐实例判定**（instance_id 升序=施加序；同一英雄同一时点
由技能安装格子顺序执行，格子序即施加序）。单实例内能力序固定：

1. **次数型格挡**：`block_charges` 计数直接消耗 1 次（不 roll）。
2. **闪避**：`evade_bps` 修正键几率 roll（普通随机，source=`evade`）。
3. **几率型格挡**：`block_rate_bps` roll（source=`block`）。
4. **反弹**：`reflect_rate_bps` roll（source=`reflect`）。

任一实例 roll 中即短路。实例可带 `mitigation_gate` 闸门（圣盾受明睿·匠心旁骛
压制时整实例跳过）。RNG 总序：减免逐实例 → 暴击 → 随机系数。

roll 中：伤害置 0 落账，仍发 `damage` 事件、payload 带
`mitigation="block"/"evade"/"reflect"`，供客户端播对应动画；**不算受到实际伤害**
——不吸血、不触发任何受击响应（D-20 口径推广）。DoT、`special` 伤害、
固定量伤害不参与判定。

**反弹**（1.3.1，圣盾）：仍完整走一遍主公式得到"本应受伤害"（暴击/随机系数
照常 roll，保持 RNG 流确定），受击方归零落账后，发 `status_tick`（反弹状态）
+ 子 `damage`（`damage_class="special"`，固定量、不可暴击、不可再被减免、
不触发响应/吸血，即不连锁）反弹给**攻击者本人**。

## 六、特殊伤害（震荡等）

`is_special=True` 的伤害（如三叉戟震荡、圣盾反制）：正常发 `damage` 事件
（payload 带 `damage_class="special"`）供播放，但**不触发任何产生伤害效果的响应**
（雷霆/血誓/试炼/凝视等一律不响应），且不参与格挡/闪避与吸血。

## 七、标定示例（与单测对齐）

兵刃：武力 120 vs 统率 100 → 核心 380；攻方 8000 兵（TroopCoef=0.90）、
技能系数 200%、增伤 20%、减伤 10%、随机 1.0 → **739**
（拆分 dead 221 / wounded 518）。

治疗公式 Phase 3 未变，见 `battle/formulas.py` `calc_heal`（基础 = 治疗者兵力上限
× 5% × 治疗率 × 智力系数 [0.6,1.5] × 乘区链）。
