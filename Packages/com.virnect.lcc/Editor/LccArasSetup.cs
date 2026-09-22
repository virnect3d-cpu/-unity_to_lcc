using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Virnect.Lcc.Editor
{
    /// aras-p UnityGaussianSplatting 의 GaussianSplatRenderer 로 LCC 씬을 와이어업.
    ///
    /// 동작:
    ///   1) 현재 씬에 __ArasRoot 생성 (이미 있으면 재사용) + -90X 적용
    ///   2) 프로젝트 안의 모든 GaussianSplatAsset 을 자동 발견
    ///   3) 에셋마다 자식 "ArasSplat_<name>" 생성 + GaussianSplatRenderer 부착
    ///   4) SplatScale=1, OpacityScale=1, SHOrder=3, SortNthFrame=1, RenderMode=Splats (=0)
    ///      — photoreal 프리셋
    ///
    /// 에셋 이름 규칙: "<name>_lod0" / "<name>_lod1" ... 접미사는 벗겨서 <name> 으로
    /// 묶고, 가장 낮은 LOD 번호(= 최고 밀도)를 대표로 고른다. 접미사가 없으면 그대로 쓴다.
    public static class LccArasSetup
    {
        // "Foo_lod0" → ("Foo", 0) · "Foo" → ("Foo", int.MaxValue)
        static (string baseName, int lod) ParseAssetName(string fileName)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                fileName, @"^(?<base>.+?)_lod(?<n>\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups["n"].Value, out var n))
                return (m.Groups["base"].Value, n);
            return (fileName, int.MaxValue);
        }

        [MenuItem("Virnect/LCC/🎬 Wire up aras-p Gaussian Splats", priority = 70)]
        public static void Run()
        {
            Type rendererType = null, assetType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (rendererType == null) rendererType = asm.GetType("GaussianSplatting.Runtime.GaussianSplatRenderer");
                if (assetType    == null) assetType    = asm.GetType("GaussianSplatting.Runtime.GaussianSplatAsset");
                if (rendererType != null && assetType != null) break;
            }
            if (rendererType == null || assetType == null)
            {
                Debug.LogError("[Aras] GaussianSplatting package not loaded. Check Packages/manifest.json.");
                return;
            }

            var scn = SceneManager.GetActiveScene();
            var root = GameObject.Find("__ArasRoot");
            if (root == null) root = new GameObject("__ArasRoot");
            // Maya 의 Freeze Transformations 효과 — 좌표계 변환(-90° X) 은 부모 __ArasRoot 가
            // 짊어지고, 각 ArasSplat_* child 는 완벽 identity 가 됨.
            Undo.RecordObject(root.transform, "Hoist -90X to __ArasRoot");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            root.transform.localScale    = Vector3.one;

            // 프로젝트 안의 모든 GaussianSplatAsset 을 자동 발견하고, LOD 접미사를
            // 벗긴 base 이름으로 묶는다. 같은 base 가 여러 LOD 로 있으면 가장 낮은
            // 번호(= 최고 밀도)를 대표로 쓴다.
            var picked = new Dictionary<string, (UnityEngine.Object asset, int lod, string file)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var g in AssetDatabase.FindAssets("t:GaussianSplatAsset"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath(p, assetType);
                if (a == null) continue;
                var file = System.IO.Path.GetFileNameWithoutExtension(p);
                var (baseName, lod) = ParseAssetName(file);
                if (!picked.TryGetValue(baseName, out var cur) || lod < cur.lod)
                    picked[baseName] = (a, lod, file);
            }
            Debug.Log($"[Aras] found {picked.Count} splat scene(s) in project");
            if (picked.Count == 0)
            {
                Debug.LogWarning("[Aras] GaussianSplatAsset 이 하나도 없다. " +
                                 ".lcc 를 Assets/LCC_Drops/ 에 드롭했는지, " +
                                 "또는 Tools → Gaussian Splats → Create GaussianSplatAsset 로 " +
                                 "에셋을 만들었는지 확인할 것.");
                return;
            }

            // Disable our home-grown LccSplatRenderer to avoid double-rendering
            int disabled = 0;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<LccSplatRenderer>(includeInactive: true))
            {
                if (!r.gameObject.activeSelf) continue;
                Undo.RecordObject(r.gameObject, "Disable LCC Splat");
                r.gameObject.SetActive(false);
                disabled++;
            }
            Debug.Log($"[Aras] disabled {disabled} LccSplatRenderer GameObject(s)");

            int wired = 0;
            foreach (var kv in picked)
            {
                var lccName  = kv.Key;
                var asset    = kv.Value.asset;
                var usedName = kv.Value.file;

                var goName = "ArasSplat_" + lccName;
                var child = root.transform.Find(goName)?.gameObject;
                if (child == null)
                {
                    child = new GameObject(goName);
                    child.transform.SetParent(root.transform, worldPositionStays: false);
                    Undo.RegisterCreatedObjectUndo(child, "Create ArasSplat");
                }
                // 표준 LCC 임포트 컨벤션 (2026-05-12 확정, Option B = wrapper hoist):
                //   ArasSplat_<name> 자체는 완벽 identity — Maya 의 Freeze 한 결과와 동일.
                //   좌표계 변환(-90° X) 은 부모 __ArasRoot 가 짊어진다.
                //   향후 정합·이동은 ArasSplat_ 자체에 자유롭게 적용 가능 (identity 가 anchor).
                Undo.RecordObject(child.transform, "Apply LCC standard frozen xform (identity)");
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale    = Vector3.one;

                var renderer = child.GetComponent(rendererType) ?? child.AddComponent(rendererType);

                // photoreal preset
                SetField(renderer, "m_Asset", asset);
                SetField(renderer, "m_SplatScale", 1f);
                SetField(renderer, "m_OpacityScale", 1f);
                SetField(renderer, "m_SHOrder", 3);
                SetField(renderer, "m_SHOnly", false);
                SetField(renderer, "m_SortNthFrame", 1);
                SetField(renderer, "m_RenderMode", 0); // 0 = Splats
                SetField(renderer, "m_PointDisplaySize", 3f);

                EditorUtility.SetDirty(renderer);
                Debug.Log($"[Aras]   ✓ {goName} ← {usedName}");
                wired++;
            }

            EditorSceneManager.MarkSceneDirty(scn);
            Debug.Log($"[Aras] done. {wired}/{picked.Count} wired up.");
        }

        static void SetField(object obj, string fieldName, object value)
        {
            var t = obj.GetType();
            var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) { f.SetValue(obj, value); return; }
            var p = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(obj, value); return; }
            Debug.LogWarning($"[Aras] field/property '{fieldName}' not found on {t.Name}");
        }
    }
}
