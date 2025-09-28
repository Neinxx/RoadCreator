// 文件路径: Assets/RoadCreator/Runtime/Generation/RoadMeshBuilder.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    /// <summary>
    /// [新增]
    /// 负责实际的道路网格构建工作。
    /// 这个类遵循“构建器模式”，将复杂的构建过程分解为一系列独立的步骤。
    /// 这是一个非静态类，持有构建过程中需要的所有状态。
    /// </summary>
    public class RoadMeshBuilder
    {
        // --- 内部状态 ---
        private readonly IReadOnlyList<RoadControlPoint> localControlPoints;
        private readonly RoadConfig settings;
        private readonly Transform roadObjectTransform;
        private readonly Mesh mesh;

        private List<PathPoint> pathPoints;
        private List<Vector3> vertices;
        private List<Vector2> uvs;
        private List<List<int>> subMeshTriangles;

        // --- 内部结构体 ---
        private struct PathPoint
        {
            public Vector3 position;
            public Vector3 right;
            public Vector3 forward;
            public float cumulativeDistance;
        }

        // --- 构造函数 ---
        public RoadMeshBuilder(IReadOnlyList<RoadControlPoint> localControlPoints, RoadConfig settings, Transform roadObjectTransform)
        {
            this.localControlPoints = localControlPoints;
            this.settings = settings;
            this.roadObjectTransform = roadObjectTransform;
            this.mesh = new Mesh { name = "Road Spline Mesh" };
        }

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

            // --- 流水线步骤 ---
            CalculatePathPoints();
            InitializeMeshArrays();
            BuildVerticesAndUVs();
            BuildTriangles();
            AssignToMesh();

            return mesh;
        }

        private bool IsReadyToBuild()
        {
            return localControlPoints.Count >= 2 && settings.layerProfiles.Count > 0;
        }

        /// <summary>
        /// 步骤 1: 计算包含方向、距离等信息的完整路径点。
        /// </summary>
        private void CalculatePathPoints()
        {
            var worldDisplayPoints = GetFinalDisplayPoints();
            int totalSegments = worldDisplayPoints.Count - 1;
            pathPoints = new List<PathPoint>(totalSegments + 1);

            float totalDistance = 0f;
            pathPoints.Add(CalculateSinglePathPoint(worldDisplayPoints, 0, 0));

            for (int i = 1; i <= totalSegments; i++)
            {
                Vector3 prevDisplayPos = pathPoints[i - 1].position;
                Vector3 currentDisplayPos = worldDisplayPoints[i];
                totalDistance += Vector3.Distance(prevDisplayPos, currentDisplayPos);
                float t = (float)i / totalSegments;
                pathPoints.Add(CalculateSinglePathPoint(worldDisplayPoints, t, totalDistance));
            }
        }
        
        /// <summary>
        /// 步骤 2: 根据配置初始化顶点、UV和子网格列表。
        /// </summary>
        private void InitializeMeshArrays()
        {
            int vertexCountEstimate = (pathPoints.Count) * settings.layerProfiles.Count * 4;
            vertices = new List<Vector3>(vertexCountEstimate);
            uvs = new List<Vector2>(vertexCountEstimate);

            subMeshTriangles = new List<List<int>>();
            for (int i = 0; i < settings.layerProfiles.Count; i++)
            {
                int triangleCountEstimate = (pathPoints.Count - 1) * 2 * 3;
                subMeshTriangles.Add(new List<int>(triangleCountEstimate));
            }
        }

        /// <summary>
        /// 步骤 3: 遍历路径点和图层，生成所有顶点和UV。
        /// </summary>
        private void BuildVerticesAndUVs()
        {
            float totalRoadWidth = settings.layerProfiles.Sum(p => p.width) * settings.globalWidthMultiplier;
            if (totalRoadWidth == 0) totalRoadWidth = 1;

            for (int i = 0; i < pathPoints.Count; i++)
            {
                PathPoint currentPathPoint = pathPoints[i];
                float cumulativeOffset = 0;

                for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
                {
                    var profile = settings.layerProfiles[layerIndex];
                    float innerOffset, outerOffset;

                    if (settings.controlLayersIndependently)
                    {
                        float halfWidth = (profile.width * settings.globalWidthMultiplier) / 2f;
                        innerOffset = profile.offsetFromCenter - halfWidth;
                        outerOffset = profile.offsetFromCenter + halfWidth;
                    }
                    else
                    {
                        innerOffset = cumulativeOffset;
                        outerOffset = cumulativeOffset + (profile.width * settings.globalWidthMultiplier);
                        cumulativeOffset = outerOffset;
                    }
                    
                    float finalWobbleFreq = profile.boundaryWobbleFrequency * settings.globalWobbleFrequencyMultiplier;
                    float finalWobbleAmp = profile.boundaryWobbleAmplitude * settings.globalWobbleAmplitudeMultiplier;
                    float wobble = RoadNoiseUtility.GetDryBrushWobble(currentPathPoint.position, finalWobbleFreq, finalWobbleAmp);
                    outerOffset += wobble;
                    if (outerOffset < innerOffset) outerOffset = innerOffset + 0.01f;

                    Vector3 rightDir = currentPathPoint.right;
                    Vector3 innerLeftPos = currentPathPoint.position - rightDir * innerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 outerLeftPos = currentPathPoint.position - rightDir * outerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 innerRightPos = currentPathPoint.position + rightDir * innerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 outerRightPos = currentPathPoint.position + rightDir * outerOffset + Vector3.up * profile.verticalOffset;

                    vertices.Add(roadObjectTransform.InverseTransformPoint(innerLeftPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(outerLeftPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(innerRightPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(outerRightPos));

                    // UV 计算
                    switch (settings.uvGenerationMode)
                    {
                        case UVGenerationMode.WorldSpace:
                            uvs.Add(new Vector2(innerLeftPos.x * settings.worldUVScaling.x, innerLeftPos.z * settings.worldUVScaling.y));
                            uvs.Add(new Vector2(outerLeftPos.x * settings.worldUVScaling.x, outerLeftPos.z * settings.worldUVScaling.y));
                            uvs.Add(new Vector2(innerRightPos.x * settings.worldUVScaling.x, innerRightPos.z * settings.worldUVScaling.y));
                            uvs.Add(new Vector2(outerRightPos.x * settings.worldUVScaling.x, outerRightPos.z * settings.worldUVScaling.y));
                            break;
                        case UVGenerationMode.Adaptive:
                        default:
                            float v = currentPathPoint.cumulativeDistance;
                            float u_il = (-innerOffset + totalRoadWidth / 2) / totalRoadWidth;
                            float u_ol = (-outerOffset + totalRoadWidth / 2) / totalRoadWidth;
                            float u_ir = (innerOffset + totalRoadWidth / 2) / totalRoadWidth;
                            float u_or = (outerOffset + totalRoadWidth / 2) / totalRoadWidth;
                            uvs.Add(new Vector2(u_il, v));
                            uvs.Add(new Vector2(u_ol, v));
                            uvs.Add(new Vector2(u_ir, v));
                            uvs.Add(new Vector2(u_or, v));
                            break;
                    }
                }
            }
        }
        
        /// <summary>
        /// 步骤 4: 构建三角形索引，连接顶点。
        /// </summary>
        private void BuildTriangles()
        {
            int verticesPerStep = settings.layerProfiles.Count * 4;
            for (int i = 0; i < pathPoints.Count - 1; i++)
            {
                for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
                {
                    int root = i * verticesPerStep + layerIndex * 4;
                    int rootNext = (i + 1) * verticesPerStep + layerIndex * 4;
                    var currentLayerTriangles = subMeshTriangles[layerIndex];
                    
                    int il = root, ol = root + 1, ir = root + 2, or = root + 3;
                    int il_next = rootNext, ol_next = rootNext + 1, ir_next = rootNext + 2, or_next = rootNext + 3;
                    
                    // Left side
                    currentLayerTriangles.Add(il); currentLayerTriangles.Add(il_next); currentLayerTriangles.Add(ol_next);
                    currentLayerTriangles.Add(il); currentLayerTriangles.Add(ol_next); currentLayerTriangles.Add(ol);
                    
                    // Right side
                    currentLayerTriangles.Add(ir); currentLayerTriangles.Add(or_next); currentLayerTriangles.Add(ir_next);
                    currentLayerTriangles.Add(ir); currentLayerTriangles.Add(or); currentLayerTriangles.Add(or_next);
                }
            }
        }

        /// <summary>
        /// 步骤 5: 将所有计算出的数据赋值给 Mesh 对象。
        /// </summary>
        private void AssignToMesh()
        {
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subMeshTriangles.Count;
            for (int i = 0; i < subMeshTriangles.Count; i++)
                mesh.SetTriangles(subMeshTriangles[i], i);
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // --- 辅助方法 (从旧类中迁移并调整) ---

        private List<Vector3> GetFinalDisplayPoints()
        {
            var displayPoints = new List<Vector3>();
            int totalSegments = (localControlPoints.Count - 1) * settings.splineResolution;
            if (totalSegments == 0) totalSegments = 1;

            for (int i = 0; i <= totalSegments; i++)
            {
                displayPoints.Add(GetDisplayPoint((float)i / totalSegments));
            }

            if (settings.conformToTerrainUndulations && settings.verticalSmoothness > 0 && displayPoints.Count > 2)
            {
                return SmoothVerticalCurve(displayPoints, settings.verticalSmoothness, settings.smoothIterations);
            }

            return displayPoints;
        }

        private Vector3 GetDisplayPoint(float t)
        {
            Vector3 localPos = SplineUtility.GetPoint(localControlPoints, t);
            Vector3 worldPos = roadObjectTransform.TransformPoint(localPos);

            if (settings.conformToTerrainUndulations && settings.terrainConformity > 0)
            {
                Terrain terrain = TerrainUtility.GetTerrainAt(worldPos);
                if (terrain != null)
                {
                    float terrainHeight = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                    worldPos.y = Mathf.Lerp(worldPos.y, terrainHeight, settings.terrainConformity);
                }
            }
            return worldPos;
        }

        private List<Vector3> SmoothVerticalCurve(List<Vector3> points, float smoothness, int iterations)
        {
            List<Vector3> smoothedPoints = new List<Vector3>(points);
            for (int i = 0; i < iterations; i++)
            {
                List<Vector3> currentPoints = new List<Vector3>(smoothedPoints);
                for (int j = 1; j < currentPoints.Count - 1; j++)
                {
                    float previousY = currentPoints[j - 1].y;
                    float currentY = currentPoints[j].y;
                    float nextY = currentPoints[j + 1].y;
                    float averageY = (previousY + nextY) / 2f;
                    float smoothedY = Mathf.Lerp(currentY, averageY, smoothness);
                    Vector3 point = smoothedPoints[j];
                    point.y = smoothedY;
                    smoothedPoints[j] = point;
                }
            }
            return smoothedPoints;
        }

        private PathPoint CalculateSinglePathPoint(IReadOnlyList<Vector3> worldDisplayPoints, float t, float distance)
        {
            int totalSegments = worldDisplayPoints.Count - 1;
            int currentIndex = Mathf.FloorToInt(t * totalSegments);
            currentIndex = Mathf.Clamp(currentIndex, 0, totalSegments -1);

            Vector3 displayPoint = worldDisplayPoints[currentIndex];
            displayPoint.y += settings.previewHeightOffset;

            Vector3 localForward = SplineUtility.GetVelocity(localControlPoints, t).normalized;
            Vector3 worldForward = roadObjectTransform.TransformDirection(localForward);
            if (worldForward == Vector3.zero) worldForward = roadObjectTransform.forward;
            
            Vector3 right = Vector3.Cross(Vector3.up, worldForward).normalized;
            if (Mathf.Abs(Vector3.Dot(worldForward, Vector3.up)) > 0.999f)
            {
                right = Vector3.Cross(roadObjectTransform.forward, worldForward).normalized;
            }

            return new PathPoint
            {
                position = displayPoint,
                right = right,
                forward = worldForward,
                cumulativeDistance = distance
            };
        }
    }
}