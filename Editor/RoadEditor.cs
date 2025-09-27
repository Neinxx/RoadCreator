using UnityEngine;
using UnityEditor;

namespace RoadSystem
{
    [CustomEditor(typeof(RoadManager))]
    public class RoadEditor : Editor
    {
        private SerializedObject serializedManager;
        private RoadManager roadManager;

        private void OnEnable()
        {
            roadManager = (RoadManager)target;
            serializedManager = new SerializedObject(roadManager);
        }

        public override void OnInspectorGUI()
        {
            serializedManager.Update();

            EditorGUILayout.HelpBox("要编辑道路路径，请从 Unity 主工具栏中选择“道路编辑工具”。", MessageType.Info);
            EditorGUILayout.Space();

            DrawConfigurationFields();
            if (serializedManager.ApplyModifiedProperties())
            {
                if (GUI.changed) roadManager.RegenerateRoad();
            }

            EditorGUILayout.Space(10);
            DrawDataManagementButtons();
            EditorGUILayout.Space(10);

            DrawVisibilityToggle();
            DrawApplyTerrainButton(); // 调用我们即将修改的方法
        }

        // --- [核心修改区域] ---
        private void DrawApplyTerrainButton()
        {
            if (GUILayout.Button("应用地形修改 (Apply & Hide Mesh)", GUILayout.Height(35)))
            {
                if (!roadManager.IsReadyForTerrainModification)
                {
                    Debug.LogWarning("无法应用地形修改：请确保 Road Manager 已正确配置 Road Config 和 Terrain Config。");
                    return;
                }

                // 1. 在Editor脚本中安全地获取设置
                var settings = RoadCreatorSettings.GetOrCreateSettings();

                // 2. 根据设置，创建对应的模块实例
                ITerrainModificationModule module;
                switch (settings.modificationMode)
                {
                    case ProcessingMode.GPU:
                        module = new GPUFlattenAndTextureModule();
                        break;
                    case ProcessingMode.CPU:
                    default:
                        module = new CPUFlattenAndTextureModule();
                        break;
                }

                // 3. 将创建好的模块“注入”到TerrainModifier的构造函数中
                ITerrainModifier modifier = new TerrainModifier(module);

                // 4. 执行
                modifier.Execute(roadManager);

                // 5. 隐藏路面
                if (roadManager.MeshRenderer != null)
                {
                    Undo.RecordObject(roadManager.MeshRenderer, "Hide Road Mesh After Apply");
                    roadManager.MeshRenderer.enabled = false;
                }
            }
        }

        // ... 其他方法保持不变 ...
        private void DrawConfigurationFields()
        {
            EditorGUILayout.LabelField("核心配置 (Core Configs)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedManager.FindProperty("roadConfig"));
            EditorGUILayout.PropertyField(serializedManager.FindProperty("terrainConfig"));
        }
        private void DrawDataManagementButtons()
        {
            EditorGUILayout.LabelField("路径数据管理", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导入路径"))
                {
                    string path = EditorUtility.OpenFilePanel("导入路径数据", "", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        Undo.RecordObject(roadManager, "Import Road Path");
                        roadManager.ImportPath(path);
                        EditorUtility.SetDirty(roadManager);
                    }
                }
                if (GUILayout.Button("导出路径"))
                {
                    string path = EditorUtility.SaveFilePanel("导出路径数据", "", $"{roadManager.gameObject.name}_path.json", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        roadManager.ExportPath(path);
                    }
                }
            }
        }
        private void DrawVisibilityToggle()
        {
            if (roadManager.MeshRenderer == null) return;
            string buttonText = roadManager.MeshRenderer.enabled ? "隐藏预览路面 (Hide Mesh)" : "显示预览路面 (Show Mesh)";
            if (GUILayout.Button(buttonText))
            {
                Undo.RecordObject(roadManager.MeshRenderer, "Toggle Road Mesh Visibility");
                roadManager.MeshRenderer.enabled = !roadManager.MeshRenderer.enabled;
            }
        }
    }
}