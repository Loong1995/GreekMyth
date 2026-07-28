using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    /// <summary>调试时切特效画质档：`GreekMyth/特效/画质档`。
    ///
    /// 存 EditorPrefs 而不是改代码字段，理由有二：改代码要等编译、且会被人不小心
    /// 提交进版本库（"我这儿明明是低端档"就是这么来的）；EditorPrefs 是本机偏好，
    /// 域重载后由 `[InitializeOnLoadMethod]` 自动回填 `VfxQuality.EditorTier`。
    ///
    /// Play 中切档立即对**下次启用**的特效生效（`VfxTierScale.OnEnable` 会对拍），
    /// 已经在播的那一份不会中途变——想看完整对比就重开一场。
    /// **镜头层例外**：Bloom 是全屏后处理，切档即刻重写、下一帧就能看出差别。</summary>
    static class VfxQualityMenu
    {
        const string Pref = "GreekMyth.VfxEditorTier";
        const string Root = "GreekMyth/特效/画质档/";

        [InitializeOnLoadMethod]
        static void Restore() => VfxQuality.EditorTier = (VfxTier)EditorPrefs.GetInt(Pref, (int)VfxTier.Mid);

        [MenuItem(Root + "低端（Low）")] static void Low() => Set(VfxTier.Low);
        [MenuItem(Root + "中端（Mid，默认）")] static void Mid() => Set(VfxTier.Mid);
        [MenuItem(Root + "高端 / PC（High）")] static void High() => Set(VfxTier.High);

        [MenuItem(Root + "低端（Low）", true)] static bool LowCheck() => Check(VfxTier.Low);
        [MenuItem(Root + "中端（Mid，默认）", true)] static bool MidCheck() => Check(VfxTier.Mid);
        [MenuItem(Root + "高端 / PC（High）", true)] static bool HighCheck() => Check(VfxTier.High);

        [MenuItem(Root + "打印当前判据")]
        static void Print() => Debug.Log("[VfxQuality] " + VfxQuality.Describe());

        static void Set(VfxTier tier)
        {
            EditorPrefs.SetInt(Pref, (int)tier);
            VfxQuality.EditorTier = tier;
            VfxQuality.Override(tier);   // Play 中即时生效，不必退出重进
            BattlePostFx.Apply();        // 镜头层（Bloom）当帧重写；未在 Play 时静默跳过
            Debug.Log($"[VfxQuality] 编辑器画质档 → {tier}（镜头层立即生效；逐件缩放待下次启用）");
        }

        static bool Check(VfxTier tier)
        {
            Menu.SetChecked(Root + MenuNameOf(tier), VfxQuality.EditorTier == tier);
            return true;
        }

        static string MenuNameOf(VfxTier tier) => tier switch
        {
            VfxTier.Low => "低端（Low）",
            VfxTier.High => "高端 / PC（High）",
            _ => "中端（Mid，默认）",
        };
    }
}
