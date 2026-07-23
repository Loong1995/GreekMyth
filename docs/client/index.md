# 客户端主文档（index）

> 客户端唯一实现：**ClientBattle 战报驱动特效框架**（2026-07-09 依
> `docs/prompts/client_perform.md` 重构，替换并删除旧 `Assets/Scripts/Battle/`
> 三层实现与其配套文档）。代码 `Assets/Scripts/ClientBattle/`，5 层架构；
> 所有美术/音频资源占位化，上传即生效。逻辑已全量生效（含状态常驻光环、
> 整盘滤镜、单挑、台词气泡），当前处于"占位可跑"状态，成品化只差资源替换。

## 一、文档清单

| 文档 | 内容 |
|---|---|
| [playback_requirements.md](playback_requirements.md) | **战斗播放逻辑要求（行为规格书，R-条款）**：生命周期/播放单元/模板族/机制/鲁棒性要求全集，验收逐条对照 |
| [architecture.md](architecture.md) | **播放架构权威**：分层依赖规则、L4 拆分（控制器/建造器/导演/策略）、生命周期迁移表、子机制索引、服务端适配点 |
| [performance_mechanisms.md](performance_mechanisms.md) | **演出机制总纲（改演出先看这里）**：全部机制一句话结论+代码位置+细则索引 |
| [playback_units.md](playback_units.md) | **播放单元机制**：一回合怎么在屏幕上展开、分组管线六 processor、时间轴阻塞规则、台词独占三原则、模板族节拍 |
| [rendering_layout.md](rendering_layout.md) | **渲染/分辨率/层级**：机型适配、图像槽位缩放（contain/stretch）、sorting 层级总表、棋盘布局 |
| [text_system.md](text_system.md) | **文字系统**：飘字（Tuning SO 调参）、台词气泡独占播放、横幅/cut-in、中文名注册表 |
| [settlement_stats.md](settlement_stats.md) | **战后技能结算表**：分局 Tab、带技能（神谕）归因到施法者技能格、status→skill 映射 |
| [client_battle_framework.md](client_battle_framework.md) | **框架主文档**：5 层数据流向图、逐文件职责、关键机制（播放单元/补发重组/三级表演策略/占位回退）、运行方式 |
| [assets_upload_guide.md](assets_upload_guide.md) | **资源清单·现状·成品化路线（唯一资源文档）**：全部 key 与路径、每类现状、六步成品化流程、采购登记（原 to_purchase.md 已并入） |
| [faction_style.md](faction_style.md) | 四阵营视觉规范（配色同源 `Units/BattleBoardView.cs`） |
| [manual_setup_panel.md](manual_setup_panel.md) | **手动配阵测试页**：客户端内配阵 → python 结算 → 播战报/百场统计 |

## 二、快速上手

1. 新建空场景，空物体挂 `ClientBattle/Test/BattleReportTester.cs`。
2. 战报来源：`Assets/StreamingAssets/battle_reports/*.json`（或 Inspector 粘贴 JSON）。
3. Play 即自动播放；右上角按钮＝重播 / 跳到结尾 / 调速 / 高光回放 / 打开结算。
   默认 `DurationMul=2`（放慢一倍）+ 单元间隙，系列结束后自动弹出技能杀伤/治疗表。
4. 换演出/资源：见 assets_upload_guide.md（零代码）；改逻辑策略：
   `PerformanceDatabase`（三级配置）与 `Events/Processors/`（管线分析器）。

## 三、总体要求（复现规范——任何 AI 依此重写必须得到同构架构；
## 完整行为规格见 playback_requirements.md，架构约束见 architecture.md）

1. **服务器权威，客户端零结算**：客户端只消费战报事件流（schema `docs/schema/`），
   兵力恒取事件 `troops_after`，禁止任何数值推算/兜底。
2. **5 层单向数据流**：事件模型 → 事件管线（分组/重排）→ 三级演出配置解析 →
   演出执行（协程模板）→ 基础设施（池/飘字/音效/气泡/相机）。上层不得反向依赖。
3. **播放单元 = EventGroup**：按 `group_id` 聚合，一组一个演出协程；只有行动类组
   （主动/普攻/追击/状态触发/单挑）可占用时间轴，其余组即时落账零阻塞。
4. **零死帧**：所有等待必须对应正在播放的可见动画（位移/弹道/命中特效）；
   纯 `WaitForSeconds` 定格只允许出现在单挑横幅。
5. **配置驱动**：演出行为（模板/资源 key/尺寸/强度）全部走 `PerformanceProfile`
   三级查找（特殊配置→组默认→全默认），演出代码零硬编码、任何情况必能播出。
6. **占位回退**：全部资源 `Resources/ClientBattle/<类别>/<key>` 有则用、无则程序化
   占位（色块/合成音），上传同名文件即生效，零代码改动。
7. **预热前置**：战斗热路径零首次开销——VFX 离屏实渲预热、字形/图标/音效/气泡
   按战报内容开战前生成（`PlaybackWorldBuilder.PrewarmFromReport`）。
8. **机型兼容唯一权威**：`CameraFitter` 动态取景保安全区（半宽 4.6/半高 5.2）；
   表现层禁止写死 orthoSize/像素坐标；OnGUI 按屏高缩放。
9. **向前兼容**：未知事件类型/字段优雅忽略 + 告警（契约 1.x 加法演进）。

## 四、红线备忘

- **全项目根本规范见 `docs/discipline/`**（global_rules §四为客户端播放红线总纲）。
- 只读：`battle/`、`battlecore/`、`reference/`、`docs/schema/`、`docs/prompts/`。
- 中文名注册表 `Names/ChineseNames.cs` 与后端 `battle/names.py` 必须同步。
- 资源 key 新增：先登记 assets_upload_guide.md 再写代码。
- 性能验证以**独立版**为准（编辑器/远程桌面均有显示层干扰，2026-07-10 定案：
  独立版 60fps 满帧零长帧）；诊断工具 `Test/FrameSpikeProbe.cs`（Tester 勾
  ShowDiagnostics 启用）。
