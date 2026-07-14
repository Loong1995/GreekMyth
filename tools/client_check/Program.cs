using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Battle.Playback.Data;
using Battle.Playback.Director;
using Battle.Playback.Logging;

namespace ClientCheck
{
    /// <summary>
    /// Unity 外验证入口（与 EditMode 单测同源断言）。
    /// 用法：dotnet run [golden目录]，默认 ../../battle/tests/golden。
    /// </summary>
    internal static class Program
    {
        private static int _passed, _failed;

        private static int Main(string[] args)
        {
            string goldenDir = args.Length > 0 ? args[0]
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..", "battle", "tests", "golden"));
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"golden 目录: {goldenDir}");
            var files = Directory.GetFiles(goldenDir, "*.json");
            Console.WriteLine($"golden 战报 {files.Length} 份\n");

            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                var report = ReportLoader.LoadFromJson(File.ReadAllText(file));

                // 1. 完整播放 + 终态兵力对 result.stats
                var played = new PlaybackDirector();
                played.Load(report, NullSink.Instance);
                played.RunToEnd();
                bool ok = true;
                foreach (var stat in report.Result.Stats)
                    if (played.Mirror.Hero(stat.HeroId).Troops != stat.FinalTroops)
                    {
                        Fail($"{name}: {stat.HeroId} 终态兵力 {played.Mirror.Hero(stat.HeroId).Troops} != {stat.FinalTroops}");
                        ok = false;
                    }
                if (ok) Pass($"{name}: 播到底，终态兵力与 result.stats 一致（{report.Games.Count} 局）");

                // 2. 跳过等价
                var skipped = new PlaybackDirector();
                skipped.Load(report, NullSink.Instance);
                skipped.SkipToSeriesEnd();
                skipped.FastForwardToEnd();
                Check(played.Mirror.StateFingerprint() == skipped.Mirror.StateFingerprint(),
                    $"{name}: 跳过 == 完整播放（镜像指纹）");

                // 3. 展开粒度等价
                var expanded = new PlaybackDirector { Granularity = Granularity.Expanded };
                expanded.Load(report, NullSink.Instance);
                expanded.RunToEnd();
                Check(played.Mirror.StateFingerprint() == expanded.Mirror.StateFingerprint(),
                    $"{name}: 折叠 == 展开粒度终态");

                // 4. 事件树父链
                foreach (var game in report.Games)
                    EventTreeBuilder.BuildForest(game, report.BattleId);
                Pass($"{name}: 事件树父链完整");
            }

            // 5. 非法战报显式报错
            string sample = File.ReadAllText(Path.Combine(goldenDir, "1v1_seed7.json"));
            ExpectThrow("截断 JSON", () => ReportLoader.LoadFromJson(sample.Substring(0, sample.Length / 2)));
            var noTeams = JObject.Parse(sample); noTeams.Remove("teams");
            ExpectThrow("缺顶层字段 teams", () => ReportLoader.Parse(noTeams));
            var badMajor = JObject.Parse(sample); badMajor["schema_version"] = "2.0.0";
            ExpectThrow("schema major 不兼容", () => ReportLoader.Parse(badMajor));
            var badSeq = JObject.Parse(sample);
            var evs = (JArray)badSeq["games"][0]["events"];
            evs[1]["seq"] = evs[0]["seq"].Value<long>();
            ExpectThrow("seq 非严格递增", () => ReportLoader.Parse(badSeq));
            var tampered = JObject.Parse(sample);
            foreach (var e in (JArray)tampered["games"][0]["events"])
                if (e["type"].Value<string>() == "damage")
                {
                    e["payload"]["troops"]["troops_before"] =
                        e["payload"]["troops"]["troops_before"].Value<int>() + 123;
                    break;
                }
            ExpectThrow("篡改 troops_before → 镜像自校验", () =>
            {
                var d = new PlaybackDirector();
                d.Load(ReportLoader.Parse(tampered), NullSink.Instance);
                d.RunToEnd();
            });

            // 6. 播放日志样例落盘
            var logReport = ReportLoader.LoadFromJson(
                File.ReadAllText(Path.Combine(goldenDir, "standard_seed20260705.json")));
            string logDir = Path.Combine(goldenDir, "..", "..", "..", "Logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.GetFullPath(Path.Combine(logDir,
                $"battle_playback_{logReport.BattleId}.log"));
            File.WriteAllText(logPath, PlaybackLogFormatter.Format(logReport),
                new System.Text.UTF8Encoding(false));
            Pass($"播放日志已落盘 {logPath}");

            Console.WriteLine($"\n===== 通过 {_passed} / 失败 {_failed} =====");
            return _failed == 0 ? 0 : 1;
        }

        private static void Pass(string msg) { _passed++; Console.WriteLine($"[PASS] {msg}"); }
        private static void Fail(string msg) { _failed++; Console.WriteLine($"[FAIL] {msg}"); }
        private static void Check(bool cond, string msg) { if (cond) Pass(msg); else Fail(msg); }

        private static void ExpectThrow(string label, Action action)
        {
            try { action(); Fail($"{label}: 未报错（应抛 ReportFormatException）"); }
            catch (ReportFormatException ex) { Pass($"{label}: 显式报错 → {Trim(ex.Message)}"); }
        }

        private static string Trim(string s) => s.Length > 80 ? s.Substring(0, 80) + "…" : s;
    }
}
