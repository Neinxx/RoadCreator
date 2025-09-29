using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Fbx; // 确保项目中已正确安装 Autodesk FBX SDK
using UnityEngine.AI;

public class AdvancedFBXExporter : EditorWindow
{
    // --- 公共配置参数 (以便 Visualizer 文件能访问) ---
    public string exportPath = "Assets/";
    public List<GameObject> selectedObjects = new List<GameObject>();

    public float agentRadius = 0.5f;
    public float agentHeight = 2.0f;
    public float maxWalkableSlopeAngle = 45.0f;
    public float maxStepHeight = 0.4f;

    public float minRegionArea = 2.0f;
    public bool useHeightLimit = false;
    public float maxHeight = 10.0f;

    public bool showVisualizers = true;
    public bool exportOriginalModel = true;
    private Vector3 calculatedGroundPosition;

    // --- 内部状态 ---
    private Vector2 scrollPosition;
    public Bounds calculatedBounds;

    [MenuItem("Tools/Advanced FBX Exporter (Unity NavMesh)")]
    public static void ShowWindow()
    {
        GetWindow<AdvancedFBXExporter>("FBX NavMesh Generator");
    }

    #region Scene Visualization Hooks

    private void OnEnable()
    {

        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {

        SceneView.duringSceneGui -= OnSceneGUI;
    }


    private void OnSceneGUI(SceneView sceneView)
    {
        // 【修改】只在这里处理输入和修改数据
        EditorGUI.BeginChangeCheck();
        float newHeight = ExporterVisualizer.DrawHeightHandle(this);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(this, "Change Max Height");
            maxHeight = newHeight;
            RecalculateBoundsAndGround(); // 高度变化也需要重算Bounds
            Repaint(); // 重绘窗口
        }

        // 【修改】将预计算的地面位置传给绘制函数
        ExporterVisualizer.Draw(this, calculatedGroundPosition);
    }

    #endregion

    #region GUI

    private void OnGUI()
    {
        // 【优化关键】开始监视UI控件的变化
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("FBX NavMesh's mesh Generator ", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("生成寻路网格的网格，并可将其与原始模型一同导出为FBX,方便编辑。", MessageType.Info);

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Export Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        exportPath = EditorGUILayout.TextField("Export Path", exportPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string path = EditorUtility.SaveFolderPanel("Select Export Folder", exportPath, Application.dataPath);
            if (!string.IsNullOrEmpty(path)) { exportPath = path; }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("NavMesh Generation Settings", EditorStyles.boldLabel);
        agentRadius = EditorGUILayout.FloatField(new GUIContent("Agent Radius (meters)"), agentRadius);
        agentHeight = EditorGUILayout.FloatField(new GUIContent("Agent Height (meters)"), agentHeight);
        maxWalkableSlopeAngle = EditorGUILayout.Slider("Max Walkable Slope (°)", maxWalkableSlopeAngle, 0.0f, 90.0f);
        maxStepHeight = EditorGUILayout.FloatField(new GUIContent("Max Step Height (meters)"), maxStepHeight);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Filtering & Cleanup Settings", EditorStyles.boldLabel);
        minRegionArea = EditorGUILayout.FloatField(new GUIContent("Min Region Area"), minRegionArea);
        useHeightLimit = EditorGUILayout.BeginToggleGroup(new GUIContent("Use Max Height Limit"), useHeightLimit);
        maxHeight = EditorGUILayout.FloatField("Max Height (Y value)", maxHeight);
        EditorGUILayout.EndToggleGroup();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Visualization Settings", EditorStyles.boldLabel);
        showVisualizers = EditorGUILayout.Toggle(new GUIContent("Show In-Scene Visualizers"), showVisualizers);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Export Options", EditorStyles.boldLabel);
        exportOriginalModel = EditorGUILayout.Toggle("Export Combined Original Model", exportOriginalModel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Objects to Process", EditorStyles.boldLabel);
        if (GUILayout.Button("Update from Scene Selection"))
        {
            selectedObjects.Clear();
            selectedObjects.AddRange(Selection.gameObjects.Where(go => go.activeInHierarchy));
            RecalculateBoundsAndGround(); // 更新选择时，需要立即更新一次包围盒
        }
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(100));
        if (selectedObjects.Count == 0) EditorGUILayout.LabelField("No active objects selected.");
        else { for (int i = 0; i < selectedObjects.Count; i++) { EditorGUILayout.ObjectField($"Object {i + 1}", selectedObjects[i], typeof(GameObject), true); } }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);
        if (GUILayout.Button("Generate NavMesh and Export", GUILayout.Height(40)))
        {
            if (ValidateSettings()) { ProcessAndExport(); }
        }


        if (EditorGUI.EndChangeCheck())
        {
            // 重新计算包围盒，因为可能有影响它的参数（如高度限制）发生了变化
            RecalculateBoundsAndGround();

            // 命令所有场景视图重绘，从而更新指示器
            SceneView.RepaintAll();
        }
    }

    private bool ValidateSettings()
    {
        if (selectedObjects.Count == 0) { EditorUtility.DisplayDialog("Error", "No objects selected for export.", "OK"); return false; }
        if (string.IsNullOrEmpty(exportPath) || !Directory.Exists(exportPath)) { EditorUtility.DisplayDialog("Error", "Invalid export path.", "OK"); return false; }
        return true;
    }

    #endregion

    #region Core Logic

    private void ProcessAndExport()
    {
        EditorUtility.DisplayProgressBar("FBX Export", "Step 1/3: Gathering source objects...", 0.1f);
        Mesh masterMesh = null;
        if (exportOriginalModel)
        {
            masterMesh = CombineAllSelectedObjects();
        }

        EditorUtility.DisplayProgressBar("FBX Export", "Step 2/3: Generating NavMesh...", 0.3f);
        Mesh generatedNavMesh = null;
        try { generatedNavMesh = GenerateNavMeshWithUnityAPI(); }
        catch (System.Exception e)
        {
            Debug.LogError($"NavMesh Generation Failed: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Generation Failed", $"An error occurred. Check the console.\n\nError: {e.Message}", "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }


        if (generatedNavMesh == null || generatedNavMesh.vertexCount == 0)
        {
            EditorUtility.DisplayDialog("Warning", "NavMesh generation resulted in an empty mesh. Try adjusting parameters.", "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("FBX Export", "Step 3/3: Writing FBX file...", 0.9f);
        ExportToFbx(masterMesh, generatedNavMesh);
    }

    private Mesh GenerateNavMeshWithUnityAPI()
    {
        var sources = new List<NavMeshBuildSource>();
        foreach (var go in selectedObjects)
        {
            if (!go.activeInHierarchy) continue;
            var meshFilters = go.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = mf.sharedMesh, transform = mf.transform.localToWorldMatrix, area = 0 });
            }
            var terrains = go.GetComponentsInChildren<Terrain>();
            foreach (var t in terrains)
            {
                if (!t.gameObject.activeInHierarchy) continue;
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Terrain, sourceObject = t.terrainData, transform = Matrix4x4.TRS(t.transform.position, Quaternion.identity, Vector3.one), area = 0 });
            }
        }
        if (sources.Count == 0) return null;

        var settings = new NavMeshBuildSettings
        {
            agentRadius = agentRadius,
            agentHeight = agentHeight,
            agentSlope = maxWalkableSlopeAngle,
            agentClimb = maxStepHeight,
            minRegionArea = this.minRegionArea,
            overrideVoxelSize = false,
        };

        RecalculateBoundsAndGround();
        Bounds buildBounds = calculatedBounds;
        buildBounds.Expand(0.1f);

        NavMeshData navMeshData = NavMeshBuilder.BuildNavMeshData(settings, sources, buildBounds, Vector3.zero, Quaternion.identity);
        if (navMeshData == null) return null;

        var navMeshDataInstance = NavMesh.AddNavMeshData(navMeshData);
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        navMeshDataInstance.Remove();
        Object.DestroyImmediate(navMeshData);

        if (triangulation.vertices.Length == 0) return null;

        var navMesh = new Mesh { name = "GeneratedNavMesh", vertices = triangulation.vertices, triangles = triangulation.indices };
        navMesh.RecalculateNormals();
        navMesh.RecalculateBounds();
        return navMesh;
    }

    public void RecalculateBoundsAndGround()
    {
        if (selectedObjects.Count == 0) { calculatedBounds = new Bounds(Vector3.zero, Vector3.zero); return; }

        Bounds bounds = new Bounds();
        bool boundsInitialized = false;

        foreach (var obj in selectedObjects)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!boundsInitialized) { bounds = renderer.bounds; boundsInitialized = true; }
                else { bounds.Encapsulate(renderer.bounds); }
            }
        }

        if (useHeightLimit)
        {
            float newMaxY = Mathf.Min(bounds.max.y, maxHeight);
            float newMinY = Mathf.Min(bounds.min.y, newMaxY);
            bounds.min = new Vector3(bounds.min.x, newMinY, bounds.min.z);
            bounds.max = new Vector3(bounds.max.x, newMaxY, bounds.max.z);
        }
        calculatedBounds = bounds;
        Ray ray = new Ray(calculatedBounds.center, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, calculatedBounds.size.y))
        {
            calculatedGroundPosition = hit.point;
        }
        else
        {
            calculatedGroundPosition = new Vector3(calculatedBounds.center.x, calculatedBounds.min.y, calculatedBounds.center.z);
        }
    }

    #endregion

    #region Mesh and FBX Utilities 

    private Mesh CombineAllSelectedObjects()
    {
        var allMeshes = new List<Mesh>();
        foreach (var obj in selectedObjects)
        {
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                var mesh = GetMeshInWorldSpace(mf);
                if (mesh != null) allMeshes.Add(mesh);
            }
            var terrains = obj.GetComponentsInChildren<Terrain>();
            foreach (var terrain in terrains)
            {
                var terrainMesh = ConvertTerrainToMesh(terrain);
                if (terrainMesh != null) allMeshes.Add(terrainMesh);
            }
        }
        return CombineMeshes(allMeshes, "CombinedOriginalScene");
    }

    private Mesh GetMeshInWorldSpace(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null) return null;
        Mesh mesh = mf.sharedMesh;
        if (mesh.vertexCount == 0) return null;
        Mesh worldMesh = Instantiate(mesh);
        Vector3[] vertices = worldMesh.vertices;
        Matrix4x4 matrix = mf.transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++) { vertices[i] = matrix.MultiplyPoint3x4(vertices[i]); }
        worldMesh.vertices = vertices;
        worldMesh.RecalculateNormals();
        worldMesh.RecalculateBounds();
        return worldMesh;
    }

    private Mesh ConvertTerrainToMesh(Terrain terrain)
    {
        TerrainData td = terrain.terrainData;
        int w = td.heightmapResolution;
        int h = td.heightmapResolution;
        Vector3 size = td.size;
        Vector3 pos = terrain.transform.position;
        float[,] heights = td.GetHeights(0, 0, w, h);
        var vertices = new Vector3[w * h];
        var triangles = new int[(w - 1) * (h - 1) * 6];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                vertices[y * w + x] = pos + new Vector3(x * size.x / (w - 1), heights[y, x] * size.y, y * size.z / (h - 1));
            }
        }
        int index = 0;
        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                int current = y * w + x; int next = (y + 1) * w + x;
                triangles[index++] = current; triangles[index++] = next; triangles[index++] = current + 1;
                triangles[index++] = current + 1; triangles[index++] = next; triangles[index++] = next + 1;
            }
        }
        Mesh mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh CombineMeshes(List<Mesh> meshes, string name)
    {
        if (meshes == null || meshes.Count == 0) return null;
        meshes.RemoveAll(m => m == null || m.vertexCount == 0);
        if (meshes.Count == 0) return null;
        var combineInstances = meshes.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray();
        var combinedMesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        combinedMesh.CombineMeshes(combineInstances, true, false);
        combinedMesh.RecalculateBounds();
        return combinedMesh;
    }

    private void ExportToFbx(Mesh originalMesh, Mesh navMesh)
    {
        FbxManager fbxManager = null;
        FbxExporter exporter = null;
        try
        {
            fbxManager = FbxManager.Create();
            fbxManager.SetIOSettings(FbxIOSettings.Create(fbxManager, Globals.IOSROOT));
            string fileName = $"SceneExport_{System.DateTime.Now:yyyyMMdd_HHmmss}.fbx";
            string fullPath = Path.Combine(exportPath, fileName);
            exporter = FbxExporter.Create(fbxManager, "");
            if (!exporter.Initialize(fullPath, -1, fbxManager.GetIOSettings())) throw new System.Exception("FBX Exporter initialization failed.");

            var fbxScene = FbxScene.Create(fbxManager, "ExportedScene");
            if (exportOriginalModel && originalMesh != null) { CreateFBXNodeFromMesh(fbxScene, originalMesh, "OriginalScene"); }
            if (navMesh != null) { CreateFBXNodeFromMesh(fbxScene, navMesh, "GeneratedNavMesh"); }

            exporter.Export(fbxScene);
            EditorUtility.DisplayDialog("Success", $"FBX file exported successfully to:\n{fullPath}", "OK");
            AssetDatabase.Refresh();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"FBX Export Failed: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Export Failed", $"An error occurred. Check console.\n\nError: {e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            exporter?.Destroy();
            fbxManager?.Destroy();
        }
    }

    private void CreateFBXNodeFromMesh(FbxScene scene, Mesh mesh, string nodeName)
    {
        var fbxNode = FbxNode.Create(scene, nodeName);
        var fbxMesh = FbxMesh.Create(scene, nodeName + "_Mesh");
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        fbxMesh.InitControlPoints(vertices.Length);
        for (int i = 0; i < vertices.Length; i++) { fbxMesh.SetControlPointAt(new FbxVector4(vertices[i].x, vertices[i].y, vertices[i].z), i); }


        var normalLayer = FbxLayerElementNormal.Create(fbxMesh, "");

        normalLayer.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
        normalLayer.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
        for (int i = 0; i < normals.Length; i++) { normalLayer.GetDirectArray().Add(new FbxVector4(normals[i].x, normals[i].y, normals[i].z)); }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            fbxMesh.BeginPolygon();
            fbxMesh.AddPolygon(triangles[i]);
            fbxMesh.AddPolygon(triangles[i + 1]);
            fbxMesh.AddPolygon(triangles[i + 2]);
            fbxMesh.EndPolygon();
        }
        fbxNode.SetNodeAttribute(fbxMesh);
        scene.GetRootNode().AddChild(fbxNode);
    }

    #endregion
}