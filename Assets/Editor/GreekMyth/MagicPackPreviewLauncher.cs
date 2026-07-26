using System.Collections.Generic;
using System.IO;
using ClientBattle.Test;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // Magic Effects Pack 1 一键预览（透视 + Bloom + Effect1–33 + Collision 落点）。
    // =========================================================================

    public static class MagicPackPreviewLauncher
    {
        const string ScenePath = "Assets/Scenes/MagicPackPreview.unity";
        const string EffectsFolder =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects";
        const string PartsFolder =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/EffectParts";

        [MenuItem("GreekMyth/Magic Pack/可靠预览（一键）")]
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

        [MenuItem("GreekMyth/Magic Pack/仅刷新预览场景（不 Play）")]
        public static void RefreshSceneOnly()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[MagicPreview] 请先退出 Play 再刷新场景。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            BuildOrRefreshScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[MagicPreview] 场景已刷新：" + ScenePath);
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

            var runnerGo = new GameObject("MagicPackPreviewRunner");
            var runner = runnerGo.AddComponent<MagicPackPreviewRunner>();
            runner.SetPrefabs(LoadPrefabs());
            EditorUtility.SetDirty(runner);
            EditorSceneManager.MarkSceneDirty(scene);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        static List<GameObject> LoadPrefabs()
        {
            var list = new List<GameObject>();
            for (int i = 1; i <= 33; i++)
            {
                var path = $"{EffectsFolder}/Effect{i}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    Debug.LogWarning("[MagicPreview] 缺失：" + path);
                else
                    list.Add(prefab);
            }

            // 落点件（宙斯/雅典娜命中用的是 Collision）
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PartsFolder });
            var collisions = new List<GameObject>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("_Collision") < 0) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) collisions.Add(prefab);
            }
            collisions.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            list.AddRange(collisions);

            if (list.Count == 0)
                Debug.LogError("[MagicPreview] 未找到 Magic Pack Prefab，检查是否已导入。");
            else
                Debug.Log($"[MagicPreview] 已装载 {list.Count} 个 Prefab（Effects+Collision）。");

            return list;
        }
    }
}
