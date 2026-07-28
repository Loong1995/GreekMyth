using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    /// <summary>雷霆神谕场域：画廊 2/8 · 11/61 ＝ Magic Effect19
    /// → <c>ambient_thunder_storm</c>（勿与命中件 <c>hit_lightning</c>＝同原料碰撞子件混淆）。
    ///
    /// 【为什么不是罩身】Effect19 的护罩壳按单人身位做，缩到一张卡上几乎看不见
    /// （壳是屏幕抓帧折射，低频舞台不可见，P-74/P-77）；真正有观感的是那层
    /// 世界空间游离电弧。故改判为**场域氛围件**：摘壳留电弧，源点钉主战场地面
    /// 中心铺满全场（`VfxUsage.AmbientField`）。挂载走 UnitAuraService 的
    /// `ambient_` 分流（全场按 key 去重）。状态 <c>thunder</c> 持有期间常显。</summary>
    public static class WireThunderStorm
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect19.prefab";
        const string Key = "ambient_thunder_storm";
        const string Dest = VfxPackStandardizer.VfxDir + "/" + Key + ".prefab";

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Dest) != null) return;
                Debug.LogWarning("[ThunderStorm] ambient_thunder_storm 缺失，自动按 AmbientField 流水线补齐…");
                Wire();
            };
        }

        [MenuItem("GreekMyth/Magic Pack/接线雷霆神谕场域（画廊2/8·11/61 Effect19→ambient_thunder_storm）")]
        public static void Wire()
        {
            if (!VfxPackStandardizer.Standardize(Src, Key, VfxUsage.AmbientField))
                Debug.LogError("[ThunderStorm] 落盘失败");
        }
    }
}
