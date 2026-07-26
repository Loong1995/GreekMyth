using System.IO;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // RFX4 粉红材质 = 未导官方 URP Patch（Built-in Particles/Standard 在 URP 下洋红）。
    // 官方 Readme：必须导入 HDRP and URP patches/URP patch.unitypackage。
    // =========================================================================

    public static class Rfx4UrpPatchImporter
    {
        const string RfxRoot = "Assets/KriptoFX/Realistic Effects Pack v4";
        const string UrpPatch = RfxRoot + "/HDRP and URP patches/URP patch.unitypackage";

        [MenuItem("GreekMyth/RFX4/导入 URP Patch（修粉红）")]
        public static void ImportUrpPatch()
        {
            if (!File.Exists(UrpPatch))
            {
                Debug.LogError("[RFX4] 找不到 URP patch：" + UrpPatch);
                return;
            }

            AssetDatabase.ImportPackage(UrpPatch, interactive: false);
            Debug.Log("[RFX4] 已请求导入 URP patch。导入完成后重开「RFX4 可靠预览」验收 Effect22。");
        }

        [MenuItem("GreekMyth/RFX4/诊断粉红材质（Built-in shader）")]
        public static void DiagnosePinkMaterials()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { RfxRoot + "/Effects/Materials" });
            int bad = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                var name = mat.shader.name ?? "";
                // Built-in 粒子/标准在 URP 下必粉；Hidden/InternalErrorShader 亦粉。
                bool suspect = name.StartsWith("Particles/")
                               || name == "Standard"
                               || name == "Standard (Specular setup)"
                               || name == "Mobile/Diffuse"
                               || name == "Legacy Shaders/Diffuse"
                               || name.Contains("InternalErrorShader")
                               || name == "Hidden/InternalErrorShader";
                if (!suspect) continue;
                bad++;
                Debug.LogWarning($"[RFX4粉红?] {path}  → shader=\"{name}\"");
            }

            Debug.Log(bad == 0
                ? "[RFX4] 诊断：Effects/Materials 下未发现 Built-in/错误 shader 材质。"
                : $"[RFX4] 诊断：发现 {bad} 个疑似粉红材质。请菜单「GreekMyth/RFX4/导入 URP Patch（修粉红）」。");
        }
    }
}
