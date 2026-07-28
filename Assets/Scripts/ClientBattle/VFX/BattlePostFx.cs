using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层】BattlePostFx：战斗场景的**镜头层**（强制 HDR + Bloom）。
    // KriptoFX RFX4 Readme：效果按 HDR+Bloom 设计，无 Bloom 会塌成廉价粒子喷洒；
    // 自研裂地的熔岩锋面也是靠 HDR 分量 >1 溢出成光（GroundCrackPalette）。
    // 建世界时 Ensure，两场景共用，不依赖场景手工挂 Volume。
    //
    // 【档位联动，2026-07-28】Bloom 是**全屏 pass**，开销与粒子数无关，因此
    // 不受 `VfxTierScale`（逐件缩放）管辖 —— 只能由本类按 `VfxQuality.Current`
    // 直接写 Volume。系数表仍在 `VfxQuality`（唯一配置点），本类只负责落。
    //
    // 【调用顺序红线】必须在 `VfxQuality.LoadUserPreference()` **之后**调
    // Ensure，否则读到的是上一场残留的档（`PlaybackWorldBuilder.Build` 已按此排序）。
    // 切档后重新落一遍走 <see cref="Apply"/>（编辑器画质档菜单即用此路）。
    //
    // 文档：docs/client/vfx_mobile_budget.md §二b（镜头层档位）
    // =========================================================================

    public static class BattlePostFx
    {
        const string VolumeName = "ClientBattlePostFx";

        /// <summary>光晕扩散半径。与档位无关（不影响成本，只影响观感），故不进档位表。</summary>
        const float BloomScatter = 0.65f;

        static Volume _volume;

        public static void Ensure()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var urp = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null) urp = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            cam.allowHDR = true;

            var existing = GameObject.Find(VolumeName);
            if (existing == null)
            {
                existing = new GameObject(VolumeName);
                var created = existing.AddComponent<Volume>();
                created.isGlobal = true;
                created.priority = 50f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.Add<Bloom>(true);
                created.sharedProfile = profile;
            }
            _volume = existing.GetComponent<Volume>();
            Apply();
        }

        /// <summary>按当前画质档写镜头层参数。幂等、可反复调用——切档时无需重建 Volume，
        /// 也无需重进 Play（Bloom 是全屏后处理，下一帧即生效，不像逐件缩放要等重新启用）。</summary>
        public static void Apply()
        {
            if (_volume == null)
            {
                var go = GameObject.Find(VolumeName);
                if (go == null) return;
                _volume = go.GetComponent<Volume>();
            }
            if (_volume == null || _volume.sharedProfile == null) return;
            if (!_volume.sharedProfile.TryGet<Bloom>(out var bloom)) return;

            int tier = VfxQuality.Index;
            bloom.threshold.Override(VfxQuality.BloomThreshold[tier]);
            bloom.intensity.Override(VfxQuality.BloomIntensity[tier]);
            bloom.scatter.Override(BloomScatter);
            bloom.highQualityFiltering.Override(VfxQuality.BloomHighQuality[tier]);
        }
    }
}
