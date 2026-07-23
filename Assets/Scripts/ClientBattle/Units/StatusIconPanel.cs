using System.Collections.Generic;
using ClientBattle.Placeholder;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 状态图标面板：控制类图标（缄默/缴械/犹豫/石化/冥锁/魅惑/冥火…）
    // 排在卡牌**上边缘外侧**横排；宽 ≈ 卡宽 1/5；每枚独立随机抖动。
    // 常规状态不显示图标（靠光环 / 飘字）。资源：StatusIcons/<status_id>.png。
    // =========================================================================

    public class StatusIconPanel : MonoBehaviour
    {
        const float DefaultCardWidth = 1.55f;   // 与 UnitView.FrameSlotW 对齐
        const float DefaultCardHalfH = 1.27f;   // FrameSlotH * 0.5
        const float GapRatio = 0.15f;           // 图标间距相对图标宽
        const float OutsidePadRatio = 0.12f;    // 卡顶外侧间隙相对图标宽
        const float JitterAmpRatio = 0.14f;     // 抖动幅度相对图标宽
        const int PerRow = 5;                  // 一行最多 5（约铺满卡宽）

        float _cardWidth = DefaultCardWidth;
        float _cardHalfH = DefaultCardHalfH;
        float _iconSize;

        readonly Dictionary<string, IconEntry> _icons = new();
        readonly List<string> _keyBuf = new(8);

        struct IconEntry
        {
            public GameObject Go;
            public Vector3 BaseLocal;
            public float PhaseX, PhaseY;
            public float FreqX, FreqY;
        }

        /// <summary>由 UnitView 注入卡框尺寸，决定图标大小与顶边位置。</summary>
        public void Configure(float cardWidth, float cardHalfHeight)
        {
            _cardWidth = cardWidth;
            _cardHalfH = cardHalfHeight;
            _iconSize = cardWidth / 5f;
            Relayout();
        }

        public static bool IsControl(string statusId)
            => Names.StatusPresentationRegistry.IsControl(statusId);

        public static void PrewarmIcon(string statusId)
        {
            if (!IsControl(statusId)) return;
            PlaceholderFactory.GetSprite("StatusIcons", statusId, ColorOf(statusId), 48);
        }

        public void AddStatus(string statusId)
        {
            if (!IsControl(statusId)) return;
            if (_icons.ContainsKey(statusId)) return;
            EnsureSize();
            int hash = statusId.GetHashCode();
            _icons[statusId] = new IconEntry
            {
                Go = BuildIcon(statusId),
                PhaseX = (hash & 0xFF) / 255f * Mathf.PI * 2f,
                PhaseY = ((hash >> 8) & 0xFF) / 255f * Mathf.PI * 2f,
                FreqX = 6.5f + ((hash >> 16) & 0xF) * 0.35f,
                FreqY = 5.2f + ((hash >> 20) & 0xF) * 0.4f,
            };
            Relayout();
        }

        public void RemoveStatus(string statusId)
        {
            if (!_icons.TryGetValue(statusId, out var entry)) return;
            _icons.Remove(statusId);
            if (entry.Go != null) Destroy(entry.Go);
            Relayout();
        }

        public void Clear()
        {
            foreach (var e in _icons.Values)
                if (e.Go != null) Destroy(e.Go);
            _icons.Clear();
        }

        void EnsureSize()
        {
            if (_iconSize <= 0f) _iconSize = _cardWidth / 5f;
        }

        void Update()
        {
            if (_icons.Count == 0) return;
            float amp = _iconSize * JitterAmpRatio;
            float t = Time.time;
            FillKeys();
            for (int i = 0; i < _keyBuf.Count; i++)
            {
                var e = _icons[_keyBuf[i]];
                if (e.Go == null) continue;
                float jx = Mathf.Sin(t * e.FreqX + e.PhaseX) * amp;
                float jy = Mathf.Cos(t * e.FreqY + e.PhaseY) * amp * 0.85f;
                jx += (Mathf.PerlinNoise(t * 1.7f + e.PhaseX, e.PhaseY) - 0.5f) * amp * 0.9f;
                jy += (Mathf.PerlinNoise(e.PhaseX, t * 1.9f + e.PhaseY) - 0.5f) * amp * 0.9f;
                e.Go.transform.localPosition = e.BaseLocal + new Vector3(jx, jy, 0f);
            }
        }

        void Relayout()
        {
            EnsureSize();
            int n = _icons.Count;
            if (n == 0) return;
            float gap = _iconSize * GapRatio;
            float step = _iconSize + gap;
            float rowY0 = _cardHalfH + _iconSize * (0.5f + OutsidePadRatio);

            FillKeys();
            for (int idx = 0; idx < _keyBuf.Count; idx++)
            {
                var e = _icons[_keyBuf[idx]];
                int row = idx / PerRow;
                int rowCount = Mathf.Min(PerRow, n - row * PerRow);
                int col = idx % PerRow;
                float x = (col - (rowCount - 1) / 2f) * step;
                float y = rowY0 + row * step;
                e.BaseLocal = new Vector3(x, y, 0f);
                if (e.Go != null)
                {
                    e.Go.transform.localPosition = e.BaseLocal;
                    e.Go.transform.localScale = new Vector3(_iconSize, _iconSize, 1f);
                }
                _icons[_keyBuf[idx]] = e;
            }
        }

        void FillKeys()
        {
            _keyBuf.Clear();
            foreach (var k in _icons.Keys) _keyBuf.Add(k);
        }

        GameObject BuildIcon(string statusId)
        {
            var go = new GameObject($"status_{statusId}");
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderFactory.GetSprite(
                "StatusIcons", statusId, ColorOf(statusId), 48);
            renderer.sortingOrder = 30;
            go.transform.localScale = new Vector3(_iconSize, _iconSize, 1f);
            return go;
        }

        static Color ColorOf(string statusId)
        {
            int hash = 0;
            foreach (char c in statusId) hash = hash * 31 + c;
            return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.65f, 0.95f);
        }
    }
}
