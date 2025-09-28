using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    public class CPUFlattenAndTextureModule : ITerrainModificationModule
    {
        public string ModuleName => "CPU High-Performance Mesh Processor";

        private struct PixelData
        {
            public float Height;
            public int TextureLayerIndex;
            public float TextureBlendStrength;
            public bool IsSet;
        }

        public void Execute(TerrainModificationData data)
        {
            var roadManager = data.RoadManager;
            var terrain = data.Terrain;
            var terrainConfig = roadManager.TerrainConfig;
            var roadConfig = roadManager.RoadConfig;
            var roadMesh = roadManager.MeshFilter.sharedMesh;

            if (terrain == null || roadMesh == null || roadMesh.vertexCount == 0) return;

            // =======================================================================================
            // [核心修正] 解决多地形处理的关键逻辑
            // 不再使用整个道路的包围盒，而是计算道路与当前这块地形相交部分的包围盒。
            // =======================================================================================

            // 1. 获取道路网格在世界空间中的完整包围盒
            Bounds totalRoadWorldBounds = roadMesh.GetWorldBounds(roadManager.transform);

            // 2. 获取当前地形块在世界空间中的XZ平面矩形
            TerrainData terrainData = terrain.terrainData;
            Vector3 terrainWorldPos = terrain.transform.position;
            Rect terrainWorldRect = new Rect(terrainWorldPos.x, terrainWorldPos.z, terrainData.size.x, terrainData.size.z);

            // 3. 计算道路和地形在世界空间XZ平面上的实际相交矩形
            Rect roadWorldRect = new Rect(totalRoadWorldBounds.min.x, totalRoadWorldBounds.min.z, totalRoadWorldBounds.size.x, totalRoadWorldBounds.size.z);

            // 使用数学函数精确计算交集，避免浮点数精度问题
            float x1 = Mathf.Max(terrainWorldRect.x, roadWorldRect.x);
            float y1 = Mathf.Max(terrainWorldRect.y, roadWorldRect.y);
            float x2 = Mathf.Min(terrainWorldRect.xMax, roadWorldRect.xMax);
            float y2 = Mathf.Min(terrainWorldRect.yMax, roadWorldRect.yMax);

            // 4. 如果没有重叠区域，则此地形块无需处理，直接返回
            if (x2 <= x1 || y2 <= y1)
            {
                return;
            }

            // 5. 根据计算出的2D交集，创建一个精确的3D世界空间包围盒
            Rect intersectionWorldRect = new Rect(x1, y1, x2 - x1, y2 - y1);
            Bounds intersectionWorldBounds = new Bounds(
                new Vector3(intersectionWorldRect.center.x, totalRoadWorldBounds.center.y, intersectionWorldRect.center.y),
                new Vector3(intersectionWorldRect.width, totalRoadWorldBounds.size.y, intersectionWorldRect.height)
            );

            // 6. 将这个精确的、局部的相交包围盒转换为地形高度图上的像素矩形
            RectInt heightmapRect = WorldBoundsToHeightmapRect(intersectionWorldBounds, terrain);
            if (heightmapRect.width <= 0 || heightmapRect.height <= 0) return;
            // =======================================================================================
            // [核心修正结束] - 后续所有计算都将在此 `heightmapRect` 范围内进行
            // =======================================================================================

            PixelData[,] modifications = new PixelData[heightmapRect.height, heightmapRect.width];
            var meshVertices = roadMesh.vertices;

            // 1. 快速光栅化，确定基础图层
            for (int subMeshIndex = 0; subMeshIndex < roadMesh.subMeshCount; subMeshIndex++)
            {
                if (subMeshIndex >= roadConfig.layerProfiles.Count) continue;
                var profile = roadConfig.layerProfiles[subMeshIndex];
                int layerIndex = EditorTerrainUtility.EnsureAndGetLayerIndex(terrain, profile.terrainLayer);

                var triangles = roadMesh.GetTriangles(subMeshIndex);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 v0 = roadManager.transform.TransformPoint(meshVertices[triangles[i]]);
                    Vector3 v1 = roadManager.transform.TransformPoint(meshVertices[triangles[i + 1]]);
                    Vector3 v2 = roadManager.transform.TransformPoint(meshVertices[triangles[i + 2]]);

                    Vector2 p0 = new Vector2(v0.x, v0.z);
                    Vector2 p1 = new Vector2(v1.x, v1.z);
                    Vector2 p2 = new Vector2(v2.x, v2.z);

                    // 计算每个三角形的包围盒，并将其裁剪到当前地形块的有效范围内
                    RectInt triPixelRect = WorldBoundsToHeightmapRect(new Bounds((v0 + v1 + v2) / 3f, Vector3.zero) { min = Vector3.Min(Vector3.Min(v0, v1), v2), max = Vector3.Max(Vector3.Max(v0, v1), v2) }, terrain);
                    triPixelRect.ClampToBounds(heightmapRect); // 关键：确保只处理与当前地形相交的部分

                    for (int y = triPixelRect.yMin; y < triPixelRect.yMax; y++)
                    {
                        for (int x = triPixelRect.xMin; x < triPixelRect.xMax; x++)
                        {
                            int localX = x - heightmapRect.x;
                            int localY = y - heightmapRect.y;

                            Vector3 worldPos = HeightmapToWorldPos(x, y, terrain);
                            if (TerrainUtility.IsPointInTriangle(new Vector2(worldPos.x, worldPos.z), p0, p1, p2))
                            {
                                Vector3 barycentric = TerrainUtility.Barycentric(new Vector2(worldPos.x, worldPos.z), p0, p1, p2);
                                if (barycentric.x < 0 || barycentric.y < 0 || barycentric.z < 0) continue;
                                float height = barycentric.x * v0.y + barycentric.y * v1.y + barycentric.z * v2.y;

                                modifications[localY, localX] = new PixelData
                                {
                                    Height = height - terrainConfig.flattenOffset,
                                    TextureLayerIndex = layerIndex,
                                    TextureBlendStrength = profile.textureBlendFactor,
                                    IsSet = true
                                };
                            }
                        }
                    }
                }
            }

            // 应用风格化、高度、平滑和纹理
            modifications = ApplyStylizationPass(modifications, heightmapRect, terrain, terrainConfig);

            float[,] heights = terrainData.GetHeights(heightmapRect.x, heightmapRect.y, heightmapRect.width, heightmapRect.height);
            for (int y = 0; y < heightmapRect.height; y++)
            {
                for (int x = 0; x < heightmapRect.width; x++)
                {
                    if (modifications[y, x].IsSet)
                    {
                        float originalHeight = heights[y, x] * terrainData.size.y;
                        heights[y, x] = Mathf.Lerp(originalHeight, modifications[y, x].Height, terrainConfig.flattenStrength) / terrainData.size.y;
                    }
                }
            }

            if (terrainConfig.flattenFeatherWidth > 0)
            {
                heights = ApplySmoothingPass(heights, modifications, (int)terrainConfig.flattenFeatherWidth);
            }
            terrainData.SetHeights(heightmapRect.x, heightmapRect.y, heights);

            //  ApplyTexture(terrainData, heightmapRect, modifications);
        }

        // ... 其他方法保持不变 ...
        // ApplyStylizationPass, ApplyTexture, ApplySmoothingPass, PaintAlphamap, 和所有 Helper Methods 都不需要修改。
        // 因为它们的操作范围已经由 Execute 方法开头计算出的 `heightmapRect` 限定了。
        #region Unchanged Methods

        private PixelData[,] ApplyStylizationPass(PixelData[,] modifications, RectInt heightmapRect, Terrain terrain, TerrainConfig terrainConfig)
        {
            int width = modifications.GetLength(1);
            int height = modifications.GetLength(0);
            PixelData[,] stylizedModifications = (PixelData[,])modifications.Clone();
            Vector2Int[] neighbors = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!modifications[y, x].IsSet) continue;

                    int currentLayer = modifications[y, x].TextureLayerIndex;
                    bool isEdge = false;

                    foreach (var offset in neighbors)
                    {
                        int nx = x + offset.x;
                        int ny = y + offset.y;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height && modifications[ny, nx].IsSet)
                        {
                            if (modifications[ny, nx].TextureLayerIndex != currentLayer)
                            {
                                isEdge = true;
                                break;
                            }
                        }
                    }

                    if (isEdge)
                    {
                        Vector3 worldPos = HeightmapToWorldPos(x + heightmapRect.x, y + heightmapRect.y, terrain);
                        float noise = Mathf.PerlinNoise(worldPos.x * terrainConfig.textureNoiseScale, worldPos.z * terrainConfig.textureNoiseScale);

                        if (noise > 0.6f)
                        {
                            var randomNeighbor = neighbors[Random.Range(0, 4)];
                            int nx = x + randomNeighbor.x;
                            int ny = y + randomNeighbor.y;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height && modifications[ny, nx].IsSet)
                            {
                                stylizedModifications[y, x].TextureLayerIndex = modifications[ny, nx].TextureLayerIndex;
                                stylizedModifications[y, x].TextureBlendStrength = modifications[ny, nx].TextureBlendStrength;
                            }
                        }
                    }
                }
            }
            return stylizedModifications;
        }

        private void ApplyTexture(TerrainData terrainData, RectInt heightmapRect, PixelData[,] modifications)
        {
            RectInt alphamapRect = HeightmapRectToAlphamapRect(heightmapRect, terrainData);
            if (alphamapRect.width <= 0 || alphamapRect.height <= 0) return;

            alphamapRect.ClampToBounds(new RectInt(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight));
            if (alphamapRect.width <= 0 || alphamapRect.height <= 0) return;

            float[,,] alphamaps = terrainData.GetAlphamaps(alphamapRect.x, alphamapRect.y, alphamapRect.width, alphamapRect.height);

            for (int y = 0; y < alphamapRect.height; y++)
            {
                for (int x = 0; x < alphamapRect.width; x++)
                {
                    Vector2Int hmCoords = AlphamapToHeightmapCoords(x + alphamapRect.x, y + alphamapRect.y, terrainData);
                    int localHmX = hmCoords.x - heightmapRect.x;
                    int localHmY = hmCoords.y - heightmapRect.y;

                    if (localHmX >= 0 && localHmX < heightmapRect.width && localHmY >= 0 && localHmY < heightmapRect.height)
                    {
                        var mod = modifications[localHmY, localHmX];
                        if (mod.IsSet && mod.TextureLayerIndex != -1)
                        {
                            PaintAlphamap(alphamaps, x, y, mod.TextureLayerIndex, mod.TextureBlendStrength);
                        }
                    }
                }
            }
            terrainData.SetAlphamaps(alphamapRect.x, alphamapRect.y, alphamaps);
        }

        private float[,] ApplySmoothingPass(float[,] heights, PixelData[,] modifications, int kernelSize)
        {
            int width = heights.GetLength(1);
            int height = heights.GetLength(0);
            float[,] smoothedHeights = (float[,])heights.Clone();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!modifications[y, x].IsSet)
                    {
                        float influence = 0;
                        for (int j = -kernelSize; j <= kernelSize; j++)
                        {
                            for (int i = -kernelSize; i <= kernelSize; i++)
                            {
                                int nx = x + i;
                                int ny = y + j;
                                if (nx >= 0 && nx < width && ny >= 0 && ny < height && modifications[ny, nx].IsSet)
                                {
                                    float dist = Mathf.Sqrt(i * i + j * j);
                                    if (dist <= kernelSize)
                                    {
                                        influence = Mathf.Max(influence, 1.0f - (dist / kernelSize));
                                    }
                                }
                            }
                        }

                        if (influence > 0)
                        {
                            float avgNeighborHeight = 0;
                            int count = 0;
                            for (int j = -1; j <= 1; j++)
                            {
                                for (int i = -1; i <= 1; i++)
                                {
                                    int nx = x + i;
                                    int ny = y + j;
                                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                    {
                                        avgNeighborHeight += heights[ny, nx];
                                        count++;
                                    }
                                }
                            }
                            if (count > 0)
                            {
                                smoothedHeights[y, x] = Mathf.Lerp(heights[y, x], avgNeighborHeight / count, influence * influence);
                            }
                        }
                    }
                }
            }
            return smoothedHeights;
        }

        private void PaintAlphamap(float[,,] alphamaps, int x, int y, int layerIndex, float strength)
        {
            int numLayers = alphamaps.GetLength(2);
            if (layerIndex >= numLayers) return;

            float[] originalWeights = new float[numLayers];
            for (int i = 0; i < numLayers; i++)
            {
                originalWeights[i] = alphamaps[y, x, i];
            }

            float targetWeight = Mathf.Lerp(originalWeights[layerIndex], 1.0f, strength);

            float totalWeight = 0;
            float[] newWeights = new float[numLayers];

            newWeights[layerIndex] = targetWeight;
            totalWeight += targetWeight;

            float remainingWeight = 1.0f - targetWeight;
            float originalOtherLayersTotal = 1.0f - originalWeights[layerIndex];

            for (int i = 0; i < numLayers; i++)
            {
                if (i == layerIndex) continue;

                if (originalOtherLayersTotal > 0.001f)
                {
                    newWeights[i] = originalWeights[i] / originalOtherLayersTotal * remainingWeight;
                }
                else
                {
                    newWeights[i] = remainingWeight / (numLayers - 1);
                }
                totalWeight += newWeights[i];
            }

            for (int i = 0; i < numLayers; i++)
            {
                alphamaps[y, x, i] = newWeights[i] / totalWeight;
            }
        }

        private RectInt WorldBoundsToHeightmapRect(Bounds bounds, Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 terrainPos = terrain.GetPosition();

            Vector3 localMin = bounds.min - terrainPos;
            Vector3 localMax = bounds.max - terrainPos;

            // 原始计算
            int minX = Mathf.FloorToInt((localMin.x / td.size.x) * td.heightmapResolution);
            int maxX = Mathf.CeilToInt((localMax.x / td.size.x) * td.heightmapResolution);
            int minY = Mathf.FloorToInt((localMin.z / td.size.z) * td.heightmapResolution);
            int maxY = Mathf.CeilToInt((localMax.z / td.size.z) * td.heightmapResolution);

            // [新增逻辑] 应用1像素的Padding并进行Clamp
            int padding = 1;
            minX = Mathf.Clamp(minX - padding, 0, td.heightmapResolution - 1);
            maxX = Mathf.Clamp(maxX + padding, 0, td.heightmapResolution - 1);
            minY = Mathf.Clamp(minY - padding, 0, td.heightmapResolution - 1);
            maxY = Mathf.Clamp(maxY + padding, 0, td.heightmapResolution - 1);

            int width = Mathf.Max(0, maxX - minX);
            int height = Mathf.Max(0, maxY - minY);

            return new RectInt(minX, minY, width, height);
        }

        private Vector3 HeightmapToWorldPos(int x, int y, Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 terrainPos = terrain.GetPosition();
            float normX = x / (float)(td.heightmapResolution - 1);
            float normZ = y / (float)(td.heightmapResolution - 1);
            float worldX = terrainPos.x + normX * td.size.x;
            float worldZ = terrainPos.z + normZ * td.size.z;
            return new Vector3(worldX, 0, worldZ);
        }

        private RectInt HeightmapRectToAlphamapRect(RectInt heightmapRect, TerrainData terrainData)
        {
            Vector2Int min = HeightmapToAlphamapCoords(heightmapRect.xMin, heightmapRect.yMin, terrainData);
            Vector2Int max = HeightmapToAlphamapCoords(heightmapRect.xMax, heightmapRect.yMax, terrainData);
            return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        private Vector2Int AlphamapToHeightmapCoords(int alphaX, int alphaY, TerrainData terrainData)
        {
            int hmX = Mathf.RoundToInt((float)alphaX / (terrainData.alphamapResolution - 1) * (terrainData.heightmapResolution - 1));
            int hmY = Mathf.RoundToInt((float)alphaY / (terrainData.alphamapResolution - 1) * (terrainData.heightmapResolution - 1));
            return new Vector2Int(hmX, hmY);
        }

        private Vector2Int HeightmapToAlphamapCoords(int hmX, int hmY, TerrainData terrainData)
        {
            int alphaX = Mathf.RoundToInt((float)hmX / (terrainData.heightmapResolution - 1) * (terrainData.alphamapResolution - 1));
            int alphaY = Mathf.RoundToInt((float)hmY / (terrainData.heightmapResolution - 1) * (terrainData.alphamapResolution - 1));
            return new Vector2Int(alphaX, alphaY);
        }

        #endregion
    }
}