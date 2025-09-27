// RoadNoiseUtility.cs
using UnityEngine;

namespace RoadSystem
{
    public static class RoadNoiseUtility
    {
        /// <summary>
        /// 计算在指定世界位置的边缘抖动偏移值。
        /// </summary>
        /// <param name="worldPosition">采样的世界坐标</param>
        /// <param name="frequency">噪声频率</param>
        /// <param name="amount">噪声最大幅度</param>
        /// <returns>返回一个基于Perlin噪声的偏移值</returns>
        public static float GetEdgeWobble(Vector3 worldPosition, float frequency, float amount)
        {
            if (amount <= 0 || frequency <= 0)
            {
                return 0f;
            }

            // 使用世界坐标的x和z作为噪声输入，确保噪声在世界空间中是固定的
            float noise = Mathf.PerlinNoise(worldPosition.x * frequency, worldPosition.z * frequency);

            // PerlinNoise返回[0, 1]，我们将其映射到[-1, 1]
            float mappedNoise = (noise - 0.5f) * 2f;

            return mappedNoise * amount;
        }
    }
}