using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 反应重组处理器（client_perform §二 的"事件流重新解析排序分组"）：
    //
    // 后端把状态触发的结算（雷霆落雷 / 圣盾反制 / 海神震荡 / 试炼反打 / 凝视…）
    // 以 status_tick 为根挂在主动作组内（damage 的子链）。表演要求：
    // 「特殊状态触发类动画永远是其他播放单元执行完后再去播放」——
    // 群攻主动播完后补发雷霆的集体触发、圣盾的逐次触发。
    //
    // 做法：把组内每个 status_tick 及其后代事件（按 parent_seq 链）摘出，
    // 拆成独立的 StatusTrigger 组追加在原组之后；同一状态在同组内的多次 tick
    // 各自成组按原 seq 序排列（"逐次触发"）。摘除后的原组保持顺序不变。
    // 伤害响应类 tick 的 seq 序与引擎一致：先守后攻；同持有者他人施加先于自身
    // （determinism.md §2）——播放序跟随事件流，不在此二次重排。
    // =========================================================================

    public class ReactionRegroupProcessor : IEventProcessor
    {
        public List<EventGroup> Process(List<EventGroup> groups)
        {
            var result = new List<EventGroup>(groups.Count);
            foreach (var group in groups)
            {
                // 组根本身就是 status_tick（独立状态触发组）无需拆分
                if (group.Kind == GroupKind.StatusTrigger || group.Events.Count <= 1)
                {
                    result.Add(group);
                    continue;
                }

                var tickRoots = new List<StatusTickEvent>();
                foreach (var ev in group.Events)
                    if (ev is StatusTickEvent tick && tick.Seq != group.RootSeq)
                        tickRoots.Add(tick);

                if (tickRoots.Count == 0)
                {
                    result.Add(group);
                    continue;
                }

                // 按 parent_seq 传递闭包收集每个 tick 的后代
                var claimed = new HashSet<int>();
                var reactionGroups = new List<EventGroup>();
                foreach (var tick in tickRoots)
                {
                    // 批次随原组：摘出来的响应仍属于「引发它的那次行动」这一批
                    var reaction = group.Fork(tick, GroupKind.StatusTrigger);
                    reaction.Events.Add(tick);
                    claimed.Add(tick.Seq);
                    foreach (var ev in group.Events)
                    {
                        if (claimed.Contains(ev.Seq)) continue;
                        if (IsDescendant(ev, tick.Seq, claimed, group))
                        {
                            reaction.Events.Add(ev);
                            claimed.Add(ev.Seq);
                        }
                    }
                    reactionGroups.Add(reaction);
                }

                var trimmed = group.Fork();
                foreach (var ev in group.Events)
                    if (!claimed.Contains(ev.Seq))
                        trimmed.Events.Add(ev);

                result.Add(trimmed);
                result.AddRange(reactionGroups);  // 主单元播完后补发
            }
            return result;
        }

        /// <summary>ev 是否是 rootSeq 的后代（沿 parent_seq 链回溯，只在本组范围内查）。</summary>
        static bool IsDescendant(BattleEvent ev, int rootSeq, HashSet<int> claimed, EventGroup group)
        {
            int cursor = ev.ParentSeq;
            int guard = 0;
            while (cursor != 0 && guard++ < 64)
            {
                if (cursor == rootSeq || claimed.Contains(cursor)) return true;
                BattleEvent parent = null;
                foreach (var e in group.Events)
                    if (e.Seq == cursor) { parent = e; break; }
                if (parent == null) return false;
                cursor = parent.ParentSeq;
            }
            return false;
        }
    }
}
