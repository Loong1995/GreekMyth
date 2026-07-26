using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 厂包接件体检（docs/client/vfx_pack_integration.md §四）。
    //
    // 每接一个厂包件都要过一遍：靠肉眼在 Inspector 里翻层是不可能翻全的
    // （Effect8_Collision 一件就有 9 层、18000 行 YAML）。本工具对
    // Resources/ClientBattle/VFX 下的自有 prefab 做静态检查，输出违规清单。
    //
    // 检查项与判据全部来自实测踩坑，改判据前先读 pitfalls P-32~P-35。
    // =========================================================================

    public static class VfxPackAudit
    {
        const string VfxDir = "Assets/Resources/ClientBattle/VFX";

        /// <summary>Built-in 管线专属、URP 下渲成品红或错位的 shader（P-33）。</summary>
        static readonly string[] BlockedShaders =
        {
            "KriptoFX/RFX1/Decal",
            "KriptoFX/RFX4/Decal",
        };

        /// <summary>Built-in 专属组件：URP 下 Projector 已弃用。</summary>
        static readonly string[] BlockedComponents =
        {
            "Projector",
        };

        [MenuItem("GreekMyth/特效/体检 全量 VFX prefab")]
        public static void AuditAll()
        {
            var report = new StringBuilder();
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { VfxDir });
            int bad = 0;
            foreach (var guid in guids.OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var issues = Inspect(prefab);
                if (issues.Count == 0) continue;
                bad++;
                report.AppendLine($"■ {System.IO.Path.GetFileNameWithoutExtension(path)}");
                foreach (var issue in issues) report.AppendLine($"    {issue}");
            }
            Debug.Log(bad == 0
                ? $"[VfxAudit] {guids.Length} 个 prefab 全部合规"
                : $"[VfxAudit] {guids.Length} 个 prefab，{bad} 个有问题：\n{report}");
        }

        /// <summary>单件体检。接新件后先跑这个再谈观感。</summary>
        public static List<string> Inspect(GameObject prefab)
        {
            var issues = new List<string>();
            bool groundLayer = prefab.GetComponent<VfxGroundLayer>() != null;

            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in MaterialsOf(r))
                {
                    if (mat == null)
                    {
                        issues.Add($"[材质空] {Path(r.transform, prefab.transform)}");
                        continue;
                    }
                    if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                    {
                        issues.Add($"[品红/错误 shader] {Path(r.transform, prefab.transform)}");
                        continue;
                    }
                    if (BlockedShaders.Contains(mat.shader.name))
                        issues.Add($"[Built-in 专属 shader] {mat.shader.name} @ " +
                                   Path(r.transform, prefab.transform));
                }
            }

            foreach (var c in prefab.GetComponentsInChildren<Component>(true))
            {
                if (c == null)
                {
                    issues.Add("[组件丢失] 存在 Missing Script");
                    continue;
                }
                string type = c.GetType().Name;
                if (BlockedComponents.Contains(type))
                    issues.Add($"[Built-in 专属组件] {type} @ " + Path(c.transform, prefab.transform));
            }

            // 尺寸归一：非地面件应挂 VfxFitter（地面件由 GroundCrackDecal 自管）
            if (!groundLayer && prefab.GetComponentInChildren<VfxFitter>(true) == null)
                issues.Add("[未归一] 缺 VfxFitter，尺寸未与卡宽/逻辑圆挂钩");

            return issues;
        }

        /// <summary>粒子渲染器的 sharedMaterials 第二槽是拖尾材质，拖尾模块关着时
        /// 本来就是 null —— 不能当"材质空"报（否则 52 件里 30 多件全是假警）。</summary>
        static IEnumerable<Material> MaterialsOf(Renderer r)
        {
            // 关掉的渲染器不出图，材质空是正常的：厂包常用一个空粒子系统当容器节点
            if (!r.enabled) yield break;
            if (r is ParticleSystemRenderer psr)
            {
                if (psr.renderMode == ParticleSystemRenderMode.None) yield break;
                yield return psr.sharedMaterial;
                var ps = psr.GetComponent<ParticleSystem>();
                if (ps != null && ps.trails.enabled) yield return psr.trailMaterial;
                yield break;
            }
            foreach (var m in r.sharedMaterials) yield return m;
        }

        static string Path(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return parts.Count == 0 ? "(root)" : string.Join("/", parts);
        }
    }
}
