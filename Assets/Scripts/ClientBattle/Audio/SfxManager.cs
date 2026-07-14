using System.Collections.Generic;
using ClientBattle.Placeholder;
using UnityEngine;

namespace ClientBattle.Audio
{
    // =========================================================================
    // 音效服务：AudioSource 轮转池 + 同帧去重。
    //
    // - 资源回退：Resources/ClientBattle/SFX/<key>.wav → 按 key 合成占位提示音。
    // - 同帧去重（client_perform：状态常与伤害同时打出，两音效不要重复播放）：
    //   同一帧内相同 key 只播一次；同一帧超过 4 个不同音效丢弃后来者防炸耳。
    // =========================================================================

    public class SfxManager : MonoBehaviour
    {
        public static SfxManager Instance { get; private set; }

        const int SourceCount = 8;
        const int MaxPerFrame = 4;

        readonly List<AudioSource> _sources = new();
        readonly HashSet<string> _playedThisFrame = new();
        int _frameOfRecord = -1, _countThisFrame, _cursor;

        public static SfxManager Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("SfxManager");
                Instance = go.AddComponent<SfxManager>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            for (int i = 0; i < SourceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources.Add(source);
            }
        }

        /// <summary>播放音效 key（空 key 忽略）。同帧同 key 去重。</summary>
        public void Play(string key, float volume = 1f)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (Time.frameCount != _frameOfRecord)
            {
                _frameOfRecord = Time.frameCount;
                _playedThisFrame.Clear();
                _countThisFrame = 0;
            }
            if (_playedThisFrame.Contains(key) || _countThisFrame >= MaxPerFrame) return;
            _playedThisFrame.Add(key);
            _countThisFrame++;

            var clip = PlaceholderFactory.GetAudio(key);
            var source = _sources[_cursor];
            _cursor = (_cursor + 1) % _sources.Count;
            source.PlayOneShot(clip, volume);
        }

        public void StopAll()
        {
            foreach (var source in _sources) source.Stop();
        }
    }
}
