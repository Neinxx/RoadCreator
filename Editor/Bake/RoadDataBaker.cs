using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using RoadSystem.Editor;

namespace RoadSystem
{
    /// <summary>
    /// [新增] 道路数据烘焙器
    /// 负责将 RoadConfig 中的多个材质和纹理，烘焙成一张单一的纹理图集 (Texture Atlas)，
    /// 并创建一个使用该图集的特殊 TerrainLayer。
    /// 这是实现“所见即所得”高级地形纹理的第一步。
    /// </summary>
    public static class RoadDataBaker
    {



        // 烘焙结果的数据结构
        public class BakerResult
        {
            public TerrainLayer roadTerrainLayer;
            public Texture2D atlasTexture;
            public Dictionary<Material, Rect> uvRects; // 存储每个原始材质在图集中的位置
        }

        /// <summary>
        /// 烘焙操作的主入口。
        /// </summary>
        /// <param name="roadConfig">道路配置</param>
        /// <param name="roadName">用于命名生成资产的道路名称</param>
        /// <returns>包含 TerrainLayer 和图集纹理的烘焙结果</returns>
        public static BakerResult Bake(RoadConfig roadConfig, string roadName)
        {
            if (roadConfig == null || roadConfig.layerProfiles.Count == 0)
            {
                Debug.LogError("RoadConfig 为空或没有图层可供烘焙。");
                return null;
            }

            var settings = RoadCreatorSettings.GetOrCreateSettings();
            string savePath = settings.generatedAssetsPath;
            Directory.CreateDirectory(savePath);

            var materials = roadConfig.layerProfiles.Select(p => p.meshMaterial).ToList();
            var originalTextures = materials.Select(m => m.mainTexture as Texture2D).ToList();

            // --- [终极修复] 创建内存中的可读克隆体 ---
            List<Texture2D> readableClones = new List<Texture2D>();
            for (int i = 0; i < originalTextures.Count; i++)
            {
                if (originalTextures[i] == null)
                {
                    // 如果某个材质没有纹理，用一个纯白的小纹理占位
                    readableClones.Add(Texture2D.whiteTexture);
                    continue;
                }

                // 如果纹理本身已经是可读的，直接使用，无需克隆
                if (originalTextures[i].isReadable)
                {
                    readableClones.Add(originalTextures[i]);
                }
                else
                {
                    // 创建可读的克隆体
                    readableClones.Add(CreateReadableClone(originalTextures[i]));
                }
            }

            // --- 现在，我们用这些 100% 可读的克隆体来打包 ---
            var atlas = new Texture2D(2048, 2048);
            Rect[] rects = atlas.PackTextures(readableClones.ToArray(), 2, 2048, false);

            // [重要] 销毁我们创建的临时克隆体，释放内存
            foreach (var clone in readableClones)
            {
                // 不要销毁原始纹理或白色占位纹理
                if (originalTextures.Contains(clone) || clone == Texture2D.whiteTexture) continue;
                UnityEngine.Object.DestroyImmediate(clone);
            }

            // --- 后续的保存资产逻辑保持不变 ---
            byte[] bytes = atlas.EncodeToPNG();
            string assetPath = Path.Combine(savePath, $"{roadName}_Atlas.png");
            File.WriteAllBytes(assetPath, bytes);
            AssetDatabase.ImportAsset(assetPath);

            var atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            var terrainLayer = new TerrainLayer
            {
                diffuseTexture = atlasTexture,
                tileSize = new Vector2(10, 10),
                name = $"{roadName}_RoadLayer"
            };

            string layerAssetPath = Path.Combine(savePath, $"{terrainLayer.name}.terrainlayer");
            AssetDatabase.CreateAsset(terrainLayer, layerAssetPath);

            Debug.Log($"成功烘焙道路数据！资产保存在: {savePath}");

            var result = new BakerResult
            {
                roadTerrainLayer = terrainLayer,
                atlasTexture = atlasTexture,
                uvRects = new Dictionary<Material, Rect>()
            };

            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] != null)
                {
                    result.uvRects[materials[i]] = rects[i];
                }
            }
            return result;
        }

        /// <summary>
        /// 在内存中创建一张纹理的可读克隆体。
        /// </summary>
        /// <param name="source">源纹理</param>
        /// <returns>一张新的、可读的纹理</returns>
        private static Texture2D CreateReadableClone(Texture2D source)
        {
            // 1. 创建一个与源纹理同样大小的 RenderTexture
            RenderTexture renderTex = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);

            // 2. 将源纹理“画”到这个 RenderTexture 上
            Graphics.Blit(source, renderTex);

            // 3. 准备一个同样大小的、新的、可读的 Texture2D 来接收像素
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            Texture2D readableText = new Texture2D(source.width, source.height);

            // 4. 从 RenderTexture 中将像素读回到 CPU
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();

            // 5. 清理并返回结果
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            return readableText;
        }
    
        //gpu
        public static RenderTexture BakeRoadDataToTexture(RoadManager roadManager, int resolution, Shader bakerShader, float terrainMaxHeight)
        {
            var roadMesh = roadManager.MeshFilter.sharedMesh;
            if (roadMesh == null || roadMesh.vertexCount == 0) return null;

            var bakerMaterial = new Material(bakerShader);
            bakerMaterial.SetFloat("_MaxHeight", terrainMaxHeight);

            var worldBounds = GetWorldBounds(roadMesh, roadManager.transform);
            var rt = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            // --- 创建临时对象 ---
            var bakeObject = new GameObject("Temp Bake Object") { hideFlags = HideFlags.HideAndDontSave };
            bakeObject.AddComponent<MeshFilter>().sharedMesh = roadMesh;
            bakeObject.AddComponent<MeshRenderer>().sharedMaterial = bakerMaterial;
            bakeObject.transform.SetPositionAndRotation(roadManager.transform.position, roadManager.transform.rotation);
            bakeObject.transform.localScale = roadManager.transform.localScale;

            var camGO = new GameObject("Road Baker Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGO.AddComponent<Camera>();

            int tempLayer = 31;
            int originalLayer = roadManager.gameObject.layer;

            // --- [核心修复] 不再使用 try...finally ---

            // 1. 移动到临时图层
            bakeObject.layer = tempLayer;

            // 2. 配置相机
            ConfigureBakerCamera(cam, worldBounds, rt, tempLayer);

            // 3. 执行渲染
            cam.Render();

            // 4. [关键] 使用 delayCall 在下一帧安全地销毁所有临时对象和恢复图层
            EditorApplication.delayCall += () =>
            {
                if (camGO != null) UnityEngine.Object.DestroyImmediate(camGO);
                if (bakeObject != null) UnityEngine.Object.DestroyImmediate(bakeObject);
                // 确保原始对象图层被恢复
                if (roadManager != null) roadManager.gameObject.layer = originalLayer;
            };

            return rt;
        }

        private static void ConfigureBakerCamera(Camera cam, Bounds worldBounds, RenderTexture target, int layerMask)
        {
            cam.orthographic = true;
            cam.enabled = false;
            cam.cullingMask = 1 << layerMask;
            cam.targetTexture = target;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;

            var (viewMatrix, projMatrix) = GetBakeMatrices(worldBounds);
            cam.worldToCameraMatrix = viewMatrix;
            cam.projectionMatrix = projMatrix;
        }





        public static (Matrix4x4 view, Matrix4x4 proj) GetBakeMatrices(Bounds worldBounds)
        {
            Matrix4x4 viewMatrix = Matrix4x4.LookAt(
                worldBounds.center + Vector3.up * (worldBounds.extents.y + 1f),
                worldBounds.center,
                Vector3.forward
            );

            float padding = 2f;
            Matrix4x4 projectionMatrix = Matrix4x4.Ortho(
                -worldBounds.extents.x - padding, worldBounds.extents.x + padding,
                -worldBounds.extents.z - padding, worldBounds.extents.z + padding,
                0.01f, worldBounds.size.y + 2f
            );
            return (viewMatrix, projectionMatrix);
        }

        // 计算 Mesh 的世界空间 Bounds
        public static Bounds GetWorldBounds(Mesh mesh, Transform transform)
        {
            if (mesh == null || mesh.vertexCount == 0) return new Bounds(transform.position, Vector3.zero);
            var vertices = mesh.vertices;
            var min = transform.TransformPoint(vertices[0]);
            var max = min;
            for (int i = 1; i < vertices.Length; i++)
            {
                var v = transform.TransformPoint(vertices[i]);
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            var center = (min + max) * 0.5f;
            var size = max - min;
            return new Bounds(center, size);
        }
    }
}