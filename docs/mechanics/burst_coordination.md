# 连发与协击 + 站位 1~6（Phase 4 底座）

> 实现：`battle/engine.py`（`_cast_active_skill` / `perform_coordinated_attack` /
> `_dispatch_ally_basic_attack`）、`battle/skills.py`（`burst_rate_bps`）、
> `battle/setup.py`（position 1~6）。契约字段见 schema 1.4.0（payloads §5/§6）。

## 一、连发（burst）

自带主动战法的可选属性：`Skill.burst_rate_bps > 0` 时，成功释放后立即 roll
是否**再次释放**，可连续。

- **判定**：伪随机补偿，key=`(hero_id, skill_id, "burst")`，一局内真累计，
  参数 `burst_pseudo_random` 逐战法可调；≥100% 不消耗 RNG。
- **硬上限**：同一行动窗口内该战法总释放次数 ≤ **7**（首发 + 至多 6 次连发）。
- **每次释放独立选目标**；效果与首发完全相同。
- **准备型不重新准备**：release 段直接连发。
- **适用释放路径**：正常 cast、准备 release、犹豫延迟 release、连携 assist
  （统一走 `_cast_active_skill`，语义单点收口）。
- **事件**：连发的 `skill_trigger` 带 `burst_no`（2 起），与首发同 kind；
  自成播放组（每次释放一个播放单元），`parent_seq` 指回首发触发事件。
- **RNG 消费序**：首发效果内部 roll → 连发 roll → 连发效果内部 roll → …
- 每次释放记 `active` 轨势能 +1（见 momentum.md）。

## 二、协击（coordinated attack）

时机：**队友普攻每一击结算后**（追击分发之后），攻击者存活队友身上带
`on_ally_basic_attack` 钩子的状态按全局响应键
`(response_priority, hero_order 序, 他人/自身层, instance_id)` 定序
（determinism.md §2）。

钩子内由战法自行 roll 概率，命中则调用原语
`engine.perform_coordinated_attack(ally, target, parent_seq=ctx["damage_seq"])`：

- 一次普攻口径的兵刃攻击（系数 1.0），`normal_attack` 带 `kind="coordinated"`；
- **不 roll 连击**；命中后照常分发追击；
- **协击不再触发协击**（钩子只在真普攻后分发，防连锁）；
- 协击者被禁普攻 / 任一方阵亡 / 局已分胜负 → 静默跳过；
- 新播放组，组根 parent 指回引发它的普攻 `damage`（客户端镜头衔接同追击）；
- 记 `basic_pursuit` 轨势能 +1。

ctx 字段：`attacker / target / strike_no / damage_seq`。

## 三、站位 1~6

- `position` 合法域扩展为 **0~6**：1~3 前排、4~6 **后排**（`is_backline`，
  HeroSetup 与 HeroState 均有谓词）；0~2 为 Phase 3 旧口径，均视为前排（兼容
  旧 golden 与旧战报）。
- 后排语义由战法选人谓词消费（如「优先攻击后排」，A3 落地）；
  受击率选人本身不区分前后排（无变化）。

## 四、维护红线

- 连发/协击均为**注册表式可选能力**：未配置的战法/状态零行为差异，
  旧 golden 不变（`test_phase4_base.py` 固化）。
- 新的释放路径必须复用 `_cast_active_skill`，禁止绕开（否则连发/势能漏记账）。
