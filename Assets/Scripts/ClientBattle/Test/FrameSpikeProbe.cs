using System.Text;
using UnityEngine;

namespace ClientBattle.Test
{
    // =========================================================================
    // 帧尖峰探针（诊断用）：平均 FPS 掩盖单帧尖峰，这里直接记录
    //   - 每 5 秒窗口的平均 FPS / 最差帧耗时 / 超 33ms、66ms 的帧数
    //   - 每个超 66ms 的帧记录帧号与耗时，战后可对照事件定位
    // 用法：场景里任意挂一个即可；Report() 拿汇总文本。
    // =========================================================================

    public class FrameSpikeProbe : MonoBehaviour
    {
        public static FrameSpikeProbe Instance { get; private set; }

        readonly StringBuilder _spikeLog = new();
        float _elapsed, _worst;
        int _frames, _over33, _over66;
        int _totalOver33, _totalOver66, _totalFrames;
        float _totalTime, _totalWorst;

        public static FrameSpikeProbe Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("FrameSpikeProbe");
                Instance = go.AddComponent<FrameSpikeProbe>();
                DontDestroyOnLoad(go);
            }
            return Instance;
        }

        void Awake() => Instance = this;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _frames++; _totalFrames++;
            _elapsed += dt; _totalTime += dt;
            if (dt > _worst) _worst = dt;
            if (dt > _totalWorst) _totalWorst = dt;
            if (dt > 0.033f) { _over33++; _totalOver33++; }
            if (dt > 0.066f)
            {
                _over66++; _totalOver66++;
                _spikeLog.AppendLine($"frame {Time.frameCount}: {dt * 1000f:F1}ms");
            }

            if (_elapsed < 5f) return;
            Debug.Log($"[Probe] {_frames / _elapsed:F0}fps avg | worst {_worst * 1000f:F1}ms" +
                      $" | >33ms x{_over33} | >66ms x{_over66} (window {_elapsed:F1}s)");
            _elapsed = 0f; _frames = 0; _worst = 0f; _over33 = 0; _over66 = 0;
        }

        GUIStyle _style;

        void OnGUI()
        {
            // 心跳转子：由 Update 每帧驱动。冻结时它若也停 = 引擎主循环停了
            // （真卡死）；它仍在转而特效不动 = 演出内容层死时间。肉眼即可分诊。
            float k = Mathf.Max(1f, Screen.height / 800f);
            _style ??= new GUIStyle(GUI.skin.label);
            _style.fontSize = Mathf.RoundToInt(16 * k);
            _style.normal.textColor = Color.green;
            int spin = (int)(Time.unscaledTime * 10f) % 8;
            string wheel = "|/-\\|/-\\".Substring(spin, 1);
            GUI.Label(new Rect(10 * k, Screen.height - 30 * k, 400 * k, 26 * k),
                $"{wheel} {Time.unscaledDeltaTime * 1000f:F0}ms f{Time.frameCount}", _style);
        }

        public string Report() =>
            $"total {_totalTime:F1}s {_totalFrames} frames avg {_totalFrames / Mathf.Max(0.01f, _totalTime):F0}fps" +
            $" worst {_totalWorst * 1000f:F1}ms >33ms x{_totalOver33} >66ms x{_totalOver66}\n{_spikeLog}";
    }
}
