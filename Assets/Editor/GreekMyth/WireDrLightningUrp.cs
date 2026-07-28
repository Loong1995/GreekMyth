using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GreekMyth.EditorTools
{
    /// <summary>DigitalRuby Lightning 材质 URP 化（P-83）。
    ///
    /// 厂包三件材质挂 Built-in <c>Legacy Shaders/Particles/Additive*</c>。
    /// 编辑器里 <c>shader.isSupported</c> 常为 true，但 URP 真机/移动端打包后
    /// Legacy 粒子 shader 被剔除或回退 → LineRenderer 渲成<strong>纯白带子</strong>。
    /// 竖雷加粗（alpha≈1、width×2.6）会把问题放大成「整条白贴图」。
    ///
    /// 正解：同资产改挂 <c>Universal Render Pipeline/Unlit</c> Transparent+Additive，
    /// 贴图写到 <c>_BaseMap</c>；运行期 <see cref="ClientBattle.VFX.DrLightningUtil"/>
    /// 再兜一层。尘雾同款口径见 <c>GroundCrackComposer.EnsureDustMaterial</c>。
    /// </summary>
    public static class WireDrLightningUrp
    {
        const string Sheet3 = "Assets/LightningBolt/Textures/LightningSpriteSheet3.png";
        const string BoltTex = "Assets/LightningBolt/Textures/LightningBoltTexture.png";

        static readonly string[] MatPaths =
        {
            "Assets/LightningBolt/LightningBoltMaterialAnimatedAdditive.mat",
            "Assets/LightningBolt/LightningBoltMaterialAdditive.mat",
            "Assets/LightningBolt/LightningBoltMaterialAlphaBlend.mat",
        };

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                foreach (var path in MatPaths)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) continue;
                    if (mat.shader != null
                        && mat.shader.name == "Universal Render Pipeline/Unlit"
                        && mat.GetTexture("_BaseMap") != null)
                        continue;
                    Wire();
                    return;
                }
            };
        }

        [MenuItem("GreekMyth/特效/接线 DR 闪电材质 → URP Unlit（防纯白）")]
        public static void Wire()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Sheet3);
            var bolt = AssetDatabase.LoadAssetAtPath<Texture2D>(BoltTex);
            if (sheet == null)
            {
                Debug.LogError("[DrLightningUrp] 缺贴图 " + Sheet3);
                return;
            }

            // 移动端：关 mipmap、≤1024、认 alpha（与厂包 VFX 贴图红线一致）
            HardenTextureImport(Sheet3);
            HardenTextureImport(BoltTex);
            sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(Sheet3);
            bolt = AssetDatabase.LoadAssetAtPath<Texture2D>(BoltTex);

            // Animated / Additive → Additive 混合；AlphaBlend → Alpha 混合
            Patch(MatPaths[0], sheet, additive: true);
            Patch(MatPaths[1], bolt != null ? bolt : sheet, additive: true);
            Patch(MatPaths[2], sheet, additive: false);

            // Resources 标准件跟材质走（同引用），再标脏确保刷新
            TouchPrefab("Assets/Resources/ClientBattle/VFX/dr_lightning_bolt_anim.prefab");
            TouchPrefab("Assets/Resources/ClientBattle/VFX/dr_lightning_bolt.prefab");

            AssetDatabase.SaveAssets();
            Debug.Log("[DrLightningUrp] DR 三材质已迁 URP/Unlit；竖雷/乱劈不再吃 Legacy Particles。");
        }

        static void Patch(string path, Texture2D tex, bool additive)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogWarning("[DrLightningUrp] 缺材质 " + path);
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[DrLightningUrp] 找不到 URP/Unlit");
                return;
            }
            if (mat.shader != shader)
                mat.shader = shader;

            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            // 电青白：即使贴图采样失败也不至于整条死白
            var tint = new Color(0.72f, 0.88f, 1f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", additive ? 2f : 0f); // Additive / Alpha
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (mat.HasProperty("_SrcBlendAlpha"))
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha"))
                mat.SetFloat("_DstBlendAlpha",
                    (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // Off：竖雷侧视不丢

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive)
            {
                mat.EnableKeyword("_BLENDMODE_ADD");
                mat.DisableKeyword("_BLENDMODE_ALPHA");
                mat.DisableKeyword("_BLENDMODE_PREMULTIPLY");
                mat.DisableKeyword("_BLENDMODE_MULTIPLY");
            }
            else
            {
                mat.EnableKeyword("_BLENDMODE_ALPHA");
                mat.DisableKeyword("_BLENDMODE_ADD");
                mat.DisableKeyword("_BLENDMODE_PREMULTIPLY");
                mat.DisableKeyword("_BLENDMODE_MULTIPLY");
            }
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
        }

        static void HardenTextureImport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            bool dirty = false;
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
            if (importer.maxTextureSize > 1024)
            {
                importer.maxTextureSize = 1024;
                dirty = true;
            }
            // wrap 保持 Repeat：DR 脚本用 mainTextureOffset 切行，Repeat 是原厂设定
            if (!dirty) return;
            importer.SaveAndReimport();
        }

        static void TouchPrefab(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return;
            var lr = go.GetComponent<LineRenderer>();
            if (lr == null || lr.sharedMaterial == null) return;
            // 触发一次序列化刷新；材质已是同资产引用
            EditorUtility.SetDirty(go);
        }
    }
}
