using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层】BattlePostFx：战斗场景强制 HDR 后处理（Bloom）。
    // KriptoFX RFX4 Readme：效果按 HDR+Bloom 设计；无 Bloom 会塌成廉价粒子喷洒。
    // 建世界时 Ensure，两场景共用，不依赖场景手工挂 Volume。
    // =========================================================================

    public static class BattlePostFx
    {
        const string VolumeName = "ClientBattlePostFx";

        public static void Ensure()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var urp = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null) urp = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true;
            cam.allowHDR = true;

            var existing = GameObject.Find(VolumeName);
            if (existing != null) return;

            var go = new GameObject(VolumeName);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.15f); // RFX 峰值可见；日常卡面仍可压
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(true);
            volume.sharedProfile = profile;
        }
    }
}
