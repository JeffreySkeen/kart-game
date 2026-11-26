Shader "Kart/WetGround"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (0.35, 0.35, 0.4, 1)

        _Wetness("Wetness", Range(0, 1)) = 1
        _DrySmoothness("Dry Smoothness", Range(0, 1)) = 0.2
        _WetSmoothness("Wet Smoothness", Range(0, 1)) = 0.92
        _WetDarken("Wet Darkening", Range(0, 1)) = 0.45

        [Header(Ripples)]
        _RippleDensity("Ripple Density (cells per meter)", Range(0.5, 8)) = 2.5
        _RippleSpeed("Ripple Speed", Range(0.1, 3)) = 0.9
        _RippleStrength("Ripple Strength", Range(0, 2)) = 0.6
        _RippleFrequency("Ripple Ring Frequency", Range(20, 200)) = 110
        _RippleFadeDistance("Ripple Fade Distance", Range(5, 100)) = 40

        [Header(Tire Tracks)]
        _TrackDarken("Track Darkening", Range(0, 1)) = 0.4
        _TrackRoughen("Track Roughness", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP // required for Forward+ (this project's renderer)
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // Set globally by TireTracks.cs. xy = map center (world XZ), z = map size in meters (0 = off).
            TEXTURE2D(_TrackMap);
            SAMPLER(sampler_TrackMap);
            float4 _TrackMapRect;

            CBUFFER_START(UnityPerMaterial)
                half _TrackDarken;
                half _TrackRoughen;
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Wetness;
                half _DrySmoothness;
                half _WetSmoothness;
                half _WetDarken;
                half _RippleDensity;
                half _RippleSpeed;
                half _RippleStrength;
                half _RippleFrequency;
                half _RippleFadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vn = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS = vn.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(vp.positionCS.z);
                OUT.shadowCoord = GetShadowCoord(vp);
                return OUT;
            }

            float3 Hash3(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yxz + 33.33);
                return frac((q.xxy + q.yzz) * q.zyx);
            }

            // One layer of drops. Each grid cell spawns a ring that expands and fades,
            // then respawns at a new random spot. Returns the ring's slope direction.
            float2 RippleLayer(float2 p, float time)
            {
                float2 id = floor(p);
                float2 f = frac(p) - 0.5;

                float phase = Hash3(id).z;
                float tt = time * _RippleSpeed + phase;
                float cycle = floor(tt);
                float t = frac(tt);

                // New position every cycle. Max offset 0.25 + max radius 0.25 stays inside the cell.
                float3 h = Hash3(id + cycle * 7.13);
                float2 center = (h.xy - 0.5) * 0.5;

                float2 v = f - center;
                float d = length(v);
                float radius = t * 0.25;
                float ring = d - radius;

                float mask = saturate(1.0 - abs(ring) / 0.12);
                float wave = cos(ring * _RippleFrequency);
                float fade = (1.0 - t) * (1.0 - t);

                return (v / max(d, 1e-4)) * wave * mask * fade;
            }

            float2 Ripples(float2 posXZ)
            {
                float2 p = posXZ * _RippleDensity;
                float2 r = RippleLayer(p, _Time.y);
                r += RippleLayer(p * 1.73 + 17.3, _Time.y * 1.21 + 3.7);
                return r;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;
                albedo *= lerp(1.0h, 1.0h - _WetDarken, _Wetness);

                // Tire track amount (0..1) from the world-space track map
                float2 trackUV = (IN.positionWS.xz - _TrackMapRect.xy) / max(_TrackMapRect.z, 1e-4) + 0.5;
                float inside = step(0.0, min(trackUV.x, trackUV.y)) * step(max(trackUV.x, trackUV.y), 1.0)
                             * step(1e-3, _TrackMapRect.z);
                half track = SAMPLE_TEXTURE2D_LOD(_TrackMap, sampler_TrackMap, trackUV, 0).r * inside;
                albedo *= 1.0h - track * _TrackDarken;

                // Ripples are laid out in world XZ so they don't stretch with the mesh scale.
                float dist = distance(_WorldSpaceCameraPos, IN.positionWS);
                float distFade = saturate(1.0 - dist / _RippleFadeDistance);
                float2 rip = Ripples(IN.positionWS.xz) * _RippleStrength * _Wetness * distFade * (1.0 - track);

                // Tilt the normal by the ripple slope (assumes a mostly horizontal surface).
                float3 normalWS = normalize(IN.normalWS);
                normalWS = normalize(normalWS + float3(rip.x, 0, rip.y));

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = 0;
                s.specular = 0;
                s.smoothness = lerp(_DrySmoothness, _WetSmoothness, _Wetness) * (1.0h - track * _TrackRoughen);
                s.occlusion = 1;
                s.alpha = 1;
                s.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.positionCS = IN.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = IN.shadowCoord;
                inputData.fogCoord = IN.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, s);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
