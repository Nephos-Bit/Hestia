Shader "WinterMarket/SnowCap"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.788, 0.863, 0.910, 1)
        _SideColor ("Side Color", Color) = (0.557, 0.678, 0.753, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Unlit"
        }

        Pass
        {
            Name "SnowCapForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _SideColor;
            CBUFFER_END

            half4 _WinterMarketAmbient;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half facingUp = step(0.5h, normalWS.y);
                half3 albedo = lerp(_SideColor.rgb, _TopColor.rgb, facingUp);

                // Authored cap colors stay below the paper-white sky. Ambient only
                // darkens them toward night so the noon sun cannot clip the snow away.
                half3 ambient = _WinterMarketAmbient.rgb;
                half luminance = dot(ambient, half3(0.2126h, 0.7152h, 0.0722h));
                half grade = luminance < 0.001h
                    ? 1.0h
                    : lerp(0.62h, 1.0h, saturate(luminance));
                half3 color = MixFog(albedo * grade, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return 0.0h;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
