// 文件名: RoadEditorTool.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem
{
    [EditorTool("道路编辑工具 (Road Edit Tool)", typeof(RoadManager))]
    public class RoadEditorTool : EditorTool
    {
        #region Fields
        private GUIContent m_ToolIconContent;
        private RoadManager m_RoadManager;
        private SerializedObject m_SerializedManager;
        private SerializedObject m_SerializedConfig;
        private SerializedObject m_SerializedTerrainConfig;
        private SerializedProperty m_ControlPointsProp;
        private SerializedProperty m_LayerProfilesProp;
        private SerializedProperty m_ConformToTerrainProp;
        private SerializedProperty m_TerrainConformityProp;
        private SerializedProperty m_FlattenOffsetProp;
        private ReorderableList m_LayerProfileList;
        private Rect m_SettingsPanelRect = new Rect(10, 10, 320, 420);
        private bool m_IsPanelPositionInitialized = false;
        private int m_HoveredPointIndex = -1;
        #endregion

        #region EditorTool Lifecycle
        void OnEnable()
        {
            m_ToolIconContent = new GUIContent()
            {
                image = EditorGUIUtility.IconContent("d_EditCollider").image,
                text = "道路编辑工具",
                tooltip = "用于创建和编辑道路路径点"
            };
        }

        public override GUIContent toolbarIcon => m_ToolIconContent;

        public override void OnActivated()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            m_IsPanelPositionInitialized = false;
            Initialize();
        }

        public override void OnWillBeDeactivated()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }
        #endregion

        #region Initialization
        private void Initialize()
        {
            m_RoadManager = target as RoadManager;
            if (m_RoadManager == null) return;

            m_SerializedManager = new SerializedObject(m_RoadManager);
            m_ControlPointsProp = m_SerializedManager.FindProperty("controlPoints");

            m_SerializedConfig = m_RoadManager.RoadConfig != null ? new SerializedObject(m_RoadManager.RoadConfig) : null;
            m_SerializedTerrainConfig = m_RoadManager.TerrainConfig != null ? new SerializedObject(m_RoadManager.TerrainConfig) : null;

            if (m_SerializedConfig != null)
            {
                m_LayerProfilesProp = m_SerializedConfig.FindProperty("layerProfiles");
                m_ConformToTerrainProp = m_SerializedConfig.FindProperty("conformToTerrainUndulations");
                m_TerrainConformityProp = m_SerializedConfig.FindProperty("terrainConformity");
                SetupReorderableList();
            }

            if (m_SerializedTerrainConfig != null)
            {
                m_FlattenOffsetProp = m_SerializedTerrainConfig.FindProperty("flattenOffset");
            }
        }

        private void SetupReorderableList()
        {
            m_LayerProfileList = new ReorderableList(m_SerializedConfig, m_LayerProfilesProp, true, true, true, true);
            m_LayerProfileList.drawHeaderCallback = (Rect rect) => EditorGUI.LabelField(rect, "道路分层剖面 (由内向外)");
            m_LayerProfileList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                SerializedProperty element = m_LayerProfileList.serializedProperty.GetArrayElementAtIndex(index);
                rect.y += 2;
                float originalLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 60;
                Rect nameRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
                Rect propsRect = new Rect(rect.x, rect.y + EditorGUIUtility.singleLineHeight + 2, rect.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(nameRect, element.FindPropertyRelative("layerName"), new GUIContent("图层名称"));
                float thirdWidth = propsRect.width / 3f - 5;
                Rect widthRect = new Rect(propsRect.x, propsRect.y, thirdWidth, propsRect.height);
                Rect matRect = new Rect(propsRect.x + thirdWidth + 5, propsRect.y, thirdWidth, propsRect.height);
                Rect offsetRect = new Rect(propsRect.x + (thirdWidth + 5) * 2, propsRect.y, thirdWidth, propsRect.height);
                EditorGUI.PropertyField(widthRect, element.FindPropertyRelative("width"), new GUIContent("宽度"));
                EditorGUI.PropertyField(matRect, element.FindPropertyRelative("meshMaterial"), GUIContent.none);
                EditorGUI.PropertyField(offsetRect, element.FindPropertyRelative("verticalOffset"), new GUIContent("高度偏移"));
                EditorGUIUtility.labelWidth = originalLabelWidth;
            };
            m_LayerProfileList.elementHeightCallback = (index) => (EditorGUIUtility.singleLineHeight + 2) * 2 + 5;
            m_LayerProfileList.onChangedCallback = (list) =>
            {
                m_SerializedConfig.ApplyModifiedProperties();
                m_RoadManager.RegenerateRoad();
            };
        }
        #endregion

        #region Core GUI Loop
        private void OnSceneGUI(SceneView sceneView)
        {
            // ... (Initialization check logic remains the same)

            m_SerializedManager.Update();
            if (m_SerializedConfig != null) m_SerializedConfig.Update();
            if (m_SerializedTerrainConfig != null) m_SerializedTerrainConfig.Update();

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            DrawSceneHandlesAndUI(Event.current);
            HandleSceneInput(Event.current);
            DrawSettingsPanel(sceneView);

            m_SerializedManager.ApplyModifiedProperties();
        }
        #endregion

        #region Drawing Methods
        private void DrawSettingsPanel(SceneView sceneView)
        {
            Handles.BeginGUI();

            // [修复 #1] 设置面板默认位置为右下角
            if (!m_IsPanelPositionInitialized)
            {
                m_SettingsPanelRect.x = sceneView.position.width - m_SettingsPanelRect.width - 10;
                m_SettingsPanelRect.y = sceneView.position.height - m_SettingsPanelRect.height - 40;
                m_IsPanelPositionInitialized = true;
            }

            m_SettingsPanelRect.x = Mathf.Clamp(m_SettingsPanelRect.x, 10, sceneView.position.width - m_SettingsPanelRect.width - 10);
            m_SettingsPanelRect.y = Mathf.Clamp(m_SettingsPanelRect.y, 10, sceneView.position.height - m_SettingsPanelRect.height - 40);

            // [修复 #2] 恢复使用默认窗口风格
            m_SettingsPanelRect = GUILayout.Window(0, m_SettingsPanelRect, (id) =>
            {
                if (m_RoadManager.RoadConfig == null || m_SerializedConfig == null)
                {
                    EditorGUILayout.HelpBox("请先为RoadManager指定一个RoadConfig资产。", MessageType.Warning);
                    GUI.DragWindow();
                    return;
                }

                // [修复 #2] 使用最直接的ChangeCheck方式，确保参数能被修改
                EditorGUI.BeginChangeCheck();

                GUILayout.Label("道路设置", EditorStyles.boldLabel);

                if (m_LayerProfileList != null)
                {
                    m_LayerProfileList.DoLayoutList();
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("道路整体设置", EditorStyles.boldLabel);

                // [修复 #3] 移除slider bar，使用默认字段
                SerializedProperty splineResolutionProp = m_SerializedConfig.FindProperty("splineResolution");
                SerializedProperty edgeWobbleAmountProp = m_SerializedConfig.FindProperty("edgeWobbleAmount");
                SerializedProperty edgeWobbleFrequencyProp = m_SerializedConfig.FindProperty("edgeWobbleFrequency");

                // 使用默认字段，不带slider
                EditorGUILayout.PropertyField(splineResolutionProp, new GUIContent("曲线精度"));
                EditorGUILayout.PropertyField(edgeWobbleAmountProp, new GUIContent("抖动幅度"));
                EditorGUILayout.PropertyField(edgeWobbleFrequencyProp, new GUIContent("抖动频率"));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("地形交互设置", EditorStyles.boldLabel);
                if (m_ConformToTerrainProp != null)
                {
                    EditorGUILayout.PropertyField(m_ConformToTerrainProp, new GUIContent("吸附地形"));
                    if (m_ConformToTerrainProp.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(m_TerrainConformityProp, new GUIContent("吸附度"));
                        EditorGUI.indentLevel--;
                    }
                }
                if (m_FlattenOffsetProp != null)
                {
                    EditorGUILayout.PropertyField(m_FlattenOffsetProp, new GUIContent("压平偏移"));
                }

                // [修复 #5] 只在值真正改变时才应用修改和重新生成道路
                if (EditorGUI.EndChangeCheck())
                {
                    if (m_SerializedConfig != null) m_SerializedConfig.ApplyModifiedProperties();
                    if (m_SerializedTerrainConfig != null) m_SerializedTerrainConfig.ApplyModifiedProperties();
                    m_RoadManager.RegenerateRoad();
                }

                // [修复 #4] 移除交互时变亮效果，保持颜色不变
                GUI.DragWindow();

            }, "道路设置");

            Handles.EndGUI();
        }


        private void DrawSceneHandlesAndUI(Event e)
        {
            List<RoadControlPoint> currentPoints = m_RoadManager.GetControlPointsList();
            UpdateHoveredPoint(e, currentPoints);

            if (m_RoadManager.IsReadyForGeneration)
            {
                DrawSplineCurve(currentPoints);
                DrawAllPathPointHandles(currentPoints);

                if (m_HoveredPointIndex != -1)
                {
                    DrawLayerWidthHandles(m_HoveredPointIndex, currentPoints);
                }

                if (e.shift && !e.control && !e.alt && m_HoveredPointIndex == -1)
                {
                    DrawInsertPreview(e);
                }
            }
        }

        private void DrawAllPathPointHandles(IReadOnlyList<RoadControlPoint> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Handles.color = (i == m_HoveredPointIndex) ? Color.yellow : Color.white;

                float handleSize = HandleUtility.GetHandleSize(points[i].position) * 0.15f;

                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = Handles.FreeMoveHandle(points[i].position, Quaternion.identity, handleSize, Vector3.one * 0.05f, Handles.SphereHandleCap);

                if (EditorGUI.EndChangeCheck())
                {
                    if (m_RoadManager.MeshRenderer != null && !m_RoadManager.MeshRenderer.enabled)
                    {
                        Undo.RecordObject(m_RoadManager.MeshRenderer, "Show Road Mesh");
                        m_RoadManager.MeshRenderer.enabled = true;
                    }

                    Undo.RecordObject(m_RoadManager, "Move Road Point");

                    if (m_ConformToTerrainProp != null && m_ConformToTerrainProp.boolValue)
                    {
                        Terrain terrain = TerrainUtility.GetTerrainAt(newPosition);
                        if (terrain != null)
                        {
                            newPosition.y = terrain.SampleHeight(newPosition) + terrain.transform.position.y;
                        }
                    }

                    MovePoint(i, newPosition);
                    m_RoadManager.RegenerateRoad();
                }
            }
        }

        private void DrawLayerWidthHandles(int pointIndex, IReadOnlyList<RoadControlPoint> points)
        {
            if (m_RoadManager.RoadConfig == null) return;
            RoadControlPoint point = points[pointIndex];
            Vector3 forward = SplineUtility.GetVelocity(points, (float)pointIndex / (points.Count - 1)).normalized;
            if (forward == Vector3.zero) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float currentOffset = 0;
            Handles.color = new Color(1f, 1f, 0f, 0.5f);
            foreach (var profile in m_RoadManager.RoadConfig.layerProfiles)
            {
                float outerOffset = currentOffset + profile.width;
                Vector3 verticalOffset = Vector3.up * profile.verticalOffset;
                Handles.DrawLine(point.position - right * outerOffset + verticalOffset, point.position + right * outerOffset + verticalOffset, 2f);
                currentOffset = outerOffset;
            }
        }

        private void DrawSplineCurve(IReadOnlyList<RoadControlPoint> points)
        {
            if (points.Count < 2 || m_RoadManager.RoadConfig == null) return;
            Handles.color = Color.cyan;
            int drawResolution = m_RoadManager.RoadConfig.splineResolution;
            int totalSegments = (points.Count - 1) * drawResolution;
            for (int i = 0; i < totalSegments; i++)
            {
                float t1 = (float)i / totalSegments;
                float t2 = (float)(i + 1) / totalSegments;
                Vector3 p1 = SplineUtility.GetPoint(points, t1);
                Vector3 p2 = SplineUtility.GetPoint(points, t2);
                Handles.DrawLine(p1, p2, 2.0f);
            }
        }

        private void DrawInsertPreview(Event e)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            (Vector3 closestPoint, int insertIndex) = FindClosestPointOnSpline(worldRay);
            if (insertIndex != -1)
            {
                float handleSize = HandleUtility.GetHandleSize(closestPoint) * 0.12f;
                Handles.color = Color.green;
                Handles.DrawSolidDisc(closestPoint, SceneView.currentDrawingSceneView.camera.transform.forward, handleSize);
            }
        }
        #endregion

        #region Input Handling
        private void HandleSceneInput(Event e)
        {
            if (GUIUtility.hotControl != 0 || e.alt) return;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.shift) ProcessAddPointOnCurve(e);
                else if (e.control) ProcessAddPointAtEnd(e);
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && m_HoveredPointIndex != -1)
            {
                ProcessPointDeletion();
                e.Use();
            }
        }

        private void ProcessPointDeletion()
        {
            if (m_ControlPointsProp.arraySize > 2)
            {
                Undo.RecordObject(m_RoadManager, "Delete Road Point");
                DeletePoint(m_HoveredPointIndex);
                m_RoadManager.RegenerateRoad();
            }
        }

        private void ProcessAddPointOnCurve(Event e)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            (Vector3 closestPoint, int insertIndex) = FindClosestPointOnSpline(worldRay);
            if (insertIndex != -1)
            {
                Undo.RecordObject(m_RoadManager, "Insert Road Point");
                InsertPoint(insertIndex, closestPoint);
                m_RoadManager.RegenerateRoad();
                e.Use();
            }
        }

        private void ProcessAddPointAtEnd(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Undo.RecordObject(m_RoadManager, "Add Road Point");
                InsertPoint(m_ControlPointsProp.arraySize, hit.point);
                m_RoadManager.RegenerateRoad();
                e.Use();
            }
        }
        #endregion

        #region Data & Helpers
        private void UpdateHoveredPoint(Event e, List<RoadControlPoint> points)
        {
            if (GUIUtility.hotControl != 0) return;

            int oldHoveredIndex = m_HoveredPointIndex;
            m_HoveredPointIndex = -1;
            float minPickDistance = 20f;
            for (int i = 0; i < points.Count; i++)
            {
                float dist = HandleUtility.DistanceToCircle(points[i].position, 0);
                if (dist < minPickDistance)
                {
                    minPickDistance = dist;
                    m_HoveredPointIndex = i;
                }
            }

            if (m_HoveredPointIndex != oldHoveredIndex)
            {
                SceneView.RepaintAll();
            }
        }

        private void MovePoint(int index, Vector3 newPosition)
        {
            m_ControlPointsProp.GetArrayElementAtIndex(index).FindPropertyRelative("position").vector3Value = newPosition;
        }

        private void DeletePoint(int index)
        {
            m_ControlPointsProp.DeleteArrayElementAtIndex(index);
            m_HoveredPointIndex = -1;
        }

        private void InsertPoint(int index, Vector3 position)
        {
            m_ControlPointsProp.InsertArrayElementAtIndex(index);
            var newPointProp = m_ControlPointsProp.GetArrayElementAtIndex(index);
            newPointProp.FindPropertyRelative("position").vector3Value = position;
            newPointProp.FindPropertyRelative("rollAngle").floatValue = 0;
        }

        private (Vector3 point, int index) FindClosestPointOnSpline(Ray ray)
        {
            var points = m_RoadManager.GetControlPointsList();
            if (points.Count < 2 || m_RoadManager.RoadConfig == null) return (Vector3.zero, -1);

            float minSqrDist = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            int bestIndex = -1;
            float bestT = 0;

            int segments = points.Count - 1;
            int searchSteps = segments * m_RoadManager.RoadConfig.splineResolution;

            for (int i = 0; i <= searchSteps; i++)
            {
                float t = (searchSteps == 0) ? 0 : (float)i / searchSteps;
                Vector3 p = SplineUtility.GetPoint(points, t);
                float dist = HandleUtility.DistancePointLine(p, ray.origin, ray.origin + ray.direction * 5000);
                if (dist < minSqrDist)
                {
                    minSqrDist = dist;
                    bestPoint = p;
                    bestT = t;
                }
            }
            if (minSqrDist > 20) return (Vector3.zero, -1);
            float scaledT = bestT * (points.Count - 1);
            bestIndex = Mathf.FloorToInt(scaledT) + 1;
            return (bestPoint, bestIndex);
        }
        #endregion
    }
}