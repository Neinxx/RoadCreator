
using UnityEngine;

namespace RoadSystem.Editor
{
    public static class TerrainNeighborManager
    {
        /// <summary>
        /// 遍历场景中的所有地形，并自动设置它们的邻居关系。
        /// </summary>
        public static void UpdateAllTerrainNeighbors()
        {
            Terrain[] terrains = Object.FindObjectsOfType<Terrain>();
            if (terrains.Length <= 1)
            {
                Debug.Log("场景中只有一个或没有地形，无需设置邻居。");
                return;
            }

            foreach (var terrain in terrains)
            {
                Terrain left = null, top = null, right = null, bottom = null;
                var terrainPos = terrain.transform.position;
                var terrainSize = terrain.terrainData.size;

                foreach (var other in terrains)
                {
                    if (terrain == other) continue;

                    var otherPos = other.transform.position;

                    // 使用 Mathf.Approximately 来比较浮点数，避免精度问题
                    // 检查右邻居 (Right)
                    if (Mathf.Approximately(otherPos.x, terrainPos.x + terrainSize.x) && Mathf.Approximately(otherPos.z, terrainPos.z))
                    {
                        right = other;
                    }
                    // 检查左邻居 (Left)
                    else if (Mathf.Approximately(otherPos.x, terrainPos.x - terrainSize.x) && Mathf.Approximately(otherPos.z, terrainPos.z))
                    {
                        left = other;
                    }
                    // 检查上邻居 (Top)
                    else if (Mathf.Approximately(otherPos.z, terrainPos.z + terrainSize.z) && Mathf.Approximately(otherPos.x, terrainPos.x))
                    {
                        top = other;
                    }
                    // 检查下邻居 (Bottom)
                    else if (Mathf.Approximately(otherPos.z, terrainPos.z - terrainSize.z) && Mathf.Approximately(otherPos.x, terrainPos.x))
                    {
                        bottom = other;
                    }
                }

                // 设置邻居
                terrain.SetNeighbors(left, top, right, bottom);
            }

            Debug.Log($"已为 {terrains.Length} 块地形更新邻居关系。");
        }
    }
}