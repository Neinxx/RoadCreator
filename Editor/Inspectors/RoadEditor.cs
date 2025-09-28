// 文件路径: Assets/RoadCreator/Editor/Inspectors/RoadEditor.cs
using UnityEngine;
using UnityEditor;

namespace RoadSystem.Editor
{
    [CustomEditor(typeof(RoadManager))]
    public class RoadEditor : UnityEditor.Editor
    {
        private SerializedObject serializedManager;
        private RoadManager roadManager;

        // OnEnable 在选中对象时被调用
        private void OnEnable()
        {

            roadManager = (RoadManager)target;
            serializedManager = new SerializedObject(roadManager);
        }

        // OnInspectorGUI 负责绘制 Inspector 面板的内容
        public override void OnInspectorGUI()
        {
            // 每次绘制前更新序列化对象，以反映最新的数据
            serializedManager.Update();

            // 使用自定义样式来增强视觉效果
            var boldCenteredLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };

            EditorGUILayout.HelpBox("要编辑道路路径，请从 Unity 主工具栏中选择“道路编辑工具”。", MessageType.Info);
            EditorGUILayout.Space();

            // --- 核心配置 ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("核心配置 (Core Configs)", boldCenteredLabel);
            EditorGUILayout.PropertyField(serializedManager.FindProperty("roadConfig"), new GUIContent("道路配置", "定义道路的剖面、材质和风格。"));
            EditorGUILayout.PropertyField(serializedManager.FindProperty("terrainConfig"), new GUIContent("地形配置", "定义道路如何与地形交互，例如纹理混合。"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- 数据管理 ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("路径数据管理", boldCenteredLabel);
            DrawDataManagementButtons();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // --- 可视化与操作 ---
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("预览与应用", boldCenteredLabel);
            DrawVisibilityToggle();

            EditorGUILayout.Space(5); // 在两个按钮之间增加一点间距

            // 用颜色来区分重要操作
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f); // 淡绿色，表示一个安全的“应用”操作
            DrawApplyTerrainButton();
            GUI.backgroundColor = Color.white; // 恢复默认颜色

            EditorGUILayout.EndVertical();

            // 应用所有通过 PropertyField 修改的属性
            if (serializedManager.ApplyModifiedProperties())
            {
                // 如果属性真的发生了变化，则重新生成道路预览
                roadManager.RegenerateRoad();
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
                        EditorUtility.SetDirty(roadManager); // 标记对象已更改，以便保存
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

        // 这是我们第二步重构过的简洁版本
        private void DrawApplyTerrainButton()
        {
            if (GUILayout.Button(new GUIContent("应用地形修改 (Apply & Hide Mesh)", "..."), GUILayout.Height(35)))
            {
                if (!roadManager.IsReadyForTerrainModification)
                {
                    Debug.LogWarning("无法应用地形修改：请确保 Road Manager 已正确配置...");
                    return;
                }

                // 依然需要设置邻居来处理Unity的自动缝合
                TerrainNeighborManager.UpdateAllTerrainNeighbors();

                // =================================================================
                // [核心修改] 使用全新的统一处理器
                // =================================================================
                var processor = new UnifiedTerrainProcessor();
                processor.Execute(roadManager);
                // =================================================================

                if (roadManager.MeshRenderer != null)
                {
                    Undo.RecordObject(roadManager.MeshRenderer, "Hide Road Mesh After Apply");
                    roadManager.MeshRenderer.enabled = false;
                }
            }
        }
    }
}