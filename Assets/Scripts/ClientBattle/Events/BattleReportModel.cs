using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClientBattle.Events
{
    // =========================================================================
    // 战报顶层模型：schema/core 版本、双方阵容快照、逐局事件（已解析为 BattleEvent）。
    // 只读镜像，不做任何客户端结算。schema 主版本 != 1 时拒绝加载。
    // =========================================================================

    /// <summary>战法标签条目（schema 1.5.0 `skill_catalog`）。服务端定义处声明、
    /// 客户端播放编译层**直读**，禁止再逐事件推断「是不是追击/魔法」。
    /// 未知 tags 必须忽略（契约加法演进义务）。</summary>
    public class SkillCatalogEntry
    {
        public string Name = "";
        /// <summary>basic / active / prepare_active / passive / pursuit / oracle。</summary>
        public string Category = "";
        /// <summary>physical / magic / mixed / none。</summary>
        public string DamageType = "none";
        public string Timing = "";
        public bool IsOracle;
        public int PrepareRounds;
        public List<string> Tags = new();
    }

    /// <summary>状态播放标签条目（schema 1.5.2 `status_catalog`）。服务端在
    /// `StatusDef.playback_tags` 定义处声明，客户端编译层直读，决定该状态的触发
    /// 能否与同批次的其它触发并成一个播放单元。未知 tags 必须忽略。</summary>
    public class StatusCatalogEntry
    {
        public string Name = "";
        /// <summary>simultaneous＝跨持有者可齐发并组；sequential＝必须逐次成单元。</summary>
        public List<string> Tags = new();

        public bool Has(string tag) => Tags.Contains(tag);
    }

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
        /// <summary>出场战法标签目录（1.5.0 可选；旧战报为空表，编译层回退启发式并告警）。</summary>
        public Dictionary<string, SkillCatalogEntry> SkillCatalog = new();

        /// <summary>带播放标签的状态目录（1.5.2 可选；旧战报为空表，
        /// 编译层回落客户端 StatusPresentationRegistry 的集体标记）。</summary>
        public Dictionary<string, StatusCatalogEntry> StatusCatalog = new();

        /// <summary>状态是否带某播放标签；旧战报（无目录）一律 false，
        /// 调用方自行回落。</summary>
        public bool StatusHasTag(string statusId, string tag) =>
            !string.IsNullOrEmpty(statusId)
            && StatusCatalog.TryGetValue(statusId, out var e) && e.Has(tag);

        /// <summary>目录查询（无条目返回 null；调用方必须容忍旧战报缺目录）。</summary>
        public SkillCatalogEntry CatalogOf(string skillId) =>
            !string.IsNullOrEmpty(skillId) && SkillCatalog.TryGetValue(skillId, out var e)
                ? e : null;

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

            var catalogJson = root.Value<JObject>("skill_catalog");
            if (catalogJson != null)
                foreach (var prop in catalogJson.Properties())
                {
                    var e = (JObject)prop.Value;
                    var entry = new SkillCatalogEntry
                    {
                        Name = e.Value<string>("name") ?? "",
                        Category = e.Value<string>("category") ?? "",
                        DamageType = e.Value<string>("damage_type") ?? "none",
                        Timing = e.Value<string>("timing") ?? "",
                        IsOracle = e.Value<bool?>("is_oracle") ?? false,
                        PrepareRounds = e.Value<int?>("prepare_rounds") ?? 0,
                    };
                    var tags = e.Value<JArray>("tags");
                    if (tags != null)
                        foreach (var t in tags) entry.Tags.Add((string)t);
                    report.SkillCatalog[prop.Name] = entry;
                }

            var statusCatalogJson = root.Value<JObject>("status_catalog");
            if (statusCatalogJson != null)
                foreach (var prop in statusCatalogJson.Properties())
                {
                    var e = (JObject)prop.Value;
                    var entry = new StatusCatalogEntry { Name = e.Value<string>("name") ?? "" };
                    var tags = e.Value<JArray>("tags");
                    if (tags != null)
                        foreach (var t in tags) entry.Tags.Add((string)t);
                    report.StatusCatalog[prop.Name] = entry;
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
