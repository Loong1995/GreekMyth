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
    /// 所见即所播——排「为什么这组这么演」不再需要进 Play 模式打断点。</summary>
    public static class ExportPlaybackScript
    {
        [MenuItem("GreekMyth/播放/导出 PlaybackScript（战报→.playback.json）")]
        public static void Export()
        {
            string src = EditorUtility.OpenFilePanel(
                "选择战报 JSON", Application.streamingAssetsPath + "/battle_reports", "json");
            if (string.IsNullOrEmpty(src)) return;

            var report = BattleReport.Parse(File.ReadAllText(src));
            if (report == null)
            {
                Debug.LogError("[PlaybackScript] 战报解析失败：" + src);
                return;
            }
            var resolver = new VFXResolver(null); // 编辑器侧用运行时默认库（同源兜底）
            var compiled = PlaybackCompiler.Compile(
                report, g => resolver.Resolve(g).BorrowBlade,
                MomentumService.TrackTable.ContainsKey);

            string dest = Path.ChangeExtension(src, null) + ".playback.json";
            File.WriteAllText(dest, ToJson(compiled).ToString(Newtonsoft.Json.Formatting.Indented));
            Debug.Log($"[PlaybackScript] 已导出 {dest}（{compiled.GameGroups.Count} 局）");
            EditorUtility.RevealInFinder(dest);
        }

        static JObject ToJson(CompiledPlayback compiled)
        {
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
                        ["key"] = VFXResolver.KeyOf(g),
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
            return root;
        }
    }
}
