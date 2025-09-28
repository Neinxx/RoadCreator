// 文件路径: Assets/RoadCreator/Editor/Terrain/CPUFlattenAndTextureModule.cs
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Linq; // 需要引入 Linq

namespace RoadSystem.Editor
{
    /// <summary>
    /// [重构后]
    /// 模块的职责变得非常纯粹：只负责处理传入的单个 TerrainModificationData。
    /// 它不再关心如何寻找地形或处理多个地形，这些都由 TerrainModifier 总指挥来完成。
    /// </summary>
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
            var terrainConfig = roadManager.TerrainConfig;
            var terrainData = terrain.terrainData;

            // --- 声明所有需要手动管理的内存 ---
            NativeArray<Vector3> meshVertices = default;
            NativeArray<int> meshTriangles = default;
            NativeArray<int2> vertexLayerInfos = default;
            NativeArray<float> heightMap = default;
            NativeArray<float> alphaMap = default;
            NativeArray<int> layerMapping = default;

            try
            {
                // --- 1. 分配内存并准备道路网格数据 ---
                meshVertices = new NativeArray<Vector3>(roadMesh.vertices, Allocator.TempJob);
                meshTriangles = new NativeArray<int>(roadMesh.triangles, Allocator.TempJob);
                // [修正] 这里不再使用 using var，所以可以自由修改
                vertexLayerInfos = new NativeArray<int2>(meshVertices.Length, Allocator.TempJob);

                for (int i = 0; i < roadMesh.subMeshCount; i++)
                {
                    if (i >= roadConfig.layerProfiles.Count) continue;
                    var profile = roadConfig.layerProfiles[i];
                    var subMeshTriangles = roadMesh.GetTriangles(i);
                    var layerInfo = new int2(i, (int)(profile.textureBlendFactor * 1000f));
                    foreach (int vertexIndex in subMeshTriangles)
                    {
                        if (vertexIndex < vertexLayerInfos.Length)
                        {
                            // 现在这样写完全没有问题
                            vertexLayerInfos[vertexIndex] = layerInfo;
                        }
                    }
                }

                // --- 2. 准备地形数据 ---
                var heightsData = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
                var alphamapsData = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);

                heightMap = new NativeArray<float>(heightsData.Cast<float>().ToArray(), Allocator.TempJob);
                alphaMap = new NativeArray<float>(alphamapsData.Cast<float>().ToArray(), Allocator.TempJob);
                
                // --- 3. 准备图层映射 ---
                layerMapping = new NativeArray<int>(roadConfig.layerProfiles.Count, Allocator.TempJob);
                for (int i = 0; i < roadConfig.layerProfiles.Count; i++)
                {
                    layerMapping[i] = EditorTerrainUtility.EnsureAndGetLayerIndex(terrain, roadConfig.layerProfiles[i].terrainLayer);
                }

                // --- 4. 创建并调度 Job ---
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
                    heightmapResolutionMinusOne = terrainData.heightmapResolution - 1,
                    alphamapResolutionMinusOne = terrainData.alphamapWidth - 1
                };

                job.Schedule().Complete();

                // --- 5. 将修改后的数据写回地形 ---
                // [修正] 从 NativeArray 写回到托管数组
                var modifiedHeights = heightMap.ToArray().To2DFloatArray(terrainData.heightmapResolution, terrainData.heightmapResolution);
                terrainData.SetHeights(0, 0, modifiedHeights);
            
                float[,,] alphaMap3D = alphaMap.To3DArray(terrainData.alphamapHeight, terrainData.alphamapWidth, terrainData.alphamapLayers);
                terrainData.SetAlphamaps(0, 0, alphaMap3D);
            }
            finally
            {
                // --- 6. [重要] 确保所有分配的内存都被释放 ---
                if (meshVertices.IsCreated) meshVertices.Dispose();
                if (meshTriangles.IsCreated) meshTriangles.Dispose();
                if (vertexLayerInfos.IsCreated) vertexLayerInfos.Dispose();
                if (heightMap.IsCreated) heightMap.Dispose();
                if (alphaMap.IsCreated) alphaMap.Dispose();
                if (layerMapping.IsCreated) layerMapping.Dispose();
            }
        }
    }
}