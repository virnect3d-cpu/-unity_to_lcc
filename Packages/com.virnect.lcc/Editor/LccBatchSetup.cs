using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Virnect.Lcc.Editor
{
    /// Unity batchmode 진입점 — GUI 없이 LCC 6개 씬 완전 세팅.
    ///   Unity.exe -batchmode -projectPath ...
    ///              -executeMethod Virnect.Lcc.Editor.LccBatchSetup.RunOnce
    ///              -logFile -
    /// 동작:
    ///   1) AssetDatabase 리프레시 → .lcc 자동 import (LccScriptedImporter)
    ///   2) 새 씬 LccMain.unity 생성 (카메라+라이트 디폴트)
    ///   3) 각 LccScene 마다 GameObject "Splat_&lt;name&gt;" + LccSplatRenderer 부착
    ///      (enabled=false 로 두어 batchmode 에서 GPU 디코딩 회피)
    ///   4) LccColliderBuilder.BakeScene 호출 — PLY proxy 메시 → MeshCollider 자동 연결
    ///   5) 씬 저장 후 Exit
    public static class LccBatchSetup
    {
        const string DropsRoot   = "Assets/LCC_Drops";
        const string SceneFolder = "Assets/Scenes";
        const string ScenePath   = "Assets/Scenes/LccMain.unity";

        // 사용자 Unity 안에서 1-클릭 활성화 — batchmode 가 만든 씬을 열어둔 상태에서
        // LccSplatRenderer 가 disabled 라 가우시안이 안 보이는 케이스 해결용.
        [MenuItem("Virnect/LCC/▶ Activate Splats + Frame Camera", priority = 50)]
        public static void Menu_ActivateAllSplats()
        {
            var scn = SceneManager.GetActiveScene();
            int enabled = 0;
            LccScene first = null;
            foreach (var root in scn.GetRootGameObjects())
            {
                foreach (var r in root.GetComponentsInChildren<LccSplatRenderer>(includeInactive: true))
                {
                    if (first == null && r.scene != null) first = r.scene;
                    if (r.enabled) continue;
                    Undo.RecordObject(r, "Activate LccSplatRenderer");
                    r.enabled = true;
                    enabled++;
                }
            }
            _FrameCameraToBBox(scn, first);
            EditorSceneManager.MarkSceneDirty(scn);
            Debug.Log($"[LCC] activated {enabled} LccSplatRenderer + framed camera to first bbox");
        }

        public static void RunOnce()
        {
            int exitCode = 0;
            try
            {
                Debug.Log("[LCC-batch] === start ===");

                // 1) Asset DB 강제 동기 리프레시 → .lcc 6개 import
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[LCC-batch] AssetDatabase refreshed");

                // .lcc 파일 직접 ImportAsset (junction 통한 외부 .lcc 안정 import)
                var lccPaths = new List<string>();
                if (Directory.Exists(DropsRoot))
                {
                    foreach (var f in Directory.GetFiles(DropsRoot, "*.lcc", SearchOption.AllDirectories))
                        lccPaths.Add(f.Replace('\\', '/'));
                }
                Debug.Log($"[LCC-batch] found {lccPaths.Count} .lcc files");
                foreach (var p in lccPaths)
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);

                // 2) 출력 폴더 보장
                if (!AssetDatabase.IsValidFolder(SceneFolder))
                    AssetDatabase.CreateFolder("Assets", "Scenes");

                // 3) 새 씬을 LccScene 로드 전에 먼저 만든다 — Single 모드 NewScene 은
                //    기존 씬 ScriptableObject 참조를 destroy 시키므로 순서가 중요.
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var root = new GameObject("__LccRoot");

                // 4) 새 씬이 켜진 뒤 LccScene 에셋 로드 + GameObject 부착
                int attached = 0;
                var seen = new HashSet<string>();
                LccScene firstScene = null;
                foreach (var p in lccPaths)
                {
                    var sc = AssetDatabase.LoadAssetAtPath<LccScene>(p);
                    if (sc == null) { Debug.LogWarning("[LCC-batch] LccScene load fail: " + p); continue; }
                    if (!seen.Add(sc.name)) { Debug.Log("[LCC-batch] dedupe skip " + sc.name); continue; }
                    if (firstScene == null) firstScene = sc;

                    var go = new GameObject("Splat_" + sc.name);
                    go.transform.SetParent(root.transform, worldPositionStays: false);
                    var r = go.AddComponent<LccSplatRenderer>();
                    r.scene           = sc;
                    r.lodLevel        = 2;
                    r.scaleMultiplier = 1.5f;
                    r.opacityBoost    = 0.3f;
                    r.tint            = Color.white;
                    r.enabled         = true; // 켜둬야 OnEnable→_TryLoad 가 호출됨 (Unity 열 때)
                    attached++;
                    Debug.Log("[LCC-batch] attached " + sc.name);
                }
                Debug.Log("[LCC-batch] " + attached + " Splat_* GameObjects created");

                // 카메라를 첫 LCC scene 의 bbox 중심으로 자동 프레이밍 (없으면 origin)
                _FrameCameraToBBox(newScene, firstScene);

                // 콜라이더 wiring — 각 Splat_* 마다 PLY 우선, 없으면 OBJ subasset fallback.
                // BakeScene 의 분기 한계(PLY 없으면 __LccCollider 자식도 안 만듦)를 피해
                // 한 패스로 단순화.
                int colWired = 0, colSkipped = 0;
                foreach (var t in newScene.GetRootGameObjects())
                    _WireCollidersSubtree(t.transform, ref colWired, ref colSkipped);
                Debug.Log($"[LCC-batch] colliders wired={colWired} skipped={colSkipped}");

                // 5) 씬 저장
                bool saved = EditorSceneManager.SaveScene(newScene, ScenePath);
                Debug.Log("[LCC-batch] save " + ScenePath + " = " + saved);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[LCC-batch] === done ===");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LCC-batch] EXCEPTION: " + e.Message + "\n" + e.StackTrace);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        static void _FrameCameraToBBox(UnityEngine.SceneManagement.Scene scene, LccScene refScene)
        {
            if (refScene?.manifest?.boundingBox == null) return;
            var bb = refScene.manifest.boundingBox;
            var min = new Vector3(bb.min[0], bb.min[1], bb.min[2]);
            var max = new Vector3(bb.max[0], bb.max[1], bb.max[2]);
            var center = (min + max) * 0.5f;
            var size   = max - min;
            float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            Camera cam = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                cam = go.GetComponent<Camera>();
                if (cam != null) break;
                cam = go.GetComponentInChildren<Camera>();
                if (cam != null) break;
            }
            if (cam == null) { Debug.LogWarning("[LCC-batch] no camera to frame"); return; }

            cam.transform.position = center + new Vector3(radius * 0.9f, radius * 0.6f, -radius * 0.9f);
            cam.transform.LookAt(center);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane  = Mathf.Max(cam.farClipPlane, radius * 5f);
            Debug.Log($"[LCC-batch] camera framed: center={center}, radius={radius:F1}m");
        }

        // 각 Splat_<scene> 에 콜라이더 부착 — PLY 우선, 없으면 OBJ subasset.
        // 항상 __LccCollider 자식 + MeshCollider 생성 (mesh 가 잡혔을 때만).
        static void _WireCollidersSubtree(Transform t, ref int wired, ref int skipped)
        {
            if (t.name.StartsWith(LccColliderBuilder.SplatNamePrefix))
            {
                string sceneName = t.name.Substring(LccColliderBuilder.SplatNamePrefix.Length);
                Mesh mesh = null;
                string source = null;

                // 1) PLY proxy (LccProxyMeshBaker → ProxyMesh.asset)
                var bake = LccProxyMeshBaker.BakeBySceneName(sceneName, forceRebuild: false);
                if (bake.mesh != null)
                {
                    mesh = bake.mesh;
                    source = bake.assetPath + (bake.reused ? " (reused)" : "");
                }
                else
                {
                    // 2) OBJ fallback — Unity ModelImporter 가 만든 Mesh subasset 사용
                    var objPath = $"{DropsRoot}/{sceneName}/mesh-files/{sceneName}.obj";
                    if (File.Exists(objPath))
                    {
                        AssetDatabase.ImportAsset(objPath, ImportAssetOptions.ForceSynchronousImport);
                        var meshes = AssetDatabase.LoadAllAssetsAtPath(objPath).OfType<Mesh>().ToArray();
                        if (meshes.Length > 0)
                        {
                            mesh = meshes[0];
                            source = objPath + " (OBJ subasset)";
                        }
                    }
                }

                if (mesh == null)
                {
                    Debug.LogWarning($"[LCC-batch]   ✗ no PLY or OBJ for {sceneName}");
                    skipped++;
                }
                else
                {
                    var colGo = new GameObject(LccColliderBuilder.ColliderChildName);
                    colGo.transform.SetParent(t, worldPositionStays: false);
                    var mc = colGo.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = false;
                    Debug.Log($"[LCC-batch]   ✓ {t.name} ← {source} ({mesh.vertexCount:N0} v)");
                    wired++;
                }
            }
            for (int i = 0; i < t.childCount; i++)
                _WireCollidersSubtree(t.GetChild(i), ref wired, ref skipped);
        }
    }
}
