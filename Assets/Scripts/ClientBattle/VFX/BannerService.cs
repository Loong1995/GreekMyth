using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】BannerService：顶部横幅 + 无主体 cut-in 的 OnGUI 文字回退。
    //
    // - Set(text)：常驻顶部横幅（回合号/单挑/终局等），空串清除。
    // - ShowTextCutIn(text, holdSeconds)：金字大标横幅（战术变更等无立绘主体
    //   的 cut-in 回退），到时自动淡出。
    // - 战后结算面板（Test/SettlementPanel）可见时本服务不绘制（原 Runner 行为）。
    // - 渲染仍为 OnGUI（本次重构不换 Canvas）；字号按屏高缩放（800px 基准）。
    // =========================================================================

    public class BannerService : MonoBehaviour
    {
        public static BannerService Instance { get; private set; }

        string _banner = "";
        string _cutInText = "";
        float _cutInUntil; // Time.time 到期即淡出

        GUIStyle _bannerStyle, _cutInStyle; // 缓存：OnGUI 每帧 new GUIStyle 会产生 GC 压力

        public static BannerService Ensure()
        {
            if (Instance == null)
                Instance = new GameObject("BannerService").AddComponent<BannerService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>设置顶部横幅（空串清除）。</summary>
        public void Set(string text) => _banner = text ?? "";

        /// <summary>无主体 cut-in 的文字横幅回退（金字 + 淡出）。</summary>
        public void ShowTextCutIn(string text, float holdSeconds)
        {
            _cutInText = text;
            _cutInUntil = Time.time + holdSeconds;
        }

        public void Clear()
        {
            _banner = "";
            _cutInText = "";
            _cutInUntil = 0f;
        }

        void OnGUI()
        {
            if (Test.SettlementPanel.Visible) return; // 结算面板独占屏幕
            float k = Mathf.Max(1f, Screen.height / 800f);
            DrawCutIn(k);
            if (string.IsNullOrEmpty(_banner)) return;
            _bannerStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            var style = _bannerStyle;
            style.fontSize = Mathf.RoundToInt(26 * k);
            // 阴影+白字双绘：任何底色（无色黑/白图背景）都可读
            var rect = new Rect(0, 24 * k, Screen.width, 40 * k);
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), _banner, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, _banner, style);
        }

        void DrawCutIn(float k)
        {
            if (Time.time >= _cutInUntil || string.IsNullOrEmpty(_cutInText)) return;
            float alpha = Mathf.Clamp01((_cutInUntil - Time.time) / 0.4f); // 末段淡出
            _cutInStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            };
            _cutInStyle.fontSize = Mathf.RoundToInt(34 * k);
            var rect = new Rect(0, Screen.height * 0.30f, Screen.width, 50 * k);
            _cutInStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);
            GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), _cutInText, _cutInStyle);
            _cutInStyle.normal.textColor = new Color(1f, 0.9f, 0.35f, alpha); // 金字
            GUI.Label(rect, _cutInText, _cutInStyle);
        }
    }
}
