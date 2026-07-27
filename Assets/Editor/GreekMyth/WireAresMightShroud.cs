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

    /// <summary>战神之勇罩身：画廊 2/8 · 10/61 ＝ Magic Effect18 完整件
    /// → <c>shroud_ares_might</c>（不裁层）。勿与「件 18/61＝Effect25」混淆。
    /// 挂载仍走 MountShroud + VfxShroudFollower（投影圆定径）。</summary>
    public static class WireAresMightShroud
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect18.prefab";
        const string Dest = "Assets/Resources/ClientBattle/VFX/shroud_ares_might.prefab";

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            // 只在缺件时补；原料换件须点菜单强制重拷（避免每次域重载盖掉本地微调）。
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Dest) != null) return;
                Debug.LogWarning("[AresShroud] shroud_ares_might 缺失，自动 Effect18 完整件补齐…");
                Wire();
            };
        }

        [MenuItem("GreekMyth/Magic Pack/接线战神之勇罩身（画廊2/8·10/61 Effect18→shroud_ares_might）")]
        public static void Wire()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[AresShroud] 拒绝在 Play 模式下接线。请先退出 Play 再点本菜单。");
                return;
            }
            if (!WireShroudEffect.CopyFull(Src, Dest, "shroud_ares_might", stripNameContains: null))
                Debug.LogError("[AresShroud] 落盘失败");
        }
    }
}
