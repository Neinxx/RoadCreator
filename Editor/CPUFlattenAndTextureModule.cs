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

            var terrainData = terrain.terrainData;
            int heightmapRes = terrainData.heightmapResolution;

            Bounds roadBounds = roadMesh.GetWorldBounds(roadManager.transform);
            RectInt heightmapRect = WorldBoundsToHeightmapRect(roadBounds, terrain);
            if (heightmapRect.width <= 0 || heightmapRect.height <= 0) return;

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

                    RectInt triPixelRect = WorldBoundsToHeightmapRect(new Bounds((v0 + v1 + v2) / 3f, Vector3.zero) { min = Vector3.Min(Vector3.Min(v0, v1), v2), max = Vector3.Max(Vector3.Max(v0, v1), v2) }, terrain);

                    for (int y = triPixelRect.yMin; y < triPixelRect.yMax; y++)
                    {
                        for (int x = triPixelRect.xMin; x < triPixelRect.xMax; x++)
                        {
                            int localX = x - heightmapRect.x;
                            int localY = y - heightmapRect.y;
                            if (localX < 0 || localX >= heightmapRect.width || localY < 0 || localY >= heightmapRect.height) continue;

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

            // 2. [新功能] 对光栅化结果进行边缘风格化处理
            modifications = ApplyStylizationPass(modifications, heightmapRect, terrain, terrainConfig);

            // 3. 应用高度修改
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

            // 4. 应用平滑
            if (terrainConfig.flattenFeatherWidth > 0)
            {
                heights = ApplySmoothingPass(heights, modifications, (int)terrainConfig.flattenFeatherWidth);
            }
            terrainData.SetHeights(heightmapRect.x, heightmapRect.y, heights);

            // 5. 应用纹理
            ApplyTexture(terrainData, heightmapRect, modifications);
        }

        /// <summary>
        /// [新功能] 对光栅化后的图层数据进行边缘风格化处理
        /// </summary>
        private PixelData[,] ApplyStylizationPass(PixelData[,] modifications, RectInt heightmapRect, Terrain terrain, TerrainConfig terrainConfig)
        {
            int width = modifications.GetLength(1);
            int height = modifications.GetLength(0);
            PixelData[,] stylizedModifications = (PixelData[,])modifications.Clone();

            // 定义邻居检查的偏移量
            Vector2Int[] neighbors = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!modifications[y, x].IsSet) continue;

                    int currentLayer = modifications[y, x].TextureLayerIndex;
                    bool isEdge = false;

                    // 检查邻居是否属于不同图层
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

                        // 根据噪声值决定是否“侵蚀”邻居
                        // 例如，如果噪声值大于0.6，则有几率变成邻居的图层
                        if (noise > 0.6f)
                        {
                            // 随机选择一个邻居
                            var randomNeighbor = neighbors[Random.Range(0, 4)];
                            int nx = x + randomNeighbor.x;
                            int ny = y + randomNeighbor.y;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height && modifications[ny, nx].IsSet)
                            {
                                // "窃取"邻居的图层信息
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
                    // If this pixel was NOT part of the original road, check if it's near one.
                    if (!modifications[y, x].IsSet)
                    {
                        float influence = 0;
                        // Check neighbors for influence
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

        #region Helper Methods
        private void PaintAlphamap(float[,,] alphamaps, int x, int y, int layerIndex, float strength)
        {
            int numLayers = alphamaps.GetLength(2);
            if (layerIndex >= numLayers) return;

            float[] weights = new float[numLayers];
            for (int i = 0; i < numLayers; i++) { weights[i] = 0; } // Start with a clean slate
            weights[layerIndex] = 1.0f; // This layer wins

            // Blend with original terrain based on strength
            float[] originalWeights = new float[numLayers];
            for (int i = 0; i < numLayers; i++) { originalWeights[i] = alphamaps[y, x, i]; }

            for (int i = 0; i < numLayers; i++)
            {
                alphamaps[y, x, i] = Mathf.Lerp(originalWeights[i], weights[i], strength);
            }
        }

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
            minX = Mathf.Clamp(minX, 0, td.heightmapResolution);
            maxX = Mathf.Clamp(maxX, 0, td.heightmapResolution);
            minY = Mathf.Clamp(minY, 0, td.heightmapResolution);
            maxY = Mathf.Clamp(maxY, 0, td.heightmapResolution);
            return new RectInt(minX, minY, maxX - minX, maxY - minY);
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