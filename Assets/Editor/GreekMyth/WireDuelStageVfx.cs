using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 单挑舞台厂包特效的**接线清单**。标准化本体在 VfxPackStandardizer
    // （唯一流水线，pack 无关）；本文件只登记「哪件 → 哪个 key → 什么用途」。
    //
    // 点名来源（画廊序号 → 实际 prefab，用 battle/tools/_gallery_index_dump.py 复算）：
    //   包 3/8 RFX4          件 19/54  Effect28 → cast_duel_launch     出阵地面（定位圆）
    //   包 3/8 RFX4          件 15/54  Effect23 → aura_duel_victory    加冕卡面
    //                                                 （兼：出阵双方卡面追加，画廊 1/8 件 8/60）
    //   包 2/8 Magic Pack v1 件 32/61  Effect8  → ground_duel_defeat   溃败地面（定位圆）
    //   同原料 Effect8               → aura_duel_defeat               溃败卡面追加
    //                                                 （画廊 1/8 件 32/60 观感，Anchor 不定地）
    //
    // Effect23 / Effect8 是投射物运载器，流水线会自动改选其碰撞爆发子件。
    // Effect8 地面焦痕贴花（URP 画不出）由 GroundCrackService.PlayHit 替代。
    //
    // 接线端：key 写在 StagePerformanceConfig.Duel*VfxKey，由 VFX/DuelStage.cs 读取。
    // =========================================================================

    public static class WireDuelStageVfx
    {
        const string Rfx4 = "Assets/KriptoFX/Realistic Effects Pack v4/Effects/Prefabs/Effects";
        const string Magic = "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects";

        static readonly (string Src, string Key, VfxUsage Usage)[] Items =
        {
            ($"{Rfx4}/Effect28.prefab", "cast_duel_launch", VfxUsage.Anchor),
            ($"{Rfx4}/Effect23.prefab", "aura_duel_victory", VfxUsage.Anchor),
            ($"{Magic}/Effect8.prefab", "ground_duel_defeat", VfxUsage.Ground),
            // 同原料、卡面用途：挂在败者卡上时不能带 VfxGroundLayer（会压到卡下看不见）
            ($"{Magic}/Effect8.prefab", "aura_duel_defeat", VfxUsage.Anchor),
        };

        /// <summary>自愈：编辑器每次加载检查清单件是否在盘上，缺了就自动接线。
        /// Play 模式跳过（流水线也会拒绝）。</summary>
        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                foreach (var (_, key, _) in Items)
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"{VfxPackStandardizer.VfxDir}/{key}.prefab") != null) continue;
                    Debug.LogWarning($"[WireDuel] 标准件缺失（{key} 等），自动接线补齐…");
                    Wire();
                    return;
                }
            };
        }

        [MenuItem("GreekMyth/特效/接线 单挑三件（出阵·加冕·溃败）")]
        public static void Wire()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[WireDuel] 拒绝在 Play 模式下接线。请先退出 Play 再点本菜单。");
                return;
            }

            int ok = 0;
            foreach (var (src, key, usage) in Items)
                if (VfxPackStandardizer.Standardize(src, key, usage)) ok++;

            AssetDatabase.SaveAssets();
            Debug.Log($"[WireDuel] 落盘并验证 {ok}/{Items.Length} 件。"
                      + "key 已由 StagePerformanceConfig.Duel*VfxKey 引用。"
                      + "验收：跑一次含单挑的战报，确认无品红、各件各就各位。");
        }
    }
}
