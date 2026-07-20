# 控制/延迟类状态交互矩阵（status interactions）

> 任务书 B3 硬性要求：逐格写明结算结果、逐格配测试。
> 测试文件：`battle/tests/test_status_interactions.py`（格号与测试一一对应）。
> 控制状态定义见 statuses.md；犹豫细则见 hesitation.md。

## 矩阵总表

| # | 交互 | 结算结果 | 测试 |
|---|---|---|---|
| 1a | 缄默 × 准备中 | 施加瞬间打断：`status_apply` + `skill_trigger(kind=interrupted, interrupted_by=缄默)` 两事件**同组**（两种特效并列播放）；准备登记清除，无 release | `test_cell_silence_interrupts_prepare` |
| 1b | 冥锁/石化 × 准备中 | 同 1a（凡施加 forbid_active 的控制均打断） | `test_cell_ming_lock_and_petrify_also_interrupt` |
| 1c | 缴械 × 准备中 | **不打断**（缴械只禁普攻），准备照常推进 | `test_cell_disarm_does_not_interrupt_prepare` |
| 2a | 石化 × 暴击（公式） | 石化 +10% 落**易伤乘区**（D-01）加法叠加；暴击是独立乘区，二者连乘：石化+暴击 ≈ 2.2 倍 | `test_cell_petrify_vulnerable_stacks_additively_with_crit` |
| 2b | 石化 × 暴击（承受方） | 被石化者仍可被暴击，易伤对暴击伤害同样生效；石化不改变攻方暴击率 | `test_cell_petrified_hero_can_still_be_crit` |
| 3a | 犹豫 × 冥锁 | 全禁无可延后行动 → **不做犹豫判定（不消耗 RNG）**、无动作事件、action_start 标 skipped；犹豫照常在窗口开始时计次（Phase 3 前移） | `test_cell_hesitation_with_ming_lock_no_delay_roll` |
| 3b | 犹豫 × 缄默 | 主动被禁不参与；普攻仍可被延后（delayed 宣告 skill_id=basic_attack） | `test_cell_hesitation_with_silence_delays_basic_only` |
| 4a | 延迟到期 × 施法者被缄默 | 主动部分作废（静默）；普攻部分照常补打 | `test_cell_delayed_active_voided_by_silence_basic_still_lands` |
| 4b | 延迟到期 × 施法者被冥锁/石化 | 主动与普攻一并作废；到期条目清除（不顺延） | `test_cell_delayed_all_voided_by_ming_lock` |
| 5 | 延迟到期 × 原目标阵亡 | 生效时点按战法目标规则**重新选目标**；无合法目标作废 | `test_cell_delayed_action_reselects_target` |
| 6a | 施法者阵亡 × 延迟中行动/准备中战法 | 随 `hero_defeated` 清理一并作废（静默，无补发事件） | `test_cell_defeat_clears_delayed_and_preparing` |
| 6b | 延迟/准备 × 跨局边界 | 战时状态不跨局：随 game_end 清空作废 | `test_cell_game_reset_clears_delayed_and_preparing` |
| 7a | 石化（普攻命中后反制施加）× 追击 | 禁普攻即无追击：追击分发前检查 forbid_basic/forbid_pursuit，被反制石化后追击**不触发** | `test_cell_petrify_mid_chain_stops_pursuit` |
| 7b | 无控制 × 追击（对照组） | 100% 追击战法必触发，事件跨组结构正确 | `test_cell_pursuit_fires_without_control` |
| 8 | 冥锁/石化 × DoT | 控制**不冻结** DoT/HoT：受控者的周期结算照常进行 | `test_cell_dot_ticks_while_owner_controlled` |

## 补充语义说明

- **打断只发生在「施加成功」时**：负面控制默认不可刷新——目标已有同 id 控制时
  重复施加被静默拒绝，不会再次产生打断事件。
- **犹豫计次与延迟判定解耦**（D-02 二次修订 2026-07-05）：延迟 roll 每窗口一次；
  roll 中固定延后 1 回合（N→N+1）；重复施加为刷新不叠层，已登记的延迟行动
  不受刷新影响；持续到期在行动窗口开始统一计次移除（Phase 3 前移）。
- **连击 × 控制**：两击之间重新检查 forbid_basic（第一击的反制控制能取消第二击），
  见 pursuit_combo.md §2。
- **死亡边界**（任务书 5.5）：死亡瞬间未结算的 DoT 不再结算（来源阵亡时状态已清理；
  持有者阵亡时状态随身清空）；死亡者是连携来源（主将阵亡）→ 收局优先，后续连携不发生。
