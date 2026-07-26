using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientBattle.Test;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 特效画廊一键入口。场景每次重建：只有相机/光/Runner 三件，
    // 舞台与卡牌由 Runner 在运行期用真战报建，故场景本身不需要维护。
    //
    // 厂包 prefab 不在 Resources 下，运行期加载不到，只能编辑期用
    // AssetDatabase 收集好、按包分组注入 Runner 的序列化字段。
    // =========================================================================

    public static class VfxGalleryLauncher
    {
        const string ScenePath = "Assets/Scenes/VfxGallery.unity";

        /// <summary>各包的 prefab 目录。顺序即画廊里的翻包顺序：
        /// 我方标准件（Runner 自己从 Resources 装）在最前，其后按"最可能出货"排。</summary>
        static readonly (string Name, string Dir)[] Packs =
        {
            ("Magic Pack v1", "Assets/KriptoFX/Magic Effects Pack v1/Prefabs"),
            ("RFX4", "Assets/KriptoFX/Realistic Effects Pack v4/Effects"),
            ("Vefects 连击闪卡", "Assets/Vefects/Combat Flipbook VFX URP/VFX"),
            ("Cartoon FX Remaster", "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs"),
            ("2D 斩击", "Assets/Cartoon Coffee/2D Slash VFX/Prefabs"),
            ("彩色系列", "Assets/VFX/Prefabs"),
            ("闪电链", "Assets/LightningBolt"),
        };

        /// <summary>排除项：示例场景用件与非特效资产，混进来只会拖长审核。</summary>
        static readonly string[] Excluded =
        {
            "/Demo/", "/SceneResources/", "/Models/", "/Materials/",
        };

        [MenuItem("GreekMyth/特效/特效画廊（一键）")]
        public static void Launch()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Launch;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            camGo.AddComponent<AudioListener>(); // 缺它会满屏 AudioListener 警告

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            lightGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            var runner = new GameObject("VfxGalleryRunner").AddComponent<VfxGalleryRunner>();
            runner.SetGroups(CollectGroups());
            EditorUtility.SetDirty(runner);

            EditorSceneManager.MarkSceneDirty(scene);
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        /// <summary>碎片件（命中/子件）排到主件之后。纯路径 Ordinal 排序会把
        /// `Prefabs/EffectParts/` 排在 `Prefabs/Effects/` 前面（'P' &lt; 's'），于是一进
        /// Magic Pack 就是连续 28 件不能独立成立的命中碎片，很容易误判"整包没货"。
        /// 主件才是完整出手流程（自带位移 + 命中时生成碎片件），必须先过。</summary>
        static bool IsFragment(string path) =>
            path.Contains("/EffectParts/") || path.Contains("_Collision") || path.Contains("_Part");

        /// <summary>厂包目录里混着示例用的角色模型与道具（Magic Pack 的
        /// `Character_Effect*` 是一整套 challenger 蒙皮角色）。判据：必须有粒子或
        /// 线/拖尾渲染器，且不能带蒙皮网格 —— 比按名字黑名单可靠。</summary>
        static bool IsEffect(GameObject prefab)
        {
            if (prefab == null) return false;
            if (prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return false;
            return prefab.GetComponentInChildren<ParticleSystem>(true) != null
                   || prefab.GetComponentInChildren<LineRenderer>(true) != null
                   || prefab.GetComponentInChildren<TrailRenderer>(true) != null;
        }

        static List<VfxGalleryRunner.Group> CollectGroups()
        {
            var groups = new List<VfxGalleryRunner.Group>();
            var log = new System.Text.StringBuilder();

            foreach (var (name, dir) in Packs)
            {
                if (!Directory.Exists(dir))
                {
                    log.AppendLine($"  跳过 {name}：目录不存在 {dir}");
                    continue;
                }

                var items = AssetDatabase.FindAssets("t:Prefab", new[] { dir })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !Excluded.Any(p.Contains))
                    .OrderBy(IsFragment)
                    .ThenBy(p => p, System.StringComparer.Ordinal)
                    .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                    .Where(IsEffect)
                    .ToList();

                if (items.Count == 0)
                {
                    log.AppendLine($"  跳过 {name}：无 prefab");
                    continue;
                }

                groups.Add(new VfxGalleryRunner.Group { Name = name, Ours = false, Items = items });
                log.AppendLine($"  {name}：{items.Count} 件");
            }

            Debug.Log($"[VfxGallery] 已装载 {groups.Count} 个厂包组（我方标准件由 Runner 自装）\n{log}");
            return groups;
        }
    }
}
