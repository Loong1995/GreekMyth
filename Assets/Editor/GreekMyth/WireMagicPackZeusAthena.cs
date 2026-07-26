using System.IO;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 将 Magic Effects Pack 1 接到宙斯命中 / 雅典娜圣盾（Resources variant）。
    // 竖雷几何仍 DR；禁 RFX4。
    // =========================================================================

    public static class WireMagicPackZeusAthena
    {
        const string MagicRoot = "Assets/KriptoFX/Magic Effects Pack v1";
        const string VfxDir = "Assets/Resources/ClientBattle/VFX";
        const string UrpPatch = MagicRoot + "/HDRP and URP patches/URP patch.unitypackage";

        const string SrcHitLightning = MagicRoot + "/Prefabs/EffectParts/Effect19_Collision.prefab";
        const string SrcAresMight = MagicRoot + "/Prefabs/Effects/Effect18.prefab";
        const string SrcShieldHit = MagicRoot + "/Prefabs/EffectParts/Effect17_Collision.prefab";
        // 地面裂地已迁出本工具：三档裂地统一由 GroundCrackComposer（G4）产出，
        // 碎块从舞台底图现切以保证与地面同色。此处不得再写 ground_* key，
        // 否则会覆盖组合器输出、把厂包深色岩石带回来（风格割裂）。

        [MenuItem("GreekMyth/Magic Pack/导入 URP Patch（Magic Pack 1）")]
        public static void ImportUrpPatch()
        {
            if (!File.Exists(UrpPatch))
            {
                Debug.LogError("[MagicWire] 找不到 URP patch：" + UrpPatch);
                return;
            }
            AssetDatabase.ImportPackage(UrpPatch, false);
            Debug.Log("[MagicWire] 已请求导入 Magic Pack 1 URP patch。");
        }

        [MenuItem("GreekMyth/Magic Pack/接线宙斯命中+雅典娜反制+战神之勇环")]
        public static void WireZeusAthena()
        {
            Directory.CreateDirectory(VfxDir.Replace("Assets/", Application.dataPath + "/").Replace('/', Path.DirectorySeparatorChar));

            WriteVariant(SrcHitLightning, VfxDir + "/hit_lightning.prefab", "hit_lightning", 0.32f);
            WriteVariant(SrcShieldHit, VfxDir + "/hit_shield_counter.prefab", "hit_shield_counter", 0.38f);
            WriteVariant(SrcAresMight, VfxDir + "/aura_ares_might.prefab", "aura_ares_might", 0.22f);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MagicWire] 已写入 hit_lightning / hit_shield_counter / aura_ares_might ← Magic Pack 1。");
        }

        [MenuItem("GreekMyth/Magic Pack/一键：URP Patch + 宙斯雅典娜接线")]
        public static void ImportAndWire()
        {
            ImportUrpPatch();
            EditorApplication.delayCall += WireZeusAthena;
        }

        static void WriteVariant(string sourcePath, string destPath, string name, float scale)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (src == null)
            {
                Debug.LogError("[MagicWire] 源缺失：" + sourcePath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(src);
            instance.name = name;
            instance.transform.localScale = Vector3.one * scale;

            // 卡面层：关掉过亮点光，避免 Bloom 爆白
            foreach (var light in instance.GetComponentsInChildren<Light>(true))
                light.enabled = false;

            PrefabUtility.SaveAsPrefabAsset(instance, destPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"[MagicWire] {destPath} ← {sourcePath} scale={scale}");
        }
    }
}
