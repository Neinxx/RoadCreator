using UnityEngine;
using System;

namespace RoadSystem
{
    /// <summary>
    /// A helper class with extension methods for converting texture data arrays.
    /// </summary>
    public static class TextureDataHelper
    {
        /// <summary>
        /// Converts a 1D Color32 array to a 3D float array for terrain alphamaps.
        /// </summary>
        public static float[,,] To3DFloatArray(this Color32[] colors, int width, int height, int layers)
        {
            var map = new float[height, width, layers];
            if (layers == 0 || colors.Length == 0) return map;

            int actualWidth = (int)Mathf.Sqrt(colors.Length);
            int copyWidth = Mathf.Min(width, actualWidth);
            int copyHeight = Mathf.Min(height, actualWidth);

            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    Color32 c = colors[y * actualWidth + x];
                    if (layers > 0) map[y, x, 0] = c.r / 255f;
                    if (layers > 1) map[y, x, 1] = c.g / 255f;
                    if (layers > 2) map[y, x, 2] = c.b / 255f;
                    if (layers > 3) map[y, x, 3] = c.a / 255f;
                }
            }
            return map;
        }

        /// <summary>
        /// [FIX] Converts a 1D float array to a 2D float array for terrain heightmaps using an element-by-element copy.
        /// </summary>
        public static float[,] To2DFloatArray(this float[] data, int width, int height)
        {
            // Create the correctly sized target array that the terrain system expects.
            var map = new float[height, width];

            if (data.Length == 0) return map;

            // Determine the dimensions of the source data
            int dataSideLength = (int)Mathf.Sqrt(data.Length);
            int copyWidth = Mathf.Min(width, dataSideLength);
            int copyHeight = Mathf.Min(height, dataSideLength);

            // [核心修复] 使用嵌套循环逐个元素进行复制
            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                {
                    // Calculate the index in the 1D source array
                    int sourceIndex = y * dataSideLength + x;

                    // Assign the value to the correct [y, x] position in the 2D destination array
                    map[y, x] = data[sourceIndex];
                }
            }

            return map;
        }
    }
}