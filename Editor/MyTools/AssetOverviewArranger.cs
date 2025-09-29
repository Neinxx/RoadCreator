using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public class AssetOverviewArranger : EditorWindow
{
    // 排列设置
    private float spacing = 3.0f;
    private float prefabScale = 1.0f;
    private Vector3 startPosition = new Vector3(-20, 0, -20);
    private int maxItemsPerRow = 10;
    
    // 过滤设置
    private string searchFilter = "";
    private bool includeSubfolders = true;
    
    // 预览
    private int prefabCount = 0;
    private Vector2 scrollPos;

    [MenuItem("Tools/Asset Overview Arranger")]
    public static void ShowWindow()
    {
        GetWindow<AssetOverviewArranger>("资产总览排列器");
    }

    private void OnGUI()
    {
        GUILayout.Label("资产总览设置", EditorStyles.boldLabel);
        
        // 排列设置
        GUILayout.Label("排列设置", EditorStyles.label);
        spacing = EditorGUILayout.FloatField("间距", spacing);
        prefabScale = EditorGUILayout.FloatField("预制体缩放", prefabScale);
        maxItemsPerRow = EditorGUILayout.IntField("每行最大数量", maxItemsPerRow);
        startPosition = EditorGUILayout.Vector3Field("起始位置", startPosition);
        
        GUILayout.Space(10);
        
        // 过滤设置
        GUILayout.Label("过滤设置", EditorStyles.label);
        searchFilter = EditorGUILayout.TextField("搜索过滤", searchFilter);
        includeSubfolders = EditorGUILayout.Toggle("包含子文件夹", includeSubfolders);
        
        GUILayout.Space(10);
        
        // 信息显示
        GUILayout.Label($"当前找到 {prefabCount} 个预制体", EditorStyles.helpBox);
        
        GUILayout.Space(20);
        
        // 操作按钮
        if (GUILayout.Button("刷新预制体列表"))
        {
            RefreshPrefabList();
        }
        
        if (GUILayout.Button("在场景中排列所有预制体"))
        {
            ArrangeAllPrefabsInMatrix();
        }
        
        if (GUILayout.Button("清除场景中的排列"))
        {
            ClearArrangedObjects();
        }
        
        GUILayout.Space(10);
        
        // 预览区域
        GUILayout.Label("预制体列表预览:", EditorStyles.label);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
        
        var prefabs = GetAllPrefabs();
        foreach (var prefab in prefabs)
        {
            EditorGUILayout.LabelField(prefab.name);
        }
        
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// 刷新预制体列表并更新计数
    /// </summary>
    private void RefreshPrefabList()
    {
        var prefabs = GetAllPrefabs();
        prefabCount = prefabs.Length;
        Debug.Log($"找到 {prefabCount} 个预制体");
    }

    /// <summary>
    /// 获取项目中所有预制体
    /// </summary>
    private GameObject[] GetAllPrefabs()
    {
        // 搜索所有预制体
        string[] guids = AssetDatabase.FindAssets("t:Prefab " + searchFilter, 
            includeSubfolders ? null : new[] { "Assets" });
        
        return guids.Select(guid => 
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }).Where(prefab => prefab != null).ToArray();
    }

    /// <summary>
    /// 计算最佳矩阵排列
    /// </summary>
    private (int rows, int cols) CalculateMatrixDimensions(int itemCount)
    {
        if (itemCount == 0) return (0, 0);
        
        int cols = Mathf.Min(itemCount, maxItemsPerRow);
        int rows = Mathf.CeilToInt((float)itemCount / cols);
        
        return (rows, cols);
    }

    /// <summary>
    /// 在场景中排列所有预制体
    /// </summary>
    private void ArrangeAllPrefabsInMatrix()
    {
        // 先清除之前的排列
        ClearArrangedObjects();
        
        // 获取所有预制体
        GameObject[] prefabs = GetAllPrefabs();
        
        if (prefabs.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有找到预制体", "确定");
            return;
        }
        
        // 计算矩阵尺寸
        var (rows, cols) = CalculateMatrixDimensions(prefabs.Length);
        
        // 创建一个父对象来管理所有实例
        GameObject overviewParent = new GameObject($"Asset Overview - {prefabs.Length} items");
        overviewParent.transform.position = Vector3.zero;
        
        // 排列预制体
        for (int i = 0; i < prefabs.Length; i++)
        {
            // 计算行列位置
            int row = i / cols;
            int col = i % cols;
            
            // 实例化预制体
            GameObject instance = PrefabUtility.InstantiatePrefab(prefabs[i]) as GameObject;
            if (instance != null)
            {
                instance.transform.parent = overviewParent.transform;
                
                // 计算位置
                float xPos = startPosition.x + col * spacing;
                float zPos = startPosition.z + row * spacing;
                instance.transform.position = new Vector3(xPos, startPosition.y, zPos);
                
                // 设置缩放
                instance.transform.localScale = Vector3.one * prefabScale;
                
                // 添加标签方便后续清除
                instance.tag = "AssetOverview";
            }
        }
        
        Debug.Log($"已在场景中排列 {prefabs.Length} 个预制体，矩阵尺寸: {rows}行 x {cols}列");
    }

    /// <summary>
    /// 清除场景中排列的预制体
    /// </summary>
    private void ClearArrangedObjects()
    {
        // 查找所有带有AssetOverview标签的对象
        GameObject[] overviewObjects = GameObject.FindGameObjectsWithTag("AssetOverview");
        
        // 查找所有资产总览父对象
        GameObject[] overviewParents = GameObject.FindObjectsOfType<GameObject>()
            .Where(obj => obj.name.StartsWith("Asset Overview")).ToArray();
        
        // 删除所有相关对象
        foreach (var obj in overviewObjects.Concat(overviewParents))
        {
            DestroyImmediate(obj);
        }
        
        Debug.Log("已清除场景中的资产总览排列");
    }
}
    