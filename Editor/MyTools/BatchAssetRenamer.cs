// 放入Editor文件夹
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// An advanced Unity editor tool for batch renaming assets.
/// Features two main modes: a general-purpose renamer with various rules (prefix, suffix, replace, regex, sequential)
/// and a specialized mode to rename asset dependencies based on the prefabs that use them.
/// </summary>
public class BatchAssetRenamer : EditorWindow
{
    #region Variables and Data Structures

    // === UI & State ===
    private Vector2 scrollPosition;
    private int selectedTab = 0;
    private readonly string[] tabTitles = { "通用重命名", "按预制体重命名依赖" };
    private int selectedPreviewIndex = -1; // NEW: To track selected item in the preview list

    // === Section 1: General Renaming ===
    private enum GeneralRenameRule { AddPrefix, AddSuffix, ReplaceString, ReplaceWithRegex, SequentialNaming, RemoveCharacters }
    private GeneralRenameRule generalRule = GeneralRenameRule.AddPrefix;
    private string g_prefix = "PRE_";
    private string g_suffix = "_SUF";
    private string g_stringToReplace = "Old";
    private string g_newString = "New";
    private string g_regexPattern = "(.*)";
    private string g_regexReplacement = "$1_New";
    private string g_baseName = "MyAsset_";
    private int g_startNumber = 0;
    private int g_digits = 3;
    private string g_charsToRemove = " ";
    private List<RenamePreview> generalRenameList = new List<RenamePreview>();

    // === Section 2: Dependency Renaming ===
    private string d_prefix = "";
    private string d_suffix = "";
    private bool d_groupByType = true;
    private bool d_showExtensions = false;
    private List<RenamePreview> dependencyRenameList = new List<RenamePreview>();
    private static readonly Dictionary<string, bool> targetExtensionToggles = new Dictionary<string, bool>
  {
        // Models
        {".fbx", true}, {".obj", true}, {".blend", true}, {".max", false}, {".c4d", false},
        // Materials
        {".mat", true},
        // Textures
        {".png", true}, {".jpg", true}, {".jpeg", true}, {".tga", true}, {".psd", true}, {".bmp", false}, {".tiff", false}
  };

    /// <summary>
    /// Represents a single asset's pending rename operation.
    /// </summary>
    private class RenamePreview
    {
        public enum Status { Ok, Warning, Error }

        public string OriginalPath { get; }
        public string OriginalName => Path.GetFileName(OriginalPath);
        public string NewName { get; set; }
        public string SourceInfo { get; } // e.g., the prefab that this dependency belongs to
        public Status PreviewStatus { get; set; } = Status.Ok;
        public string StatusTooltip { get; set; } = "OK";

        public RenamePreview(string originalPath, string newName, string sourceInfo = "")
        {
            OriginalPath = originalPath;
            NewName = newName;
            SourceInfo = sourceInfo;
        }
    }

    #endregion

    #region Window Management

    [MenuItem("Tools/重命名工具 (牛腩增强版)")]
    public static void ShowWindow()
    {
        GetWindow<BatchAssetRenamer>("高级批量重命名").minSize = new Vector2(480, 520);
    }

    private void OnGUI()
    {
        // MODIFIED: Draw a modern, clean title
        EditorGUILayout.LabelField(Styles.MainTitle, Styles.MainTitleStyle, GUILayout.Height(30));
        EditorGUILayout.Space(10);

        // MODIFIED: Use a more modern toolbar style
        selectedTab = GUILayout.Toolbar(selectedTab, tabTitles, "LargeButton", GUILayout.Height(32));
        EditorGUILayout.Space(10);

        // Main scroll view for the entire content area
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.BeginVertical(Styles.PaddingBox); // Add padding around content
        switch (selectedTab)
        {
            case 0: DrawGeneralRenamingUI(); break;
            case 1: DrawDependencyRenamingUI(); break;
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        DrawPreviewArea();

        EditorGUILayout.EndScrollView();
    }

    #endregion

    #region UI Drawing

    /// <summary>
    /// Draws the UI for the "General Renaming" tab.
    /// </summary>
    private void DrawGeneralRenamingUI()
    {
        // Section 1: Rules
        DrawSection(() =>
    {
        DrawSectionHeader("1. 选择命名规则");
        generalRule = (GeneralRenameRule)EditorGUILayout.EnumPopup("命名规则", generalRule);
        EditorGUILayout.Space(5);

        switch (generalRule)
        {
            case GeneralRenameRule.AddPrefix:
                g_prefix = EditorGUILayout.TextField("前缀 (Prefix)", g_prefix); break;
            case GeneralRenameRule.AddSuffix:
                g_suffix = EditorGUILayout.TextField("后缀 (Suffix)", g_suffix); break;
            case GeneralRenameRule.ReplaceString:
                g_stringToReplace = EditorGUILayout.TextField("查找 (Find)", g_stringToReplace);
                g_newString = EditorGUILayout.TextField("替换为 (Replace With)", g_newString); break;
            case GeneralRenameRule.ReplaceWithRegex:
                EditorGUILayout.HelpBox("使用正则表达式进行查找和替换。\n捕获组 (Capture Groups) 可用 $1, $2 等引用。", MessageType.Info);
                g_regexPattern = EditorGUILayout.TextField("正则模式 (Pattern)", g_regexPattern);
                g_regexReplacement = EditorGUILayout.TextField("替换格式 (Replacement)", g_regexReplacement); break;
            case GeneralRenameRule.SequentialNaming:
                g_baseName = EditorGUILayout.TextField("基础名称 (Base Name)", g_baseName);
                g_startNumber = EditorGUILayout.IntField("起始序号 (Start Number)", g_startNumber);
                g_digits = EditorGUILayout.IntSlider("序号位数 (Digits)", g_digits, 1, 10); break;
            case GeneralRenameRule.RemoveCharacters:
                g_charsToRemove = EditorGUILayout.TextField("要移除的字符", g_charsToRemove); break;
        }
    });

        // Section 2: Actions
        DrawSection(() =>
    {
        DrawSectionHeader("2. 执行操作");
        if (GUILayout.Button(Styles.GeneratePreviewIcon, GUILayout.Height(24))) GenerateGeneralRenamePreview();

        GUI.enabled = generalRenameList.Count > 0;
        if (GUILayout.Button(Styles.ExecuteIcon, GUILayout.Height(24)))
        {
            if (ConfirmExecution(generalRenameList.Count)) ExecuteRename(generalRenameList);
        }
        GUI.enabled = true;
    });
    }

    /// <summary>
    /// Draws the UI for the "Rename by Dependency" tab.
    /// </summary>
    private void DrawDependencyRenamingUI()
    {
        EditorGUILayout.HelpBox("选择预制体或其所在文件夹，将其依赖的资产（模型、材质、贴图等）重命名为与预制体同名。", MessageType.Info);

        // Section 1: Formatting
        DrawSection(() =>
    {
        DrawSectionHeader("1. 设置命名格式 (可选)");
        EditorGUILayout.LabelField("新名称格式: [前缀] + [预制体名称] + [分组后缀] + [后缀]", EditorStyles.miniLabel);

        d_prefix = EditorGUILayout.TextField("前缀 (Prefix)", d_prefix);
        d_suffix = EditorGUILayout.TextField("后缀 (Suffix)", d_suffix);
        d_groupByType = EditorGUILayout.Toggle(new GUIContent("按类型添加后缀", "为不同类型的资产（如模型、材质）添加特定后缀，例如 '_Mat', '_Tex'。"), d_groupByType);

        // Settings for file extensions
        d_showExtensions = EditorGUILayout.Foldout(d_showExtensions, Styles.SettingsIcon, true);
        if (d_showExtensions)
        {
            EditorGUI.indentLevel++;
            var keys = new List<string>(targetExtensionToggles.Keys);
            foreach (var key in keys)
            {
                targetExtensionToggles[key] = EditorGUILayout.Toggle(key, targetExtensionToggles[key]);
            }
            EditorGUI.indentLevel--;
        }
    });

        // Section 2: Actions
        DrawSection(() =>
    {
        DrawSectionHeader("2. 执行操作");
        if (GUILayout.Button(Styles.GeneratePreviewIcon, GUILayout.Height(24))) GenerateDependencyRenamePreview();

        GUI.enabled = dependencyRenameList.Count > 0;
        if (GUILayout.Button(Styles.ExecuteIcon, GUILayout.Height(24)))
        {
            if (ConfirmExecution(dependencyRenameList.Count)) ExecuteRename(dependencyRenameList);
        }
        GUI.enabled = true;
    });
    }

    /// <summary>
    /// Draws the scrollable area that displays the list of assets to be renamed.
    /// </summary>
    private void DrawPreviewArea()
    {
        DrawSectionHeader("预览");

        var listToDraw = selectedTab == 0 ? generalRenameList : dependencyRenameList;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(200));

        if (listToDraw.Count > 0)
        {
            for (int i = listToDraw.Count - 1; i >= 0; i--)
            {
                // Safety check in case list is modified during draw
                if (i < listToDraw.Count)
                {
                    DrawPreviewItem(listToDraw, i);
                }
            }
        }
        else
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("尚未生成预览。请选择资产并点击“生成预览”。", Styles.CenteredMessageStyle);
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Draws a single item in the preview list with status-based background color.
    /// </summary>
    private void DrawPreviewItem(List<RenamePreview> list, int index)
    {
        var item = list[index];

        // MODIFIED: Choose background style, prioritizing the 'Selected' state
        GUIStyle backgroundStyle;
        if (index == selectedPreviewIndex)
        {
            backgroundStyle = Styles.PreviewBox_Selected;
        }
        else
        {
            backgroundStyle = Styles.PreviewBox_Ok; // Default to OK
            if (item.PreviewStatus == RenamePreview.Status.Warning) backgroundStyle = Styles.PreviewBox_Warning;
            if (item.PreviewStatus == RenamePreview.Status.Error) backgroundStyle = Styles.PreviewBox_Error;
        }

        // Use a horizontal block for the whole item with the selected background
        EditorGUILayout.BeginHorizontal(backgroundStyle);

        // Status Icon - MODIFIED: Vertically centered to adapt to row height
        EditorGUILayout.BeginVertical(GUILayout.Width(20));
        GUILayout.FlexibleSpace();
        GUIContent statusIcon = GetStatusIcon(item.PreviewStatus, item.StatusTooltip);
        GUILayout.Label(statusIcon, GUILayout.Width(20), GUILayout.Height(20));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();

        // Main Content
        EditorGUILayout.BeginVertical();
        if (!string.IsNullOrEmpty(item.SourceInfo))
        {
            EditorGUILayout.LabelField($"源: {Path.GetFileName(item.SourceInfo)}", EditorStyles.miniLabel);
        }

        // Old and New Names
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(new GUIContent(item.OriginalName, item.OriginalPath), GUILayout.MinWidth(100), GUILayout.ExpandWidth(true));
        GUILayout.Label("→", GUILayout.Width(20));
        item.NewName = EditorGUILayout.TextField(item.NewName, GUILayout.MinWidth(120), GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // MODIFIED: Remove Button with modern text 'X'
        if (GUILayout.Button(Styles.RemoveButtonContent, Styles.RemoveButtonStyle, GUILayout.Width(24), GUILayout.Height(24)))
        {
            list.RemoveAt(index);
            GUIUtility.ExitGUI(); // Exit GUI to prevent layout errors from removed item
        }

        EditorGUILayout.EndHorizontal();

        // MODIFIED: Handle Ping and Selection on click
        Rect itemRect = GUILayoutUtility.GetLastRect();
        if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
        {
            // Toggle selection: if clicking the same item, deselect; otherwise, select the new one.
            selectedPreviewIndex = (selectedPreviewIndex == index) ? -1 : index;
            Repaint(); // Force a redraw to show the selection highlight

            var asset = AssetDatabase.LoadAssetAtPath<Object>(item.OriginalPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset; // Also select the object in the Project view
            }
            Event.current.Use();
        }
    }

    #endregion

    #region General Renaming Logic

    /// <summary>
        /// Generates a preview of rename operations based on the general rules and selected assets.
        /// </summary>
    private void GenerateGeneralRenamePreview()
    {
        ClearAllPreviews();
        var assetPaths = GetSelectedAssetPaths(Selection.assetGUIDs);
        if (assetPaths.Count == 0)
        {
            ShowNotification(new GUIContent("没有选择任何资产!"));
            return;
        }

        for (int i = 0; i < assetPaths.Count; i++)
        {
            string path = assetPaths[i];
            string originalName = Path.GetFileName(path);
            string originalNameWithoutExt = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string newName = originalName;

            try
            {
                switch (generalRule)
                {
                    case GeneralRenameRule.AddPrefix:
                        newName = g_prefix + originalName;
                        break;
                    case GeneralRenameRule.AddSuffix:
                        newName = originalNameWithoutExt + g_suffix + extension;
                        break;
                    case GeneralRenameRule.ReplaceString:
                        newName = string.IsNullOrEmpty(g_stringToReplace) ? originalName : originalName.Replace(g_stringToReplace, g_newString);
                        break;
                    case GeneralRenameRule.ReplaceWithRegex:
                        newName = Regex.Replace(originalName, g_regexPattern, g_regexReplacement);
                        break;
                    case GeneralRenameRule.SequentialNaming:
                        string sequence = (g_startNumber + i).ToString().PadLeft(g_digits, '0');
                        newName = g_baseName + sequence + extension;
                        break;
                    case GeneralRenameRule.RemoveCharacters:
                        var sb = new StringBuilder(originalName);
                        foreach (char c in g_charsToRemove) { sb.Replace(c.ToString(), ""); }
                        newName = sb.ToString();
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"为 '{originalName}' 生成新名称时出错: {e.Message}");
                continue;
            }

            generalRenameList.Add(new RenamePreview(path, newName));
        }

        ValidatePreviewList(generalRenameList);
        Debug.Log($"已生成 {generalRenameList.Count} 个资产的通用重命名预览。");
    }

    #endregion

    #region Dependency Renaming Logic

    /// <summary>
    /// Generates a preview of rename operations for the dependencies of selected prefabs.
    /// </summary>
    private void GenerateDependencyRenamePreview()
    {
        ClearAllPreviews();
        var prefabPaths = GetSelectedPrefabPaths();
        if (prefabPaths.Count == 0)
        {
            Debug.LogWarning("未选中任何预制体。请在 Project 窗口中选择预制体或包含预制体的文件夹。");
            ShowNotification(new GUIContent("没有选择任何预制体!"));
            return;
        }

        // Used to ensure an asset shared by multiple prefabs is only renamed once.
        var processedDependencies = new Dictionary<string, string>();

        try
        {
            for (int i = 0; i < prefabPaths.Count; i++)
            {
                string prefabPath = prefabPaths[i];
                EditorUtility.DisplayProgressBar("正在分析预制体依赖", $"正在处理: {Path.GetFileName(prefabPath)}", (float)i / prefabPaths.Count);

                string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                string[] dependencyPaths = AssetDatabase.GetDependencies(prefabPath, true); // Recursive to find all dependencies

                foreach (string depPath in dependencyPaths)
                {
                    string extension = Path.GetExtension(depPath).ToLower();
                    // Skip scripts, the prefab itself, or unselected file types
                    if (depPath.EndsWith(".cs") || depPath == prefabPath || !targetExtensionToggles.GetValueOrDefault(extension, false))
                    {
                        continue;
                    }

                    // If another selected prefab has already claimed this dependency, skip.
                    if (processedDependencies.ContainsKey(depPath) && processedDependencies[depPath] != prefabPath)
                    {
                        Debug.LogWarning($"冲突跳过: 资产 '{Path.GetFileName(depPath)}' 已被预制体 '{Path.GetFileName(processedDependencies[depPath])}' 引用，不能再被 '{Path.GetFileName(prefabPath)}' 重命名。");
                        continue;
                    }

                    string baseName = prefabName;
                    string typeSuffix = d_groupByType ? GetTypeSuffix(extension) : "";

                    // New name format: [Prefix] + [PrefabName] + [TypeSuffix] + [Suffix] + .ext
                    string newName = $"{d_prefix}{baseName}{typeSuffix}{d_suffix}{extension}";

                    dependencyRenameList.Add(new RenamePreview(depPath, newName, prefabPath));
                    processedDependencies[depPath] = prefabPath;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        ValidatePreviewList(dependencyRenameList);
        Debug.Log($"已生成 {dependencyRenameList.Count} 个依赖资产的重命名预览。请检查控制台和预览窗口确认有无冲突警告。");
    }

    /// <summary>
    /// Gets a standardized suffix based on the asset's file extension.
    /// </summary>
    private string GetTypeSuffix(string extension)
    {
        if (new[] { ".mat" }.Contains(extension)) return "_Mat";
        if (new[] { ".fbx", ".obj", ".blend", ".max", ".c4d" }.Contains(extension)) return "_Mod";
        if (new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".bmp", ".tiff" }.Contains(extension)) return "_Tex";
        return "_Asset";
    }

    #endregion

    #region Core Logic & Helpers

    /// <summary>
    /// Executes the renaming operations for the given list of previews.
    /// </summary>
    private void ExecuteRename(List<RenamePreview> renameList)
    {
        var validItems = renameList.Where(item => item.PreviewStatus != RenamePreview.Status.Error).ToList();
        if (validItems.Count == 0)
        {
            Debug.LogError("没有可执行的重命名操作。所有项均存在错误。");
            return;
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < validItems.Count; i++)
            {
                var item = validItems[i];
                EditorUtility.DisplayProgressBar("批量重命名", $"重命名: {item.OriginalName} → {item.NewName}", (float)i / validItems.Count);
                string errorMessage = AssetDatabase.RenameAsset(item.OriginalPath, Path.GetFileNameWithoutExtension(item.NewName));
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    Debug.LogError($"无法重命名资产 '{item.OriginalPath}' 为 '{item.NewName}': {errorMessage}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"成功完成 {validItems.Count} 个资产的重命名操作。");
        ClearAllPreviews();
    }

    /// <summary>
    /// Validates a list of rename previews, checking for errors like invalid names, duplicates, or no change.
    /// </summary>
    private void ValidatePreviewList(List<RenamePreview> list)
    {
        var newPathCounts = list.GroupBy(p => Path.Combine(Path.GetDirectoryName(p.OriginalPath) ?? "", p.NewName).ToLower())
                    .ToDictionary(g => g.Key, g => g.Count());

        foreach (var item in list)
        {
            // Reset status
            item.PreviewStatus = RenamePreview.Status.Ok;
            item.StatusTooltip = "OK";
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(item.OriginalPath);

            // Check for invalid characters
            if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(item.NewName)) || item.NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                item.PreviewStatus = RenamePreview.Status.Error;
                item.StatusTooltip = $"错误: 新名称 '{item.NewName}' 包含无效字符或为空。";
                Debug.LogError(item.StatusTooltip, asset);
                continue;
            }

            // Check if name is unchanged
            if (item.OriginalName.Equals(item.NewName, System.StringComparison.Ordinal))
            {
                item.PreviewStatus = RenamePreview.Status.Warning;
                item.StatusTooltip = "警告: 新名称与原名称相同。";
            }

            // Check for conflicts with other items in the list
            string newFullPath = Path.Combine(Path.GetDirectoryName(item.OriginalPath) ?? "", item.NewName).ToLower();
            if (newPathCounts.GetValueOrDefault(newFullPath, 0) > 1)
            {
                item.PreviewStatus = RenamePreview.Status.Error;
                item.StatusTooltip = $"错误: 新名称 '{item.NewName}' 与列表中其他资产的新名称冲突。";
                Debug.LogError(item.StatusTooltip, asset);
            }
        }
    }

    /// <summary>
    /// Gets all individual asset paths from the current selection, expanding any selected folders.
    /// </summary>
    private List<string> GetSelectedAssetPaths(string[] guids)
    {
        var assetPaths = new HashSet<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Directory.Exists(path))
            {
                string[] subGuids = AssetDatabase.FindAssets("t:Object", new[] { path });
                foreach (string subGuid in subGuids)
                {
                    string subPath = AssetDatabase.GUIDToAssetPath(subGuid);
                    if (!Directory.Exists(subPath)) { assetPaths.Add(subPath); }
                }
            }
            else
            {
                assetPaths.Add(path);
            }
        }
        return assetPaths.ToList();
    }

    /// <summary>
    /// Gets all prefab asset paths from the current selection, expanding folders.
    /// </summary>
    private List<string> GetSelectedPrefabPaths()
    {
        var prefabPaths = new HashSet<string>();
        string[] guids = Selection.assetGUIDs;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Directory.Exists(path))
            {
                string[] subGuids = AssetDatabase.FindAssets("t:Prefab", new[] { path });
                prefabPaths.UnionWith(subGuids.Select(AssetDatabase.GUIDToAssetPath));
            }
            else if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
            {
                prefabPaths.Add(path);
            }
        }
        return prefabPaths.ToList();
    }

    private void ClearAllPreviews()
    {
        generalRenameList.Clear();
        dependencyRenameList.Clear();
        selectedPreviewIndex = -1; // NEW: Reset selection when clearing
    }

    private bool ConfirmExecution(int count)
    {
        return EditorUtility.DisplayDialog("确认操作",
          $"你确定要重命名 {count} 个资产吗？\n请检查预览和控制台以确认无错误。",
          "确定", "取消");
    }

    /// <summary>
    /// Draws a styled section header.
    /// </summary>
    private static void DrawSectionHeader(string title)
    {
        EditorGUILayout.LabelField(title, Styles.SectionHeaderStyle);
        EditorGUILayout.Space(2);
    }

    /// <summary>
        /// A helper to wrap a block of UI code in a styled vertical group.
        /// </summary>
    private static void DrawSection(System.Action drawAction)
    {
        EditorGUILayout.BeginVertical(Styles.SectionBox);
        drawAction?.Invoke();
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(10);
    }

    /// <summary>
        /// Gets the appropriate status icon based on the RenamePreview status.
        /// </summary>
    private static GUIContent GetStatusIcon(RenamePreview.Status status, string tooltip)
    {
        switch (status)
        {
            case RenamePreview.Status.Warning: return new GUIContent(Styles.WarningIcon, tooltip);
            case RenamePreview.Status.Error: return new GUIContent(Styles.ErrorIcon, tooltip);
            default: return new GUIContent(Styles.OkIcon, tooltip);
        }
    }

    #endregion

    #region UI Styles and Content

    /// <summary>
        /// Static class to manage and cache GUIStyles and GUIContents for the editor window.
        /// This improves performance by avoiding object creation during OnGUI.
        /// </summary>
    private static class Styles
    {
        public static readonly GUIContent MainTitle;
        public static readonly GUIContent GeneratePreviewIcon;
        public static readonly GUIContent ExecuteIcon;
        public static readonly GUIContent SettingsIcon;
        public static readonly GUIContent RemoveButtonContent;
        public static readonly Texture2D WarningIcon;
        public static readonly Texture2D ErrorIcon;
        public static readonly Texture2D OkIcon;

        public static readonly GUIStyle MainTitleStyle;
        public static readonly GUIStyle SectionHeaderStyle;
        public static readonly GUIStyle SectionBox;
        public static readonly GUIStyle PaddingBox;
        public static readonly GUIStyle PreviewBox_Ok;
        public static readonly GUIStyle PreviewBox_Warning;
        public static readonly GUIStyle PreviewBox_Error;
        public static readonly GUIStyle PreviewBox_Selected; // NEW: Style for selected items
        public static readonly GUIStyle RemoveButtonStyle;
        public static readonly GUIStyle CenteredMessageStyle;

        static Styles()
        {
            // Icons & Content
            // MODIFIED: Removed icon from title
            MainTitle = new GUIContent("传说中的Unity资产重命名工具几乎完美版");
            GeneratePreviewIcon = new GUIContent(" 生成重命名预览", EditorGUIUtility.IconContent("d_PlayButton").image, "根据当前规则生成预览。");
            ExecuteIcon = new GUIContent(" 执行重命名", EditorGUIUtility.IconContent("d_SaveAs").image, "应用预览中的名称更改。");
            SettingsIcon = new GUIContent(" 可重命名的文件类型", EditorGUIUtility.IconContent("d_Settings").image);
            // MODIFIED: Changed remove button to a simple text 'X'
            RemoveButtonContent = new GUIContent("✕", "从预览中移除");
            WarningIcon = EditorGUIUtility.IconContent("d_console.warnicon.sml").image as Texture2D;
            ErrorIcon = EditorGUIUtility.IconContent("d_console.erroricon.sml").image as Texture2D;
            OkIcon = EditorGUIUtility.IconContent("d_console.infoicon.sml").image as Texture2D;

            // Styles
            //标题
            MainTitleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(5, 7, 5, 5),
                normal = { textColor = new Color(1.0f, 0.65f, 0.0f) }
            };

            SectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            PaddingBox = new GUIStyle { padding = new RectOffset(10, 10, 10, 10) };
            SectionBox = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 0, 0)
            };

            var basePreviewBox = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(5, 5, 5, 5),
                margin = new RectOffset(0, 0, 4, 4)
            };

            PreviewBox_Ok = new GUIStyle(basePreviewBox);
            PreviewBox_Ok.normal.background = MakeTex(1, 1, new Color(0.188f, 0.188f, 0.188f, 1.0f));

            PreviewBox_Warning = new GUIStyle(basePreviewBox);
            PreviewBox_Warning.normal.background = MakeTex(1, 1, new Color(0.7f, 0.5f, 0.1f, 0.25f));

            PreviewBox_Error = new GUIStyle(basePreviewBox);
            PreviewBox_Error.normal.background = MakeTex(1, 1, new Color(0.6f, 0.2f, 0.2f, 0.3f));

            // NEW: Style for the selected preview item
            PreviewBox_Selected = new GUIStyle(basePreviewBox);
            PreviewBox_Selected.normal.background = MakeTex(1, 1, new Color(0.22f, 0.45f, 0.8f, 0.4f));

            // MODIFIED: New style for the text-based remove button
            RemoveButtonStyle = new GUIStyle()
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.grey : new Color(0.3f, 0.3f, 0.3f) },
                hover = { textColor = Color.white, background = MakeTex(1, 1, new Color(0.9f, 0.3f, 0.3f, 0.5f)) },
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            CenteredMessageStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                wordWrap = true,
                fontSize = 12
            };
        }

        private static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) { pix[i] = col; }
            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }

    #endregion
}