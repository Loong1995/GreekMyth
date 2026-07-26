using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif


public class RFX1_PerPlatformSettings : MonoBehaviour
{
    [Range(0.1f, 1)] public float ParticleBudgetForMobiles = 1f;

    private bool defaultOpaueColorUsing;

    void Awake()
    {
        ChangeParticlesBudget(ParticleBudgetForMobiles);
    }

    void OnEnable()
    {
        var cam = Camera.main;

        if (cam == null) return;
        var addCamData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (addCamData != null)
        {
            defaultOpaueColorUsing = addCamData.requiresColorTexture;
            addCamData.requiresColorTexture = true;
        }
    }

    void OnDisable()
    {
        var cam = Camera.main;

        if (cam == null) return;
        var addCamData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (addCamData != null) addCamData.requiresColorTexture = defaultOpaueColorUsing;
    }

    void ChangeParticlesBudget(float particlesMul)
    {
        var particles = GetComponent<ParticleSystem>();
        if (particles == null) return;

        var main = particles.main;
        main.maxParticles = Mathf.Max(1, (int)(main.maxParticles * particlesMul));

        var emission = particles.emission;
        if (!emission.enabled) return;

        var rateOverTime = emission.rateOverTime;
        {
            if (rateOverTime.constantMin > 1) rateOverTime.constantMin *= particlesMul;
            if (rateOverTime.constantMax > 1) rateOverTime.constantMax *= particlesMul;
            emission.rateOverTime = rateOverTime;
        }

        var rateOverDistance = emission.rateOverDistance;
        if (rateOverDistance.constantMin > 1)
        {
            if (rateOverDistance.constantMin > 1) rateOverDistance.constantMin *= particlesMul;
            if (rateOverDistance.constantMax > 1) rateOverDistance.constantMax *= particlesMul;
            emission.rateOverDistance = rateOverDistance;
        }

        ParticleSystem.Burst[] burst = new ParticleSystem.Burst[emission.burstCount];
        emission.GetBursts(burst);
        for (var i = 0; i < burst.Length; i++)
        {

            if (burst[i].minCount > 1) burst[i].minCount = (short)(burst[i].minCount * particlesMul);
            if (burst[i].maxCount > 1) burst[i].maxCount = (short)(burst[i].maxCount * particlesMul);
        }
        emission.SetBursts(burst);
    }
}
