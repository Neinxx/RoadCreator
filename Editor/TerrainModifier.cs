using UnityEngine;
using UnityEditor; // 现在可以安全地直接引用
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    // 因为此文件在Editor文件夹下，所以整个类都是Editor代码
    public class TerrainModifier : ITerrainModifier
    {
        private readonly ITerrainModificationModule moduleToExecute;

        public TerrainModifier(ITerrainModificationModule module)
        {
            this.moduleToExecute = module;
        }

        public void Execute(RoadManager roadManager)
        {
            if (moduleToExecute == null)
            {
                Debug.LogError("没有提供地形修改模块！");
                return;
            }

            var controlPoints = roadManager.ControlPoints.ToList();
            if (controlPoints.Count < 2) return;

            var affectedTerrains = FindTerrainsUnderSpline(controlPoints);

            // [优化] 移除了 #if UNITY_EDITOR 指令，因为整个类都是Editor代码
            try
            {
                int terrainIndex = 0;
                foreach (var terrain in affectedTerrains)
                {
                    EditorUtility.DisplayProgressBar(
                        "地形修改",
                        $"处理地形: {terrain.name}\n模块: {moduleToExecute.ModuleName}",
                        (float)terrainIndex / affectedTerrains.Count);

                    var data = new TerrainModificationData(terrain, roadManager);
                    moduleToExecute.Execute(data);

                    // 确保每次修改后地形数据被标记为已更改
                    EditorUtility.SetDirty(terrain.terrainData);

                    terrainIndex++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // [核心修改] 现在可以安全地调用同在Editor文件夹下的TerrainUtility
        private HashSet<Terrain> FindTerrainsUnderSpline(List<RoadControlPoint> points)
        {
            var terrains = new HashSet<Terrain>();
            TerrainUtility.FindAndCacheAllTerrains(); // 调用不会再报错
            for (float t = 0; t <= 1; t += 0.01f)
            {
                var p = SplineUtility.GetPoint(points, t);
                var terrain = TerrainUtility.GetTerrainAt(p); // 调用不会再报错
                if (terrain != null) terrains.Add(terrain);
            }
            return terrains;
        }
    }
}