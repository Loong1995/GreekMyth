using System.IO;
using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Test
{
    // =========================================================================
    // 战报播放测试入口：挂到空场景任意物体上即可。
    //
    // 用法：
    //   1. 场景放一个空物体挂本脚本，设置 ReportPath（StreamingAssets 相对路径）；
    //   2. Play 后自动播放全场表现；右上角按钮可调速/跳过/重播；
    //   3. 也可在 Inspector 里直接粘 JSON 到 InlineJson（优先级高于文件）。
    // =========================================================================

    public class BattleReportTester : MonoBehaviour
    {
        [Header("战报来源（二选一）")]
        [Tooltip("StreamingAssets 下的相对路径")]
        public string ReportPath = "battle_reports/burst_tactics_seed42.json"; // Phase 4 验收：连发×10/预设战术/势能
        [Tooltip("直接粘贴战报 JSON（优先于文件路径）")]
        [TextArea(3, 10)] public string InlineJson = "";

        [Header("播放")]
        public bool AutoPlayOnStart = true;
        [Range(0.25f, 4f)] public float Speed = 1f;

        [Header("诊断")]
        [Tooltip("显示帧尖峰探针（左下角心跳转子 + 长帧日志），排查性能时打开")]
        public bool ShowDiagnostics = false;
        [Tooltip("屏幕下方实时计时条 + 每回合刻度点（点刻度跳到该回合，人工校验时长用）")]
        public bool ShowTimelineBar = true;

        PerformanceRunner _runner;

        void Start()
        {
            // 独立版观感基线：垂直同步锁刷新率（无节制上千 fps 反而撕裂、节奏不匀）
            QualitySettings.vSyncCount = 1;
            // 失焦不暂停：默认 false 时窗口一失焦（弹窗/切屏/点别处）整个游戏冻住，
            // 观感就是"全场飘字和特效卡死一会"
            Application.runInBackground = true;
#if !UNITY_EDITOR
            // 独立版默认窗口化（1280x720），全屏体验交给正式客户端做
            if (Screen.fullScreen) Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
#endif
            if (ShowDiagnostics) FrameSpikeProbe.Ensure();
            if (ShowTimelineBar) PlaybackTimelineBar.Ensure();
            _runner = PerformanceRunner.Ensure();
            _runner.OnAllComplete += () => Debug.Log("[ClientBattle] 战报播放完成");
            if (AutoPlayOnStart) Play();
        }

        [ContextMenu("Play Report")]
        public void Play()
        {
            string json = InlineJson;
            if (string.IsNullOrEmpty(json))
            {
                string fullPath = Path.Combine(Application.streamingAssetsPath, ReportPath);
                if (!File.Exists(fullPath))
                {
                    Debug.LogError($"[ClientBattle] 找不到战报文件: {fullPath}");
                    return;
                }
                json = File.ReadAllText(fullPath);
            }
            _runner.Speed = Speed;
            _runner.PlayBattleReport(json);
        }

        void Update()
        {
            if (_runner != null) _runner.Speed = Speed;
            // 项目用新 Input System：不得用旧 UnityEngine.Input（运行时每帧抛异常）
#if ENABLE_INPUT_SYSTEM
            bool esc = UnityEngine.InputSystem.Keyboard.current != null &&
                       UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            bool esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (esc)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        GUIStyle _buttonStyle; // 缓存：每帧 new GUIStyle 会产生 GC 压力

        void OnGUI()
        {
            // 按屏幕高度缩放（800px 高为基准），手机高分屏按钮不至于点不到
            float k = Mathf.Max(1f, Screen.height / 800f);
            float w = 110f * k, h = 30f * k;
            float x = Screen.width - w - 12f * k, y = 12f * k;
            _buttonStyle ??= new GUIStyle(GUI.skin.button);
            var style = _buttonStyle;
            style.fontSize = Mathf.RoundToInt(14 * k);
            if (GUI.Button(new Rect(x, y, w, h), "重播", style)) Play();
            if (GUI.Button(new Rect(x, y + h * 1.2f, w, h), "跳到结尾", style)) _runner?.SkipToEnd();
            if (GUI.Button(new Rect(x, y + h * 2.4f, w, h), $"速度 x{Speed:0.##}", style))
                Speed = Speed >= 4f ? 0.5f : Speed * 2f;
            // 高光回放（B6）：播放完成后可用（按我方 A 队单窗伤害最大行动窗重播）
            if (_runner != null && !_runner.IsPlaying &&
                GUI.Button(new Rect(x, y + h * 3.6f, w, h), "高光回放", style))
                _runner.PlayHighlight("A");
            if (_runner != null && !_runner.IsPlaying &&
                GUI.Button(new Rect(x, y + h * 4.8f, w, h), "打开结算", style))
                _runner.ShowSettlement();
            bool timelineOn = PlaybackTimelineBar.Instance is { Visible: true };
            if (GUI.Button(new Rect(x, y + h * 6f, w, h),
                           timelineOn ? "计时条 开" : "计时条 关", style))
                PlaybackTimelineBar.Ensure().Visible = !timelineOn;
        }
    }
}
