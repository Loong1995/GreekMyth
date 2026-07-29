using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Test
{
    // =========================================================================
    // 【诊断 UI】播放时间轴条：屏幕下方一条实时计时条 + 每回合一个刻度点。
    //
    // 用途是**人工校验时长模型**：条上刻度点的位置＝`PlaybackDurationModel` 算出的
    // 该回合起始秒数，游标＝真实经过的秒数（`PerformanceRunner.TimelineSeconds`，
    // 预热不计）。顺播时盯着「游标是否在回合横幅出现时正好压在刻度上」即可对时；
    // 点刻度点 → `PerformanceRunner.PlayFrom` 跳到该回合，并把时钟对齐到该刻度秒数，
    // 于是可以只校验某一回合而不必等前面播完。
    //
    // 刻度只在编译产物变化时重算一次（模型是纯函数，无需每帧算）。
    // OnGUI 绘制：与项目内既有诊断 UI（BattleReportTester/SettlementPanel）同路，
    // 不引入 Canvas 依赖。
    //
    // 文档：docs/client/playback_script.md §四.2
    // =========================================================================

    public class PlaybackTimelineBar : MonoBehaviour
    {
        public static PlaybackTimelineBar Instance { get; private set; }

        /// <summary>显示开关（BattleReportTester 的「计时条」按钮切换）。</summary>
        public bool Visible = true;

        PerformanceRunner _runner;
        List<RoundTiming> _rounds = new();
        float _total;
        object _compiledStamp;      // 编译产物身份：换战报/重建会话就重算刻度
        float _pacingStamp;         // 节奏参数变了也要重算（速度键）
        GUIStyle _label, _tick;
        Texture2D _px;

        public static PlaybackTimelineBar Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("PlaybackTimelineBar");
                Instance = go.AddComponent<PlaybackTimelineBar>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_px != null) Destroy(_px);
        }

        void RefreshIfNeeded()
        {
            _runner = PerformanceRunner.Instance;
            var compiled = _runner?.Compiled;
            if (compiled == null)
            {
                _rounds.Clear();
                _total = 0f;
                _compiledStamp = null;
                return;
            }
            float pacing = _runner.DurationMul * 1000f + _runner.Speed * 7f
                           + _runner.ActionPauseSeconds + _runner.GroupPauseSeconds;
            if (ReferenceEquals(compiled, _compiledStamp) && Mathf.Approximately(pacing, _pacingStamp))
                return;
            _compiledStamp = compiled;
            _pacingStamp = pacing;
            _rounds = PlaybackDurationModel.Rounds(
                compiled, new VFXResolver(_runner.Database),
                PlaybackTimingOptions.FromPacing(_runner));
            _total = 0f;
            foreach (var r in _rounds) _total += r.Seconds;
        }

        Texture2D Px()
        {
            if (_px == null)
            {
                _px = new Texture2D(1, 1);
                _px.SetPixel(0, 0, Color.white);
                _px.Apply();
            }
            return _px;
        }

        void Fill(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Px());
            GUI.color = prev;
        }

        void OnGUI()
        {
            RefreshIfNeeded();
            if (!Visible || _rounds.Count == 0 || _total <= 0.01f) return;

            float k = Mathf.Max(1f, Screen.height / 800f);
            float barH = 10f * k;
            float margin = 24f * k;
            float y = Screen.height - 54f * k;
            var bar = new Rect(margin, y, Screen.width - margin * 2f, barH);

            _label ??= new GUIStyle(GUI.skin.label);
            _label.fontSize = Mathf.RoundToInt(13 * k);
            _tick ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
            _tick.fontSize = Mathf.RoundToInt(11 * k);

            float now = _runner != null ? _runner.TimelineSeconds : 0f;
            float p = Mathf.Clamp01(now / _total);

            Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f),
                 new Color(0f, 0f, 0f, 0.55f));
            Fill(bar, new Color(0.16f, 0.17f, 0.2f, 0.9f));
            Fill(new Rect(bar.x, bar.y, bar.width * p, bar.height),
                 new Color(0.35f, 0.72f, 1f, 0.85f));

            // 回合刻度点：可点击（跳到该回合并对齐时钟）
            for (int i = 0; i < _rounds.Count; i++)
            {
                var r = _rounds[i];
                float x = bar.x + bar.width * Mathf.Clamp01(r.StartSeconds / _total);
                float dot = 9f * k;
                var hit = new Rect(x - dot, bar.y - dot * 0.9f, dot * 2f, bar.height + dot * 1.8f);
                bool hover = hit.Contains(Event.current.mousePosition);
                Fill(new Rect(x - dot * 0.5f, bar.y - dot * 0.35f, dot, bar.height + dot * 0.7f),
                     hover ? Color.white : new Color(1f, 0.85f, 0.35f, 0.95f));
                string text = r.RoundNo < 0 ? "开" : r.RoundNo.ToString();
                GUI.Label(new Rect(x - 20f * k, bar.y + bar.height + 2f * k, 40f * k, 18f * k),
                          text, _tick);
                if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
                    _runner?.PlayFrom(r.GameIndex, r.StartSeq, r.StartSeconds);
            }

            // 读数：当前秒 / 预估总长 + 当前所处回合（按刻度判定）
            var cur = CurrentRound(now);
            string where = cur.RoundNo < 0 ? "开场" : $"第 {cur.RoundNo} 回合";
            string label = $"{now:0.0}s / 预估 {_total:0.0}s   {where}"
                           + $"（预估 {cur.StartSeconds:0.0}~{cur.StartSeconds + cur.Seconds:0.0}s）"
                           + "   点刻度＝跳到该回合";
            var shadow = new Rect(bar.x + 1f, bar.y - 20f * k + 1f, bar.width, 18f * k);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(shadow, label, _label);
            GUI.color = prev;
            GUI.Label(new Rect(bar.x, bar.y - 20f * k, bar.width, 18f * k), label, _label);
        }

        RoundTiming CurrentRound(float seconds)
        {
            var best = _rounds[0];
            foreach (var r in _rounds)
                if (seconds >= r.StartSeconds - 0.01f) best = r;
            return best;
        }
    }
}
