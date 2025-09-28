
using UnityEngine;
using UnityEditor;

namespace RoadSystem.Editor
{
    [CustomEditor(typeof(RoadManager))]
    public class RoadEditor : UnityEditor.Editor
    {
        private SerializedObject serializedManager;
        private RoadManager roadManager;

        private void OnEnable()
        {
            roadManager = (RoadManager)target;
            serializedManager = new SerializedObject(roadManager);
            roadManager.OnRoadDataChanged += roadManager.RegenerateRoad;
        }

        private void OnDisable()
        {
            if (roadManager != null)
            {
                roadManager.OnRoadDataChanged -= roadManager.RegenerateRoad;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedManager.Update();

            var boldCenteredLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

            EditorGUILayout.HelpBox("要编辑道路路径，请从 Unity 主工具栏中选择“道路编辑工具”。", MessageType.Info);
            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("核心配置 (Core Configs)", boldCenteredLabel);
            EditorGUILayout.PropertyField(serializedManager.FindProperty("roadConfig"), new GUIContent("道路配置", "定义道路的剖面、材质和风格。"));
            EditorGUILayout.PropertyField(serializedManager.FindProperty("terrainConfig"), new GUIContent("地形配置", "定义道路如何与地形交互，例如纹理混合。"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("路径数据管理", boldCenteredLabel);
            DrawDataManagementButtons();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("预览与应用", boldCenteredLabel);
            DrawVisibilityToggle();

            EditorGUILayout.Space(5);

            // 使用我之前建议的 Scope 写法，更安全
            using (new GUIBackgroundColorScope(new Color(0.7f, 1f, 0.7f)))
            {
                DrawApplyTerrainButton();
            }

            EditorGUILayout.EndVertical();

            if (serializedManager.ApplyModifiedProperties())
            {
                roadManager.RegenerateRoad();
            }
        }

        // [修正] 应用地形修改的按钮逻辑
        private void DrawApplyTerrainButton()
        {
            if (GUILayout.Button(new GUIContent("应用地形修改 (Apply & Hide Mesh)", "将道路形状应用到地形上，并隐藏预览网格。"), GUILayout.Height(35)))
            {
                if (!roadManager.IsReadyForTerrainModification)
                {
                    Debug.LogWarning("无法应用地形修改：请确保 Road Manager 已正确配置...");
                    return;
                }

             
                // 总指挥 TerrainModifier。
                var modifier = new TerrainModifier();
                modifier.Execute(roadManager);

                if (roadManager.MeshRenderer != null)
                {
                    Undo.RecordObject(roadManager.MeshRenderer, "Hide Road Mesh After Apply");
                    roadManager.MeshRenderer.enabled = false;
                }
            }
        }
        
        

        private void DrawDataManagementButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("导入路径", "从JSON文件加载道路路径点。")))
                {
                    string path = EditorUtility.OpenFilePanel("导入路径数据", "", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        Undo.RecordObject(roadManager, "Import Road Path");
                        roadManager.ImportPath(path);
                        EditorUtility.SetDirty(roadManager);
                    }
                }
                if (GUILayout.Button(new GUIContent("导出路径", "将当前道路路径点保存为JSON文件。")))
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
            string buttonText = roadManager.MeshRenderer.enabled ? "隐藏预览路面" : "显示预览路面";
            if (GUILayout.Button(new GUIContent(buttonText, "切换道路预览网格的可见性。")))
            {
                Undo.RecordObject(roadManager.MeshRenderer, "Toggle Road Mesh Visibility");
                roadManager.MeshRenderer.enabled = !roadManager.MeshRenderer.enabled;
            }
        }
    }

    /// <summary>
    /// 一个辅助类，用于安全地临时改变GUI背景色。
    /// </summary>
    public class GUIBackgroundColorScope : GUI.Scope
    {
        private readonly Color previousColor;
        public GUIBackgroundColorScope(Color color)
        {
            previousColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
        }

        protected override void CloseScope()
        {
            GUI.backgroundColor = previousColor;
        }
    }
}