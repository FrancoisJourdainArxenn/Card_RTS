Shader "Custom/VFX_Assimilate"
{
    Properties
    {
        _ShapeTex ("Shape Texture (votre entonnoir)", 2D) = "white" {}
        _WaveTex ("Wave Noise (ex: WindNoise1)", 2D) = "gray" {}
        _WaveAmount ("Wave Amount", Float) = 0.04
        _WavePanSpeed ("Wave Pan Speed", Float) = 0.3
        _TurbTex ("Turbulence Noise (ex: Spiral07)", 2D) = "gray" {}
        _TurbPanSpeed ("Turbulence Pan Speed", Float) = 0.6
        _TurbMin ("Turbulence Min", Range(0,1)) = 0.55
        _Progress ("Progress", Range(0,1)) = 0
        _EdgeWidth ("Edge Width", Float) = 0.12
        _BaseColor ("Base Color", Color) = (0.5, 0.15, 0.9, 0.6)
        _SecondaryColor ("Secondary Color", Color) = (0.15, 0.6, 0.9, 0.6)

        [HDR] _EdgeColor ("Edge Color (HDR)", Color) = (1.2, 0.7, 2.5, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_ShapeTex); SAMPLER(sampler_ShapeTex);
            TEXTURE2D(_WaveTex); SAMPLER(sampler_WaveTex);
            TEXTURE2D(_TurbTex); SAMPLER(sampler_TurbTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShapeTex_ST;
                float _WaveAmount;
                float _WavePanSpeed;
                float _TurbPanSpeed;
                float _TurbMin;
                float _Progress;
                float _EdgeWidth;
                float4 _BaseColor;
                float4 _SecondaryColor;
                float4 _EdgeColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _ShapeTex);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float y = uv.y; // 0 = source (pointe), 1 = cible (large)

                // Bloc 3 : ondulation du bord (distortion UV avant lecture de la shape texture)
                float2 waveUV = uv + float2(0, _Time.y * _WavePanSpeed);
                float waveNoise = SAMPLE_TEXTURE2D(_WaveTex, sampler_WaveTex, waveUV).r;
                float waveOffset = (waveNoise - 0.5) * 2.0 * _WaveAmount;
                float2 distortedUV = uv + float2(waveOffset, 0);

                // Silhouette = votre texture
                float shapeAlpha = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, distortedUV).r;

                // Bloc 5 : turbulence interne
                float2 turbUV = uv + float2(0, -_Time.y * _TurbPanSpeed);
                float turbRaw = SAMPLE_TEXTURE2D(_TurbTex, sampler_TurbTex, turbUV).r;

                float turbMult = lerp(_TurbMin, 1.0, turbRaw);
                float bodyAlpha = shapeAlpha * turbMult;

                float3 bodyColor = lerp(_SecondaryColor.rgb, _BaseColor.rgb, turbRaw);
                
                // Bloc 6 : balayage piloté par _Progress
                float boundary = 1.0 - _Progress;
                float keepMask = 1.0 - step(boundary, y);
                float alphaAfterWipe = bodyAlpha * keepMask;

                float distToEdge = abs(y - boundary);
                float edgeGlow = (1.0 - saturate(distToEdge / _EdgeWidth)) * shapeAlpha;

                // Bloc 7 : combinaison finale
                float finalAlpha = saturate(alphaAfterWipe + edgeGlow) * _BaseColor.a;
                float3 finalColor = lerp(bodyColor, _EdgeColor.rgb, edgeGlow);

                return float4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
}
