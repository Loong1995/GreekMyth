using System.Collections.Generic;
using ClientBattle.Events;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】HighlightSelector：终局高光窗选取（纯函数，零表现依赖）。
    //
    // 遍历指定队伍每武将的全部行动窗（action_start 分界），按单窗观感分取最大窗：
    // 观感分 = 伤害量 + 满势能 cut_in 次数 × 奖励（避免「伤害略高但无满势能切入」
    // 的窗口抢走真正有 cut-in 的高光窗——manual 阿喀琉斯常见）。
    // 原嵌在 PerformanceRunner.PlayHighlight，2026-07-22 拆出。
    // =========================================================================

    public struct HighlightWindow
    {
        public int GameIndex;
        public int StartSeq;
        public int EndSeq;
        public string ActorId;
        public int Damage;
    }

    public static class HighlightSelector
    {
        // 单次满势能 cut_in 约等于一次高伤门槛的观感权重，使带切入的窗优先
        const int CutInScoreBonus = 3000;

        /// <summary>选取观感分最高的我方行动窗；无有效窗返回 false。</summary>
        public static bool TryFindBestWindow(BattleReport report, string teamId, out HighlightWindow best)
        {
            best = default;
            var ourHeroes = new HashSet<string>();
            foreach (var team in report.Teams)
                if (team.TeamId == teamId)
                    foreach (var hero in team.Heroes) ourHeroes.Add(hero.HeroId);

            int bestScore = 0;
            var result = new HighlightWindow { GameIndex = -1 };
            for (int gi = 0; gi < report.Games.Count; gi++)
            {
                var events = report.Games[gi].Events;
                string actor = null; int start = 0, damage = 0, cutIns = 0;
                void CloseWindow(int endSeq)
                {
                    if (actor == null) return;
                    int score = damage + cutIns * CutInScoreBonus;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        result = new HighlightWindow
                        {
                            GameIndex = gi, StartSeq = start, EndSeq = endSeq,
                            ActorId = actor, Damage = damage,
                        };
                    }
                }
                foreach (var ev in events)
                {
                    if (ev is ActionStartEvent action)
                    {
                        CloseWindow(ev.Seq);
                        actor = ourHeroes.Contains(action.ActorId) ? action.ActorId : null;
                        start = ev.Seq;
                        damage = 0;
                        cutIns = 0;
                    }
                    else if (actor == null) continue;
                    else if (ev is DamageEvent d
                             && string.IsNullOrEmpty(d.Mitigation) && ourHeroes.Contains(d.SourceId))
                        damage += d.Amount;
                    else if (ev is MomentumChangeEvent { CutIn: true } m
                             && ourHeroes.Contains(m.HeroId))
                        cutIns++;
                }
                CloseWindow(int.MaxValue);
            }
            if (result.GameIndex < 0) return false;
            best = result;
            return true;
        }
    }
}
