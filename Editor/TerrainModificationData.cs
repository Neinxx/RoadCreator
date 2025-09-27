using UnityEngine;
using System.Collections.Generic;

namespace RoadSystem
{
    // 用于在模块间传递数据的数据容器
    public class TerrainModificationData
    {
        public Terrain Terrain { get; }
        public IReadOnlyList<RoadControlPoint> ControlPoints { get; }
        public RoadManager RoadManager { get; }

        public TerrainModificationData(Terrain terrain, RoadManager roadManager)
        {
            Terrain = terrain;
            RoadManager = roadManager;
            ControlPoints = roadManager.ControlPoints;
        }
    }
}