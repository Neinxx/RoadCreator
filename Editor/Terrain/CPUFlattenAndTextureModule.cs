// 文件路径: Assets/RoadCreator/Editor/Terrain/CPUFlattenAndTextureModule.cs
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;

namespace RoadSystem
{
    public class CPUFlattenAndTextureModule : ITerrainModificationModule
    {
        public string ModuleName => "CPU Direct Triangle-Based Processor";

        public void Execute(TerrainModificationData data)
        {
            var roadManager = data.RoadManager;
            var terrain = data.Terrain;
            var roadMesh = roadManager.MeshFilter.sharedMesh;

            if (terrain == null || roadMesh == null || roadMesh.vertexCount == 0) return;

            var roadConfig = roadManager.RoadConfig;

            // --- 准备共享的道路数据 ---
            var meshVertices = new NativeArray<Vector3>(roadMesh.vertices, Allocator.TempJob);
            var meshTriangles = new NativeArray<int>(roadMesh.triangles, Allocator.TempJob);
            var vertexLayerInfos = new NativeArray<int2>(meshVertices.Length, Allocator.TempJob);
            
            for (int i = 0; i < roadMesh.subMeshCount; i++)
            {
                if (i >= roadConfig.layerProfiles.Count) continue;
                var profile = roadConfig.layerProfiles[i];
                var subMeshTriangles = roadMesh.GetTriangles(i);
                var layerInfo = new int2(i, (int)(profile.textureBlendFactor * 1000f));
                foreach (int vertexIndex in subMeshTriangles)
                {
                    if (vertexIndex < vertexLayerInfos.Length) vertexLayerInfos[vertexIndex] = layerInfo;
                }
            }

            // --- 直接在当前地形上应用修改 ---
            ApplyToSingleTerrain(terrain, roadManager, meshVertices, meshTriangles, vertexLayerInfos);

            // --- 清理共享数据 ---
            meshVertices.Dispose();
            meshTriangles.Dispose();
            vertexLayerInfos.Dispose();
        }

        private unsafe void ApplyToSingleTerrain(Terrain terrain, RoadManager roadManager,
            NativeArray<Vector3> meshVertices, NativeArray<int> meshTriangles, NativeArray<int2> vertexLayerInfos)
        {
            var terrainData = terrain.terrainData;
            var terrainConfig = roadManager.TerrainConfig;
            var roadConfig = roadManager.RoadConfig;

            // --- 获取地形的原始数据 ---
            var heightsData = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
            var alphamapsData = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
            
            // --- 使用 UnsafeUtility 高效地将地形数据复制到 NativeArray ---
            var heightMap = new NativeArray<float>(heightsData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            fixed(void* ptr = heightsData) { UnsafeUtility.MemCpy(heightMap.GetUnsafePtr(), ptr, heightsData.Length * sizeof(float)); }
            
            var alphaMap = new NativeArray<float>(alphamapsData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            fixed(void* ptr = alphamapsData) { UnsafeUtility.MemCpy(alphaMap.GetUnsafePtr(), ptr, alphamapsData.Length * sizeof(float)); }

            // --- 准备图层映射 ---
            var layerMapping = new NativeArray<int>(roadConfig.layerProfiles.Count, Allocator.TempJob);
            for (int i = 0; i < roadConfig.layerProfiles.Count; i++)
            {
                layerMapping[i] = EditorTerrainUtility.EnsureAndGetLayerIndex(terrain, roadConfig.layerProfiles[i].terrainLayer);
            }
            
            // --- 创建并调度优化后的Job ---
            var job = new TerrainJobs.TriangleBasedProcessingJob
            {
                terrainPosition = terrain.transform.position,
                terrainSize = terrainData.size,
                heightmapResolution = terrainData.heightmapResolution,
                roadTransform = roadManager.transform.localToWorldMatrix,
                flattenOffset = terrainConfig.flattenOffset,
                vertices = meshVertices,
                triangles = meshTriangles,
                vertexLayerInfos = vertexLayerInfos,
                heightMap = heightMap,
                alphaMap = alphaMap,
                alphamapWidth = terrainData.alphamapWidth,
                alphamapHeight = terrainData.alphamapHeight,
                alphamapLayers = terrainData.alphamapLayers,
                flattenStrength = terrainConfig.flattenStrength,
                layerMapping = layerMapping,
                
                // ✅ 添加坐标转换所需参数
                heightmapResolutionMinusOne = terrainData.heightmapResolution - 1,
                alphamapResolutionMinusOne = terrainData.alphamapWidth - 1 // 假设宽度等于高度
            };

            // 执行单线程Job（性能更好，因为避免了对每个像素检查所有三角形）
            JobHandle handle = job.Schedule();
            handle.Complete();

            // --- 将修改后的数据写回地形 ---
            fixed(void* ptr = heightsData) { UnsafeUtility.MemCpy(ptr, heightMap.GetUnsafePtr(), heightsData.Length * sizeof(float)); }
            terrainData.SetHeights(0, 0, heightsData);
            
            // 纹理混合图写回（修复版）
            float[,,] alphaMap3D = alphaMap.To3DArray(terrainData.alphamapHeight, terrainData.alphamapWidth, terrainData.alphamapLayers);
            terrainData.SetAlphamaps(0, 0, alphaMap3D);

            // --- 清理本地数据 ---
            heightMap.Dispose();
            alphaMap.Dispose();
            layerMapping.Dispose();
        }
    }

    // 多地形处理类
    public class MultiTerrainProcessor
    {
        public void Execute(RoadManager roadManager)
        {
            var roadMesh = roadManager.MeshFilter.sharedMesh;
            if (roadMesh == null || roadMesh.vertexCount == 0) return;

            var roadConfig = roadManager.RoadConfig;
            Bounds roadWorldBounds = roadMesh.GetWorldBounds(roadManager.transform);
            List<Terrain> affectedTerrains = EditorTerrainUtility.FindAffectedTerrains(roadWorldBounds);
            if (affectedTerrains.Count == 0) return;

            // --- 准备共享的道路数据 ---
            var meshVertices = new NativeArray<Vector3>(roadMesh.vertices, Allocator.TempJob);
            var meshTriangles = new NativeArray<int>(roadMesh.triangles, Allocator.TempJob);
            var vertexLayerInfos = new NativeArray<int2>(meshVertices.Length, Allocator.TempJob);

            for (int i = 0; i < roadMesh.subMeshCount; i++)
            {
                if (i >= roadConfig.layerProfiles.Count) continue;
                var profile = roadConfig.layerProfiles[i];
                var subMeshTriangles = roadMesh.GetTriangles(i);
                var layerInfo = new int2(i, (int)(profile.textureBlendFactor * 1000f));
                foreach (int vertexIndex in subMeshTriangles)
                {
                    if (vertexIndex < vertexLayerInfos.Length) vertexLayerInfos[vertexIndex] = layerInfo;
                }
            }

            // --- 为每个受影响的地形分别调度Job ---
            foreach (var terrain in affectedTerrains)
            {
                // ✅ 你的简洁写法
                var terrainData = new TerrainModificationData(terrain, roadManager);
                var module = new CPUFlattenAndTextureModule();
                module.Execute(terrainData);
            }

            // --- 清理共享数据 ---
            meshVertices.Dispose();
            meshTriangles.Dispose();
            vertexLayerInfos.Dispose();

            Debug.Log("所有地形修改任务已完成！");
        }
    }
}