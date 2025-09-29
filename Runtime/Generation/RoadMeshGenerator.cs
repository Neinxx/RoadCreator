// 文件路径: Assets/RoadCreator/Runtime/Generation/RoadMeshGenerator.cs
using UnityEngine;
using System.Collections.Generic;

namespace RoadSystem
{
    /// <summary>
    /// [重构后]
    /// 一个静态的入口类，负责创建和调用 RoadMeshBuilder。
    /// 它为系统的其他部分提供了一个稳定的接口，同时将复杂的实现逻辑委托给了构建器。
    /// </summary>
    public static class RoadMeshGenerator
    {
        public static Mesh GenerateMesh(IReadOnlyList<RoadControlPoint> localControlPoints, RoadConfig settings, Transform roadObjectTransform)
        {
            // 创建一个构建器实例
            var builder = new RoadMeshBuilder(localControlPoints, settings, roadObjectTransform);
            
            // 执行构建过程并返回结果
            return builder.Build();
        }
    }
}