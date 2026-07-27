using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 默认/巨伤命中特效接线（解析逻辑见 SkillPerformance.ResolveHitKey，
    // 查表文档 docs/client/vfx_config_index.md）。
    //
    // 一、指向已有标准件（画廊 [1/8] 我方标准件，只体检不落盘）：
    //   魔法默认  件 41/61 → hit_petrify
    //   物理默认  件 45/61 → hit_sword
    //   神谕伤害  件 47/61 → hit_wave（OracleDefault.HitKey）
    //
    // 二、需从厂包标准化落盘（走 VfxPackStandardizer）：
    //   巨伤 hit_massive ← 画廊 3/8（RFX4）件 7/54＝Effect15。母件是
    //   PhysicsMotion 运载器（P-68），定点用途取碰撞子件 Effect15_Collision。
    // =========================================================================

    public static class WireDefaultHitVfx
    {
        static readonly string[] RequiredKeys = { "hit_petrify", "hit_sword", "hit_wave" };

        const string MassiveSrc =
            "Assets/KriptoFX/Realistic Effects Pack v4/Effects/Prefabs/EffectParts/CollisionEffects/Effect15_Collision.prefab";
        const string MassiveKey = "hit_massive";

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                foreach (var key in RequiredKeys)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"{VfxPackStandardizer.VfxDir}/{key}.prefab") != null) continue;
                    Debug.LogError(
                        $"[WireHit] 默认命中标准件缺失：{key}.prefab。"
                        + "该 key 应已在 Resources（画廊 1/8），勿从厂包覆盖。");
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"{VfxPackStandardizer.VfxDir}/{MassiveKey}.prefab") == null)
                {
                    Debug.LogWarning("[WireHit] 巨伤命中件缺失，自动标准化补齐…");
                    WireMassive();
                }
            };
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
                      + "触发条件＝CutInPolicy.IsHighDamage（与「重创」横幅同判据）。");
        }

        [MenuItem("GreekMyth/特效/体检 默认命中（物理 hit_sword · 魔法 hit_petrify · 神谕 hit_wave · 巨伤 hit_massive）")]
        public static void Audit()
        {
            int ok = 0, total = 0;
            foreach (var key in new[] { "hit_petrify", "hit_sword", "hit_wave", MassiveKey })
            {
                total++;
                var path = $"{VfxPackStandardizer.VfxDir}/{key}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    ok++;
                    Debug.Log($"[WireHit] OK {key}");
                }
                else
                    Debug.LogError($"[WireHit] 缺失 {path}");
            }
            Debug.Log($"[WireHit] 命中族体检 {ok}/{total}。解析顺序见 ResolveHitKey：巨伤→专配→伤害类型→兜底。");
        }
    }
}
