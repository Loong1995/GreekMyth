using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ClientBattle.Test
{
    // =========================================================================
    // 战斗结算桥接：客户端不做任何结算，两种传输取结果：
    //
    //   1. HTTP（首选，iOS/真机唯一通道）：请求长期运行的
    //      `python battle/tools/battle_server.py`（/catalog /battle /stats）。
    //   2. 子进程回退（仅编辑器与桌面独立版，且与仓库同机）：HTTP 不通时
    //      直接调 battle/tools/client_battle_bridge.py。
    //
    // 调用方在 Update 里轮询 Done / Error / ResultJson。
    // =========================================================================

    public class ManualBattleBridge
    {
        /// <summary>战斗服务地址（手动配阵页可改；发布 iOS 前改成局域网/公网地址）。</summary>
        public static string ServerUrl = "http://127.0.0.1:8017";

        public bool Done { get; private set; }
        public string Error { get; private set; }
        public string ResultJson { get; private set; }
        /// <summary>本次实际用的通道："http" / "process"。</summary>
        public string Transport { get; private set; } = "";

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(300) };

        static bool ProcessFallbackAllowed =>
#if UNITY_EDITOR || UNITY_STANDALONE
            true;
#else
            false;
#endif

        // ------------------------------------------------------------ 三个入口

        public static ManualBattleBridge FetchCatalog() =>
            Start(
                httpFunc: () => HttpGet("/catalog"),
                processArgs: root =>
                {
                    string outPath = Path.Combine(OutDir(root), "catalog.json");
                    return ($"battle/tools/client_battle_bridge.py --catalog --out \"{outPath}\"", outPath);
                });

        public static ManualBattleBridge RunOnce(string configJson, int seed) =>
            Start(
                httpFunc: () => HttpPost("/battle", $"{{\"config\":{configJson},\"seed\":{seed}}}"),
                processArgs: root =>
                {
                    string cfgPath = WriteConfig(root, configJson);
                    string outPath = Path.Combine(OutDir(root), "report.json");
                    return ($"battle/tools/client_battle_bridge.py --config \"{cfgPath}\" --seed {seed} --out \"{outPath}\"", outPath);
                });

        public static ManualBattleBridge RunStats(string configJson, int n, int seed) =>
            Start(
                httpFunc: () => HttpPost("/stats", $"{{\"config\":{configJson},\"n\":{n},\"seed\":{seed}}}"),
                processArgs: root =>
                {
                    string cfgPath = WriteConfig(root, configJson);
                    string outPath = Path.Combine(OutDir(root), "stats.json");
                    return ($"battle/tools/client_battle_bridge.py --config \"{cfgPath}\" --n {n} --seed {seed} --stats-out \"{outPath}\"", outPath);
                });

        // ------------------------------------------------------------ 调度

        static ManualBattleBridge Start(
            Func<string> httpFunc,
            Func<string, (string args, string resultPath)> processArgs)
        {
            var bridge = new ManualBattleBridge();
            new Thread(() =>
            {
                string httpError;
                try
                {
                    bridge.ResultJson = httpFunc();
                    bridge.Transport = "http";
                    bridge.Done = true;
                    return;
                }
                catch (Exception ex)
                {
                    httpError = ex.InnerException?.Message ?? ex.Message;
                }

                if (!ProcessFallbackAllowed)
                {
                    bridge.Error = $"战斗服务不可达（{ServerUrl}）：{httpError}\n" +
                                   "请先启动：python battle/tools/battle_server.py";
                    bridge.Done = true;
                    return;
                }

                // 子进程回退（编辑器/桌面）
                try
                {
                    string root = RepoRoot();
                    var (args, resultPath) = processArgs(root);
                    RunProcess(bridge, root, args, resultPath);
                    bridge.Transport = "process";
                    if (bridge.Error == null)
                        UnityEngine.Debug.LogWarning(
                            $"[ManualBridge] HTTP 不可达（{httpError}），已用本机 python 子进程回退");
                }
                catch (Exception ex)
                {
                    bridge.Error = $"HTTP 与子进程均失败。HTTP: {httpError}；子进程: {ex.Message}";
                }
                finally
                {
                    bridge.Done = true;
                }
            }) { IsBackground = true }.Start();
            return bridge;
        }

        // ------------------------------------------------------------ HTTP 通道

        static string HttpGet(string path)
        {
            var resp = Http.GetAsync(ServerUrl.TrimEnd('/') + path).GetAwaiter().GetResult();
            return ReadResponse(resp);
        }

        static string HttpPost(string path, string body)
        {
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = Http.PostAsync(ServerUrl.TrimEnd('/') + path, content).GetAwaiter().GetResult();
            return ReadResponse(resp);
        }

        static string ReadResponse(HttpResponseMessage resp)
        {
            string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)resp.StatusCode}: {Tail(text)}");
            return text;
        }

        // ------------------------------------------------------------ 子进程通道

        /// <summary>仓库根 = Assets 上一级（编辑器）；独立版沿 exe 目录向上找 battle/。</summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(UnityEngine.Application.dataPath).Parent;
            for (var d = dir; d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "battle")))
                    return d.FullName;
            return dir?.FullName ?? ".";
        }

        static string OutDir(string root) => Path.Combine(root, "battle", "out", "manual", "ui");

        static string WriteConfig(string root, string configJson)
        {
            string dir = OutDir(root);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, configJson, new UTF8Encoding(false));
            return path;
        }

        static void RunProcess(ManualBattleBridge bridge, string root, string args, string resultPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = args,
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi);
            string stderr = proc.StandardError.ReadToEnd();
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                bridge.Error = $"python 退出码 {proc.ExitCode}\n{Tail(stderr)}";
            else if (!File.Exists(resultPath))
                bridge.Error = $"python 成功但无输出文件: {resultPath}";
            else
                bridge.ResultJson = File.ReadAllText(resultPath, Encoding.UTF8);
        }

        static string Tail(string s, int lines = 12)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var arr = s.Replace("\r", "").Split('\n');
            int start = Math.Max(0, arr.Length - lines);
            return string.Join("\n", arr, start, arr.Length - start);
        }

        // ------------------------------------------------------------ config 拼装

        /// <summary>由每队最多 6 个站位槽拼 bridge config（空位跳过；
        /// slots 下标 0..5 = position 1..6）。</summary>
        public static string BuildConfigJson(ManualSlot[] slotsA, ManualSlot[] slotsB)
        {
            var root = new JObject
            {
                ["battle_id"] = "manual_ui",
                ["teams"] = new JArray(TeamObj("A", slotsA), TeamObj("B", slotsB)),
            };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        static JObject TeamObj(string teamId, ManualSlot[] slots)
        {
            var heroes = new JArray();
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.IsEmpty) continue;
                var extras = new JArray();
                foreach (var sk in s.ExtraSkills)
                    if (!string.IsNullOrEmpty(sk)) extras.Add(sk);
                heroes.Add(new JObject
                {
                    ["template"] = s.TemplateId,
                    ["position"] = i + 1, // 槽位序 → 站位 1~6
                    ["extra_skills"] = extras,
                });
            }
            return new JObject { ["team_id"] = teamId, ["heroes"] = heroes };
        }
    }
}
