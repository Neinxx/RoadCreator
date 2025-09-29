// 文件路径: Assets/RoadCreator/Editor/Tools/RoadEditorTool.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace RoadSystem.Editor
{
    [EditorTool("道路编辑工具 (Road Edit Tool)")]
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
            public static readonly GUIStyle instructionTextStyle;
            
            public static readonly GUIStyle emptyStateBox;
            public static readonly GUIStyle emptyStateHeader;
            public static readonly GUIStyle emptyStateText;
            public static readonly GUIStyle emptyStateButton;

            static Styles()
            {
                instructionBoxStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    alignment = TextAnchor.UpperLeft,
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
                
                emptyStateBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(20, 20, 20, 20),
                    alignment = TextAnchor.MiddleCenter
                };
                emptyStateHeader = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter
                };
                emptyStateText = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                emptyStateButton = new GUIStyle("Button")
                {
                    fontSize = 12,
                    padding = new RectOffset(10, 10, 8, 8),
                    margin = new RectOffset(0, 0, 5, 5)
                };
            }
        }

        public override GUIContent toolbarIcon
        {
            get
            {
                if (m_ToolbarIcon == null)
                {
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
            Selection.selectionChanged += OnSelectionChanged;
            RoadSettingsWindow.OnSettingsChanged += MarkDisplayCacheAsDirty;
            
            UpdateTargetRoadManager();
            EnsureRoadIsVisible();
        }

        public override void OnWillBeDeactivated()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            RoadSettingsWindow.OnSettingsChanged -= MarkDisplayCacheAsDirty;
        }
        
        private void OnSelectionChanged()
        {
            UpdateTargetRoadManager();
            SceneView.RepaintAll();
        }
        
        private void MarkDisplayCacheAsDirty()
        {
            m_DisplayCacheIsDirty = true;
            SceneView.RepaintAll();
        }

        private void UpdateTargetRoadManager()
        {
            RoadManager newManager = null;
            if (Selection.activeGameObject != null)
            {
                newManager = Selection.activeGameObject.GetComponent<RoadManager>();
            }
            
            if (newManager != m_RoadManager)
            {
                m_RoadManager = newManager;
                InitializeSerializedObjects();
            }
        }

        private void InitializeSerializedObjects()
        {
            if (m_RoadManager == null)
            {
                m_SerializedManager = null;
                m_ControlPointsProp = null;
                return;
            }
            
            m_SerializedManager = new SerializedObject(m_RoadManager);
            m_ControlPointsProp = m_SerializedManager.FindProperty("controlPoints");
            MarkDisplayCacheAsDirty();
        }
        #endregion

        #region Core GUI Loop
        private void OnSceneGUI(SceneView sceneView)
        {
            if (m_RoadManager == null)
            {
                DrawEmptyStateGUI();
                return;
            }
            
            using (new SerializedObjectUpdateScope(m_SerializedManager))
            {
                if (m_DisplayCacheIsDirty)
                {
                    RecalculateFinalDisplayCache();
                }

                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

                HandleSceneInput(Event.current);
                DrawSceneHandlesAndUI(Event.current);
                ToolUI();

                if (Event.current.type == EventType.Repaint)
                {
                    m_LastHotControl = GUIUtility.hotControl;
                }
            }
        }
        
        private void DrawEmptyStateGUI()
        {
            Handles.BeginGUI();
            
            Rect viewRect = SceneView.currentDrawingSceneView.position;
            Rect panelRect = new Rect(0, 0, 300, 160);
            panelRect.center = new Vector2(viewRect.width / 2, viewRect.height / 2 - 50);

            GUILayout.BeginArea(panelRect, Styles.emptyStateBox);
            
            GUILayout.Label("道路编辑工具", Styles.emptyStateHeader);
            GUILayout.Space(10);
            GUILayout.Label("请在层级视图中选择一个道路对象 (RoadManager) 以进行编辑。", Styles.emptyStateText);
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("创建一条新道路", Styles.emptyStateButton))
            {
                CreateNewRoad();
            }
            if (GUILayout.Button("打开道路选项窗口", Styles.emptyStateButton))
            {
                RoadSettingsWindow.ShowWindow();
            }
            
            GUILayout.EndArea();
            
            Handles.EndGUI();
        }

        private class SerializedObjectUpdateScope : System.IDisposable
        {
            private readonly SerializedObject m_SerializedObject;
            public SerializedObjectUpdateScope(SerializedObject so)
            {
                m_SerializedObject = so;
                if(m_SerializedObject != null && m_SerializedObject.targetObject != null)
                    m_SerializedObject.Update();
            }
            public void Dispose()
            {
                if(m_SerializedObject != null && m_SerializedObject.targetObject != null)
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
                // 注意: 我将 RoadMeshGenerator 重命名为了 RoadEditorUtility，以符合其功能。
                // 请确保你的 RoadMeshGenerator.cs 文件和类名也同步修改为 RoadEditorUtility.cs 和 RoadEditorUtility。
                m_CachedFinalDisplayPoints.Add(RoadEditorUtility.GetDisplayPoint(points, t, RoadConfig, m_RoadManager.transform));
            }
            m_DisplayCacheIsDirty = false;
        }

        private void DrawSplineCurve()
        {
            if (m_CachedFinalDisplayPoints.Count < 2) return;
            Handles.color = Color.cyan;
            Handles.DrawAAPolyLine(k_LineWidth, m_CachedFinalDisplayPoints.ToArray());
        }

        /// <summary>
        /// [核心修改] 新增一个辅助方法，用于精确计算道路总宽度
        /// </summary>
        private float GetTotalRoadWidth()
        {
            if (RoadConfig == null || RoadConfig.layerProfiles.Count == 0) return 0f;

            // 在独立控制模式下，总宽度由最左和最右的边界决定
            if (RoadConfig.controlLayersIndependently)
            {
                float minOffset = 0;
                float maxOffset = 0;
                foreach (var layer in RoadConfig.layerProfiles)
                {
                    float halfWidth = (layer.width * RoadConfig.globalWidthMultiplier) / 2f;
                    minOffset = Mathf.Min(minOffset, layer.offsetFromCenter - halfWidth);
                    maxOffset = Mathf.Max(maxOffset, layer.offsetFromCenter + halfWidth);
                }
                return maxOffset - minOffset;
            }
            // 在叠加模式下，总宽度是所有层宽度的总和
            else
            {
                return RoadConfig.layerProfiles.Sum(p => p.width) * RoadConfig.globalWidthMultiplier;
            }
        }
        
        private void DrawRoadSurfacePreview()
        {
            if (m_CachedFinalDisplayPoints.Count < 2) return;

            // [核心修改] 使用新的辅助方法获取总宽度
            float totalWidth = GetTotalRoadWidth();
            if (totalWidth <= 0.01f) return;

            var leftEdgePoints = new Vector3[m_CachedFinalDisplayPoints.Count];
            var rightEdgePoints = new Vector3[m_CachedFinalDisplayPoints.Count];

            for (int i = 0; i < m_CachedFinalDisplayPoints.Count; i++)
            {
                Vector3 currentPoint = m_CachedFinalDisplayPoints[i];
                
                Vector3 forward;
                if (i < m_CachedFinalDisplayPoints.Count - 1)
                {
                    forward = (m_CachedFinalDisplayPoints[i + 1] - currentPoint).normalized;
                }
                else 
                {
                    forward = (currentPoint - m_CachedFinalDisplayPoints[i - 1]).normalized;
                }

                if (forward == Vector3.zero) forward = m_RoadManager.transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

                leftEdgePoints[i] = currentPoint - right * totalWidth / 2;
                rightEdgePoints[i] = currentPoint + right * totalWidth / 2;
            }

            Handles.color = new Color(1f, 1f, 0f, 0.4f);
            Handles.DrawAAPolyLine(k_LineWidth - 1f, leftEdgePoints);
            Handles.DrawAAPolyLine(k_LineWidth - 1f, rightEdgePoints);
        }

        private void DrawAllPathPointHandles(IReadOnlyList<RoadControlPoint> localPoints)
        {
            if (localPoints == null || localPoints.Count == 0) return;
            Transform managerTransform = m_RoadManager.transform;
            for (int i = 0; i < localPoints.Count; i++)
            {
                float t = localPoints.Count > 1 ? (float)i / (localPoints.Count - 1) : 0f;
                Vector3 displayWorldPos = RoadEditorUtility.GetDisplayPoint(localPoints, t, RoadConfig, managerTransform);
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

            bool isHotControlReleased = m_LastHotControl != 0 && GUIUtility.hotControl == 0;
            if (m_IsDraggingHandle && isHotControlReleased)
            {
                m_IsDraggingHandle = false;
                m_HoveredPointIndex = -1;
                MarkDisplayCacheAsDirty();
                m_RoadManager.RegenerateRoad();
                SceneView.RepaintAll(); 
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

                if (m_IsDraggingHandle)
                {
                    DrawRoadSurfacePreview();
                }

                DrawAllPathPointHandles(currentPoints);
                if (m_HoveredPointIndex != -1 && !m_IsDraggingHandle)
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
            if (GUIUtility.hotControl != 0 || localPoints == null) return;
            int oldHoveredIndex = m_HoveredPointIndex;
            m_HoveredPointIndex = -1;
            if (localPoints.Count == 0) return;
            float minPickDistance = k_HandlePickDistance;
            for (int i = 0; i < localPoints.Count; i++)
            {
                float t = localPoints.Count > 1 ? (float)i / (localPoints.Count - 1) : 0f;
                Vector3 worldPos = RoadEditorUtility.GetDisplayPoint(localPoints, t, RoadConfig, m_RoadManager.transform);
                float dist = HandleUtility.DistanceToCircle(worldPos, k_HandleSizeMultiplier);
                if (dist < minPickDistance)
                {
                    minPickDistance = dist;
                    m_HoveredPointIndex = i;
                }
            }
            if (m_HoveredPointIndex != oldHoveredIndex) SceneView.RepaintAll();
        }

        /// <summary>
        /// [核心修改] 重写此方法，使用2D屏幕坐标进行拾取，解决3D视图下的点击不准问题
        /// </summary>
        private (Vector3 point, int index, float t) FindClosestPointOnSpline(Ray unusedRay)
        {
            if (m_CachedFinalDisplayPoints.Count < 2) return (Vector3.zero, -1, 0);

            Vector2 mousePos = Event.current.mousePosition;
            float minDistanceSqr = float.MaxValue;
            int closestSegmentIndex = -1;
            float bestSegmentT = 0;

            for (int i = 0; i < m_CachedFinalDisplayPoints.Count - 1; i++)
            {
                // 将3D线段的起点和终点投影到2D屏幕
                Vector2 p1_2D = HandleUtility.WorldToGUIPoint(m_CachedFinalDisplayPoints[i]);
                Vector2 p2_2D = HandleUtility.WorldToGUIPoint(m_CachedFinalDisplayPoints[i + 1]);

                // 计算2D空间中鼠标到线段的最近点
                Vector2 closestPoint2D = ProjectPointOnLineSegment(mousePos, p1_2D, p2_2D);
                float distSqr = (mousePos - closestPoint2D).sqrMagnitude;

                if (distSqr < minDistanceSqr)
                {
                    minDistanceSqr = distSqr;
                    closestSegmentIndex = i;
                    
                    // 计算这个最近点在线段上的比例 (t)
                    float segmentLength = (p2_2D - p1_2D).magnitude;
                    if (segmentLength > 0.001f)
                    {
                        bestSegmentT = (closestPoint2D - p1_2D).magnitude / segmentLength;
                    }
                    else
                    {
                        bestSegmentT = 0;
                    }
                }
            }
            
            // 如果最近距离大于拾取阈值，则认为没有点中
            if (Mathf.Sqrt(minDistanceSqr) > k_HandlePickDistance) return (Vector3.zero, -1, 0);

            // 使用在2D空间计算出的比例(t)，在3D空间中插值得到精确的3D世界坐标
            Vector3 worldPos = Vector3.Lerp(m_CachedFinalDisplayPoints[closestSegmentIndex], m_CachedFinalDisplayPoints[closestSegmentIndex + 1], bestSegmentT);

            var controlPoints = m_RoadManager.GetControlPointsList();
            int controlSegments = controlPoints.Count - 1;
            if (controlSegments <= 0) return (Vector3.zero, -1, 0);

            // 计算全局的 t 值
            int totalDisplaySegments = m_CachedFinalDisplayPoints.Count - 1;
            float globalT = (closestSegmentIndex + bestSegmentT) / totalDisplaySegments;

            // 计算最终的插入索引
            int insertionIndex = Mathf.FloorToInt(globalT * controlSegments) + 1;

            return (worldPos, insertionIndex, globalT);
        }

        // 修改为2D版本
        private Vector2 ProjectPointOnLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 lineDir = lineEnd - lineStart;
            float lineSqrMag = lineDir.sqrMagnitude;
            if (lineSqrMag < 0.00001f) return lineStart;
            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, lineDir) / lineSqrMag);
            return lineStart + lineDir * t;
        }

        /// <summary>
        /// [核心修改] 此方法现在只绘制最终视觉宽度的两条线
        /// </summary>
        private void DrawLayerWidthHandles(int pointIndex, IReadOnlyList<RoadControlPoint> points)
        {
            if (RoadConfig == null) return;
            
            float totalWidth = GetTotalRoadWidth();
            if (totalWidth <= 0.01f) return;

            float t = (points.Count > 1) ? (float)pointIndex / (points.Count - 1) : 0f;
            Vector3 displayPos = RoadEditorUtility.GetDisplayPoint(points, t, RoadConfig, m_RoadManager.transform);
            Vector3 localForward = SplineUtility.GetVelocity(points, t).normalized;
            Vector3 worldForward = m_RoadManager.transform.TransformDirection(localForward);
            if (worldForward == Vector3.zero) worldForward = m_RoadManager.transform.forward;
            
            Vector3 right = Vector3.Cross(Vector3.up, worldForward).normalized;
            
            Vector3 leftPoint = displayPos - right * totalWidth / 2;
            Vector3 rightPoint = displayPos + right * totalWidth / 2;

            Handles.color = new Color(1f, 1f, 0f, 0.8f);
            Handles.DrawAAPolyLine(k_LineWidth, leftPoint, rightPoint);
        }

        private void DrawInsertPreview(Event e)
        {
            // 使用空的Ray，因为新方法不再需要它
            (Vector3 closestPoint, int insertIndex, float t) = FindClosestPointOnSpline(new Ray());
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
            (Vector3 closestPointWorld, int insertIndex, float t) = FindClosestPointOnSpline(new Ray());
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
            if (m_ControlPointsProp == null) return;
            m_ControlPointsProp.GetArrayElementAtIndex(index).FindPropertyRelative("position").vector3Value = newLocalPosition;
        }

        private void InsertPoint(int index, Vector3 localPosition)
        {
            if (m_ControlPointsProp == null) return;
            m_ControlPointsProp.InsertArrayElementAtIndex(index);
            var newPointProp = m_ControlPointsProp.GetArrayElementAtIndex(index);
            newPointProp.FindPropertyRelative("position").vector3Value = localPosition;
            newPointProp.FindPropertyRelative("rollAngle").floatValue = 0;
        }

        private void DeletePoint(int index)
        {
            if (m_ControlPointsProp == null) return;
            m_ControlPointsProp.DeleteArrayElementAtIndex(index);
            m_HoveredPointIndex = -1;
        }
        
        private void CreateNewRoad()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                EditorUtility.DisplayDialog("错误", "无法创建道路，请先打开一个场景视图(Scene View)。", "好的");
                return;
            }

            var ray = sceneView.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            Vector3 startPoint = Vector3.zero;

            if (Physics.Raycast(ray, out var hit))
            {
                startPoint = hit.point;
            }
            else if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var enter))
            {
                startPoint = ray.GetPoint(enter);
            }

            var roadObject = new GameObject("New Road");
            Undo.RegisterCreatedObjectUndo(roadObject, "Create New Road");
            roadObject.transform.position = startPoint;

            var roadManager = Undo.AddComponent<RoadManager>(roadObject);

            var so = new SerializedObject(roadManager);
            so.FindProperty("roadConfig").objectReferenceValue = GetOrCreateDefaultConfig<RoadConfig>("DefaultRoadConfig");
            so.FindProperty("terrainConfig").objectReferenceValue = GetOrCreateDefaultConfig<TerrainConfig>("DefaultTerrainConfig");
            so.ApplyModifiedProperties();

            Selection.activeGameObject = roadObject;
            EditorGUIUtility.PingObject(roadObject);

            EditorApplication.delayCall += roadManager.RegenerateRoad;
        }
        
        private T GetOrCreateDefaultConfig<T>(string fileName) where T : ScriptableObject
        {
            string defaultConfigPath = "Assets/RoadCreator/Editor/DefaultConfigs";
            string assetPath = $"{defaultConfigPath}/{fileName}.asset";

            if (!Directory.Exists(defaultConfigPath))
            {
                Directory.CreateDirectory(defaultConfigPath);
            }

            T config = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (config == null)
            {
                config = CreateInstance<T>();
                AssetDatabase.CreateAsset(config, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"已创建默认配置文件: {assetPath}");
            }
            return config;
        }
        #endregion
    }
}