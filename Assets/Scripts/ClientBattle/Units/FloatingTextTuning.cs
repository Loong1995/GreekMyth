using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【B4】飘字调参 ScriptableObject：字体/字号/颜色/上浮曲线全参数收口，
    // Inspector 实时改、Play 模式立即生效（服务每条飘字读本配置）。
    //
    // - 资产路径：Resources/ClientBattle/FloatingTextTuning.asset；
    //   缺失时用代码默认值（与历史硬编码观感一致）。
    // - 字体：填 Resources/ClientBattle/Fonts/ 下的字体资产名（免费商用建议：
    //   思源黑体 SourceHanSans / 得意黑 SmileySans / 站酷高端黑 ZCOOL）；
    //   留空用 Unity 内置默认字体。
    // - 操作文档：docs/client/floating_text_tuning.md。
    // =========================================================================

    [CreateAssetMenu(menuName = "GreekMyth/Floating Text Tuning", fileName = "FloatingTextTuning")]
    public class FloatingTextTuning : ScriptableObject
    {
        [Header("字体（Resources/ClientBattle/Fonts/<名>；空=内置默认）")]
        public string FontName = "";
        [Tooltip("字形像素尺寸（动态字体纹理档位，改后自动重预热）")]
        public int FontSize = 48;
        [Tooltip("世界空间基准缩放（0.1 = 历史默认观感）")]
        public float BaseScale = 0.1f;

        [Header("动画（上浮曲线：OutCubic 上浮 + InQuad 淡出）")]
        public float FloatDuration = 1.1f;
        public float RiseDistance = 0.9f;
        [Tooltip("同单位连续飘字的纵向错位间距")]
        public float StackSpacing = 0.35f;

        [Header("倍率")]
        public float CritScale = 1.45f;
        public float HealCritScale = 1.35f;

        [Header("颜色（按结算类别）")]
        public Color PhysicalDamage = new(1f, 0.35f, 0.3f);
        public Color MagicDamage = new(0.7f, 0.45f, 1f);
        public Color TrueDamage = new(1f, 0.95f, 0.4f);
        public Color Mitigation = new(0.7f, 0.85f, 1f);   // 格挡/闪避/反弹
        public Color Heal = new(0.4f, 1f, 0.5f);
        public Color StatusGain = new(0.55f, 0.8f, 1f);
        public Color StatusLose = new(0.7f, 0.7f, 0.7f);
        public Color AttrUp = new(1f, 0.85f, 0.4f);
        public Color AttrDown = new(0.85f, 0.5f, 0.85f);

        static FloatingTextTuning _loaded;

        public static FloatingTextTuning LoadOrDefault()
        {
            if (_loaded != null) return _loaded;
            _loaded = Resources.Load<FloatingTextTuning>("ClientBattle/FloatingTextTuning");
            if (_loaded == null) _loaded = CreateInstance<FloatingTextTuning>();
            return _loaded;
        }

        /// <summary>解析字体资产（缺失回退内置默认，不报错）。</summary>
        public Font ResolveFont()
        {
            if (!string.IsNullOrEmpty(FontName))
            {
                var font = Resources.Load<Font>($"ClientBattle/Fonts/{FontName}");
                if (font != null) return font;
                Debug.LogWarning($"[ClientBattle] 飘字字体未找到：Fonts/{FontName}，回退内置默认");
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
