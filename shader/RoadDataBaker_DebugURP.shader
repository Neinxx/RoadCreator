Shader "Hidden/RoadCreator/RoadDataBakerURP"
{
    Properties
    {
        _MaxHeight("Max Height", Float) = 1000.0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex vert
            #pragma fragment frag

            CBUFFER_START(UnityPerMaterial)
            float _MaxHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // R通道 : 存储归一化后的高度 (世界Y坐标 / 地形最大高度)
                // A通道 : 存储一个“蒙版”，1.0表示这里有路面
                half normalizedHeight = saturate(IN.worldPos.y / _MaxHeight);
                return half4(normalizedHeight, 0, 0, 1.0);
            }
            ENDHLSL
        }
    }
}