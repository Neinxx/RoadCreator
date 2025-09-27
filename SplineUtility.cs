using UnityEngine;
using System.Collections.Generic;

namespace RoadSystem
{
    public static class SplineUtility
    {
        /// <summary>
        /// 根据0-1的比例t，获取Catmull-Rom样条曲线上的一个点。
        /// </summary>
        public static Vector3 GetPoint(IReadOnlyList<RoadControlPoint> points, float t)
        {
            if (points.Count < 2) return points.Count > 0 ? points[0].position : Vector3.zero;

            t = Mathf.Clamp01(t);
            float totalSegments = points.Count - 1;
            float scaledT = t * totalSegments;
            int segmentIndex = Mathf.FloorToInt(scaledT);
            segmentIndex = Mathf.Clamp(segmentIndex, 0, (int)totalSegments - 1);

            float segmentT = scaledT - segmentIndex;

            Vector3 p0 = (segmentIndex == 0) ? points[segmentIndex].position : points[segmentIndex - 1].position;
            Vector3 p1 = points[segmentIndex].position;
            Vector3 p2 = points[segmentIndex + 1].position;
            Vector3 p3 = (segmentIndex + 2 > points.Count - 1) ? points[segmentIndex + 1].position : points[segmentIndex + 2].position;

            return CalculateCatmullRomPoint(segmentT, p0, p1, p2, p3);
        }

        /// <summary>
        /// 计算Catmull-Rom曲线上某一点的切线（一阶导数）。
        /// 这代表了曲线在该点的方向和“速度”。
        /// </summary>
        public static Vector3 GetVelocity(IReadOnlyList<RoadControlPoint> points, float t)
        {
            if (points.Count < 2) return Vector3.forward;

            t = Mathf.Clamp01(t);
            float totalSegments = points.Count - 1;
            float scaledT = t * totalSegments;
            int segmentIndex = Mathf.FloorToInt(scaledT);
            segmentIndex = Mathf.Clamp(segmentIndex, 0, (int)totalSegments - 1);

            float segmentT = scaledT - segmentIndex;

            Vector3 p0 = (segmentIndex == 0) ? points[segmentIndex].position : points[segmentIndex - 1].position;
            Vector3 p1 = points[segmentIndex].position;
            Vector3 p2 = points[segmentIndex + 1].position;
            Vector3 p3 = (segmentIndex + 2 > points.Count - 1) ? points[segmentIndex + 1].position : points[segmentIndex + 2].position;

            return CalculateCatmullRomVelocity(segmentT, p0, p1, p2, p3);
        }

        /// <summary>
        /// [新功能] 估算样条曲线的总长度。
        /// </summary>
        /// <param name="points">控制点列表。</param>
        /// <param name="stepsPerSegment">每个分段的估算步数，越高越精确。</param>
        /// <returns>曲线的近似世界单位长度。</returns>
        public static float EstimateSplineLength(IReadOnlyList<RoadControlPoint> points, int stepsPerSegment = 20)
        {
            if (points.Count < 2) return 0;

            float length = 0;
            Vector3 lastPoint = GetPoint(points, 0);
            int totalSteps = (points.Count - 1) * stepsPerSegment;

            for (int i = 1; i <= totalSteps; i++)
            {
                float t = (float)i / totalSteps;
                Vector3 p = GetPoint(points, t);
                length += Vector3.Distance(lastPoint, p);
                lastPoint = p;
            }
            return length;
        }

        private static Vector3 CalculateCatmullRomPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            Vector3 a = 2f * p1;
            Vector3 b = p2 - p0;
            Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
            Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;
            return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
        }

        private static Vector3 CalculateCatmullRomVelocity(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            Vector3 b = p2 - p0;
            Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
            Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;
            return 0.5f * (b + (2f * c * t) + (3f * d * t * t));
        }
    }
}