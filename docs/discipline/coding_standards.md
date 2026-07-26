# 代码规范（后端 Python + 客户端 Unity）

## 一、通用

1. 代码标识符/id 一律英文 snake_case（Python）或 PascalCase（C#）；
   注释与文档一律中文，写「为什么」而非复述代码。
2. 禁止提交任何临时探针/调试脚本（`tmp_*` 用完即删）。
3. 每次实质改动必须：跑通对应测试 → 同步机制/契约文档 → changelog 追加。
   文档与代码不同步视为任务未完成。
4. 常量集中定义并带单位后缀（`_bps`、`_rounds`、`_seconds`）；
   魔法数字必须具名。

## 二、后端 Python（`battle/`）

1. 模块职责固定：`api.py` 入口校验 / `engine.py` 状态机 / `formulas.py` 公式 /
   `statuses.py` 状态模型 / `traits.py` 性格 / `skills_<faction>.py` 战法 /
   `skill_common.py` 共用工具 / `events.py`+`report.py` 事件与战报 /
   `rng.py`+`pseudo_random.py` 随机 / `roster.py`+`names.py` 数据表 /
   `version.py` 版本 / `textlog.py` 文本日志。新机制入既有模块或新建同级模块，
   禁止塞进 engine 巨型函数。
2. 战法类**无状态**（frozen dataclass 单例 + register）；运行期计数用状态实例
   `counters`/`round_counters`，禁止类属性存局内状态。
3. 持续型效果一律走状态响应钩子（StatusDef 挂钩子，引擎统一分发），
   禁止战法在引擎里安插专用 if。
4. 事件 payload 字段写入顺序固定（契约 pb# 序），禁止 dict 推导等不定序拼装。
5. 测试：新机制配单测；机制交互配交互矩阵格测试；结算路径变化必须重跑 golden
   并显式说明。测试文件 `battle/tests/test_<主题>.py`。
6. 类型注解 + docstring（模块头写职责与规则出处）；`from __future__` 风格
   跟随现有文件。

## 三、客户端 Unity（`Assets/Scripts/ClientBattle/`）

1. 五层架构不许跨层引用：Events（模型/管线）→ VFX（解析/演出/Runner）→
   Units/Audio（基础设施）→ Placeholder（回退）→ Test（入口）。
   新事件处理加 Processor（`EventPipeline.Register`），新演出派生
   `SkillPerformance`，禁止改 Runner 主循环塞特例。
2. 配置数据进 `PerformanceDatabase`（SO + 代码默认双轨，字段一致）；
   状态光环进 `UnitAuraService.StatusAuraTable`；禁止散落硬编码。
3. 性能红线：Update/OnGUI 内零 alloc（GUIStyle/字符串缓存）；特效必须走
   `VFXManager` 池；开战前 Prewarm（渲染级 + 战报驱动字形/音效/图标）；
   贴图导入压缩上限 1024、无 mipmap。
4. DOTween tween 必须持引用并在重入前 Kill（石化覆盖层教训）；
   对象回池必须复位 scale/alpha/粒子。
5. asmdef 依赖显式登记（Newtonsoft/DOTween/InputSystem）；
   新 Input System API，不用旧 Input。
6. **特效标准化**（权威 `docs/client/vfx_standardization.md`）：
   用户点名画廊/厂包件后，默认落盘 `Resources/ClientBattle/VFX/<key>.prefab`
   + 清洗死贴花/Projector + `VfxFitter`/`VfxGroundLayer` + guide 登记 +
   Profile 填 key。禁止 Profile 引用包目录；禁止为人做晋升 GUI/入队流程；
   可重跑的接线写成 Editor `MenuItem` 常量脚本（如 Wire*），批处理清洗用既有
   Standardizer 即可。

## 四、提交纪律

- 只在人工要求时 commit；golden 变更单独说明原因。
- 一次提交一个主题；机制改动与文档同步必须同一提交。
