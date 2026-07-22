# 势能系统（Phase 4，四轨；纯表现记账）

> 实现：`battle/engine.py`（`add_momentum` / `momentum_of` / `MOMENTUM_TRACK_OF_KIND`）。
> 契约：`momentum_change` 事件（schema 1.4.0，payloads §25）。
> 演出：满轨切入 + 溢出「神格化」三拍，见 `docs/dev/phase4_plan.md` C7 与客户端 B2。

## 一、定性红线

- **纯表现**：势能不参与任何结算、不消耗 RNG、不影响任何数值。
  引擎关闭势能与开启势能生成的战报，除 `momentum_change` 外逐事件相同
  （`test_phase4_base.py::test_momentum_enabled_full_battle` 固化）。
- **门控**：契约 1.4.0 冻结（2026-07-20）起**默认开启**（golden 已全量重生成）；
  `setup.metadata["enable_momentum"]=False` 可显式关闭。

## 二、四轨记账

每武将独立四轨，互不相通，**按轨类型跨技能累计**（不是每个技能一条独立条；
例如主动轨叠「战吼释放 + 猛攻释放 + 连发每一发」），无上限：

| 轨 | 记 +1 的时点 | 备注 |
|---|---|---|
| `active` 主动 | 主动战法每次释放（cast/release/assist/连发各 +1）；主动伤害暴击 | — |
| `passive` 被动 | **追伤等触发类伤害每次落地 +1**（`MOMENTUM_ON_TRIGGER_KINDS`，当前含 `fury`；不要求暴击、暴击不双计）；其它未登记 kind 的暴击兜底 | 可加法扩展 trial 等 |
| `oracle` 神谕 | 神谕类伤害暴击（lightning/trident/reflect 归轨） | 待扩展：神谕状态触发每次 +1 |
| `basic_pursuit` 普攻/追击 | 普攻命中（未被闪避）+1；触发追击 +1；协击 +1；该类暴击再 +1 | 连击第二击已天然计入 |

- **归轨注册表**：`MOMENTUM_TRACK_OF_KIND`（伤害 `kind` → 轨），未登记 kind
  兜底 `passive`。A3 新增触发类 kind 时必须同步登记（可扩展红线）。
- **清零**：武将自身 `action_start` 时其四轨静默清零（不发事件，客户端同步清零）。
  即一轮势能 = 自己上次行动开始到本次行动开始之间的所有触发。

## 三、事件与满轨切入

`momentum_change`：`{hero_id, track, delta:1, value, reason, cut_in?}`，
`parent_seq` 指向引发事件（随组折叠播放）。

- **满档 `MOMENTUM_FULL = 5`**：`value ≥ 5` 当次及之后该轨每次触发带
  `cut_in=true`，客户端播切入横幅。
- **闪光档 = 4**（仅客户端）：该轨首次 `value ≥ 4` 播白闪爆发；不入事件字段。
- 分档表现：0~3 半亮条 → ≥4 全亮+首次闪光 → ≥5 常驻流光 + cut_in。
- **势能火**（客户端，`momentum_fire`←CFXR3 Fire）：取该武将四轨**最高值**——
  ≥4 小火 / ≥5 / ≥6 / ≥7 满分大；上一行动窗结束进入 `ActionPauseSeconds` 时
  **场上火渐灭并销毁**（hold-off，禁止窗间因账本仍满重挂）。势能条仍待自身
  下次 `action_start` 清零。与冥火状态无关。

## 四、BGM 全局势能

BGM 强度 = 场上双方全部存活武将四轨之和（客户端本地聚合，无需新事件），
分层调度见 phase4_plan B3。

## 五、维护

- 新增记账时点一律经 `engine.add_momentum`（事件字段/门控/满轨判定单点收口）。
- reason 标签自由扩展（契约字符串字段），textlog all 档打印，brief 档不打印。
