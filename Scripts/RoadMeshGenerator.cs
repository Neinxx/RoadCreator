// 文件名: RoadMeshGenerator.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    public static class RoadMeshGenerator
    {
        private struct PathPoint
        {
            public Vector3 position;
            public Vector3 right;
            public Vector3 forward;
        }

        public static Mesh GenerateMesh(IReadOnlyList<RoadControlPoint> points, RoadConfig settings, Transform roadObjectTransform)
        {
            var mesh = new Mesh { name = "Road Spline Mesh" };
            if (points.Count < 2 || settings.layerProfiles.Count == 0)
            {
                mesh.Clear();
                return mesh;
            }

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subMeshTriangles = new List<List<int>>();
            for (int i = 0; i < settings.layerProfiles.Count; i++)
            {
                subMeshTriangles.Add(new List<int>());
            }

            int totalSegments = (points.Count - 1) * settings.splineResolution;
            float step = 1f / totalSegments;

            for (int i = 0; i <= totalSegments; i++)
            {
                float t = i * step;
                PathPoint currentPathPoint = CalculatePathPoint(points, t, settings);

                float currentOffsetFromCenter = 0;
                for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
                {
                    var profile = settings.layerProfiles[layerIndex];
                    float innerOffset = currentOffsetFromCenter;
                    float outerOffset = currentOffsetFromCenter + profile.width;

                    if (layerIndex == settings.layerProfiles.Count - 1)
                    {
                        float wobble = RoadNoiseUtility.GetEdgeWobble(currentPathPoint.position, settings.edgeWobbleFrequency, settings.edgeWobbleAmount);
                        outerOffset += wobble;
                        outerOffset = Mathf.Max(innerOffset + 0.1f, outerOffset);
                    }

                    Vector3 rightDir = currentPathPoint.right;
                    Vector3 innerLeftPos = currentPathPoint.position - rightDir * innerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 outerLeftPos = currentPathPoint.position - rightDir * outerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 innerRightPos = currentPathPoint.position + rightDir * innerOffset + Vector3.up * profile.verticalOffset;
                    Vector3 outerRightPos = currentPathPoint.position + rightDir * outerOffset + Vector3.up * profile.verticalOffset;

                    vertices.Add(roadObjectTransform.InverseTransformPoint(innerLeftPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(outerLeftPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(innerRightPos));
                    vertices.Add(roadObjectTransform.InverseTransformPoint(outerRightPos));

                    uvs.Add(new Vector2(innerLeftPos.x, innerLeftPos.z));
                    uvs.Add(new Vector2(outerLeftPos.x, outerLeftPos.z));
                    uvs.Add(new Vector2(innerRightPos.x, innerRightPos.z));
                    uvs.Add(new Vector2(outerRightPos.x, outerRightPos.z));

                    currentOffsetFromCenter = outerOffset;
                }

                if (i < totalSegments)
                {
                    int verticesPerStep = settings.layerProfiles.Count * 4;
                    for (int layerIndex = 0; layerIndex < settings.layerProfiles.Count; layerIndex++)
                    {
                        int root = i * verticesPerStep + layerIndex * 4;
                        int rootNext = (i + 1) * verticesPerStep + layerIndex * 4;
                        var currentLayerTriangles = subMeshTriangles[layerIndex];
                        int il = root + 0, ol = root + 1, ir = root + 2, or = root + 3;
                        int il_next = rootNext + 0, ol_next = rootNext + 1, ir_next = rootNext + 2, or_next = rootNext + 3;

                        currentLayerTriangles.Add(il); currentLayerTriangles.Add(il_next); currentLayerTriangles.Add(ol_next);
                        currentLayerTriangles.Add(il); currentLayerTriangles.Add(ol_next); currentLayerTriangles.Add(ol);
                        currentLayerTriangles.Add(ir); currentLayerTriangles.Add(ir_next); currentLayerTriangles.Add(or_next);
                        currentLayerTriangles.Add(ir); currentLayerTriangles.Add(or_next); currentLayerTriangles.Add(or);
                    }
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subMeshTriangles.Count;
            for (int i = 0; i < subMeshTriangles.Count; i++)
            {
                mesh.SetTriangles(subMeshTriangles[i], i);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static PathPoint CalculatePathPoint(IReadOnlyList<RoadControlPoint> points, float t, RoadConfig settings)
        {
            t = Mathf.Clamp01(t);
            Vector3 smoothPoint = SplineUtility.GetPoint(points, t);
            Vector3 finalPoint = smoothPoint;

            if (settings.conformToTerrainUndulations)
            {
                Terrain terrain = TerrainUtility.GetTerrainAt(smoothPoint);
                if (terrain != null)
                {
                    float terrainHeight = terrain.SampleHeight(smoothPoint) + terrain.transform.position.y;
                    finalPoint.y = Mathf.Lerp(smoothPoint.y, terrainHeight, settings.terrainConformity);
                }
            }

            finalPoint.y += settings.previewHeightOffset;

            Vector3 forward = SplineUtility.GetVelocity(points, t).normalized;
            if (forward == Vector3.zero)
            {
                if (points.Count > 1)
                    forward = (points[points.Count - 1].position - points[0].position).normalized;
                if (forward == Vector3.zero)
                    forward = Vector3.forward;
            }

            return new PathPoint
            {
                position = finalPoint,
                right = Vector3.Cross(Vector3.up, forward).normalized,
                forward = forward
            };
        }
    }
}