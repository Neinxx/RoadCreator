// 文件路径: Assets/RoadCreator/Editor/RoadCreatorSettingsEditor.cs
using UnityEngine;
using UnityEditor;
using System.IO;

namespace RoadSystem.Editor
{
    /// <summary>
    /// [新增] RoadCreatorSettings 的自定义编辑器。
    /// 提供了更美观、更具交互性的 Inspector 面板。
    /// </summary>
    [CustomEditor(typeof(RoadCreatorSettings))]
    public class RoadCreatorSettingsEditor : UnityEditor.Editor
    {
        private SerializedProperty modificationModeProp;
        private SerializedProperty generatedAssetsPathProp;
        private SerializedProperty customTerrainMaterialProp;
        private SerializedProperty enableVerboseLoggingProp;

        private GUIStyle titleStyle;
        private GUIStyle headerStyle;

        private void OnEnable()
        {
            // 链接到 SO 的属性
            modificationModeProp = serializedObject.FindProperty("modificationMode");
            generatedAssetsPathProp = serializedObject.FindProperty("generatedAssetsPath");
            customTerrainMaterialProp = serializedObject.FindProperty("customTerrainMaterial");
            enableVerboseLoggingProp = serializedObject.FindProperty("enableVerboseLogging");

            // 初始化GUI样式
            titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 10, 10)
            };

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 10, 5)
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update(); // 更新序列化对象

            // --- 标题 ---
            EditorGUILayout.LabelField("Road Creator Pro", titleStyle, GUILayout.Height(30));
            EditorGUILayout.HelpBox("这里是道路创建工具的核心配置中心。所有全局设置和资源路径都在此管理。", MessageType.Info);

            // --- 核心设置 ---
            EditorGUILayout.LabelField("核心设置", headerStyle);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(modificationModeProp);
            }
            
            // --- 资源与路径配置 ---
            EditorGUILayout.LabelField("资源与路径配置", headerStyle);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(generatedAssetsPathProp);
                EditorGUILayout.PropertyField(customTerrainMaterialProp);
            }
            
            // --- 调试选项 ---
            EditorGUILayout.LabelField("调试选项", headerStyle);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(enableVerboseLoggingProp);
            }

            EditorGUILayout.Space(20);

            // --- 智能操作按钮 ---
            EditorGUILayout.LabelField("智能操作", headerStyle);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button("验证配置有效性", GUILayout.Height(30)))
                {
                    ((RoadCreatorSettings)target).ValidateSettings();
                }

                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f); // 危险操作用红色背景
                if (GUILayout.Button("! 清理所有生成的资源 !", GUILayout.Height(30)))
                {
                    CleanGeneratedAssets();
                }
                GUI.backgroundColor = Color.white; // 恢复默认颜色
            }

            serializedObject.ApplyModifiedProperties(); // 应用所有更改
        }

        private void CleanGeneratedAssets()
        {
            var settings = (RoadCreatorSettings)target;
            string path = settings.generatedAssetsPath;

            if (Directory.Exists(path))
            {
                if (EditorUtility.DisplayDialog("确认清理",
                    $"你确定要删除文件夹 '{path}' 内的所有内容吗？\n\n这个操作不可撤销！", "是的，删除它们", "取消"))
                {
                    Directory.Delete(path, true);
                    AssetDatabase.Refresh();
                    Debug.Log($"已成功清理并删除了文件夹: {path}");
                }
            }
            else
            {
                Debug.LogWarning($"文件夹 '{path}' 不存在，无需清理。");
            }
        }
    }
}