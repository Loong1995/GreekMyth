# discipline：任何 AI/开发者开工前的根本上下文（必读）

> 本目录是全项目**不可违背**的规范与全局规则总纲。任何 AI 在本仓库开展任何工作，
> 第一步必须通读本目录全部文件；与本目录冲突的做法一律错误。
> 本目录条目只能**加法式演进**（新增条目/收紧规则），放宽或删除须人工批准并留记录。

## 阅读顺序

| # | 文件 | 内容 | 何时必读 |
|---|---|---|---|
| 1 | [project_overview.md](project_overview.md) | 项目一句话/世界观/阵营/术语定论 | 永远 |
| 2 | [global_rules.md](global_rules.md) | 机制全局规则（确定性/契约/事件流红线） | 改 `battle/` 或契约前 |
| 3 | [coding_standards.md](coding_standards.md) | 代码规范（后端 Python + 客户端 Unity） | 写任何代码前 |
| 4 | [doc_standards.md](doc_standards.md) | 文档规范（目录职责/行数/同步义务） | 写任何文档前 |
| 5 | [ai_workflow_pitfalls.md](ai_workflow_pitfalls.md) | 历史 AI 工作流踩坑录 | 永远；踩新坑必须追记 |
| 6 | [extension_points.md](extension_points.md) | 通用机制（注册表/钩子）与特殊机制（特例登记）总账本 | 新增战法/状态/性格/演出前 |

## 与其他文档的关系

- **契约**（唯一前后端共同依赖）：`docs/schema/battle_events.md` +
  `battle_events_payloads.md` + `battle_events.schema.json`。冻结，仅加法式演进。
- **机制细则**：`docs/mechanics/index.md` 起步，逐机制一文件。
- **战法语义**：`docs/skills/`（三段式：效果/实现/事件流）。
- **客户端**：`docs/client/index.md` 起步；核心机制 `performance_mechanisms.md`。
- **历史存档**（只读参考，不代表现状）：`docs/prompts/`（任务书）、
  `docs/dev/v0_analysis.md`、`phase3_plan.md`、`decisions_client_phase2.md`、
  `performance.md`（旧基准）。
- **决策记录**：`docs/dev/decisions.md`（D 系列玩法决策，现行有效）。
- **变更日志**：`docs/dev/changelog.md`，每个工作会话结束必须追加。

## 本目录维护规则

- 每文件 ≤500 行；新增文件必须在上表登记。
- 工作中发现规则空白 → 先按最保守做法执行，再在对应文件补条目。
- AI 工作流踩了新坑（返工/误判/被人工纠正）→ 当次会话内写入
  `ai_workflow_pitfalls.md`，格式见该文件头部。
