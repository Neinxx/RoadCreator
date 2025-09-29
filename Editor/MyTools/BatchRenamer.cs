using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

public class BatchRenamer : EditorWindow
{
    // 命名设置
    private string prefix = "Object_";
    private string suffix = "";
    private int startNumber = 1;
    private int incrementBy = 1;
    private bool useZeroPadding = false;
    private int zeroPaddingCount = 3;

    // 地形特定设置
    private bool useTerrainDataInNaming = false;
    private string terrainPrefix = "Terrain_";
    private bool terrainUseOriginalDataName = true;
    private string terrainCustomSuffix = "_v1";
    private bool renameTerrainDataAssets = true;

    // 排序设置
    private bool sortByPosition = true;
    private enum SortAxis { X, Y, Z }
    private SortAxis sortAxis = SortAxis.X;
    private bool ascending = true;

    // 来源设置
    private bool includeHierarchy = true;
    private bool includeProject = true;

    // 保存选中的对象
    private List<Object> selectedObjects = new List<Object>();
    private List<GameObject> terrainObjects = new List<GameObject>();

    // 缓存系统
    private Dictionary<GameObject, Terrain> terrainCache = new Dictionary<GameObject, Terrain>();
    private Dictionary<string, int> numberCache = new Dictionary<string, int>();

    // 异步操作相关
    private CancellationTokenSource cancellationTokenSource;
    private bool isRenaming = false;
    private float renameProgress = 0.0f;

    // 预览相关
    private List<string> previewNames = new List<string>();
    private Vector2 scrollPosition;

    // 预编译正则表达式
    private static readonly Regex NumberRegex = new Regex(@"\d+", RegexOptions.Compiled);

    // 打开窗口
    [MenuItem("Tools/Batch Renamer")]
    public static void ShowWindow()
    {
        GetWindow<BatchRenamer>("批量重命名工具");
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("批量重命名工具", EditorStyles.boldLabel);

        DrawSourceSection();
        DrawNamingSection();
        DrawSortingSection();
        DrawTerrainDataSection();
        DrawPreviewSection();
        DrawRenameSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawSourceSection()
    {
        GUILayout.Space(10);
        GUILayout.Label("来源选择", EditorStyles.boldLabel);
        includeHierarchy = EditorGUILayout.Toggle("包含 Hierarchy", includeHierarchy);
        includeProject = EditorGUILayout.Toggle("包含 Project", includeProject);

        if (GUILayout.Button("获取选中对象"))
        {
            GetSelectedObjects();
            GeneratePreview();
        }

        EditorGUILayout.LabelField($"选中对象总数: {selectedObjects.Count}");
        if (terrainObjects.Count > 0)
        {
            EditorGUILayout.LabelField($"地形对象数量: {terrainObjects.Count}", EditorStyles.miniBoldLabel);
        }

        // 显示选中对象列表（最多显示3个）
        if (selectedObjects.Count > 0)
        {
            EditorGUILayout.LabelField("选中对象列表：", EditorStyles.miniLabel);
            int displayCount = Mathf.Min(selectedObjects.Count, 3);
            for (int i = 0; i < displayCount; i++)
            {
                EditorGUILayout.LabelField($" - {selectedObjects[i].name}");
            }
            if (selectedObjects.Count > 3)
            {
                EditorGUILayout.LabelField($"... 还有 {selectedObjects.Count - 3} 个对象");
            }
        }
    }

    private void DrawNamingSection()
    {
        GUILayout.Space(10);
        GUILayout.Label("命名规则", EditorStyles.boldLabel);

        prefix = EditorGUILayout.TextField("前缀", prefix);
        suffix = EditorGUILayout.TextField("后缀", suffix);
        startNumber = EditorGUILayout.IntField("起始编号", startNumber);
        incrementBy = EditorGUILayout.IntField("递增步长", incrementBy);

        useZeroPadding = EditorGUILayout.Toggle("启用数字补零", useZeroPadding);
        if (useZeroPadding)
        {
            zeroPaddingCount = EditorGUILayout.IntField("补零位数", zeroPaddingCount);
            zeroPaddingCount = Mathf.Max(1, zeroPaddingCount);
        }
    }

    private void DrawSortingSection()
    {
        GUILayout.Space(10);
        GUILayout.Label("排序设置", EditorStyles.boldLabel);
        sortByPosition = EditorGUILayout.Toggle("按位置排序", sortByPosition);
        
        if (sortByPosition)
        {
            sortAxis = (SortAxis)EditorGUILayout.EnumPopup("排序轴向", sortAxis);
            ascending = EditorGUILayout.Toggle("升序排列", ascending);
        }

        EditorGUILayout.HelpBox(
            sortByPosition
                ? $"将根据世界坐标 {sortAxis} 轴位置进行排序并重命名"
                : "将按名称中的数字进行排序并重命名",
            MessageType.Info
        );
    }

    private void DrawTerrainDataSection()
    {
        GUILayout.Space(10);
        GUILayout.Label("地形数据重命名", EditorStyles.boldLabel);
        renameTerrainDataAssets = EditorGUILayout.Toggle("重命名 Terrain Data 资源", renameTerrainDataAssets);
        
        if (renameTerrainDataAssets)
        {
            EditorGUILayout.HelpBox("启用后，会根据地形对象的新名称重命名其引用的 Terrain Data 资源文件", MessageType.Info);
        }

        GUILayout.Space(10);
        GUILayout.Label("地形对象命名", EditorStyles.boldLabel);
        useTerrainDataInNaming = EditorGUILayout.Toggle("为地形使用特殊命名", useTerrainDataInNaming);
        if (useTerrainDataInNaming)
        {
            terrainPrefix = EditorGUILayout.TextField("地形前缀", terrainPrefix);
            terrainUseOriginalDataName = EditorGUILayout.Toggle("使用 TerrainData 名称", terrainUseOriginalDataName);
            if (terrainUseOriginalDataName)
            {
                terrainCustomSuffix = EditorGUILayout.TextField("自定义后缀", terrainCustomSuffix);
            }
        }
    }

    private void DrawPreviewSection()
    {
        GUILayout.Space(10);
        GUILayout.Label("命名预览", EditorStyles.boldLabel);

        if (previewNames.Count == 0)
        {
            EditorGUILayout.HelpBox("请先点击\"获取选中对象\"生成预览", MessageType.Info);
        }
        else
        {
            // 只显示前3个预览
            int displayCount = Mathf.Min(previewNames.Count, 3);
            for (int i = 0; i < displayCount; i++)
            {
                EditorGUILayout.LabelField($"{i + 1}. {previewNames[i]}");
            }
            if (previewNames.Count > 3)
            {
                EditorGUILayout.LabelField($"... 还有 {previewNames.Count - 3} 项");
            }
        }
    }

    private void DrawRenameSection()
    {
        GUILayout.Space(20);
        if (isRenaming)
        {
            if (GUILayout.Button("取消", GUILayout.Height(30)))
            {
                CancelRename();
            }
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), renameProgress, "正在重命名...");
        }
        else
        {
            GUI.enabled = selectedObjects.Count > 0;
            if (GUILayout.Button("执行重命名", GUILayout.Height(30)))
            {
                StartRenameAsync();
            }
            GUI.enabled = true;
        }
    }

    private void GetSelectedObjects()
    {
        selectedObjects.Clear();
        terrainObjects.Clear();
        terrainCache.Clear();
        numberCache.Clear();

        foreach (Object obj in Selection.objects)
        {
            if (obj == null) continue;

            bool isHierarchyObject = obj is GameObject;
            bool isProjectObject = !isHierarchyObject;

            if ((includeHierarchy && isHierarchyObject) || (includeProject && isProjectObject))
            {
                selectedObjects.Add(obj);
                GameObject go = obj as GameObject;
                if (isHierarchyObject && IsTerrainObject(go))
                {
                    terrainObjects.Add(go);
                }
            }
        }

        if (Selection.objects.Length > 0 && selectedObjects.Count == 0)
        {
            Debug.Log("当前选中的对象与来源设置不匹配，未获取到可重命名对象。");
        }
    }

    private bool IsTerrainObject(GameObject go)
    {
        if (go == null) return false;
        
        // 使用缓存避免重复调用 GetComponent
        if (!terrainCache.ContainsKey(go))
        {
            terrainCache[go] = go.GetComponent<Terrain>();
        }
        
        return terrainCache[go] != null;
    }

    private string GetTerrainDataName(GameObject terrainObject)
    {
        if (terrainObject == null) return "UnknownTerrain";
        
        // 使用缓存的 Terrain 组件
        if (!terrainCache.ContainsKey(terrainObject))
        {
            terrainCache[terrainObject] = terrainObject.GetComponent<Terrain>();
        }
        
        var terrain = terrainCache[terrainObject];
        if (terrain?.terrainData == null) return "UnknownTerrain";

        string assetPath = AssetDatabase.GetAssetPath(terrain.terrainData);
        return string.IsNullOrEmpty(assetPath) ? "UnknownTerrain" : System.IO.Path.GetFileNameWithoutExtension(assetPath);
    }

    private int ExtractFirstNumber(string text)
    {
        // 使用缓存避免重复计算
        if (numberCache.ContainsKey(text))
        {
            return numberCache[text];
        }

        var match = NumberRegex.Match(text);
        int result = match.Success ? int.Parse(match.Value) : 0;
        
        // 缓存结果
        numberCache[text] = result;
        return result;
    }

    private async void StartRenameAsync()
    {
        isRenaming = true;
        renameProgress = 0f;
        cancellationTokenSource = new CancellationTokenSource();

        try
        {
            await RenameAndReorderAsync(cancellationTokenSource.Token);

            if (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                EditorUtility.DisplayDialog(
                    "完成",
                    $"成功重命名 {selectedObjects.Count} 个对象。\n" +
                    (renameTerrainDataAssets ? "Terrain Data 资源也已重命名。\n" : "") +
                    $"对象已根据位置排序并重命名。",
                    "确定"
                );
            }
        }
        catch (System.Exception ex)
        {
            if (ex is TaskCanceledException)
            {
                Debug.Log("批量重命名操作已取消。");
            }
            else
            {
                Debug.LogError($"重命名过程中发生错误: {ex}");
                EditorUtility.DisplayDialog("错误", $"重命名失败: {ex.Message}", "确定");
            }
        }
        finally
        {
            isRenaming = false;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private void CancelRename()
    {
        cancellationTokenSource?.Cancel();
    }

    private List<Object> GetSortedObjects()
    {
        if (sortByPosition)
        {
            return selectedObjects.OrderBy(obj => GetSortValue(obj)).ToList();
        }
        else
        {
            return selectedObjects.OrderBy(obj => ExtractFirstNumber(obj.name)).ToList();
        }
    }

    private float GetSortValue(Object obj)
    {
        if (obj is GameObject go)
        {
            Vector3 position = go.transform.position;
            float value = sortAxis == SortAxis.X ? position.x : 
                         sortAxis == SortAxis.Y ? position.y : position.z;
            return ascending ? value : -value;
        }
        return 0f;
    }

    private async Task RenameAndReorderAsync(CancellationToken cancellationToken)
    {
        // 先排序
        var sortedObjects = GetSortedObjects();

        // 记录所有需要修改的对象和它们的Transform
        var undoObjects = new List<Object>();
        foreach (var obj in sortedObjects)
        {
            undoObjects.Add(obj);
            if (obj is GameObject go)
            {
                undoObjects.Add(go.transform);
            }
        }
        Undo.RecordObjects(undoObjects.ToArray(), "批量重命名");

        // 分离 Hierarchy 和 Project 资源
        var hierarchyObjects = new List<GameObject>();
        var projectObjects = new List<Object>();
        var projectAssetPaths = new Dictionary<Object, string>();

        foreach (var obj in sortedObjects)
        {
            if (obj is GameObject go)
            {
                hierarchyObjects.Add(go);
            }
            else
            {
                projectObjects.Add(obj);
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    projectAssetPaths[obj] = assetPath;
                }
            }
        }

        // 使用更快的批量重命名方法
        try
        {
            int currentNumber = startNumber;
            
            // 1. 先重命名Hierarchy对象
            for (int i = 0; i < hierarchyObjects.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var go = hierarchyObjects[i];
                if (go != null)
                {
                    string newName = GenerateNewName(go, currentNumber);
                    go.name = newName;
                }
                currentNumber += incrementBy;
            }

            // 2. 批量重命名Project资源（使用AssetDatabase，但批量处理）
            if (projectObjects.Count > 0)
            {
                await RenameProjectAssetsBatch(projectAssetPaths, projectObjects, cancellationToken);
            }

            // 3. 重命名Terrain Data资源
            if (renameTerrainDataAssets && terrainObjects.Count > 0)
            {
                await RenameTerrainDataAssetsBatch(terrainObjects, cancellationToken);
            }

        }
        catch (System.Exception ex)
        {
            Debug.LogError($"批量重命名出错: {ex.Message}");
            throw;
        }

        // 对 Hierarchy 对象进行排序
        for (int i = 0; i < hierarchyObjects.Count; i++)
        {
            if (hierarchyObjects[i] != null)
            {
                hierarchyObjects[i].transform.SetSiblingIndex(i);
            }
        }
    }

    // 批量重命名Project资源（使用AssetDatabase但优化性能）
    private async Task RenameProjectAssetsBatch(Dictionary<Object, string> assetPaths, List<Object> projectObjects, CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        
        // 使用AssetDatabase.StartAssetEditing优化批量操作
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < projectObjects.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var obj = projectObjects[i];
                if (obj == null || !assetPaths.ContainsKey(obj)) continue;

                string assetPath = assetPaths[obj];
                string newName = GenerateNewName(obj, startNumber + i * incrementBy);
                
                // 使用AssetDatabase.RenameAsset但批量处理
                AssetDatabase.RenameAsset(assetPath, newName);

                renameProgress = (float)(i + 1) / projectObjects.Count;

                if (i % batchSize == 0)
                {
                    EditorUtility.DisplayProgressBar("重命名Project资源", $"正在处理 {i + 1}/{projectObjects.Count}", renameProgress);
                    await Task.Yield();
                    Repaint();
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    // 批量重命名Terrain Data资源
    private async Task RenameTerrainDataAssetsBatch(List<GameObject> terrainObjects, CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < terrainObjects.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var terrainObject = terrainObjects[i];
                if (terrainObject == null || !IsTerrainObject(terrainObject)) continue;

                var terrain = terrainCache[terrainObject];
                if (terrain?.terrainData == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(terrain.terrainData);
                if (string.IsNullOrEmpty(assetPath)) continue;

                // 根据GameObject的新名称来命名Terrain Data资源
                string newName = $"{terrainObject.name}";
                
                // 使用AssetDatabase.RenameAsset
                AssetDatabase.RenameAsset(assetPath, newName);

                renameProgress = (float)(i + 1) / terrainObjects.Count;

                if (i % batchSize == 0)
                {
                    EditorUtility.DisplayProgressBar("重命名Terrain Data", $"正在处理 {i + 1}/{terrainObjects.Count}", renameProgress);
                    await Task.Yield();
                    Repaint();
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    private string GenerateNewName(Object obj, int number)
    {
        GameObject go = obj as GameObject;

        if (useTerrainDataInNaming && IsTerrainObject(go))
        {
            return terrainUseOriginalDataName
                ? $"{terrainPrefix}{GetTerrainDataName(go)}{terrainCustomSuffix}"
                : $"{terrainPrefix}{FormatNumber(number)}{terrainCustomSuffix}";
        }
        else
        {
            return $"{prefix}{FormatNumber(number)}{suffix}";
        }
    }

    private string FormatNumber(int number)
    {
        return useZeroPadding ? number.ToString($"D{zeroPaddingCount}") : number.ToString();
    }

    private void GeneratePreview()
    {
        previewNames.Clear();
        // 使用排序后的对象生成预览
        var sorted = GetSortedObjects();
        int currentNumber = startNumber;

        // 只生成前3个预览
        int previewCount = Mathf.Min(sorted.Count, 3);
        for (int i = 0; i < previewCount; i++)
        {
            string name = GenerateNewName(sorted[i], currentNumber);
            previewNames.Add(name);
            currentNumber += incrementBy;
        }
    }

    private void OnFocus()
    {
        GetSelectedObjects();
        GeneratePreview();
        Repaint();
    }

    private void OnDisable()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        terrainCache.Clear();
        numberCache.Clear();
    }
}