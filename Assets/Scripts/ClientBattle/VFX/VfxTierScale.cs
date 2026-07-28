using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 画质分档的执行者：按 VfxQuality 的系数**缩放强度**，而不是删层。
    //
    // 【为什么必须是运行期缩放，而不是落盘时烤进 prefab】烤进去意味着中高端机
    // 永远也拿不回原始强度，且以后想改平衡点得把所有件重接一遍。运行期缩放
    // 让成品始终是"厂包满强度"，档位是一个可随时改的系数（VfxQuality 三张表）。
    //
    // 挂载由标准化流水线自动完成（`VfxPackStandardizer.AttachTierScales`），
    // 手工接件不需要理会。
    // =========================================================================

    /// <summary>被缩放的目标类别。</summary>
    public enum VfxTierTarget
    {
        /// <summary>挂在件根上：缩放本件所有粒子层的发射量。</summary>
        Particles,
        /// <summary>挂在屏幕折射节点上：单独缩放该层（折射有自己的系数）。</summary>
        Refraction,
        /// <summary>挂在灯节点上：缩放亮度；低于 MinTier 时整盏关（灯的开销
        /// 来自"多一盏多一遍光照循环"，调亮度省不下来）。</summary>
        Light,
    }

    [DisallowMultipleComponent]
    public class VfxTierScale : MonoBehaviour
    {
        public VfxTierTarget Target = VfxTierTarget.Particles;

        /// <summary>低于此档直接停用（只对 Light 有意义，其余一律 Low 常开）。</summary>
        public VfxTier MinTier = VfxTier.Low;

        ParticleSystem[] _systems;
        float[] _rate;
        float[][] _burst;
        Light _light;
        float _lightIntensity;
        bool _captured;
        VfxTier _applied;
        bool _hasApplied;

        void Awake() => Capture();

        void OnEnable()
        {
            // 池化复用时档位可能已被改过（设置面板/调试），故每次启用对拍一次
            if (_hasApplied && _applied == VfxQuality.Current) return;
            Apply();
        }

        /// <summary>记录**原始满强度**。只做一次：Apply 之后的值是缩放过的，
        /// 再采一次就会一层层往下乘（池化复用时会越播越稀）。</summary>
        void Capture()
        {
            if (_captured) return;
            _captured = true;

            if (Target == VfxTierTarget.Light)
            {
                _light = GetComponent<Light>();
                if (_light != null) _lightIntensity = _light.intensity;
                return;
            }

            if (Target == VfxTierTarget.Refraction)
            {
                _systems = GetComponents<ParticleSystem>();
            }
            else
            {
                // 折射层有自己的系数与自己的 gate，根上这把闸必须跳过它，
                // 否则两个写方相乘，低端档会被压成原来的十分之一。
                var all = GetComponentsInChildren<ParticleSystem>(true);
                var kept = new System.Collections.Generic.List<ParticleSystem>(all.Length);
                foreach (var ps in all)
                {
                    var own = ps.GetComponent<VfxTierScale>();
                    if (own != null && own != this && own.Target == VfxTierTarget.Refraction) continue;
                    kept.Add(ps);
                }
                _systems = kept.ToArray();
            }
            _rate = new float[_systems.Length];
            _burst = new float[_systems.Length][];
            for (int i = 0; i < _systems.Length; i++)
            {
                var emission = _systems[i].emission;
                _rate[i] = emission.rateOverTimeMultiplier;
                _burst[i] = new float[emission.burstCount];
                for (int b = 0; b < emission.burstCount; b++)
                    _burst[i][b] = emission.GetBurst(b).count.constantMax;
            }
        }

        void Apply()
        {
            Capture();
            _applied = VfxQuality.Current;
            _hasApplied = true;

            if (Target == VfxTierTarget.Light)
            {
                if (_light == null) return;
                bool on = VfxQuality.Current >= MinTier;
                _light.enabled = on;
                if (on) _light.intensity = _lightIntensity * VfxQuality.Factor(VfxQuality.LightFactor);
                return;
            }

            float factor = VfxQuality.Factor(Target == VfxTierTarget.Refraction
                ? VfxQuality.RefractionFactor
                : VfxQuality.ParticleFactor);
            for (int i = 0; i < _systems.Length; i++)
            {
                if (_systems[i] == null) continue;
                var emission = _systems[i].emission;
                emission.rateOverTimeMultiplier = _rate[i] * factor;
                for (int b = 0; b < emission.burstCount && b < _burst[i].Length; b++)
                {
                    var burst = emission.GetBurst(b);
                    var count = burst.count;
                    // 至少留 1 颗：一次性 burst 被压到 0 就等于把这层删了
                    count.constantMin = count.constantMax = Mathf.Max(1f, _burst[i][b] * factor);
                    burst.count = count;
                    emission.SetBurst(b, burst);
                }
            }
        }
    }
}
