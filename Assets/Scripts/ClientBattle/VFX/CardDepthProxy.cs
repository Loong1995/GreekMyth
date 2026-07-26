using UnityEngine;
using UnityEngine.Rendering;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层】卡牌深度代理：给每张卡补一份**不透明、alpha 裁剪**的同形副本，
    // 画在几何队列里，位置比卡面略靠后一点点。
    //
    // 为什么需要：卡牌是透明 Sprite，既**不写深度**、也**不进不透明贴图拷贝**。
    // 厂包特效里三类主力层都因此失效：
    //   1) 折射壳（RFX1_UberDistortion 采 _CameraOpaqueTexture）取到的是"没有卡牌
    //      的背景"，叠上去等于把卡抹掉一块 —— 护盾类件包不住卡；
    //   2) 软粒子（USE_SOFT_PARTICLES 采 _CameraDepthTexture）没有可淡出的表面，
    //      粒子硬边穿插；
    //   3) 深度排序：透明件只能靠 sortingOrder 决定前后，穹顶前后半塌成一片。
    // 补上这份副本，三条同时恢复，且**卡牌本体的渲染完全不动** ——
    // All In 1 的石化/圣盾描边、阵营染色、呼吸浮动全部照旧。
    //
    // 代价：每张卡多两次 draw（卡框 + 立绘），6 张卡共 12 次，可接受。
    // 副本不跟随卡面的染色/闪光（只在深度与折射取样里被看到，肉眼不可见差异）。
    //
    // 红线：副本必须**略小 + 略靠后**，否则会在卡沿露出一圈"重影"。
    // =========================================================================

    public static class CardDepthProxy
    {
        /// <summary>总开关（画廊按 J 键 A/B 对比用）。关掉后已建的副本一并隐藏。</summary>
        public static bool Enabled = true;

        const string ProxyName = "DepthProxy";

        /// <summary>比卡面缩一点，避免副本在卡沿露边成重影。</summary>
        const float Inset = 0.985f;

        /// <summary>沿卡牌法线向后的偏移（本地 +z ＝ 背离相机）。</summary>
        const float BackOffset = 0.015f;

        const float AlphaCutoff = 0.5f;

        /// <summary>给一个 SpriteRenderer 挂深度副本。sprite 为空或已挂过则跳过。</summary>
        public static void AttachTo(SpriteRenderer source)
        {
            if (source == null || source.sprite == null) return;
            if (source.transform.Find(ProxyName) != null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = ProxyName;
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider); // 深度副本不参与任何碰撞

            quad.transform.SetParent(source.transform, false);
            var bounds = source.sprite.bounds;
            quad.transform.localPosition = new Vector3(bounds.center.x, bounds.center.y, BackOffset);
            quad.transform.localScale = new Vector3(bounds.size.x * Inset, bounds.size.y * Inset, 1f);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = BuildMaterial(source.sprite);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = Enabled;
        }

        /// <summary>不透明 + alpha 裁剪的无光照材质；UV 取 sprite 在图集里的实际矩形。
        /// 走 Geometry 队列是关键 —— 只有不透明队列才会被写进深度图与不透明贴图拷贝。</summary>
        static Material BuildMaterial(Sprite sprite)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var mat = new Material(shader) { name = "CardDepthProxyMat" };
            var tex = sprite.texture;
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);

            var rect = sprite.textureRect;
            var scale = new Vector2(rect.width / tex.width, rect.height / tex.height);
            var offset = new Vector2(rect.x / tex.width, rect.y / tex.height);
            mat.mainTextureScale = scale;
            mat.mainTextureOffset = offset;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
            }

            // Opaque + AlphaClip + 写深度：三者缺一就退回透明队列，代理即失效
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", AlphaCutoff);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Geometry;
            return mat;
        }

        /// <summary>全场副本开关（画廊 A/B 对比）。</summary>
        public static void SetEnabled(bool on)
        {
            Enabled = on;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (renderer != null && renderer.gameObject.name == ProxyName)
                    renderer.enabled = on;
            }
        }
    }
}
