using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace RoadSystem
{
    [AddComponentMenu("Road Creator/Road Manager")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadManager : MonoBehaviour
    {


        //Event
        public event Action OnRoadDataChanged;


        [SerializeField] private RoadConfig roadConfig;
        [SerializeField] private TerrainConfig terrainConfig;
        // [坐标系修复] controlPoints 现在存储的是本地坐标
        [SerializeField] private List<RoadControlPoint> controlPoints = new();

        public RoadConfig RoadConfig => roadConfig;
        public TerrainConfig TerrainConfig => terrainConfig;
        public IReadOnlyList<RoadControlPoint> ControlPoints => controlPoints;
        public List<RoadControlPoint> GetControlPointsList() => controlPoints;

        public MeshFilter MeshFilter { get; private set; }
        public MeshRenderer MeshRenderer { get; private set; }

        public bool IsReadyForGeneration => roadConfig != null && controlPoints.Count >= 2;
        public bool IsReadyForTerrainModification => IsReadyForGeneration && terrainConfig != null;

        private void Awake()
        {
            MeshFilter = GetComponent<MeshFilter>();
            MeshRenderer = GetComponent<MeshRenderer>();
        }

        public void RegenerateRoad()
        {
            if (MeshFilter == null) MeshFilter = GetComponent<MeshFilter>();
            if (MeshRenderer == null) MeshRenderer = GetComponent<MeshRenderer>();

            if (!IsReadyForGeneration)
            {
                if (MeshFilter.sharedMesh != null) MeshFilter.sharedMesh.Clear();
                return;
            }

            var newMesh = RoadMeshGenerator.GenerateMesh(controlPoints, roadConfig, transform);
            MeshFilter.sharedMesh = newMesh;

            if (roadConfig.layerProfiles.Any())
            {
                var materials = roadConfig.layerProfiles.Select(profile => profile.meshMaterial).ToArray();
                MeshRenderer.sharedMaterials = materials;
            }

            if (TryGetComponent<MeshCollider>(out var meshCollider))
            {
                meshCollider.sharedMesh = newMesh;
            }
        }

        public float CalculateLength()
        {
            if (controlPoints.Count < 2) return 0;

            float length = 0;
            // 使用一个合理的精度来计算长度，避免过度计算
            int steps = (controlPoints.Count - 1) * 10;
            if (steps == 0) return 0;

            Vector3 lastPoint = transform.TransformPoint(SplineUtility.GetPoint(controlPoints, 0));

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 currentPoint = transform.TransformPoint(SplineUtility.GetPoint(controlPoints, t));
                length += Vector3.Distance(currentPoint, lastPoint);
                lastPoint = currentPoint;
            }

            return length;
        }

        public void ExportPath(string path)
        {
            RoadData data = new() { controlPoints = this.controlPoints };
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json);
        }

        public void ImportPath(string path)
        {
            string json = File.ReadAllText(path);
            RoadData data = JsonUtility.FromJson<RoadData>(json);
            this.controlPoints = data.controlPoints;

            OnRoadDataChanged?.Invoke();
        }
        /// <summary>
        /// [新增] 公共的通知方法。
        /// 外部脚本（如 RoadEditorTool）将调用此方法来安全地触发 OnRoadDataChanged 事件。
        /// </summary>
        public void NotifyDataChanged()
        {
            OnRoadDataChanged?.Invoke();
        }

        private void Reset()
        {
            // [坐标系修复] Reset时，创建相对于父物体的本地坐标点
            controlPoints = new List<RoadControlPoint>
            {
                new() { position = Vector3.zero }, // (0, 0, 0) in local space
                new() { position = new Vector3(0, 0, 10) } // 10 units forward in local space
            };
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => OnRoadDataChanged?.Invoke();
#endif
        }
    }

    [System.Serializable]
    public class RoadData
    {
        public List<RoadControlPoint> controlPoints;
    }
}