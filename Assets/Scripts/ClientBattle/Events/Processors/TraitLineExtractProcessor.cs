using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 台词独占拆分：把混在行动/状态触发组里的 trait_trigger 抽成独立 TraitLine，
    // 同时保持出击段仍挂靠原组 Root（skill_trigger / status_tick），以便
    // VFXResolver 仍能命中 Melee 等专属配置。
    //
    // 例：十二试炼 [tick, attr, taunt, dmg格挡, taunt, dmg] →
    //   TraitLine → StatusTrigger(tick+attr+dmg) → TraitLine → StatusTrigger(dmg)
    // 格挡/反弹的 dmg 仍走近战突进，不因 amount=0 丢动画。
    // =========================================================================

    public class TraitLineExtractProcessor : IEventProcessor
    {
        public List<EventGroup> Process(List<EventGroup> groups)
        {
            var result = new List<EventGroup>(groups.Count);
            foreach (var group in groups)
            {
                // 单挑组内台词由 PlayDuel 按时点播，禁止抽成独立 TraitLine
                // （否则会先于交锋/拒绝横幅播出，或打乱 challenge/result 同组）
                if (group.Kind == GroupKind.TraitLine || group.Kind == GroupKind.Duel
                    || !ContainsTrait(group))
                {
                    result.Add(group);
                    continue;
                }

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
                    result.Add(new EventGroup
                    {
                        // 保留原 Root：KeyOf/ActorOf 仍指向试炼/战法 id，Melee 不丢
                        Root = group.Root,
                        Kind = group.Kind,
                        Events = events,
                    });
                }

                foreach (var ev in group.Events)
                {
                    if (ev is TraitTriggerEvent)
                    {
                        EmitStrike();
                        result.Add(new EventGroup
                        {
                            Kind = GroupKind.TraitLine,
                            Root = ev,
                            Events = new List<BattleEvent> { ev },
                        });
                        continue;
                    }

                    if (!seenDamage && ev is not DamageEvent)
                    {
                        header.Add(ev);
                        continue;
                    }

                    if (ev is DamageEvent)
                    {
                        EmitStrike(); // 上一段出击先落组（一段伤害一个近战节拍）
                        seenDamage = true;
                        pending.Add(ev);
                        continue;
                    }

                    seenDamage = true;
                    pending.Add(ev);
                }

                EmitStrike();

                // 仅有宣告/属性、尚无伤害：原样落一组（神谕类等）
                if (!headerConsumed && header.Count > 0)
                {
                    result.Add(new EventGroup
                    {
                        Root = group.Root,
                        Kind = group.Kind,
                        Events = header,
                    });
                }
            }
            return result;
        }

        static bool ContainsTrait(EventGroup group)
        {
            foreach (var ev in group.Events)
                if (ev is TraitTriggerEvent) return true;
            return false;
        }
    }
}
