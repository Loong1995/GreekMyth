using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientBattle.Events
{
    // =========================================================================
    // 借刀分段处理器（2026-07-22）：
    //
    // 借刀战法（代战/披甲，profile.BorrowBlade）一次触发打出多段伤害，
    // 每段由不同的"借手"武将执行；服务端事件流里各段之间交错着该段
    // 引出的响应（圣盾反弹等）与追伤（阿喀琉斯之怒）——它们各有自己的
    // group_id。初始分组按 group_id 全量聚合会把 N 段合成一个播放单元
    // 一口气播完，把交错的响应/追伤全部挤到单元之后，观感违背因果：
    //
    //   事件流：段1(阿喀琉斯) → 响应 → 阿喀琉斯追伤 → 段2 → …
    //   旧播放：段1段2段3 → 响应 → 追伤 → …（借手连劈三刀再补账）
    //
    // 做法：借刀组按"组根直接子伤害"（parent_seq==RootSeq）切段，每段
    // 自成播放单元（Root 保留原触发事件以便解析借刀 profile；段内其余
    // 副事件按 seq 归属所在段），然后全列表按首事件 seq 稳定重排，
    // 使各段与其引出的响应/追伤组恢复事件流原生交错。
    //
    // 判定借刀经构造注入的谓词（Runner 用 PerformanceDatabase 查
    // BorrowBlade 字段），本层不依赖 VFX 配置类型。
    // =========================================================================

    public class BorrowBladeSplitProcessor : IEventProcessor
    {
        readonly Func<EventGroup, bool> _isBorrowBlade;

        public BorrowBladeSplitProcessor(Func<EventGroup, bool> isBorrowBlade)
        {
            _isBorrowBlade = isBorrowBlade;
        }

        public List<EventGroup> Process(List<EventGroup> groups)
        {
            var result = new List<EventGroup>(groups.Count);
            bool anySplit = false;
            foreach (var group in groups)
            {
                if (!ShouldSplit(group))
                {
                    result.Add(group);
                    continue;
                }
                result.AddRange(SplitBySegment(group));
                anySplit = true;
            }
            // 稳定重排（首事件 seq）：拆出的段落回事件流原生位置，
            // 与响应/追伤组恢复交错；未拆组本就按首现序=首事件 seq 序，不受影响
            return anySplit ? result.OrderBy(g => g.Events[0].Seq).ToList() : result;
        }

        bool ShouldSplit(EventGroup group)
        {
            if (group.Events.Count <= 2) return false;
            if (_isBorrowBlade == null || !_isBorrowBlade(group)) return false;
            return CountDirectDamages(group) >= 2;
        }

        static int CountDirectDamages(EventGroup group)
        {
            int count = 0;
            foreach (var ev in group.Events)
                if (ev is DamageEvent && ev.ParentSeq == group.RootSeq)
                    count++;
            return count;
        }

        /// <summary>按直接子伤害切段：每遇到一条新的直接伤害开新段；
        /// 段内其余事件（momentum 等）按 seq 顺序归属当前段。
        /// 段 1 含组根；段 2+ 的 Root 仍指向组根（供解析借刀 profile /
        /// 飘字战法名），但事件列表不含组根，避免重复落账。</summary>
        static List<EventGroup> SplitBySegment(EventGroup group)
        {
            var segments = new List<EventGroup>();
            var current = new EventGroup { Root = group.Root, Kind = group.Kind };
            bool currentHasDamage = false;
            foreach (var ev in group.Events)
            {
                bool isSegmentDamage = ev is DamageEvent && ev.ParentSeq == group.RootSeq;
                if (isSegmentDamage && currentHasDamage)
                {
                    segments.Add(current);
                    current = new EventGroup { Root = group.Root, Kind = group.Kind };
                    currentHasDamage = false;
                }
                current.Events.Add(ev);
                if (isSegmentDamage) currentHasDamage = true;
            }
            if (current.Events.Count > 0) segments.Add(current);
            return segments;
        }
    }
}
