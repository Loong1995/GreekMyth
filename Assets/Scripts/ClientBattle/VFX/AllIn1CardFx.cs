using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // All In 1 Sprite Shader 卡牌效果：石化（砂岩叠染，非黑白遗像）/ 圣盾（金描边）
    // =========================================================================
    public static class AllIn1CardFx
    {
        const string ShaderName = "AllIn1SpriteShader/AllIn1SpriteShader";

        // 暖砂岩色；立绘只半石化，框更石一点——避免整脸洗成遗像
        static readonly Color StoneTint = new(0.90f, 0.82f, 0.66f, 1f);
        static readonly Color StoneOutline = new(0.55f, 0.48f, 0.38f, 1f);
        static readonly Color AegisGold = new(1f, 0.86f, 0.38f, 1f);

        public const float PetrifyPortraitMax = 0.40f;
        public const float PetrifyFrameMax = 0.68f;

        public static Material CreateFxMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[AllIn1CardFx] Shader not found: {ShaderName}");
                return null;
            }
            var mat = new Material(shader) { name = "AllIn1CardFx_Runtime" };
            mat.DisableKeyword("GREYSCALE_ON");
            mat.DisableKeyword("OUTBASE_ON");
            mat.DisableKeyword("GLOW_ON");
            mat.SetFloat("_GreyscaleBlend", 0f);
            mat.SetFloat("_OutlineAlpha", 0f);
            mat.SetFloat("_Glow", 0f);
            return mat;
        }

        public static void Apply(Material mat, bool petrified, bool aegis, bool isFrame)
        {
            if (mat == null) return;

            if (petrified)
            {
                mat.EnableKeyword("GREYSCALE_ON");
                float max = isFrame ? PetrifyFrameMax : PetrifyPortraitMax;
                mat.SetFloat("_GreyscaleBlend", max);
                mat.SetFloat("_GreyscaleLuminosity", isFrame ? 0.05f : 0.12f);
                mat.SetColor("_GreyscaleTintColor", StoneTint);
            }
            else
            {
                mat.DisableKeyword("GREYSCALE_ON");
                mat.SetFloat("_GreyscaleBlend", 0f);
                mat.SetColor("_GreyscaleTintColor", Color.white);
            }

            // 圣盾优先金描边；否则石化时给卡框一圈石缘
            if (aegis)
            {
                mat.EnableKeyword("OUTBASE_ON");
                mat.DisableKeyword("GLOW_ON");
                mat.SetFloat("_Glow", 0f);
                mat.SetColor("_OutlineColor", AegisGold);
                if (isFrame)
                {
                    mat.SetFloat("_OutlineWidth", 0.018f);
                    mat.SetFloat("_OutlineAlpha", 0.85f);
                    mat.SetFloat("_OutlineGlow", 1.6f);
                }
                else
                {
                    mat.DisableKeyword("OUTBASE_ON");
                    mat.SetFloat("_OutlineAlpha", 0f);
                    mat.SetFloat("_OutlineGlow", 1f);
                }
            }
            else if (petrified && isFrame)
            {
                mat.EnableKeyword("OUTBASE_ON");
                mat.DisableKeyword("GLOW_ON");
                mat.SetFloat("_Glow", 0f);
                mat.SetColor("_OutlineColor", StoneOutline);
                mat.SetFloat("_OutlineWidth", 0.014f);
                mat.SetFloat("_OutlineAlpha", 0.7f);
                mat.SetFloat("_OutlineGlow", 1.1f);
            }
            else
            {
                mat.DisableKeyword("OUTBASE_ON");
                mat.DisableKeyword("GLOW_ON");
                mat.SetFloat("_OutlineAlpha", 0f);
                mat.SetFloat("_Glow", 0f);
            }
        }

        /// <summary>t=0~1 映射到框/立绘各自的石化上限。</summary>
        public static void SetPetrifyAmount(Material frameMat, Material portraitMat, float t)
        {
            t = Mathf.Clamp01(t);
            if (frameMat != null)
                frameMat.SetFloat("_GreyscaleBlend", t * PetrifyFrameMax);
            if (portraitMat != null)
                portraitMat.SetFloat("_GreyscaleBlend", t * PetrifyPortraitMax);
        }
    }
}
