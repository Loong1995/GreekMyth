using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    /// <summary>宙斯雷霆神谕**罩身**：画廊 2/8 · 11/61 ＝ Magic Effect19
    /// → <c>shroud_thunder</c>（勿与命中件 <c>hit_lightning</c>＝同原料碰撞子件混淆）。
    ///
    /// 【为什么保留电弧层】罩身流水线默认摘游离 `LightningTrails*`（卡面尺度下
    /// 会糊满视野，P-78）。但这件的主视觉**就是**电弧：摘完只剩一个屏幕折射壳，
    /// 而折射壳在罩身用途下又必须中和（糊卡面，P-77）——两条一起执行的结果是
    /// 「什么都看不见」（实翻过车）。故按 keepLayers 豁免电弧层：
    /// 电弧是 Local 模拟空间，随 `VfxShroudFitter` 的定径一起缩到卡尺度，
    /// 读作"雷缠身"而不是全屏乱电。</summary>
    public static class WireThunderShroud
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect19.prefab";
        const string Key = "shroud_thunder";
        const string Dest = VfxPackStandardizer.VfxDir + "/" + Key + ".prefab";

        /// <summary>豁免裁层：只留**贴着罩面**的壳边环。
        ///
        /// **折射壳 `Shield` 不豁免**：2026-07-28 实测保留它把身后卡面搅糊到"难受"，
        /// 人工否决——P-77 是实测结论不是保守。</summary>
        static readonly string[] Keep = { "Fringe" };

        /// <summary>点名摘掉**往外喷**的层：游离电弧由罩身默认清洗摘（P-78），
        /// 剩下这几层是厂包的一次性爆发（世界空间喷射粒子 / 冲击点 / 落点痕 / 烟），
        /// 默认清洗会把它们提到根下保住「罩身 + 喷射一下」。
        ///
        /// 但本件要的语义是**罩住**，与阿瑞斯战神之勇（`shroud_ares_might`：
        /// 加色壳 + 边环 + 背火，没有任何外喷）对齐：往外喷的雷电线会读成
        /// "这人正在放技能"，而罩身表达的是"这人身上一直挂着雷"——
        /// 常驻态与施放态混淆，是罩身件最常见的观感错位。</summary>
        static readonly string[] Drop = { "Particles", "Point", "Fog", "ImpactDecal" };

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Dest) != null) return;
                Debug.LogWarning("[ThunderShroud] shroud_thunder 缺失，自动按 Shroud 流水线补齐…");
                Wire();
            };
        }

        [MenuItem("GreekMyth/Magic Pack/接线雷霆神谕罩身（画廊2/8·11/61 Effect19→shroud_thunder）")]
        public static void Wire()
        {
            if (!VfxPackStandardizer.Standardize(Src, Key, VfxUsage.Shroud, Keep, Drop))
                Debug.LogError("[ThunderShroud] 落盘失败");
        }
    }
}
