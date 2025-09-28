// 文件名: RoadLayerProfile.cs
using UnityEngine;
using UnityEditor;

namespace RoadSystem
{
    [System.Serializable]
    public class RoadLayerProfile
    {
        [Tooltip("该图层的名称，便于在编辑器中识别")]
        public string layerName = "New Layer";

        [Tooltip("该图层在道路单侧的宽度（米）")]
        [Min(0)]
        public float width = 1.5f;

        [Tooltip("渲染该图层网格所用的材质")]
        public Material meshMaterial;

        [Tooltip("该图层相对于道路中心线的高度偏移，可以制作路缘石或沟渠")]
        public float verticalOffset = 0f;

        [Header("地形交互")]
        [Tooltip("将该图层绘制到地形上时使用的Terrain Layer资产")]
        public TerrainLayer terrainLayer;

        [Tooltip("地形纹理的混合强度")]
        [Range(0, 1)]
        public float textureBlendFactor = 1.0f;

        // --- [新功能] 每层的独立风格化参数 ---
        [Header("边缘风格化 (Stylization)")]
        [Tooltip("边界抖动的频率。值越小，边缘的起伏越大块、越舒缓；值越大，起伏越密集、越破碎。")]
        [Range(0.01f, 2f)]
        public float boundaryWobbleFrequency = 0.5f;

        [Tooltip("边界抖动的最大幅度（米）。0表示边缘是完美的直线。")]
        [Range(0f, 5f)]
        public float boundaryWobbleAmplitude = 0.2f;

        [Header("其他")]
        // [新增] 用于控制UI折叠状态的字段
        public bool isExpanded = true;
        // --- [新增] 仅在“独立模式”下使用的字段 ---
        public float offsetFromCenter = 0f; // 相对于道路中心线的偏移
    }
}