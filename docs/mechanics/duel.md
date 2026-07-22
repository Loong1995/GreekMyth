# 单挑（duel）

> 规则来源：决策 D-03 演进（2026-07-21 配对升级）。
> 实现：`battle/engine.py::_run_duel`；羁绊表 `battle/bonds.py`；
> 测试：`battle/tests/test_duel.py`。

## 1. 触发时点

- **仅第 1 局**开局：`game_start` 之后、所有战法（含准备回合神谕）之前，
  独立 DUEL 相位（`t.p=2`）。第 2 局及以后不再判定。

## 2. 参赛与初对

1. **参赛选手**：双方存活且有效武力 **>** 有效智力；队内按
   （武力↓，站位↑，hero_id）排序。
2. **序号对位**：两队同序号 `zip` 到 `min(lenA,lenB)` 形成初对。
3. **羁绊初对**：`weight∈{1,2}`（S1/S2，见 `docs/character/bonds.md` /
   `battle/bonds.py`）且分属两队的模板对，追加为初对。
4. 同一武将可出现在多对；同键 `(id_lo,id_hi)` 保留更佳（更小）羁绊 weight。
5. 任一方无参赛选手 → **不演绎**。

## 3. 入池概率与取对

对每条初对，武力差 \(d=|F_a-F_b|\)：

| \(d\) | 入池率 |
|---|---|
| 0 | 90% |
| 0~50 | 线性：`9000 - d×170` bps |
| ≥50 | 5% |

RNG：`duel_pair`（按初对确定性序逐条 roll）。

- **候选池非空**：按（羁绊 weight↑，无羁绊=99；武力差↑；id）排序，取第 1 对
  → **真决斗**。
- **候选池空、但有初对**：同序取第 1 对 → **固定叫阵-拒绝**（不 roll 胜负、
  无四维惩罚）。

## 4. 真决斗结算

叫阵方 = 该对武力高者（相等则 A 队侧）。

| 步骤 | 公式 | RNG |
|---|---|---|
| 拒绝 | 低武力方拒绝率 = `d × 8%`，封顶 80% | `duel_reject`（d=0 不 roll） |
| 胜负 | 高武力方胜率 = `50% + d`（百分点），d≥50 必胜 | `duel_win` |
| 惩罚 | 负者四维立即 -10（`attr_change scope=game`） | 无 |

性格**不**改写拒绝/胜负判定（约战机械表已废除）。

**台词**（`battle/voice_lines.py`）：按说话者 `template_id` 双池
（对方模板羁绊池 → `generic`），发 `trait_trigger`（`effect=duel_*`），
**挂在 duel 组内**（`parent_seq`→challenge/result）。文案权威
`docs/character/*.md`，机器表 `battle/voice_duel_data.py`
（`python battle/tools/_extract_duel_voice.py` 重抽）。

## 5. 事件与 cut-in

```
duel_challenge（组根：双方 id/武力 + clash_cutins）
 ├─ trait_trigger effect=duel_challenge（叫阵方）
 ├─ trait_trigger effect=duel_accept（接受时，防守方）
 └─ duel_result
     ├─ trait_trigger effect=duel_reject（拒绝时，防守方）
     └─ attr_change（仅 accepted：负者四维-10）
```

客户端 `PlayDuel`：号角 → 叫阵气泡 →（拒绝横幅+拒战气泡 |
应战气泡 → **全屏裂缝交错 cut-in**（`CutInService.DuelClashRoutine`，
两半屏卡对向滑过中央裂缝线 × clash_cutins 次）→ 胜者横幅）。
`TraitLineExtractProcessor` **不**抽 Duel 组内台词。

`clash_cutins`：武差 ≤10 → 3；≤20 → 2；否则 1。

## 6. 边界

- 单挑不掉兵、不产生伤害事件。
- 惩罚随第 1 局 game_end 回滚，不带入第 2 局。
- 无词库／空池 → 静默（不发 trait_trigger）。
