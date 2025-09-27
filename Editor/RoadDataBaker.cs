using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace RoadSystem
{
    public static class RoadDataBaker
    {
        public static RenderTexture BakeRoadDataToTexture(RoadManager roadManager, int resolution, Shader bakerShader, float terrainMaxHeight)
        {
            var roadMesh = roadManager.MeshFilter.sharedMesh;
            if (roadMesh == null || roadMesh.vertexCount == 0) return null;

            var bakerMaterial = new Material(bakerShader);
            bakerMaterial.SetFloat("_MaxHeight", terrainMaxHeight);

            var worldBounds = roadMesh.GetWorldBounds(roadManager.transform);
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
                if (camGO != null) Object.DestroyImmediate(camGO);
                if (bakeObject != null) Object.DestroyImmediate(bakeObject);
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
    }
}