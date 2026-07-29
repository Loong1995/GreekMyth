using UnityEditor;
using UnityEngine;
using ClientBattle.VFX;

namespace GreekMyth.EditorTools
{
    /// <summary>魔法主动默认弹道：Magic Pack <c>Effect1</c> → <c>magic_bolt</c>。
    ///
    /// 走 <see cref="VfxUsage.Projectile"/>：保留母件（rateOverDistance 飞行出图），
    /// **不**改选 Collision（那是定点爆发）。位移归 <c>LaunchProjectile</c>。
    /// </summary>
    public static class WireMagicBolt
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect1.prefab";
        const string Key = "magic_bolt";
        const string Dest = VfxPackStandardizer.VfxDir + "/" + Key + ".prefab";

        /// <summary>投影圆定径后再乘。Effect1 缩到卡尺度后略放大，偏「重」可读。</summary>
        public const float CircleFitFactor = 0.95f;

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Dest) != null) return;
                Debug.LogWarning("[WireMagicBolt] magic_bolt 缺失，自动按 Projectile 流水线补齐…");
                Wire();
            };
        }

        [MenuItem("GreekMyth/Magic Pack/接线魔法默认弹道（Effect1 → magic_bolt）")]
        public static void Wire()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[WireMagicBolt] 拒绝在 Play 模式下接线。请先退出 Play。");
                return;
            }
            if (!VfxPackStandardizer.Standardize(Src, Key, VfxUsage.Projectile))
            {
                Debug.LogError("[WireMagicBolt] 落盘失败");
                return;
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Dest);
            if (go != null)
            {
                var fit = go.GetComponent<VfxCircleFit>();
                if (fit != null)
                {
                    fit.Factor = CircleFitFactor;
                    EditorUtility.SetDirty(go);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[WireMagicBolt] magic_bolt ← Magic Effect1（Projectile）；"
                      + $"VfxCircleFit.Factor={CircleFitFactor}");
        }
    }
}
