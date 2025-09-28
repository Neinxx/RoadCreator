// 文件路径: Editor/TerrainModifier.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    /// <summary>
    /// [重构后]
    /// 地形修改的总入口和调度器。
    /// 它根据设置选择一个高性能的整体处理器（CPU或GPU），并执行它。
    /// 它不再负责具体的、逐个地形的修改逻辑。
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

            // 1. 获取全局设置，以决定使用哪种处理模式
            var settings = RoadCreatorSettings.GetOrCreateSettings();

            // 2. [重要] 无论使用哪种模式，都先更新地形邻居关系，以确保无缝拼接
            Editor.TerrainNeighborManager.UpdateAllTerrainNeighbors();

            try
            {
                // 3. 根据模式选择并执行相应的总处理器
                switch (settings.modificationMode)
                {
                    case ProcessingMode.CPU:
                        ExecuteCPU(roadManager);
                        break;
                    case ProcessingMode.GPU:
                        ExecuteGPU(roadManager);
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
            }
        }

        private void ExecuteCPU(RoadManager roadManager)
        {
            EditorUtility.DisplayProgressBar("地形修改", "正在初始化CPU统一处理器...", 0f);
            
            var cpuProcessor = new MultiTerrainProcessor();
            cpuProcessor.Execute(roadManager);
            
            Debug.Log("CPU模式地形修改完成。");
        }

        private void ExecuteGPU(RoadManager roadManager)
        {
            EditorUtility.DisplayProgressBar("地形修改", "正在初始化GPU处理器...", 0f);
            
            // 临时兼容旧的GPU模块
            ExecuteLegacyGPU(roadManager);
            
            Debug.Log("GPU模式地形修改完成（兼容模式）。为了获得最佳性能，建议将GPU逻辑也重构为统一处理器。");
        }

        /// <summary>
        /// 这是一个临时的兼容方法，用于执行旧的、逐个处理地形的GPU模块。
        /// 这样可以确保在重构CPU路径时，原有的GPU功能不受影响。
        /// </summary>
        private void ExecuteLegacyGPU(RoadManager roadManager)
        {
            var affectedTerrains = GetAffectedTerrains(roadManager);
            
            if (affectedTerrains.Count == 0)
            {
                Debug.Log("未找到受影响的地形。");
                return;
            }

            var gpuModule = new GPUFlattenAndTextureModule();

            for (int i = 0; i < affectedTerrains.Count; i++)
            {
                var terrain = affectedTerrains[i];
                
                EditorUtility.DisplayProgressBar(
                    "地形修改 (GPU兼容模式)",
                    $"处理地形: {terrain.name}",
                    (float)i / affectedTerrains.Count);

                var data = new TerrainModificationData(terrain, roadManager);
                gpuModule.Execute(data);

                EditorUtility.SetDirty(terrain.terrainData);
            }
        }

        private List<Terrain> GetAffectedTerrains(RoadManager roadManager)
        {
            var affectedTerrains = new List<Terrain>();
            var allTerrains = Terrain.activeTerrains;

            foreach (var terrain in allTerrains)
            {
                var terrainBounds = new Bounds(
                    terrain.transform.position + terrain.terrainData.size / 2, 
                    terrain.terrainData.size);
                
                bool isAffected = roadManager.ControlPoints.Any(point =>
                    terrainBounds.Contains(new Vector3(point.position.x, 0, point.position.z)));

                if (isAffected)
                {
                    affectedTerrains.Add(terrain);
                }
            }

            return affectedTerrains;
        }
    }
}