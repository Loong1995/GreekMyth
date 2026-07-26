# 神舞台「奥林匹斯山巅」具体实施方案

> 状态：**现行实施方案**（2026-07-24，隶属 [stage_plan.md](stage_plan.md) 第 1 周）。
> 效果定数：神阵营（faction=olympus）英雄造成伤害时 10%（1000 bps）几率
> **伤害双倍**，整场被动；触发时播赫拉神像交互动画。

## 一、core 侧（battle/，先行落地，零美术成本）

### 1. 注册表 `battle/stages.py`（新建）

- `StageDef(stage_id, name, factions: frozenset[str], buff_factory)`，
  `STAGE_REGISTRY: dict[str, StageDef]`，首个条目 `"olympus"`（惠及 `{"olympus"}`）。
- 常量 `OLYMPUS_DOUBLE_RATE_BPS = 1000`；状态 id `stage_olympus_favor`
  （赫拉眷顾，BUFF，`duration_rounds=PERMANENT`）。

### 2. 双倍判定挂 `on_pre_damage_dealt` 钩子（现成扩展点）

- 判定在状态定义内实现：roll `engine.rng.rand_bps("status_trigger",
  f"stage_olympus:{source.hero_id}")`，中则 `pre_ctx["extra_up_bonus"] += 10000`
  （`extra_damage_up_bps` 独立乘区 +100% ＝ 该次伤害精确 ×2）。
- 中签后在伤害事件落账后以 `damage_seq` 为 parent 走 `emit_status_trigger`
  带出触发事件（同雷霆印记 thunder 的挂法，`skill_common.emit_status_trigger`）——
  客户端凭该事件播神像动画，**契约纯加法**（复用既有 status_trigger 事件类型）。
- 触发范围限定：只对主动/普攻/追击的常规伤害生效；反弹、震荡（`damage_class=
  "special"`）等衍生伤害不 roll（实现时在钩子内按 `pre_ctx["kind"]` 过滤，
  最终口径实现当日定案并写入 `docs/mechanics/stages.md`）。

### 3. 接线与配置

- `BattleSetup` 增可选字段 `stage: str = ""`（默认空＝行为完全不变，
  golden 零扰动）；`validate_setup` 校验 stage_id 在注册表内。
- 每局 `game_start` 后按 `hero_order` 确定序给 faction 命中的存活英雄挂
  `stage_olympus_favor`（接线点紧邻 `_apply_formation_buffs`，同构复用）。
- 阵营判定读 `roster.FACTION_OF`，不新增武将字段。
- `manual_battle.py` / `client_battle_bridge.py` / `test_manual_3v3.py`
  同步透传 `STAGE` 配置项（照抄 formation 的透传方式）。

### 4. 双端同步与文档义务

- `names.py` ↔ `ChineseNames.cs` 登记 `stage_olympus_favor`＝「赫拉眷顾」。
- 新建 `docs/mechanics/stages.md` 并在 mechanics index 登记；
  `extension_points.md` 追加 `stages.STAGE_REGISTRY` 注册表条目。
- 单测 `battle/tests/test_stages.py`：默认无舞台不变；olympus 阵营才挂 buff；
  触发时伤害恰为不触发的 2 倍（同 seed 对照）；status_trigger 事件带出。

## 二、客户端侧（ClientBattle/）

### 1. 骨架（占位可先行）

- 舞台背景管理：按战报 `stage` 元数据加载
  `Resources/ClientBattle/Stages/olympus/` 下分层资源，缺资源走占位色块
  （三级回退照旧）。
- 神像＝BoardActor（挂在背景层，sorting 低于卡牌）：待机呼吸（代码缩放/
  透明度循环）+ 触发动画两态。
- `StatusPresentationRegistry` 登记 `stage_olympus_favor`（金色 BUFF 色系，
  不进控制类图标行）。
- 触发播放：解析到 `stage_olympus_favor` 的 status_trigger 事件 → 神像双目
  亮金（发光层 fade in）→ 光束 VFX 从神像洒向该英雄卡 → 卡面金色描边
  0.6s（AllIn1 outline，复用势能描边参数思路）→ 飘字「赫拉眷顾·双倍」。
  归入所属伤害组的时间轴，不另占播放单元（零死帧）。

### 2. 分层资源清单（AI 出静态，特效包出动态）

| 层 | 资源 key（Stages/olympus/） | 类型 | 来源 |
|---|---|---|---|
| L0 远景 | `bg_sky`（星空+云海全景） | 静态 | AI |
| L1 云海 | `cloud_layer`（可平铺横条） | 代码 UV 滚动 | AI |
| L2 主景 | `bg_temple`（山巅神殿平台＝棋盘地） | 静态 | AI |
| L3 神像 | `statue_hera_base` / `statue_hera_glow`（发光件分层） | 静态+代码 fade | AI |
| L4 装饰 | `relief_peacock_l/r`（孔雀浮雕）、`emblem_center`（徽记，呼吸） | 静态+代码 | AI |
| 动态 | 天空远雷（Realistic Effects Pack 4 雷暴，占位期先用 Vefects）、神像洒光束（kripto289 Magic Effects Pack 1 光束）、山巅光柱（Lumen 2 god ray）、环境金尘粒子（kripto 家族抠件） | 粒子/网格光 | 见 stage_plan §四清单与风格基准 §四.0 |

AI 出图规则（工序铁律）：全部垫特效包截图做风格参考；出 2x 尺寸再缩，
神像发光件与本体分层出图（同构图两次生成或手动抠层）。

### 3. 触发动画时间轴（目标 ≤1.2s，不阻塞后续单元）

1. 0.00s 神像眼部发光层 fade in（0.15s）
2. 0.15s 光束 VFX 神像→英雄卡（0.3s，特效包光束改金色）
3. 0.45s 卡面金描边亮起 + 飘字（0.6s 内淡出）
4. 1.05s 眼部发光 fade out

## 三、实施步骤与验收

| 步 | 内容 | 依赖 |
|---|---|---|
| S1 | core：stages.py + 接线 + 单测 + 双端名字 + mechanics 文档 | 无（当天可完成） |
| S2 | 客户端骨架：背景分层管理 + 神像占位 BoardActor + 触发动画链路（占位色块/现有 VFX 顶替） | S1 战报可产出 |
| S3 | 按 stage_plan §四清单购包（先 kripto289 主包提风格卡，史诗写实基准）→ AI 出 L0~L4 全部静态 | 采购 |
| S4 | 资源替换 + 光束/远雷接包内粒子 + 色调分级 | S2+S3 |

**验收**：默认（无 stage）战报与 golden 完全不变；同 seed 下触发伤害＝
不触发的 2 倍；触发动画每场 2~4 次观感不刷屏、独立版 60fps；
任截一屏画风统一不出戏。

## 四、进度记录

| 日期 | 进展 |
|---|---|
| 2026-07-24 | 方案定稿 |
