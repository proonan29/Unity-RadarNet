// Map terrain: relief texture (hillshade baked) blended with a tactical board texture, coverage overlay and 100 km grid.
Shader "RadarNet/Terrain"
{
    Properties
    {
        _ReliefTex ("Relief", 2D) = "white" {}
        _BoardTex ("Board", 2D) = "black" {}
        _CovTex ("Coverage", 2D) = "black" {}
        _BoardMix ("Board mix", Range(0,1)) = 0
        _CovAlpha ("Coverage alpha", Range(0,1)) = 0
        _GridAlpha ("Grid alpha", Range(0,1)) = 0.18
        _MapSize ("Map size (units)", Float) = 1000
        _GridStep ("Grid step (units)", Float) = 100
        _Haze ("Distance haze", Float) = 0.00035
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ReliefTex); SAMPLER(sampler_ReliefTex);
            TEXTURE2D(_BoardTex); SAMPLER(sampler_BoardTex);
            TEXTURE2D(_CovTex); SAMPLER(sampler_CovTex);
            CBUFFER_START(UnityPerMaterial)
                float _BoardMix, _CovAlpha, _GridAlpha, _MapSize, _GridStep, _Haze;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.world = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.world);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.world.xz / _MapSize + 0.5;
                half3 relief = SAMPLE_TEXTURE2D(_ReliefTex, sampler_ReliefTex, uv).rgb;
                half3 board = SAMPLE_TEXTURE2D(_BoardTex, sampler_BoardTex, uv).rgb;
                half3 c = lerp(relief, board, _BoardMix);
                half4 cov = SAMPLE_TEXTURE2D(_CovTex, sampler_CovTex, uv);
                c = lerp(c, cov.rgb, cov.a * _CovAlpha);
                // 100 km grid (anti-aliased)
                float2 g = i.world.xz / _GridStep;
                float2 w = fwidth(g) * 1.2;
                float2 l = smoothstep(w, 0, abs(frac(g + 0.5) - 0.5));
                float grid = max(l.x, l.y);
                c = lerp(c, lerp(half3(1,1,1), half3(0.45,0.8,1), _BoardMix), grid * _GridAlpha);
                // gentle distance haze (3D view only)
                float d = distance(_WorldSpaceCameraPos, i.world);
                float haze = (1 - exp(-d * _Haze)) * (1 - _BoardMix) * 0.55;
                c = lerp(c, half3(0.62, 0.72, 0.82), haze);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
