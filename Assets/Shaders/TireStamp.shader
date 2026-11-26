// Used with Graphics.Blit by TireTracks.cs. Reads last frame's track map, fades it,
// then stamps up to 4 line segments (one per tire, previous position -> current position).
Shader "Hidden/Kart/TireStamp"
{
    Properties
    {
        _MainTex("Previous Track Map", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _SegAB[4];      // xy = segment start (map UV), zw = segment end (map UV)
            float4 _SegParams[4];  // x = radius (map UV), y = strength 0..1
            float _SegCount;
            float _Fade;           // amount removed from the map this frame

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float frag(Varyings IN) : SV_Target
            {
                float v = max(0.0, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).r - _Fade);

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    if (i >= (int)_SegCount) break;

                    float2 a = _SegAB[i].xy;
                    float2 b = _SegAB[i].zw;
                    float2 pa = IN.uv - a;
                    float2 ba = b - a;
                    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-10));
                    float d = length(pa - ba * h);

                    float r = _SegParams[i].x;
                    float s = _SegParams[i].y * (1.0 - smoothstep(r * 0.5, r, d));
                    v = max(v, s);
                }
                return v;
            }
            ENDHLSL
        }
    }
}
