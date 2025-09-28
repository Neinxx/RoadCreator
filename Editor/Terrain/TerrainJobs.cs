// 文件路径: Assets/RoadCreator/Editor/Terrain/TerrainJobs.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RoadSystem
{
    public static class TerrainJobs
    {
        [BurstCompile]
        public struct TriangleBasedProcessingJob : IJob
        {
            // --- 地形数据 ---
            public float3 terrainPosition;
            public float3 terrainSize;
            public int heightmapResolution;

            // --- 道路数据 ---
            [ReadOnly] public float4x4 roadTransform;
            [ReadOnly] public float flattenOffset;
            [ReadOnly] public NativeArray<Vector3> vertices;
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<int2> vertexLayerInfos;

            // --- 修改目标 ---
            public NativeArray<float> heightMap;
            public NativeArray<float> alphaMap;

            // --- 其他配置 ---
            public int alphamapWidth;
            public int alphamapHeight;
            public int alphamapLayers;
            public float flattenStrength;
            [ReadOnly] public NativeArray<int> layerMapping;

            // --- 添加用于坐标转换的参数 ---
            public int heightmapResolutionMinusOne; // heightmapResolution - 1
            public int alphamapResolutionMinusOne;  // alphamapWidth - 1 (假设 alphamapWidth == alphamapHeight)

            public void Execute()
            {
                // 遍历所有三角形
                for (int triIdx = 0; triIdx < triangles.Length / 3; triIdx++)
                {
                    ProcessTriangle(triIdx);
                }
            }

            private void ProcessTriangle(int triIdx)
            {
                // 获取当前三角形的三个顶点索引
                int i0 = triangles[triIdx * 3 + 0];
                int i1 = triangles[triIdx * 3 + 1];
                int i2 = triangles[triIdx * 3 + 2];

                // 检查顶点索引是否有效
                if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length) 
                    return;

                // 将顶点从模型空间转换到世界空间
                float3 v0 = math.transform(roadTransform, vertices[i0]);
                float3 v1 = math.transform(roadTransform, vertices[i1]);
                float3 v2 = math.transform(roadTransform, vertices[i2]);

                // 计算该三角形在世界空间XZ平面上的包围盒
                float minX = math.min(v0.x, math.min(v1.x, v2.x));
                float maxX = math.max(v0.x, math.max(v1.x, v2.x));
                float minZ = math.min(v0.z, math.min(v1.z, v2.z));
                float maxZ = math.max(v0.z, math.max(v1.z, v2.z));

                // 将世界空间的包围盒转换为地形高度图的像素坐标范围
                int startX = (int)(((minX - terrainPosition.x) / terrainSize.x) * heightmapResolution);
                int endX   = (int)(((maxX - terrainPosition.x) / terrainSize.x) * heightmapResolution) + 1;
                int startY = (int)(((minZ - terrainPosition.z) / terrainSize.z) * heightmapResolution);
                int endY   = (int)(((maxZ - terrainPosition.z) / terrainSize.z) * heightmapResolution) + 1;

                // 将范围限制在地形的有效边界内
                startX = math.max(0, startX);
                endX   = math.min(heightmapResolution, endX);
                startY = math.max(0, startY);
                endY   = math.min(heightmapResolution, endY);

                // 如果包围盒无效，跳过
                if (startX >= endX || startY >= endY) return;
                
                float2 p0 = v0.xz;
                float2 p1 = v1.xz;
                float2 p2 = v2.xz;
                
                // 遍历这个小范围内的所有地形像素
                for (int y = startY; y < endY; y++)
                {
                    for (int x = startX; x < endX; x++)
                    {
                        // 计算当前像素中心点的世界坐标
                        float normX = (float)x / heightmapResolutionMinusOne;
                        float normY = (float)y / heightmapResolutionMinusOne;
                        float3 worldPos = terrainPosition + new float3(normX * terrainSize.x, 0, normY * terrainSize.z);

                        // 判断像素中心点是否真的在三角形内
                        if (EditorTerrainUtility.IsPointInTriangle(worldPos.xz, p0, p1, p2))
                        {
                            // --- 如果在，则计算并应用修改 ---
                            float3 bary = EditorTerrainUtility.Barycentric(worldPos.xz, p0, p1, p2);
                            if (bary.x < 0 || bary.y < 0 || bary.z < 0) continue;

                            // 计算高度
                            float roadHeight = bary.x * v0.y + bary.y * v1.y + bary.z * v2.y;
                            roadHeight -= flattenOffset;
                            
                            int heightmapIndex = y * heightmapResolution + x;
                            if (heightmapIndex >= 0 && heightmapIndex < heightMap.Length)
                            {
                                float currentTerrainHeight = heightMap[heightmapIndex] * terrainSize.y;
                                float newHeight = math.lerp(currentTerrainHeight, roadHeight, flattenStrength);
                                heightMap[heightmapIndex] = newHeight / terrainSize.y;
                            }

                            // ✅ 使用正确的坐标转换方法
                            int alphaX = (int)((float)x / heightmapResolutionMinusOne * alphamapResolutionMinusOne);
                            int alphaY = (int)((float)y / heightmapResolutionMinusOne * alphamapResolutionMinusOne);
                            
                            // 确保纹理坐标在有效范围内
                            if (alphaX < 0 || alphaX >= alphamapWidth || alphaY < 0 || alphaY >= alphamapHeight) continue;
                            
                            int2 layerInfo = vertexLayerInfos[i0];
                            int subMeshIndex = layerInfo.x;
                            float blendStrength = layerInfo.y / 1000f;
                            
                            if (subMeshIndex >= 0 && subMeshIndex < layerMapping.Length)
                            {
                                int terrainLayerIndex = layerMapping[subMeshIndex];
                                if (terrainLayerIndex != -1 && terrainLayerIndex < alphamapLayers)
                                {
                                    PaintAlphamap(alphaY * alphamapWidth + alphaX, terrainLayerIndex, blendStrength);
                                }
                            }
                        }
                    }
                }
            }
            
            private void PaintAlphamap(int pixelIndex, int layerIndex, float strength)
            {
                // 检查像素索引和层索引是否有效
                if (pixelIndex < 0 || pixelIndex >= alphamapWidth * alphamapHeight) return;
                if (layerIndex < 0 || layerIndex >= alphamapLayers) return;
                
                int startIdx = pixelIndex * alphamapLayers;
                
                // 检查起始索引是否在范围内
                if (startIdx + alphamapLayers > alphaMap.Length) return;
                
                float currentWeight = alphaMap[startIdx + layerIndex];
                float targetWeight = math.lerp(currentWeight, 1.0f, strength);

                var newWeights = new NativeArray<float>(alphamapLayers, Allocator.Temp);
                try
                {
                    newWeights[layerIndex] = targetWeight;
                    float remainingWeight = 1.0f - targetWeight;
                    float originalOtherLayersTotal = 1.0f - currentWeight;

                    for (int i = 0; i < alphamapLayers; i++)
                    {
                        if (i == layerIndex) continue;
                        
                        if (startIdx + i >= alphaMap.Length) continue; // 额外检查
                        
                        if (originalOtherLayersTotal > 0.001f)
                        {
                            newWeights[i] = alphaMap[startIdx + i] / originalOtherLayersTotal * remainingWeight;
                        }
                        else
                        {
                            newWeights[i] = remainingWeight / (alphamapLayers - 1);
                        }
                    }

                    float totalWeight = 0;
                    for (int i = 0; i < alphamapLayers; i++) 
                    {
                        if (startIdx + i < alphaMap.Length) // 额外检查
                        {
                            totalWeight += newWeights[i];
                        }
                    }
                    
                    if (totalWeight > 0.001f)
                    {
                        for (int i = 0; i < alphamapLayers; i++) 
                        {
                            if (startIdx + i < alphaMap.Length) // 额外检查
                            {
                                alphaMap[startIdx + i] = newWeights[i] / totalWeight;
                            }
                        }
                    }
                }
                finally { newWeights.Dispose(); }
            }
        }
    }
}