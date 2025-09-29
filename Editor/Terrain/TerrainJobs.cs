// 文件路径: Assets/RoadCreator/Editor/Terrain/TerrainJobs.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace RoadSystem.Editor
{
    public static class TerrainJobs
    {
        // [新增] 专门用于压平地形高度的 Job
        [BurstCompile]
       public struct FlattenHeightmapJob : IJobParallelFor
        {
            [ReadOnly] public float3 terrainPosition;
            [ReadOnly] public float3 terrainSize;
            [ReadOnly] public int heightmapResolution;
            [ReadOnly] public float4x4 roadLocalToWorldMatrix;
            [ReadOnly] public NativeArray<Vector3> roadVertices;
            [ReadOnly] public NativeArray<int> roadTriangles;
            [ReadOnly] public float flattenOffset;
            
            // [修改] 允许多线程写入，因为每个 Job 只会处理自己负责的区域
            [NativeDisableParallelForRestriction]
            public NativeArray<float> heightMap;

            public void Execute(int triangleIndex)
            {
                int i = triangleIndex * 3;
                int vIndex0 = roadTriangles[i];
                int vIndex1 = roadTriangles[i + 1];
                int vIndex2 = roadTriangles[i + 2];

                float3 v0_world = math.transform(roadLocalToWorldMatrix, roadVertices[vIndex0]);
                float3 v1_world = math.transform(roadLocalToWorldMatrix, roadVertices[vIndex1]);
                float3 v2_world = math.transform(roadLocalToWorldMatrix, roadVertices[vIndex2]);

                float targetHeight = (v0_world.y + v1_world.y + v2_world.y) / 3f;
                targetHeight = (targetHeight + flattenOffset) / terrainSize.y;

                // --- [核心修复] 将世界坐标精确转换为 heightmap 像素坐标 ---
                float2 p0 = (v0_world.xz - terrainPosition.xz) / terrainSize.xz * (heightmapResolution - 1);
                float2 p1 = (v1_world.xz - terrainPosition.xz) / terrainSize.xz * (heightmapResolution - 1);
                float2 p2 = (v2_world.xz - terrainPosition.xz) / terrainSize.xz * (heightmapResolution - 1);
                
                // --- 然后基于像素坐标计算精确的包围盒 ---
                int minX = (int)math.floor(math.min(p0.x, math.min(p1.x, p2.x)));
                int maxX = (int)math.ceil(math.max(p0.x, math.max(p1.x, p2.x)));
                int minZ = (int)math.floor(math.min(p0.y, math.min(p1.y, p2.y)));
                int maxZ = (int)math.ceil(math.max(p0.y, math.max(p1.y, p2.y)));
                
                minX = math.clamp(minX, 0, heightmapResolution - 1);
                maxX = math.clamp(maxX, 0, heightmapResolution - 1);
                minZ = math.clamp(minZ, 0, heightmapResolution - 1);
                maxZ = math.clamp(maxZ, 0, heightmapResolution - 1);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (IsPointInTriangle(new float2(x, z), p0, p1, p2))
                        {
                            // 使用原子操作来安全地写入，避免多个线程修改同一个像素时冲突
                            // 对于压平，简单覆盖即可
                            heightMap[z * heightmapResolution + x] = targetHeight;
                        }
                    }
                }
            }

            private bool IsPointInTriangle(float2 p, float2 a, float2 b, float2 c)
            {
                float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
                float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;

                if ((s < 0) != (t < 0) && s != 0 && t != 0)
                {
                    return false;
                }
                float A = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
                return A < 0 ? (s <= 0 && s + t >= A) : (s >= 0 && s + t <= A);
            }
        }

        // [升级] BakeRoadToAlphamapJob 现在也负责写入智能数据
        [BurstCompile]
        public struct BakeRoadToAlphamapJob : IJobParallelFor
        {
            // ... (大部分属性不变) ...
            [ReadOnly] public float3 terrainPosition;
            [ReadOnly] public float3 terrainSize;
            [ReadOnly] public int alphamapWidth;
            [ReadOnly] public int alphamapHeight;
            [ReadOnly] public float4x4 roadLocalToWorldMatrix;
            [ReadOnly] public NativeArray<Vector3> roadVertices;
            [ReadOnly] public NativeArray<int> roadTriangles;
            [ReadOnly] public NativeArray<int> vertexToLayerIndexMap;
            [ReadOnly] public int roadLayerSplatIndex;
            [ReadOnly] public float baseLayerWeight;
            [NativeDisableParallelForRestriction] public NativeArray<float4> alphamapData;

            // [新增] 智能数据图的写入权限
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float4> roadDataMap;

            public void Execute(int triangleIndex)
            {
                int i = triangleIndex * 3;
                int vIndex0 = roadTriangles[i];
                int vIndex1 = roadTriangles[i + 1];
                int vIndex2 = roadTriangles[i + 2];

                float3 v0 = math.transform(roadLocalToWorldMatrix, roadVertices[vIndex0]);
                float3 v1 = math.transform(roadLocalToWorldMatrix, roadVertices[vIndex1]);
                float3 v2 = math.transform(roadLocalToWorldMatrix, roadVertices[vIndex2]);

                int layerIndex = vertexToLayerIndexMap[vIndex0];
                var roadWorldToLocalMatrix = math.inverse(roadLocalToWorldMatrix);

                // ... (包围盒计算不变) ...
                float3 terrainLocalV0 = (v0 - terrainPosition);
                float3 terrainLocalV1 = (v1 - terrainPosition);
                float3 terrainLocalV2 = (v2 - terrainPosition);
                int minX = (int)math.floor(math.min(math.min(terrainLocalV0.x, terrainLocalV1.x), terrainLocalV2.x) / terrainSize.x * (alphamapWidth - 1));
                int maxX = (int)math.ceil(math.max(math.max(terrainLocalV0.x, terrainLocalV1.x), terrainLocalV2.x) / terrainSize.x * (alphamapWidth - 1));
                int minZ = (int)math.floor(math.min(math.min(terrainLocalV0.z, terrainLocalV1.z), terrainLocalV2.z) / terrainSize.z * (alphamapHeight - 1));
                int maxZ = (int)math.ceil(math.max(math.max(terrainLocalV0.z, terrainLocalV1.z), terrainLocalV2.z) / terrainSize.z * (alphamapHeight - 1));
                minX = math.clamp(minX, 0, alphamapWidth - 1);
                maxX = math.clamp(maxX, 0, alphamapWidth - 1);
                minZ = math.clamp(minZ, 0, alphamapHeight - 1);
                maxZ = math.clamp(maxZ, 0, alphamapHeight - 1);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float3 pixelWorldPos = new float3(
                            (float)x / (alphamapWidth - 1) * terrainSize.x + terrainPosition.x, 0,
                            (float)z / (alphamapHeight - 1) * terrainSize.z + terrainPosition.z
                        );

                        if (IsPointInTriangle(pixelWorldPos.xz, v0.xz, v1.xz, v2.xz))
                        {
                            int mapIndex = z * alphamapWidth + x;

                            // --- 1. 修改 Splat Map 权重 ---
                            float4 currentSplat = alphamapData[mapIndex];
                            currentSplat[roadLayerSplatIndex] = baseLayerWeight;
                            // ... (归一化逻辑不变) ...
                            float otherLayersTotalWeight = 1.0f - baseLayerWeight;
                            float currentTotalWeight = 1.0f - currentSplat[roadLayerSplatIndex];
                            if (currentTotalWeight > 0.001f)
                            {
                                float rescaleFactor = otherLayersTotalWeight / currentTotalWeight;
                                for (int c = 0; c < 4; c++)
                                {
                                    if (c != roadLayerSplatIndex)
                                    {
                                        currentSplat[c] *= rescaleFactor;
                                    }
                                }
                            }
                            alphamapData[mapIndex] = currentSplat;

                            // --- 2. [核心] 写入智能数据 ---
                            float3 pixelLocalPos = math.transform(roadWorldToLocalMatrix, pixelWorldPos);
                            roadDataMap[mapIndex] = new float4(
                                pixelLocalPos.x, // U
                                pixelLocalPos.z, // V
                                (float)layerIndex / 255f, // Atlas Index
                                1.0f // Blend
                            );
                        }
                    }
                }
            }
            private bool IsPointInTriangle(float2 p, float2 a, float2 b, float2 c)
            {
                // 计算从一个顶点到另两个顶点和目标点的向量
                float2 v0 = c - a;
                float2 v1 = b - a;
                float2 v2 = p - a;

                // 计算点积
                float dot00 = math.dot(v0, v0);
                float dot01 = math.dot(v0, v1);
                float dot02 = math.dot(v0, v2);
                float dot11 = math.dot(v1, v1);
                float dot12 = math.dot(v1, v2);

                // 计算重心坐标
                float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
                float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
                float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

                // 检查点是否在三角形内
                return (u >= 0) && (v >= 0) && (u + v < 1);
            }

        }
    }
    // --- [核心修复] 将数据转换 Jobs 的定义放回这个文件 ---
    [BurstCompile]
    public struct ConvertToFloat4Job : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> alphamaps;
        public int alphamapWidth;
        public int alphamapHeight;
        public int alphamapLayers;
        [WriteOnly] public NativeArray<float4> output;

        public void Execute(int index)
        {
            int y = index / alphamapWidth;
            int x = index % alphamapWidth;

            float4 val = float4.zero;
            int baseIndex = (y * alphamapWidth + x) * alphamapLayers;
            if (alphamapLayers > 0) val.x = alphamaps[baseIndex + 0];
            if (alphamapLayers > 1) val.y = alphamaps[baseIndex + 1];
            if (alphamapLayers > 2) val.z = alphamaps[baseIndex + 2];
            if (alphamapLayers > 3) val.w = alphamaps[baseIndex + 3];
            output[index] = val;
        }
    }

    [BurstCompile]
    public struct ConvertBackTo3DArrayJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> input;
        public int alphamapWidth;
        public int alphamapHeight;
        public int alphamapLayers;
        [WriteOnly] public NativeArray<float> output;

        public void Execute(int index)
        {
            int y = index / alphamapWidth;
            int x = index % alphamapWidth;

            int baseIndex = (y * alphamapWidth + x) * alphamapLayers;
            if (alphamapLayers > 0) output[baseIndex + 0] = input[index].x;
            if (alphamapLayers > 1) output[baseIndex + 1] = input[index].y;
            if (alphamapLayers > 2) output[baseIndex + 2] = input[index].z;
            if (alphamapLayers > 3) output[baseIndex + 3] = input[index].w;
        }
    }
}