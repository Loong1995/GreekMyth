using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 集体触发合并处理器（client_perform §二 雷霆神谕表演）：
    //
    // 「对于群攻主动类似的播放单元，播放完群攻主动后补发雷霆的一次**集体触发**」
    // ——群攻的每条伤害都可能各触发一次落雷，后端逐条 status_tick 成组；
    // 表演上应合并为一个播放单元一次齐发（N 道闪电同时指向各目标）。
    //
    // 合并条件（全部满足才并组，保守起见）：
    // - 相邻两组都是 StatusTrigger、组根都是 status_tick；
    // - 同 status_id 且同 source_id（同一持有者的同一状态连续触发）。
    // 圣盾等要求"逐次触发"的状态不在此列——通过 CollectiveStatusIds 白名单控制。
    // =========================================================================

    public class CollectiveTriggerMergeProcessor : IEventProcessor
    {
        /// <summary>要求集体齐发的状态白名单（文档明确"集体触发"的才进）。</summary>
        public static readonly HashSet<string> CollectiveStatusIds = new() { "thunder" };

        public List<EventGroup> Process(List<EventGroup> groups)
        {
            var result = new List<EventGroup>(groups.Count);
            foreach (var group in groups)
            {
                if (result.Count > 0 && CanMerge(result[^1], group))
                {
                    result[^1].Events.AddRange(group.Events);
                    continue;
                }
                result.Add(group);
            }
            return result;
        }

        static bool CanMerge(EventGroup prev, EventGroup next)
        {
            if (prev.Kind != GroupKind.StatusTrigger || next.Kind != GroupKind.StatusTrigger)
                return false;
            if (prev.Root is not StatusTickEvent a || next.Root is not StatusTickEvent b)
                return false;
            if (a.Status == null || b.Status == null) return false;
            return a.Status.StatusId == b.Status.StatusId
                   && CollectiveStatusIds.Contains(a.Status.StatusId)
                   && a.SourceId == b.SourceId;
        }
    }
}
