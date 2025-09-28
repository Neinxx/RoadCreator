using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    public static class RoadMeshGenerator
    {
        // PathPoint 结构体保持不变
        private struct PathPoint
        {
            public Vector3 position;
            public Vector3 right;
            public Vector3 forward;
            public float cumulativeDistance;
        }

        // 文件: RoadMeshGenerator.cs
        // 请用下面的代码完整替换你文件中的 GenerateMesh 方法

        public static Mesh GenerateMesh(IReadOnlyList<RoadControlPoint> localControlPoints, RoadConfig settings, Transform roadObjectTransform)
        {
            var mesh = new Mesh { name = "Road Spline Mesh" };

            // --- 1. 初始化和安全检查 ---
            if (localControlPoints.Count < 2 || settings.layerProfiles.Count == 0)
            {
                mesh.Clear();
                return mesh;
            }

            // --- 2. 获取平滑后的最终路径点 (世界坐标) ---
            var worldDisplayPoints = GetFinalDisplayPoints(localControlPoints, settings, roadObjectTransform);
            int totalSegments = worldDisplayPoints.Count - 1;
            if (totalSegments < 1)
            {
                mesh.Clear();
                return mesh;
            }

            // --- 3. 计算包含方向、距离等信息的完整路径点 ---
            var pathPoints = new PathPoint[totalSegments + 1];
            float totalDistance = 0f;
            pathPoints[0] = CalculateFinalPathPoint(localControlPoints, worldDisplayPoints, 0, settings, 0, roadObjectTransform);
            for (int i = 1; i <= totalSegments; i++)
            {
                Vector3 prevDisplayPos = pathPoints[i - 1].position;
                Vector3 currentDisplayPos = worldDisplayPoints[i];
                totalDistance += Vector3.Distance(prevDisplayPos, currentDisplayPos);
                float t = (float)i / totalSegments;
                pathPoints[i] = CalculateFinalPathPoint(localControlPoints, worldDisplayPoints, t, settings, totalDistance, roadObjectTransform);
            }

            // --- 4. 构建顶点、UV和三角形 ---
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subMeshTriangles = new List<List<int>>();
            for (int i = 0; i < settings.layerProfiles.Count; i++)
                subMeshTriangles.Add(new List<int>());

            // [注意] 这个总宽度的计算在“独立模式”下可能不完全精确，但为了保持UV的稳定，我们暂时沿用
            float totalRoadWidth = settings.layerProfiles.Sum(p => p.width) * settings.globalWidthMultiplier;
            if (totalRoadWidth == 0) totalRoadWidth = 1;

            // --- 遍历每个路径点，生成一圈顶点 ---
            for (int i = 0; i <= totalSegments; i++)
            {
                PathPoint currentPathPoint = pathPoints[i];
                float cumulativeOffset = 0; // [新增] 只在叠加模式下使用

                // --- 遍历每个道路图层，生成一层顶点 ---
                for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
                {
                    var profile = settings.layerProfiles[layerIndex];

                    float innerOffset, outerOffset;

                    // [核心逻辑] 根据RoadConfig中的开关，决定使用哪种布局模式
                    if (settings.controlLayersIndependently)
                    {
                        // --- 独立布局模式 ---
                        float halfWidth = (profile.width * settings.globalWidthMultiplier) / 2f;
                        halfWidth = Mathf.Max(0, halfWidth); // 确保宽度不会是负数

                        innerOffset = profile.offsetFromCenter - halfWidth;
                        outerOffset = profile.offsetFromCenter + halfWidth;
                    }
                    else
                    {
                        // --- 传统叠加模式 ---
                        innerOffset = cumulativeOffset;
                        float width = profile.width * settings.globalWidthMultiplier;
                        outerOffset = cumulativeOffset + Mathf.Max(0, width);

                        // 将无抖动的原始偏移传递给下一层
                        cumulativeOffset = outerOffset;
                    }

                    // [核心逻辑] 将全局缩放应用到每个图层的独立参数上
                    float finalWobbleFreq = profile.boundaryWobbleFrequency * settings.globalWobbleFrequencyMultiplier;
                    float finalWobbleAmp = profile.boundaryWobbleAmplitude * settings.globalWobbleAmplitudeMultiplier;

                    // [核心逻辑] 调用新的“干笔刷”抖动函数
                    float wobble = RoadNoiseUtility.GetDryBrushWobble(
                        currentPathPoint.position,
                        finalWobbleFreq,
                        finalWobbleAmp);

                    // 将抖动应用于外边缘
                    outerOffset += wobble;
                    // 确保抖动不会让内外边缘交叉
                    if (outerOffset < innerOffset) outerOffset = innerOffset + 0.1f;

                    // --- 计算4个顶点的世界坐标 ---
                    Vector3 rightDir = currentPathPoint.right;
                    Vector3 innerLeftPos = currentPathPoint.position - rightDir * innerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 outerLeftPos = currentPathPoint.position - rightDir * outerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 innerRightPos = currentPathPoint.position + rightDir * innerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 outerRightPos = currentPathPoint.position + rightDir * outerOffset + Vector3.up * profile.verticalOffset;

                    // 将世界坐标转换为本地坐标并添加到顶点列表
                    vertices.Add(roadObjectTransform.InverseTransformPoint(innerLeftPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(outerLeftPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(innerRightPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(outerRightPos));

                    // --- UV计算逻辑 (保持不变) ---
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

            // --- 5. 构建三角形索引 ---
            int verticesPerStep = settings.layerProfiles.Count * 4;
            for (int i = 0; i < totalSegments; i++)
            {
                for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
                {
                    int root = i * verticesPerStep + layerIndex * 4;
                    int rootNext = (i + 1) * verticesPerStep + layerIndex * 4;
                    var currentLayerTriangles = subMeshTriangles[layerIndex];
                    int il = root, ol = root + 1, ir = root + 2, or = root + 3;
                    int il_next = rootNext, ol_next = rootNext + 1, ir_next = rootNext + 2, or_next = rootNext + 3;
                    currentLayerTriangles.Add(il); currentLayerTriangles.Add(il_next); currentLayerTriangles.Add(ol_next);
                    currentLayerTriangles.Add(il); currentLayerTriangles.Add(ol_next); currentLayerTriangles.Add(ol);
                    currentLayerTriangles.Add(ir); currentLayerTriangles.Add(or_next); currentLayerTriangles.Add(ir_next);
                    currentLayerTriangles.Add(ir); currentLayerTriangles.Add(or); currentLayerTriangles.Add(or_next);
                }
            }

            // --- 6. 将所有数据赋值给Mesh对象 ---
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subMeshTriangles.Count;
            for (int i = 0; i < subMeshTriangles.Count; i++)
                mesh.SetTriangles(subMeshTriangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// [重构] 核心函数：根据原始控制点和设置，高效地计算出单个最终的、平滑的、贴合地形的显示点世界坐标.
        /// </summary>
        public static Vector3 GetDisplayPoint(IReadOnlyList<RoadControlPoint> localControlPoints, float t, RoadConfig settings, Transform roadObjectTransform)
        {
            // 1. 在本地坐标的原始点上获取平滑点
            Vector3 localPos = SplineUtility.GetPoint(localControlPoints, t);

            // 2. 将其转换为世界坐标
            Vector3 worldPos = roadObjectTransform.TransformPoint(localPos);

            // 3. 如果需要，对这个世界坐标点进行地形吸附
            if (settings.conformToTerrainUndulations && settings.terrainConformity > 0)
            {
                Terrain terrain = TerrainUtility.GetTerrainAt(worldPos);
                if (terrain != null)
                {
                    float terrainHeight = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                    // 使用 Lerp 根据吸附度混合原始高度和地形高度
                    worldPos.y = Mathf.Lerp(worldPos.y, terrainHeight, settings.terrainConformity);
                }
            }

            return worldPos;
        }

        // [重构] 废弃 GetTerrainSnappedControlPoints 方法，因为它效率低下
        // private static List<RoadControlPoint> GetTerrainSnappedControlPoints(...) { ... }

        // 辅助方法，获取最终用于渲染的所有点的列表 (世界坐标)，现在它会变得非常快
        private static List<Vector3> GetFinalDisplayPoints(IReadOnlyList<RoadControlPoint> localControlPoints, RoadConfig settings, Transform roadObjectTransform)
        {
            var displayPoints = new List<Vector3>();
            int totalSegments = (localControlPoints.Count - 1) * settings.splineResolution;
            if (totalSegments == 0) totalSegments = 1;

            for (int i = 0; i <= totalSegments; i++)
            {
                displayPoints.Add(GetDisplayPoint(localControlPoints, (float)i / totalSegments, settings, roadObjectTransform));
            }

            // [核心修改] 在获取所有点后，如果需要，则进行平滑处理
            if (settings.conformToTerrainUndulations && settings.verticalSmoothness > 0 && displayPoints.Count > 2)
            {
                return SmoothVerticalCurve(displayPoints, settings.verticalSmoothness, settings.smoothIterations);
            }

            return displayPoints;
        }
        /// <summary>
        /// 对路径点的垂直（Y轴）位置进行平滑处理。
        /// </summary>
        /// <param name="points">需要平滑的点列表</param>
        /// <param name="smoothness">平滑度，介于0和1之间</param>
        /// <param name="iterations">迭代次数，次数越多越平滑</param>
        /// <returns>平滑处理后的新点列表</returns>
        private static List<Vector3> SmoothVerticalCurve(List<Vector3> points, float smoothness, int iterations)
        {
            List<Vector3> smoothedPoints = new List<Vector3>(points);

            for (int i = 0; i < iterations; i++)
            {
                // 每次迭代都基于上一次平滑的结果
                List<Vector3> currentPoints = new List<Vector3>(smoothedPoints);

                // 遍历除首尾之外的所有点
                for (int j = 1; j < currentPoints.Count - 1; j++)
                {
                    float previousY = currentPoints[j - 1].y;
                    float currentY = currentPoints[j].y;
                    float nextY = currentPoints[j + 1].y;

                    // 计算前后两个点的Y轴平均值
                    float averageY = (previousY + nextY) / 2f;

                    // 使用 Lerp 将当前点的Y值朝平均值移动，移动距离由smoothness决定
                    float smoothedY = Mathf.Lerp(currentY, averageY, smoothness);

                    Vector3 point = smoothedPoints[j];
                    point.y = smoothedY;
                    smoothedPoints[j] = point;
                }
            }

            return smoothedPoints;
        }

        // 辅助方法，计算包含方向和距离的完整路径点，逻辑不变
        private static PathPoint CalculateFinalPathPoint(IReadOnlyList<RoadControlPoint> localControlPoints, IReadOnlyList<Vector3> worldDisplayPoints, float t, RoadConfig settings, float distance, Transform roadObjectTransform)
        {
            int totalSegments = worldDisplayPoints.Count - 1;
            int currentIndex = Mathf.FloorToInt(t * totalSegments);
            currentIndex = Mathf.Clamp(currentIndex, 0, totalSegments);

            Vector3 displayPoint = worldDisplayPoints[currentIndex];
            displayPoint.y += settings.previewHeightOffset;

            // 方向的计算仍然基于原始的本地控制点，这样可以避免地形起伏造成方向向量的剧烈抖动，使道路更平滑
            Vector3 localForward = SplineUtility.GetVelocity(localControlPoints, t).normalized;
            Vector3 worldForward = roadObjectTransform.TransformDirection(localForward);

            if (worldForward == Vector3.zero)
            {
                if (localControlPoints.Count > 1)
                {
                    Vector3 localDir = (localControlPoints[localControlPoints.Count - 1].position - localControlPoints[0].position).normalized;
                    worldForward = roadObjectTransform.TransformDirection(localDir);
                }
                if (worldForward == Vector3.zero)
                {
                    worldForward = roadObjectTransform.forward;
                }
            }

            Vector3 right;
            // 如果前进方向与世界垂直方向过于接近
            if (Mathf.Abs(Vector3.Dot(worldForward, Vector3.up)) > 0.999f)
            {
                // 使用世界前方作为 "up" 来计算 right
                right = Vector3.Cross(Vector3.forward, worldForward).normalized;
            }
            else
            {
                // 正常情况
                right = Vector3.Cross(Vector3.up, worldForward).normalized;
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