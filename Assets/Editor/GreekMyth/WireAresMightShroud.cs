using System.IO;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 罩身厂包件默认接线：完整件原样拷入 Resources，**不删除任何子节点**。
    // Magic Pack EffectN 等同构件一律走本入口；裁层（去石块等）只允许在
    // 各技能自己的 Wire* 里单独列名单，禁止写进 VfxShroudFitter/Follower。
    // =========================================================================

    public static class WireShroudEffect
    {
        /// <summary>完整件 Regular 拷贝。stripNameContains 非空时仅删除名称命中的
        /// **一级子节点**（个案名单，默认应传 null）。</summary>
        public static bool CopyFull(string sourcePath, string destPath, string prefabName,
                                    string[] stripNameContains = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(
                destPath.Replace("Assets/", Application.dataPath + "/")));

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (src == null)
            {
                Debug.LogError("[WireShroud] 源缺失：" + sourcePath);
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<Object>(destPath) != null)
                AssetDatabase.DeleteAsset(destPath);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(src);
            instance.name = prefabName;
            // 解开嵌套，存成独立 Regular（否则会变成缺层的 Variant）
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                              InteractionMode.AutomatedAction);

            if (stripNameContains != null && stripNameContains.Length > 0)
            {
                for (int i = instance.transform.childCount - 1; i >= 0; i--)
                {
                    var child = instance.transform.GetChild(i).gameObject;
                    foreach (var key in stripNameContains)
                    {
                        if (string.IsNullOrEmpty(key)) continue;
                        if (child.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Object.DestroyImmediate(child);
                            break;
                        }
                    }
                }
            }

            PrefabUtility.SaveAsPrefabAsset(instance, destPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
            var type = PrefabUtility.GetPrefabAssetType(saved);
            Debug.Log($"[WireShroud] {destPath} ← {sourcePath} type={type} " +
                      $"children={saved.transform.childCount} " +
                      $"(默认完整件；strip={FormatStrip(stripNameContains)})");
            return type == PrefabAssetType.Regular;
        }

        static string FormatStrip(string[] keys)
        {
            if (keys == null || keys.Length == 0) return "无";
            return string.Join(",", keys);
        }
    }

    /// <summary>战神之勇：Effect31 → shroud_ares_might，**完整件不裁层**。</summary>
    public static class WireAresMightShroud
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect31.prefab";
        const string Dest = "Assets/Resources/ClientBattle/VFX/shroud_ares_might.prefab";

        [MenuItem("GreekMyth/Magic Pack/接线战神之勇罩身（Effect31→shroud_ares_might）")]
        public static void Wire()
        {
            if (!WireShroudEffect.CopyFull(Src, Dest, "shroud_ares_might", stripNameContains: null))
                Debug.LogError("[AresShroud] 落盘失败");
        }
    }
}
