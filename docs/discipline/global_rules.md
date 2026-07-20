# 机制全局规则（不可违背）

> 改 `battle/`、契约或客户端播放逻辑前必读。细则见 `docs/mechanics/`；
> 本文只列**红线级**约束，违反即 bug/返工。

## 一、确定性（全项目最高优先级）

1. 单一 RNG 流（`battle/rng.py`，PCG），种子外部注入；core 内禁产种子。
2. 禁止：系统时间、标准库 random、os 熵、浮点参与结算、依赖内存地址/无序遍历。
3. 一切影响结算顺序的集合遍历必须有**显式排序键**
   （登记表：`docs/mechanics/determinism.md` §2）。Python dict 插入序不算数。
4. 新增 RNG 消费点必须在 determinism.md §1 登记；消费顺序即代码执行顺序。
5. 数值一律整数/定点；概率与系数用 **bps 万分比**（10000=100%）。
   伤害乘区连乘后对 `10000^8` 一次四舍五入，禁止默默改变舍入点。
6. 同 `(rng_seed, setup, core_version)` 必须逐字节复现——golden 测试保证。

## 二、契约（事件流 Schema）

1. 契约三件：`docs/schema/battle_events.md` + `battle_events_payloads.md` +
   `battle_events.schema.json`。**冻结**，仅允许加法式演进
   （新增事件类型/新增可选字段），禁止修改已有字段语义。
2. 任何契约演进必须：升 `SCHEMA_VERSION`（`battle/version.py`）＋
   更新三件契约文档＋在总纲 §7 版本演进表登记。
3. 客户端向前兼容义务：未知事件类型/未知字段跳过继续播，永不中断。
4. 调试信息（`_debug_rolls` 等下划线开头顶层键）不进正式战报 JSON
   （`serialize_report` 过滤）。

## 三、结算规则红线

1. `skill_files.py`（旧 core 标杆）与 `docs/skills/` 描述是标杆语义：
   只能照描述实现，不得改动描述迁就实现。
2. 禁止为通过测试修改已标定公式或 golden；golden 变更必须显式重跑
   `gen_golden.py --write` 并在 commit 说明原因。
3. core 内部错误不得静默吞掉：结算异常必须使战斗失败并输出完整上下文
   （`BattleCoreError`），禁止半截战报。
4. 原语对已阵亡目标一律 no-op（不结算、不发事件、不消耗 RNG）。
5. 顺序裁定通则（v3.2 定）：同类状态能力冲突时，**按状态施加到英雄身上的
   顺序（instance_id 升序）逐实例判定**；同一时点再按技能安装格子顺序。
6. 破平通则（D-08）：站位序（0→2）→ 队伍序（A→B）。

## 四、客户端播放红线

1. **零客户端结算**：表现层只读事件；兵力恒取 `troops_after`。
2. **零死帧**：一切 `yield` 等待必须对应可见动画；纯 WaitForSeconds 垫时间
   禁止（单挑横幅唯一例外）。节奏停顿（ActionPause/GroupPause）期间
   常驻动画（待机呼吸/光环）必须继续。
3. **播放单元完整性**：按 `group_id` 全量聚合（非连续段）；群攻是一个单元
   一次放出；节点组全部子事件必须 ApplySilently 落账（漏掉→图标/覆盖层残留）。
4. **占位三级回退**：真资源 → 程序化占位，任何配置缺失播放不中断，仅告警一次。
5. **机型兼容唯一权威**：取景只经 `CameraFitter`；表现层禁止写死
   orthographicSize/像素坐标。
6. 特效缩放只做相对乘法（`*=`），禁止覆盖 localScale；回池由
   `VfxOriginalScale` 复位。
7. 中文名表 `Names/ChineseNames.cs` 与后端 `battle/names.py` 必须同步。

## 五、目录权限

- `battlecore/`、`reference/`：**只读，禁止修改其中任何文件**。
- `docs/prompts/`：任务书只读存档。
- 新战斗实现只在 `battle/`（含 tests/、benchmarks/、tools/）。

## 六、验收基线（改动后必跑）

- `python -m pytest battle/tests -q` 全绿（含 golden 逐字节）。
- 契约相关改动：`battle_events.schema.json` 校验 + replay_report 重放闭环。
- 客户端改动：Unity 编译零错误 + `BattleReportTester` 播完一份标准战报。
