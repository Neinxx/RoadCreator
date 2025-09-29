// 文件路径: Assets/RoadCreator/Editor/RoadSettingsWindow.cs
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace RoadSystem.Editor
{
    /// <summary>
    /// 提供一个编辑器窗口，用于管理和编辑场景中所有 RoadManager 对象的设置。
    /// </summary>
    public class RoadSettingsWindow : EditorWindow
    {
        public static event Action OnSettingsChanged;

        #region 静态样式与常量
        private static class Styles
        {
            // --- 新增的核心样式 ---
            public static readonly GUIStyle welcomeBox;
            public static readonly GUIStyle primaryButton;
            public static readonly GUIStyle iconLabel;
            public static readonly GUIStyle bodyLabel;

            // --- 原有样式的调整 ---
            public static readonly GUIStyle headerLabel;
            public static readonly GUIStyle sectionBox;
            public static readonly GUIStyle selectedListItem;

            // --- GUIContent (补全所有) ---
            public static readonly GUIContent createRoadButtonContent;
            public static readonly GUIContent roadSettingsHeader;
            public static readonly GUIContent terrainSettingsHeader;
            public static readonly GUIContent splineResolutionContent;
            public static readonly GUIContent uvModeContent;
            public static readonly GUIContent uvScalingContent;
            public static readonly GUIContent selectButtonIcon;
            public static readonly GUIContent refreshButtonIcon;
            
            public static readonly GUIContent conformToTerrainContent;
            public static readonly GUIContent terrainConformityContent;
            public static readonly GUIContent flattenOffsetContent;
            public static readonly GUIContent flattenStrengthContent;
            
            static Styles()
            {
                // --- 1. 定义核心视觉元素 (美化布局部分) ---
                welcomeBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(20, 20, 20, 20), margin = new RectOffset(10, 10, 10, 10) };
                iconLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fixedHeight = 80, fontSize = 60 };
                headerLabel = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                bodyLabel = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.gray } };
                primaryButton = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 15, 0), normal = { textColor = new Color(0.9f, 0.95f, 1f) } };
                selectedListItem = new GUIStyle(EditorStyles.toolbarButton) { fontStyle = FontStyle.Bold, border = new RectOffset(5, 2, 2, 2) };
                sectionBox = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(5, 5, 5, 5) };

                // --- 2. 定义 GUIContent (文本和图标) ---
                
                // [核心修改] 将图标从 "d_Toolbar Plus More" 更换为 "d_Toolbar Plus"，以移除下拉箭头
                createRoadButtonContent = new GUIContent(" 创建新道路 (Create New Road)", EditorGUIUtility.IconContent("d_Toolbar Plus").image, "在场景中心创建一条新的道路。");
                
                refreshButtonIcon = new GUIContent(EditorGUIUtility.IconContent("d_Refresh").image, "刷新场景中的道路列表");

                Texture roadSettingsIcon = EditorGUIUtility.IconContent("d_Settings").image ?? EditorGUIUtility.FindTexture("Settings Icon");
                roadSettingsHeader = new GUIContent(" 道路整体设置", roadSettingsIcon);

                Texture terrainSettingsIcon = EditorGUIUtility.IconContent("d_Terrain Icon").image ?? EditorGUIUtility.FindTexture("TerrainAsset Icon");
                terrainSettingsHeader = new GUIContent(" 地形交互设置", terrainSettingsIcon);
                
                splineResolutionContent = new GUIContent("曲线精度", "数值越小，曲线越平滑，但性能开销越大。");
                uvModeContent = new GUIContent("UV模式", "选择UV坐标的生成方式：Tiled 适用于重复纹理，WorldSpace 适用于基于世界空间的投影。");
                uvScalingContent = new GUIContent("UV缩放", "在 WorldSpace 模式下，控制纹理在世界空间中的大小。");
                selectButtonIcon = new GUIContent(EditorGUIUtility.IconContent("Search").image, "在场景中高亮并选中此道路");

                conformToTerrainContent = new GUIContent("吸附地形", "使道路的控制点自动贴合到地形表面。");
                terrainConformityContent = new GUIContent("吸附度", "控制道路样条曲线跟随地形起伏的程度。");
                flattenOffsetContent = new GUIContent("压平偏移", "在地形上压平路基时，相对于道路中心点的垂直偏移。");
                flattenStrengthContent = new GUIContent("压平强度", "控制地形压平效果的强度。");
            }
        }
        #endregion

        #region 成员字段
        private RoadManager m_CurrentRoadManager;
        private List<RoadManager> m_SceneRoads;
        private readonly Dictionary<RoadManager, float> m_roadLengthsCache = new Dictionary<RoadManager, float>();
        private SerializedObject m_SerializedManager;
        private SerializedObject m_SerializedConfig;
        private SerializedObject m_SerializedTerrainConfig;
        private SerializedProperty m_GlobalWidthProp;
        private SerializedProperty m_GlobalWobbleFreqProp;
        private SerializedProperty m_GlobalWobbleAmpProp;
        private SerializedProperty m_IndependentLayersProp;
        private SerializedProperty m_LayerProfilesProp;
        private SerializedProperty m_SplineResolutionProp;
        private SerializedProperty m_UvGenerationModeProp;
        private SerializedProperty m_WorldUVScalingProp;
        private SerializedProperty m_ConformToTerrainProp;
        private SerializedProperty m_TerrainConformityProp;
        private SerializedProperty m_VerticalSmoothnessProp;
        private SerializedProperty m_SmoothIterationsProp;
        private SerializedProperty m_FlattenOffsetProp;
        private SerializedProperty m_FlattenStrengthProp;
        private ReorderableList m_LayerProfileList;
        private Vector2 m_ScrollPosition;
        private bool m_RoadListDirty = true;
        private bool m_isRoadListExpanded = true;
        #endregion

        #region 窗口入口
        [MenuItem("Tools/Road Creator/Road Options")]
        public static void ShowWindow()
        {
            GetWindow<RoadSettingsWindow>("Road Options");
        }
        #endregion

        #region Unity 生命周期方法
        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += MarkRoadListAsDirty;
            m_RoadListDirty = true;
            OnSelectionChanged(); // 立即更新一次以反映当前选择
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= MarkRoadListAsDirty;
        }

        private void OnGUI()
        {
            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            DrawEditorPanel();
            EditorGUILayout.Separator();
            DrawSceneRoadsList();
            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region 主 GUI 绘制逻辑
        private void DrawEditorPanel()
        {
            EditorGUILayout.BeginVertical(Styles.sectionBox);
            if (m_CurrentRoadManager != null)
            {
                DrawEditorForSelectedRoad();
            }
            else
            {
                DrawEmptyStatePanel();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyStatePanel()
        {
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(Styles.welcomeBox, GUILayout.MaxWidth(350));
            
            var icon = EditorGUIUtility.IconContent("d_UnityEditor.SceneView").image;
            GUILayout.Label(icon, Styles.iconLabel);
            EditorGUILayout.Space(10);
            
            EditorGUILayout.LabelField("尚未选择道路", Styles.headerLabel);
            EditorGUILayout.Space(5);
            
            EditorGUILayout.LabelField("从下方列表选择一条道路进行编辑，或创建一条新路。", Styles.bodyLabel);
            EditorGUILayout.Space(20);
            
            Color originalBgColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.25f, 0.5f, 0.9f, 1f); // 设置一个柔和的蓝色主题
            if (GUILayout.Button(Styles.createRoadButtonContent, Styles.primaryButton, GUILayout.Height(40)))
            {
                CreateNewRoad();
            }
            GUI.backgroundColor = originalBgColor; // 恢复背景色

            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }
        
        private void DrawEditorForSelectedRoad()
        {
            if (m_SerializedConfig != null) m_SerializedConfig.Update();
            if (m_SerializedTerrainConfig != null) m_SerializedTerrainConfig.Update();
            if (m_SerializedManager != null) m_SerializedManager.Update();

            EditorGUILayout.LabelField("当前编辑: " + m_CurrentRoadManager.name, Styles.headerLabel);
            EditorGUILayout.Separator();

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                DrawRoadConfigSection();
                EditorGUILayout.Space(10);
                DrawTerrainInteractionSection();

                if (check.changed)
                {
                    if (m_SerializedConfig != null) m_SerializedConfig.ApplyModifiedProperties();
                    if (m_SerializedTerrainConfig != null) m_SerializedTerrainConfig.ApplyModifiedProperties();
                    m_CurrentRoadManager.RegenerateRoad();
                    OnSettingsChanged?.Invoke();
                }
            }
        }
        
        private void DrawRoadConfigSection()
        {
            EditorGUILayout.LabelField(Styles.roadSettingsHeader, EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (m_CurrentRoadManager.RoadConfig != null && m_SerializedConfig != null)
            {
                EditorGUILayout.LabelField("全局整体控制", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                if (m_GlobalWidthProp != null) EditorGUILayout.PropertyField(m_GlobalWidthProp, new GUIContent("整体宽度缩放"));
                if (m_GlobalWobbleFreqProp != null) EditorGUILayout.PropertyField(m_GlobalWobbleFreqProp, new GUIContent("整体抖动频率缩放"));
                if (m_GlobalWobbleAmpProp != null) EditorGUILayout.PropertyField(m_GlobalWobbleAmpProp, new GUIContent("整体抖动幅度缩放"));
                EditorGUILayout.EndVertical();

                EditorGUILayout.Separator();

                EditorGUILayout.LabelField("道路基础设置", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                if (m_SplineResolutionProp != null)
                    EditorGUILayout.PropertyField(m_SplineResolutionProp, Styles.splineResolutionContent);

                if (m_UvGenerationModeProp != null)
                {
                    EditorGUILayout.PropertyField(m_UvGenerationModeProp, Styles.uvModeContent);
                    if (m_UvGenerationModeProp.enumValueIndex == (int)UVGenerationMode.WorldSpace)
                    {
                        EditorGUI.indentLevel++;
                        if (m_WorldUVScalingProp != null)
                            EditorGUILayout.PropertyField(m_WorldUVScalingProp, Styles.uvScalingContent);
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Separator();
                
                EditorGUILayout.LabelField("图层布局与细节", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.inspectorDefaultMargins);
                if (m_IndependentLayersProp != null)
                    EditorGUILayout.PropertyField(m_IndependentLayersProp, new GUIContent("独立控制每个图层"));
                EditorGUILayout.Space(5); 

                if (m_LayerProfileList != null)
                    m_LayerProfileList.DoLayoutList();
                EditorGUILayout.EndVertical();
            }
            else
            {
                DrawMissingConfigHelpBox<RoadConfig>("roadConfig", "DefaultRoadConfig");
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTerrainInteractionSection()
        {
            EditorGUILayout.LabelField(Styles.terrainSettingsHeader, EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (m_CurrentRoadManager.RoadConfig != null && m_SerializedConfig != null)
            {
                if (m_ConformToTerrainProp != null)
                {
                    bool wasConforming = m_ConformToTerrainProp.boolValue;
                    EditorGUILayout.PropertyField(m_ConformToTerrainProp, Styles.conformToTerrainContent);
                    
                    if (m_ConformToTerrainProp.boolValue)
                    {
                        if (!wasConforming) SnapAllControlPointsToTerrain();
                        
                        EditorGUI.indentLevel++;
                        if (m_TerrainConformityProp != null)
                            EditorGUILayout.PropertyField(m_TerrainConformityProp, Styles.terrainConformityContent);
                        if (m_VerticalSmoothnessProp != null)
                            EditorGUILayout.PropertyField(m_VerticalSmoothnessProp, new GUIContent("垂直平滑度"));
                        if (m_SmoothIterationsProp != null)
                            EditorGUILayout.PropertyField(m_SmoothIterationsProp, new GUIContent("平滑迭代次数"));
                        EditorGUI.indentLevel--;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("需要 RoadConfig 才能设置地形吸附。", MessageType.Info);
            }

            EditorGUILayout.Separator();

            if (m_CurrentRoadManager.TerrainConfig != null && m_SerializedTerrainConfig != null)
            {
                if (m_FlattenOffsetProp != null)
                    EditorGUILayout.PropertyField(m_FlattenOffsetProp, Styles.flattenOffsetContent);
                if (m_FlattenStrengthProp != null)
                    EditorGUILayout.PropertyField(m_FlattenStrengthProp, Styles.flattenStrengthContent);
            }
            else
            {
                DrawMissingConfigHelpBox<TerrainConfig>("terrainConfig", "DefaultTerrainConfig");
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawMissingConfigHelpBox<T>(string propertyName, string defaultConfigName) where T : ScriptableObject
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.HelpBox($"缺少 {typeof(T).Name} 资产。请在 Road Manager 上指定一个，或点击右侧按钮创建。", MessageType.Warning, true);
            if (GUILayout.Button("创建并分配", GUILayout.Width(100), GUILayout.ExpandHeight(true)))
            {
                var newConfig = GetOrCreateDefaultConfig<T>(defaultConfigName);
                m_SerializedManager.FindProperty(propertyName).objectReferenceValue = newConfig;
                m_SerializedManager.ApplyModifiedProperties();
                InitializeSerializedObjects();
            }
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawSceneRoadsList()
        {
            if (m_RoadListDirty) RefreshRoadList();
            
            EditorGUILayout.BeginHorizontal();
            m_isRoadListExpanded = EditorGUILayout.Foldout(m_isRoadListExpanded, "场景中的所有道路", true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Styles.refreshButtonIcon, EditorStyles.iconButton))
            {
                MarkRoadListAsDirty();
            }
            EditorGUILayout.EndHorizontal();
            
            if (m_isRoadListExpanded)
            {
                EditorGUILayout.BeginVertical(Styles.sectionBox);
                if (m_SceneRoads == null || m_SceneRoads.Count == 0)
                {
                    EditorGUILayout.HelpBox("场景中没有找到 Road Manager 对象。", MessageType.Info);
                }
                else
                {
                    foreach (var road in m_SceneRoads)
                    {
                        if (road == null) { MarkRoadListAsDirty(); continue; }

                        if (!m_roadLengthsCache.ContainsKey(road)) m_roadLengthsCache[road] = road.CalculateLength();
                        float length = m_roadLengthsCache[road];

                        Color originalBgColor = GUI.backgroundColor;
                        GUIStyle currentStyle = EditorStyles.toolbarButton;
                        
                        if (road == m_CurrentRoadManager)
                        {
                            GUI.backgroundColor = new Color(0.25f, 0.5f, 0.9f, 0.5f);
                            currentStyle = Styles.selectedListItem;
                        }

                        var buttonContent = new GUIContent($" {road.gameObject.name} ({length:F1} m)", EditorGUIUtility.IconContent("d_GameObject Icon").image);
                        
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button(buttonContent, currentStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22)))
                        {
                            Selection.activeGameObject = road.gameObject;
                            EditorGUIUtility.PingObject(road.gameObject);
                        }
                        EditorGUILayout.EndHorizontal();

                        GUI.backgroundColor = originalBgColor;
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }
        #endregion

        #region ReorderableList 回调方法
        private void SetupReorderableList()
        {
            m_LayerProfileList = new ReorderableList(m_SerializedConfig, m_LayerProfilesProp, true, true, true, true);
            m_LayerProfileList.drawHeaderCallback = DrawReorderableListHeader;
            m_LayerProfileList.drawElementCallback = DrawReorderableListElement;
            m_LayerProfileList.elementHeightCallback = GetReorderableListElementHeight;
            m_LayerProfileList.onChangedCallback = (list) =>
            {
                m_SerializedConfig.ApplyModifiedProperties();
                m_CurrentRoadManager.RegenerateRoad();
            };
        }

        private void DrawReorderableListHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "道路分层剖面 (由内向外)");
        }
        
        private void DrawReorderableListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            const float VERTICAL_SPACING = 6f;
            const float TOTAL_VERTICAL_PADDING = 4f;

            SerializedProperty element = m_LayerProfileList.serializedProperty.GetArrayElementAtIndex(index);
            rect.y += TOTAL_VERTICAL_PADDING;
            float singleLineHeight = EditorGUIUtility.singleLineHeight;
            float originalLabelWidth = EditorGUIUtility.labelWidth;
            
            SerializedProperty isExpandedProp = element.FindPropertyRelative("isExpanded");
            SerializedProperty layerNameProp = element.FindPropertyRelative("layerName");
            
            bool isIndependentMode = m_IndependentLayersProp != null && m_IndependentLayersProp.boolValue;
            
            EditorGUI.BeginChangeCheck();
            
            Rect foldoutRect = new Rect(rect.x, rect.y, rect.width, singleLineHeight);
            isExpandedProp.boolValue = EditorGUI.Foldout(foldoutRect, isExpandedProp.boolValue, new GUIContent(layerNameProp.stringValue), true);
            
            if (isExpandedProp.boolValue)
            {
                Rect bgRect = new Rect(rect.x, foldoutRect.yMax, rect.width, GetReorderableListElementHeight(index) - singleLineHeight - TOTAL_VERTICAL_PADDING * 2);
                EditorGUI.DrawRect(bgRect, new Color(0, 0, 0, 0.1f));

                Rect currentRect = new Rect(rect.x + 15, foldoutRect.yMax + VERTICAL_SPACING, rect.width - 20, singleLineHeight);
                EditorGUIUtility.labelWidth = 80;
                
                EditorGUI.PropertyField(currentRect, layerNameProp, new GUIContent("图层名"));
                currentRect.y += singleLineHeight + VERTICAL_SPACING;
                
                if (isIndependentMode)
                {
                    EditorGUI.PropertyField(currentRect, element.FindPropertyRelative("offsetFromCenter"), new GUIContent("中心偏移"));
                    currentRect.y += singleLineHeight + VERTICAL_SPACING;
                    EditorGUI.PropertyField(currentRect, element.FindPropertyRelative("width"), new GUIContent("宽度"));
                    currentRect.y += singleLineHeight + VERTICAL_SPACING;
                }
                else
                {
                    EditorGUI.PropertyField(currentRect, element.FindPropertyRelative("width"), new GUIContent("附加宽度"));
                    currentRect.y += singleLineHeight + VERTICAL_SPACING;
                }
                
                EditorGUI.PropertyField(currentRect, element.FindPropertyRelative("verticalOffset"), new GUIContent("高度偏移"));
                currentRect.y += singleLineHeight + VERTICAL_SPACING;
                
                EditorGUI.PropertyField(currentRect, element.FindPropertyRelative("meshMaterial"), new GUIContent("材质"));
                currentRect.y += singleLineHeight + VERTICAL_SPACING;
                
                EditorGUI.LabelField(currentRect, "边缘风格化 (Stylization)", EditorStyles.boldLabel);
                currentRect.y += singleLineHeight + VERTICAL_SPACING;

                var wobbleFreqProp = element.FindPropertyRelative("boundaryWobbleFrequency");
                EditorGUI.Slider(currentRect, wobbleFreqProp, 0f, 10f, new GUIContent("抖动频率"));
                currentRect.y += singleLineHeight + VERTICAL_SPACING;

                var wobbleAmpProp = element.FindPropertyRelative("boundaryWobbleAmplitude");
                EditorGUI.Slider(currentRect, wobbleAmpProp, 0f, 2f, new GUIContent("抖动幅度"));
            }
            
            if (EditorGUI.EndChangeCheck())
            {
                if (m_SerializedConfig != null) m_SerializedConfig.ApplyModifiedProperties();
                if (m_CurrentRoadManager != null) m_CurrentRoadManager.RegenerateRoad();
                OnSettingsChanged?.Invoke();
            }
            
            EditorGUIUtility.labelWidth = originalLabelWidth;
        }

        private float GetReorderableListElementHeight(int index)
        {
            SerializedProperty element = m_LayerProfileList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty isExpandedProp = element.FindPropertyRelative("isExpanded");

            if (isExpandedProp == null || !isExpandedProp.boolValue)
            {
                return EditorGUIUtility.singleLineHeight + 8f;
            }
            else
            {
                bool isIndependentMode = m_IndependentLayersProp != null && m_IndependentLayersProp.boolValue;
                const float VERTICAL_SPACING = 6f;
                const float TOTAL_VERTICAL_PADDING = 12f;
                
                int lineCount = isIndependentMode ? 8 : 7;
                return (EditorGUIUtility.singleLineHeight * lineCount) + (VERTICAL_SPACING * (lineCount)) + TOTAL_VERTICAL_PADDING;
            }
        }
        #endregion

        #region 事件处理与状态更新
        private void OnSelectionChanged()
        {
            var selectedRoadManager = Selection.activeGameObject?.GetComponent<RoadManager>();

            if (m_CurrentRoadManager != selectedRoadManager)
            {
                m_CurrentRoadManager = selectedRoadManager;
                InitializeSerializedObjects();
            }
            Repaint();
        }

        private void MarkRoadListAsDirty()
        {
            m_RoadListDirty = true;
            Repaint();
        }

        private void RefreshRoadList()
        {
#if UNITY_2023_1_OR_NEWER
            m_SceneRoads = FindObjectsByType<RoadManager>(FindObjectsSortMode.None).ToList();
#else
            m_SceneRoads = FindObjectsOfType<RoadManager>().ToList();
#endif
            m_roadLengthsCache.Clear();
            m_RoadListDirty = false;
        }
        #endregion

        #region 初始化与数据管理
        private void InitializeSerializedObjects()
        {
            ClearSerializedObjects();
            if (m_CurrentRoadManager == null) return;

            m_SerializedManager = new SerializedObject(m_CurrentRoadManager);

            InitializeRoadConfig();
            InitializeTerrainConfig();
        }

        private void InitializeRoadConfig()
        {
            if (m_CurrentRoadManager.RoadConfig != null)
            {
                m_SerializedConfig = new SerializedObject(m_CurrentRoadManager.RoadConfig);
                
                m_LayerProfilesProp = m_SerializedConfig.FindProperty("layerProfiles");
                m_SplineResolutionProp = m_SerializedConfig.FindProperty("splineResolution");
                m_UvGenerationModeProp = m_SerializedConfig.FindProperty("uvGenerationMode");
                m_WorldUVScalingProp = m_SerializedConfig.FindProperty("worldUVScaling");
                m_ConformToTerrainProp = m_SerializedConfig.FindProperty("conformToTerrainUndulations");
                m_TerrainConformityProp = m_SerializedConfig.FindProperty("terrainConformity");
                
                m_GlobalWidthProp = m_SerializedConfig.FindProperty("globalWidthMultiplier");
                m_GlobalWobbleFreqProp = m_SerializedConfig.FindProperty("globalWobbleFrequencyMultiplier");
                m_GlobalWobbleAmpProp = m_SerializedConfig.FindProperty("globalWobbleAmplitudeMultiplier");
                m_IndependentLayersProp = m_SerializedConfig.FindProperty("controlLayersIndependently");

                m_VerticalSmoothnessProp = m_SerializedConfig.FindProperty("verticalSmoothness");
                m_SmoothIterationsProp = m_SerializedConfig.FindProperty("smoothIterations");
                
                SetupReorderableList();
            }
        }

        private void InitializeTerrainConfig()
        {
            if (m_CurrentRoadManager.TerrainConfig != null)
            {
                m_SerializedTerrainConfig = new SerializedObject(m_CurrentRoadManager.TerrainConfig);
                m_FlattenOffsetProp = m_SerializedTerrainConfig.FindProperty("flattenOffset");
                m_FlattenStrengthProp = m_SerializedTerrainConfig.FindProperty("flattenStrength");
            }
        }
        
        private void ClearSerializedObjects()
        {
            m_SerializedManager = null;
            m_SerializedConfig = null;
            m_SerializedTerrainConfig = null;
            m_LayerProfileList = null;
        }
        #endregion

        #region 核心功能方法
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

        private void SnapAllControlPointsToTerrain()
        {
            if (m_CurrentRoadManager == null) return;
            Undo.RecordObject(m_CurrentRoadManager, "Snap Points to Terrain");

            var managerTransform = m_CurrentRoadManager.transform;
            var pointsProp = m_SerializedManager.FindProperty("controlPoints");

            for (int i = 0; i < pointsProp.arraySize; i++)
            {
                var pointProp = pointsProp.GetArrayElementAtIndex(i);
                var posProp = pointProp.FindPropertyRelative("position");

                var worldPos = managerTransform.TransformPoint(posProp.vector3Value);
                var terrain = Terrain.activeTerrain;
                if (terrain != null)
                {
                    worldPos.y = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                }

                posProp.vector3Value = managerTransform.InverseTransformPoint(worldPos);
            }

            m_SerializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(m_CurrentRoadManager);
            m_CurrentRoadManager.RegenerateRoad();
            OnSettingsChanged?.Invoke();
        }
        #endregion
    }
}