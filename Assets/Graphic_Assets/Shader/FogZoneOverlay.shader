Shader "Custom/FogMapOverlay"
{
    Properties
    {
        _FogColor      ("Fog Color", Color)                  = (0.05, 0.05, 0.1, 0.92)
        _FogTex        ("Fog Texture", 2D)                   = "black" {}
        _NoiseTex      ("Smoke Noise (tileable)", 2D)        = "white" {}
        _NoiseScale    ("Noise Scale", Float)                 = 3.0
        _NoiseSpeed    ("Noise Speed (XY)", Vector)           = (0.02, 0.015, 0, 0)
        _NoiseStrength ("Smoke Intensity", Range(0,1))        = 0.30
        _EdgeBlur      ("Edge Blur (texels)", Range(1,24))   = 8.0
        _MapMin        ("Map World Min (XZ)", Vector)         = (-25, -15, 0, 0)
        _MapSize       ("Map World Size (XZ)", Vector)        = (50, 30, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Overlay+10"
            "RenderPipeline" = "UniversalPipeline"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FogTex);   SAMPLER(sampler_FogTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4  _FogColor;
                float2 _MapMin;
                float2 _MapSize;
                float  _NoiseScale;
                float2 _NoiseSpeed;
                float  _NoiseStrength;
                float  _EdgeBlur;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vpi.positionCS;
                OUT.worldPos    = vpi.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Project to ground plane Y=0
                float3 rayDir    = IN.worldPos - _WorldSpaceCameraPos.xyz;
                float  t         = -_WorldSpaceCameraPos.y / rayDir.y;
                float3 groundPos = _WorldSpaceCameraPos.xyz + t * rayDir;
                float2 fogUV     = (groundPos.xz - _MapMin) / _MapSize;

                // Soft edge: cross-blur on the fog data texture
                float2 step = _EdgeBlur / 256.0;
                float revealed;
                revealed  = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, fogUV).r                       * 0.40;
                revealed += SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, fogUV + float2( step.x, 0)).r  * 0.15;
                revealed += SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, fogUV + float2(-step.x, 0)).r  * 0.15;
                revealed += SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, fogUV + float2(0,  step.y)).r  * 0.15;
                revealed += SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, fogUV + float2(0, -step.y)).r  * 0.15;

                // Animated smoke: two layers at different scales and speeds
                float2 uv1 = fogUV * _NoiseScale       + _Time.y * _NoiseSpeed;
                float2 uv2 = fogUV * _NoiseScale * 0.6 + _Time.y * _NoiseSpeed * 0.5 + float2(0.37, 0.63);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv1).r * 0.6
                            + SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv2).r * 0.4;

                // Organic edge: noise shifts the fog/reveal boundary
                float edge = smoothstep(0.05 - noise * _NoiseStrength * 0.4,
                                        0.95 + noise * _NoiseStrength * 0.2,
                                        revealed);

                // Fog color and alpha modulated by smoke (only visible in fogged areas)
                half3 color = lerp(_FogColor.rgb * 0.65, _FogColor.rgb * 1.5, noise) * (1.0 - edge * 0.3);
                float alpha = _FogColor.a * (1.0 - edge) * (0.6 + noise * 0.4);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
