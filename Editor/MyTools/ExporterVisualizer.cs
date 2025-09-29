// ***** ExporterVisualizer_Refined.cs *****

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ExporterVisualizer
{
    
    private static class VStyles
    {
        public static readonly Color agentColor = new Color(1f, 0.5f, 0f); // 标志性的橙色
        public static readonly Color minAreaColor = new Color(1f, 0.7f, 0.2f); // 稍亮的橙黄色
        public static readonly Color heightGridColor = new Color(0.5f, 0.8f, 1f); // 清爽的蓝色

        public static readonly Color agentFillColor = new Color(agentColor.r, agentColor.g, agentColor.b, 0.1f);
        public static readonly Color minAreaFillColor = new Color(minAreaColor.r, minAreaColor.g, minAreaColor.b, 0.15f);
        public static readonly Color heightGridFillColor = new Color(heightGridColor.r, heightGridColor.g, heightGridColor.b, 0.1f);
        public static readonly Color heightGridLineColor = new Color(heightGridColor.r, heightGridColor.g, heightGridColor.b, 0.7f);

        public static readonly GUIStyle labelStyle = new GUIStyle
        {
            normal = { textColor = Color.white },
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        
        public const float AGENT_LINE_THICKNESS = 3.0f;
        public const float GRID_LINE_THICKNESS = 2.0f;
    }

    // 【重构】主绘制函数现在只负责调度，并且传入预先计算好的地面位置
    public static void Draw(AdvancedFBXExporter window, Vector3 groundPosition)
    {
        if (!window.showVisualizers || window.selectedObjects.Count == 0 || window.calculatedBounds.size == Vector3.zero)
            return;

        // 根据是否使用高度限制，确定Agent的可视化基准位置
        float yLevel = window.useHeightLimit ? window.maxHeight : groundPosition.y;
        Vector3 basePosition = new Vector3(window.calculatedBounds.center.x, yLevel, window.calculatedBounds.center.z);

        using (new Handles.DrawingScope(Matrix4x4.identity)) // 使用 using 来自动处理矩阵和颜色重置
        {
            // 总是让可视化对象在模型之上渲染，避免被遮挡
            Handles.zTest = CompareFunction.LessEqual;
            
            DrawMinRegionAreaVisualizer(basePosition, window);
            DrawAgentVisualizer(basePosition, window);

            if (window.useHeightLimit)
            {
                DrawHeightLimitVisuals(window, groundPosition);
            }
        }
    }

    
      public static float DrawHeightHandle(AdvancedFBXExporter window)
    {
        if (!window.showVisualizers || !window.useHeightLimit || window.selectedObjects.Count == 0)
            return window.maxHeight;

        Bounds bounds = window.calculatedBounds;
        Vector3 handlePos = new Vector3(bounds.max.x + 1.0f, window.maxHeight, bounds.center.z); // 将Handle向外移动一点避免和模型重叠
        
        float handleSize = HandleUtility.GetHandleSize(handlePos) * 1.0f;

        // 使用 using 确保 ZTest 状态被正确还原
        using (new Handles.DrawingScope(Matrix4x4.identity))
        {
            Handles.zTest = CompareFunction.Always; // 确保句柄总是可见
            Handles.color = VStyles.heightGridColor;
            
            // 使用更美观的带箭头的滑块，返回新位置的 y 坐标
            Vector3 newPos = Handles.Slider(handlePos, Vector3.up, handleSize, Handles.ArrowHandleCap, 0f);
            return newPos.y;
        }
    }

    private static void DrawAgentVisualizer(Vector3 position, AdvancedFBXExporter window)
    {
        Handles.color = VStyles.agentColor;

        // 【优化】增加半透明填充，提升可见性
        Handles.color = VStyles.agentFillColor;
        Handles.DrawSolidDisc(position, Vector3.up, window.agentRadius);
        Handles.DrawSolidDisc(position + Vector3.up * window.agentHeight, Vector3.up, window.agentRadius);

        // 绘制线框
        Handles.color = VStyles.agentColor;
        Handles.DrawWireDisc(position, Vector3.up, window.agentRadius);
        Handles.DrawWireDisc(position + Vector3.up * window.agentHeight, Vector3.up, window.agentRadius);

        Handles.DrawAAPolyLine(VStyles.AGENT_LINE_THICKNESS, position + new Vector3(window.agentRadius, 0, 0), position + new Vector3(window.agentRadius, window.agentHeight, 0));
        Handles.DrawAAPolyLine(VStyles.AGENT_LINE_THICKNESS, position + new Vector3(-window.agentRadius, 0, 0), position + new Vector3(-window.agentRadius, window.agentHeight, 0));
        Handles.DrawAAPolyLine(VStyles.AGENT_LINE_THICKNESS, position + new Vector3(0, 0, window.agentRadius), position + new Vector3(0, window.agentHeight, window.agentRadius));
        Handles.DrawAAPolyLine(VStyles.AGENT_LINE_THICKNESS, position + new Vector3(0, 0, -window.agentRadius), position + new Vector3(0, window.agentHeight, -window.agentRadius));
        
        Handles.Label(position + Vector3.up * (window.agentHeight + 0.3f), $"Agent\nH:{window.agentHeight} R:{window.agentRadius}", VStyles.labelStyle);
    }
    
        private static void DrawMinRegionAreaVisualizer(Vector3 agentPosition, AdvancedFBXExporter window)
    {
        if (window.minRegionArea <= 0) return;

        float sideLength = Mathf.Sqrt(window.minRegionArea);
        
        // 计算agent正前方位置（Z轴正方向），距离为agent的直径（一个身位）
        float offset = window.agentRadius * 2f + sideLength / 2f;
        Vector3 centerPos = agentPosition + new Vector3(0, 0.01f, offset);
        
        Vector3 p1 = centerPos + new Vector3(-sideLength / 2, 0, -sideLength / 2);
        Vector3 p2 = centerPos + new Vector3(sideLength / 2, 0, -sideLength / 2);
        Vector3 p3 = centerPos + new Vector3(sideLength / 2, 0, sideLength / 2);
        Vector3 p4 = centerPos + new Vector3(-sideLength / 2, 0, sideLength / 2);

        // 【优化】同样增加半透明填充
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { p1, p2, p3, p4 }, VStyles.minAreaFillColor, VStyles.minAreaColor);
        
        Handles.Label(centerPos + Vector3.down * 0.3f, $"Min Area: {window.minRegionArea:F1} m²", VStyles.labelStyle);
    }
    
    private static void DrawHeightLimitVisuals(AdvancedFBXExporter window, Vector3 groundPosition)
    {
        float height = window.maxHeight;
        Bounds bounds = window.calculatedBounds;

        // 绘制高度网格
        float size = Mathf.Max(bounds.size.x, bounds.size.z);
        Vector3 center = new Vector3(bounds.center.x, height, bounds.center.z);
        Vector3 p1 = center + new Vector3(-size / 2, 0, -size / 2);
        Vector3 p2 = center + new Vector3(size / 2, 0, -size / 2);
        Vector3 p3 = center + new Vector3(size / 2, 0, size / 2);
        Vector3 p4 = center + new Vector3(-size / 2, 0, size / 2);
        Handles.DrawSolidRectangleWithOutline(new Vector3[] { p1, p2, p3, p4 }, VStyles.heightGridFillColor, Color.clear);

        Handles.color = VStyles.heightGridLineColor;
        int lineCount = 10;
        float spacing = size / lineCount;
        Vector3 gridOrigin = center - new Vector3(size / 2, 0, size / 2);

        for (int i = 0; i <= lineCount; i++)
        {
            Vector3 start1 = gridOrigin + new Vector3(i * spacing, 0, 0);
            Vector3 end1 = start1 + new Vector3(0, 0, size);
            Handles.DrawAAPolyLine(VStyles.GRID_LINE_THICKNESS, start1, end1);

            Vector3 start2 = gridOrigin + new Vector3(0, 0, i * spacing);
            Vector3 end2 = start2 + new Vector3(size, 0, 0);
            Handles.DrawAAPolyLine(VStyles.GRID_LINE_THICKNESS, start2, end2);
        }
        
        // 绘制从地面到高度平面的虚线
        Handles.color = VStyles.heightGridColor;
        Vector3 groundPoint = new Vector3(bounds.center.x, groundPosition.y, bounds.center.z);
        Handles.DrawDottedLine(groundPoint, center, 4.0f);
        
        string labelText = $"Max Height: {height:F2}";
        Handles.Label(center + new Vector3(size / 2 + 0.5f, 0, 0), labelText, VStyles.labelStyle);
    }
}