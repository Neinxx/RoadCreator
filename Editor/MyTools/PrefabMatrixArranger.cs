using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class PrefabMatrixArranger : EditorWindow
{
    // 矩阵行数和列数
    private int rows = 3;
    private int columns = 3;
    
    // 预制体之间的间距
    private float spacing = 2.0f;
    
    // 缩放比例范围
    private float minScale = 0.5f;
    private float maxScale = 2.0f;
    
    // 中心点位置
    private Vector3 centerPosition = Vector3.zero;

    [MenuItem("Tools/Prefab Matrix Arranger")]
    public static void ShowWindow()
    {
        GetWindow<PrefabMatrixArranger>("Prefab Matrix Arranger");
    }

    private void OnGUI()
    {
        GUILayout.Label("矩阵设置", EditorStyles.boldLabel);
        
        rows = EditorGUILayout.IntField("行数", rows);
        columns = EditorGUILayout.IntField("列数", columns);
        spacing = EditorGUILayout.FloatField("间距", spacing);
        
        GUILayout.Space(10);
        GUILayout.Label("缩放设置", EditorStyles.boldLabel);
        
        maxScale = EditorGUILayout.FloatField("最大缩放", maxScale);
        minScale = EditorGUILayout.FloatField("最小缩放", minScale);
        
        GUILayout.Space(10);
        GUILayout.Label("位置设置", EditorStyles.boldLabel);
        
        centerPosition = EditorGUILayout.Vector3Field("中心点", centerPosition);
        
        GUILayout.Space(20);
        
        if (GUILayout.Button("排列选中的预制体"))
        {
            ArrangePrefabsInMatrix();
        }
    }

    private void ArrangePrefabsInMatrix()
    {
        // 获取选中的预制体
        GameObject[] selectedPrefabs = Selection.gameObjects;
        
        if (selectedPrefabs.Length == 0)
        {
            EditorUtility.DisplayDialog("警告", "请先选中至少一个预制体", "确定");
            return;
        }
        
        // 计算矩阵的总大小
        float totalWidth = (columns - 1) * spacing;
        float totalHeight = (rows - 1) * spacing;
        
        // 计算起始位置（左上角）
        Vector3 startPosition = centerPosition - new Vector3(totalWidth / 2, 0, totalHeight / 2);
        
        // 计算缩放比例的差值
        float scaleDifference = maxScale - minScale;
        
        int prefabIndex = 0;
        
        // 创建一个父对象来管理所有实例
        GameObject matrixParent = new GameObject("Matrix Group");
        matrixParent.transform.position = centerPosition;
        
        // 循环创建矩阵
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                // 如果预制体用完了就循环使用
                GameObject prefab = selectedPrefabs[prefabIndex % selectedPrefabs.Length];
                
                // 实例化预制体
                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance != null)
                {
                    instance.transform.parent = matrixParent.transform;
                    
                    // 计算位置
                    float xPos = startPosition.x + col * spacing;
                    float zPos = startPosition.z + row * spacing;
                    instance.transform.position = new Vector3(xPos, centerPosition.y, zPos);
                    
                    // 计算缩放比例（从大到小）
                    // 计算当前位置在矩阵中的相对位置（0到1之间）
                    float normalizedRow = (float)row / (rows - 1);
                    float normalizedCol = (float)col / (columns - 1);
                    
                    // 取平均作为缩放因子，使左上角最大，右下角最小
                    float scaleFactor = 1 - (normalizedRow + normalizedCol) / 2;
                    float scale = minScale + scaleFactor * scaleDifference;
                    
                    instance.transform.localScale = Vector3.one * scale;
                }
                
                prefabIndex++;
            }
        }
        
        Debug.Log($"已创建 {rows * columns} 个预制体实例，排列成 {rows}x{columns} 的矩阵");
    }
}
