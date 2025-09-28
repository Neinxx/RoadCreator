// 文件路径: Assets/RoadCreator/Editor/Terrain/EditorTerrainUtility.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.Collections;

namespace RoadSystem
{
    public static class EditorTerrainUtility
    {
        #region Terrain Layer Management

        public static int EnsureAndGetLayerIndex(Terrain terrain, TerrainLayer layer)
        {
            if (layer == null) return -1;

            var terrainData = terrain.terrainData;
            var layers = terrainData.terrainLayers;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == layer)
                {
                    return i;
                }
            }

            // 如果没找到，则尝试添加
            List<TerrainLayer> newLayers = new List<TerrainLayer>(layers);
            newLayers.Add(layer);
            terrainData.terrainLayers = newLayers.ToArray();

            return newLayers.Count - 1; // 修正：移除重复的添加和赋值
        }

        #endregion

        #region Multi-Terrain Helpers

        public static List<Terrain> FindAffectedTerrains(Bounds worldBounds)
        {
            return Object.FindObjectsOfType<Terrain>()
         .Where(t => t != null)
         .Where(t => new Bounds(t.transform.position + t.terrainData.size / 2, t.terrainData.size).Intersects(worldBounds))
         .ToList();
        }

        public static Bounds CalculateSuperBounds(Bounds initialBounds, List<Terrain> terrains)
        {
            Bounds combined = initialBounds;
            foreach (var terrain in terrains)
            {
                Bounds terrainBounds = new Bounds(terrain.transform.position + terrain.terrainData.size / 2, terrain.terrainData.size);
                if (initialBounds.Intersects(terrainBounds))
                {
                    combined.Encapsulate(Vector3.Max(initialBounds.min, terrainBounds.min));
                    combined.Encapsulate(Vector3.Min(initialBounds.max, terrainBounds.max));
                }
            }
            return combined;
        }

        public static Bounds GetOverlapBounds(Bounds boundsA, Terrain terrain)
        {
            Bounds boundsB = new Bounds(terrain.transform.position + terrain.terrainData.size / 2, terrain.terrainData.size);
            Bounds overlap = new Bounds();
            overlap.SetMinMax(
                Vector3.Max(boundsA.min, boundsB.min),
                Vector3.Min(boundsA.max, boundsB.max)
            );
            return overlap;
        }

        #endregion

        #region Coordinate and Bounds Conversion

        public static RectInt WorldBoundsToHeightmapRect(Bounds bounds, Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 terrainPos = terrain.GetPosition();
            Vector3 localMin = bounds.min - terrainPos;
            Vector3 localMax = bounds.max - terrainPos;
            int minX = Mathf.FloorToInt(localMin.x / td.size.x * td.heightmapResolution);
            int maxX = Mathf.CeilToInt(localMax.x / td.size.x * td.heightmapResolution);
            int minY = Mathf.FloorToInt(localMin.z / td.size.z * td.heightmapResolution);
            int maxY = Mathf.CeilToInt(localMax.z / td.size.z * td.heightmapResolution);
            minX = Mathf.Clamp(minX, 0, td.heightmapResolution - 1);
            maxX = Mathf.Clamp(maxX, 0, td.heightmapResolution - 1);
            minY = Mathf.Clamp(minY, 0, td.heightmapResolution - 1);
            maxY = Mathf.Clamp(maxY, 0, td.heightmapResolution - 1);
            int width = Mathf.Max(0, maxX - minX);
            int height = Mathf.Max(0, maxY - minY);
            if (width <= 0 || height <= 0)
            {
                Debug.LogWarning($"WorldBoundsToHeightmapRect: 转换后的高度图矩形尺寸无效（宽：{width}，高：{height}），可能是边界未覆盖地形。");
            }
            return new RectInt(minX, minY, width, height);
        }

        public static Bounds HeightmapRectToWorldBounds(RectInt rect, Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 terrainPos = terrain.transform.position;

            float minX = (float)rect.xMin / (td.heightmapResolution - 1) * td.size.x + terrainPos.x;
            float maxX = (float)rect.xMax / (td.heightmapResolution - 1) * td.size.x + terrainPos.x;
            float minZ = (float)rect.yMin / (td.heightmapResolution - 1) * td.size.z + terrainPos.z;
            float maxZ = (float)rect.yMax / (td.heightmapResolution - 1) * td.size.z + terrainPos.z;

            return new Bounds
            {
                min = new Vector3(minX, terrainPos.y, minZ),
                max = new Vector3(maxX, terrainPos.y + td.size.y, maxZ)
            };
        }

        public static Vector3 HeightmapToWorldPos(int x, int y, Terrain terrain)
        {
            TerrainData td = terrain.terrainData;
            Vector3 terrainPos = terrain.GetPosition();
            float normX = (float)x / (td.heightmapResolution - 1);
            float normZ = (float)y / (td.heightmapResolution - 1);
            float worldX = terrainPos.x + normX * td.size.x;
            float worldZ = terrainPos.z + normZ * td.size.z;
            return new Vector3(worldX, td.GetHeight(x, y) + terrainPos.y, worldZ);
        }

        public static RectInt HeightmapRectToAlphamapRect(RectInt heightmapRect, TerrainData terrainData)
        {
            var min = HeightmapToAlphamapCoords(heightmapRect.xMin, heightmapRect.yMin, terrainData);
            var max = HeightmapToAlphamapCoords(heightmapRect.xMax, heightmapRect.yMax, terrainData);
            return new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        public static Vector2Int HeightmapToAlphamapCoords(int hmX, int hmY, TerrainData terrainData)
        {
            int alphaX = Mathf.RoundToInt((float)hmX / (terrainData.heightmapResolution - 1) * (terrainData.alphamapResolution - 1));
            int alphaY = Mathf.RoundToInt((float)hmY / (terrainData.heightmapResolution - 1) * (terrainData.alphamapResolution - 1));
            // 新增：限制索引在有效范围内
            alphaX = Mathf.Clamp(alphaX, 0, terrainData.alphamapResolution - 1);
            alphaY = Mathf.Clamp(alphaY, 0, terrainData.alphamapResolution - 1);
            return new Vector2Int(alphaX, alphaY);
        }

        #endregion

        #region Burst-compatible Math Helpers

        public static bool IsPointInTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
            float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
            if ((s < 0) != (t < 0) && s != 0 && t != 0) return false;
            float A = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
            return A < 0 ? (s <= 0 && s + t >= A) : (s >= 0 && s + t <= A);
        }


        private const float Epsilon = 1e-4f;
        public static float3 Barycentric(float2 p, float2 a, float2 b, float2 c)
        {
            float det = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (math.abs(det) < Epsilon) return new float3(-1, -1, -1);
            float wA = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / det;
            float wB = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / det;
            return new float3(wA, wB, 1.0f - wA - wB);
        }

        #endregion

        #region Array Conversion Extensions

        /// <summary>
        /// Converts a 1D NativeArray back into a 2D array for SetHeights.
        /// </summary>
        public static float[,] To2DArray(this NativeArray<float> array, int height, int width)
        {
            if (array.Length != height * width) throw new System.ArgumentException("Array length does not match dimensions.");
            var result = new float[height, width];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    result[y, x] = array[y * width + x];
                }
            }
            return result;
        }

        /// <summary>
        /// Converts a 1D NativeArray back into a 3D array for SetAlphamaps.
        /// </summary>
        public static float[,,] To3DArray(this NativeArray<float> array, int height, int width, int depth)
        {
            if (array.Length != height * width * depth) throw new System.ArgumentException("Array length does not match dimensions.");
            var result = new float[height, width, depth];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        result[y, x, z] = array[(y * width + x) * depth + z];
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Correctly "flattens" a 2D array into a 1D NativeArray.
        /// </summary>
        public static NativeArray<T> ToNativeArray<T>(this T[,] array, Allocator allocator) where T : struct
        {
            int height = array.GetLength(0);
            int width = array.GetLength(1);
            var nativeArray = new NativeArray<T>(height * width, allocator);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    nativeArray[y * width + x] = array[y, x];
                }
            }
            return nativeArray;
        }

        /// <summary>
        /// Correctly "flattens" a 3D array into a 1D NativeArray.
        /// </summary>
        public static NativeArray<T> ToNativeArray<T>(this T[,,] array, Allocator allocator) where T : struct
        {
            int height = array.GetLength(0);
            int width = array.GetLength(1);
            int depth = array.GetLength(2);
            var nativeArray = new NativeArray<T>(height * width * depth, allocator);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        nativeArray[(y * width + x) * depth + z] = array[y, x, z];
                    }
                }
            }
            return nativeArray;
        }
        #endregion
    }
}