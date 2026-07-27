using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    /// <summary>战神之勇罩身：画廊 2/8 · 10/61 ＝ Magic Effect18
    /// → <c>shroud_ares_might</c>（勿与「件 18/61＝Effect25」混淆）。
    ///
    /// 2026-07-27 改走标准化流水线 <see cref="VfxPackStandardizer"/>（Shroud 用途）：
    /// 原「完整件原样拷贝」（WireShroudEffect.CopyFull，已删）保留了两层屏幕
    /// 折射（Shield/Distortion）、死贴花、音频/灯光曲线与 PerPlatformSettings，
    /// 折射层罩在卡前把卡面整块折糊（P-77），PerPlatformSettings 还会在真机
    /// 运行期偷降发射率——「与画廊预览不一样」的两大来源。
    /// 挂载仍走 MountShroud + VfxShroudFollower（VfxShroudFitter 定径）。</summary>
    public static class WireAresMightShroud
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect18.prefab";
        const string Key = "shroud_ares_might";
        const string Dest = VfxPackStandardizer.VfxDir + "/" + Key + ".prefab";

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            // 只在缺件时补；原料换件须点菜单强制重跑。
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Dest) != null) return;
                Debug.LogWarning("[AresShroud] shroud_ares_might 缺失，自动按 Shroud 流水线补齐…");
                Wire();
            };
        }

        [MenuItem("GreekMyth/Magic Pack/接线战神之勇罩身（画廊2/8·10/61 Effect18→shroud_ares_might）")]
        public static void Wire()
        {
            if (!VfxPackStandardizer.Standardize(Src, Key, VfxUsage.Shroud))
                Debug.LogError("[AresShroud] 落盘失败");
        }
    }
}
