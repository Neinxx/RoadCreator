Shader "RoadCreator/RoadPreviewShader"
{
    Properties
    {
        // 我们会通过脚本传递数组，所以这里可以留空或只放一些测试参数
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 与Compute Shader匹配的结构体
            struct RoadLayerProfile
            {
                float width;
                float verticalOffset;
                int terrainLayerIndex; // 在这里用不上
                float textureBlendFactor; // 在这里用不上
            };

            // 顶点着色器输入
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            // 顶点到片元着色器的输出
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            // -- - 从C#传入的参数 -- -
            // 为简化，这里假设最多4个图层。更复杂的系统会用Texture2DArray
            TEXTURE2D(_Layer0_MainTex); SAMPLER(sampler_Layer0_MainTex);
            TEXTURE2D(_Layer1_MainTex); SAMPLER(sampler_Layer1_MainTex);
            TEXTURE2D(_Layer2_MainTex); SAMPLER(sampler_Layer2_MainTex);
            TEXTURE2D(_Layer3_MainTex); SAMPLER(sampler_Layer3_MainTex);
            float4 _Layer0_MainTex_ST; // Tiling and Offset

            CBUFFER_START(UnityPerMaterial)
            float _LayerWidths[4];
            float _LayerVerticalOffsets[4];
            int _LayerCount;
            float _EdgeWobbleAmount;
            float _EdgeWobbleFrequency;
            CBUFFER_END

            // -- - 与Compute Shader一致的噪声函数 -- -
            float2 hash(float2 p) {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return - 1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }
            float noise(float2 p) {
                float2 i = floor(p); float2 f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(dot(hash(i + float2(0, 0)), f - float2(0, 0)), dot(hash(i + float2(1, 0)), f - float2(1, 0)), f.x),
                lerp(dot(hash(i + float2(0, 1)), f - float2(0, 1)), dot(hash(i + float2(1, 1)), f - float2(1, 1)), f.x), f.y);
            }
            float GetEdgeWobble(float3 worldPos) {
                if (_EdgeWobbleAmount <= 0.0 || _EdgeWobbleFrequency <= 0.0) return 0.0;
                return noise(worldPos.xz * _EdgeWobbleFrequency) * _EdgeWobbleAmount;
            }

            // -- - 顶点着色器 -- -
            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                // 暂时不在这里做顶点抖动，因为网格生成时已经做了。
                // 一个更高级的实现会在这里做抖动，并使用一个平直的输入网格。
                // 为保持简单，我们假设输入的网格形状已经是正确的。

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            // -- - 片元着色器 -- -
            half4 frag (Varyings IN) : SV_Target
            {
                // uv.x 0 - 1 代表了从路中心到边缘的距离
                // 我们需要把它映射到世界单位的距离
                float totalWidth = 0;
                for(int k = 0; k < _LayerCount; k ++) { totalWidth += _LayerWidths[k]; }

                // 通过uv.x可以判断是左侧还是右侧，但对于Unlit材质这不是必须的
                float distFromCenter = IN.uv.x * totalWidth;

                // -- - 查找所属图层 -- -
                float currentOffsetFromCenter = 0;
                int targetLayerIndex = - 1;
                for (int i = 0; i < _LayerCount; i ++)
                {
                    float layerOuterEdge = currentOffsetFromCenter + _LayerWidths[i];
                    if (i == _LayerCount - 1)
                    {
                        // wobble只影响最外层的纹理混合
                        layerOuterEdge += GetEdgeWobble(IN.positionWS);
                    }
                    if (distFromCenter <= layerOuterEdge)
                    {
                        targetLayerIndex = i;
                        break;
                    }
                    currentOffsetFromCenter = layerOuterEdge;
                }

                half4 finalColor = half4(0.5, 0.5, 0.5, 1); // 默认灰色

                // 根据图层索引采样对应的贴图
                if (targetLayerIndex == 0) finalColor = SAMPLE_TEXTURE2D(_Layer0_MainTex, sampler_Layer0_MainTex, IN.uv * _Layer0_MainTex_ST.xy + _Layer0_MainTex_ST.zw);
                else if (targetLayerIndex == 1) finalColor = SAMPLE_TEXTURE2D(_Layer1_MainTex, sampler_Layer1_MainTex, IN.uv);
                else if (targetLayerIndex == 2) finalColor = SAMPLE_TEXTURE2D(_Layer2_MainTex, sampler_Layer2_MainTex, IN.uv);
                else if (targetLayerIndex == 3) finalColor = SAMPLE_TEXTURE2D(_Layer3_MainTex, sampler_Layer3_MainTex, IN.uv);

                return finalColor;
            }
            ENDHLSL
        }
    }
}