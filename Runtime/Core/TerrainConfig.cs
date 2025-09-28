// TerrainConfig.cs
using UnityEngine;

namespace RoadSystem
{
    [CreateAssetMenu(fileName = "New Terrain Config", menuName = "Road Creator/Terrain Config")]
    public class TerrainConfig : ScriptableObject
    {
        [Tooltip("压平区域边缘的羽化宽度（米）。值越大，道路边缘与地形的过渡越平滑。")]
        [Min(0)]
        public float flattenFeatherWidth = 2f;

        // --- [保留和调整的代码] ---
        [Header("地形压平 (Flattening)")]
        [Tooltip("压平地形时，最终地形表面相比道路网格向下的偏移量。")]
        public float flattenOffset = 0.1f;
        [Tooltip("控制地形压平的强度。")]
        [Range(0, 1f)]
        public float flattenStrength = 1f;

        [Header("风格化 (Stylization)")]
        [Tooltip("应用于所有图层纹理边缘混合的噪点缩放尺寸。")]
        [Range(0.01f, 1f)]
        public float textureNoiseScale = 0.1f;
    }
}