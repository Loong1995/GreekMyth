using System.Collections.Generic;
using UnityEngine;

namespace ClientBattle.Placeholder
{
    // =========================================================================
    // 占位资源工厂：所有美术/音频资源的"三级回退"最后一层。
    //
    // 查找顺序（由各服务执行）：
    //   1. Resources/ClientBattle/<类别>/<key>   ← 你后续上传真实资源放这里（同名覆盖即生效）
    //   2. 本工厂程序化生成的占位（纯色块/字母图标/合成音）
    // 目录与命名约定见 docs/client/assets_upload_guide.md。
    // 生成结果全部缓存，重复 key 零开销。
    // =========================================================================

    public static class PlaceholderFactory
    {
        static readonly Dictionary<string, Sprite> SpriteCache = new();
        static readonly Dictionary<string, AudioClip> AudioCache = new();

        // ---------------------------------------------------------- Sprite

        /// <summary>优先 Resources/ClientBattle/{folder}/{key}.png，缺失则生成纯色占位。</summary>
        public static Sprite GetSprite(string folder, string key, Color fallbackColor, int size = 64)
        {
            string cacheKey = $"{folder}/{key}";
            // Unity 判空：运行时合成的资源退出 Play 会被销毁，跨会话静态缓存要重建
            if (SpriteCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            var loaded = Resources.Load<Sprite>($"ClientBattle/{folder}/{key}");
            if (loaded == null)
                loaded = MakeSolidSprite(fallbackColor, size, LabelOf(key));
            SpriteCache[cacheKey] = loaded;
            return loaded;
        }

        /// <summary>仅尝试加载真实资源（不生成占位），供"真图走白底染色、占位走纯色"的双路径用。</summary>
        public static Sprite TryLoadSprite(string folder, string key)
        {
            string cacheKey = $"real:{folder}/{key}";
            if (SpriteCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            var loaded = Resources.Load<Sprite>($"ClientBattle/{folder}/{key}");
            if (loaded != null) SpriteCache[cacheKey] = loaded;
            return loaded;
        }

        /// <summary>纯色圆角方块 + 可选首字母压印（区分不同占位图标）。</summary>
        public static Sprite MakeSolidSprite(Color color, int size, char label = '\0')
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            int r = size / 8; // 圆角半径
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool corner =
                        (x < r && y < r && (r - x) * (r - x) + (r - y) * (r - y) > r * r) ||
                        (x >= size - r && y < r && (x - size + r + 1) * (x - size + r + 1) + (r - y) * (r - y) > r * r) ||
                        (x < r && y >= size - r && (r - x) * (r - x) + (y - size + r + 1) * (y - size + r + 1) > r * r) ||
                        (x >= size - r && y >= size - r && (x - size + r + 1) * (x - size + r + 1) + (y - size + r + 1) * (y - size + r + 1) > r * r);
                    pixels[y * size + x] = corner ? new Color32(0, 0, 0, 0) : (Color32)color;
                }
            // 中央压印一道浅色横条示意"占位"（避免依赖字体渲染到纹理）
            if (label != '\0')
            {
                var stripe = (Color32)Color.Lerp(color, Color.white, 0.6f);
                for (int y = size / 2 - size / 16; y < size / 2 + size / 16; y++)
                    for (int x = size / 4; x < size * 3 / 4; x++)
                        pixels[y * size + x] = stripe;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.name = $"placeholder_{color}";
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        static char LabelOf(string key) => string.IsNullOrEmpty(key) ? '\0' : char.ToUpperInvariant(key[0]);

        // ---------------------------------------------------------- Audio

        /// <summary>优先 Resources/ClientBattle/SFX/{key}，缺失则按 key 哈希合成短促提示音。</summary>
        public static AudioClip GetAudio(string key)
        {
            if (AudioCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var loaded = Resources.Load<AudioClip>($"ClientBattle/SFX/{key}");
            if (loaded == null)
                loaded = SynthesizeBeep(key);
            AudioCache[key] = loaded;
            return loaded;
        }

        /// <summary>确定性合成：同一 key 永远同一音色（频率由哈希决定），方便肉耳区分占位音。</summary>
        static AudioClip SynthesizeBeep(string key)
        {
            const int sampleRate = 22050;
            float duration = 0.12f;
            int hash = 0;
            foreach (char c in key) hash = hash * 31 + c;
            float freq = 320f + Mathf.Abs(hash % 700);      // 320~1020 Hz
            int samples = (int)(sampleRate * duration);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = 1f - (float)i / samples;    // 线性衰减
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.28f;
            }
            var clip = AudioClip.Create($"beep_{key}", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
