// =========================================================================
// 裂地专用 Sprite shader（docs/client/ground_crack_language.md §3 L1/L2）。
//
// 与普通 Sprites/Default 的两点不同，也正是「厂包酷炫感」的真正来源：
//   1) 生长：不是整片淡入，而是按生长场 field 从冲击点向外推进阈值 _Growth，
//      裂缝像真的裂开一样延伸出去（厂包用 RFX1_ShaderFloatCurve 推 _Cutout，
//      同一手法，我方改为由 GroundCrackDecal 按时间推）。
//   2) 熔岩：推进锋面上一条 HDR 亮带 + 缝底余烬，交给 Bloom 过曝发光。
//
// 混合用预乘 alpha（Blend One OneMinusSrcAlpha）：同一 pass 里既能让近黑裂缝
// 「盖住」地面（alpha 覆盖），又能让熔岩「加光」（rgb 直加不受 alpha 约束）。
// 普通 SrcAlpha 混合做不到后者。
//
// 颜色不在此写死：底色走 SpriteRenderer.color（＝GroundCrackPalette），
// 熔岩色走 _GlowColor（由组合器从调色板写入）。
// =========================================================================
Shader "GreekMyth/GroundCrack"
{
    Properties
    {
        [PerRendererData] _MainTex ("Crack Mask", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HDR] _GlowColor ("Lava Color", Color) = (1.6, 0.42, 0.1, 1)
        _Growth ("Growth", Range(0,1.6)) = 1.6
        _GlowGrowth ("Lava Growth (lags behind crack)", Range(0,2)) = 2
        _GrowthMode ("Growth Mode (0=radial 1=axial)", Float) = 0
        _GlowIntensity ("Glow Intensity", Float) = 0
        _FrontWidth ("Glow Front Width", Range(0.01,0.6)) = 0.18
        _Softness ("Growth Edge Softness", Range(0.005,0.5)) = 0.06
        _EmberFloor ("Ember Floor", Range(0,1)) = 0.25
        _MaskGain ("Crack Width Gain", Range(0.5,4)) = 1
        _LavaGradient ("Lava Gradient (rim dark -> core white-hot)", Range(0,1)) = 1
        _LavaScatter ("Lava Ignition Scatter", Range(0,0.8)) = 0.4
        _LavaCells ("Lava Ignition Points (per axis)", Range(1,8)) = 3
        _LavaExtinguish ("Lava Extinguish (0=full 1=gone)", Range(0,1)) = 0
        _LavaFadeCells ("Lava Extinguish Points", Range(1,10)) = 5
        _LavaFadeSoft ("Lava Extinguish Softness", Range(0.05,0.6)) = 0.22
        _LavaFadeSeed ("Lava Extinguish Seed Offset", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _GlowColor;
                float _Growth;
                float _GlowGrowth;
                float _GrowthMode;
                float _GlowIntensity;
                float _FrontWidth;
                float _Softness;
                float _EmberFloor;
                float _MaskGain;
                float _LavaGradient;
                float _LavaScatter;
                float _LavaCells;
                float _LavaExtinguish;
                float _LavaFadeCells;
                float _LavaFadeSoft;
                float _LavaFadeSeed;
            CBUFFER_END

            // 值噪声：格点哈希 + Hermite 插值。用来把熔岩点火时刻沿缝**打散成
            // 几处火口**，而不是整条缝同时着（同步就读作一个开关，2026-07-26）。
            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.5);
                return frac(p.x * p.y * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half4  color       : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half raw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                // 缝宽：抬遮罩 alpha 增益＝细线变粗、梢部更多长出来。用增益而不是
                // 缩放面片，才不会把放射骨架拉成椭圆（强度档 MaskGain，2026-07-26）
                half mask = saturate(raw * _MaskGain);

                // 生长场：命中档从中心向外扩（radial），弹道档沿弹道方向推进（axial）。
                // 叠一点 (1-mask) 抖动，让锋面沿裂纹粗细起伏，不是一个完美的圆/直线。
                float2 d = IN.uv - 0.5;
                float radial = saturate(length(d) * 2.0);
                float field = lerp(radial, saturate(IN.uv.x), saturate(_GrowthMode));
                field += (1.0 - mask) * 0.18;

                float edge = _Growth - field;
                half reveal = saturate(edge / max(1e-4, _Softness));

                half alpha = mask * reveal * IN.color.a;

                // 锋面亮带：edge≈0 处最亮，随推进离开而熄；余烬保底让缝底一直微亮。
                // 两处都按 mask² 聚拢：只让**缝最深处**透光，缝沿与细梢保持暗，
                // 否则整片裂纹一起发亮就成了一个橙色爆点而不是熔岩缝（实测教训）。
                // 熔岩走**自己的推进量** `_GlowGrowth`（由 GroundCrackDecal 驱动，
                // 起步比裂缝晚、爬得比裂缝慢）：缝先黑着裂开，火再顺着缝爬进去。
                // 两者共用一个 _Growth 时，光与缝同生同灭，读不出「烧起来」的过程。
                // 火口：沿缝取一张低频噪声，一处提前点着、一处拖后，强度也各不同。
                // 于是「熔岩推进」不是一条整齐的锋线，而是几处火口先后烧开再连片。
                float ign = ValueNoise(IN.uv * max(1.0, _LavaCells));
                float ignDelay = (ign - 0.5) * _LavaScatter;
                float ignGain = lerp(0.55, 1.35, ValueNoise(IN.uv * max(1.0, _LavaCells) + 13.7));

                float glowEdge = (_GlowGrowth - ignDelay) - field;
                half glowReveal = saturate(glowEdge / max(1e-4, _Softness));

                half band = saturate(1.0 - abs(glowEdge) / max(1e-4, _FrontWidth));
                band *= band;
                half core = mask * mask;
                half ember = _EmberFloor * glowReveal;
                half heat = core * max(band, ember);

                // 熔岩沿缝**渐变**，不是整条缝一个亮色：缝沿暗红 → 缝底熔岩色 →
                // 最深处再补一点白热。高档因此读起来仍是「同一条缝烧得更透」，
                // 而不是贴了一张独立的橙色贴图（2026-07-26 人工验收要求）。
                half3 lava = lerp(_GlowColor.rgb * 0.22, _GlowColor.rgb,
                                  saturate(core * 1.6));
                lava += _GlowColor.rgb * saturate(core - 0.72) * 1.6 * _LavaGradient;
                // 起点烧得最透、末梢渐凉，让熔岩顺着生长方向铺开
                half cool = 1.0 - 0.3 * saturate(field) * _LavaGradient;

                // 消退：从几处随机灭点渐变熄灭（每发种子不同）。
                // keep=1 整条还在烧；_LavaExtinguish 推进时，噪声阈值低的点先灭，
                // 高的点后灭 —— 禁止整片同步压暗（那读作一个开关）。
                float fadeN = ValueNoise(IN.uv * max(1.0, _LavaFadeCells) +
                                         float2(_LavaFadeSeed, _LavaFadeSeed * 1.7));
                half soft = max(0.05, _LavaFadeSoft);
                half keep = 1.0 - saturate(
                    (_LavaExtinguish - (fadeN - soft)) / max(1e-4, soft * 2.0));

                half3 glow = lava * _GlowIntensity * heat * cool * ignGain * keep * IN.color.a;

                // 预乘：rgb 已含 alpha 覆盖量，再把熔岩直接加上去
                return half4(IN.color.rgb * alpha + glow, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
