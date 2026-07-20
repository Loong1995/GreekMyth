using System.Collections.Generic;
using ClientBattle.Placeholder;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 状态图标面板：仅控制类（缄默/缴械/犹豫/石化/冥锁/魅惑）卡中央大图标。
    // 常规状态上方小图标已取消（2026-07-20）——靠光环 / 飘字即可；勿再上传普通 status 图。
    // 图标资源：Resources/ClientBattle/StatusIcons/<status_id>.png → 色块占位。
    // =========================================================================

    public class StatusIconPanel : MonoBehaviour
    {
        static readonly HashSet<string> ControlStatuses = new()
        {
            "silence", "disarm", "hesitation", "petrify", "ming_lock", "charm",
        };

        const float ControlIconSize = 0.55f;
        const int ControlPerRow = 3;          // 中央区每行最多 3 个，超出折行

        readonly Dictionary<string, GameObject> _controlIcons = new();

        public static bool IsControl(string statusId) => ControlStatuses.Contains(statusId);

        /// <summary>开战前仅预热控制类图标（常规小图标已关闭）。</summary>
        public static void PrewarmIcon(string statusId)
        {
            if (!IsControl(statusId)) return;
            PlaceholderFactory.GetSprite("StatusIcons", statusId, ColorOf(statusId), 48);
        }

        public void AddStatus(string statusId)
        {
            if (!IsControl(statusId)) return; // 常规状态不显示上方小图标
            if (_controlIcons.ContainsKey(statusId)) return;
            _controlIcons[statusId] = BuildIcon(statusId);
            Relayout();
        }

        public void RemoveStatus(string statusId)
        {
            if (!_controlIcons.TryGetValue(statusId, out var icon)) return;
            _controlIcons.Remove(statusId);
            Destroy(icon);
            Relayout();
        }

        public void Clear()
        {
            foreach (var icon in _controlIcons.Values) Destroy(icon);
            _controlIcons.Clear();
        }

        void Relayout()
        {
            int n = _controlIcons.Count, idx = 0;
            foreach (var icon in _controlIcons.Values)
            {
                int row = idx / ControlPerRow;
                int rowCount = Mathf.Min(ControlPerRow, n - row * ControlPerRow);
                int col = idx % ControlPerRow;
                float x = (col - (rowCount - 1) / 2f) * (ControlIconSize + 0.08f);
                float y = 0.15f - row * (ControlIconSize + 0.08f);
                icon.transform.localPosition = new Vector3(x, y, 0f);
                idx++;
            }
        }

        GameObject BuildIcon(string statusId)
        {
            var go = new GameObject($"status_{statusId}");
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderFactory.GetSprite(
                "StatusIcons", statusId, ColorOf(statusId), 48);
            renderer.sortingOrder = 30;
            go.transform.localScale = new Vector3(ControlIconSize, ControlIconSize, 1f);
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
