using UnityEngine;
using System.Collections;

public class RFX1_ShaderColorGradient : MonoBehaviour {

    public RFX1_ShaderProperties ShaderColorProperty = RFX1_ShaderProperties._TintColor;
    public Gradient Color = new Gradient();
    public float TimeMultiplier = 1;
    public bool IsLoop;
    public bool UseSharedMaterial;
    [HideInInspector] public float HUE = -1;

    [HideInInspector]
    public bool canUpdate;
    //private Material mat;
    private int propertyID;
    private float startTime;
    private Color startColor;

    private bool isInitialized;
    private string shaderProperty;

    private MaterialPropertyBlock props;
    private Renderer rend;

    // GreekMyth 补丁：见 RFX1_ShaderFloatCurve —— 惰性初始化，避免
    // OnEnable/Update 早于 Awake 时用到未初始化的 props/rend 而抛异常。
    private void Init()
    {
        if (props == null) props = new MaterialPropertyBlock();
        if (rend == null) rend = GetComponent<Renderer>();

        if (!isInitialized)
        {
            shaderProperty = ShaderColorProperty.ToString();
            propertyID = Shader.PropertyToID(shaderProperty);
            if (rend != null && rend.sharedMaterial != null)
                startColor = rend.sharedMaterial.GetColor(propertyID);
            isInitialized = true;
        }
    }

    void Awake()
    {
        Init();
    }


    private void OnEnable()
    {
        Init();
        startTime = Time.time;
        canUpdate = true;

        if (rend == null || rend.sharedMaterial == null) return;
        rend.GetPropertyBlock(props);

        startColor = rend.sharedMaterial.GetColor(propertyID);
        props.SetColor(propertyID, startColor * Color.Evaluate(0));

        rend.SetPropertyBlock(props);
    }

    private void Update()
    {
        Init();
        if (rend == null || rend.sharedMaterial == null) return;
        rend.GetPropertyBlock(props);

        var time = Time.time - startTime;
        if (canUpdate)
        {
            var eval = Color.Evaluate(time / TimeMultiplier);
            if (HUE > -0.9f)
            {
                eval = RFX1_ColorHelper.ConvertRGBColorByHUE(eval, HUE);
                startColor = RFX1_ColorHelper.ConvertRGBColorByHUE(startColor, HUE);
            }
            props.SetColor(propertyID, eval * startColor);
        }
        if (time >= TimeMultiplier)
        {
            if (IsLoop) startTime = Time.time;
            else canUpdate = false;
        }

        rend.SetPropertyBlock(props);
    }
}
