

using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoadSystem
{
    public static class Drawing
    {
        public static void SaveRenderTextureToPNG(RenderTexture rt, string fileName)
        {
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var old_rt = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old_rt;
            string directoryPath = Path.Combine(Application.dataPath, "RoadCreator", "Debug");
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
            string fullPath = Path.Combine(directoryPath, fileName);
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.Refresh();
            string relativePath = "Assets/RoadCreator/Debug/" + fileName;
            Debug.Log($"[调试] 已将最终测试结果保存到: <a href=\"{relativePath}\">{relativePath}</a>", AssetDatabase.LoadAssetAtPath<Texture>(relativePath));
        }


    }
}
