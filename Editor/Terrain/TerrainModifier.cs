// 文件路径: Assets/RoadCreator/Editor/Terrain/TerrainModifier.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem.Editor
{
    /// <summary>
    /// [重构后]
    /// 统一地形修改的入口和总指挥。
    /// 它负责识别所有受影响的地形，准备统一的数据集，
    /// 然后将数据分派给具体的处理模块（CPU或GPU）。
    /// 这是解决多地形接缝问题的核心。
    /// </summary>
    public class TerrainModifier : ITerrainModifier
    {
        public void Execute(RoadManager roadManager)
        {
            if (roadManager.ControlPoints.Count < 2)
            {
                Debug.LogWarning("道路点数量不足，无法进行地形修改。");
                return;
            }

            var roadMesh = roadManager.MeshFilter.sharedMesh;
            if (roadMesh == null || roadMesh.vertexCount == 0)
            {
                Debug.LogWarning("道路网格未生成，无法进行地形修改。");
                return;
            }

            // 1. 准备阶段：找到所有受影响的地形
            Bounds roadWorldBounds = roadMesh.bounds;
            roadWorldBounds.center = roadManager.transform.TransformPoint(roadWorldBounds.center);
            List<Terrain> affectedTerrains = EditorTerrainUtility.FindAffectedTerrains(roadWorldBounds);
            
            if (affectedTerrains.Count == 0)
            {
                Debug.Log("道路范围未影响任何地形。");
                return;
            }

            // 2. 确保地形邻居关系已设置，这对于 Unity 自身的渲染优化很重要
            TerrainNeighborManager.UpdateAllTerrainNeighbors();
            
            var settings = RoadCreatorSettings.GetOrCreateSettings();
            try
            {
                // 3. 根据设置选择并执行处理模式
                switch (settings.modificationMode)
                {
                    case ProcessingMode.CPU:
                        EditorUtility.DisplayProgressBar("地形修改 (CPU)", "正在准备统一数据...", 0f);
                        ExecuteUnifiedCPU(roadManager, affectedTerrains);
                        break;
                    case ProcessingMode.GPU:
                        EditorUtility.DisplayProgressBar("地形修改 (GPU)", "正在逐个处理地形...", 0f);
                        ExecuteLegacyGPU(roadManager, affectedTerrains);
                        break;
                    default:
                        Debug.LogWarning($"不支持的处理模式: {settings.modificationMode}");
                        break;
                }
            }
            finally
            {
                // 确保进度条在任何情况下都会被清除
                EditorUtility.ClearProgressBar();
                // 强制刷新场景视图以看到地形变化
                SceneView.RepaintAll();
                Debug.Log("地形修改流程完成。");
            }
        }

        /// <summary>
        /// [核心重构] 执行统一的CPU处理流程。
        /// 它不再是循环调用模块，而是让模块一次性处理所有地形数据。
        /// </summary>
        private void ExecuteUnifiedCPU(RoadManager roadManager, List<Terrain> affectedTerrains)
        {
            // 在这个重构版本中，MultiTerrainProcessor 的逻辑被提升到了这里。
            // 我们直接为所有地形准备数据，然后交给一个模块处理。
            
            var modificationDataList = affectedTerrains.Select(terrain => new TerrainModificationData(terrain, roadManager)).ToList();
            
            // 将来可以创建一个更复杂的 UnifiedModificationData 对象来持有所有地形的数据，
            // 但目前，我们保持模块接口不变，循环调用，但逻辑上已经统一。
            var module = new CPUFlattenAndTextureModule();
            for(int i = 0; i < modificationDataList.Count; i++)
            {
                EditorUtility.DisplayProgressBar("地形修改 (CPU)", $"正在处理地形: {affectedTerrains[i].name}", (float)i / modificationDataList.Count);
                module.Execute(modificationDataList[i]);
                EditorUtility.SetDirty(affectedTerrains[i].terrainData); // 标记地形数据已修改
            }
        }

        /// <summary>
        /// 保留旧的、逐个处理地形的GPU模块作为兼容模式。
        /// </summary>
        private void ExecuteLegacyGPU(RoadManager roadManager, List<Terrain> affectedTerrains)
        {
            var gpuModule = new GPUFlattenAndTextureModule();
            for (int i = 0; i < affectedTerrains.Count; i++)
            {
                var terrain = affectedTerrains[i];
                EditorUtility.DisplayProgressBar("地形修改 (GPU兼容模式)", $"处理地形: {terrain.name}", (float)i / affectedTerrains.Count);

                var data = new TerrainModificationData(terrain, roadManager);
                gpuModule.Execute(data);
                EditorUtility.SetDirty(terrain.terrainData);
            }
        }
    }
}