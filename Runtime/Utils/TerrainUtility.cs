using UnityEngine;
using System.Linq;

// 这是一个Runtime安全的工具类
public static class TerrainUtility
{
    

    

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

   
}