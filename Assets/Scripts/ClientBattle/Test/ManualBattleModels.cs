using System.Collections.Generic;
using Newtonsoft.Json;

namespace ClientBattle.Test
{
    // =========================================================================
    // 手动配阵页数据模型：武将/战法目录（python bridge --catalog 导出）、
    // 配阵 config、百场统计结果。字段与 battle/tools/client_battle_bridge.py 对齐。
    // =========================================================================

    public class ManualCatalog
    {
        [JsonProperty("level")] public int Level;
        [JsonProperty("max_extra_skills")] public int MaxExtraSkills = 2;
        [JsonProperty("heroes")] public List<CatalogHero> Heroes = new();
        [JsonProperty("skill_pool")] public List<string> SkillPool = new();
        [JsonProperty("skills")] public Dictionary<string, CatalogSkill> Skills = new();

        public static ManualCatalog Parse(string json)
            => JsonConvert.DeserializeObject<ManualCatalog>(json);

        public CatalogHero HeroOf(string templateId)
            => Heroes.Find(h => h.TemplateId == templateId);

        public CatalogSkill SkillOf(string skillId)
            => skillId != null && Skills.TryGetValue(skillId, out var s) ? s : null;
    }

    public class CatalogHero
    {
        [JsonProperty("template_id")] public string TemplateId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("faction")] public string Faction;
        [JsonProperty("faction_name")] public string FactionName;
        [JsonProperty("gender")] public string Gender;
        [JsonProperty("trait_id")] public string TraitId;
        [JsonProperty("force")] public int Force;
        [JsonProperty("intelligence")] public int Intelligence;
        [JsonProperty("command")] public int Command;
        [JsonProperty("speed")] public int Speed;
        [JsonProperty("innate_skill")] public string InnateSkill;
        [JsonProperty("hidden_skills")] public List<string> HiddenSkills = new();
    }

    public class CatalogSkill
    {
        [JsonProperty("skill_id")] public string SkillId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("timing")] public string Timing;
        [JsonProperty("trigger_rate_bps")] public int TriggerRateBps;
        [JsonProperty("prepare_rounds")] public int PrepareRounds;
        [JsonProperty("is_oracle")] public bool IsOracle;

        public string RateText => TriggerRateBps >= 10000 ? "必发" : $"{TriggerRateBps / 100f:0.#}%";
    }

    /// <summary>一个武将上阵位：模板 + 两个可配战法格（null=空）。</summary>
    public class ManualSlot
    {
        public string TemplateId;                     // null = 空位
        public string[] ExtraSkills = new string[2];  // 每格 null = 空

        public bool IsEmpty => string.IsNullOrEmpty(TemplateId);

        public void Clear()
        {
            TemplateId = null;
            ExtraSkills[0] = ExtraSkills[1] = null;
        }
    }

    // ------------------------------------------------------------ 统计结果

    public class ManualStats
    {
        [JsonProperty("n")] public int N;
        [JsonProperty("avg_end_round")] public double AvgEndRound;
        [JsonProperty("elapsed_sec")] public double ElapsedSec;
        [JsonProperty("win_rate")] public Dictionary<string, WinRateEntry> WinRate = new();
        [JsonProperty("teams")] public Dictionary<string, TeamStatEntry> Teams = new();
        [JsonProperty("heroes")] public List<HeroStatEntry> Heroes = new();

        public static ManualStats Parse(string json)
            => JsonConvert.DeserializeObject<ManualStats>(json);
    }

    public class WinRateEntry
    {
        [JsonProperty("wins")] public int Wins;
        [JsonProperty("rate_pct")] public double RatePct;
    }

    public class TeamStatEntry
    {
        [JsonProperty("avg_dead")] public double AvgDead;
        [JsonProperty("avg_wounded")] public double AvgWounded;
        [JsonProperty("avg_remain")] public double AvgRemain;
    }

    public class HeroStatEntry
    {
        [JsonProperty("hero_id")] public string HeroId;
        [JsonProperty("team")] public string Team;
        [JsonProperty("rows")] public List<SkillStatEntry> Rows = new();
    }

    public class SkillStatEntry
    {
        [JsonProperty("skill_id")] public string SkillId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("avg_triggers")] public double AvgTriggers;
        [JsonProperty("avg_damage")] public double AvgDamage;
    }
}
