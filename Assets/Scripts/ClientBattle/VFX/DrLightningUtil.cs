using DigitalRuby.LightningBolt;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClientBattle.VFX
{
    // Digital Ruby 免费 Lightning Bolt：Animated 贴图闪电路径。
    // 材质必须是 URP/Unlit（P-83）。闪电感＝饱和蓝晕 + 细白芯 + 周期重 Trigger。
    public static class DrLightningUtil
    {
        const string PrefabPath = "ClientBattle/VFX/dr_lightning_bolt_anim";
        const string UrpUnlit = "Universal Render Pipeline/Unlit";

        /// <summary>电芯：偏白青（细线）。</summary>
        static readonly Color CoreTint = new(0.55f, 0.82f, 1f, 1f);
        /// <summary>电晕：饱和宝蓝（宽线、较低亮度）——去掉灰白感的关键。</summary>
        static readonly Color HaloTint = new(0.12f, 0.42f, 1f, 1f);

        static GameObject _prefab;
        static Material _runtimeUrpMat;

        public static LightningBoltScript Spawn(Transform parent, string name = "DrBolt")
        {
            EnsurePrefab();
            var go = Object.Instantiate(_prefab, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var bolt = go.GetComponent<LightningBoltScript>();
            bolt.StartObject = null;
            bolt.EndObject = null;
            bolt.ManualMode = true;
            bolt.Rows = 8;
            bolt.Columns = 1;
            bolt.AnimationMode = LightningBoltAnimationMode.PingPong;

            var lr = go.GetComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sortingOrder = 15;
            EnsureUrpMaterial(lr);
            bolt.UpdateFromMaterialChange();

            for (int i = go.transform.childCount - 1; i >= 0; i--)
                go.transform.GetChild(i).gameObject.SetActive(false);

            return bolt;
        }

        /// <summary>世界坐标劈一道。
        ///
        /// <paramref name="alpha"/>＝亮度强度，写进材质 <c>_BaseColor</c>
        ///（URP/Unlit 不乘顶点色，P-83）。
        /// <paramref name="tint"/>：null＝电芯色；晕层传 <see cref="HaloTint"/>。</summary>
        public static void Fire(LightningBoltScript bolt, Vector3 worldFrom, Vector3 worldTo,
                                float duration = 0.12f, float chaos = 0.15f, int generations = 5,
                                float alpha = 0.8f, float widthMul = 0.45f, int sortingOrder = 15,
                                Color? tint = null, bool flicker = true)
        {
            if (bolt == null) return;
            bolt.StartPosition = worldFrom;
            bolt.EndPosition = worldTo;
            bolt.Duration = Mathf.Max(0.02f, duration);
            bolt.ChaosFactor = Mathf.Clamp01(chaos);
            bolt.Generations = Mathf.Clamp(generations, 1, 8);
            bolt.ManualMode = true;

            var lr = bolt.GetComponent<LineRenderer>();
            EnsureUrpMaterial(lr);
            lr.sortingOrder = sortingOrder;
            lr.widthCurve = Taper;
            lr.widthMultiplier = widthMul;
            var color = tint ?? CoreTint;
            ApplyIntensity(lr, alpha, color);
            ApplyTintAndAlpha(lr, alpha, color);

            bolt.Trigger();
            if (flicker && duration > 0.08f)
            {
                var f = bolt.gameObject.GetComponent<DrLightningFlicker>()
                        ?? bolt.gameObject.AddComponent<DrLightningFlicker>();
                f.Begin(bolt, duration, interval: 0.04f);
            }
        }

        /// <summary>回收一道雷：连实例材质一起销毁。</summary>
        public static void Release(LightningBoltScript bolt, float delay)
        {
            if (bolt == null) return;
            var lr = bolt.GetComponent<LineRenderer>();
            var mat = lr != null ? lr.sharedMaterial : null;
            if (mat != null && mat != _runtimeUrpMat
                && mat.name.EndsWith("(Instance)", System.StringComparison.Ordinal))
                Object.Destroy(mat, delay);
            Object.Destroy(bolt.gameObject, delay);
        }

        static readonly AnimationCurve Taper = new(
            new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1f),
            new Keyframe(0.88f, 1f), new Keyframe(1f, 0.2f));

        /// <summary>亮度上限（加色 + 明亮舞台）。芯略高、晕走饱和色不靠拉亮。</summary>
        const float MaxIntensity = 0.7f;

        static void ApplyIntensity(LineRenderer lr, float intensity, Color tint)
        {
            var mat = lr.material;
            if (mat == null) return;
            var c = tint * Mathf.Clamp(intensity, 0f, MaxIntensity);
            c.a = 1f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        static void EnsureUrpMaterial(LineRenderer lr)
        {
            if (lr == null) return;
            var shared = lr.sharedMaterial;
            if (shared != null
                && shared.shader != null
                && shared.shader.name == UrpUnlit
                && shared.GetTexture("_BaseMap") != null)
                return;

            if (_runtimeUrpMat == null)
            {
                var shader = Shader.Find(UrpUnlit) ?? Shader.Find("Sprites/Default");
                if (shader == null) return;
                Texture tex = shared != null
                    ? (shared.GetTexture("_BaseMap") ?? shared.mainTexture)
                    : null;
                _runtimeUrpMat = new Material(shader) { name = "DR_Lightning_URP_Runtime" };
                if (tex != null)
                {
                    _runtimeUrpMat.SetTexture("_BaseMap", tex);
                    _runtimeUrpMat.mainTexture = tex;
                }
                ApplyUrpAdditive(_runtimeUrpMat);
            }
            lr.sharedMaterial = _runtimeUrpMat;
        }

        static void ApplyUrpAdditive(Material mat)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", CoreTint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", CoreTint);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_BLENDMODE_ADD");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        static void ApplyTintAndAlpha(LineRenderer lr, float alpha, Color tint)
        {
            alpha = Mathf.Clamp01(alpha);
            var grad = lr.colorGradient;
            var keys = grad.colorKeys;
            if (keys == null || keys.Length == 0)
                keys = new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) };
            else
            {
                for (int i = 0; i < keys.Length; i++)
                    keys[i] = new GradientColorKey(tint, keys[i].time);
            }
            var alphas = grad.alphaKeys.Length == 0
                ? new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) }
                : new GradientAlphaKey[grad.alphaKeys.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                float t = grad.alphaKeys.Length == 0 ? (i == 0 ? 0f : 1f) : grad.alphaKeys[i].time;
                float a = alpha;
                if (t < 0.08f || t > 0.92f) a *= 0.35f;
                alphas[i] = new GradientAlphaKey(a, t);
            }
            if (alphas.Length >= 2)
            {
                alphas[0] = new GradientAlphaKey(alpha * 0.15f, 0f);
                alphas[alphas.Length - 1] = new GradientAlphaKey(alpha * 0.15f, 1f);
            }
            grad.SetKeys(keys, alphas);
            lr.colorGradient = grad;
        }

        static void EnsurePrefab()
        {
            if (_prefab != null) return;
            _prefab = Resources.Load<GameObject>(PrefabPath);
            if (_prefab == null)
                Debug.LogError($"[DrLightningUtil] Missing Resources/{PrefabPath}.prefab");
        }

        /// <summary>电晕色（分叉/外晕用）。</summary>
        public static Color Halo => HaloTint;
        /// <summary>电芯色。</summary>
        public static Color Core => CoreTint;
    }
}
