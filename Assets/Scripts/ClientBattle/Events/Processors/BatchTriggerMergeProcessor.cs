using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 批次触发合并处理器（playback_units.md §二「同批次同状态并成一发」）：
    //
    // 播放的最小粒度是**播放单元**，一个单元内的东西一起同步播出。一次群攻打到
    // 对面三人，三人各自触发的落雷在因果上是**同一批**（同一次行动引发），观感上
    // 就该是「下一个单元：三道雷一起劈」，而不是劈三次。
    //
    // 合并判据（前身 CollectiveTriggerMergeProcessor 只认「相邻 + 白名单」，
    // 中间夹一个节点或别人的响应就并不起来，2026-07-28 换成批次口径）：
    //   1. 双方都是 StatusTrigger 组、组根都是 status_tick；
    //   2. 同 `BatchId`（同一次行动引发）且同 `status_id`；
    //   3. 状态未被标 `sequential`（圣盾反制、代战借刀必须逐次成单元）；
    //   4. 同持有者；**或**状态标了 `simultaneous`（落雷这类演出与持有者无关，
    //      跨持有者也能并）。跨持有者并组对「持有者突进」型演出是致命的
    //      ——一个人替所有人挥刀，故默认不并。
    //
    // 标签真源在**服务端定义处**（`StatusDef.playback_tags` → 战报头
    // `status_catalog`，schema 1.5.2）；旧战报无目录时回落客户端
    // StatusPresentationRegistry 的 CollectiveMerge 标记。
    //
    // 并组不跨批次，因此不会把不同回合/不同行动的触发揉到一起；被并的组按
    // 首次出现位置落位（组内事件仍按各自 seq 序）。
    // =========================================================================

    public class BatchTriggerMergeProcessor : IEventProcessor
    {
        readonly BattleReport _report; // 可空＝旧战报，回落客户端注册表

        public BatchTriggerMergeProcessor(BattleReport report = null) => _report = report;

        public List<EventGroup> Process(List<EventGroup> groups)
        {
            var result = new List<EventGroup>(groups.Count);
            var anchors = new Dictionary<(int, string), EventGroup>();
            foreach (var group in groups)
            {
                if (!Mergeable(group, out string statusId))
                {
                    result.Add(group);
                    continue;
                }
                var key = (group.BatchId, statusId);
                if (anchors.TryGetValue(key, out var anchor) && SameActor(anchor, group, statusId))
                {
                    anchor.Events.AddRange(group.Events);
                    continue;
                }
                anchors[key] = group;
                result.Add(group);
            }
            return result;
        }

        static bool Mergeable(EventGroup group, out string statusId)
        {
            statusId = null;
            if (group.Kind != GroupKind.StatusTrigger) return false;
            if (group.Root is not StatusTickEvent tick || tick.Status == null) return false;
            statusId = tick.Status.StatusId;
            return !string.IsNullOrEmpty(statusId);
        }

        /// <summary>演出主体是否兼容：同持有者恒可并；不同持有者只有
        /// `simultaneous`（与主体无关的齐发型，如落雷）才可并。</summary>
        bool SameActor(EventGroup a, EventGroup b, string statusId)
        {
            if (HasTag(statusId, "sequential")) return false;
            var ta = (StatusTickEvent)a.Root;
            var tb = (StatusTickEvent)b.Root;
            if (ta.Status?.OwnerId == tb.Status?.OwnerId) return true;
            return HasTag(statusId, "simultaneous");
        }

        bool HasTag(string statusId, string tag)
        {
            if (_report != null && _report.StatusCatalog.Count > 0)
                return _report.StatusHasTag(statusId, tag);
            // 旧战报（schema < 1.5.2）：只有集体标记可回落，sequential 无从得知
            return tag == "simultaneous" && Names.StatusPresentationRegistry.IsCollective(statusId);
        }
    }
}
