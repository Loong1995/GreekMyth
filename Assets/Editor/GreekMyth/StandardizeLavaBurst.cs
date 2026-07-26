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
    // 为什么只搬一部分（逐层去向，见 docs/client/vfx_standardization.md §二/§三.1）：
    //   Particles / Trail        → 晋升。HDR 火屑，Effect8「很亮」的主来源
    //   GroundDistortion         → 晋升。地面热扭曲
    //   GroundFog                → **丢弃**（2026-07-26）。俯视舞台下贴地烟
    //     读作漂浮烟雾块，与裂地语言抢戏；热感靠 Particles/Trail/Distortion/Light
    //   Light + RFX1_LightCurves → 晋升（强度折半）。爆发瞬间的地面受光
    //   Decal1（KriptoFX/RFX1/Decal）→ **替代**。厂包深度投影贴花在 URP 下画不出
    //     （P-33；画廊逐件横幅亦标注）。裂缝观感改由自研 GroundCrack.shader 的
    //     生长 + 熔岩锋面复现（手法同源：厂包推 _Cutout，我方推 _Growth）
    //   Wind(WindZone) / Audio   → 丢弃。全局风区会影响别的粒子；音效走自己的 SfxKey
    //   RFX1_Target / RFX1_TransformMotion / PerPlatformSettings → 丢弃。
    //     那是厂包的弹道飞行与画质分级逻辑，与池化播放冲突
    //
    // 幂等可重跑；不改厂包原件。
    // =========================================================================

    public static class StandardizeLavaBurst
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect8.prefab";
        const string Dest = "Assets/Resources/ClientBattle/VFX/ground_lava_bloom.prefab";

        /// <summary>只保留这些节点（其余按上表丢弃）。</summary>
        static readonly string[] Keep =
        {
            "Particles", "Trail", "GroundDistortion", "Light",
        };

        /// <summary>烘制基准尺度。全局大裂地按逻辑圆量级（T3 Width=10），
        /// Effect8 原件是单体爆点体量，这里放大到能铺满场心。</summary>
        const float BakedScale = 1.6f;

        /// <summary>点光强度折半：地面层要「热」不要把卡面 Bloom 爆白。</summary>
        const float LightIntensityMul = 0.5f;

        [MenuItem("GreekMyth/裂地/G12 标准化熔岩层（Effect8 → ground_lava_bloom）")]
        public static void Build()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Src);
            if (src == null)
            {
                Debug.LogError("[LavaStd] 源缺失：" + Src);
                return;
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            root.name = "ground_lava_bloom";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * BakedScale;

            // 厂包弹道/分级脚本：留着会在池化复用时改 transform 或降级关层
            StripPackScripts(root);

            // Effect8 的可视层都挂在 Collision 子节点下，提到根下再按白名单筛
            var carrier = root.transform.Find("Collision");
            if (carrier != null)
            {
                for (int i = carrier.childCount - 1; i >= 0; i--)
                    carrier.GetChild(i).SetParent(root.transform, false);
                Object.DestroyImmediate(carrier.gameObject);
            }

            int dropped = 0;
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (System.Array.IndexOf(Keep, child.name) >= 0) continue;
                Debug.Log($"[LavaStd] 丢弃层 {child.name}");
                Object.DestroyImmediate(child.gameObject);
                dropped++;
            }

            // 兜底：白名单里若混入死贴花（厂包改版），一并摘掉
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null) continue;
                if (shader.name != "KriptoFX/RFX1/Decal" && shader.name != "KriptoFX/RFX4/Decal")
                    continue;
                Debug.Log($"[LavaStd] 摘死贴花 {r.gameObject.name}（{shader.name}，URP 画不出）");
                Object.DestroyImmediate(r.gameObject);
                dropped++;
            }

            foreach (var light in root.GetComponentsInChildren<Light>(true))
                light.intensity *= LightIntensityMul;

            // 地面层：排序豁免（必须留在卡牌之下），尺寸不交给 VfxFitter
            if (root.GetComponent<VfxGroundLayer>() == null)
                root.AddComponent<VfxGroundLayer>();
            foreach (var fitter in root.GetComponentsInChildren<VfxFitter>(true))
                Object.DestroyImmediate(fitter);

            PrefabUtility.SaveAsPrefabAsset(root, Dest);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LavaStd] {Dest} ← {Src}；保留 {string.Join("/", Keep)}，" +
                      $"丢弃 {dropped} 层，基准缩放 {BakedScale}");
        }

        /// <summary>按类型名摘厂包运行时脚本（ClientBattle 不引用厂包程序集，
        /// 这里也只按名字判，避免编辑器脚本反向依赖包）。</summary>
        static void StripPackScripts(GameObject root)
        {
            string[] blocked =
            {
                "RFX1_Target", "RFX1_TransformMotion", "RFX1_PerPlatformSettings",
            };
            foreach (var mono in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono == null) continue;
                if (System.Array.IndexOf(blocked, mono.GetType().Name) < 0) continue;
                Debug.Log($"[LavaStd] 摘脚本 {mono.GetType().Name} @ {mono.gameObject.name}");
                Object.DestroyImmediate(mono);
            }
        }
    }
}
