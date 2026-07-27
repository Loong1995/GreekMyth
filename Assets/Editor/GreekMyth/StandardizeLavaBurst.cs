using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // G12 熔岩层标准化：Magic Effects Pack v1 / Effect8 → ground_lava_bloom。
    //
    // 用途：裂地**第 3 发光档**（全局大裂地）叠的熔岩过曝层。三档裂地的裂缝
    // 几何仍由自研三层配方（G4）出，本件只补 Effect8 的「亮」。
    //
    // 【2026-07-27 重做】旧版直接搬 Effect8 母件的 Particles/Trail 等层——
    // 那些层按"移动距离"发射（运载器层），定点摆着**一颗粒子都不出**，
    // 全程只有点光在亮（全量体检抓出）。现改走 VfxPackStandardizer 流水线：
    // 自动改选碰撞爆发子件 Effect8_Collision（真正"很亮"的那次爆炸），
    // 再做本件专属后处理：
    //   · 基准缩放 1.6：全局大裂地按逻辑圆量级，子件原体量是单体爆点；
    //   · 点光强度折半：地面层要「热」不要把卡面 Bloom 爆白；
    //   · 摘 VfxCircleFit：本件尺寸由裂地档位控制，交给 CircleFit 会打架。
    // 地面焦痕贴花（URP 画不出）由自研 GroundCrack.shader 替代，流水线已摘。
    // =========================================================================

    public static class StandardizeLavaBurst
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect8.prefab";
        const string Key = "ground_lava_bloom";
        const string Dest = VfxPackStandardizer.VfxDir + "/" + Key + ".prefab";

        const float BakedScale = 1.6f;
        const float LightIntensityMul = 0.5f;

        [MenuItem("GreekMyth/裂地/G12 标准化熔岩层（Effect8 → ground_lava_bloom）")]
        public static void Build()
        {
            if (!VfxPackStandardizer.Standardize(Src, Key, VfxUsage.Ground)) return;

            var root = PrefabUtility.LoadPrefabContents(Dest);
            try
            {
                root.transform.localScale = Vector3.one * BakedScale;
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    light.intensity *= LightIntensityMul;
                var fit = root.GetComponent<VfxCircleFit>();
                if (fit != null) Object.DestroyImmediate(fit, true);
                PrefabUtility.SaveAsPrefabAsset(root, Dest);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[LavaStd] {Dest}：流水线（原料已改选碰撞子件）+ 基准缩放 {BakedScale}、"
                      + $"灯强 ×{LightIntensityMul}、摘 VfxCircleFit（尺寸归裂地档位）");
        }
    }
}
