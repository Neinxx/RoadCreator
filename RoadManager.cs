using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoadSystem
{

    [AddComponentMenu("Road Creator/Road Manager")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadManager : MonoBehaviour
    {
        [SerializeField] private RoadConfig roadConfig;
        [SerializeField] private TerrainConfig terrainConfig;
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
                MeshFilter.sharedMesh?.Clear();
                return;
            }

            var newMesh = RoadMeshGenerator.GenerateMesh(controlPoints, roadConfig, transform);
            MeshFilter.sharedMesh = newMesh;

            // --- [核心修改] ---
            // 从RoadConfig的每个profile中提取材质，并赋值给MeshRenderer
            var materials = roadConfig.layerProfiles.Select(profile => profile.meshMaterial).ToArray();
            MeshRenderer.sharedMaterials = materials; // 注意是 sharedMaterials (复数)

            if (TryGetComponent<MeshCollider>(out var meshCollider))
            {
                meshCollider.sharedMesh = newMesh;
            }
        }

        // [新功能] 导出路径数据为JSON文件
        public void ExportPath(string path)
        {
            RoadData data = new() { controlPoints = this.controlPoints };
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json);
        }

        // [新功能] 从JSON文件导入路径数据
        public void ImportPath(string path)
        {
            string json = File.ReadAllText(path);
            RoadData data = JsonUtility.FromJson<RoadData>(json);
            this.controlPoints = data.controlPoints;
            RegenerateRoad();
        }

        private void Reset()
        {
            controlPoints = new List<RoadControlPoint>
            {
                new() { position = transform.position + new Vector3(0, 0, 0) },
                new() { position = transform.position + new Vector3(0, 0, 10) }
            };
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += RegenerateRoad;
#endif
        }
    }

    // [新功能] 用于序列化的辅助类
    [System.Serializable]
    public class RoadData
    {
        public List<RoadControlPoint> controlPoints;
    }
}