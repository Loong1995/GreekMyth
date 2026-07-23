using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 阿喀琉斯贯穿标记：傲慢 25% 判定成功会先抽出 TraitLine(pierce)，
    // 再播 StatusTrigger(achilles_wrath 追伤)。本处理器把二者对齐打标，
    // 供 DefaultPerformance 仅在贯穿成功时播裂甲 ExtraIcon。
    // 须挂在 TraitLineExtractProcessor 之后。
    // =========================================================================

    public class AchillesPierceTagProcessor : IEventProcessor
    {
        public List<EventGroup> Process(List<EventGroup> groups)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g.Kind != GroupKind.StatusTrigger) continue;
                if (g.Root is not StatusTickEvent tick) continue;
                if (tick.Status?.StatusId != "achilles_wrath") continue;

                string owner = tick.Status.OwnerId ?? tick.SourceId;
                // 向前看最近几组：贯穿台词常紧挨在追伤组之前
                for (int j = i - 1; j >= 0 && j >= i - 4; j--)
                {
                    if (groups[j].Root is TraitTriggerEvent t
                        && t.Effect == "pierce"
                        && (string.IsNullOrEmpty(owner) || t.HeroId == owner))
                    {
                        g.PierceBoost = true;
                        break;
                    }
                    // 跨过别的状态触发则停止（避免误绑更早的贯穿）
                    if (groups[j].Kind == GroupKind.StatusTrigger) break;
                }
            }
            return groups;
        }
    }
}
