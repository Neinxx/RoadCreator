using UnityEngine;
using System.Linq;

// 这是一个Runtime安全的工具类
public static class TerrainUtility
{
    private static Terrain[] cachedTerrains;

    public static void FindAndCacheAllTerrains()
    {
        cachedTerrains = Object.FindObjectsOfType<Terrain>();
    }

    /// <param name="worldPosition">世界空间中的一个点.</param>
    /// <returns>包含该点的地形，如果没有则返回null.</returns>
    public static Terrain GetTerrainAt(Vector3 worldPosition)
    {
        // 遍历场景中所有激活的地形
        foreach (var terrain in Terrain.activeTerrains)
        {
            var terrainPos = terrain.GetPosition();
            var terrainSize = terrain.terrainData.size;

            // 检查点是否在地形的XZ边界内
            if (worldPosition.x >= terrainPos.x && worldPosition.x <= terrainPos.x + terrainSize.x &&
                worldPosition.z >= terrainPos.z && worldPosition.z <= terrainPos.z + terrainSize.z)
            {
                return terrain;
            }
        }
        return null;
    }

    // --- 几何计算辅助方法 (这些也是Runtime安全的) ---
    public static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
        float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
        if ((s < 0) != (t < 0) && s != 0 && t != 0)
            return false;
        float A = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
        return A < 0 ? (s <= 0 && s + t >= A) : (s >= 0 && s + t <= A);
    }

    public static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < 1e-5) return new Vector3(-1, -1, -1);
        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0f - v - w;
        return new Vector3(u, v, w);
    }
}