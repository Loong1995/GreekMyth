using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 【第2层 事件流处理管线】原始事件序列 → EventGroup 列表（播放单元）
    //
    // - EventGroup：一组"作为一个整体演出"的事件（主事件 + 副事件），
    //   如群攻主动的 skill_trigger + N 条 damage 合并为一组。
    // - IEventProcessor：管线节点接口；管线按注册顺序链式处理，
    //   后续可注册自定义分析器（重排序 / 特殊拆分等），预留扩展点。
    // =========================================================================

    /// <summary>播放单元语义类别（供表演解析选择组默认策略）。</summary>
    public enum GroupKind
    {
        Node,           // 回合/相位/局节点（横幅类，非战斗动作）
        ActiveSkill,    // 主动战法（cast/release/assist）
        NormalAttack,   // 普攻
        Pursuit,        // 追击战法
        Passive,        // 被动/神谕宣告（prepare 期挂状态）
        StatusChange,   // 独立的状态施加/移除/属性修改
        StatusTrigger,  // 特殊状态触发（雷霆/圣盾/试炼等，由状态打出的结算）
        TraitLine,      // 性格台词（当场弹聊天框）
        Duel,           // 单挑
        Defeat,         // 阵亡
        Other,
    }

    /// <summary>一个播放单元：主事件 + 需要一起演出的副事件。</summary>
    public class EventGroup
    {
        public GroupKind Kind = GroupKind.Other;
        public BattleEvent Root;
        public List<BattleEvent> Events = new();  // 含 Root，按 seq 序
        /// <summary>true = 可与下一组并行播放（如同帧状态图标增删）；默认串行。</summary>
        public bool ParallelWithNext;
        /// <summary>阿喀琉斯傲慢贯穿（25%）已成功：StatusTrigger 组播裂甲 ExtraIcon。</summary>
        public bool PierceBoost;

        public int RootSeq => Root?.Seq ?? (Events.Count > 0 ? Events[0].Seq : 0);

        /// <summary>取组内第一个指定类型的事件（不含则 null）。</summary>
        public T First<T>() where T : BattleEvent
        {
            foreach (var e in Events)
                if (e is T hit) return hit;
            return null;
        }

        /// <summary>取组内全部指定类型事件。</summary>
        public List<T> All<T>() where T : BattleEvent
        {
            var list = new List<T>();
            foreach (var e in Events)
                if (e is T hit) list.Add(hit);
            return list;
        }
    }

    /// <summary>管线节点：输入组列表，输出改写后的组列表（可拆分/合并/重排）。</summary>
    public interface IEventProcessor
    {
        List<EventGroup> Process(List<EventGroup> groups);
    }

    /// <summary>
    /// 事件流处理管线：Run(rawEvents) = 初始分组 → 依次过 processor 链。
    /// 初始分组按 group_id 聚合（后端契约：组根 group_id=自身 seq，子事件继承），
    /// 保证一个因果链先落在同一组里，再由 processor 做表演级拆分。
    /// </summary>
    public class EventPipeline
    {
        readonly List<IEventProcessor> _processors = new();

        public EventPipeline Register(IEventProcessor processor)
        {
            _processors.Add(processor);
            return this;
        }

        public List<EventGroup> Run(List<BattleEvent> rawEvents)
        {
            var groups = GroupByGroupId(rawEvents);
            foreach (var processor in _processors)
                groups = processor.Process(groups);
            return groups;
        }

        static List<EventGroup> GroupByGroupId(List<BattleEvent> events)
        {
            // 按 group_id 全量聚合（组序=首次出现序），不能只合并连续段：
            // 群攻主动的 N 条伤害之间会被状态触发（雷霆 tick 等，new_group）插队，
            // 连续段合并会把一次群攻切成 N 个碎片，违反"群攻=一个播放单元"。
            var groups = new List<EventGroup>();
            var byId = new Dictionary<int, EventGroup>();
            foreach (var ev in events)
            {
                if (!byId.TryGetValue(ev.GroupId, out var group))
                {
                    group = new EventGroup { Root = ev };
                    byId[ev.GroupId] = group;
                    groups.Add(group);
                }
                group.Events.Add(ev); // 原始流本身 seq 有序，组内即 seq 序
            }
            foreach (var group in groups)
                group.Kind = Classify(group);
            return groups;
        }

        /// <summary>按组根事件推断播放语义类别。</summary>
        public static GroupKind Classify(EventGroup group)
        {
            switch (group.Root)
            {
                case NormalAttackEvent: return GroupKind.NormalAttack;
                case SkillTriggerEvent st:
                    if (st.Kind == "prepare" || st.Kind == "interrupted" || st.Kind == "delayed")
                        return GroupKind.Passive;
                    // 连发（1.4.0）：parent 指回首发触发事件但语义仍是主动释放，
                    // 必须与首发同模板演出（burst_no 是与追击的唯一判别字段）
                    if (st.BurstNo >= 2) return GroupKind.ActiveSkill;
                    // 追击：组根 skill_trigger 的 parent 指回普攻 damage（契约 §3.2）
                    return st.ParentSeq != 0 ? GroupKind.Pursuit : GroupKind.ActiveSkill;
                case StatusTickEvent: return GroupKind.StatusTrigger;
                case StatusApplyEvent:
                case StatusRemoveEvent:
                case AttrChangeEvent: return GroupKind.StatusChange;
                case TraitTriggerEvent: return GroupKind.TraitLine;
                case DuelChallengeEvent:
                case DuelResultEvent: return GroupKind.Duel;
                case HeroDefeatedEvent: return GroupKind.Defeat;
                case RoundStartEvent:
                case ActionStartEvent:
                case MarkerEvent: return GroupKind.Node;
                case MomentumChangeEvent: return GroupKind.Node; // 1.4.0 势能记账：B 批接 UI 前静默落账
                case TacticAppliedEvent: return GroupKind.Node;  // 1.4.1 战术变更：非阻塞横幅
                case DamageEvent:
                case HealEvent: return GroupKind.StatusTrigger; // 独立组根的伤害/治疗多为状态结算（DoT 等）
                default: return GroupKind.Other;
            }
        }
    }
}
