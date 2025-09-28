using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using System.Collections.Generic;
using System.Linq;

namespace RoadSystem.Editor
{
    [EditorTool("道路编辑工具 (Road Edit Tool)", typeof(RoadManager))]
    public class RoadEditorTool : EditorTool
    {
        #region Fields & Constants
        private RoadManager m_RoadManager;
        private SerializedObject m_SerializedManager;
        private SerializedProperty m_ControlPointsProp;
        private static GUIContent m_ToolbarIcon;

        private RoadConfig RoadConfig => m_RoadManager?.RoadConfig;

        private int m_HoveredPointIndex = -1;
        private bool m_IsDraggingHandle = false;

        // [新增] 用于检测hotControl的变化，从而精确捕捉拖拽结束的瞬间
        private int m_LastHotControl = 0;

        private readonly List<Vector3> m_CachedFinalDisplayPoints = new List<Vector3>();
        private bool m_DisplayCacheIsDirty = true;

        private const float k_HandleSizeMultiplier = 0.15f;
        private const float k_HandlePickDistance = 20f;
        private const float k_LineWidth = 2.5f;

        #endregion

        #region Styles
        private static class Styles
        {
            public static readonly GUIStyle instructionBoxStyle;
            public static readonly GUIContent instructionTitle;
            public static readonly string instructionText;
            public static readonly GUIStyle instructionTextStyle; // 新增样式用于自定义文本样式

            static Styles()
            {
                instructionBoxStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    alignment = TextAnchor.UpperLeft,
                };


                GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16, // 调整标题字体大小
                    wordWrap = true
                };
                instructionTitle = new GUIContent(" 道路编辑提示");

                instructionText =
                    "• <b>移动点:</b> 拖拽白色控制球。\n" +
                    "• <b>添加点 (末尾):</b> 按住 <b>Ctrl</b> + 左键点击地面。\n" +
                    "• <b>插入点 (中间):</b> 按住 <b>Shift</b> + 移动鼠标预览, 左键点击确认。\n" +
                    "• <b>删除点:</b> 鼠标悬停于点上，按 <b>Delete</b> 键或<b>右键点击</b>。";


                instructionTextStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 13,
                    richText = true,
                    wordWrap = true
                };
            }
        }

        public override GUIContent toolbarIcon
        {
            get
            {
                if (m_ToolbarIcon == null)
                {
                    // 使用节点图标作为工具栏图标
                    Texture icon = EditorGUIUtility.IconContent("ParticleSystem Icon").image ??
                                  EditorGUIUtility.IconContent("SphereCollider Icon").image ??
                                  EditorGUIUtility.FindTexture("d_Toolbar Plus") ??
                                  EditorGUIUtility.FindTexture("Toolbar Plus");

                    m_ToolbarIcon = new GUIContent(icon, "道路编辑工具 (Road Edit Tool)");
                }
                return m_ToolbarIcon;
            }
        }
        #endregion

        #region Lifecycle
        public override void OnActivated()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Initialize();
            RoadSettingsWindow.OnSettingsChanged += MarkDisplayCacheAsDirty;
            EnsureRoadIsVisible();
        }

        public override void OnWillBeDeactivated()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            RoadSettingsWindow.OnSettingsChanged -= MarkDisplayCacheAsDirty;
        }

        private void MarkDisplayCacheAsDirty()
        {
            m_DisplayCacheIsDirty = true;
            SceneView.RepaintAll();
        }

        private void Initialize()
        {
            m_RoadManager = target as RoadManager;
            if (m_RoadManager == null) return;
            m_SerializedManager = new SerializedObject(m_RoadManager);
            m_ControlPointsProp = m_SerializedManager.FindProperty("controlPoints");
            MarkDisplayCacheAsDirty();
        }
        #endregion

        #region Core GUI Loop
        private void OnSceneGUI(SceneView sceneView)
        {
            if (m_RoadManager == null) { Initialize(); if (m_RoadManager == null) return; }

            using (var serializedObjectScope = new SerializedObjectUpdateScope(m_SerializedManager))
            {
                if (m_DisplayCacheIsDirty)
                {
                    RecalculateFinalDisplayCache();
                }

                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

                HandleSceneInput(Event.current);
                DrawSceneHandlesAndUI(Event.current);
                ToolUI();

                // [新增] 在所有GUI事件处理结束后，记录当前的hotControl，用于下一帧的比较
                if (Event.current.type == EventType.Repaint)
                {
                    m_LastHotControl = GUIUtility.hotControl;
                }
            }
        }

        private class SerializedObjectUpdateScope : System.IDisposable
        {
            private readonly SerializedObject m_SerializedObject;
            public SerializedObjectUpdateScope(SerializedObject so)
            {
                m_SerializedObject = so;
                m_SerializedObject.Update();
            }
            public void Dispose()
            {
                m_SerializedObject.ApplyModifiedProperties();
            }
        }
        #endregion

        #region Drawing & Cache
        private void RecalculateFinalDisplayCache()
        {
            m_CachedFinalDisplayPoints.Clear();
            if (m_RoadManager == null || !m_RoadManager.IsReadyForGeneration)
            {
                m_DisplayCacheIsDirty = false;
                return;
            }
            var points = m_RoadManager.GetControlPointsList();
            if (points.Count < 2) { m_DisplayCacheIsDirty = false; return; }
            int drawResolution = RoadConfig.splineResolution;
            int totalSegments = (points.Count - 1) * drawResolution;
            if (totalSegments <= 0) totalSegments = 1;
            for (int i = 0; i <= totalSegments; i++)
            {
                float t = (float)i / totalSegments;
                m_CachedFinalDisplayPoints.Add(RoadMeshGenerator.GetDisplayPoint(points, t, RoadConfig, m_RoadManager.transform));
            }
            m_DisplayCacheIsDirty = false;
        }

        private void DrawSplineCurve()
        {
            if (m_CachedFinalDisplayPoints.Count < 2) return;
            Handles.color = Color.cyan;
            Handles.DrawAAPolyLine(k_LineWidth, m_CachedFinalDisplayPoints.ToArray());
        }

        private void DrawAllPathPointHandles(IReadOnlyList<RoadControlPoint> localPoints)
        {
            if (localPoints.Count == 0) return;
            Transform managerTransform = m_RoadManager.transform;
            for (int i = 0; i < localPoints.Count; i++)
            {
                float t = localPoints.Count > 1 ? (float)i / (localPoints.Count - 1) : 0f;
                Vector3 displayWorldPos = RoadMeshGenerator.GetDisplayPoint(localPoints, t, RoadConfig, managerTransform);
                Handles.color = (i == m_HoveredPointIndex) ? Color.yellow : Color.white;
                float handleSize = HandleUtility.GetHandleSize(displayWorldPos) * k_HandleSizeMultiplier;
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    Vector3 newWorldPos = Handles.FreeMoveHandle(displayWorldPos, Quaternion.identity, handleSize, Vector3.one * 0.05f, Handles.SphereHandleCap);
                    if (check.changed)
                    {
                        if (!m_IsDraggingHandle)
                        {
                            m_IsDraggingHandle = true;
                            Undo.RecordObject(m_RoadManager, "Move Road Point");
                        }
                        EnsureRoadIsVisible();
                        Vector3 newLocalPos = managerTransform.InverseTransformPoint(newWorldPos);
                        Vector3 originalLocalPos = localPoints[i].position;
                        if (!RoadConfig.conformToTerrainUndulations)
                        {
                            originalLocalPos.y = newLocalPos.y;
                        }
                        originalLocalPos.x = newLocalPos.x;
                        originalLocalPos.z = newLocalPos.z;
                        MovePoint(i, originalLocalPos);
                        MarkDisplayCacheAsDirty();
                        m_RoadManager.RegenerateRoad();
                    }
                }
            }
        }
        #endregion

        #region Input Handling
        private void HandleSceneInput(Event e)
        {
            if (e.alt) return;

            // =================================================================================
            // [核心修正] 重构拖拽结束逻辑：不再依赖MouseUp事件，而是检测hotControl的变化。
            // 这是最可靠的方式，能确保在手柄被释放的“那一刻”执行清理逻辑。
            // =================================================================================
            bool isHotControlReleased = m_LastHotControl != 0 && GUIUtility.hotControl == 0;
            if (m_IsDraggingHandle && isHotControlReleased)
            {
                m_IsDraggingHandle = false;
                m_HoveredPointIndex = -1;
                SceneView.RepaintAll(); // 请求重绘以清除高亮
            }

            if (e.type == EventType.MouseMove && e.shift)
            {
                SceneView.RepaintAll();
            }

            if (GUIUtility.hotControl != 0) return;

            if (e.type == EventType.MouseDown && e.button == 1 && m_HoveredPointIndex != -1)
            {
                ProcessPointDeletion();
                e.Use();
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && m_HoveredPointIndex != -1)
            {
                ProcessPointDeletion();
                e.Use();
            }
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.shift && !e.control)
                {
                    ProcessAddPointOnCurve(e);
                }
                else if (e.control && !e.shift)
                {
                    ProcessAddPointAtEnd(e);
                }
            }
        }
        #endregion

        #region Helper Methods
        private void DrawSceneHandlesAndUI(Event e)
        {
            List<RoadControlPoint> currentPoints = m_RoadManager.GetControlPointsList();
            UpdateHoveredPoint(e, currentPoints);
            if (m_RoadManager.IsReadyForGeneration)
            {
                DrawSplineCurve();
                DrawAllPathPointHandles(currentPoints);
                if (m_HoveredPointIndex != -1)
                {
                    DrawLayerWidthHandles(m_HoveredPointIndex, currentPoints);
                }
                if (e.shift && !e.control)
                {
                    DrawInsertPreview(e);
                }
            }
        }

        private void ToolUI()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(46, 2, 390, 110), Styles.instructionBoxStyle);
            EditorGUILayout.LabelField(Styles.instructionTitle, new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
            EditorGUILayout.LabelField(Styles.instructionText, Styles.instructionTextStyle);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private void UpdateHoveredPoint(Event e, List<RoadControlPoint> localPoints)
        {
            if (GUIUtility.hotControl != 0) return;
            int oldHoveredIndex = m_HoveredPointIndex;
            m_HoveredPointIndex = -1;
            if (localPoints.Count == 0) return;
            float minPickDistance = k_HandlePickDistance;
            for (int i = 0; i < localPoints.Count; i++)
            {
                float t = localPoints.Count > 1 ? (float)i / (localPoints.Count - 1) : 0f;
                Vector3 worldPos = RoadMeshGenerator.GetDisplayPoint(localPoints, t, RoadConfig, m_RoadManager.transform);
                float dist = HandleUtility.DistanceToCircle(worldPos, k_HandleSizeMultiplier);
                if (dist < minPickDistance)
                {
                    minPickDistance = dist;
                    m_HoveredPointIndex = i;
                }
            }
            if (m_HoveredPointIndex != oldHoveredIndex) SceneView.RepaintAll();
        }

        private (Vector3 point, int index, float t) FindClosestPointOnSpline(Ray unusedRay)
        {
            if (m_CachedFinalDisplayPoints.Count < 2) return (Vector3.zero, -1, 0);
            float minDistance = float.MaxValue;
            int closestSegmentIndex = -1;
            for (int i = 0; i < m_CachedFinalDisplayPoints.Count - 1; i++)
            {
                Vector3 p1 = m_CachedFinalDisplayPoints[i];
                Vector3 p2 = m_CachedFinalDisplayPoints[i + 1];
                float distance = HandleUtility.DistanceToLine(p1, p2);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestSegmentIndex = i;
                }
            }
            if (minDistance > k_HandlePickDistance) return (Vector3.zero, -1, 0);
            Vector3 segmentStart = m_CachedFinalDisplayPoints[closestSegmentIndex];
            Vector3 segmentEnd = m_CachedFinalDisplayPoints[closestSegmentIndex + 1];
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            Vector3 closestPoint3D = ProjectPointOnLineSegment(mouseRay.origin, segmentStart, segmentEnd);
            var controlPoints = m_RoadManager.GetControlPointsList();
            int controlSegments = controlPoints.Count - 1;
            if (controlSegments <= 0) return (Vector3.zero, -1, 0);
            int totalDisplaySegments = m_CachedFinalDisplayPoints.Count - 1;
            float segmentLength = Vector3.Distance(segmentStart, segmentEnd);
            float distFromStart = Vector3.Distance(segmentStart, closestPoint3D);
            float localT = (segmentLength > 0.0001f) ? distFromStart / segmentLength : 0f;
            float globalT = (closestSegmentIndex + localT) / totalDisplaySegments;
            int insertionIndex = Mathf.FloorToInt(globalT * controlSegments) + 1;
            return (closestPoint3D, insertionIndex, globalT);
        }

        private Vector3 ProjectPointOnLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
        {
            Vector3 lineDir = lineEnd - lineStart;
            float lineSqrMag = lineDir.sqrMagnitude;
            if (lineSqrMag < 0.00001f) return lineStart;
            float t = Vector3.Dot(point - lineStart, lineDir) / lineSqrMag;
            t = Mathf.Clamp01(t);
            return lineStart + lineDir * t;
        }

        private void DrawLayerWidthHandles(int pointIndex, IReadOnlyList<RoadControlPoint> points)
        {
            if (RoadConfig == null) return;
            float t = (points.Count > 1) ? (float)pointIndex / (points.Count - 1) : 0f;
            Vector3 displayPos = RoadMeshGenerator.GetDisplayPoint(points, t, RoadConfig, m_RoadManager.transform);
            Vector3 localForward = SplineUtility.GetVelocity(points, t).normalized;
            Vector3 worldForward = m_RoadManager.transform.TransformDirection(localForward);
            if (worldForward == Vector3.zero) worldForward = m_RoadManager.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, worldForward).normalized;
            float currentOffset = 0;
            Handles.color = new Color(1f, 1f, 0f, 0.5f);
            foreach (var profile in RoadConfig.layerProfiles)
            {
                float outerOffset = currentOffset + profile.width;
                Vector3 verticalOffset = Vector3.up * profile.verticalOffset;
                Handles.DrawLine(displayPos - right * outerOffset + verticalOffset, displayPos + right * outerOffset + verticalOffset, 2f);
                currentOffset = outerOffset;
            }
        }

        private void DrawInsertPreview(Event e)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            (Vector3 closestPoint, int insertIndex, float t) = FindClosestPointOnSpline(worldRay);
            if (insertIndex != -1)
            {
                float handleSize = HandleUtility.GetHandleSize(closestPoint) * 0.12f;
                Handles.color = Color.green;
                Handles.DrawSolidDisc(closestPoint, SceneView.currentDrawingSceneView.camera.transform.forward, handleSize);
            }
        }

        private void EnsureRoadIsVisible()
        {
            if (m_RoadManager != null && m_RoadManager.MeshRenderer != null)
            {
                if (!m_RoadManager.MeshRenderer.enabled)
                {
                    m_RoadManager.MeshRenderer.enabled = true;
                }
            }
        }

        private void ProcessPointDeletion()
        {
            if (m_ControlPointsProp.arraySize <= 2)
            {
                EditorApplication.Beep();
                Debug.LogWarning("道路至少需要两个点。");
                return;
            }
            Undo.RecordObject(m_RoadManager, "Delete Road Point");
            EnsureRoadIsVisible();
            DeletePoint(m_HoveredPointIndex);
            m_RoadManager.RegenerateRoad();
            MarkDisplayCacheAsDirty();
        }

        private void ProcessAddPointOnCurve(Event e)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            (Vector3 closestPointWorld, int insertIndex, float t) = FindClosestPointOnSpline(worldRay);
            if (insertIndex != -1)
            {
                insertIndex = Mathf.Clamp(insertIndex, 1, m_ControlPointsProp.arraySize);
                Undo.RecordObject(m_RoadManager, "Insert Road Point");
                EnsureRoadIsVisible();
                Vector3 localInsertionPos = SplineUtility.GetPoint(m_RoadManager.GetControlPointsList(), t);
                InsertPoint(insertIndex, localInsertionPos);
                m_RoadManager.RegenerateRoad();
                MarkDisplayCacheAsDirty();
                e.Use();
            }
        }

        private void ProcessAddPointAtEnd(Event e)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Undo.RecordObject(m_RoadManager, "Add Road Point");
                EnsureRoadIsVisible();
                Vector3 hitPointLocal = m_RoadManager.transform.InverseTransformPoint(hit.point);
                InsertPoint(m_ControlPointsProp.arraySize, hitPointLocal);
                m_RoadManager.RegenerateRoad();
                MarkDisplayCacheAsDirty();
                e.Use();
            }
        }
        private void MovePoint(int index, Vector3 newLocalPosition)
        {
            m_ControlPointsProp.GetArrayElementAtIndex(index).FindPropertyRelative("position").vector3Value = newLocalPosition;
        }

        private void InsertPoint(int index, Vector3 localPosition)
        {
            m_ControlPointsProp.InsertArrayElementAtIndex(index);
            var newPointProp = m_ControlPointsProp.GetArrayElementAtIndex(index);
            newPointProp.FindPropertyRelative("position").vector3Value = localPosition;
            newPointProp.FindPropertyRelative("rollAngle").floatValue = 0;
        }

        private void DeletePoint(int index)
        {
            m_ControlPointsProp.DeleteArrayElementAtIndex(index);
            m_HoveredPointIndex = -1;
        }
        #endregion
    }
}