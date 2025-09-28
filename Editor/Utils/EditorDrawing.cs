// 文件路径: Assets/RoadCreator/Editor/Utils/EditorDrawing.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace RoadSystem.Editor
{
    /// <summary>
    /// 提供在Unity编辑器Scene视图中绘制辅助图形的功能。
    /// 这个类被设为 internal，意味着它只能在Editor程序集内部被访问。
    /// </summary>
    internal static class EditorDrawing
    {
        // 保持原有方法不变，只是类名和命名空间可能需要根据新结构调整
        public static void DrawSpline(IReadOnlyList<RoadControlPoint> points, Color color, float thickness)
        {
            Handles.color = color;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 p1 = points[i].position;
                Vector3 p2 = points[i + 1].position;
                Handles.DrawLine(p1, p2, thickness);
            }
        }

        public static void DrawSplineBezier(IReadOnlyList<RoadControlPoint> points, Color color, float thickness, int resolution = 20)
        {
            Handles.color = color;
            for (float t = 0; t <= 1; t += 1f / ((points.Count - 1) * resolution))
            {
                Vector3 point = SplineUtility.GetPoint(points, t);
                Vector3 nextPoint = SplineUtility.GetPoint(points, t + (1f / resolution));
                Handles.DrawLine(point, nextPoint, thickness);

            }
        }
    }
}