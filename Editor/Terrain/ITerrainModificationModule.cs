// 文件路径: Assets/RoadCreator/Editor/Terrain/ITerrainModificationModule.cs
using UnityEngine; // 需要引用 UnityEngine 来识别 Texture2D

namespace RoadSystem.Editor
{
    /// <summary>
    /// [最终版接口]
    /// 为所有地形修改模块（无论是CPU还是GPU）定义了统一的接口。
    /// 所有模块现在都遵循“高级烘焙”流程，并且需要处理 roadDataMap。
    /// </summary>
    public interface ITerrainModificationModule
    {
        string ModuleName { get; }

        /// <summary>
        /// 执行地形修改。
        /// </summary>
        /// <param name="data">基础的地形和道路数据</param>
        /// <param name="bakerResult">包含纹理图集和UV映射的关键烘焙数据</param>
        /// <param name="roadLayerIndex">道路图层在地形中的索引</param>
        /// <param name="roadDataMap">用于写入“智能数据”的独立纹理</param>
        void Execute(TerrainModificationData data, RoadDataBaker.BakerResult bakerResult, int roadLayerIndex, Texture2D roadDataMap);
    }
}