using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Virnect.Lcc.Editor
{
    /// aras-p UnityGaussianSplatting 의 URP 렌더러 피처(GaussianSplatURPFeature) 를
    /// 프로젝트의 모든 ScriptableRendererData 에 추가. 이게 없으면 GaussianSplatRenderer 가
    /// HasValidRenderSetup=false 로 떠 화면에 아무것도 안 찍힘.
    public static class LccArasUrpFeatureInstaller
    {
        [MenuItem("Virnect/LCC/🔧 Install Aras URP Feature", priority = 71)]
        public static void Install()
        {
            Type featureType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                featureType = asm.GetType("GaussianSplatting.Runtime.GaussianSplatURPFeature");
                if (featureType != null) break;
            }
            if (featureType == null)
            {
                Debug.LogError("[Aras-URP] GaussianSplatURPFeature type not found. Is the package imported?");
                return;
            }

            int added = 0, skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (renderer == null) { continue; }

                bool already = renderer.rendererFeatures.Any(f => f != null && f.GetType() == featureType);
                if (already) { Debug.Log($"[Aras-URP] {path} — already has feature"); skipped++; continue; }

                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
                feature.name = "GaussianSplatURPFeature";

                // SerializedProperty 로 추가 (Unity 가 sub-asset로 처리할 수 있도록)
                AssetDatabase.AddObjectToAsset(feature, renderer);

                // 내부 list 에 추가 — 리플렉션 (m_RendererFeatures 는 보통 internal)
                var listField = typeof(ScriptableRendererData).GetField("m_RendererFeatures",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var mapField  = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var list = listField?.GetValue(renderer) as System.Collections.IList;
                if (list != null)
                {
                    list.Add(feature);
                    listField.SetValue(renderer, list);
                }

                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(renderer);
                Debug.Log($"[Aras-URP] ✓ added GaussianSplatURPFeature to {path}");
                added++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Aras-URP] done. added={added}, skipped={skipped}");
        }
    }
}
