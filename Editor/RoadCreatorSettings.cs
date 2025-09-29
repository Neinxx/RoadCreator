// 文件路径: Assets/RoadCreator/Editor/RoadCreatorSettings.cs
using UnityEngine;
using UnityEditor;
using System.IO;

namespace RoadSystem.Editor
{
    public enum ProcessingMode
    {
        CPU,
        GPU
    }

    
    [CreateAssetMenu(fileName = "RoadCreatorSettings", menuName = "Road Creator/New Settings Asset")]
    public class RoadCreatorSettings : ScriptableObject
    {
        public const string k_DefaultSettingsPath = "Assets/RoadCreator/Settings/RoadCreatorSettings.asset";

        [Header("核心处理设置")]
        [Tooltip("选择地形修改的计算模式。\nCPU: 兼容性好，最为稳定。\nGPU: 速度极快，需要支持Compute Shader。")]
        public ProcessingMode modificationMode = ProcessingMode.CPU;

        [Header("资源与路径配置")]
        [Tooltip("所有自动生成的资源（图集、地形图层等）都将保存在这个文件夹内。")]
        public string generatedAssetsPath = "Assets/RoadCreator/Generated";
        
        // [核心修改] 从字符串路径改为直接引用材质，更健壮、更方便！
        [Tooltip("用于渲染道路的自定义地形材质。")]
        public Material customTerrainMaterial;

        [Header("调试选项")]
        [Tooltip("勾选后，会在控制台输出更详细的日志信息，方便排查问题。")]
        public bool enableVerboseLogging = false;
        
        
        /// <summary>
        /// 验证所有路径和引用的有效性。
        /// </summary>
        /// <returns>如果所有配置都有效，则返回 true</returns>
        public bool ValidateSettings()
        {
            bool isValid = true;
            if (string.IsNullOrEmpty(generatedAssetsPath))
            {
                Debug.LogError("[RoadCreatorSettings] 'Generated Assets Path' 不能为空！");
                isValid = false;
            }
            if (customTerrainMaterial == null)
            {
                Debug.LogError("[RoadCreatorSettings] 'Custom Terrain Material' 不能为空，请拖拽一个材质球上来！");
                isValid = false;
            }

            if (isValid && enableVerboseLogging)
            {
                Debug.Log("[RoadCreatorSettings] 所有配置均有效。");
            }
            return isValid;
        }

        public static RoadCreatorSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RoadCreatorSettings>(k_DefaultSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<RoadCreatorSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(k_DefaultSettingsPath));
                AssetDatabase.CreateAsset(settings, k_DefaultSettingsPath);
                
                // [优化] 第一次创建时，自动寻找默认材质
                settings.customTerrainMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/RoadCreator/Shader/RoadTerrainHLSL_Material.mat");
                
                AssetDatabase.SaveAssets();
                Debug.Log($"已创建新的 RoadCreatorSettings 文件于: {k_DefaultSettingsPath}");
            }
            return settings;
        }

        [MenuItem("Tools/Road Creator/Select Settings")]
        public static void SelectSettingsAsset()
        {
            Selection.activeObject = GetOrCreateSettings();
        }
    }
}