// 文件名: RoadConfig.cs
using UnityEngine;
using System.Collections.Generic;

namespace RoadSystem
{
    [CreateAssetMenu(fileName = "New Road Config", menuName = "Road Creator/Road Config")]
    public class RoadConfig : ScriptableObject
    {
        [Header("分层剖面 (Layered Profile)")]
        [Tooltip("定义道路的横截面。从列表顶端（索引0）开始，由内向外逐层定义。")]
        public List<RoadLayerProfile> layerProfiles = new List<RoadLayerProfile>();

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
    }
}