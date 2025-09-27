// EditorTerrainUtility.cs
using UnityEngine;
using UnityEditor;
using System.Linq;
using RoadSystem; // 确保引用了我们的命名空间

/// <summary>
/// 包含处理地形数据的、仅限编辑器使用的工具方法。
/// 核心职责：确保Terrain对象上关联了所有需要的TerrainLayer。
/// </summary>
public static class EditorTerrainUtility
{
    /// <summary>
    /// 确保指定的TerrainLayer存在于地形的图层列表中。如果不存在，则自动添加。
    /// </summary>
    /// <param name="terrain">目标地形</param>
    /// <param name="layerToAdd">需要确保存在的TerrainLayer资产</param>
    /// <returns>该TerrainLayer在地形图层列表中的索引。如果添加失败则返回-1。</returns>
    public static int EnsureAndGetLayerIndex(Terrain terrain, TerrainLayer layerToAdd)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("目标地形或地形数据为空！");
            return -1;
        }

        if (layerToAdd == null)
        {
            // 如果用户没有在profile中指定TerrainLayer，这是一个可接受的情况，直接返回-1
            // Debug.LogWarning("尝试向地形添加一个空的TerrainLayer。");
            return -1;
        }

        var currentLayers = terrain.terrainData.terrainLayers.ToList();

        // 检查是否已经存在
        for (int i = 0; i < currentLayers.Count; i++)
        {
            if (currentLayers[i] == layerToAdd)
            {
                return i; // 已存在，直接返回索引
            }
        }

        // 如果不存在，则添加到列表中
        Undo.RecordObject(terrain.terrainData, $"Add TerrainLayer: {layerToAdd.name}");

        currentLayers.Add(layerToAdd);
        terrain.terrainData.terrainLayers = currentLayers.ToArray();

        EditorUtility.SetDirty(terrain.terrainData);
        Debug.Log($"已成功将TerrainLayer '{layerToAdd.name}' 添加到地形 '{terrain.name}'。");

        return currentLayers.Count - 1; // 返回新添加的层的索引
    }
}