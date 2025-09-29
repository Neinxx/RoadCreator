#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Security.Cryptography;

/// <summary>
/// 一个用于为预制体（Prefab）生成缩略图和信息汇总报告的 Unity 编辑器工具。
/// </summary>
public class PrefabThumbnailGenerator : EditorWindow
{
    // --- 常量定义 ---
    private const string WINDOW_TITLE = "Prefab Thumbnails";
    private const string MENU_PATH = "Tools/Prefab Thumbnail Generator";
    private const string PREFS_OUTPUT_PATH_KEY = "PrefabThumbnailGenerator_OutputPath";
    private const string PREFS_SIZE_KEY = "PrefabThumbnailGenerator_ThumbnailSize";
    private const string PREFS_INCLUDE_SUBFOLDERS_KEY = "PrefabThumbnailGenerator_IncludeSubfolders";
    // ... 其他设置的 EditorPrefs Key
    
    // --- 可配置参数 ---
    private DefaultAsset _targetFolder;
    private string _outputFolderPath = "C:/PrefabThumbnails";
    private int _thumbnailSize = 128;
    private bool _includeSubfolders = true;

    private Vector3 _prefabPosition = new Vector3(-98.481f, 12.819f, 17.528f);
    private Vector3 _cameraPosition = new Vector3(0, 0, -5);
    private Vector3 _cameraRotation = new Vector3(30, -45, 0); 
    private Color _backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    private Color _DuplicateColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    private float _cameraFOV = 60f;
    private bool _useUnityFocus = true;
    private bool _detectDuplicates = true;
    
    private Vector2 _scrollPosition;

    [MenuItem(MENU_PATH)]
    public static void ShowWindow()
    {
        GetWindow<PrefabThumbnailGenerator>(WINDOW_TITLE);
    }

    private void OnEnable()
    {
        // 加载上次的配置
        _outputFolderPath = EditorPrefs.GetString(PREFS_OUTPUT_PATH_KEY, "C:/PrefabThumbnails");
        _thumbnailSize = EditorPrefs.GetInt(PREFS_SIZE_KEY, 128);
        _includeSubfolders = EditorPrefs.GetBool(PREFS_INCLUDE_SUBFOLDERS_KEY, true);
    }

    private void OnDisable()
    {
        // 保存当前配置
        EditorPrefs.SetString(PREFS_OUTPUT_PATH_KEY, _outputFolderPath);
        EditorPrefs.SetInt(PREFS_SIZE_KEY, _thumbnailSize);
        EditorPrefs.SetBool(PREFS_INCLUDE_SUBFOLDERS_KEY, _includeSubfolders);
    }
    
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Prefab Thumbnail Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        // --- 文件夹设置 ---
        _targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder:", _targetFolder, typeof(DefaultAsset), false);

        EditorGUILayout.BeginHorizontal();
        _outputFolderPath = EditorGUILayout.TextField("Output Folder:", _outputFolderPath);
        if (GUILayout.Button("Browse...", GUILayout.Width(80)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Output Folder", _outputFolderPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                _outputFolderPath = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        // --- 渲染设置 ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);
        _useUnityFocus = EditorGUILayout.Toggle("Auto Focus (Recommended)", _useUnityFocus);
        using (new EditorGUI.DisabledScope(_useUnityFocus))
        {
            _prefabPosition = EditorGUILayout.Vector3Field("Prefab Position:", _prefabPosition);
            _cameraPosition = EditorGUILayout.Vector3Field("Camera Base Position:", _cameraPosition);
        }
        _cameraRotation = EditorGUILayout.Vector3Field("Camera Rotation:", _cameraRotation);
        _backgroundColor = EditorGUILayout.ColorField("Background Color:", _backgroundColor);
        _DuplicateColor = EditorGUILayout.ColorField("Duplicate Color:", _DuplicateColor);
        _cameraFOV = EditorGUILayout.Slider("Camera FOV:", _cameraFOV, 1, 179);

        // --- 输出和检测设置 ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output & Detection Settings", EditorStyles.boldLabel);
        _thumbnailSize = EditorGUILayout.IntSlider("Thumbnail Size:", _thumbnailSize, 64, 1024);
        _includeSubfolders = EditorGUILayout.Toggle("Include Subfolders:", _includeSubfolders);
        _detectDuplicates = EditorGUILayout.Toggle("Detect & Mark Duplicates:", _detectDuplicates);

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        
        // --- 操作按钮 ---
        if (GUILayout.Button("Generate Thumbnails", GUILayout.Height(30)))
        {
            GenerateThumbnailsWithChecks();
        }

        if (Directory.Exists(_outputFolderPath))
        {
            if (GUILayout.Button("Open Output Folder"))
            {
                OpenOutputFolder();
            }
        }
    }
    
    /// <summary>
    /// 带有前置检查的生成入口
    /// </summary>
    private void GenerateThumbnailsWithChecks()
    {
        if (_targetFolder == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a target folder.", "OK");
            return;
        }

        string targetFolderPath = AssetDatabase.GetAssetPath(_targetFolder);
        if (string.IsNullOrEmpty(targetFolderPath) || !AssetDatabase.IsValidFolder(targetFolderPath))
        {
            EditorUtility.DisplayDialog("Error", "Invalid target folder.", "OK");
            return;
        }

        if (!Directory.Exists(_outputFolderPath))
        {
            try
            {
                Directory.CreateDirectory(_outputFolderPath);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create output folder: {e.Message}", "OK");
                return;
            }
        }

        List<string> prefabPaths = GetAllPrefabPaths(targetFolderPath);
        if (prefabPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Info", "No prefabs found in the selected folder.", "OK");
            return;
        }

        ProcessPrefabs(prefabPaths, targetFolderPath);
    }
    
    /// <summary>
    /// 主处理流程
    /// </summary>
    private void ProcessPrefabs(List<string> prefabPaths, string targetFolderPath)
    {
        
        PreviewRenderer.Initialize();
        List<PrefabInfo> prefabInfos = new List<PrefabInfo>();
        
        try
        {
            // 识别出重复项
            var duplicateData = _detectDuplicates ? DetectDuplicates(prefabPaths) : new Dictionary<string, int>();
            
            for (int i = 0; i < prefabPaths.Count; i++)
            {
                string prefabPath = prefabPaths[i];
                string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                
                if (EditorUtility.DisplayCancelableProgressBar("Generating Thumbnails", $"Processing: {prefabName}", (float)i / prefabPaths.Count))
                {
                    break; // 用户取消
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                bool isDuplicate = duplicateData.ContainsKey(prefabPath);
                int duplicateGroup = isDuplicate ? duplicateData[prefabPath] : 0;
                
                // 使用复用的渲染器生成缩略图
                Texture2D thumbnail = PreviewRenderer.Render(
                    prefab, 
                    _thumbnailSize, 
                    _prefabPosition, 
                    _cameraPosition, 
                    Quaternion.Euler(_cameraRotation), 
                    _cameraFOV, 
                    isDuplicate ? _DuplicateColor : _backgroundColor,
                    _useUnityFocus
                );

                PrefabInfo info = new PrefabInfo
                {
                    name = prefab.name,
                    path = prefabPath,
                    fileSize = GetFormattedFileSize(prefabPath),
                    isDuplicate = isDuplicate,
                    duplicateGroup = duplicateGroup
                };
                
                if (thumbnail != null)
                {
                    string relativePath = GetRelativePathInTarget(prefabPath, targetFolderPath);
                    string thumbnailFolderPath = Path.Combine(_outputFolderPath, relativePath);
                    Directory.CreateDirectory(thumbnailFolderPath);

                    string filename = $"{prefabName}{(isDuplicate ? "_duplicate" : "")}.png";
                    string thumbnailPath = Path.Combine(thumbnailFolderPath, filename);
                    
                    File.WriteAllBytes(thumbnailPath, thumbnail.EncodeToPNG());
                    DestroyImmediate(thumbnail);

                    info.thumbnailPath = Path.Combine(relativePath, filename).Replace("\\", "/");
                }
                
                prefabInfos.Add(info);
            }
            
            GenerateCsvSummary(prefabInfos);
            EditorUtility.DisplayDialog("Success", $"Generated thumbnails for {prefabInfos.Count} prefabs.\nOutput: {_outputFolderPath}", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error generating thumbnails: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Error", $"An unexpected error occurred: {e.Message}", "OK");
        }
        finally
        {
            
            PreviewRenderer.Cleanup();
            EditorUtility.ClearProgressBar();
        }
    }
    
    
    private List<string> GetAllPrefabPaths(string folderPath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        IEnumerable<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath);

        if (!_includeSubfolders)
        {
            paths = paths.Where(p => Path.GetDirectoryName(p).Replace("\\", "/") == folderPath);
        }

        return paths.ToList();
    }
    
    
    //返回一个路径到分组ID的映射
    
    private Dictionary<string, int> DetectDuplicates(List<string> prefabPaths)
    {
        var signatureToPaths = new Dictionary<string, List<string>>();
        
        // 1. 计算所有签名
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            EditorUtility.DisplayProgressBar("Detecting Duplicates", $"Analyzing: {Path.GetFileName(prefabPaths[i])}", (float)i / prefabPaths.Count);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
            if (prefab == null) continue;

            string signature = GetPrefabSignature(prefab);
            if (!signatureToPaths.ContainsKey(signature))
            {
                signatureToPaths[signature] = new List<string>();
            }
            signatureToPaths[signature].Add(prefabPaths[i]);
        }

        // 2. 标记重复项
        var duplicateMap = new Dictionary<string, int>();
        int groupIndex = 1;
        foreach (var group in signatureToPaths.Values)
        {
            if (group.Count > 1)
            {
                foreach (string path in group)
                {
                    duplicateMap[path] = groupIndex;
                }
                groupIndex++;
            }
        }
        
        return duplicateMap;
    }

    
    private string GetPrefabSignature(GameObject prefab)
    {
        var signatureBuilder = new StringBuilder();
        
        // 使用 HashSet 自动去重，
        var meshGuids = new HashSet<string>();
        var materialGuids = new HashSet<string>();
        var textureGuids = new HashSet<string>();

        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            // 处理网格
            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                meshGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(smr.sharedMesh)));
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
            {
                meshGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mf.sharedMesh)));
            }

            // 处理材质和纹理
            foreach (var mat in renderer.sharedMaterials)
            {
                if (mat == null) continue;
                materialGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mat)));

                // 从材质中提取纹理
                var so = new SerializedObject(mat);
                var properties = so.GetIterator();
                while (properties.NextVisible(true))
                {
                    if (properties.propertyType == SerializedPropertyType.ObjectReference &&
                        properties.objectReferenceValue is Texture tex)
                    {
                        textureGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tex)));
                    }
                }
            }
        }
        
        // 构建有序的签名字符串，确保结果一致性
        signatureBuilder.Append("M:").Append(string.Join(",", meshGuids.OrderBy(g => g)));
        signatureBuilder.Append("|T:").Append(string.Join(",", textureGuids.OrderBy(g => g)));
        signatureBuilder.Append("|A:").Append(string.Join(",", materialGuids.OrderBy(g => g)));
        
        // 生成固定长度的签名
        using (var sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(signatureBuilder.ToString()));
            return System.BitConverter.ToString(hashBytes).Replace("-", "");
        }
    }
    
    private void GenerateCsvSummary(List<PrefabInfo> prefabInfos)
    {
        string csvPath = Path.Combine(_outputFolderPath, "prefab_summary.csv");
        var csvContent = new StringBuilder();
        csvContent.AppendLine("Name,Path,Thumbnail Path,File Size,Is Duplicate,Duplicate Group");

        foreach (var info in prefabInfos)
        {
            csvContent.AppendLine(
                $"{EscapeCsvField(info.name)}," +
                $"{EscapeCsvField(info.path)}," +
                $"{EscapeCsvField(info.thumbnailPath)}," +
                $"{info.fileSize}," +
                $"{(info.isDuplicate ? "Yes" : "No")}," +
                $"{(info.isDuplicate ? info.duplicateGroup.ToString() : "")}"
            );
        }

        File.WriteAllText(csvPath, csvContent.ToString(), Encoding.UTF8);
    }
    
    // --- 辅助方法 ---
    private string GetRelativePathInTarget(string fullPath, string targetPath)
    {
        if (fullPath.StartsWith(targetPath))
        {
            string relative = fullPath.Substring(targetPath.Length);
            return Path.GetDirectoryName(relative.TrimStart('/')).Replace("\\", "/");
        }
        return "";
    }
    
    private string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
    
    
    private string GetFormattedFileSize(string assetPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath)) return "N/A";

            long size = new FileInfo(fullPath).Length;
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F2} KB";
            return $"{size / (1024.0 * 1024.0):F2} MB";
        }
        catch { return "Error"; }
    }
    
    
    private void OpenOutputFolder()
    {
        if (!Directory.Exists(_outputFolderPath))
        {
            EditorUtility.DisplayDialog("Error", "Output folder does not exist.", "OK");
            return;
        }
        EditorUtility.RevealInFinder(Path.GetFullPath(_outputFolderPath));
    }

    // --- 数据结构 ---
    private class PrefabInfo
    {
        public string name;
        public string path;
        public string thumbnailPath;
        public string fileSize;
        public bool isDuplicate;
        public int duplicateGroup;
    }
}


internal static class PreviewRenderer
{
    private static GameObject _previewRoot;
    private static Camera _camera;

    /// <summary>
    /// 初始化渲染环境。在所有渲染操作开始前调用一次。
    /// </summary>
    public static void Initialize()
    {
        // 创建一个隐藏的根对象，用于存放相机和实例化的Prefab
        _previewRoot = new GameObject("PrefabPreviewRenderer") { hideFlags = HideFlags.HideAndDontSave };
        
        var cameraObj = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
        cameraObj.transform.SetParent(_previewRoot.transform);
        
        _camera = cameraObj.AddComponent<Camera>();
        _camera.enabled = false; // 必须禁用，防止渲染到Game视图
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.orthographic = false;
        _camera.allowHDR = false;
        _camera.allowMSAA = false;
    }
    
    /// <summary>
    /// 为指定的GameObject渲染一张缩略图。
    /// </summary>
    public static Texture2D Render(GameObject obj, int size, Vector3 prefabPos, Vector3 camPos, Quaternion camRot, float fov, Color bgColor, bool autoFocus)
    {
        if (_previewRoot == null || _camera == null)
        {
            Debug.LogError("PreviewRenderer is not initialized. Call Initialize() first.");
            return null;
        }

        GameObject instance = null;
        // 临时RenderTexture
        RenderTexture renderTexture = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);

        try
        {
            instance = Object.Instantiate(obj, _previewRoot.transform, false);
            instance.transform.position = prefabPos;
            
            // 配置相机
            _camera.backgroundColor = bgColor;
            _camera.fieldOfView = fov;
            
            if (autoFocus)
            {
                SetupCameraAutoFocus(instance, _camera, camRot);
            }
            else
            {
                _camera.transform.position = camPos;
                _camera.transform.rotation = camRot;
            }

            // 渲染到目标纹理
            _camera.targetTexture = renderTexture;
            _camera.Render();
            
            // 从RenderTexture读取像素到Texture2D
            RenderTexture.active = renderTexture;
            Texture2D thumbnail = new Texture2D(size, size, TextureFormat.RGB24, false);
            thumbnail.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            thumbnail.Apply();

            return thumbnail;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to render preview for {obj.name}: {e.Message}");
            return null;
        }
        finally
        {
            // 清理本次渲染的临时对象
            if (instance != null) Object.DestroyImmediate(instance);
            
            _camera.targetTexture = null;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }
    
    /// <summary>
    /// 清理渲染环境。在所有渲染操作结束后调用一次。
    /// </summary>
    public static void Cleanup()
    {
        if (_previewRoot != null)
        {
            Object.DestroyImmediate(_previewRoot);
            _previewRoot = null;
            _camera = null;
        }
    }

    private static void SetupCameraAutoFocus(GameObject target, Camera camera, Quaternion rotation)
    {
        Bounds bounds = GetObjectBounds(target);
        Vector3 pivot = bounds.center;
        float distance = CalculateCameraDistance(bounds, camera.fieldOfView);
        
        Vector3 direction = rotation * Vector3.forward;
        camera.transform.position = pivot - direction * distance;
        camera.transform.rotation = rotation;
    }
    
    private static float CalculateCameraDistance(Bounds bounds, float fov)
    {
        // 确保物体能完整显示在相机视野内，并留出一些边距
        float objectSize = bounds.size.magnitude;
        if (objectSize < 0.001f) return 10f;
        
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        float distance = (objectSize * 0.5f) / Mathf.Tan(halfFovRad);
        
        return Mathf.Max(distance * 1.2f, 0.01f); // 增加20%的距离作为边距
    }
    
    private static Bounds GetObjectBounds(GameObject obj)
    {
        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(obj.transform.position, Vector3.one * 0.1f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }
}
#endif