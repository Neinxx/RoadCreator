// GPUFlattenAndTextureModule.cs
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using RoadSystem;
using System.Collections.Generic;
using RoadSystem.Editor;

namespace RoadSystem
{
    // 这个结构体的内存布局必须与Compute Shader中的结构体完全匹配
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RoadLayerProfileGPU
    {
        public float width;
        public float verticalOffset;
        public int terrainLayerIndex;
        public float textureBlendFactor;
    }
    public class GPUFlattenAndTextureModule : ITerrainModificationModule
    {
        public string ModuleName => "GPU Layered Synchronous Bake & Blend";
        private readonly ComputeShader terrainModifierCS;

        public GPUFlattenAndTextureModule()
        {
            // 确保你的Compute Shader文件路径正确
            const string computeShaderPath = "Assets/RoadCreator/shader/RoadTerrainModifier.compute";
            terrainModifierCS = AssetDatabase.LoadAssetAtPath<ComputeShader>(computeShaderPath);
        }

        public void Execute(TerrainModificationData data)
        {
            if (terrainModifierCS == null)
            {
                Debug.LogError("GPU模块所需的Compute Shader资源未找到。");
                // 如果需要，可以回退到CPU模式
                // new CPUFlattenAndTextureModule().Execute(data);
                return;
            }

            var terrain = data.Terrain;
            var terrainData = terrain.terrainData;
            var roadManager = data.RoadManager;
            var roadConfig = roadManager.RoadConfig;

            if (roadConfig.layerProfiles.Count == 0) return;

            // --- 资源准备 ---
            RenderTexture influenceMapRT = RenderTexture.GetTemporary(terrainData.heightmapResolution, terrainData.heightmapResolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            influenceMapRT.enableRandomWrite = true;
            // ... (其他RenderTexture的准备工作保持不变) ...
            RenderTexture finalHeightRT = RenderTexture.GetTemporary(terrainData.heightmapResolution, terrainData.heightmapResolution, 0, RenderTextureFormat.RFloat);
            finalHeightRT.enableRandomWrite = true;
            RenderTexture finalAlphaRT = RenderTexture.GetTemporary(terrainData.alphamapWidth, terrainData.alphamapHeight, 0, RenderTextureFormat.ARGB32);
            finalAlphaRT.enableRandomWrite = true;


            // --- [核心修改] 创建并填充ComputeBuffer ---
            ComputeBuffer splinePointsBuffer = null;
            ComputeBuffer layerProfilesBuffer = null;

            try
            {
                // 1. 准备样条线点数据
                var points = data.ControlPoints.Select(p => p.position).ToArray();
                if (points.Length < 2) return;
                splinePointsBuffer = new ComputeBuffer(points.Length, sizeof(float) * 3);
                splinePointsBuffer.SetData(points);

                // 2. 准备分层剖面数据
                var profilesForGPU = roadConfig.layerProfiles.Select(p => new RoadLayerProfileGPU
                {
                    width = p.width,
                    verticalOffset = p.verticalOffset,
                    textureBlendFactor = p.textureBlendFactor,
                    terrainLayerIndex = EditorTerrainUtility.EnsureAndGetLayerIndex(terrain, p.terrainLayer)
                }).ToArray();

                layerProfilesBuffer = new ComputeBuffer(profilesForGPU.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(RoadLayerProfileGPU)));
                layerProfilesBuffer.SetData(profilesForGPU);

                // --- Pass 1: 生成影响图 (此Pass可以被合并或保留，取决于你的具体效果) ---
                // (为简化，我们假设主要逻辑在BlendTerrain Kernel中)

                // --- Pass 2: 混合地形 ---
                int blendKernel = terrainModifierCS.FindKernel("BlendTerrain");
                SetCommonParameters(terrainModifierCS, blendKernel, data); // 设置通用参数

                // 绑定Buffers
                terrainModifierCS.SetBuffer(blendKernel, "SplinePoints", splinePointsBuffer);
                terrainModifierCS.SetBuffer(blendKernel, "LayerProfiles", layerProfilesBuffer);

                // 绑定Textures (Render Textures)
                terrainModifierCS.SetTexture(blendKernel, "ExistingHeightMap", terrainData.heightmapTexture);
                terrainModifierCS.SetTexture(blendKernel, "ExistingAlphaMap", terrainData.alphamapTextures.Length > 0 ? terrainData.alphamapTextures[0] : Texture2D.blackTexture);
                terrainModifierCS.SetTexture(blendKernel, "HeightMapResult", finalHeightRT);
                terrainModifierCS.SetTexture(blendKernel, "AlphaMapResult", finalAlphaRT);

                // 执行Compute Shader
                int threadGroupsX = Mathf.CeilToInt(terrainData.heightmapResolution / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(terrainData.heightmapResolution / 8.0f);
                terrainModifierCS.Dispatch(blendKernel, threadGroupsX, threadGroupsY, 1);

                // --- GPU回读与应用数据 (这部分逻辑保持不变) ---
                var heightRequest = AsyncGPUReadback.Request(finalHeightRT);
                var alphaRequest = AsyncGPUReadback.Request(finalAlphaRT);
                heightRequest.WaitForCompletion();
                alphaRequest.WaitForCompletion();

                if (heightRequest.hasError) Debug.LogError("GPU高度图回读失败！");
                else terrainData.SetHeights(0, 0, heightRequest.GetData<float>().ToArray().To2DFloatArray(terrainData.heightmapResolution, terrainData.heightmapResolution));

                if (alphaRequest.hasError) Debug.LogError("GPU纹理图回读失败！");
                else terrainData.SetAlphamaps(0, 0, alphaRequest.GetData<Color32>().ToArray().To3DFloatArray(terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers));
            }
            finally
            {
                // --- [重要] 确保所有Buffer都被释放，防止内存泄漏 ---
                splinePointsBuffer?.Release();
                layerProfilesBuffer?.Release();
                RenderTexture.ReleaseTemporary(influenceMapRT);
                RenderTexture.ReleaseTemporary(finalHeightRT);
                RenderTexture.ReleaseTemporary(finalAlphaRT);
            }
        }


        private void SetCommonParameters(ComputeShader cs, int kernel, TerrainModificationData data)
        {
            var terrain = data.Terrain;
            var terrainData = terrain.terrainData;
            var roadConfig = data.RoadManager.RoadConfig;
            var terrainConfig = data.RoadManager.TerrainConfig;

            cs.SetInt("splinePointCount", data.ControlPoints.Count);
            cs.SetInt("layerProfileCount", roadConfig.layerProfiles.Count); // 新增：传递图层数量
            cs.SetVector("terrainPosition", terrain.transform.position);
            cs.SetVector("terrainSize", terrainData.size);
            cs.SetInt("heightMapResolution", terrainData.heightmapResolution);
            cs.SetInt("alphaMapResolution", terrainData.alphamapWidth);
            cs.SetInt("alphaMapLayerCount", terrainData.alphamapLayers);

            // --- [旧参数 - 已被ComputeBuffer取代] ---
            // cs.SetFloat("roadWidth", roadConfig.roadWidth);
            // cs.SetFloat("shoulderWidth", roadConfig.shoulderWidth);
            // ...其他分层参数...

            // --- [保留的全局参数] ---
            cs.SetFloat("edgeWobbleAmount", roadConfig.edgeWobbleAmount);
            cs.SetFloat("edgeWobbleFrequency", roadConfig.edgeWobbleFrequency);
            cs.SetFloat("flattenStrength", terrainConfig.flattenStrength);
            cs.SetFloat("flattenOffset", terrainConfig.flattenOffset);
            cs.SetFloat("textureNoiseScale", terrainConfig.textureNoiseScale);
        }

        public void Execute(TerrainModificationData data, RoadDataBaker.BakerResult bakerResult, int roadLayerIndex, Texture2D roadDataMap)
        {
            throw new System.NotImplementedException();
        }
    }
}