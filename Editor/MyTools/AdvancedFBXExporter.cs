using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Fbx; // 确保项目中已正确安装 Autodesk FBX SDK

public class AdvancedFBXExporter : EditorWindow
{
    // --- 配置参数 ---
    private string exportPath = "";
    private List<GameObject> selectedObjects = new List<GameObject>();

    // 网格生成参数
    private float maxWalkableSlopeAngle = 45.0f;
    private float maxStepHeight = 0.4f;

    // --- 新增：优化与简化参数 ---
    private float voxelCellSize = 0.3f; // 体素单元格大小（米）。关键参数！
    private int agentWalkableHeight = 2; // 角色可通过的高度（体素单位）
    private int agentWalkableClimb = 1; // 角色可攀爬的高度（体素单位）

    // 轮廓重构参数
    private int contourSegmentCount = 50; // 轮廓线段数量，可调节
    private bool useContourReconstruction = true; // 是否使用轮廓重构
    private bool preserveOriginalHeights = true; // 是否保留原始高度信息
    private float smoothFactor = 0.5f; // 平滑强度
    private int targetEdgeCount = 20; // 目标边数（减少边数后的结果）

    // 导出选项
    private bool exportOriginalModel = true;

    // --- 内部状态 ---
    private Vector2 scrollPosition;

    [MenuItem("Tools/Advanced FBX Exporter (Optimized NavMesh Generation)")]
    public static void ShowWindow()
    {
        GetWindow<AdvancedFBXExporter>("Optimized FBX NavMesh Generator");
    }

    #region GUI

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Optimized FBX NavMesh Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("此工具使用高性能的体素化方法生成一个低面数的、干净的可行走区域网格。", MessageType.Info);

        // --- 导出路径设置 ---
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

        // --- 网格生成参数 ---
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Generation Core Settings", EditorStyles.boldLabel);
        maxWalkableSlopeAngle = EditorGUILayout.Slider("Max Walkable Slope (°)", maxWalkableSlopeAngle, 0.0f, 90.0f);
        maxStepHeight = EditorGUILayout.FloatField("Max Step Height (meters)", maxStepHeight);
        EditorGUILayout.EndVertical();

        // --- 优化参数 ---
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Performance & Simplification Settings", EditorStyles.boldLabel);
        voxelCellSize = EditorGUILayout.FloatField(new GUIContent("Voxel Cell Size (meters)", "关键参数！值越小细节越多，但速度越慢，面数越多。推荐0.2-0.5。"), voxelCellSize);
        EditorGUILayout.HelpBox("单元格大小直接控制最终网格的精度和面数。", MessageType.Info);
        EditorGUILayout.EndVertical();

        // --- 轮廓重构参数 ---
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Contour Reconstruction Settings", EditorStyles.boldLabel);
        useContourReconstruction = EditorGUILayout.Toggle("Use Contour Reconstruction", useContourReconstruction);
        if (useContourReconstruction)
        {
            contourSegmentCount = EditorGUILayout.IntSlider("Contour Segment Count", contourSegmentCount, 10, 200);
            EditorGUILayout.HelpBox($"线段数量越多，轮廓越贴近原始形状，但计算复杂度越高。当前: {contourSegmentCount} segments", MessageType.Info);
            preserveOriginalHeights = EditorGUILayout.Toggle("Preserve Original Heights", preserveOriginalHeights);
            smoothFactor = EditorGUILayout.Slider("Smooth Factor", smoothFactor, 0.0f, 1.0f);
            targetEdgeCount = EditorGUILayout.IntSlider("Target Edge Count", targetEdgeCount, 5, 100);
        }
        EditorGUILayout.EndVertical();

        // --- 导出选项 ---
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Export Options", EditorStyles.boldLabel);
        exportOriginalModel = EditorGUILayout.Toggle("Export Combined Original Model", exportOriginalModel);
        EditorGUILayout.EndVertical();

        // --- 物体选择 ---
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Objects to Process", EditorStyles.boldLabel);
        if (GUILayout.Button("Update from Scene Selection"))
        {
            selectedObjects.Clear();
            selectedObjects.AddRange(Selection.gameObjects);
        }
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(100));
        if (selectedObjects.Count == 0) EditorGUILayout.LabelField("No objects selected.");
        else { for (int i = 0; i < selectedObjects.Count; i++) { EditorGUILayout.ObjectField($"Object {i + 1}", selectedObjects[i], typeof(GameObject), true); } }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // --- 导出按钮 ---
        GUILayout.Space(10);
        if (GUILayout.Button("Generate Optimized NavMesh and Export", GUILayout.Height(40)))
        {
            if (ValidateSettings()) { ProcessAndExport(); }
        }
    }

    private bool ValidateSettings()
    {
        if (selectedObjects.Count == 0) { EditorUtility.DisplayDialog("Error", "No objects selected for export.", "OK"); return false; }
        if (string.IsNullOrEmpty(exportPath) || !Directory.Exists(exportPath)) { EditorUtility.DisplayDialog("Error", "Invalid export path.", "OK"); return false; }
        if (maxStepHeight < 0) maxStepHeight = 0;
        if (voxelCellSize < 0.01f) voxelCellSize = 0.01f;
        
        // 更新体素高度参数
        agentWalkableClimb = Mathf.Max(1, Mathf.FloorToInt(maxStepHeight / voxelCellSize)); // 使用voxelCellSize作为高度单位
        agentWalkableHeight = Mathf.Max(agentWalkableClimb + 1, Mathf.FloorToInt(2.0f / voxelCellSize)); // 假设角色高度为2米
        
        return true;
    }

    #endregion

    #region Core Logic

    private void ProcessAndExport()
    {
        // --- 阶段 1: 合并所有源网格 ---
        EditorUtility.DisplayProgressBar("FBX Export", "Step 1/3: Combining source objects...", 0.1f);
        Mesh masterMesh = CombineAllSelectedObjects();
        if (masterMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "No valid meshes found in the selection.", "OK");
            EditorUtility.ClearProgressBar();
            return;
        }

        // --- 阶段 2: 高性能生成可行走网格 ---
        EditorUtility.DisplayProgressBar("FBX Export", "Step 2/3: Generating optimized walkable mesh...", 0.3f);
        Mesh generatedNavMesh = null;
        try
        {
            generatedNavMesh = GenerateOptimizedNavMesh(masterMesh);
        }
        catch (System.Exception e)
        {
             Debug.LogError($"Optimized NavMesh Generation Failed: {e.Message}\n{e.StackTrace}");
             EditorUtility.DisplayDialog("Generation Failed", $"An error occurred during NavMesh generation. Check the console.\n\nError: {e.Message}", "OK");
             EditorUtility.ClearProgressBar();
             return;
        }
       
        if (generatedNavMesh == null || generatedNavMesh.vertexCount == 0)
        {
            EditorUtility.DisplayDialog("Warning", "Walkable mesh generation resulted in an empty mesh. Check your parameters.", "OK");
            EditorUtility.ClearProgressBar();
            return;
        }

        // --- 阶段 3: 轮廓重构 ---
        if (useContourReconstruction)
        {
            EditorUtility.DisplayProgressBar("FBX Export", "Step 2.5/3: Contour reconstruction...", 0.7f);
            generatedNavMesh = ReconstructMeshFromContour(generatedNavMesh);
        }

        // --- 阶段 3: 导出到FBX ---
        EditorUtility.DisplayProgressBar("FBX Export", "Step 3/3: Writing FBX file...", 0.9f);
        ExportToFbx(masterMesh, generatedNavMesh);
    }

    private Mesh CombineAllSelectedObjects()
    {
        var allMeshes = new List<Mesh>();
        foreach (var obj in selectedObjects)
        {
            // 递归获取所有子物体的网格
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                var mesh = GetMeshFromObject(mf.gameObject);
                if (mesh != null) allMeshes.Add(mesh);
            }
            // 处理地形
            var terrains = obj.GetComponentsInChildren<Terrain>();
            foreach(var terrain in terrains)
            {
                 var terrainMesh = GetMeshFromObject(terrain.gameObject);
                 if(terrainMesh != null) allMeshes.Add(terrainMesh);
            }
        }
        return CombineMeshes(allMeshes, "MasterSceneMesh");
    }

    #endregion

    #region Optimized NavMesh Generation

    // 代表高度图中的一个高度区间
    private class HeightSpan { public int min, max; }

    private Mesh GenerateOptimizedNavMesh(Mesh sourceMesh)
    {
        Bounds bounds = sourceMesh.bounds;
        Vector3[] vertices = sourceMesh.vertices;
        int[] triangles = sourceMesh.triangles;

        // 1. 创建高度图 (Heightfield)
        int gridWidth = Mathf.CeilToInt(bounds.size.x / voxelCellSize);
        int gridDepth = Mathf.CeilToInt(bounds.size.z / voxelCellSize);
        var heightfield = new Dictionary<int, HeightSpan>(); // 使用1D索引优化性能

        // 2. 体素化 (Voxelize) - 将三角形光栅化到高度图中
        float cosMaxSlope = Mathf.Cos(maxWalkableSlopeAngle * Mathf.Deg2Rad);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 v1 = vertices[triangles[i + 1]];
            Vector3 v2 = vertices[triangles[i + 2]];

            // 坡度检查
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
            if (normal.y / normal.magnitude < cosMaxSlope) continue;

            // 计算三角形的2D包围盒
            float minX = Mathf.Min(v0.x, v1.x, v2.x);
            float maxX = Mathf.Max(v0.x, v1.x, v2.x);
            float minZ = Mathf.Min(v0.z, v1.z, v2.z);
            float maxZ = Mathf.Max(v0.z, v1.z, v2.z);

            int minGridX = Mathf.FloorToInt((minX - bounds.min.x) / voxelCellSize);
            int maxGridX = Mathf.CeilToInt((maxX - bounds.min.x) / voxelCellSize);
            int minGridZ = Mathf.FloorToInt((minZ - bounds.min.z) / voxelCellSize);
            int maxGridZ = Mathf.CeilToInt((maxZ - bounds.min.z) / voxelCellSize);

            // 遍历包围盒内的所有格子
            for (int z = minGridZ; z < maxGridZ; z++)
            {
                for (int x = minGridX; x < maxGridX; x++)
                {
                    // 计算格子中心点的高度
                    Vector3 cellCenter = new Vector3(bounds.min.x + x * voxelCellSize, bounds.center.y, bounds.min.z + z * voxelCellSize);
                    if (IsPointInTriangle(cellCenter, v0, v1, v2, out float height))
                    {
                        int y = Mathf.FloorToInt((height - bounds.min.y) / voxelCellSize);
                        int idx = z * gridWidth + x;
                        if (!heightfield.TryGetValue(idx, out var span))
                        {
                            span = new HeightSpan { min = y, max = y };
                            heightfield[idx] = span;
                        }
                        span.min = Mathf.Min(span.min, y);
                        span.max = Mathf.Max(span.max, y);
                    }
                }
            }
        }
        
        // 3. 过滤高度图，标记可行走区域
        int[] walkableGrid = new int[gridWidth * gridDepth];
        foreach(var pair in heightfield)
        {
            int idx = pair.Key;
            int x = idx % gridWidth;
            int z = idx / gridWidth;
            
            int neighborMaxFloor = -1;
            // 检查四个方向的邻居
            for(int dir = 0; dir < 4; dir++)
            {
                int nx = x + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                int nz = z + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                int nIdx = nz * gridWidth + nx;

                if (heightfield.TryGetValue(nIdx, out var neighborSpan))
                {
                    neighborMaxFloor = Mathf.Max(neighborMaxFloor, neighborSpan.max);
                }
            }
            
            // 如果与邻居的高度差在可攀爬范围内，则标记为可行走
            if(neighborMaxFloor != -1 && Mathf.Abs(pair.Value.max - neighborMaxFloor) < agentWalkableClimb)
            {
                walkableGrid[idx] = pair.Value.max;
            }
        }

        // 4. 连通性分析 - 找到最大的岛屿
        int[] islandGrid = new int[gridWidth * gridDepth];
        for (int i = 0; i < islandGrid.Length; i++) islandGrid[i] = -1;
        
        var islandSizes = new Dictionary<int, int>();
        int currentIslandId = 0;
        
        for (int z = 0; z < gridDepth; z++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                int idx = z * gridWidth + x;
                if (walkableGrid[idx] > 0 && islandGrid[idx] == -1)
                {
                    // 发现新的岛屿，进行广度优先搜索
                    var queue = new Queue<int>();
                    queue.Enqueue(idx);
                    islandGrid[idx] = currentIslandId;
                    int currentSize = 1;
                    
                    while (queue.Count > 0)
                    {
                        int currentIdx = queue.Dequeue();
                        int cx = currentIdx % gridWidth;
                        int cz = currentIdx / gridWidth;
                        
                        // 检查四个邻居
                        for (int dir = 0; dir < 4; dir++)
                        {
                            int nx = cx + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                            int nz = cz + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                            int nIdx = nz * gridWidth + nx;
                            
                            if (nx >= 0 && nx < gridWidth && nz >= 0 && nz < gridDepth && 
                                walkableGrid[nIdx] > 0 && islandGrid[nIdx] == -1)
                            {
                                islandGrid[nIdx] = currentIslandId;
                                queue.Enqueue(nIdx);
                                currentSize++;
                            }
                        }
                    }
                    
                    islandSizes[currentIslandId] = currentSize;
                    currentIslandId++;
                }
            }
        }
        
        // 找到最大的岛屿ID
        int largestIslandId = -1;
        int largestSize = 0;
        foreach (var pair in islandSizes)
        {
            if (pair.Value > largestSize)
            {
                largestSize = pair.Value;
                largestIslandId = pair.Key;
            }
        }
        
        // 如果没有找到岛屿，返回空网格
        if (largestIslandId == -1)
        {
            return null;
        }
        
        // 创建只包含最大岛屿的网格
        var newVertices = new List<Vector3>();
        var newTriangles = new List<int>();
        var vertexMap = new Dictionary<int, int>();

        for (int z = 0; z < gridDepth - 1; z++)
        {
            for (int x = 0; x < gridWidth - 1; x++)
            {
                int idx00 = z * gridWidth + x;
                int idx10 = z * gridWidth + x + 1;
                int idx01 = (z + 1) * gridWidth + x;
                int idx11 = (z + 1) * gridWidth + x + 1;

                // 只处理属于最大岛屿的格子
                if (islandGrid[idx00] == largestIslandId && islandGrid[idx10] == largestIslandId && 
                    islandGrid[idx01] == largestIslandId && walkableGrid[idx00] > 0 && 
                    walkableGrid[idx10] > 0 && walkableGrid[idx01] > 0)
                {
                    newTriangles.Add(GetVertex(idx00, walkableGrid[idx00], bounds.min));
                    newTriangles.Add(GetVertex(idx01, walkableGrid[idx01], bounds.min));
                    newTriangles.Add(GetVertex(idx10, walkableGrid[idx10], bounds.min));
                }
                if (islandGrid[idx11] == largestIslandId && islandGrid[idx01] == largestIslandId && 
                    islandGrid[idx10] == largestIslandId && walkableGrid[idx11] > 0 && 
                    walkableGrid[idx01] > 0 && walkableGrid[idx10] > 0)
                {
                    newTriangles.Add(GetVertex(idx10, walkableGrid[idx10], bounds.min));
                    newTriangles.Add(GetVertex(idx01, walkableGrid[idx01], bounds.min));
                    newTriangles.Add(GetVertex(idx11, walkableGrid[idx11], bounds.min));
                }
            }
        }
        
        int GetVertex(int index, int height, Vector3 origin)
        {
            if (vertexMap.TryGetValue(index, out int vertIndex)) return vertIndex;

            int x = index % gridWidth;
            int z = index / gridWidth;
            Vector3 pos = new Vector3(
                origin.x + x * voxelCellSize,
                origin.y + height * voxelCellSize,
                origin.z + z * voxelCellSize
            );
            newVertices.Add(pos);
            int newIndex = newVertices.Count - 1;
            vertexMap[index] = newIndex;
            return newIndex;
        }

        Mesh finalMesh = new Mesh {
            name = "OptimizedWalkableMesh",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = newVertices.ToArray()
        };
        finalMesh.triangles = newTriangles.ToArray();
        finalMesh.RecalculateNormals();
        finalMesh.RecalculateBounds();
        return finalMesh;
    }
    
    // 辅助函数：判断点是否在三角形内（2D），并返回高度
    bool IsPointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float height)
    {
        height = 0;
        // 使用重心坐标法
        Vector3 v0 = c - a;
        Vector3 v1 = b - a;
        Vector3 v2 = p - a;

        float dot00 = Vector3.Dot(v0, v0);
        float dot01 = Vector3.Dot(v0, v1);
        float dot02 = Vector3.Dot(v0, v2);
        float dot11 = Vector3.Dot(v1, v1);
        float dot12 = Vector3.Dot(v1, v2);

        float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        
        if ((u >= 0) && (v >= 0) && (u + v < 1))
        {
            // 点在三角形内，通过重心坐标计算高度
            height = a.y + u * (c.y - a.y) + v * (b.y - a.y);
            return true;
        }
        return false;
    }

    #endregion

    #region Contour Reconstruction

    private Mesh ReconstructMeshFromContour(Mesh originalMesh)
    {
        if (originalMesh == null || originalMesh.vertexCount == 0) return originalMesh;

        Vector3[] vertices = originalMesh.vertices;
        int[] triangles = originalMesh.triangles;

        // 提取所有顶点的XZ坐标，用于轮廓分析
        var points2D = new List<Vector2>();
        var pointHeights = new List<float>();
        var originalPoints = new List<Vector3>();
        
        foreach (var v in vertices)
        {
            points2D.Add(new Vector2(v.x, v.z));
            pointHeights.Add(v.y);
            originalPoints.Add(v);
        }

        // 获取轮廓点
        var contourPoints = ExtractContourPoints(points2D, originalPoints);
        
        if (contourPoints.Count < 3) return originalMesh;

        // 使用可调节的线段数量简化轮廓
        var simplifiedContour = SimplifyContour(contourPoints, contourSegmentCount);
        
        // 从简化轮廓重建网格
        var reconstructedMesh = ReconstructMeshFromContourPoints(simplifiedContour, originalPoints, pointHeights);
        
        return reconstructedMesh;
    }

    private List<Vector3> ExtractContourPoints(List<Vector2> points2D, List<Vector3> originalPoints)
    {
        // 使用改进的轮廓提取算法
        if (points2D.Count < 3) return new List<Vector3>();
        
        var contourCandidates = new List<Vector3>();
        var usedIndices = new HashSet<int>();
        
        // 找到最外围的点
        for (int i = 0; i < points2D.Count; i++)
        {
            bool isBoundary = IsBoundaryPoint(i, points2D);
            if (isBoundary)
            {
                contourCandidates.Add(originalPoints[i]);
                usedIndices.Add(i);
            }
        }
        
        // 如果候选点太少，回退到凸包
        if (contourCandidates.Count < 3)
        {
            var hull = ConvexHull(points2D);
            contourCandidates.Clear();
            for (int i = 0; i < hull.Count; i++)
            {
                int index = FindClosestPoint(hull[i], points2D);
                if (index >= 0) contourCandidates.Add(originalPoints[index]);
            }
        }
        
        return contourCandidates;
    }

    private bool IsBoundaryPoint(int index, List<Vector2> points2D)
    {
        if (points2D.Count < 3) return true;
        
        Vector2 point = points2D[index];
        
        // 计算该点到其他点的平均距离
        float avgDistance = 0;
        for (int i = 0; i < points2D.Count; i++)
        {
            if (i != index)
            {
                avgDistance += Vector2.Distance(point, points2D[i]);
            }
        }
        avgDistance /= points2D.Count - 1;
        
        // 如果该点到其他点的距离大于平均距离的某个倍数，则认为是边界点
        int boundaryCount = 0;
        for (int i = 0; i < points2D.Count; i++)
        {
            if (i != index && Vector2.Distance(point, points2D[i]) > avgDistance * 0.8f)
            {
                boundaryCount++;
            }
        }
        
        return boundaryCount > points2D.Count * 0.3f;
    }

    private int FindClosestPoint(Vector2 target, List<Vector2> points)
    {
        if (points.Count == 0) return -1;
        
        int closestIndex = 0;
        float minDistance = Vector2.Distance(target, points[0]);
        
        for (int i = 1; i < points.Count; i++)
        {
            float distance = Vector2.Distance(target, points[i]);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestIndex = i;
            }
        }
        
        return closestIndex;
    }

    // 凸包算法 (Graham Scan)
    private List<Vector2> ConvexHull(List<Vector2> points)
    {
        if (points.Count < 3) return new List<Vector2>(points);

        // 找到最左边的点
        Vector2 start = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].x < start.x || (points[i].x == start.x && points[i].y < start.y))
            {
                start = points[i];
            }
        }

        // 按角度排序
        var sortedPoints = new List<Vector2>(points);
        sortedPoints.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.y - start.y, a.x - start.x);
            float angleB = Mathf.Atan2(b.y - start.y, b.x - start.x);
            return angleA.CompareTo(angleB);
        });

        var hull = new List<Vector2>();
        foreach (var p in sortedPoints)
        {
            while (hull.Count > 1 && CrossProduct(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(p);
        }

        return hull;
    }

    private float CrossProduct(Vector2 O, Vector2 A, Vector2 B)
    {
        return (A.x - O.x) * (B.y - O.y) - (A.y - O.y) * (B.x - O.x);
    }

    // 简化轮廓，使用可调节的线段数量
    private List<Vector3> SimplifyContour(List<Vector3> contourPoints, int segmentCount)
    {
        if (contourPoints.Count <= segmentCount) return contourPoints;

        var simplified = new List<Vector3>();
        float totalPerimeter = 0;
        
        // 计算总周长
        for (int i = 0; i < contourPoints.Count; i++)
        {
            int next = (i + 1) % contourPoints.Count;
            totalPerimeter += Vector3.Distance(contourPoints[i], contourPoints[next]);
        }

        float segmentLength = totalPerimeter / segmentCount;
        float currentDistance = 0;
        int currentIndex = 0;
        simplified.Add(contourPoints[0]);

        for (int i = 0; i < segmentCount; i++)
        {
            float targetDistance = (i + 1) * segmentLength;
            while (currentDistance < targetDistance && currentIndex < contourPoints.Count - 1)
            {
                int nextIndex = (currentIndex + 1) % contourPoints.Count;
                float dist = Vector3.Distance(contourPoints[currentIndex], contourPoints[nextIndex]);
                if (currentDistance + dist >= targetDistance)
                {
                    // 在当前段内插值
                    float ratio = (targetDistance - currentDistance) / dist;
                    Vector3 interpolated = Vector3.Lerp(contourPoints[currentIndex], contourPoints[nextIndex], ratio);
                    simplified.Add(interpolated);
                    break;
                }
                currentDistance += dist;
                currentIndex = nextIndex;
            }
        }

        return simplified;
    }

    // 从轮廓点重建网格 - 使用Blender风格的完整优化流程
    private Mesh ReconstructMeshFromContourPoints(List<Vector3> contourPoints, List<Vector3> originalPoints, List<float> pointHeights)
    {
        if (contourPoints.Count < 3) return null;

        // 1. 顺时针排序轮廓点
        var sortedContour = SortPointsClockwise(contourPoints);
        
        // 2. 平滑顶点
        var smoothedContour = SmoothPoints(sortedContour, smoothFactor);
        
        // 3. 减少边数
        var reducedContour = ReduceEdges(smoothedContour, targetEdgeCount);
        
        // 4. 使用耳切法生成三角面
        var triangulatedMesh = EarClippingTriangulate(reducedContour);
        
        if (triangulatedMesh == null) return null;
        
        return triangulatedMesh;
    }

    // 顺时针排序点
    private List<Vector3> SortPointsClockwise(List<Vector3> points)
    {
        if (points.Count < 3) return points;
        
        // 计算质心
        Vector3 centroid = Vector3.zero;
        foreach (var p in points)
        {
            centroid += p;
        }
        centroid /= points.Count;
        
        // 按角度排序
        var sortedPoints = new List<Vector3>(points);
        sortedPoints.Sort((a, b) =>
        {
            float angleA = Mathf.Atan2(a.z - centroid.z, a.x - centroid.x);
            float angleB = Mathf.Atan2(b.z - centroid.z, b.x - centroid.x);
            return angleA.CompareTo(angleB);
        });
        
        // 检查是否为顺时针
        if (IsCounterClockwise(sortedPoints))
        {
            sortedPoints.Reverse();
        }
        
        return sortedPoints;
    }

    private bool IsCounterClockwise(List<Vector3> points)
    {
        if (points.Count < 3) return false;
        
        float sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            sum += (points[j].x - points[i].x) * (points[j].z + points[i].z);
        }
        
        return sum > 0;
    }

    // 平滑顶点
    private List<Vector3> SmoothPoints(List<Vector3> points, float smoothFactor)
    {
        if (points.Count < 3) return points;
        
        var smoothed = new List<Vector3>(points);
        
        for (int i = 0; i < points.Count; i++)
        {
            int prev = (i - 1 + points.Count) % points.Count;
            int next = (i + 1) % points.Count;
            
            Vector3 average = (points[prev] + points[next]) * 0.5f;
            smoothed[i] = Vector3.Lerp(points[i], average, smoothFactor);
        }
        
        return smoothed;
    }

    // 减少边数
    private List<Vector3> ReduceEdges(List<Vector3> points, int targetCount)
    {
        if (points.Count <= targetCount) return points;
        
        var reduced = new List<Vector3>(points);
        
        // 简单的边减少算法：移除最短边的中间点
        while (reduced.Count > targetCount)
        {
            int shortestEdgeIndex = -1;
            float shortestLength = float.MaxValue;
            
            for (int i = 0; i < reduced.Count; i++)
            {
                int next = (i + 1) % reduced.Count;
                float length = Vector3.Distance(reduced[i], reduced[next]);
                
                if (length < shortestLength)
                {
                    shortestLength = length;
                    shortestEdgeIndex = i;
                }
            }
            
            if (shortestEdgeIndex != -1)
            {
                // 移除中间点（实际上是移除下一个点）
                int removeIndex = (shortestEdgeIndex + 1) % reduced.Count;
                reduced.RemoveAt(removeIndex);
            }
            else
            {
                break;
            }
        }
        
        return reduced;
    }

    // 耳切法三角剖分
    private Mesh EarClippingTriangulate(List<Vector3> points)
    {
        if (points.Count < 3) return null;

        var vertices = new List<Vector3>(points);
        var triangles = new List<int>();

        // 创建顶点索引列表
        var indices = Enumerable.Range(0, points.Count).ToList();
        
        // 重复耳切直到只剩3个顶点
        while (indices.Count > 3)
        {
            bool foundEar = false;
            
            for (int i = 0; i < indices.Count; i++)
            {
                int prev = (i - 1 + indices.Count) % indices.Count;
                int curr = i;
                int next = (i + 1) % indices.Count;
                
                int a = indices[prev];
                int b = indices[curr];
                int c = indices[next];
                
                // 检查是否为"耳朵"
                if (IsEar(a, b, c, indices, vertices))
                {
                    // 添加三角形
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    
                    // 移除中间顶点
                    indices.RemoveAt(curr);
                    foundEar = true;
                    break;
                }
            }
            
            if (!foundEar)
            {
                // 如果找不到耳朵，尝试移除第一个顶点（避免死循环）
                indices.RemoveAt(0);
                if (indices.Count < 3) break;
            }
        }
        
        // 最后添加最后一个三角形
        if (indices.Count == 3)
        {
            triangles.Add(indices[0]);
            triangles.Add(indices[1]);
            triangles.Add(indices[2]);
        }

        // 移除重叠三角形
        RemoveOverlappingTriangles(ref vertices, ref triangles);

        Mesh resultMesh = new Mesh();
        resultMesh.vertices = vertices.ToArray();
        resultMesh.triangles = triangles.ToArray();
        resultMesh.RecalculateNormals();
        resultMesh.RecalculateBounds();
        return resultMesh;
    }

    private bool IsEar(int a, int b, int c, List<int> indices, List<Vector3> vertices)
    {
        // 检查三点是否构成顺时针或逆时针
        Vector3 va = vertices[a];
        Vector3 vb = vertices[b];
        Vector3 vc = vertices[c];
        
        // 计算叉积确定方向
        Vector3 ab = vb - va;
        Vector3 ac = vc - va;
        float cross = Vector3.Cross(ab, ac).y;
        
        // 如果是凹角，则不是耳朵
        if (cross <= 0) return false;
        
        // 检查是否有其他顶点在三角形内部
        for (int i = 0; i < indices.Count; i++)
        {
            int idx = indices[i];
            if (idx == a || idx == b || idx == c) continue;
            
            Vector3 p = vertices[idx];
            
            // 检查点是否在三角形内部
            if (IsPointInTriangle(p, va, vb, vc))
            {
                return false;
            }
        }
        
        return true;
    }

    private bool IsPointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // 使用重心坐标法
        Vector3 v0 = c - a;
        Vector3 v1 = b - a;
        Vector3 v2 = p - a;

        float dot00 = Vector3.Dot(v0, v0);
        float dot01 = Vector3.Dot(v0, v1);
        float dot02 = Vector3.Dot(v0, v2);
        float dot11 = Vector3.Dot(v1, v1);
        float dot12 = Vector3.Dot(v1, v2);

        float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        
        return (u >= 0) && (v >= 0) && (u + v < 1);
    }

    private void RemoveOverlappingTriangles(ref List<Vector3> vertices, ref List<int> triangles)
    {
        if (triangles.Count < 9) return; // 至少需要3个三角形
        
        var uniqueTriangles = new List<int>();
        var triangleSet = new HashSet<string>();
        
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            
            // 创建三角形的唯一标识符（按顶点索引排序）
            int[] sortedIndices = { a, b, c };
            System.Array.Sort(sortedIndices);
            string key = $"{sortedIndices[0]}_{sortedIndices[1]}_{sortedIndices[2]}";
            
            if (!triangleSet.Contains(key))
            {
                triangleSet.Add(key);
                uniqueTriangles.Add(a);
                uniqueTriangles.Add(b);
                uniqueTriangles.Add(c);
            }
        }
        
        triangles = uniqueTriangles;
    }

    #endregion

    #region Mesh and FBX Utilities
    // ... 此区域的辅助函数(GetMeshFromObject, CombineMeshes等)与之前版本基本相同 ...
    // ... 为简洁起见，我只包含变化的部分和必须的部分 ...

    private Mesh GetMeshFromObject(GameObject obj)
    {
        if (obj == null) return null;
        Mesh mesh = null;
        if (obj.GetComponent<MeshFilter>() != null && obj.GetComponent<MeshFilter>().sharedMesh != null)
        {
            mesh = obj.GetComponent<MeshFilter>().sharedMesh;
        }
        else if (obj.GetComponent<Terrain>() != null)
        {
            return ConvertTerrainToMesh(obj.GetComponent<Terrain>());
        }
        if (mesh == null || mesh.vertexCount == 0) return null;

        Mesh worldMesh = Instantiate(mesh);
        Vector3[] vertices = worldMesh.vertices;
        Matrix4x4 matrix = obj.transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
        }
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
                int current = y * w + x;
                int next = (y + 1) * w + x;
                triangles[index++] = current;
                triangles[index++] = next;
                triangles[index++] = current + 1;
                triangles[index++] = current + 1;
                triangles[index++] = next;
                triangles[index++] = next + 1;
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
        if (meshes.Count == 1) return meshes[0];

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
        bool exportSucceeded = false;
        try
        {
            fbxManager = FbxManager.Create();
            var ios = FbxIOSettings.Create(fbxManager, Globals.IOSROOT);
            fbxManager.SetIOSettings(ios);
            string fileName = $"OptimizedScene_{System.DateTime.Now:yyyyMMdd_HHmmss}.fbx";
            string fullPath = Path.Combine(exportPath, fileName);
            exporter = FbxExporter.Create(fbxManager, "");
            if (!exporter.Initialize(fullPath, -1, fbxManager.GetIOSettings())) throw new System.Exception("FBX Exporter initialization failed.");
            
            var fbxScene = FbxScene.Create(fbxManager, "ExportedScene");
            if (exportOriginalModel && originalMesh != null)
            {
                CreateFBXNodeFromMesh(fbxScene, originalMesh, "OriginalScene");
            }
            if(navMesh != null)
            {
                CreateFBXNodeFromMesh(fbxScene, navMesh, "GeneratedWalkableMesh");
            }
            exporter.Export(fbxScene);
            exportSucceeded = true;
            EditorUtility.DisplayDialog("Success", $"FBX file exported successfully to:\n{fullPath}", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"FBX Export Failed: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Export Failed", $"An error occurred during export. Check the console.\n\nError: {e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            exporter?.Destroy();
            fbxManager?.Destroy();
            if (exportSucceeded) AssetDatabase.Refresh();
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