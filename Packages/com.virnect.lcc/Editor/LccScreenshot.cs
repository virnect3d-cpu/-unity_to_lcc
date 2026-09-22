using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Virnect.Lcc.Editor
{
    public static class LccScreenshot
    {
        [MenuItem("Virnect/LCC/📸 Screenshot Game View", priority = 60)]
        public static void Capture()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                foreach (var c in Object.FindObjectsOfType<Camera>())
                    if (c.enabled) { cam = c; break; }
            }
            if (cam == null) { Debug.LogError("[LCC-shot] no camera"); return; }

            const int W = 1280, H = 720;
            // R16G16B16A16_SFloat — aras-p Gaussian Splatting URP feature 가 요구하는 포맷.
            // 일반 ARGB32 로 캡쳐하면 URP RenderGraph 가 capture path 에서 feature 를
            // 스킵하는 케이스가 있다.
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            // Unity 6 URP — SubmitRenderRequest 를 쓰면 정상 URP 파이프 전체(=feature 포함) 가
            // 실행된다. Camera.Render() 는 일부 feature 가 스킵되는 케이스가 있다.
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req))
            {
                RenderPipeline.SubmitRenderRequest(cam, req);
            }
            else
            {
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;
            }

            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = active;

            byte[] png = tex.EncodeToPNG();
            string outDir = Path.Combine(Application.dataPath, "..", "_shots");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "lcc_gameview.png").Replace('\\', '/');
            File.WriteAllBytes(outPath, png);

            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log($"[LCC-shot] {png.Length:N0} bytes -> {outPath}");
        }
    }
}
