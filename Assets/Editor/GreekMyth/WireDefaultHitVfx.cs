using UnityEditor;
using UnityEngine;
using ClientBattle.VFX;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 默认/巨伤命中特效接线（解析逻辑见 SkillPerformance.ResolveHitKey，
    // 查表文档 docs/client/vfx_config_index.md）。
    //
    // 物理默认 hit_sword ← Cartoon Coffee Impact_Cut_V1（定稿；
    //                      Cone 刀光≈直线横切，金橙，非环形）
    // 魔法默认 hit_petrify ← CFXR Hit Magical Stars (Pink)
    //                      （粉紫星芒+刺环+拖尾，比 Hit Light 更密、更魔幻）
    // 神谕 hit_wave / 巨伤 hit_massive 另线。
    //
    // 卡面中心播：SettleDamage 走 PlayOn(卡根)。定径倍率 HitCircleFitFactor。
    // =========================================================================

    public static class WireDefaultHitVfx
    {
        const string PhysSrc =
            "Assets/Cartoon Coffee/2D Slash VFX/Prefabs/Bursts/Impact_Cut_V1.prefab";
        const string MagicSrc =
            "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Magic Misc/Variants/CFXR3 Hit Magical Stars (Pink).prefab";
        const string PhysKey = "hit_sword";
        const string MagicKey = "hit_petrify";

        /// <summary>投影圆定径后再乘此倍率。</summary>
        public const float HitCircleFitFactor = 2.5f;

        const string MassiveSrc =
            "Assets/KriptoFX/Realistic Effects Pack v4/Effects/Prefabs/EffectParts/CollisionEffects/Effect15_Collision.prefab";
        const string MassiveKey = "hit_massive";

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                bool needPhys = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{VfxPackStandardizer.VfxDir}/{PhysKey}.prefab") == null;
                bool needMagic = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{VfxPackStandardizer.VfxDir}/{MagicKey}.prefab") == null;
                if (needPhys || needMagic)
                {
                    Debug.LogWarning("[WireHit] 默认命中件缺失，自动标准化补齐…");
                    WirePhysAndMagic();
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{VfxPackStandardizer.VfxDir}/{MassiveKey}.prefab") == null)
                {
                    Debug.LogWarning("[WireHit] 巨伤命中件缺失，自动标准化补齐…");
                    WireMassive();
                }
            };
        }

        [MenuItem("GreekMyth/特效/接线 物理+魔法默认命中（Impact_Cut / Magical Stars Pink → hit_sword/hit_petrify）")]
        public static void WirePhysAndMagic()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[WireHit] 拒绝在 Play 模式下接线。请先退出 Play 再点本菜单。");
                return;
            }
            bool okP = VfxPackStandardizer.Standardize(PhysSrc, PhysKey, VfxUsage.Anchor);
            bool okM = VfxPackStandardizer.Standardize(MagicSrc, MagicKey, VfxUsage.Anchor);
            if (okP) ApplyHitFitFactor(PhysKey, HitCircleFitFactor);
            if (okM) ApplyHitFitFactor(MagicKey, HitCircleFitFactor);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WireHit] hit_sword←Impact_Cut_V1 {(okP ? "OK" : "失败")}；"
                      + $"hit_petrify←CFXR Magical Stars Pink {(okM ? "OK" : "失败")}；"
                      + $"VfxCircleFit.Factor={HitCircleFitFactor}。");
        }

        [MenuItem("GreekMyth/特效/接线 巨伤命中（hit_massive ← RFX4 Effect15_Collision）")]
        public static void WireMassive()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[WireHit] 拒绝在 Play 模式下接线。请先退出 Play 再点本菜单。");
                return;
            }
            bool ok = VfxPackStandardizer.Standardize(MassiveSrc, MassiveKey, VfxUsage.Anchor);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WireHit] hit_massive 落盘并验证：{(ok ? "OK" : "失败，见上方日志")}。"
                      + "触发条件＝CutInPlanner.IsHighDamage（与「重创」横幅同判据）。");
        }

        [MenuItem("GreekMyth/特效/体检 默认命中（物理 hit_sword · 魔法 hit_petrify · 神谕 hit_wave · 巨伤 hit_massive）")]
        public static void Audit()
        {
            int ok = 0, total = 0;
            foreach (var key in new[] { PhysKey, MagicKey, "hit_wave", MassiveKey })
            {
                total++;
                var path = $"{VfxPackStandardizer.VfxDir}/{key}.prefab";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null)
                {
                    ok++;
                    var fit = go.GetComponent<VfxCircleFit>();
                    string fitInfo = fit != null ? $" CircleFit×{fit.Factor:0.##}" : " (无 CircleFit)";
                    Debug.Log($"[WireHit] OK {key}{fitInfo}");
                }
                else
                    Debug.LogError($"[WireHit] 缺失 {path}");
            }
            Debug.Log($"[WireHit] 命中族体检 {ok}/{total}。解析顺序见 ResolveHitKey：巨伤→专配→伤害类型→兜底。");
        }

        static void ApplyHitFitFactor(string key, float factor)
        {
            string path = $"{VfxPackStandardizer.VfxDir}/{key}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fit = root.GetComponent<VfxCircleFit>();
                if (fit == null)
                {
                    fit = root.AddComponent<VfxCircleFit>();
                    fit.Reference = VfxCircleFit.Circle.Projection;
                }
                fit.Factor = factor;
                fit.RescueIfBuried = false;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
