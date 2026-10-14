// Vertex-coloured, alpha-blended lines and symbols (radar rings, tracks, data link). _ZTest 4 = LEqual, 8 = Always.
Shader "RadarNet/Overlay"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" }
        Pass
        {
            Name "Overlay"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; return o; }
            half4 frag(V i) : SV_Target { return i.col; }
            ENDHLSL
        }
    }
}
