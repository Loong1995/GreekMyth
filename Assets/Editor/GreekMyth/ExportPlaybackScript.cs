using System.IO;
using ClientBattle.Events;
using ClientBattle.Units;
using ClientBattle.VFX;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GreekMythEditor
{
    /// <summary>把战报编译成**纯播放流**并导出 `.playback.json` 离线审阅
    /// （docs/client/playback_script.md）。与运行期完全同源：走
    /// `PlaybackCompiler.Compile`（同一 processor 链、同一 cut-in 判定），
    /// 所见即所播——排「为什么这组这么演」不再需要进 Play 模式打断点。
    ///
    /// 另附 <see cref="PlaybackDurationEstimator"/> 每回合用时启发式（容差 ±5s）。</summary>
    public static class ExportPlaybackScript
    {
        [MenuItem("GreekMyth/播放/导出 PlaybackScript（战报→.playback.json）")]
        public static void Export()
        {
            string src = EditorUtility.OpenFilePanel(
                "选择战报 JSON", Application.streamingAssetsPath + "/battle_reports", "json");
            if (string.IsNullOrEmpty(src)) return;
            ExportPath(src);
        }

        [MenuItem("GreekMyth/播放/估算回合用时（战报→控制台）")]
        public static void EstimateOnly()
        {
            string src = EditorUtility.OpenFilePanel(
                "选择战报 JSON", Application.streamingAssetsPath + "/battle_reports", "json");
            if (string.IsNullOrEmpty(src)) return;
            var compiled = CompileOrNull(src);
            if (compiled == null) return;
            var opt = TimingOptions();
            Debug.Log(PlaybackDurationModel.FormatSummary(
                PlaybackDurationModel.Rounds(compiled, NewResolver(), opt), opt));
        }

        public static void ExportPath(string src)
        {
            var compiled = CompileOrNull(src);
            if (compiled == null) return;

            string dest = Path.ChangeExtension(src, null) + ".playback.json";
            File.WriteAllText(dest, ToJson(compiled).ToString(Newtonsoft.Json.Formatting.Indented));
            var opt = TimingOptions();
            Debug.Log($"[PlaybackScript] 已导出 {dest}（{compiled.GameGroups.Count} 局）\n"
                      + PlaybackDurationModel.FormatSummary(
                          PlaybackDurationModel.Rounds(compiled, NewResolver(), opt), opt));
            EditorUtility.RevealInFinder(dest);
        }

        /// <summary>节奏参数：场上有 PerformanceRunner 就读它的实时值（所见即所播），
        /// 否则用 Inspector 默认（正常速度 DurationMul=2 / Speed=1）。</summary>
        static PlaybackTimingOptions TimingOptions() =>
            PerformanceRunner.Instance != null
                ? PlaybackTimingOptions.FromPacing(PerformanceRunner.Instance)
                : new PlaybackTimingOptions();

        static VFXResolver NewResolver() => new(PerformanceDatabase.LoadOrDefault());

        static CompiledPlayback CompileOrNull(string src)
        {
            var report = BattleReport.Parse(File.ReadAllText(src));
            if (report == null)
            {
                Debug.LogError("[PlaybackScript] 战报解析失败：" + src);
                return null;
            }
            var resolver = NewResolver();
            return PlaybackCompiler.Compile(
                report, g => resolver.Resolve(g).BorrowBlade,
                MomentumService.TrackTable.ContainsKey);
        }

        static JObject ToJson(CompiledPlayback compiled)
        {
            var timingOpt = TimingOptions();
            var resolver = NewResolver();
            var root = new JObject
            {
                ["battle_id"] = compiled.Report.BattleId,
                ["schema_version"] = compiled.Report.SchemaVersion,
                ["has_skill_catalog"] = compiled.Report.SkillCatalog.Count > 0,
            };
            var games = new JArray();
            for (int gi = 0; gi < compiled.GameGroups.Count; gi++)
            {
                var groupsJson = new JArray();
                foreach (var g in compiled.GameGroups[gi])
                {
                    var events = new JArray();
                    foreach (var ev in g.Events)
                        events.Add($"{ev.Seq}:{ev.GetType().Name}");
                    var item = new JObject
                    {
                        ["kind"] = g.Kind.ToString(),
                        ["root_seq"] = g.RootSeq,
                        ["batch"] = g.BatchId,
                        ["key"] = VFXResolver.KeyOf(g),
                        ["est_sec"] = Mathf.Round(
                            PlaybackDurationModel.GroupSeconds(g, resolver, timingOpt) * 1000f)
                            / 1000f,
                        ["events"] = events,
                    };
                    if (g.ParallelWithNext) item["parallel_with_next"] = true;
                    if (g.PierceBoost) item["pierce_boost"] = true;
                    if (g.CutIn != null)
                        item["cut_in"] = new JObject
                        {
                            ["hero"] = g.CutIn.HeroId,
                            ["title"] = g.CutIn.Title,
                            ["empowered"] = g.CutIn.Empowered,
                            ["massive"] = g.CutIn.Massive,
                            ["highlight"] = g.CutIn.Highlight,
                        };
                    groupsJson.Add(item);
                }
                games.Add(new JObject
                {
                    ["game_no"] = compiled.Report.Games[gi].GameNo,
                    ["groups"] = groupsJson,
                });
            }
            root["games"] = games;
            root["timing"] = PlaybackDurationModel.ToJson(
                PlaybackDurationModel.Rounds(compiled, resolver, timingOpt), timingOpt);
            return root;
        }
    }
}
