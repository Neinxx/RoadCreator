
Shader "RoadCreator/URPTerrainRoad"
{
    
    Properties
    {
       
        [Header(Road Texturing)]
        _RoadAtlas("Road Texture Atlas", 2D) = "white" {}
        _RoadDataMap("Road Data Map (UVs & Index)", 2D) = "black" {}
        _RoadLayerIndex("Road Layer Index (0-3)", Float) = 0

        // --- 标准地形图层属性 ---
        // URP 地形系统会通过 MaterialPropertyBlock 自动填充这些
        [Header(Standard Terrain Layers)]
        [HideInInspector] _Control("Control (Splat)", 2D) = "black" {}
        [HideInInspector] _Splat0("Layer 1 (R)", 2D) = "white" {}
        [HideInInspector] _Splat1("Layer 2 (G)", 2D) = "white" {}
        [HideInInspector] _Splat2("Layer 3 (B)", 2D) = "white" {}
        [HideInInspector] _Splat3("Layer 4 (A)", 2D) = "white" {}
        
       
    }

    
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry-100" // 确保在标准几何体之后渲染
        }

        Pass
        {
            Name "TerrainLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

           
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

          
            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float4 _Splat0_ST; 
                float _RoadLayerIndex;
            CBUFFER_END

            
            TEXTURE2D(_Control);        SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);         SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);         SAMPLER(sampler_Splat1);
            TEXTURE2D(_RoadAtlas);      SAMPLER(sampler_RoadAtlas);
            TEXTURE2D(_RoadDataMap);    SAMPLER(sampler_RoadDataMap);

          
            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float3 positionWS   : TEXCOORD2;
            };

         
            Varyings Vertex(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _Control);
                return OUT;
            }

           
            half4 Fragment(Varyings IN) : SV_Target
            {
                // --- ① 获取地形 Splat Map 权重 ---
                half4 splatWeights = SAMPLE_TEXTURE2D(_Control, sampler_Control, IN.uv);

                // --- ② 获取道路图层的权重 ---
                half roadWeight = 0;
                if (_RoadLayerIndex == 0) roadWeight = splatWeights.r;
                else if (_RoadLayerIndex == 1) roadWeight = splatWeights.g;
                else if (_RoadLayerIndex == 2) roadWeight = splatWeights.b;
                else roadWeight = splatWeights.a;

                // --- ③ 读取并解码的“秘密指导图” ---
                half4 roadData = SAMPLE_TEXTURE2D(_RoadDataMap, sampler_RoadDataMap, IN.uv);
                
                // R = U, G = V
                float2 roadUV = roadData.xy;
                // B = 图集索引 (暂时用不上，因为图集UV已经包含了这个信息)
                // A = 混合权重

                // --- ④ 使用自定义 UV 采样道路图集 ---
                half4 roadColor = SAMPLE_TEXTURE2D(_RoadAtlas, sampler_RoadAtlas, roadUV);

                // --- ⑤ 采样默认的背景地形纹理 (这里以第一层为例) ---
                float2 baseUV = TRANSFORM_TEX(IN.uv, _Splat0);
                half4 baseColor = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, baseUV);

                // --- ⑥ 最终混合 ---
                // 使用 roadWeight 在基础颜色和道路颜色之间进行线性插值
                half4 finalColor = lerp(baseColor, roadColor, roadWeight);

                // --- 光照计算 (简化的兰伯特光照) ---
                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction);
                half3 normal = normalize(IN.normalWS);
                half dotNL = saturate(dot(normal, lightDir));
                
                half3 finalLighting = mainLight.color * dotNL;

                // 应用光照
                finalColor.rgb *= finalLighting;

                return finalColor;
            }
            ENDHLSL
        }
    }
    
    
    FallBack "Universal Render Pipeline/Lit"
}