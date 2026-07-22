using System.Collections.Generic;
using ClientBattle.Names;

namespace ClientBattle.Events
{
    // =========================================================================
    // 战后技能统计：从事件流汇总「武将 → 技能 ×次数 / 杀伤 / 治疗」。
    // 客户端只读聚合，不重算伤害（数值一律取事件 amount）。
    // 归因：沿 parent_seq 上溯至 skill_trigger / normal_attack / status_tick。
    // 多局：每局一张表 + 可选系列合计。
    // =========================================================================

    public class SkillStatRow
    {
        public string SkillKey;
        public string DisplayName;
        public int Triggers;
        public int Damage;
        public int Heal;
    }

    public class HeroSkillStats
    {
        public string HeroId;
        public string TeamId;
        public int FinalTroops;
        public int MaxTroops;
        public readonly List<SkillStatRow> Skills = new();
    }

    /// <summary>单局（或系列合计）结算快照。</summary>
    public class GameSettlementSnapshot
    {
        public int GameNo;              // 0 = 系列合计
        public string Title;            // 「第 1 局」/「系列合计」
        public string WinnerTeamId;
        public string TeamAId = "A";
        public string TeamBId = "B";
        public readonly List<HeroSkillStats> TeamA = new();
        public readonly List<HeroSkillStats> TeamB = new();
    }

    public class BattleSettlementSnapshot
    {
        public string SeriesWinnerTeamId;
        public readonly List<GameSettlementSnapshot> Games = new();
    }

    public static class BattleSkillStatsAggregator
    {
        public static BattleSettlementSnapshot Build(BattleReport report)
        {
            var result = new BattleSettlementSnapshot
            {
                SeriesWinnerTeamId = report.SeriesWinnerTeamId ?? "",
            };
            string teamAId = report.Teams.Count > 0 ? report.Teams[0].TeamId : "A";
            string teamBId = report.Teams.Count > 1 ? report.Teams[1].TeamId : "B";

            // 系列合计桶（多局时附在末尾）
            var seriesHeroes = NewHeroMap(report);
            var seriesOrder = NewSkillOrder(report);
            var seriesRows = new Dictionary<(string, string), SkillStatRow>();

            foreach (var game in report.Games)
            {
                var heroes = NewHeroMap(report);
                var skillOrder = NewSkillOrder(report);
                var rows = new Dictionary<(string, string), SkillStatRow>();
                AggregateGame(game, heroes, skillOrder, rows);

                // 累加到系列
                MergeInto(seriesHeroes, seriesOrder, seriesRows, heroes, skillOrder, rows);

                var snap = FillSnapshot(
                    gameNo: game.GameNo,
                    title: $"第 {game.GameNo} 局",
                    winner: game.WinnerTeamId ?? "",
                    teamAId, teamBId, report, heroes, skillOrder, rows);
                result.Games.Add(snap);
            }

            if (report.Games.Count > 1)
            {
                // 系列终局兵力用 result.stats
                foreach (var s in report.HeroStats)
                {
                    if (seriesHeroes.TryGetValue(s.HeroId, out var hs))
                        hs.FinalTroops = s.FinalTroops;
                }
                result.Games.Add(FillSnapshot(
                    gameNo: 0,
                    title: "系列合计",
                    winner: result.SeriesWinnerTeamId,
                    teamAId, teamBId, report, seriesHeroes, seriesOrder, seriesRows));
            }
            else if (report.Games.Count == 1 && report.HeroStats.Count > 0)
            {
                foreach (var s in report.HeroStats)
                {
                    if (result.Games[0].TeamA.Exists(h => h.HeroId == s.HeroId) ||
                        result.Games[0].TeamB.Exists(h => h.HeroId == s.HeroId))
                    {
                        foreach (var h in result.Games[0].TeamA)
                            if (h.HeroId == s.HeroId) h.FinalTroops = s.FinalTroops;
                        foreach (var h in result.Games[0].TeamB)
                            if (h.HeroId == s.HeroId) h.FinalTroops = s.FinalTroops;
                    }
                }
            }

            return result;
        }

        static Dictionary<string, HeroSkillStats> NewHeroMap(BattleReport report)
        {
            var heroes = new Dictionary<string, HeroSkillStats>();
            foreach (var team in report.Teams)
            {
                foreach (var h in team.Heroes)
                {
                    heroes[h.HeroId] = new HeroSkillStats
                    {
                        HeroId = h.HeroId,
                        TeamId = team.TeamId,
                        MaxTroops = h.MaxTroops,
                        FinalTroops = h.InitialTroops,
                    };
                }
            }
            return heroes;
        }

        static Dictionary<string, List<string>> NewSkillOrder(BattleReport report)
        {
            var order = new Dictionary<string, List<string>>();
            foreach (var team in report.Teams)
                foreach (var h in team.Heroes)
                    order[h.HeroId] = new List<string>();
            return order;
        }

        static void AggregateGame(
            GameRecord game,
            Dictionary<string, HeroSkillStats> heroes,
            Dictionary<string, List<string>> skillOrder,
            Dictionary<(string, string), SkillStatRow> rows)
        {
            var bySeq = new Dictionary<int, BattleEvent>();
            foreach (var ev in game.Events)
                bySeq[ev.Seq] = ev;

            foreach (var ev in game.Events)
            {
                TrackTroops(ev, heroes);
                switch (ev)
                {
                    case SkillTriggerEvent st when IsTriggerKind(st.Kind):
                        BumpTrigger(st.ActorId, st.SkillId, skillOrder, rows);
                        break;
                    case NormalAttackEvent na:
                        string key = na.Kind == "coordinated" ? "coordinated" : "basic_attack";
                        BumpTrigger(na.ActorId, key, skillOrder, rows);
                        break;
                    case StatusTickEvent tick when tick.Status != null:
                    {
                        string owner = !string.IsNullOrEmpty(tick.SourceId)
                            ? tick.SourceId : tick.Status.OwnerId;
                        BumpTrigger(owner, MapStatusToSkill(tick.Status.StatusId),
                            skillOrder, rows);
                        break;
                    }
                    case DamageEvent d when d.Amount > 0 && string.IsNullOrEmpty(d.Mitigation):
                    {
                        var (heroId, skillKey) = ResolveAttribution(d, d.SourceId, bySeq);
                        AddAmount(heroId, skillKey, d.Amount, heal: false,
                            heroes, skillOrder, rows);
                        break;
                    }
                    case HealEvent h when h.Amount > 0:
                    {
                        var (heroId, skillKey) = ResolveAttribution(h, h.SourceId, bySeq);
                        AddAmount(heroId, skillKey, h.Amount, heal: true,
                            heroes, skillOrder, rows);
                        break;
                    }
                }
            }
        }

        static void MergeInto(
            Dictionary<string, HeroSkillStats> dstHeroes,
            Dictionary<string, List<string>> dstOrder,
            Dictionary<(string, string), SkillStatRow> dstRows,
            Dictionary<string, HeroSkillStats> srcHeroes,
            Dictionary<string, List<string>> srcOrder,
            Dictionary<(string, string), SkillStatRow> srcRows)
        {
            foreach (var kv in srcHeroes)
            {
                if (dstHeroes.TryGetValue(kv.Key, out var dh))
                    dh.FinalTroops = kv.Value.FinalTroops;
            }
            foreach (var sk in srcOrder)
            {
                foreach (var skillKey in sk.Value)
                {
                    if (!srcRows.TryGetValue((sk.Key, skillKey), out var src)) continue;
                    var dst = EnsureRow(sk.Key, skillKey, dstOrder, dstRows);
                    dst.Triggers += src.Triggers;
                    dst.Damage += src.Damage;
                    dst.Heal += src.Heal;
                }
            }
        }

        static GameSettlementSnapshot FillSnapshot(
            int gameNo, string title, string winner,
            string teamAId, string teamBId, BattleReport report,
            Dictionary<string, HeroSkillStats> heroes,
            Dictionary<string, List<string>> skillOrder,
            Dictionary<(string, string), SkillStatRow> rows)
        {
            var snap = new GameSettlementSnapshot
            {
                GameNo = gameNo,
                Title = title,
                WinnerTeamId = winner ?? "",
                TeamAId = teamAId,
                TeamBId = teamBId,
            };
            foreach (var team in report.Teams)
            {
                var list = team.TeamId == teamAId ? snap.TeamA : snap.TeamB;
                foreach (var h in team.Heroes)
                {
                    if (!heroes.TryGetValue(h.HeroId, out var hs)) continue;
                    // 拷贝一份，避免系列合计与单局共享同一 SkillStatRow 引用被二次累加污染
                    var copy = new HeroSkillStats
                    {
                        HeroId = hs.HeroId,
                        TeamId = hs.TeamId,
                        MaxTroops = hs.MaxTroops,
                        FinalTroops = hs.FinalTroops,
                    };
                    foreach (var sk in skillOrder[h.HeroId])
                    {
                        if (!rows.TryGetValue((h.HeroId, sk), out var row)) continue;
                        copy.Skills.Add(new SkillStatRow
                        {
                            SkillKey = row.SkillKey,
                            DisplayName = row.DisplayName,
                            Triggers = row.Triggers,
                            Damage = row.Damage,
                            Heal = row.Heal,
                        });
                    }
                    list.Add(copy);
                }
            }
            return snap;
        }

        static bool IsTriggerKind(string kind) =>
            kind is "cast" or "release" or "prepare";

        static void TrackTroops(BattleEvent ev, Dictionary<string, HeroSkillStats> heroes)
        {
            switch (ev)
            {
                case DamageEvent d when d.Troops != null:
                    SetTroops(heroes, d.Troops.HeroId, d.Troops.TroopsAfter); break;
                case HealEvent h when h.Troops != null:
                    SetTroops(heroes, h.Troops.HeroId, h.Troops.TroopsAfter); break;
                case TroopsChangeEvent t when t.Troops != null:
                    SetTroops(heroes, t.Troops.HeroId, t.Troops.TroopsAfter); break;
                case HeroDefeatedEvent def:
                    SetTroops(heroes, def.HeroId, 0); break;
            }
        }

        static void SetTroops(Dictionary<string, HeroSkillStats> heroes, string heroId, int troops)
        {
            if (string.IsNullOrEmpty(heroId)) return;
            if (heroes.TryGetValue(heroId, out var hs))
                hs.FinalTroops = troops;
        }

        static void BumpTrigger(
            string heroId, string skillKey,
            Dictionary<string, List<string>> skillOrder,
            Dictionary<(string, string), SkillStatRow> rows)
        {
            if (string.IsNullOrEmpty(heroId) || string.IsNullOrEmpty(skillKey)) return;
            if (!skillOrder.ContainsKey(heroId)) return;
            var row = EnsureRow(heroId, skillKey, skillOrder, rows);
            row.Triggers++;
        }

        static void AddAmount(
            string heroId, string skillKey, int amount, bool heal,
            Dictionary<string, HeroSkillStats> heroes,
            Dictionary<string, List<string>> skillOrder,
            Dictionary<(string, string), SkillStatRow> rows)
        {
            if (string.IsNullOrEmpty(heroId) || amount <= 0) return;
            if (!heroes.ContainsKey(heroId)) return;
            if (string.IsNullOrEmpty(skillKey)) skillKey = "unknown";
            var row = EnsureRow(heroId, skillKey, skillOrder, rows);
            if (heal) row.Heal += amount;
            else row.Damage += amount;
        }

        static SkillStatRow EnsureRow(
            string heroId, string skillKey,
            Dictionary<string, List<string>> skillOrder,
            Dictionary<(string, string), SkillStatRow> rows)
        {
            var key = (heroId, skillKey);
            if (rows.TryGetValue(key, out var row)) return row;
            row = new SkillStatRow
            {
                SkillKey = skillKey,
                DisplayName = DisplayOf(skillKey),
            };
            rows[key] = row;
            skillOrder[heroId].Add(skillKey);
            return row;
        }

        static string DisplayOf(string skillKey)
        {
            string skill = ChineseNames.Skill(skillKey);
            if (skill != skillKey) return skill;
            return ChineseNames.Status(skillKey);
        }

        /// <summary>
        /// 状态触发结算归因：带技能（神谕/被动挂的状态）归到施法者（source_id）的
        /// 带技能格子，而非实际出手单位。例：阿喀琉斯触发宙斯雷霆 → 宙斯·雷霆神谕。
        /// </summary>
        static (string heroId, string skillKey) ResolveAttribution(
            BattleEvent ev, string fallbackHero, Dictionary<int, BattleEvent> bySeq)
        {
            int guard = 0;
            int seq = ev.ParentSeq;
            while (seq > 0 && guard++ < 48)
            {
                if (!bySeq.TryGetValue(seq, out var parent)) break;
                switch (parent)
                {
                    case SkillTriggerEvent st:
                        return (st.ActorId, st.SkillId);
                    case NormalAttackEvent na:
                        return (na.ActorId,
                            na.Kind == "coordinated" ? "coordinated" : "basic_attack");
                    case StatusTickEvent tick when tick.Status != null:
                    {
                        string owner = !string.IsNullOrEmpty(tick.SourceId)
                            ? tick.SourceId : tick.Status.OwnerId;
                        return (owner, MapStatusToSkill(tick.Status.StatusId));
                    }
                    default:
                        seq = parent.ParentSeq;
                        break;
                }
            }
            if (bySeq.TryGetValue(ev.GroupId, out var root))
            {
                switch (root)
                {
                    case SkillTriggerEvent st:
                        return (st.ActorId, st.SkillId);
                    case NormalAttackEvent na:
                        return (na.ActorId,
                            na.Kind == "coordinated" ? "coordinated" : "basic_attack");
                    case StatusTickEvent tick when tick.Status != null:
                    {
                        string owner = !string.IsNullOrEmpty(tick.SourceId)
                            ? tick.SourceId : tick.Status.OwnerId;
                        return (owner, MapStatusToSkill(tick.Status.StatusId));
                    }
                }
            }
            return (fallbackHero, "unknown");
        }

        /// <summary>状态 id → 带出该状态的战法 id（结算表格子键）。
        /// 数据收口 StatusPresentationRegistry（skill 同名状态直通零配置）。</summary>
        static string MapStatusToSkill(string statusId)
            => Names.StatusPresentationRegistry.StatsSkillOf(statusId);

        static string ResolveSkillKey(BattleEvent ev, Dictionary<int, BattleEvent> bySeq)
        {
            return ResolveAttribution(ev, "", bySeq).skillKey;
        }
    }
}
