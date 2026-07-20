using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClientBattle.Events
{
    // =========================================================================
    // 战报顶层模型：schema/core 版本、双方阵容快照、逐局事件（已解析为 BattleEvent）。
    // 只读镜像，不做任何客户端结算。schema 主版本 != 1 时拒绝加载。
    // =========================================================================

    public class HeroSnapshot
    {
        public string HeroId, TemplateId;
        public int Position;
        public int Force, Intelligence, Command, Speed;
        public int MaxTroops, InitialTroops;
        public List<string> Skills = new();
        // 1.3.0 可选字段（旧战报缺省）
        public string TraitId = "", Gender = "m";
        public int Level = 50;
    }

    public class TeamSnapshot
    {
        public string TeamId, MainHeroId;
        public List<HeroSnapshot> Heroes = new();
    }

    public class GameRecord
    {
        public int GameNo;
        public string WinnerTeamId, Reason;
        public int EndRound;
        public List<BattleEvent> Events = new();
    }

    public class HeroSeriesStats
    {
        public string HeroId;
        public int TotalDamage, TotalHeal, Kills, FinalTroops;
    }

    public class BattleReport
    {
        public string SchemaVersion, CoreVersion, BattleId;
        public long RngSeed;
        public List<TeamSnapshot> Teams = new();
        public List<GameRecord> Games = new();
        public string SeriesWinnerTeamId, SeriesReason;
        public int TotalGames;
        public List<HeroSeriesStats> HeroStats = new();

        public IEnumerable<HeroSnapshot> AllHeroes()
        {
            foreach (var team in Teams)
                foreach (var hero in team.Heroes)
                    yield return hero;
        }

        public TeamSnapshot TeamOf(string heroId)
        {
            foreach (var team in Teams)
                foreach (var hero in team.Heroes)
                    if (hero.HeroId == heroId) return team;
            return null;
        }

        // ---------------------------------------------------------- 解析入口

        public static BattleReport Parse(string json)
        {
            var root = JObject.Parse(json);
            var report = new BattleReport
            {
                SchemaVersion = root.Value<string>("schema_version"),
                CoreVersion = root.Value<string>("core_version"),
                BattleId = root.Value<string>("battle_id"),
                RngSeed = root.Value<long>("rng_seed"),
            };
            if (string.IsNullOrEmpty(report.SchemaVersion) || !report.SchemaVersion.StartsWith("1."))
            {
                Debug.LogError($"[ClientBattle] 不支持的 schema_version '{report.SchemaVersion}'（仅支持 1.x）");
                return null;
            }

            foreach (var teamJson in root.Value<JArray>("teams"))
            {
                var team = new TeamSnapshot
                {
                    TeamId = teamJson.Value<string>("team_id"),
                    MainHeroId = teamJson.Value<string>("main_hero_id"),
                };
                foreach (var h in teamJson.Value<JArray>("heroes"))
                {
                    var hero = new HeroSnapshot
                    {
                        HeroId = h.Value<string>("hero_id"),
                        TemplateId = h.Value<string>("template_id"),
                        Position = h.Value<int>("position"),
                        Force = h.Value<int>("force"),
                        Intelligence = h.Value<int>("intelligence"),
                        Command = h.Value<int>("command"),
                        Speed = h.Value<int>("speed"),
                        MaxTroops = h.Value<int>("max_troops"),
                        InitialTroops = h.Value<int>("initial_troops"),
                        TraitId = h.Value<string>("trait_id") ?? "",
                        Gender = h.Value<string>("gender") ?? "m",
                        Level = ((JObject)h).ContainsKey("level") ? h.Value<int>("level") : 50,
                    };
                    foreach (var s in h.Value<JArray>("skills")) hero.Skills.Add((string)s);
                    team.Heroes.Add(hero);
                }
                report.Teams.Add(team);
            }

            foreach (var gameJson in root.Value<JArray>("games"))
            {
                var result = gameJson.Value<JObject>("result");
                var game = new GameRecord
                {
                    GameNo = gameJson.Value<int>("game_no"),
                    WinnerTeamId = result?.Value<string>("winner_team_id"),
                    Reason = result?.Value<string>("reason"),
                    EndRound = result?.Value<int>("end_round") ?? 0,
                    Events = BattleEventParser.Parse(gameJson.Value<JArray>("events")),
                };
                report.Games.Add(game);
            }

            var seriesResult = root.Value<JObject>("result");
            if (seriesResult != null)
            {
                report.SeriesWinnerTeamId = seriesResult.Value<string>("winner_team_id");
                report.SeriesReason = seriesResult.Value<string>("reason");
                report.TotalGames = seriesResult.Value<int>("total_games");
                var statsArr = seriesResult.Value<JArray>("stats");
                if (statsArr != null)
                {
                    foreach (var s in statsArr)
                    {
                        report.HeroStats.Add(new HeroSeriesStats
                        {
                            HeroId = s.Value<string>("hero_id"),
                            TotalDamage = s.Value<int?>("total_damage") ?? 0,
                            TotalHeal = s.Value<int?>("total_heal") ?? 0,
                            Kills = s.Value<int?>("kills") ?? 0,
                            FinalTroops = s.Value<int?>("final_troops") ?? 0,
                        });
                    }
                }
            }
            return report;
        }
    }
}
