using System.Collections.Generic;
using System.IO;
using ClientBattle.Test;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 一键打开 RFX4 可靠预览场景并进入 Play（透视 + Bloom + 自动循环）。
    // =========================================================================

    public static class Rfx4PreviewLauncher
    {
        const string ScenePath = "Assets/Scenes/Rfx4Preview.unity";
        const string PrefabFolder =
            "Assets/KriptoFX/Realistic Effects Pack v4/Effects/Prefabs/Effects";

        [MenuItem("GreekMyth/RFX4 可靠预览（一键）")]
        public static void Launch()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += Launch;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnterPreviewPlay();
        }

        /// <summary>不弹保存对话框（自动化/MCP）；会丢弃当前场景未保存改动。</summary>
        public static void LaunchDiscardingUnsaved()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += LaunchDiscardingUnsaved;
                return;
            }

            EnterPreviewPlay();
        }

        static void EnterPreviewPlay()
        {
            BuildOrRefreshScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("GreekMyth/RFX4 仅刷新预览场景（不 Play）")]
        public static void RefreshSceneOnly()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[RFX4Preview] 请先退出 Play 再刷新场景。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            BuildOrRefreshScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[RFX4Preview] 场景已刷新：" + ScenePath);
        }

        static void BuildOrRefreshScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 50f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.045f, 0.06f, 1f);
            camGo.transform.position = new Vector3(0f, 2.2f, -7f);
            camGo.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            camGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 深色地面：弹道碰撞类特效有落点；纯空场景容易「一闪没」
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            var groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f, 1f));
                    else if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", new Color(0.08f, 0.08f, 0.1f, 1f));
                    groundRenderer.sharedMaterial = mat;
                }
            }

            var runnerGo = new GameObject("Rfx4PreviewRunner");
            var runner = runnerGo.AddComponent<Rfx4PreviewRunner>();
            runner.SetPrefabs(LoadEffectPrefabs());
            EditorUtility.SetDirty(runner);
            EditorSceneManager.MarkSceneDirty(scene);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        static List<GameObject> LoadEffectPrefabs()
        {
            var list = new List<GameObject>();
            for (int i = 1; i <= 27; i++)
            {
                var path = $"{PrefabFolder}/Effect{i}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    Debug.LogWarning("[RFX4Preview] 缺失：" + path);
                else
                    list.Add(prefab);
            }

            if (list.Count == 0)
                Debug.LogError("[RFX4Preview] 未找到任何 Effect Prefab，检查 KriptoFX 是否已导入。");
            else
                Debug.Log($"[RFX4Preview] 已装载 {list.Count} 个 Effect Prefab。");

            return list;
        }
    }
}
