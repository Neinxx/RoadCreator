// URP版本 - 将世界高度和蒙版烘焙到RenderTexture
Shader "Hidden/RoadCreator/RoadDataBakerURP"
{
    Properties
    {
        // 外部参数，用于归一化高度。最大值通常是地形最大高度。
        _MaxHeight("Max Height", Float) = 1000.0
    }
    SubShader
    {
        // URP管线标签
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            // 为透明渲染设置正确的状态
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            // 声明HLSL代码块
            HLSLPROGRAM
            // 包含URP的核心库，这是URP Shader的必要部分
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 定义顶点着色器的入口点和片元着色器的入口点
            #pragma vertex vert
            #pragma fragment frag

            // CBUFFER块用于从C#接收参数
            CBUFFER_START(UnityPerMaterial)
            float _MaxHeight;
            CBUFFER_END

            // 顶点着色器的输入结构
            struct Attributes
            {
                float4 positionOS : POSITION; // 顶点在模型空间中的位置
            };

            // 在顶点着色器和片元着色器之间传递的数据结构
            struct Varyings
            {
                float4 positionCS : SV_POSITION; // 顶点在裁剪空间中的位置
                float3 worldPos : TEXCOORD0; // 顶点在世界空间中的位置
            };

            // 顶点着色器
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // 将顶点位置从模型空间转换到世界空间
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                // 将顶点位置从世界空间转换到裁剪空间
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                return OUT;
            }

            // 片元着色器
            half4 frag(Varyings IN) : SV_Target
            {
                // 核心逻辑 :
                // R通道 : 存储归一化后的高度 (世界Y坐标 / 最大高度)
                // G, B通道 : 暂不使用
                // A通道 : 存储一个"蒙版"，1.0表示这里有路面

                half normalizedHeight = saturate(IN.worldPos.y / _MaxHeight);

                // 返回一个明显的颜色用于测试
                // R : 归一化高度, G, B : 未使用, A : 蒙版
                return half4(normalizedHeight, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }
}