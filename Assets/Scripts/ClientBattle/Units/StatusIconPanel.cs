using System.Collections.Generic;
using ClientBattle.Placeholder;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 状态图标面板（client_perform 默认策略·被施加状态类）：
    // - 普通状态：卡牌上方一排小图标，施加即添、消失即移。
    // - 控制类状态（缄默/缴械/犹豫/石化/冥锁/魅惑）：单独的大图标放卡牌中央，
    //   多个控制整体居中排布；放不下时按行折行，保证不管多少图标都能摆开。
    // 图标资源：Resources/ClientBattle/StatusIcons/<status_id>.png → 色块占位。
    // =========================================================================

    public class StatusIconPanel : MonoBehaviour
    {
        static readonly HashSet<string> ControlStatuses = new()
        {
            "silence", "disarm", "hesitation", "petrify", "ming_lock", "charm",
        };

        const float NormalIconSize = 0.28f;
        const float ControlIconSize = 0.55f;
        const int ControlPerRow = 3;          // 中央区每行最多 3 个，超出折行

        readonly Dictionary<string, GameObject> _normalIcons = new();   // status_id → icon
        readonly Dictionary<string, GameObject> _controlIcons = new();

        public static bool IsControl(string statusId) => ControlStatuses.Contains(statusId);

        /// <summary>开战前按战报出现的状态 id 预生成图标纹理（占位 sprite 建纹理
        /// 有一次性成本，挪出战斗热路径；真实资源则顺带完成 Resources 加载）。</summary>
        public static void PrewarmIcon(string statusId) =>
            PlaceholderFactory.GetSprite("StatusIcons", statusId, ColorOf(statusId), 48);

        public void AddStatus(string statusId)
        {
            var table = IsControl(statusId) ? _controlIcons : _normalIcons;
            if (table.ContainsKey(statusId)) return; // 刷新/叠层不重复添图标
            table[statusId] = BuildIcon(statusId, IsControl(statusId));
            Relayout();
        }

        public void RemoveStatus(string statusId)
        {
            var table = IsControl(statusId) ? _controlIcons : _normalIcons;
            if (!table.TryGetValue(statusId, out var icon)) return;
            table.Remove(statusId);
            Destroy(icon);
            Relayout();
        }

        public void Clear()
        {
            foreach (var icon in _normalIcons.Values) Destroy(icon);
            foreach (var icon in _controlIcons.Values) Destroy(icon);
            _normalIcons.Clear();
            _controlIcons.Clear();
        }

        // ---------------------------------------------------------- 布局

        void Relayout()
        {
            // 普通状态：卡牌上方一排，从左到右
            int i = 0;
            foreach (var icon in _normalIcons.Values)
            {
                icon.transform.localPosition = new Vector3(
                    -0.7f + i * (NormalIconSize + 0.06f), 1.28f, 0f);
                i++;
            }

            // 控制状态：卡牌中央，整体居中；每行 ControlPerRow 个，多行向下扩
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

        GameObject BuildIcon(string statusId, bool isControl)
        {
            var go = new GameObject($"status_{statusId}");
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderFactory.GetSprite(
                "StatusIcons", statusId, ColorOf(statusId), 48);
            renderer.sortingOrder = isControl ? 30 : 20;
            float s = isControl ? ControlIconSize : NormalIconSize;
            go.transform.localScale = new Vector3(s, s, 1f);
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
