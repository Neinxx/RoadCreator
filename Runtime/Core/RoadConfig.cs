using UnityEngine;
using System.Collections.Generic;

namespace RoadSystem
{

    public enum UVGenerationMode
    {
        [Tooltip("自适应UV：贴图会跟随道路方向进行平铺，适合车道线等。")]
        Adaptive,
        [Tooltip("世界坐标UV：贴图基于世界空间的XZ坐标，适合大面积无缝纹理。")]
        WorldSpace
    }
    [CreateAssetMenu(fileName = "New Road Config", menuName = "Road Creator/Road Config")]
    public class RoadConfig : ScriptableObject
    {
        [Header("UV 设置")]
        [Tooltip("选择道路网格的UV生成方式。")]
        public UVGenerationMode uvGenerationMode = UVGenerationMode.Adaptive;

        [Tooltip("在“世界坐标UV”模式下，控制纹理的平铺缩放。")]
        public Vector2 worldUVScaling = new(0.1f, 0.1f);

        [Header("分层剖面 (Layered Profile)")]
        [Tooltip("定义道路的横截面。从列表顶端（索引0）开始，由内向外逐层定义。")]
        public List<RoadLayerProfile> layerProfiles = new();

        [Header("曲线与精度")]
        [Range(1, 100)]
        public int splineResolution = 20;

        [Header("风格化 (Stylization)")]
        [Range(0f, 10f)]
        public float edgeWobbleAmount = 0.5f;
        [Range(0.01f, 2f)]
        public float edgeWobbleFrequency = 0.1f;

        [Header("渲染与地形预览")]
        [Min(0f)]
        public float previewHeightOffset = 0.1f;
        public bool conformToTerrainUndulations = true;
        [Range(0, 1f)]
        public float terrainConformity = 1f;

        // [新增] 全局控制参数
        [Header("全局整体控制")]
        [Range(0.1f, 5f)]
        public float globalWidthMultiplier = 1f; // 整体宽度缩放

        [Range(0.1f, 5f)]
        public float globalWobbleFrequencyMultiplier = 1f; // 整体抖动频率缩放

        [Range(0.1f, 5f)]
        public float globalWobbleAmplitudeMultiplier = 1f; // 整体抖动幅度缩放

        // [新增] 图层布局模式开关
        [Header("图层布局模式")]
        public bool controlLayersIndependently = true; // true = 独立模式, false = 叠加模式

        // [新增] 垂直平滑度控制
        [Range(0f, 1f)]
        public float verticalSmoothness = 0.5f; // 平滑度 (0=不平滑, 1=最大平滑)
        [Range(1, 8)]
        public int smoothIterations = 3; // 平滑算法的迭代次数，次数越多越平滑


    }
}
