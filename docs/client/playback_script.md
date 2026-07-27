# 播放编译（PlaybackCompiler / 播放流）权威

> 2026-07-27 客户端逻辑解析重整定论：**播放需求的全部逻辑解析在开播前一次
> 做完**，运行期只顺序消费编译产物。本文是编译层（L2 出口）的唯一权威；
> 分层总纲见 [architecture.md](architecture.md)，行为规格见
> [playback_requirements.md](playback_requirements.md)。

## 一、数据流

```
战报 JSON ─ BattleReport.Parse ─→ 强类型模型（含 skill_catalog）
      │
      ▼  PlaybackCompiler.Compile（开播前一次，PlaybackWorldBuilder 调）
  逐局： EventPipeline.Run（分组 + processor 链） → CutInPlanner.Annotate
      │
      ▼
 CompiledPlayback（逐局 List<EventGroup>，运行期只读）
      ├─ PlaybackDirector.PlaySeries      主循环
      ├─ PerformanceRunner 高光回放        同一份产物按窗口播
      └─ Editor 菜单导出 .playback.json    离线审阅（见 §四）
```

三个消费方读**同一份**编译产物；任何一方再自行跑管线/推断语义即违规。
SkipToEnd 静默落账走原始事件流（终态等价，与分组无关）。

## 二、skill_catalog（定义期标签，schema 1.5.0）

战法标签在**服务端定义处**声明（`battle.skills.Skill` 的
`damage_type`/`tags` 字段 + `category` 推导，register 强校验），经
`battle/skill_catalog.py` 进战报头。客户端 `BattleReport.SkillCatalog`
直读，编译层用途：

| 用途 | 位置 | 说明 |
|---|---|---|
| 追击 vs 主动分类 | `EventPipeline.Classify` | `category=="pursuit"` 直判，删 parent_seq 启发式（连发/借刀会让 parent 语义打架）|
| 伤害类型 | 演出层现读 `damage.damage_type`（逐条），目录为聚合视图/配阵页展示 | |

旧战报（<1.5.0，无目录）回落启发式并 LogWarning 一次；**不做向后兼容承诺**，
排查前先用 bridge / gen_golden 重新生成战报。

## 三、编译期 pass 清单（链序即语义，唯一登记处 `PlaybackCompiler.BuildPipeline`）

```
分组（group_id 全量聚合 + Classify）
→ BorrowBladeSplitProcessor      借刀按段拆单元（L3 谓词注入）
→ ReactionRegroupProcessor       响应 tick 摘出后置
→ CollectiveTriggerMergeProcessor 雷霆等集体齐发合并
→ TraitLineExtractProcessor      台词独占组抽取
→ AchillesPierceTagProcessor     傲慢贯穿图标闸门
→ NodeMergeProcessor             节点合并
→ CutInPlanner.Annotate          取景 cut-in 判定注记（非重排，只写 EventGroup.CutIn）
```

### CutInPlanner（原 L4 CutInPolicy 下沉）

- 判据与阈值全在 `Events/CutInPlanner.cs`：巨伤 >3000（mitigation 非空不算）、
  行动窗第 5 次追击、满档（cut_in 事件 + **势能预演**已满轨）。
- **势能预演**：满档判据需要「落账前镜像值」。势能事件自带落账后 `value`，
  预演按组序重放 (hero,track)→value，判定读应用本组之前的值——与运行期
  MomentumService 镜像逐组等价（同一事件流同一次序）。轨有效性谓词由 L4
  注入（`MomentumService.TrackTable`，避免轨表双真源）。
- 运行期 `PlaybackDirector.PlayGroup` 只读 `group.CutIn`
  （HeroId/Title/Empowered/Massive），不再持有追击计数、不再查镜像。
  编排形状（推镜→横幅→出手→撤镜）仍在 [cutin_stage.md](cutin_stage.md)。

## 四、导出 .playback.json（排查入口）

菜单 `GreekMyth → 播放 → 导出 PlaybackScript`：选一份战报 JSON，
在旁边落 `<名>.playback.json` —— 逐局逐组列出
kind / root_seq / 配置匹配 key / 事件清单 / cut-in 注记 / 并行与贯穿标记。
与运行期完全同源（同一 `Compile` 调用），所见即所播；
排「为什么这组这么演」先看导出文件，不需要进 Play 模式断点。

## 五、扩展纪律（加法式接入）

1. 新播放序语义 → 新 `IEventProcessor`，在 `BuildPipeline` 登记（注意链序）。
2. 新 cut-in 触发 → 只改 `CutInPlanner`。
3. 需要新的编译期决策（预演/快照类）→ 新增独立 pass 类 + 在 `Compile` 登记，
   产物写 EventGroup 注记字段；**禁止**在 Director/演出层运行期推断。
4. 需要新战法标签 → 服务端 `Skill.tags` 加法演进（客户端未知标签必须忽略）。
