// GeometryUtility.cs
using UnityEngine;

namespace RoadSystem
{
    /// <summary>
    /// 包含通用的、运行时安全的几何与网格计算。
    /// </summary>
    public static class GeometryUtility
    {
        /// <summary>
        /// 获取Mesh在世界空间下的包围盒。
        /// </summary>
        public static Bounds GetWorldBounds(this Mesh mesh, Transform transform)
        {
            if (mesh == null || transform == null)
            {
                return new Bounds();
            }

            var localBounds = mesh.bounds;
            var worldCenter = transform.TransformPoint(localBounds.center);

            // 需要考虑旋转对包围盒大小的影响，最安全的方式是变换8个角点
            Vector3 p1 = transform.TransformPoint(localBounds.center + new Vector3(localBounds.extents.x, localBounds.extents.y, localBounds.extents.z));
            Vector3 p2 = transform.TransformPoint(localBounds.center + new Vector3(localBounds.extents.x, localBounds.extents.y, -localBounds.extents.z));
            Vector3 p3 = transform.TransformPoint(localBounds.center + new Vector3(localBounds.extents.x, -localBounds.extents.y, localBounds.extents.z));
            Vector3 p4 = transform.TransformPoint(localBounds.center + new Vector3(localBounds.extents.x, -localBounds.extents.y, -localBounds.extents.z));
            Vector3 p5 = transform.TransformPoint(localBounds.center + new Vector3(-localBounds.extents.x, localBounds.extents.y, localBounds.extents.z));
            Vector3 p6 = transform.TransformPoint(localBounds.center + new Vector3(-localBounds.extents.x, localBounds.extents.y, -localBounds.extents.z));
            Vector3 p7 = transform.TransformPoint(localBounds.center + new Vector3(-localBounds.extents.x, -localBounds.extents.y, localBounds.extents.z));
            Vector3 p8 = transform.TransformPoint(localBounds.center + new Vector3(-localBounds.extents.x, -localBounds.extents.y, -localBounds.extents.z));

            var newBounds = new Bounds(worldCenter, Vector3.zero);
            newBounds.Encapsulate(p1);
            newBounds.Encapsulate(p2);
            newBounds.Encapsulate(p3);
            newBounds.Encapsulate(p4);
            newBounds.Encapsulate(p5);
            newBounds.Encapsulate(p6);
            newBounds.Encapsulate(p7);
            newBounds.Encapsulate(p8);

            return newBounds;
        }
    }
}