using System.Collections.Generic;

namespace ClientBattle.Events
{
    // =========================================================================
    // 节点合并处理器：连续的纯节点组（phase_start / round_end / troops_change
    // 自然损耗等无演出量的组）标记为与下一组并行，避免播放器空转卡节奏。
    // round_start / game_end / battle_end / duel / defeat 保留独立演出。
    // =========================================================================

    public class NodeMergeProcessor : IEventProcessor
    {
        public List<EventGroup> Process(List<EventGroup> groups)
        {
            foreach (var group in groups)
            {
                if (group.Kind != GroupKind.Node && group.Kind != GroupKind.Other) continue;
                switch (group.Root)
                {
                    case RoundStartEvent:
                    case ActionStartEvent:
                        break; // 保留：回合横幅 / 行动指示
                    case MarkerEvent m when m.Type == "game_end" || m.Type == "battle_end" || m.Type == "game_start":
                        break; // 保留：结算横幅
                    default:
                        group.ParallelWithNext = true; // 其余节点静默并行（镜像即时生效）
                        break;
                }
            }
            return groups;
        }
    }
}
