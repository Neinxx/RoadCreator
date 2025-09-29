// 文件路径: Assets/RoadCreator/Editor/Terrain/CPUFlattenAndTextureModule.cs
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using System.Linq;

namespace RoadSystem.Editor
{
    public class CPUFlattenAndTextureModule : ITerrainModificationModule
    {
        public string ModuleName => "CPU Advanced Baker";

        public void Execute(TerrainModificationData data, RoadDataBaker.BakerResult bakerResult, int roadLayerIndex, Texture2D roadDataMap)
        {
            var roadManager = data.RoadManager;
            var terrain = data.Terrain;
            var roadMesh = roadManager.MeshFilter.sharedMesh;
            var terrainConfig = roadManager.TerrainConfig;

            if (terrain == null || roadMesh == null || roadMesh.vertexCount == 0 || terrainConfig == null) return;

            var terrainData = terrain.terrainData;
            
            NativeArray<Vector3> roadVertices = default;
            NativeArray<int> roadTriangles = default;
            NativeArray<int> vertexToLayerIndexMap = default;
            NativeArray<float> heightMap = default;
            NativeArray<float> alphamaps1D = default;
            NativeArray<float4> alphamapData = default;
            NativeArray<float4> roadDataMapNative = default;
            
            try
            {
                // --- 1. 准备通用数据 (被两个 Job 共享) ---
                roadVertices = new NativeArray<Vector3>(roadMesh.vertices, Allocator.TempJob);
                roadTriangles = new NativeArray<int>(roadMesh.triangles, Allocator.TempJob);
                vertexToLayerIndexMap = new NativeArray<int>(roadVertices.Length, Allocator.TempJob);
                for (int i = 0; i < roadMesh.subMeshCount; i++)
                {
                    foreach (int vertexIndex in roadMesh.GetTriangles(i))
                    {
                        vertexToLayerIndexMap[vertexIndex] = i;
                    }
                }

                // --- 2. 调度高度压平 Job ---
                var heights3D = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
                heightMap = new NativeArray<float>(heights3D.Cast<float>().ToArray(), Allocator.TempJob);

                var flattenJob = new TerrainJobs.FlattenHeightmapJob
                {
                    terrainPosition = terrain.transform.position,
                    terrainSize = terrainData.size,
                    heightmapResolution = terrainData.heightmapResolution,
                    roadLocalToWorldMatrix = roadManager.transform.localToWorldMatrix,
                    roadVertices = roadVertices,
                    roadTriangles = roadTriangles,
                    flattenOffset = terrainConfig.flattenOffset,
                    heightMap = heightMap
                };
                var flattenHandle = flattenJob.Schedule(roadTriangles.Length / 3, 32);

                // --- 3. 调度纹理绘制流水线 ---
                var alphamaps3D = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
                alphamaps1D = new NativeArray<float>(alphamaps3D.Cast<float>().ToArray(), Allocator.TempJob);
                alphamapData = new NativeArray<float4>(terrainData.alphamapWidth * terrainData.alphamapHeight, Allocator.TempJob);
                
                var conversionToFloat4Job = new ConvertToFloat4Job 
                {
                    alphamaps = alphamaps1D,
                    alphamapWidth = terrainData.alphamapWidth,
                    alphamapHeight = terrainData.alphamapHeight,
                    alphamapLayers = terrainData.alphamapLayers,
                    output = alphamapData
                };
                var conversionHandle = conversionToFloat4Job.Schedule(alphamapData.Length, 256);
                alphamaps1D.Dispose(conversionHandle);

                roadDataMapNative = new NativeArray<float4>(roadDataMap.width * roadDataMap.height, Allocator.TempJob);

                var bakeJob = new TerrainJobs.BakeRoadToAlphamapJob 
                {
                    terrainPosition = terrain.transform.position,
                    terrainSize = terrainData.size,
                    alphamapWidth = terrainData.alphamapWidth,
                    alphamapHeight = terrainData.alphamapHeight, // 修正拼写错误
                    roadLocalToWorldMatrix = roadManager.transform.localToWorldMatrix,
                    roadVertices = roadVertices,
                    roadTriangles = roadTriangles,
                    vertexToLayerIndexMap = vertexToLayerIndexMap,
                    alphamapData = alphamapData,
                    roadDataMap = roadDataMapNative,
                    roadLayerSplatIndex = roadLayerIndex % 4,
                    baseLayerWeight = 1.0f
                };
                var bakeHandle = bakeJob.Schedule(roadTriangles.Length / 3, 32, conversionHandle);
                
                // --- 4. [核心修复] 创建并使用“联合句柄” ---
                var combinedHandle = JobHandle.CombineDependencies(flattenHandle, bakeHandle);
                
                // --- 5. 等待所有 Jobs 完成 ---
                combinedHandle.Complete();
                
                // --- 6. 将所有数据写回地形 (现在这里是绝对安全的) ---
                var finalHeights = heightMap.ToArray();
                System.Buffer.BlockCopy(finalHeights, 0, heights3D, 0, finalHeights.Length * sizeof(float));
                terrainData.SetHeights(0, 0, heights3D);
                
                var finalAlphamapData = alphamapData.ToArray();
                for (int y = 0; y < terrainData.alphamapHeight; y++) // 修正拼写错误
                {
                    for (int x = 0; x < terrainData.alphamapWidth; x++)
                    {
                        int index1D = y * terrainData.alphamapWidth + x;
                        float4 dataPoint = finalAlphamapData[index1D];
                        if (terrainData.alphamapLayers > 0) alphamaps3D[y, x, 0] = dataPoint.x;
                        if (terrainData.alphamapLayers > 1) alphamaps3D[y, x, 1] = dataPoint.y;
                        if (terrainData.alphamapLayers > 2) alphamaps3D[y, x, 2] = dataPoint.z;
                        if (terrainData.alphamapLayers > 3) alphamaps3D[y, x, 3] = dataPoint.w;
                    }
                }
                terrainData.SetAlphamaps(0, 0, alphamaps3D);

                var finalRoadDataMapArray = roadDataMapNative.ToArray();
                roadDataMap.SetPixels(finalRoadDataMapArray.Select(c => new Color(c.x, c.y, c.z, c.w)).ToArray());
                roadDataMap.Apply();
            }
            finally
            {
                // --- 7. 清理 ---
                if (roadVertices.IsCreated) roadVertices.Dispose();
                if (roadTriangles.IsCreated) roadTriangles.Dispose();
                if (vertexToLayerIndexMap.IsCreated) vertexToLayerIndexMap.Dispose();
                if (heightMap.IsCreated) heightMap.Dispose();
                if (alphamapData.IsCreated) alphamapData.Dispose();
                if (roadDataMapNative.IsCreated) roadDataMapNative.Dispose();
            }
        }
    }
    
    // ... (ConvertToFloat4Job 和 ConvertBackTo3DArrayJob 保持不变)
}