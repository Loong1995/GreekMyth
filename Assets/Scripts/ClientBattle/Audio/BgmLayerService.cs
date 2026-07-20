using System.Collections.Generic;
using UnityEngine;

namespace ClientBattle.Audio
{
    // =========================================================================
    // 【B3】BGM 分层混音（原生 AudioSource，不引入 FMOD）：
    //
    // - 4 层 stem（drums/bass/melody/other，Demucs 拆层产物）同 BPM 同长度，
    //   开战同帧同相位起播；按**全局势能档**（双方全部武将四轨之和）淡入淡出：
    //     0~7 档1（drums+bass）/ 8~15 档2（+melody）/ 16+ 档3（全层拉满）。
    // - **切层对齐小节边界**：档位变化先记 pending，到下一小节头才动音量目标
    //   （BarsPerPhrase×拍/小节，按登记 BPM 换算），避免乐句中途突兀。
    // - duck：单挑/cut-in 全层 -8dB，0.5s 线性恢复（不打断播放）。
    // - 占位路线：stem 缺失时回退单曲 bgm_main（音量+低通滤波随档位变化）；
    //   连单曲也没有则整体静默 no-op（不报错——BGM 属可选资产）。
    // - 素材路线与人工步骤见 docs/dev/phase4_manual_tasks.md（Suno+Demucs）。
    // =========================================================================

    public class BgmLayerService : MonoBehaviour
    {
        public static BgmLayerService Instance { get; private set; }

        [Header("曲目参数（换曲后人工登记）")]
        public float Bpm = 110f;
        public int BeatsPerBar = 4;
        [Tooltip("整体音量上限")] public float MasterVolume = 0.55f;

        // stem key → 档位门槛（达到该档才淡入）。注册表驱动：换分层结构只改表。
        static readonly (string key, int minTier)[] StemTable =
        {
            ("bgm_stem_drums", 1),
            ("bgm_stem_bass", 1),
            ("bgm_stem_melody", 2),
            ("bgm_stem_other", 3),
        };
        const int Tier2Momentum = 8, Tier3Momentum = 16; // 全局势能→档位
        const float DuckDb = -8f, DuckRecoverSeconds = 0.5f;
        const float FadeSeconds = 1.2f; // 层淡入淡出时长

        readonly List<(AudioSource source, int minTier)> _stems = new();
        AudioSource _fallbackSingle;          // 占位：单曲模式
        AudioLowPassFilter _lowPass;          // 占位单曲的低通（档位越低越闷）
        int _tier = 1, _pendingTier = -1;
        float _playStartDsp;                  // 起播 dspTime（小节对齐基准）
        float _duckGain = 1f;                 // duck 当前增益（1=正常）
        bool _active;

        public static BgmLayerService Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("BgmLayerService");
                Instance = go.AddComponent<BgmLayerService>();
                DontDestroyOnLoad(go);
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ---------------------------------------------------------- 生命周期

        /// <summary>开战调用：加载 stem（或占位单曲）并同相位起播。</summary>
        public void StartBattle()
        {
            StopBattle();
            foreach (var (key, minTier) in StemTable)
            {
                var clip = Resources.Load<AudioClip>($"ClientBattle/BGM/{key}");
                if (clip == null) continue;
                var source = gameObject.AddComponent<AudioSource>();
                source.clip = clip;
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0f;
                _stems.Add((source, minTier));
            }
            if (_stems.Count == 0) // 占位单曲回退
            {
                var single = Resources.Load<AudioClip>("ClientBattle/BGM/bgm_main");
                if (single == null) return; // 无任何 BGM 资产：整体静默
                _fallbackSingle = gameObject.AddComponent<AudioSource>();
                _fallbackSingle.clip = single;
                _fallbackSingle.loop = true;
                _fallbackSingle.volume = 0f;
                _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            }
            double startAt = AudioSettings.dspTime + 0.1;
            foreach (var (source, _) in _stems) source.PlayScheduled(startAt);
            _fallbackSingle?.PlayScheduled(startAt);
            _playStartDsp = (float)startAt;
            _tier = 1;
            _pendingTier = -1;
            _duckGain = 1f;
            _active = true;
        }

        public void StopBattle()
        {
            _active = false;
            foreach (var (source, _) in _stems) Destroy(source);
            _stems.Clear();
            if (_fallbackSingle != null) Destroy(_fallbackSingle);
            if (_lowPass != null) Destroy(_lowPass);
            _fallbackSingle = null;
            _lowPass = null;
        }

        // ---------------------------------------------------------- 对外接口

        /// <summary>全局势能变化（MomentumService 每次落账后回调总和）。
        /// 档位变化只记 pending，下一小节边界才生效。</summary>
        public void SetGlobalMomentum(int total)
        {
            int tier = total >= Tier3Momentum ? 3 : total >= Tier2Momentum ? 2 : 1;
            if (tier != _tier) _pendingTier = tier;
            else _pendingTier = -1;
        }

        /// <summary>单挑/cut-in duck：全层瞬间 -8dB，0.5s 线性恢复。</summary>
        public void Duck()
        {
            if (_active) _duckGain = Mathf.Pow(10f, DuckDb / 20f);
        }

        // ---------------------------------------------------------- 驱动

        void Update()
        {
            if (!_active) return;

            // 小节边界检测：pending 档位到小节头生效
            if (_pendingTier > 0)
            {
                float barSeconds = BeatsPerBar * 60f / Mathf.Max(30f, Bpm);
                float elapsed = (float)AudioSettings.dspTime - _playStartDsp;
                float posInBar = elapsed % barSeconds;
                if (posInBar < Time.unscaledDeltaTime + 0.02f) // 本帧跨过小节头
                {
                    _tier = _pendingTier;
                    _pendingTier = -1;
                }
            }

            // duck 恢复
            if (_duckGain < 1f)
                _duckGain = Mathf.Min(1f, _duckGain + Time.unscaledDeltaTime / DuckRecoverSeconds);

            // 分层音量趋近目标（交叉淡变）
            float step = Time.unscaledDeltaTime / FadeSeconds;
            foreach (var (source, minTier) in _stems)
            {
                float target = (_tier >= minTier ? MasterVolume : 0f) * _duckGain;
                source.volume = Mathf.MoveTowards(source.volume, target, step * MasterVolume);
            }
            if (_fallbackSingle != null) // 占位单曲：音量+低通随档位
            {
                float target = MasterVolume * (0.55f + 0.225f * (_tier - 1)) * _duckGain;
                _fallbackSingle.volume = Mathf.MoveTowards(_fallbackSingle.volume, target, step * MasterVolume);
                float cutoffTarget = _tier switch { 1 => 2200f, 2 => 8000f, _ => 22000f };
                _lowPass.cutoffFrequency = Mathf.MoveTowards(
                    _lowPass.cutoffFrequency, cutoffTarget, Time.unscaledDeltaTime * 20000f / FadeSeconds);
            }
        }
    }
}
