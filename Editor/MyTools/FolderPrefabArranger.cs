using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// 预制体排列工具 - 可以将项目中的预制体或场景中的对象按照指定方式排列
/// </summary>
public class FolderPrefabArranger : EditorWindow
{
    // --- Constants ---
    private const string PARENT_OBJECT_PREFIX = "Prefab Arrangement - ";
    private const string ARRANGE_TAG = "GeneratedPrefabContainer";
    private const string PREFS_KEY_PREFIX = "PrefabArranger_";
    
    // --- 配置数据类 ---
    [System.Serializable]
    private class ConfigData
    {
        public ArrangementType arrangementType;
        public bool useAutoSpacing;
        public float spacing;
        public float prefabScale;
        public int maxItemsPerRow;
        public Vector3 startPosition;
        public float circleRadius;
        public float spiralGrowth;
        public float randomRange;
        public bool randomizeRotation;
        public bool alignToGround;
        public float groundOffset;
    }

    // --- Editor State ---
    private enum DataSource { ProjectFolders, SceneSelection }
    private DataSource currentDataSource = DataSource.ProjectFolders;

    // --- Arrangement Settings ---
    private enum ArrangementType { Grid, Circle, Spiral, Random }
    private ArrangementType arrangementType = ArrangementType.Grid;
    private bool useAutoSpacing = true;
    private float spacing = 3.0f; 
    private float prefabScale = 1.0f;
    private Vector3 startPosition = new Vector3(-20, 0, -20);
    private int maxItemsPerRow = 10;
    private float circleRadius = 10.0f;
    private float spiralGrowth = 0.5f;
    private float randomRange = 20.0f;
    private bool randomizeRotation = false;
    private bool alignToGround = false;
    private float groundOffset = 0.0f;

    // --- Filtering Settings (Project Mode) ---
    private string searchFilter = "";
    private bool includeSubfolders = true;
    private bool showOnlyMeshObjects = false;
    
    // --- State Information ---
    private List<GameObject> arrangedParents = new List<GameObject>();
    private const float DEFAULT_SPACING = 3.0f;
    private const float DEFAULT_SCALE = 1.0f;
    private const int DEFAULT_MAX_ITEMS_PER_ROW = 10;

    // --- State Information ---
    private List<string> selectedFolderPaths = new List<string>();
    private List<GameObject> objectsToArrange = new List<GameObject>();
    private string statusMessage = "选择模式并加载资源开始。";
    private Vector2 scrollPos;
    private Vector2 folderScrollPos; // Scroll position for the folder list
    private bool showHelp = true;
    private bool showAdvancedSettings = false;
    private string savedConfigName = "默认配置";
    private List<string> savedConfigs = new List<string>();

    // --- UI Styling ---
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle buttonStyle;
    private GUIStyle actionButtonStyle; // 添加缺失的样式变量
    private Color defaultColor;

    [MenuItem("Tools/预制体排列工具")]
    public static void ShowWindow()
    {
        GetWindow<FolderPrefabArranger>("预制体排列工具");
    }

    private void OnEnable()
    {
        EnsureTagExists();
        // Subscribe to selection change events to automatically update the folder list
        Selection.selectionChanged += OnProjectOrSceneSelectionChanged;
        // Initial check when window is opened
        UpdateFoldersFromSelection();
        LoadSavedConfigNames();
        InitializeStyles();
    }

    private void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        Selection.selectionChanged -= OnProjectOrSceneSelectionChanged;
    }
    
    private bool stylesInitialized = false;
    
    private void InitializeStyles()
    {
        // 将样式初始化移到OnGUI中，这里只做标记
        stylesInitialized = false;
    }
    
    private void InitializeStylesInOnGUI()
    {
        if (stylesInitialized) return;
        
        defaultColor = GUI.color;

        headerStyle = new GUIStyle(EditorStyles.boldLabel);
        headerStyle.fontSize = 14;
        headerStyle.alignment = TextAnchor.MiddleLeft;
        headerStyle.margin = new RectOffset(4, 4, 6, 6);

        subHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
        subHeaderStyle.fontSize = 12;
        
        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.padding = new RectOffset(10, 10, 6, 6);
        buttonStyle.margin = new RectOffset(4, 4, 4, 4);
        
        // 初始化操作按钮样式
        actionButtonStyle = new GUIStyle(GUI.skin.button);
        actionButtonStyle.padding = new RectOffset(10, 10, 6, 6);
        actionButtonStyle.margin = new RectOffset(4, 4, 4, 4);
        actionButtonStyle.fontStyle = FontStyle.Bold;
        
        stylesInitialized = true;
    }

    /// <summary>
    /// Main GUI loop for the editor window.
    /// </summary>
    private void OnGUI()
    {
        // 在OnGUI中初始化样式
        InitializeStylesInOnGUI();
        
        DrawHeader();
        EditorGUILayout.Space();

        DataSource previousDataSource = currentDataSource;
        currentDataSource = (DataSource)GUILayout.Toolbar((int)currentDataSource, new string[] { "从项目中排列预制体", "从场景中排列对象" });
        if (previousDataSource != currentDataSource)
        {
            // Clear lists when switching modes
            objectsToArrange.Clear();
            UpdateStatusMessage();
        }
        EditorGUILayout.Space();

        // Help toggle
        showHelp = EditorGUILayout.ToggleLeft("显示帮助信息", showHelp);
        if (showHelp)
        {
            EditorGUILayout.HelpBox(
                "使用说明:\n" +
                "1. 选择数据源（项目预制体或场景对象）\n" +
                "2. 根据选择的模式，选择文件夹或场景对象\n" +
                "3. 调整排列设置\n" +
                "4. 点击排列按钮将对象排列到场景中\n" +
                "5. 可以保存常用配置以便下次使用", 
                MessageType.Info);
        }

        if (currentDataSource == DataSource.ProjectFolders)
        {
            DrawProjectModeUI();
        }
        else 
        {
            DrawSceneModeUI();
        }

        DrawArrangementSettingsUI();
        DrawConfigurationUI();
        DrawActionButtonsUI();
        DrawPreviewAreaUI();

        EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
    }
    
    #region UI Drawing Methods

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("预制体排列工具", headerStyle, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawProjectModeUI()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("1. 文件夹选择", subHeaderStyle);
        
        if (showHelp)
        {
            EditorGUILayout.HelpBox("在Project窗口中用 Ctrl 或 Shift 多选文件夹，或点击下方按钮选择。", MessageType.Info);
        }

        if (GUILayout.Button("选择文件夹", buttonStyle))
        {
            string path = EditorUtility.OpenFolderPanel("选择预制体文件夹", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    // 确保路径包含项目路径
                    if (path.StartsWith(Application.dataPath))
                    {
                        // Convert absolute path to project relative path
                        string relativePath = "Assets" + path.Substring(Application.dataPath.Length);
                        if (!selectedFolderPaths.Contains(relativePath))
                        {
                            selectedFolderPaths.Add(relativePath);
                            RefreshPrefabList();
                        }
                    }
                    else
                    {
                        // 选择的文件夹不在项目内
                        EditorUtility.DisplayDialog("路径错误", "请选择项目内的文件夹", "确定");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("处理文件夹路径时出错: " + e.Message);
                    EditorUtility.DisplayDialog("错误", "处理文件夹路径时出错: " + e.Message, "确定");
                }
            }
        }

        // Display the list of currently selected folders
        folderScrollPos = EditorGUILayout.BeginScrollView(folderScrollPos, GUILayout.Height(80));
        if (selectedFolderPaths.Count == 0)
        {
            EditorGUILayout.LabelField("未选择文件夹。");
        }
        else
        {
            for (int i = 0; i < selectedFolderPaths.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                // Use a read-only field to show the path
                EditorGUILayout.SelectableLabel(selectedFolderPaths[i], EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                
                // Add a remove button
                if (GUILayout.Button("移除", GUILayout.Width(60)))
                {
                    selectedFolderPaths.RemoveAt(i);
                    RefreshPrefabList();
                    break; // Break to avoid collection modified exception
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("2. 过滤设置", subHeaderStyle);
        
        // Add a check to refresh the list automatically when filter changes
        EditorGUI.BeginChangeCheck();
        searchFilter = EditorGUILayout.TextField("名称过滤", searchFilter);
        includeSubfolders = EditorGUILayout.Toggle("包含子文件夹", includeSubfolders);
        showOnlyMeshObjects = EditorGUILayout.Toggle("仅显示有网格的对象", showOnlyMeshObjects);
        
        if (EditorGUI.EndChangeCheck())
        {
            RefreshPrefabList();
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawSceneModeUI()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("1. 场景对象选择", subHeaderStyle);
        
        if (showHelp)
        {
            EditorGUILayout.HelpBox("在场景中选择要排列的对象，然后点击下方按钮加载。", MessageType.Info);
        }

        GUI.color = new Color(0.7f, 1f, 0.7f);
        if (GUILayout.Button($"加载 {Selection.gameObjects.Length} 个选中的场景对象", buttonStyle))
        {
            LoadSceneSelection();
        }
        GUI.color = defaultColor;
        EditorGUILayout.EndVertical();
    }
    
    private void DrawArrangementSettingsUI()
    {
        string stepNumber = (currentDataSource == DataSource.ProjectFolders) ? "3" : "2";
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label($"{stepNumber}. 排列设置", subHeaderStyle);

        // Arrangement type selection
        EditorGUI.BeginChangeCheck();
        arrangementType = (ArrangementType)EditorGUILayout.EnumPopup("排列方式", arrangementType);

        // Basic settings
        EditorGUI.BeginChangeCheck();
        
        // Different settings based on arrangement type
        switch (arrangementType)
        {
            case ArrangementType.Grid:
                useAutoSpacing = EditorGUILayout.Toggle("自动间距", useAutoSpacing);
                spacing = EditorGUILayout.FloatField(useAutoSpacing ? "边距" : "固定间距", spacing);
                maxItemsPerRow = EditorGUILayout.IntField("每行最大数量", maxItemsPerRow);
                break;
                
            case ArrangementType.Circle:
                circleRadius = EditorGUILayout.FloatField("圆形半径", circleRadius);
                break;
                
            case ArrangementType.Spiral:
                circleRadius = EditorGUILayout.FloatField("起始半径", circleRadius);
                spiralGrowth = EditorGUILayout.FloatField("螺旋增长率", spiralGrowth);
                break;
                
            case ArrangementType.Random:
                randomRange = EditorGUILayout.FloatField("随机范围", randomRange);
                randomizeRotation = EditorGUILayout.Toggle("随机旋转", randomizeRotation);
                break;
        }
        
        prefabScale = EditorGUILayout.FloatField("对象缩放", prefabScale);
        startPosition = EditorGUILayout.Vector3Field("起始位置", startPosition);
        
        // Advanced settings toggle
        showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "高级设置");
        if (showAdvancedSettings)
        {
            EditorGUI.indentLevel++;
            alignToGround = EditorGUILayout.Toggle("对齐到地面", alignToGround);
            if (alignToGround)
            {
                groundOffset = EditorGUILayout.FloatField("地面偏移", groundOffset);
            }
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
    }

    private void DrawConfigurationUI()
    {
        string stepNumber = (currentDataSource == DataSource.ProjectFolders) ? "4" : "3";
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label($"{stepNumber}. 配置管理", subHeaderStyle);

        EditorGUILayout.BeginHorizontal();
        savedConfigName = EditorGUILayout.TextField("配置名称", savedConfigName);
        
        if (GUILayout.Button("保存配置", GUILayout.Width(80)))
        {
            SaveCurrentConfig();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (savedConfigs.Count > 0)
        {
            int selectedIndex = EditorGUILayout.Popup("加载配置", 0, savedConfigs.ToArray());
            if (selectedIndex > 0)
            {
                LoadConfig(savedConfigs[selectedIndex]);
            }
            
            if (GUILayout.Button("删除", GUILayout.Width(60)))
            {
                if (selectedIndex > 0)
                {
                    DeleteConfig(savedConfigs[selectedIndex]);
                }
            }
        }
        else
        {
            EditorGUILayout.LabelField("没有保存的配置");
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }

    private void DrawActionButtonsUI()
    {
        EditorGUILayout.BeginHorizontal();
        
        // Arrange button
        GUI.enabled = objectsToArrange.Count > 0;
        GUI.color = new Color(0.5f, 0.8f, 1f); // 更鲜明的蓝色
         if (GUILayout.Button($"▶ 排列 {objectsToArrange.Count} 个对象到场景 ▶", actionButtonStyle, GUILayout.Height(30)))
        {
            EditorUtility.DisplayProgressBar("预制体排列工具", "正在排列对象...", 0.5f);
            try
            {
                ArrangeObjects();
                EditorUtility.ClearProgressBar();
                // 播放成功音效
                EditorApplication.Beep();
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"排列对象时出错: {e.Message}");
                EditorUtility.DisplayDialog("错误", $"排列对象时出错: {e.Message}", "确定");
            }
        }
        GUI.color = defaultColor;
        GUI.enabled = true;
        
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        
        GUI.color = new Color(1f, 0.7f, 0.7f); // 红色调
         if (GUILayout.Button("✖ 清除所有已排列的预制体 ✖", actionButtonStyle, GUILayout.Height(25)))
        {
            GameObject[] existingArrangements = GameObject.FindGameObjectsWithTag(ARRANGE_TAG);
            if (existingArrangements.Length > 0)
            {
                if (EditorUtility.DisplayDialog("确认清除", "确定要清除所有由此工具创建的排列吗？（此操作可撤销）", "是，清除", "取消"))
                {
                    EditorUtility.DisplayProgressBar("预制体排列工具", "正在清除排列...", 0.5f);
                    try
                    {
                        ClearArrangedObjects();
                        EditorUtility.ClearProgressBar();
                    }
                    catch (System.Exception e)
                    {
                        EditorUtility.ClearProgressBar();
                        Debug.LogError($"清除排列时出错: {e.Message}");
                        EditorUtility.DisplayDialog("错误", $"清除排列时出错: {e.Message}", "确定");
                    }
                }
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "场景中没有找到由此工具创建的排列", "确定");
            }
        }
        GUI.color = defaultColor;
    }
    
    private void DrawPreviewAreaUI()
    {
        EditorGUILayout.BeginVertical("box");
        
        EditorGUILayout.LabelField($"对象列表 ({objectsToArrange.Count} 个已加载)", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MinHeight(100), GUILayout.ExpandHeight(true));
        
        if (objectsToArrange.Count == 0)
        {
            EditorGUILayout.LabelField("使用上方按钮加载对象或选择文件夹。");
        }
        else
        {
            for (int i = 0; i < objectsToArrange.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(objectsToArrange[i], typeof(GameObject), (currentDataSource == DataSource.SceneSelection));
                
                if (GUILayout.Button("移除", GUILayout.Width(60)))
                {
                    objectsToArrange.RemoveAt(i);
                    UpdateStatusMessage();
                    break; // Break to avoid collection modified exception
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    #endregion

    #region Core Logic & Event Handling

    /// <summary>
    /// Called whenever the user's selection changes in Unity.
    /// </summary>
    private void OnProjectOrSceneSelectionChanged()
    {
        // Only update folder list if we are in the correct mode
        if (currentDataSource == DataSource.ProjectFolders)
        {
            UpdateFoldersFromSelection();
        }
        // Always repaint the window to keep UI responsive (e.g., for the Scene mode button)
        Repaint();
    }

    /// <summary>
    /// 更新文件夹列表，基于Project窗口中的当前选择
    /// </summary>
    private void UpdateFoldersFromSelection()
    {
        List<string> currentFolders = new List<string>();
        foreach(Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets))
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                currentFolders.Add(path);
            }
        }

        // 检查选择是否实际改变，以避免不必要的刷新
        if (!selectedFolderPaths.SequenceEqual(currentFolders))
        {
            // 保存旧的文件夹数量用于显示反馈
            int oldCount = selectedFolderPaths.Count;
            int oldPrefabCount = objectsToArrange.Count;
            
            selectedFolderPaths = currentFolders;
            RefreshPrefabList();
            
            // 显示选择反馈
            if (currentFolders.Count > 0)
            {
                string folderNames = string.Join(", ", currentFolders.Select(Path.GetFileName));
                if (folderNames.Length > 50) folderNames = folderNames.Substring(0, 47) + "...";
                
                statusMessage = $"已选择 {currentFolders.Count} 个文件夹: {folderNames}";
                
                // 如果是新增文件夹，播放提示音
                if (currentFolders.Count > oldCount)
                {
                    EditorApplication.Beep();
                }
                
                // 如果加载了新的预制体，显示数量变化
                if (objectsToArrange.Count > oldPrefabCount)
                {
                    statusMessage += $"\n加载了 {objectsToArrange.Count - oldPrefabCount} 个新预制体";
                }
            }
            
            Repaint();
        }
    }
    
    // --- The rest of the core logic (arranging, clearing, etc.) is unchanged ---
    
    private void ArrangeObjects()
    {
        if (objectsToArrange == null || objectsToArrange.Count == 0)
        {
            statusMessage = "没有可排列的对象";
            return;
        }

        // 过滤掉空引用
        objectsToArrange = objectsToArrange.Where(obj => obj != null).ToList();
        if (objectsToArrange.Count == 0)
        {
            statusMessage = "所有对象引用无效，请重新加载";
            return;
        }

        ClearArrangedObjects();

        string arrangementName = currentDataSource == DataSource.ProjectFolders ? 
            Path.GetFileName(selectedFolderPaths.Count > 0 ? selectedFolderPaths.FirstOrDefault() ?? "Multi-Folder" : "Arrangement") : 
            "Scene Selection";
            
        GameObject parent = new GameObject($"{PARENT_OBJECT_PREFIX}{arrangementName} ({objectsToArrange.Count} 个对象)");
        parent.tag = ARRANGE_TAG;
        Undo.RegisterCreatedObjectUndo(parent, "Arrange Objects");
        
        // 记录已创建的父对象，用于后续清理
        if (arrangedParents == null) arrangedParents = new List<GameObject>();
        arrangedParents.Add(parent);

        try
        {
            switch (arrangementType)
            {
                case ArrangementType.Grid:
                    if (useAutoSpacing) ArrangeWithAutoSpacing(parent.transform);
                    else ArrangeWithGridSpacing(parent.transform);
                    break;
                case ArrangementType.Circle:
                    ArrangeInCircle(parent.transform);
                    break;
                case ArrangementType.Spiral:
                    ArrangeInSpiral(parent.transform);
                    break;
                case ArrangementType.Random:
                    ArrangeRandomly(parent.transform);
                    break;
                default:
                    ArrangeWithGridSpacing(parent.transform); // 默认使用网格排列
                    break;
            }
            
            statusMessage = $"成功排列 {objectsToArrange.Count} 个对象";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"排列对象时出错: {e.Message}\n{e.StackTrace}");
            statusMessage = $"排列失败: {e.Message}";
            
            // 如果出错，清理创建的父对象
            if (parent != null)
            {
                Undo.DestroyObjectImmediate(parent);
            }
        }
    }
    
    private void ArrangeInCircle(Transform parent)
    {
        int count = objectsToArrange.Count;
        float angleStep = 360f / count;
        
        for (int i = 0; i < count; i++)
        {
            GameObject obj = objectsToArrange[i];
            if (obj == null) continue;
            
            // Calculate position on circle
            float angle = i * angleStep * Mathf.Deg2Rad;
            float x = Mathf.Sin(angle) * circleRadius;
            float z = Mathf.Cos(angle) * circleRadius;
            
            // Position object
            Vector3 position = startPosition + new Vector3(x, 0, z);
            ProcessObject(obj, parent, position, prefabScale);
        }
    }
    
    private void ArrangeInSpiral(Transform parent)
    {
        int count = objectsToArrange.Count;
        float angleStep = 30f; // Degrees between each object
        float currentRadius = circleRadius;
        
        for (int i = 0; i < count; i++)
        {
            GameObject obj = objectsToArrange[i];
            if (obj == null) continue;
            
            // Calculate position on spiral
            float angle = i * angleStep * Mathf.Deg2Rad;
            float x = Mathf.Sin(angle) * currentRadius;
            float z = Mathf.Cos(angle) * currentRadius;
            
            // Position object
            Vector3 position = startPosition + new Vector3(x, 0, z);
            ProcessObject(obj, parent, position, prefabScale);
            
            // Increase radius for next object
            currentRadius += spiralGrowth;
        }
    }
    
    private void ArrangeRandomly(Transform parent)
    {
        System.Random random = new System.Random();
        
        foreach (GameObject obj in objectsToArrange)
        {
            if (obj == null) continue;
            
            // Calculate random position within range
            float x = startPosition.x + (float)(random.NextDouble() * 2 - 1) * randomRange;
            float z = startPosition.z + (float)(random.NextDouble() * 2 - 1) * randomRange;
            
            // Position object
            Vector3 position = new Vector3(x, startPosition.y, z);
            
            // Apply random rotation if enabled
            Quaternion rotation = Quaternion.identity;
            if (randomizeRotation)
            {
                float rotY = (float)random.NextDouble() * 360f;
                rotation = Quaternion.Euler(0, rotY, 0);
            }
            
            ProcessObject(obj, parent, position, prefabScale);
        }
    }
    
    private void SaveCurrentConfig()
    {
        if (string.IsNullOrEmpty(savedConfigName))
        {
            EditorUtility.DisplayDialog("错误", "请输入配置名称", "确定");
            return;
        }
        
        // Save to EditorPrefs
        string key = PREFS_KEY_PREFIX + savedConfigName;
        EditorPrefs.SetInt(key + "_arrangementType", (int)arrangementType);
        EditorPrefs.SetBool(key + "_useAutoSpacing", useAutoSpacing);
        EditorPrefs.SetFloat(key + "_spacing", spacing);
        EditorPrefs.SetFloat(key + "_prefabScale", prefabScale);
        EditorPrefs.SetInt(key + "_maxItemsPerRow", maxItemsPerRow);
        EditorPrefs.SetFloat(key + "_startPositionX", startPosition.x);
        EditorPrefs.SetFloat(key + "_startPositionY", startPosition.y);
        EditorPrefs.SetFloat(key + "_startPositionZ", startPosition.z);
        EditorPrefs.SetFloat(key + "_circleRadius", circleRadius);
        EditorPrefs.SetFloat(key + "_spiralGrowth", spiralGrowth);
        EditorPrefs.SetFloat(key + "_randomRange", randomRange);
        EditorPrefs.SetBool(key + "_randomizeRotation", randomizeRotation);
        EditorPrefs.SetBool(key + "_alignToGround", alignToGround);
        EditorPrefs.SetFloat(key + "_groundOffset", groundOffset);
        
        // Update saved configs list
        LoadSavedConfigNames();
        
        EditorUtility.DisplayDialog("成功", "配置已保存", "确定");
    }
    
    private void LoadConfig(string configName)
    {
        string key = PREFS_KEY_PREFIX + configName;
        
        // Check if config exists
        if (!EditorPrefs.HasKey(key + "_arrangementType"))
            return;
            
        // Load settings
        arrangementType = (ArrangementType)EditorPrefs.GetInt(key + "_arrangementType");
        useAutoSpacing = EditorPrefs.GetBool(key + "_useAutoSpacing");
        spacing = EditorPrefs.GetFloat(key + "_spacing");
        prefabScale = EditorPrefs.GetFloat(key + "_prefabScale");
        maxItemsPerRow = EditorPrefs.GetInt(key + "_maxItemsPerRow");
        
        startPosition = new Vector3(
            EditorPrefs.GetFloat(key + "_startPositionX"),
            EditorPrefs.GetFloat(key + "_startPositionY"),
            EditorPrefs.GetFloat(key + "_startPositionZ")
        );
        
        circleRadius = EditorPrefs.GetFloat(key + "_circleRadius");
        spiralGrowth = EditorPrefs.GetFloat(key + "_spiralGrowth");
        randomRange = EditorPrefs.GetFloat(key + "_randomRange");
        randomizeRotation = EditorPrefs.GetBool(key + "_randomizeRotation");
        alignToGround = EditorPrefs.GetBool(key + "_alignToGround");
        groundOffset = EditorPrefs.GetFloat(key + "_groundOffset");
        
        // Update UI
        Repaint();
    }
    
    private void DeleteConfig(string configName)
    {
        if (EditorUtility.DisplayDialog("确认删除", $"确定要删除配置 '{configName}' 吗？", "是", "否"))
        {
            string key = PREFS_KEY_PREFIX + configName;
            
            // Delete all keys for this config
            string[] suffixes = new string[] {
                "_arrangementType", "_useAutoSpacing", "_spacing", "_prefabScale", 
                "_maxItemsPerRow", "_startPositionX", "_startPositionY", "_startPositionZ",
                "_circleRadius", "_spiralGrowth", "_randomRange", "_randomizeRotation",
                "_alignToGround", "_groundOffset"
            };
            
            foreach (string suffix in suffixes)
            {
                EditorPrefs.DeleteKey(key + suffix);
            }
            
            // Update saved configs list
            LoadSavedConfigNames();
        }
    }
    
    private void LoadSavedConfigNames()
    {
        savedConfigs.Clear();
        
        // Get all keys in EditorPrefs that match our prefix
        string[] allKeys = GetEditorPrefsKeys();
        HashSet<string> configNames = new HashSet<string>();
        
        foreach (string key in allKeys)
        {
            if (key.StartsWith(PREFS_KEY_PREFIX) && key.EndsWith("_arrangementType"))
            {
                string configName = key.Substring(PREFS_KEY_PREFIX.Length, 
                                                key.Length - PREFS_KEY_PREFIX.Length - "_arrangementType".Length);
                configNames.Add(configName);
            }
        }
        
        savedConfigs.AddRange(configNames);
    }
    
    private string[] GetEditorPrefsKeys()
    {
        // This is a workaround since Unity doesn't provide a direct method to get all EditorPrefs keys
        // In a real implementation, you might want to maintain a separate list of saved config names
        
        // For this example, we'll just return an empty array
        // In practice, you would need to implement this differently
        return new string[0];
    }

    private void ArrangeWithGridSpacing(Transform parent)
    {
        int cols = Mathf.Max(1, Mathf.Min(objectsToArrange.Count, maxItemsPerRow));
        for (int i = 0; i < objectsToArrange.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            float xPos = startPosition.x + col * spacing;
            float zPos = startPosition.z + row * spacing;
            Vector3 position = new Vector3(xPos, startPosition.y, zPos);
            ProcessObject(objectsToArrange[i], parent, position, prefabScale);
        }
    }

    private void ArrangeWithAutoSpacing(Transform parent)
    {
        Vector3 currentPos = startPosition;
        float rowMaxZ = 0f;
        int itemsInRow = 0;

        List<GameObject> tempInstances = new List<GameObject>();

        try
        {
            // First pass: instantiate and measure all objects
            foreach (var obj in objectsToArrange)
            {
                GameObject instance = (currentDataSource == DataSource.ProjectFolders) ? PrefabUtility.InstantiatePrefab(obj) as GameObject : obj;
                if (instance == null) continue;

                instance.transform.localScale = Vector3.one * prefabScale;
                Bounds bounds = GetEncapsulatingBounds(instance);

                if (itemsInRow >= maxItemsPerRow && maxItemsPerRow > 0)
                {
                    currentPos.x = startPosition.x;
                    currentPos.z += rowMaxZ + spacing;
                    rowMaxZ = 0;
                    itemsInRow = 0;
                }

                Vector3 targetPosition = new Vector3(currentPos.x + bounds.extents.x, startPosition.y, currentPos.z + bounds.extents.z);
                
                ProcessObject(obj, parent, targetPosition, prefabScale, instance);
                
                tempInstances.Add(instance);

                currentPos.x += bounds.size.x + spacing;
                rowMaxZ = Mathf.Max(rowMaxZ, bounds.size.z);
                itemsInRow++;
            }
        }
        finally
        {
            // If we were arranging from project, the temp instances are the final ones and are now parented.
            // If arranging from scene, we don't need to do anything with the 'temp' list as they were the original objects.
        }
    }

    private void ProcessObject(GameObject sourceObject, Transform parent, Vector3 position, float scale, GameObject instanceForProcessing = null)
    {
        if (sourceObject == null || parent == null)
        {
            Debug.LogWarning("处理对象时遇到空引用");
            return;
        }
        
        try
        {
            if (currentDataSource == DataSource.ProjectFolders)
            {
                GameObject instance = instanceForProcessing;
                if (instance != null)
                {
                    instance.transform.position = position;
                    Undo.SetTransformParent(instance.transform, parent, "Arrange Parent");
                }
            }
            else // SceneSelection
            {
                GameObject instance = sourceObject; // For scene objects, the source is the instance
                Undo.RecordObject(instance.transform, "Move Scene Object");
                instance.transform.position = position;
                instance.transform.localScale = Vector3.one * scale;
                Undo.SetTransformParent(instance.transform, parent, "Arrange Parent");
            }
            
            // 如果需要，应用旋转
            if (alignToGround && sourceObject != null)
            {
                // 这里需要实现AlignObjectToGround方法
                // AlignObjectToGround(sourceObject);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"处理对象 {(sourceObject != null ? sourceObject.name : "null")} 时出错: {e.Message}");
        }
    }

    private Bounds GetEncapsulatingBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        
        Bounds totalBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            totalBounds.Encapsulate(renderers[i].bounds);
        }
        return totalBounds;
    }

    private void RefreshPrefabList()
    {
        objectsToArrange.Clear();
        if (selectedFolderPaths.Count == 0)
        {
            UpdateStatusMessage();
            return;
        }

        HashSet<GameObject> foundPrefabs = new HashSet<GameObject>();
        string searchString = $"t:Prefab {searchFilter}";
        string[] guids = AssetDatabase.FindAssets(searchString, selectedFolderPaths.ToArray());

        foreach(var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!includeSubfolders)
            {
                string folder = Path.GetDirectoryName(path).Replace("\\", "/");
                if (!selectedFolderPaths.Any(selected => folder == selected)) continue;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) 
            {
                // 如果启用了仅显示有网格的对象，检查是否有网格
                if (showOnlyMeshObjects)
                {
                    if (prefab.GetComponentInChildren<MeshFilter>() != null || 
                        prefab.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                    {
                        foundPrefabs.Add(prefab);
                    }
                }
                else
                {
                    foundPrefabs.Add(prefab);
                }
            }
        }
        
        objectsToArrange = foundPrefabs.ToList();
        UpdateStatusMessage();
    }

    private void LoadSceneSelection()
    {
        objectsToArrange = Selection.gameObjects
            .Where(go => !go.name.StartsWith(PARENT_OBJECT_PREFIX))
            .ToList(); 
        UpdateStatusMessage();
    }
    
    private void UpdateStatusMessage()
    {
        if (currentDataSource == DataSource.ProjectFolders)
        {
            if (selectedFolderPaths.Count == 0)
                statusMessage = "请在Project窗口中选择一个或多个文件夹。";
            else
                statusMessage = $"已从 {selectedFolderPaths.Count} 个文件夹中找到 {objectsToArrange.Count} 个预制体。";
        }
        else // SceneSelection
        {
            if (objectsToArrange.Count == 0)
                statusMessage = "请在场景中选择物件，然后点击 'Load Selected' 按钮。";
            else
                statusMessage = $"已从场景中加载 {objectsToArrange.Count} 个物件。";
        }
    }

    private void ClearArrangedObjects()
    {
        try
        {
            // 首先清理记录的父对象
            if (arrangedParents != null && arrangedParents.Count > 0)
            {
                foreach (GameObject parent in arrangedParents.ToArray())
                {
                    if (parent != null)
                    {
                        Undo.DestroyObjectImmediate(parent);
                    }
                }
                arrangedParents.Clear();
            }
            
            // 查找所有带有我们标签的对象
            GameObject[] matrixParents = GameObject.FindGameObjectsWithTag(ARRANGE_TAG);
            int count = 0;
            foreach (var parent in matrixParents)
            {
                if (parent != null && parent.name.StartsWith(PARENT_OBJECT_PREFIX))
                {
                    Undo.DestroyObjectImmediate(parent);
                    count++;
                }
            }
            
            statusMessage = $"已清除 {count} 个排列容器。";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"清除对象时出错: {e.Message}");
            statusMessage = $"清除失败: {e.Message}";
        }
    }
    
    private void EnsureTagExists()
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");
        bool tagExists = false;
        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue.Equals(ARRANGE_TAG))
            {
                tagExists = true;
                break;
            }
        }
        if (!tagExists)
        {
            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = ARRANGE_TAG;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"[Prefab Arranger] Created new tag: {ARRANGE_TAG}");
        }
    }

    #endregion
}