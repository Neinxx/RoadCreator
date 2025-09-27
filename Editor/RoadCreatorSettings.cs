using UnityEngine;
using UnityEditor;
using System.IO;

namespace RoadSystem
{

    public enum ProcessingMode
    {
        CPU,
        GPU
    }

    public class RoadCreatorSettings : ScriptableObject
    {
        public const string k_SettingsPath = "Assets/RoadCreator/Settings/RoadCreatorSettings.asset";
        public const string ComputeShaderPath = "Assets/RoadCreator/shader/RoadTerrainModifier.compute";

        [Tooltip("新创建的TerrainLayer等资源的默认保存路径")]
        public string resourceSavePath = "Assets/RoadCreator/GeneratedAssets";

        // [新功能] 添加模式切换字段，并默认为更高效的GPU模式
        [Tooltip("选择地形修改的计算模式。\nGPU: 速度极快，推荐使用。\nCPU: 兼容性好，作为备用选项。")]
        public ProcessingMode modificationMode = ProcessingMode.GPU;


        public static RoadCreatorSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RoadCreatorSettings>(k_SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<RoadCreatorSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(k_SettingsPath));
                AssetDatabase.CreateAsset(settings, k_SettingsPath);
                AssetDatabase.SaveAssets();
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