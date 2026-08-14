Shader "Custom/UI/SmokeEdgeFade"
{
    Properties
    {
        _Color         ("Color", Color)                      = (0, 0, 0, 1)
        _Progress      ("Progress", Range(0, 1))              = 0
        _NoiseTex      ("Smoke Noise (tileable)", 2D)         = "white" {}
        _NoiseScale    ("Noise Scale", Float)                 = 3.0
        _NoiseSpeed    ("Noise Speed (XY)", Vector)           = (0.05, 0.08, 0, 0)
        _NoiseStrength ("Smoke Intensity", Range(0, 1))       = 0.3
        _EdgeSoftness  ("Edge Softness", Range(0.001, 0.3))   = 0.12
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "RenderType"        = "Transparent"
            "RenderPipeline"    = "UniversalPipeline"
            "IgnoreProjector"   = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                float  _Progress;
                float  _NoiseScale;
                float2 _NoiseSpeed;
                float  _NoiseStrength;
                float  _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.color       = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Distance au centre de l'écran, corrigée par le ratio d'aspect pour
                // obtenir un vrai cercle (et non une ellipse qui suit le rectangle écran).
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 centered = (uv - 0.5) * float2(aspect, 1.0);
                float distFromCenter = length(centered);
                float maxDist = length(float2(0.5 * aspect, 0.5)); // distance au coin le plus loin

                // Même technique que FogZoneOverlay : deux couches de bruit à échelles/
                // vitesses différentes, pour un contour animé et organique (fumée).
                float2 uv1 = uv * _NoiseScale       + _Time.y * _NoiseSpeed;
                float2 uv2 = uv * _NoiseScale * 0.6 - _Time.y * _NoiseSpeed * 0.5 + float2(0.37, 0.63);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv1).r * 0.6
                            + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv2).r * 0.4;

                // Rayon du cercle encore visible (non couvert) : rétrécit de maxDist (rien de
                // couvert) à 0 (tout est noir) à mesure que _Progress va de 0 à 1.
                float radius = lerp(maxDist, 0.0, _Progress);
                float threshold = radius + (noise - 0.5) * _NoiseStrength;
                float alpha = smoothstep(threshold - _EdgeSoftness, threshold + _EdgeSoftness, distFromCenter);

                half4 col = IN.color;
                col.a *= alpha;
                return col;
            }
            ENDHLSL
        }
    }
}
