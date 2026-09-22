using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Virnect.Lcc.Editor
{
    /// Stage-2 prep — 모든 ArasSplat_<scene> 을 사용자가 만든 "Cube" 기준 박스 안으로 옮긴다.
    ///
    /// 규칙:
    ///   1) Cube 의 world AABB 를 컨테이너로 잡음 (transform.position ± lossyScale/2).
    ///   2) 각 ArasSplat_ 의 *world AABB* 을 GaussianSplatRenderer.bounds 에서 가져옴.
    ///   3) 수평축 (X, Z) 중 더 큰 쪽이 Cube 의 Z (긴 축) 와 정렬되도록 localRotation.y 를 0 또는 90 으로 선택.
    ///   4) localPosition 으로 scan 의 world bbox center 를 Cube center 로 옮김.
    ///   5) Scale 은 그대로 — 사용자 의도가 "회전 + 이동" 이지 "크기 줄임" 아님.
    ///
    /// Stage 1 (Freeze) 가 끝난 상태를 가정하므로:
    ///   - __ArasRoot.localRotation = (-90, 0, 0)
    ///   - 각 ArasSplat_*.local* = identity (이게 anchor — 우리는 이 위에 정합용 변환을 얹는다)
    public static class LccCubeFitter
    {
        const string CubeName = "Cube";
        const string ArasRoot = "__ArasRoot";

        // fit 에서 제외할 오브젝트. 기준이 되는 스캔(움직이면 안 되는 앵커)이 있으면
        // 여기에 "ArasSplat_<name>" 을 넣는다. 기본은 전부 fit 대상.
        static readonly string[] s_Excluded = { };
        static bool IsExcluded(string n) => Array.IndexOf(s_Excluded, n) >= 0;

        [MenuItem("Virnect/LCC/📦 Fit ArasSplats into Cube", priority = 80)]
        public static void Fit()
        {
            var cube = GameObject.Find(CubeName);
            if (cube == null) { Debug.LogError($"[CubeFit] no GameObject named '{CubeName}'"); return; }
            var aras = GameObject.Find(ArasRoot);
            if (aras == null) { Debug.LogError($"[CubeFit] no GameObject named '{ArasRoot}'"); return; }

            var cubePos = cube.transform.position;
            var cubeScale = cube.transform.lossyScale;
            var cubeHalf = cubeScale * 0.5f;
            var cubeMin = cubePos - cubeHalf;
            var cubeMax = cubePos + cubeHalf;
            Debug.Log($"[CubeFit] Cube AABB: min={cubeMin}, max={cubeMax}, size={cubeScale}");

            // longest horizontal axis of Cube (X or Z)
            bool cubeZIsLonger = cubeScale.z >= cubeScale.x;
            Debug.Log($"[CubeFit] Cube long-axis = {(cubeZIsLonger ? "Z" : "X")}");

            int fitted = 0;
            int childCount = aras.transform.childCount;
            Debug.Log($"[CubeFit] iterating {childCount} children of {ArasRoot}");
            for (int ci = 0; ci < childCount; ci++)
            {
                var t = aras.transform.GetChild(ci);
                Debug.Log($"[CubeFit]   child[{ci}] = {t.name}");
                if (!t.name.StartsWith("ArasSplat_")) continue;
                if (IsExcluded(t.name))
                {
                    // Middle 은 Stage 1 의 frozen identity (0,0,0)/(0,0,0)/(1,1,1) 로 강제 복귀.
                    Undo.RecordObject(t, "Keep excluded at identity");
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                    t.localScale    = Vector3.one;
                    Debug.Log($"[CubeFit]   {t.name}: EXCLUDED — reset to identity");
                    continue;
                }
                var mr = t.GetComponent<MeshRenderer>();
                // MeshRenderer 가 없으면 GaussianSplatRenderer 의 bounds 를 리플렉션으로 추출
                Bounds worldBounds;
                if (mr != null) worldBounds = mr.bounds;
                else if (!TryGetGsBounds(t, out worldBounds))
                {
                    Debug.LogWarning($"[CubeFit] {t.name}: cannot read bounds, skip");
                    continue;
                }

                var size = worldBounds.size;
                var center = worldBounds.center;

                // Decide if scan's X is its long horizontal axis
                bool scanXLonger = size.x > size.z;
                bool needsYawRotate = cubeZIsLonger ? scanXLonger : !scanXLonger;

                Undo.RecordObject(t, "Fit into Cube");

                // 사용자 요청 (2026-05-12): 회전만 적용, 포지션은 절대 건드리지 말 것 = (0,0,0) 유지.
                // yaw 는 child 의 *local Z 축* 으로만 회전.
                // 부모 __ArasRoot 가 -90X 를 짊어지므로 child 의 local Z 는 world Y 축에 매핑됨
                // → child Z 회전 = 월드 horizontal yaw → world XZ 평면에서 X↔Z 스왑 효과.
                t.localPosition = Vector3.zero;
                t.localRotation = needsYawRotate ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
                t.localScale    = Vector3.one;

                Debug.Log($"[CubeFit]   {t.name}: size={size} yaw90={needsYawRotate} (position kept at 000)");
                fitted++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[CubeFit] done — {fitted} ArasSplat_ moved.");
        }

        // Second pass — after MeshRenderer.bounds has auto-updated post-rotation, re-translate.
        [MenuItem("Virnect/LCC/📦 Fit ArasSplats into Cube (refine)", priority = 81)]
        public static void Refine()
        {
            var cube = GameObject.Find(CubeName);
            var aras = GameObject.Find(ArasRoot);
            if (cube == null || aras == null) { Debug.LogError("[CubeFit] missing Cube or __ArasRoot"); return; }

            var cubePos = cube.transform.position;
            int n = 0;
            int childCount2 = aras.transform.childCount;
            for (int ci = 0; ci < childCount2; ci++)
            {
                var t = aras.transform.GetChild(ci);
                if (!t.name.StartsWith("ArasSplat_")) continue;
                if (IsExcluded(t.name))
                {
                    // Middle 은 손대지 않음 — Stage 1 identity 유지
                    Undo.RecordObject(t, "Keep excluded at identity");
                    t.localPosition = Vector3.zero;
                    t.localRotation = Quaternion.identity;
                    t.localScale    = Vector3.one;
                    continue;
                }
                // 회전은 유지, 포지션만 (0,0,0) 으로 강제.
                Undo.RecordObject(t, "Refine — pin position to 000");
                t.localPosition = Vector3.zero;
                n++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[CubeFit] refine — {n} centers re-aligned.");
        }

        // Reset — Stage 1 frozen state 로 되돌림
        [MenuItem("Virnect/LCC/📦 Reset ArasSplats to Frozen (Stage 1)", priority = 82)]
        public static void Reset()
        {
            var aras = GameObject.Find(ArasRoot);
            if (aras == null) { Debug.LogError("[CubeFit] no __ArasRoot"); return; }
            int n = 0;
            int childCount3 = aras.transform.childCount;
            for (int ci = 0; ci < childCount3; ci++)
            {
                var t = aras.transform.GetChild(ci);
                if (!t.name.StartsWith("ArasSplat_")) continue;
                Undo.RecordObject(t, "Reset to frozen");
                t.localPosition = Vector3.zero;
                t.localRotation = Quaternion.identity;
                t.localScale    = Vector3.one;
                n++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[CubeFit] reset {n} ArasSplat_ to frozen identity.");
        }

        // GaussianSplatRenderer 는 MeshRenderer 가 아님 → 리플렉션으로 bounds 추출
        static bool TryGetGsBounds(Transform t, out Bounds bounds)
        {
            bounds = default;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var ty = c.GetType();
                if (ty.FullName != "GaussianSplatting.Runtime.GaussianSplatRenderer") continue;

                // GaussianSplatRenderer.m_Asset 의 BoundsMin/Max 를 쓰는 게 가장 정확
                var assetField = ty.GetField("m_Asset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var asset = assetField?.GetValue(c) as UnityEngine.Object;
                if (asset != null)
                {
                    var at = asset.GetType();
                    var mn = at.GetField("m_BoundsMin", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var mx = at.GetField("m_BoundsMax", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mn != null && mx != null)
                    {
                        var minLocal = (Vector3)mn.GetValue(asset);
                        var maxLocal = (Vector3)mx.GetValue(asset);
                        // transform 8 corners → world AABB
                        var corners = new Vector3[8];
                        int k = 0;
                        for (int i = 0; i < 2; i++)
                        for (int j = 0; j < 2; j++)
                        for (int l = 0; l < 2; l++)
                            corners[k++] = t.TransformPoint(new Vector3(
                                i == 0 ? minLocal.x : maxLocal.x,
                                j == 0 ? minLocal.y : maxLocal.y,
                                l == 0 ? minLocal.z : maxLocal.z));
                        var bMin = corners[0]; var bMax = corners[0];
                        for (int i = 1; i < 8; i++)
                        {
                            bMin = Vector3.Min(bMin, corners[i]);
                            bMax = Vector3.Max(bMax, corners[i]);
                        }
                        bounds = new Bounds((bMin + bMax) * 0.5f, bMax - bMin);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
