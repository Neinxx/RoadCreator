// 文件路径: Assets/RoadCreator/Editor/Terrain/UnifiedTerrainProcessor.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    public class UnifiedTerrainProcessor
    {
        // 用于在虚拟画布中存储每个像素的数据
        private struct PixelData
        {
            public float Height;
            public int TextureLayerIndex;
            public float TextureBlendStrength;
            public bool IsSet;
        }

        public void Execute(RoadManager roadManager)
        {
            var roadMesh = roadManager.MeshFilter.sharedMesh;
            if (roadMesh == null || roadMesh.vertexCount == 0) return;

            // ========================================================================
            // 阶段一：收集与准备
            // ========================================================================

            // 1. 找到所有与道路相交的地形
            Bounds roadWorldBounds = roadMesh.GetWorldBounds(roadManager.transform);
            List<Terrain> affectedTerrains = FindAffectedTerrains(roadWorldBounds);
            if (affectedTerrains.Count == 0) return;

            // 2. 计算一个能包裹所有受影响区域的总边界框 (Super Bounds)
            Bounds superBounds = CalculateSuperBounds(roadWorldBounds, affectedTerrains);

            // 3. 基于总边界框创建统一的虚拟画布
            // 我们需要知道分辨率来创建画布。这里我们取第一个地形的分辨率作为基准。
            // (假设所有地形分辨率一致，这是Unity地形系统的通常用法)
            TerrainData referenceTd = affectedTerrains[0].terrainData;
            float pixelsPerMeter = referenceTd.heightmapResolution / referenceTd.size.x;

            int superWidth = Mathf.CeilToInt(superBounds.size.x * pixelsPerMeter);
            int superHeight = Mathf.CeilToInt(superBounds.size.z * pixelsPerMeter);
            if (superWidth <= 0 || superHeight <= 0) return;

            PixelData[,] modificationMap = new PixelData[superHeight, superWidth];

            // ========================================================================
            // 阶段二：统一计算 (在虚拟画布上光栅化)
            // ========================================================================

            Debug.Log($"开始统一计算... 虚拟画布尺寸: {superWidth}x{superHeight}");
            var meshVertices = roadMesh.vertices;
            var roadConfig = roadManager.RoadConfig;

            for (int subMeshIndex = 0; subMeshIndex < roadMesh.subMeshCount; subMeshIndex++)
            {
                if (subMeshIndex >= roadConfig.layerProfiles.Count) continue;

                var profile = roadConfig.layerProfiles[subMeshIndex];
                // 注意：layerIndex现在需要针对每个地形单独获取，我们暂时只存一个标记
                int tempLayerId = subMeshIndex; // 临时用submesh索引作为图层ID

                var triangles = roadMesh.GetTriangles(subMeshIndex);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 v0 = roadManager.transform.TransformPoint(meshVertices[triangles[i]]);
                    Vector3 v1 = roadManager.transform.TransformPoint(meshVertices[triangles[i + 1]]);
                    Vector3 v2 = roadManager.transform.TransformPoint(meshVertices[triangles[i + 2]]);

                    // 计算此三角形在虚拟画布上的像素范围
                    Bounds triBounds = new Bounds { min = Vector3.Min(Vector3.Min(v0, v1), v2), max = Vector3.Max(Vector3.Max(v0, v1), v2) };
                    RectInt triPixelRect = WorldBoundsToPixelRect(triBounds, superBounds, superWidth, superHeight);

                    for (int y = triPixelRect.yMin; y < triPixelRect.yMax; y++)
                    {
                        for (int x = triPixelRect.xMin; x < triPixelRect.xMax; x++)
                        {
                            // 将画布像素坐标转换回世界坐标
                            Vector3 worldPos = PixelToWorldPos(x, y, superBounds, superWidth, superHeight);

                            if (TerrainUtility.IsPointInTriangle(new Vector2(worldPos.x, worldPos.z), new Vector2(v0.x, v0.z), new Vector2(v1.x, v1.z), new Vector2(v2.x, v2.z)))
                            {
                                Vector3 bary = TerrainUtility.Barycentric(new Vector2(worldPos.x, worldPos.z), new Vector2(v0.x, v0.z), new Vector2(v1.x, v1.z), new Vector2(v2.x, v2.z));
                                float height = bary.x * v0.y + bary.y * v1.y + bary.z * v2.y;

                                modificationMap[y, x] = new PixelData
                                {
                                    Height = height - roadManager.TerrainConfig.flattenOffset,
                                    TextureLayerIndex = tempLayerId, // 暂时存储
                                    TextureBlendStrength = profile.textureBlendFactor,
                                    IsSet = true
                                };
                            }
                        }
                    }
                }
            }

            // ========================================================================
            // 阶段三：分发与应用
            // ========================================================================

            Debug.Log("计算完成，开始将数据分发到各地形...");
            foreach (var terrain in affectedTerrains)
            {
                ApplyToSingleTerrain(terrain, roadManager, modificationMap, superBounds, superWidth, superHeight);
            }
            Debug.Log("所有地形应用完毕！");
        }

        private void ApplyToSingleTerrain(Terrain terrain, RoadManager roadManager, PixelData[,] modificationMap, Bounds superBounds, int superWidth, int superHeight)
        {
            var terrainData = terrain.terrainData;
            var terrainPos = terrain.transform.position;

            // 1. 计算此地形与总边界框的重叠区域 (世界空间)
            Bounds terrainWorldBounds = new Bounds(terrainPos + terrainData.size / 2, terrainData.size);
            Bounds overlapWorldBounds = new Bounds();
            overlapWorldBounds.SetMinMax(
                Vector3.Max(superBounds.min, terrainWorldBounds.min),
                Vector3.Min(superBounds.max, terrainWorldBounds.max)
            );

            if (overlapWorldBounds.size.x <= 0 || overlapWorldBounds.size.z <= 0) return;

            // 2. 将重叠区域转换为两个坐标系下的像素矩形：
            //    - 地形自己的高度图坐标 (targetRect)
            //    - 虚拟画布的坐标 (sourceRect)
            RectInt targetRect = WorldBoundsToHeightmapRect(overlapWorldBounds, terrain);
            RectInt sourceRect = WorldBoundsToPixelRect(overlapWorldBounds, superBounds, superWidth, superHeight);

            if (targetRect.width <= 0 || targetRect.height <= 0) return;

            // 确保拷贝尺寸一致
            int copyWidth = Mathf.Min(targetRect.width, sourceRect.width);
            int copyHeight = Mathf.Min(targetRect.height, sourceRect.height);

            // 3. 拷贝高度数据
            float[,] heights = terrainData.GetHeights(targetRect.x, targetRect.y, copyWidth, copyHeight);
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    PixelData mod = modificationMap[sourceRect.y + y, sourceRect.x + x];
                    if (mod.IsSet)
                    {
                        float originalHeight = heights[y, x] * terrainData.size.y;
                        heights[y, x] = Mathf.Lerp(originalHeight, mod.Height, roadManager.TerrainConfig.flattenStrength) / terrainData.size.y;
                    }
                }
            }
            terrainData.SetHeights(targetRect.x, targetRect.y, heights);

            // 4. 拷贝纹理数据 - 使用高性能方法
            ApplyTextureHighPerformance(terrainData, targetRect, modificationMap);
        }

        #region 高性能纹理绘制方法（方案2）

        // 添加新的高性能纹理应用方法
        private void ApplyTextureHighPerformance(TerrainData terrainData, RectInt targetRect, PixelData[,] modificationMap)
        {
            // 获取alphamap区域
            RectInt alphamapRect = HeightmapRectToAlphamapRect(targetRect, terrainData);
            if (alphamapRect.width <= 0 || alphamapRect.height <= 0) return;

            alphamapRect.ClampToBounds(new RectInt(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight));
            if (alphamapRect.width <= 0 || alphamapRect.height <= 0) return;

            // 批量处理纹理数据
            float[,,] alphamaps = terrainData.GetAlphamaps(alphamapRect.x, alphamapRect.y, alphamapRect.width, alphamapRect.height);

            // 使用向量化操作优化性能
            ApplyTextureBatched(alphamaps, alphamapRect, targetRect, modificationMap, terrainData);

            terrainData.SetAlphamaps(alphamapRect.x, alphamapRect.y, alphamaps);
        }

        private void ApplyTextureBatched(float[,,] alphamaps, RectInt alphamapRect, RectInt heightmapRect,
                                       PixelData[,] modificationMap, TerrainData terrainData)
        {
            int alphamapWidth = alphamapRect.width;
            int alphamapHeight = alphamapRect.height;
            int numLayers = alphamaps.GetLength(2);

            // 预计算高度图到alphamap的映射关系，避免重复计算
            Vector2Int[,] hmToAlphaMap = new Vector2Int[heightmapRect.height, heightmapRect.width];
            for (int y = 0; y < heightmapRect.height; y++)
            {
                for (int x = 0; x < heightmapRect.width; x++)
                {
                    Vector2Int alphaCoords = HeightmapToAlphamapCoords(
                        x + heightmapRect.x,
                        y + heightmapRect.y,
                        terrainData);
                    hmToAlphaMap[y, x] = new Vector2Int(
                        alphaCoords.x - alphamapRect.x,
                        alphaCoords.y - alphamapRect.y);
                }
            }

            // 批量处理纹理混合
            for (int y = 0; y < heightmapRect.height; y++)
            {
                for (int x = 0; x < heightmapRect.width; x++)
                {
                    var mod = modificationMap[y, x];
                    if (mod.IsSet && mod.TextureLayerIndex != -1 && mod.TextureLayerIndex < numLayers)
                    {
                        Vector2Int alphaPos = hmToAlphaMap[y, x];
                        if (alphaPos.x >= 0 && alphaPos.x < alphamapWidth &&
                            alphaPos.y >= 0 && alphaPos.y < alphamapHeight)
                        {
                            PaintAlphamapOptimized(alphamaps, alphaPos.x, alphaPos.y,
                                                 mod.TextureLayerIndex, mod.TextureBlendStrength, numLayers);
                        }
                    }
                }
            }
        }

        private void PaintAlphamapOptimized(float[,,] alphamaps, int x, int y,
                                          int layerIndex, float strength, int numLayers)
        {
            // 优化的纹理混合算法，减少循环次数
            float currentWeight = alphamaps[y, x, layerIndex];
            float targetWeight = Mathf.Lerp(currentWeight, 1.0f, strength);
            float weightDiff = targetWeight - currentWeight;

            // 直接调整目标图层
            alphamaps[y, x, layerIndex] = targetWeight;

            // 按比例调整其他图层
            float otherWeightsTotal = 1.0f - currentWeight;
            float newOtherWeightsTotal = 1.0f - targetWeight;

            if (otherWeightsTotal > 0.001f && newOtherWeightsTotal > 0.001f)
            {
                float scale = newOtherWeightsTotal / otherWeightsTotal;
                for (int i = 0; i < numLayers; i++)
                {
                    if (i != layerIndex)
                    {
                        alphamaps[y, x, i] *= scale;
                    }
                }
            }
            else if (newOtherWeightsTotal <= 0.001f)
            {
                // 如果其他图层权重接近0，则平均分配
                float avgWeight = newOtherWeightsTotal / (numLayers - 1);
                for (int i = 0; i < numLayers; i++)
                {
                    if (i != layerIndex)
                    {
                        alphamaps[y, x, i] = avgWeight;
                    }
                }
            }
        }

        // 辅助方法：高度图坐标转alphamap坐标
        private Vector2Int HeightmapToAlphamapCoords(int hmX, int hmY, TerrainData terrainData)
        {
            int alphaX = Mathf.RoundToInt((float)hmX / (terrainData.heightmapResolution - 1) * (terrainData.alphamapResolution - 1));
            int alphaY = Mathf.RoundToInt((float)hmY / (terrainData.heightmapResolution - 1) * (terrainData.alphamapResolution - 1));
            return new Vector2Int(alphaX, alphaY);
        }

        private RectInt HeightmapRectToAlphamapRect(RectInt heightmapRect, TerrainData terrainData)
        {
            Vector2Int min = HeightmapToAlphamapCoords(heightmapRect.xMin, heightmapRect.yMin, terrainData);
            Vector2Int max = HeightmapToAlphamapCoords(heightmapRect.xMax, heightmapRect.yMax, terrainData);
            return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        #endregion

        #region Helper Methods
        private List<Terrain> FindAffectedTerrains(Bounds worldBounds)
        {
            return Object.FindObjectsOfType<Terrain>().Where(t =>
                new Bounds(t.transform.position + t.terrainData.size / 2, t.terrainData.size).Intersects(worldBounds)
            ).ToList();
        }

        private Bounds CalculateSuperBounds(Bounds initialBounds, List<Terrain> terrains)
        {
            Bounds combined = initialBounds;
            foreach (var terrain in terrains)
            {
                Bounds terrainBounds = new Bounds(terrain.transform.position + terrain.terrainData.size / 2, terrain.terrainData.size);
                combined.Encapsulate(terrainBounds.min);
                combined.Encapsulate(terrainBounds.max);
            }
            // 我们只需要与初始包围盒相交的部分
            combined.SetMinMax(Vector3.Max(initialBounds.min, combined.min), Vector3.Min(initialBounds.max, combined.max));
            return combined;
        }

        private RectInt WorldBoundsToPixelRect(Bounds worldBounds, Bounds containerBounds, int containerWidth, int containerHeight)
        {
            Vector3 localMin = worldBounds.min - containerBounds.min;
            Vector3 localMax = worldBounds.max - containerBounds.min;

            int minX = Mathf.FloorToInt((localMin.x / containerBounds.size.x) * containerWidth);
            int maxX = Mathf.CeilToInt((localMax.x / containerBounds.size.x) * containerWidth);
            int minY = Mathf.FloorToInt((localMin.z / containerBounds.size.z) * containerHeight);
            int maxY = Mathf.CeilToInt((localMax.z / containerBounds.size.z) * containerHeight);

            minX = Mathf.Clamp(minX, 0, containerWidth);
            maxX = Mathf.Clamp(maxX, 0, containerWidth);
            minY = Mathf.Clamp(minY, 0, containerHeight);
            maxY = Mathf.Clamp(maxY, 0, containerHeight);

            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        private Vector3 PixelToWorldPos(int x, int y, Bounds containerBounds, int containerWidth, int containerHeight)
        {
            float normX = (x + 0.5f) / containerWidth;
            float normZ = (y + 0.5f) / containerHeight;
            return new Vector3(
                containerBounds.min.x + normX * containerBounds.size.x,
                0, // Y值在这里不重要
                containerBounds.min.z + normZ * containerBounds.size.z
            );
        }

        // 你已有的 WorldBoundsToHeightmapRect 方法可以复用
        private RectInt WorldBoundsToHeightmapRect(Bounds bounds, Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 terrainPos = terrain.GetPosition();
            Vector3 localMin = bounds.min - terrainPos;
            Vector3 localMax = bounds.max - terrainPos;
            int minX = Mathf.FloorToInt((localMin.x / td.size.x) * td.heightmapResolution);
            int maxX = Mathf.CeilToInt((localMax.x / td.size.x) * td.heightmapResolution);
            int minY = Mathf.FloorToInt((localMin.z / td.size.z) * td.heightmapResolution);
            int maxY = Mathf.CeilToInt((localMax.z / td.size.z) * td.heightmapResolution);
            minX = Mathf.Clamp(minX, 0, td.heightmapResolution - 1);
            maxX = Mathf.Clamp(maxX, 0, td.heightmapResolution - 1);
            minY = Mathf.Clamp(minY, 0, td.heightmapResolution - 1);
            maxY = Mathf.Clamp(maxY, 0, td.heightmapResolution - 1);
            int width = Mathf.Max(0, maxX - minX);
            int height = Mathf.Max(0, maxY - minY);
            return new RectInt(minX, minY, width, height);
        }
        #endregion
    }
}