// 文件: RoadNoiseUtility.cs
using UnityEngine;

public static class RoadNoiseUtility
{
    // 保留旧的方法以兼容
    public static float GetEdgeWobble(Vector3 position, float frequency, float amplitude)
    {
        // 为了获得稳定的2D噪声，我们忽略Y轴
        float noise = (Mathf.PerlinNoise(position.x * frequency, position.z * frequency) - 0.5f) * 2f;
        return noise * amplitude;
    }

    /// <summary>
    /// [新增] 计算干笔刷效果的边缘噪音
    /// 通过叠加多层不同频率和振幅的柏林噪音来创建更丰富、更不规则的细节
    /// </summary>
    /// <param name="position">世界坐标位置</param>
    /// <param name="baseFrequency">基础频率，控制大块形状</param>
    /// <param name="baseAmplitude">基础幅度，控制整体抖动强度</param>
    /// <param name="octaves">叠加层数，层数越多细节越丰富，3-4层效果就很好</param>
    /// <param name="lacunarity">频率变化率(>1)，每一层的频率都是前一层的lacunarity倍</param>
    /// <param name="persistence">幅度变化率(<1)，每一层的幅度都是前一层的persistence倍</param>
    /// <returns></returns>
    public static float GetDryBrushWobble(Vector3 position, float baseFrequency, float baseAmplitude, int octaves = 4, float lacunarity = 2f, float persistence = 0.5f)
    {
        float totalWobble = 0;
        float frequency = baseFrequency;
        float amplitude = baseAmplitude;
        float maxAmplitude = 0;

        for (int i = 0; i < octaves; i++)
        {
            totalWobble += (Mathf.PerlinNoise(position.x * frequency, position.z * frequency) - 0.5f) * amplitude;

            maxAmplitude += amplitude;

            frequency *= lacunarity;
            amplitude *= persistence;
        }

        // 归一化到-1到1范围，再乘以基础幅度
        return (totalWobble / maxAmplitude) * 2f * baseAmplitude;
    }
}