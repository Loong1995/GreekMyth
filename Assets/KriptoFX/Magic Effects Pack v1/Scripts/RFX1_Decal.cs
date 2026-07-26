using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


[ExecuteInEditMode]
public class RFX1_Decal : MonoBehaviour
{
    public bool UseWorldSpaceRotation = false;
    public bool UseRandomRotationAndScale = true;
    public float randomScalePercent = 20;
    public bool IsScreenSpace = true;

    // Material mat;
    ParticleSystem ps;
    ParticleSystem.MainModule psMain;
    private MaterialPropertyBlock props;
    MeshRenderer rend;
    private Vector3 startScale;
    private Vector3 worldRotation = new Vector3(0, 0, 0);

    private bool defaultDepthTextureMode;

    void Awake()
    {
        startScale = transform.localScale;
    }

    private void OnEnable()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            var addCamData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (addCamData != null)
            {
                defaultDepthTextureMode = addCamData.requiresDepthTexture;
                addCamData.requiresDepthTexture = IsScreenSpace;
            }
        }

        //if (Camera.main.orthographic) IsScreenSpace = false;

        if (!IsScreenSpace)
        {
            var sharedMaterial = GetComponent<Renderer>().sharedMaterial;
            sharedMaterial.EnableKeyword("USE_QUAD_DECAL");
            sharedMaterial.SetInt("_ZTest1", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (Application.isPlaying)
            {
                var pos = transform.localPosition;
                pos.z += 0.1f;
                transform.localPosition = pos;
                var scale = transform.localScale;
                scale.y = 0.001f;
                transform.localScale = scale;
            }
        }
        else
        {
            var sharedMaterial = GetComponent<Renderer>().sharedMaterial;
            sharedMaterial.DisableKeyword("USE_QUAD_DECAL");
            sharedMaterial.SetInt("_ZTest1", (int)UnityEngine.Rendering.CompareFunction.Greater);
        }

        if (UseRandomRotationAndScale && !UseWorldSpaceRotation && Application.isPlaying)
        {
            transform.localRotation = Quaternion.Euler(Random.Range(0, 360), 90, 90);
            var randomScaleRange = Random.Range(startScale.x - startScale.x * randomScalePercent * 0.01f,
                startScale.x + startScale.x * randomScalePercent * 0.01f);
            transform.localScale = new Vector3(randomScaleRange, IsScreenSpace ? startScale.y : 0.001f, randomScaleRange);
        }
    }

    void OnDisable()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var addCamData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (addCamData != null) addCamData.requiresDepthTexture = defaultDepthTextureMode;
    }

    void LateUpdate()
    {
        if (UseWorldSpaceRotation)
        {
            transform.rotation = Quaternion.Euler(worldRotation);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = Matrix4x4.TRS(this.transform.TransformPoint(Vector3.zero), this.transform.rotation, this.transform.lossyScale);
        Gizmos.color = new Color(1, 1, 1, 1);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
