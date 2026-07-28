using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 台词独占拆分：把混在行动/状态触发组里的 trait_trigger 抽成独立 TraitLine，
    // 同时保持出击段仍挂靠原组 Root（skill_trigger / status_tick），以便
    // VFXResolver 仍能命中 Melee 等专属配置。
    //
    // 切段粒度（2026-07-28 修正，原来是「一条伤害一段」）：
    //  - **齐射组**（群攻主动：互异目标≥2 且非近战；或战法标 simultaneous）：
    //    出击**整组一个播放单元**，台词按位置提到组前（出手前的宣言）或压到组后
    //    （受击方的反应台词）。原来按伤害切会把群攻切成 N 段——只要有人在挨打时
    //    说了句话（如阿喀琉斯「踵」受击台词落进赫克托尔战吼组），一次群攻就碎成
    //    四个单元逐个飞，这是「群攻＝一个播放单元」红线的破口。
    //  - **逐段组**（近战/单体多段/战法标 per_target）：只在**台词处**切段，
    //    段内连续伤害保持在同一单元（例：十二试炼 [tick, attr, 台词, dmg格挡,
    //    台词, dmg] → TraitLine → StatusTrigger(tick+attr+dmg) → TraitLine →
    //    StatusTrigger(dmg)）。格挡/反弹的 dmg 仍走近战突进，不因 amount=0 丢动画。
    // =========================================================================

    public class TraitLineExtractProcessor : IEventProcessor
    {
        public List<EventGroup> Process(List<EventGroup> groups)
        {
            var result = new List<EventGroup>(groups.Count);
            foreach (var group in groups)
            {
                // 单挑组内台词由 DuelPerformance 按时点播，禁止抽成独立 TraitLine
                // （否则会先于交锋/拒绝横幅播出，或打乱 challenge/result 同组）
                if (group.Kind == GroupKind.TraitLine || group.Kind == GroupKind.Duel
                    || !ContainsTrait(group))
                {
                    result.Add(group);
                    continue;
                }

                if (IsSimultaneous(group)) SplitAroundStrike(group, result);
                else SplitAtLines(group, result);
            }
            return result;
        }

        /// <summary>齐射组：台词提到组前/压到组后，出击段整组不切。</summary>
        static void SplitAroundStrike(EventGroup group, List<EventGroup> result)
        {
            var pre = new List<BattleEvent>();
            var post = new List<BattleEvent>();
            var strikeEvents = new List<BattleEvent>();
            bool seenDamage = false;
            foreach (var ev in group.Events)
            {
                if (ev is TraitTriggerEvent)
                {
                    // 首条伤害之前＝出手前的宣言；之后＝挨打方的反应，压到出击之后
                    (seenDamage ? post : pre).Add(ev);
                    continue;
                }
                if (ev is DamageEvent) seenDamage = true;
                strikeEvents.Add(ev);
            }
            foreach (var line in pre) result.Add(LineGroup(group, line));
            if (strikeEvents.Count > 0)
            {
                var strike = group.Fork();
                strike.Events = strikeEvents;
                result.Add(strike);
            }
            foreach (var line in post) result.Add(LineGroup(group, line));
        }

        /// <summary>逐段组：只在台词处切段，段内连续伤害不再拆。</summary>
        static void SplitAtLines(EventGroup group, List<EventGroup> result)
        {
            var header = new List<BattleEvent>();
            var pending = new List<BattleEvent>();
            bool seenDamage = false;
            bool headerConsumed = false;

            void EmitStrike()
            {
                if (pending.Count == 0) return;
                var events = new List<BattleEvent>();
                if (!headerConsumed)
                {
                    events.AddRange(header);
                    headerConsumed = true;
                }
                events.AddRange(pending);
                pending.Clear();
                // Fork 保留原 Root 与编译期注记：KeyOf/ActorOf 仍指向试炼/战法 id，
                // Melee 不丢；批次注记也随段带走
                var strike = group.Fork();
                strike.Events = events;
                result.Add(strike);
            }

            foreach (var ev in group.Events)
            {
                if (ev is TraitTriggerEvent)
                {
                    EmitStrike();
                    result.Add(LineGroup(group, ev));
                    continue;
                }
                if (!seenDamage && ev is not DamageEvent)
                {
                    header.Add(ev);
                    continue;
                }
                seenDamage = true;
                pending.Add(ev);
            }

            EmitStrike();

            // 仅有宣告/属性、尚无伤害：原样落一组（神谕类等）
            if (!headerConsumed && header.Count > 0)
            {
                var headerGroup = group.Fork();
                headerGroup.Events = header;
                result.Add(headerGroup);
            }
        }

        static EventGroup LineGroup(EventGroup group, BattleEvent line)
        {
            var g = group.Fork(line, GroupKind.TraitLine);
            g.Events = new List<BattleEvent> { line };
            return g;
        }

        /// <summary>本组是否「一拍齐射」——判据与 DefaultPerformance 选模板一致
        /// （群攻主动＝互异目标≥2 且非近战），外加战法定义期标签覆盖。</summary>
        static bool IsSimultaneous(EventGroup group)
        {
            if (group.ForcePerTarget) return false;
            var targets = new HashSet<string>();
            foreach (var ev in group.Events)
                if (ev is DamageEvent damage) targets.Add(damage.TargetId);
            if (group.ForceSimultaneous && targets.Count > 0) return true;
            bool melee = group.Kind == GroupKind.NormalAttack
                         || (group.Kind == GroupKind.Pursuit && targets.Count <= 1);
            return !melee && targets.Count >= 2;
        }

        static bool ContainsTrait(EventGroup group)
        {
            foreach (var ev in group.Events)
                if (ev is TraitTriggerEvent) return true;
            return false;
        }
    }
}
