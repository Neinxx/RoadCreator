using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class PrefabGenerator
{
    private const string TargetShaderName = "CodeV/Scene/Standard";
    private const string MenuItemPath = "Assets/Tools/Generate Prefab & Standardize Materials";

    [MenuItem(MenuItemPath)]
    private static void GeneratePrefab()
    {
        Object[] selectedObjects = Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets);
        if (selectedObjects.Length == 0) return;

        AssetDatabase.StartAssetEditing();
        Shader targetShader = Shader.Find(TargetShaderName);

        if (targetShader == null)
        {
            Debug.LogError($"Shader not found: '{TargetShaderName}'. Please check the name in the script. Aborting.");
            AssetDatabase.StopAssetEditing();
            return;
        }
        
        try
        {
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject model = selectedObjects[i] as GameObject;
                string modelName = model.name;

                EditorUtility.DisplayProgressBar(
                    "Prefab Generation & Standardization",
                    $"Processing model: {modelName} ({i + 1}/{selectedObjects.Length})",
                    (float)i / selectedObjects.Length);
                
                ProcessModel(model, targetShader);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Process completed! Processed {selectedObjects.Length} models.");
        }
    }

    [MenuItem(MenuItemPath, true)]
    private static bool ValidateGeneratePrefab()
    {
        return Selection.GetFiltered(typeof(GameObject), SelectionMode.Assets).Length > 0;
    }

    private static void ProcessModel(GameObject model, Shader targetShader)
    {
        string modelPath = AssetDatabase.GetAssetPath(model);
        ModelImporter modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
       
        
        string modelName = Path.GetFileNameWithoutExtension(modelPath);
        
        GetNameParts(modelName, out string baseName, out string suffixNumber);

        string baseFolderPath = Path.GetDirectoryName(Path.GetDirectoryName(modelPath));
        string prefabFolderPath = EnsureFolderExists(baseFolderPath, "prefab");
        string materialFolderPath = EnsureFolderExists(baseFolderPath, "material");
        string textureFolderPath = EnsureFolderExists(baseFolderPath, "texture");
        
        var materialMap = new Dictionary<Material, Material>();
        var movedTextures = new HashSet<string>();

        // 查找材质时，我们需要遍历实例的渲染器，而不是LoadAllAssetsAtPath，这样对Prefab更有效
        GameObject tempInstanceForMats = PrefabUtility.InstantiatePrefab(model) as GameObject;
        foreach (var renderer in tempInstanceForMats.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var originalMaterial in renderer.sharedMaterials)
            {
                if (originalMaterial == null || materialMap.ContainsKey(originalMaterial)) continue;

                string newMaterialName = $"material_{baseName}_{suffixNumber}";
                string newMaterialPath = Path.Combine(materialFolderPath, $"{newMaterialName}.mat");
                if (File.Exists(Path.GetFullPath(newMaterialPath)))
                {
                    AssetDatabase.DeleteAsset(newMaterialPath);
                }
                
                Material newMaterial = new Material(targetShader);
                AssetDatabase.CreateAsset(newMaterial, newMaterialPath);
                
                RemapMaterialProperties(originalMaterial, newMaterial);
                ProcessTexturesInMaterial(newMaterial, textureFolderPath, movedTextures);
                
                materialMap[originalMaterial] = newMaterial;
            }
        }
        Object.DestroyImmediate(tempInstanceForMats);

        if (modelImporter != null)
        {
            modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        }
        
        string prefabName = $"prefab_{baseName}_{suffixNumber}";
        string prefabPath = Path.Combine(prefabFolderPath, $"{prefabName}.prefab");

        GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;

        // --- 核心修正: 断开实例与原预制体的连接 ---
        // 这行代码能确保我们创建的是一个全新的Prefab，而不是一个Prefab变体(Variant)。
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            Material[] newSharedMaterials = new Material[sharedMaterials.Length];

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material oldMat = sharedMaterials[i];
                if (oldMat != null && materialMap.TryGetValue(oldMat, out Material newMat))
                {
                    newSharedMaterials[i] = newMat;
                }
                else
                {
                    newSharedMaterials[i] = oldMat;
                }
            }
            renderer.sharedMaterials = newSharedMaterials;
        }
        
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
    }

    private static void GetNameParts(string modelName, out string baseName, out string suffixNumber)
    {
        string[] parts = modelName.Split('_');
        
        if (parts.Length >= 3)
        {
            baseName = string.Join("_", parts, 1, parts.Length - 2);
            suffixNumber = parts[parts.Length - 1];
        }
        else if (parts.Length == 2)
        {
            baseName = parts[0];
            suffixNumber = parts[1];
        }
        else
        {
            baseName = modelName;
            suffixNumber = "01";
        }
    }
    
    private static void RemapMaterialProperties(Material oldMat, Material newMat)
    {
        if (oldMat.HasProperty("_Color") && newMat.HasProperty("_BaseColor"))
            newMat.SetColor("_BaseColor", oldMat.GetColor("_Color"));
        if (oldMat.HasProperty("_MainTex") && newMat.HasProperty("_BaseMap"))
            newMat.SetTexture("_BaseMap", oldMat.GetTexture("_MainTex"));
        if (oldMat.HasProperty("_BumpMap") && newMat.HasProperty("_BumpMap"))
            newMat.SetTexture("_BumpMap", oldMat.GetTexture("_BumpMap"));
        if (oldMat.HasProperty("_MetallicGlossMap") && newMat.HasProperty("_MetallicGlossMap"))
            newMat.SetTexture("_MetallicGlossMap", oldMat.GetTexture("_MetallicGlossMap"));
        else if (oldMat.HasProperty("_Metallic") && newMat.HasProperty("_Metallic"))
             newMat.SetFloat("_Metallic", oldMat.GetFloat("_Metallic"));
        if (oldMat.HasProperty("_OcclusionMap") && newMat.HasProperty("_OcclusionMap"))
            newMat.SetTexture("_OcclusionMap", oldMat.GetTexture("_OcclusionMap"));
    }
    
    private static void ProcessTexturesInMaterial(Material material, string textureFolderPath, HashSet<string> movedTextures)
    {
        string[] texturePropertyNames = material.GetTexturePropertyNames();
        foreach (string propName in texturePropertyNames)
        {
            Texture texture = material.GetTexture(propName);
            if (texture == null) continue;
            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(texturePath) || !AssetDatabase.IsMainAsset(texture) || movedTextures.Contains(texturePath)) continue;
            string textureName = Path.GetFileName(texturePath);
            string newTexturePath = Path.Combine(textureFolderPath, textureName);
            if (texturePath != newTexturePath)
            {
                if (AssetDatabase.MoveAsset(texturePath, newTexturePath) == "")
                    movedTextures.Add(newTexturePath);
            }
            else
            {
                movedTextures.Add(texturePath);
            }
        }
    }
    
    private static string EnsureFolderExists(string basePath, string folderName)
    {
        string folderPath = Path.Combine(basePath, folderName);
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(basePath, folderName);
        }
        return folderPath;
    }
}