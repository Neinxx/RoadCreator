// 文件路径: Assets/RoadCreator/Editor/Terrain/TerrainModifier.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace RoadSystem.Editor
{
    /// <summary>
    /// [最终架构版]
    /// 道路地形修改的总指挥。
    /// 它负责整个高级烘焙流程，并根据用户设置，将最终的绘制任务分派给
    /// 合适的后端模块（CPU 或未来的 GPU）。
    /// </summary>
    public class TerrainModifier
    {
        public void Execute(RoadManager roadManager)
        {
            #region 1. 前置检查与数据烘焙
            if (roadManager.ControlPoints.Count < 2 || roadManager.MeshFilter.sharedMesh == null)
            {
                Debug.LogWarning("道路数据不完整，无法修改地形。");
                return;
            }

            EditorUtility.DisplayProgressBar("地形修改", "步骤 1/3: 烘焙道路数据...", 0.1f);
            var bakerResult = RoadDataBaker.Bake(roadManager.RoadConfig, roadManager.gameObject.name);
            if (bakerResult == null) { EditorUtility.ClearProgressBar(); return; }
            #endregion

            #region 2. 准备地形列表
            Bounds roadWorldBounds = roadManager.MeshFilter.sharedMesh.bounds;
            roadWorldBounds.center = roadManager.transform.TransformPoint(roadWorldBounds.center);
            List<Terrain> affectedTerrains = EditorTerrainUtility.FindAffectedTerrains(roadWorldBounds);
            if (affectedTerrains.Count == 0)
            {
                Debug.Log("道路范围未影响任何地形。");
                EditorUtility.ClearProgressBar();
                return;
            }
            TerrainNeighborManager.UpdateAllTerrainNeighbors();
            #endregion

            #region 3. 选择处理模块并执行
            var settings = RoadCreatorSettings.GetOrCreateSettings();
            ITerrainModificationModule module;

            switch (settings.modificationMode)
            {
                case ProcessingMode.CPU:
                    module = new CPUFlattenAndTextureModule();
                    break;
                case ProcessingMode.GPU:
                    // [未来扩展点] 当你准备好GPU模块时，在这里实例化它
                    // module = new GPUAdvancedBakerModule(); 
                    Debug.LogWarning("GPU高级烘焙模块尚未实现，将回退到CPU模式。");
                    module = new CPUFlattenAndTextureModule(); // 临时回退
                    break;
                default:
                    Debug.LogError($"不支持的处理模式: {settings.modificationMode}");
                    EditorUtility.ClearProgressBar();
                    return;
            }
            #endregion

            #region 4. 循环处理所有地形
            try
            {
                for (int i = 0; i < affectedTerrains.Count; i++)
                {
                    var terrain = affectedTerrains[i];
                    float progress = 0.3f + (i / (float)affectedTerrains.Count) * 0.7f;
                    EditorUtility.DisplayProgressBar("地形修改", $"步骤 2/2: 应用到地形 {terrain.name} (使用 {module.ModuleName})...", progress);

                    var terrainData = terrain.terrainData;
                    int roadLayerIndex = EnsureAndGetRoadLayerIndex(terrainData, bakerResult.roadTerrainLayer);

                    // [修改] ApplyCustomMaterial 现在需要返回生成的 RoadDataMap
                    Texture2D roadDataMap = ApplyCustomMaterial(terrain, bakerResult, roadLayerIndex);


                    var modificationData = new TerrainModificationData(terrain, roadManager);
                    // [修改] module.Execute 现在需要 roadDataMap
                    module.Execute(modificationData, bakerResult, roadLayerIndex, roadDataMap);


                    EditorUtility.SetDirty(terrainData);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                SceneView.RepaintAll();
                Debug.Log("高级地形修改完成！");
            }
            #endregion
        }

        // 辅助方法，逻辑更清晰
         private int EnsureAndGetRoadLayerIndex(TerrainData terrainData, TerrainLayer roadLayer)
        {
            var currentLayers = terrainData.terrainLayers.ToList();
            int index = currentLayers.FindIndex(l => l != null && l.name == roadLayer.name);
            
            if (index == -1)
            {
                index = currentLayers.Count;
                currentLayers.Add(roadLayer);
                terrainData.terrainLayers = currentLayers.ToArray();

                // --- [关键] 强制通知 Unity 编辑器数据已变更 ---
                EditorUtility.SetDirty(terrainData); // 标记资产已修改
                AssetDatabase.SaveAssets();          // 保存所有修改过的资产
                AssetDatabase.Refresh();             // 刷新资产数据库
                Debug.Log($"已向 '{terrainData.name}' 添加新的 TerrainLayer 并强制刷新。");
            }
            return index;
        }





        private Texture2D ApplyCustomMaterial(Terrain terrain, RoadDataBaker.BakerResult bakerResult, int roadLayerIndex)
        {
            var settings = RoadCreatorSettings.GetOrCreateSettings();

            // [核心修改] 直接从设置中获取材质对象
            var customMaterial = settings.customTerrainMaterial;

            if (!customMaterial)
            {
                Debug.LogError("自定义地形材质未在 RoadCreatorSettings 中设置！请前往设置文件进行配置。");
                // 选中设置文件，方便用户快速定位
                Selection.activeObject = settings;
                return null;
            }

            // --- 创建并保存 RoadDataMap ... ---
            var terrainData = terrain.terrainData;
            var roadDataMap = new Texture2D(terrainData.alphamapWidth, terrainData.alphamapHeight, TextureFormat.RGBAFloat, false, true);
            roadDataMap.name = $"{terrain.name}_RoadDataMap";

            string assetPath = Path.Combine(settings.generatedAssetsPath, $"{roadDataMap.name}.asset");
            AssetDatabase.CreateAsset(roadDataMap, assetPath);

            var materialInstance = new Material(customMaterial);
            materialInstance.SetTexture("_RoadAtlas", bakerResult.atlasTexture);
            materialInstance.SetFloat("_RoadLayerIndex", (float)roadLayerIndex);
            materialInstance.SetTexture("_RoadDataMap", roadDataMap);

            terrain.materialTemplate = materialInstance;

            return roadDataMap;
        }
    }
}