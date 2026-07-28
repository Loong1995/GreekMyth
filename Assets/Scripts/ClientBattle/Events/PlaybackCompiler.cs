using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClientBattle.Events
{
    // =========================================================================
    // 【第2层 事件管线·出口】PlaybackCompiler：战报 → 编译好的播放流（单真源）。
    //
    // 2026-07-27 重整定论：播放需求的**全部逻辑解析在开播前一次做完**，运行期
    // Director 只顺序消费编译产物，不再边播边跑管线/边推断语义。收益：
    //   - 同一战报编译结果恒定（管线只跑一遍，主循环/高光/Skip 三处消费同一份，
    //     不再各自 Run 一遍可能因 processor 内部状态漂移）；
    //   - 排查：编译产物可整体导出 .playback.json 离线审阅；
    //   - 分类读 skill_catalog（schema 1.5.0，定义期声明），删启发式。
    //
    // processor 链序是**播放语义的一部分**，唯一登记处在本类（曾散在
    // WorldBuilder，链序改动难以审阅）。新增播放序语义＝新增 IEventProcessor
    // 并在 BuildPipeline 登记（加法式接入，扩展点 R-7.7）。
    // =========================================================================

    /// <summary>编译产物：与 Report.Games 同序的组列表。运行期只读。</summary>
    public class CompiledPlayback
    {
        public BattleReport Report;
        /// <summary>逐局播放单元（已过完整 processor 链、已分类）。</summary>
        public List<List<EventGroup>> GameGroups = new();

        public List<EventGroup> GroupsOf(int gameIndex) =>
            gameIndex >= 0 && gameIndex < GameGroups.Count ? GameGroups[gameIndex] : null;
    }

    public static class PlaybackCompiler
    {
        /// <summary>标准 processor 链（唯一登记处，链序即语义，改动须过评审）：
        /// 借刀分段 → 响应后置 → 批次触发合并 → 台词独占抽取 → 贯穿标记 → 节点合并。
        /// <paramref name="borrowBlade"/> 为 L3 注入的借刀判定谓词
        /// （L2 不得直接引用 PerformanceProfile）。</summary>
        public static EventPipeline BuildPipeline(BattleReport report,
                                                  Func<EventGroup, bool> borrowBlade)
        {
            return new EventPipeline(report)
                // 借刀（代战/披甲）按段拆单元并回插事件流原生位置：
                // 段1(借手)→响应→追伤→段2…（不拆会三刀连劈再补账）
                .Register(new BorrowBladeSplitProcessor(borrowBlade))
                .Register(new ReactionRegroupProcessor())        // 状态触发摘出，排主单元之后
                // 同批次同状态的触发并成一个播放单元（落雷齐发）；标签真源在
                // 服务端 status_catalog（1.5.2），旧战报回落客户端注册表
                .Register(new BatchTriggerMergeProcessor(report))
                .Register(new TraitLineExtractProcessor())       // 台词拆成独占 TraitLine 组
                .Register(new AchillesPierceTagProcessor())      // 傲慢贯穿 → 裂甲图标闸门
                .Register(new NodeMergeProcessor());
        }

        /// <summary>全量编译：逐局「跑管线 → cut-in 判定注记」，产出运行期只读的
        /// 播放流。开播前调用一次（PlaybackWorldBuilder），此后任何消费方
        /// （主循环/高光/Skip 静默落账）都不得再自行跑管线。
        /// <paramref name="isKnownTrack"/> 势能轨有效性谓词（注入
        /// MomentumService.TrackTable，避免轨表双真源）。</summary>
        public static CompiledPlayback Compile(BattleReport report,
                                               Func<EventGroup, bool> borrowBlade,
                                               Func<string, bool> isKnownTrack)
        {
            if (report == null) return null;
            if (report.SkillCatalog.Count == 0)
                Debug.LogWarning("[PlaybackCompiler] 战报无 skill_catalog（schema<1.5.0），" +
                                 "分类回落 parent_seq 启发式；建议用 bridge 重新生成战报");
            var compiled = new CompiledPlayback { Report = report };
            var pipeline = BuildPipeline(report, borrowBlade);
            foreach (var game in report.Games)
            {
                var groups = pipeline.Run(game.Events);
                CutInPlanner.Annotate(groups, isKnownTrack); // 势能预演逐局清零
                compiled.GameGroups.Add(groups);
            }
            return compiled;
        }
    }
}
