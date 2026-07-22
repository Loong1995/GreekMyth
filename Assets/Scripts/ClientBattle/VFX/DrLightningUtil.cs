using DigitalRuby.LightningBolt;
using UnityEngine;

namespace ClientBattle.VFX
{
    // Digital Ruby 免费 Lightning Bolt：使用 Animated 贴图闪电路径（Demo 下方那种）。
    public static class DrLightningUtil
    {
        const string PrefabPath = "ClientBattle/VFX/dr_lightning_bolt_anim";

        static GameObject _prefab;

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
            // 保留 Animated prefab：8 行贴图 PingPong
            bolt.Rows = 8;
            bolt.Columns = 1;
            bolt.AnimationMode = LightningBoltAnimationMode.PingPong;

            var lr = go.GetComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sortingOrder = 15;
            bolt.UpdateFromMaterialChange();

            for (int i = go.transform.childCount - 1; i >= 0; i--)
                go.transform.GetChild(i).gameObject.SetActive(false);

            return bolt;
        }

        /// <summary>世界坐标劈一道；alpha 控制整体透明度，widthMul 控制粗细。</summary>
        public static void Fire(LightningBoltScript bolt, Vector3 worldFrom, Vector3 worldTo,
                                float duration = 0.12f, float chaos = 0.15f, int generations = 5,
                                float alpha = 0.8f, float widthMul = 0.45f, int sortingOrder = 15)
        {
            if (bolt == null) return;
            bolt.StartPosition = worldFrom;
            bolt.EndPosition = worldTo;
            bolt.Duration = Mathf.Max(0.02f, duration);
            bolt.ChaosFactor = Mathf.Clamp01(chaos);
            bolt.Generations = Mathf.Clamp(generations, 1, 8);
            bolt.ManualMode = true;

            var lr = bolt.GetComponent<LineRenderer>();
            lr.sortingOrder = sortingOrder;
            lr.widthMultiplier = widthMul;
            ApplyAlpha(lr, alpha);

            bolt.Trigger();
        }

        static void ApplyAlpha(LineRenderer lr, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            var grad = lr.colorGradient;
            var keys = grad.colorKeys;
            var alphas = grad.alphaKeys.Length == 0
                ? new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) }
                : new GradientAlphaKey[grad.alphaKeys.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                float t = grad.alphaKeys.Length == 0 ? (i == 0 ? 0f : 1f) : grad.alphaKeys[i].time;
                alphas[i] = new GradientAlphaKey(alpha, t);
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
    }
}
