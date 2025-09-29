// 文件路径: Assets/RoadCreator/Runtime/Generation/RoadMeshBuilder.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

namespace RoadSystem
{
    /// <summary>
    /// 负责道路网格的实际构建工作。
    /// 整个构建过程被设计成一个高性能的 Job System 流水线，
    /// 以确保在处理复杂道路时也能保持编辑器的流畅响应。
    /// </summary>
    public class RoadMeshBuilder
    {
        #region 成员变量与构造函数

        // --- 输入数据 ---
        private readonly IReadOnlyList<RoadControlPoint> localControlPoints;
        private readonly RoadConfig settings;
        private readonly Transform roadObjectTransform;
        
        // --- 输出结果 ---
        private readonly Mesh mesh;

        // --- Job System 使用的临时数据 ---
        private NativeArray<PathPoint> pathPoints;
        private NativeArray<Vector3> vertices;
        private NativeArray<Vector2> uvs;

        public RoadMeshBuilder(IReadOnlyList<RoadControlPoint> localControlPoints, RoadConfig settings, Transform roadObjectTransform)
        {
            this.localControlPoints = localControlPoints;
            this.settings = settings;
            this.roadObjectTransform = roadObjectTransform;
            this.mesh = new Mesh { name = "Road Spline Mesh" };
        }

        #endregion

        #region 主构建流程

        /// <summary>
        /// 执行构建过程的主方法。
        /// </summary>
        public Mesh Build()
        {
            if (!IsReadyToBuild())
            {
                mesh.Clear();
                return mesh;
            }

            // 1. 启动数据准备流水线 (Jobs)，获取一个 JobHandle 用于同步
            var pathPointsJobHandle = CalculatePathPointsPipeline(out pathPoints);

            // 2. 准备网格顶点/UV数据的 NativeArrays
            int vertexCount = pathPoints.Length * settings.layerProfiles.Count * 4;
            vertices = new NativeArray<Vector3>(vertexCount, Allocator.TempJob);
            uvs = new NativeArray<Vector2>(vertexCount, Allocator.TempJob);

            // 3. 准备 Job 需要的配置信息
            var layerProfiles = new NativeArray<LayerProfileInfo>(settings.layerProfiles.Select(p => new LayerProfileInfo(p, settings.globalWidthMultiplier)).ToArray(), Allocator.TempJob);
            var settingsInfo = new SettingsInfo(settings);

            // 4. 创建并调度最终的网格数据生成 Job
            var buildMeshDataJob = new BuildMeshDataJob
            {
                pathPoints = this.pathPoints,
                layerProfiles = layerProfiles,
                roadObjectTransformMatrix = roadObjectTransform.worldToLocalMatrix,
                settingsInfo = settingsInfo,
                vertices = this.vertices,
                uvs = this.uvs
            };

            // [关键] 让这个 Job 依赖于之前的数据准备流水线
            var buildMeshHandle = buildMeshDataJob.Schedule(pathPoints.Length, 32, pathPointsJobHandle);

            // 5. 等待所有 Job 执行完毕
            buildMeshHandle.Complete();

            // 6. Job 完成后，处理数据并构建 Mesh
            BuildTriangles();
            AssignToMesh();

            // 7. [重要] 释放所有分配的临时内存
            pathPoints.Dispose();
            vertices.Dispose();
            uvs.Dispose();
            layerProfiles.Dispose();

            return mesh;
        }

        /// <summary>
        /// 检查是否满足构建的基本条件。
        /// </summary>
        private bool IsReadyToBuild() => localControlPoints.Count >= 2 && settings.layerProfiles.Count > 0;

        #endregion
        
        #region 步骤 1: 数据准备流水线 (Job Pipeline)

        /// <summary>
        /// 使用 Job Pipeline 高效地计算出所有最终的路径点。
        /// </summary>
        private JobHandle CalculatePathPointsPipeline(out NativeArray<PathPoint> finalPathPoints)
        {
            // 准备 Job 需要的初始数据
            int totalSegments = (localControlPoints.Count - 1) * settings.splineResolution;
            if (totalSegments == 0) totalSegments = 1;

            var controlPointsNative = new NativeArray<RoadControlPoint>(localControlPoints.ToArray(), Allocator.TempJob);
            var worldDisplayPoints = new NativeArray<float3>(totalSegments + 1, Allocator.TempJob);

            // --- Job 1: 并行计算样条曲线上的点 ---
            var splineJob = new CalculateSplinePointsJob
            {
                localControlPoints = controlPointsNative,
                roadTransform = roadObjectTransform.localToWorldMatrix,
                totalSegments = totalSegments,
                result = worldDisplayPoints
            };
            var splineHandle = splineJob.Schedule(totalSegments + 1, 64);
            JobHandle dependency = splineHandle; // 初始化依赖链

            // --- Job 2 (可选): 地形贴合 ---
            if (settings.conformToTerrainUndulations && settings.terrainConformity > 0)
            {
                Terrain mainTerrain = Terrain.activeTerrain;
                if (mainTerrain != null)
                {
                    var terrainData = mainTerrain.terrainData;
                    var heights = new NativeArray<float>(terrainData.GetHeights(0,0,terrainData.heightmapResolution, terrainData.heightmapResolution).Cast<float>().ToArray(), Allocator.TempJob);
                    
                    var conformJob = new ConformToTerrainJob
                    {
                        worldPoints = worldDisplayPoints,
                        terrainPos = mainTerrain.transform.position,
                        terrainSize = terrainData.size,
                        heightmapResolution = terrainData.heightmapResolution,
                        heights = heights,
                        terrainConformity = settings.terrainConformity
                    };
                    
                    var conformHandle = conformJob.Schedule(totalSegments + 1, 64, dependency);
                    dependency = conformHandle; // 更新依赖链
                    heights.Dispose(dependency); // 安排 Job 完成后自动释放内存
                }
            }

            // --- Job 3: 计算最终的 PathPoint (方向、距离等) ---
            finalPathPoints = new NativeArray<PathPoint>(totalSegments + 1, Allocator.TempJob);
            var finalPathJob = new CalculateFinalPathPointsJob
            {
                worldDisplayPoints = worldDisplayPoints,
                localControlPoints = controlPointsNative,
                roadTransform = roadObjectTransform.localToWorldMatrix,
                previewHeightOffset = settings.previewHeightOffset,
                result = finalPathPoints
            };

            var finalPathHandle = finalPathJob.Schedule(dependency);

            // 安排在流水线完成后释放不再需要的数组
            controlPointsNative.Dispose(finalPathHandle);
            worldDisplayPoints.Dispose(finalPathHandle);
            
            return finalPathHandle;
        }

        #endregion

        #region 步骤 2 & 3: 构建三角形并应用到 Mesh

        /// <summary>
        /// 在主线程上构建三角形索引。这个过程很快，不需要 Job 化。
        /// </summary>
        private void BuildTriangles()
        {
            mesh.Clear(); 
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            
            mesh.subMeshCount = settings.layerProfiles.Count;
            int verticesPerStep = settings.layerProfiles.Count * 4;
            
            for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
            {
                int triangleCount = (pathPoints.Length - 1) * 12; // 2 quads per segment = 4 triangles = 12 indices
                var triangles = new List<int>(triangleCount);

                for (int i = 0; i < pathPoints.Length - 1; i++)
                {
                    int root = i * verticesPerStep + layerIndex * 4;
                    int rootNext = (i + 1) * verticesPerStep + layerIndex * 4;
                    int il = root, ol = root + 1, ir = root + 2, or = root + 3;
                    int il_next = rootNext, ol_next = rootNext + 1, ir_next = rootNext + 2, or_next = rootNext + 3;

                    // Left side quad
                    triangles.Add(il); triangles.Add(il_next); triangles.Add(ol_next);
                    triangles.Add(il); triangles.Add(ol_next); triangles.Add(ol);
                    // Right side quad
                    triangles.Add(ir); triangles.Add(or_next); triangles.Add(ir_next);
                    triangles.Add(ir); triangles.Add(or); triangles.Add(or_next);
                }
                mesh.SetTriangles(triangles, layerIndex);
            }
        }

        /// <summary>
        /// 最后一步，让 Mesh 重新计算法线和包围盒。
        /// </summary>
        private void AssignToMesh()
        {
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        #endregion

        #region Job System 结构体定义

        // --- 数据结构 ---

        public struct PathPoint
        {
            public float3 position;
            public float3 right;
            public float3 forward; // [修正] 必须是 float3 才能被 Burst 使用
            public float cumulativeDistance;
        }

        public struct LayerProfileInfo
        {
            public float width;
            public float verticalOffset;
            public float offsetFromCenter;
            public float boundaryWobbleFrequency;
            public float boundaryWobbleAmplitude;

            public LayerProfileInfo(RoadLayerProfile profile, float globalWidth)
            {
                width = profile.width * globalWidth;
                verticalOffset = profile.verticalOffset;
                offsetFromCenter = profile.offsetFromCenter;
                boundaryWobbleFrequency = profile.boundaryWobbleFrequency;
                boundaryWobbleAmplitude = profile.boundaryWobbleAmplitude;
            }
        }

        public struct SettingsInfo
        {
            public bool controlLayersIndependently;
            public float globalWobbleFrequencyMultiplier;
            public float globalWobbleAmplitudeMultiplier;
            public UVGenerationMode uvGenerationMode;
            public float2 worldUVScaling;
            public float totalRoadWidth;

            public SettingsInfo(RoadConfig settings)
            {
                controlLayersIndependently = settings.controlLayersIndependently;
                globalWobbleFrequencyMultiplier = settings.globalWobbleFrequencyMultiplier;
                globalWobbleAmplitudeMultiplier = settings.globalWobbleAmplitudeMultiplier;
                uvGenerationMode = settings.uvGenerationMode;
                worldUVScaling = new float2(settings.worldUVScaling.x, settings.worldUVScaling.y);
                totalRoadWidth = settings.layerProfiles.Sum(p => p.width) * settings.globalWidthMultiplier;
                if (totalRoadWidth == 0) totalRoadWidth = 1;
            }
        }

        // --- Job 定义 ---

        [BurstCompile]
        public struct CalculateSplinePointsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<RoadControlPoint> localControlPoints;
            public int totalSegments;
            public float4x4 roadTransform;
            [WriteOnly] public NativeArray<float3> result;

            public void Execute(int index)
            {
                float t = (float)index / totalSegments;
                int numSections = localControlPoints.Length - 1;
                int currPt = math.min((int)math.floor(t * numSections), numSections - 1);
                float u = t * numSections - currPt;
                
                float3 a = localControlPoints[currPt].position;
                float3 b = localControlPoints[currPt + 1].position;
                float3 localPos = math.lerp(a, b, u);

                result[index] = math.transform(roadTransform, localPos);
            }
        }

        [BurstCompile]
        public struct ConformToTerrainJob : IJobParallelFor
        {
            public NativeArray<float3> worldPoints;
            [ReadOnly] public float3 terrainPos;
            [ReadOnly] public float3 terrainSize;
            [ReadOnly] public int heightmapResolution;
            [ReadOnly] public NativeArray<float> heights;
            [ReadOnly] public float terrainConformity;

            public void Execute(int index)
            {
                float3 worldPos = worldPoints[index];
                float normX = (worldPos.x - terrainPos.x) / terrainSize.x;
                float normZ = (worldPos.z - terrainPos.z) / terrainSize.z;

                if (normX >= 0 && normX <= 1 && normZ >= 0 && normZ <= 1)
                {
                    int mapX = (int)(normX * (heightmapResolution - 1));
                    int mapZ = (int)(normZ * (heightmapResolution - 1));

                    float terrainHeight = heights[mapZ * heightmapResolution + mapX] * terrainSize.y + terrainPos.y;
                    worldPos.y = math.lerp(worldPos.y, terrainHeight, terrainConformity);
                    worldPoints[index] = worldPos;
                }
            }
        }

        [BurstCompile]
        public struct CalculateFinalPathPointsJob : IJob
        {
            [ReadOnly] public NativeArray<float3> worldDisplayPoints;
            [ReadOnly] public NativeArray<RoadControlPoint> localControlPoints;
            [ReadOnly] public float4x4 roadTransform;
            [ReadOnly] public float previewHeightOffset;
            [WriteOnly] public NativeArray<PathPoint> result;

            public void Execute()
            {
                float totalDistance = 0;
                for (int i = 0; i < worldDisplayPoints.Length; i++)
                {
                    if (i > 0) totalDistance += math.distance(worldDisplayPoints[i - 1], worldDisplayPoints[i]);

                    float t = (float)i / (worldDisplayPoints.Length - 1);
                    int numSections = localControlPoints.Length - 1;
                    int currPt = math.min((int)math.floor(t * numSections), numSections - 1);

                    float3 a = localControlPoints[currPt].position;
                    float3 b = localControlPoints[currPt + 1].position;
                    float3 localForward = math.normalize(b - a);
                    if (math.all(localForward == 0)) localForward = new float3(0, 0, 1);

                    float3 worldForward = math.mul((float3x3)roadTransform, localForward);
                    
                    float3 right = math.normalize(math.cross(new float3(0, 1, 0), worldForward));
                    if (math.abs(math.dot(worldForward, new float3(0, 1, 0))) > 0.999f)
                    {
                        float3 roadObjectForward = math.mul((float3x3)roadTransform, new float3(0, 0, 1));
                        right = math.normalize(math.cross(roadObjectForward, worldForward));
                    }

                    result[i] = new PathPoint
                    {
                        position = worldDisplayPoints[i] + new float3(0, previewHeightOffset, 0),
                        right = right,
                        forward = worldForward,
                        cumulativeDistance = totalDistance
                    };
                }
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
        public struct BuildMeshDataJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<PathPoint> pathPoints;
            [ReadOnly] public NativeArray<LayerProfileInfo> layerProfiles;
            [ReadOnly] public float4x4 roadObjectTransformMatrix;
            [ReadOnly] public SettingsInfo settingsInfo;
            [WriteOnly] public NativeArray<Vector3> vertices;
            [WriteOnly] public NativeArray<Vector2> uvs;

            public void Execute(int i)
            {
                var currentPathPoint = pathPoints[i];
                float cumulativeOffset = 0;
                int verticesPerStep = layerProfiles.Length * 4;

                for (int layerIndex = 0; layerIndex < layerProfiles.Length; layerIndex++)
                {
                    var profile = layerProfiles[layerIndex];
                    float innerOffset, outerOffset;

                    if (settingsInfo.controlLayersIndependently)
                    {
                        float halfWidth = profile.width / 2f;
                        innerOffset = profile.offsetFromCenter - halfWidth;
                        outerOffset = profile.offsetFromCenter + halfWidth;
                    }
                    else
                    {
                        innerOffset = cumulativeOffset;
                        outerOffset = cumulativeOffset + profile.width;
                        cumulativeOffset = outerOffset;
                    }

                    float3 rightDir = currentPathPoint.right;
                    float3 pos = currentPathPoint.position;
                    float vOffset = profile.verticalOffset;
                    
                    float3 innerLeftPos  = pos - rightDir * innerOffset + new float3(0, vOffset, 0);
                    float3 outerLeftPos  = pos - rightDir * outerOffset + new float3(0, vOffset, 0);
                    float3 innerRightPos = pos + rightDir * innerOffset + new float3(0, vOffset, 0);
                    float3 outerRightPos = pos + rightDir * outerOffset + new float3(0, vOffset, 0);

                    int root = i * verticesPerStep + layerIndex * 4;
                    vertices[root] = math.transform(roadObjectTransformMatrix, innerLeftPos);
                    vertices[root + 1] = math.transform(roadObjectTransformMatrix, outerLeftPos);
                    vertices[root + 2] = math.transform(roadObjectTransformMatrix, innerRightPos);
                    vertices[root + 3] = math.transform(roadObjectTransformMatrix, outerRightPos);

                    if (settingsInfo.uvGenerationMode == UVGenerationMode.WorldSpace)
                    {
                        uvs[root] = new Vector2(innerLeftPos.x * settingsInfo.worldUVScaling.x, innerLeftPos.z * settingsInfo.worldUVScaling.y);
                        uvs[root + 1] = new Vector2(outerLeftPos.x * settingsInfo.worldUVScaling.x, outerLeftPos.z * settingsInfo.worldUVScaling.y);
                        uvs[root + 2] = new Vector2(innerRightPos.x * settingsInfo.worldUVScaling.x, innerRightPos.z * settingsInfo.worldUVScaling.y);
                        uvs[root + 3] = new Vector2(outerRightPos.x * settingsInfo.worldUVScaling.x, outerRightPos.z * settingsInfo.worldUVScaling.y);
                    }
                    else // Adaptive
                    {
                        float v = currentPathPoint.cumulativeDistance;
                        float totalWidth = settingsInfo.totalRoadWidth;
                        uvs[root]     = new Vector2((-innerOffset + totalWidth / 2) / totalWidth, v);
                        uvs[root + 1] = new Vector2((-outerOffset + totalWidth / 2) / totalWidth, v);
                        uvs[root + 2] = new Vector2((innerOffset + totalWidth / 2) / totalWidth, v);
                        uvs[root + 3] = new Vector2((outerOffset + totalWidth / 2) / totalWidth, v);
                    }
                }
            }
        }

        #endregion
    }
}