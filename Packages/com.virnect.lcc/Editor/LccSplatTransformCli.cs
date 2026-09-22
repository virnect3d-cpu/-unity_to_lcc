using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Virnect.Lcc.Editor
{
    /// SuperSplat(PlayCanvas) splat-transform CLI 래퍼.
    /// LCC 입력을 그대로 받아 LOD/필터/콜리전메시/복셀/SOG 까지 변환.
    ///
    /// 설치: `npm i -g @playcanvas/splat-transform` (v0.14.0+ — LCC read 지원)
    ///
    /// 호출 패턴은 LccServerManager._RunProcess 와 동일 (stdout/stderr 라인 콜백).
    public static class LccSplatTransformCli
    {
        public const string ExePref = "Virnect.Lcc.SplatXformExe";
        public const string DefaultExe = "splat-transform"; // PATH 에 있다고 가정

        public static string ExePath
        {
            get => EditorPrefs.GetString(ExePref, DefaultExe);
            set => EditorPrefs.SetString(ExePref, value);
        }

        /// CLI 가용 여부 + 버전 문자열. 짧은 타임아웃(5s) 으로 즉시 응답.
        public static bool IsAvailable(out string versionOrError)
        {
            try
            {
                var psi = new ProcessStartInfo(ExePath, "--version")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p == null) { versionOrError = "Process.Start returned null"; return false; }
                string so = p.StandardOutput.ReadToEnd();
                string se = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                if (p.ExitCode == 0) { versionOrError = (so + se).Trim(); return true; }
                versionOrError = "exit " + p.ExitCode + " — " + (so + se).Trim();
                return false;
            }
            catch (Exception e)
            {
                versionOrError = e.Message + " (CLI 미설치? `npm i -g @playcanvas/splat-transform`)";
                return false;
            }
        }

        /// 인자 그대로 실행. 라인 콜백으로 stdout/stderr 스트림. 10 분 타임아웃.
        public static int Run(string args, Action<string> onLine)
        {
            try
            {
                var psi = new ProcessStartInfo(ExePath, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding  = Encoding.UTF8,
                };
                using var p = Process.Start(psi);
                if (p == null) { onLine?.Invoke("[error] Process.Start returned null"); return -1; }
                p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
                p.ErrorDataReceived  += (s, e) => { if (e.Data != null) onLine?.Invoke("[stderr] " + e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit(1000 * 60 * 10);
                return p.ExitCode;
            }
            catch (Exception e)
            {
                onLine?.Invoke("[exception] " + e.Message);
                return -1;
            }
        }

        // ── LCC 전용 액션 빌더 ───────────────────────────────────────────
        // 각 메서드는 args 문자열을 빌드해 Run() 호출.

        /// LCC → PLY (LOD 선택). lod < 0 이면 전체.
        public static int ExportPly(string lccPath, int lod, string outPly, Action<string> onLine)
            => Run(_LodOpt(lod) + Q(lccPath) + " -w " + Q(outPly), onLine);

        /// LCC → 콜리전 GLB 메시.
        ///   1) NaN/Inf 제거 (-N)
        ///   2) 플로터 제거 (-G) — 어떤 솔리드 복셀에도 기여 안 하는 splat 컷
        ///   3) voxel-params size,opacity (기본 0.05, 0.1)
        ///   4) --collision-mesh smooth → *.glb
        public static int BakeCollisionMesh(string lccPath, int lod,
            float voxelSize, float opacityThreshold, bool smooth, string outGlb, Action<string> onLine)
        {
            string mode = smooth ? "smooth" : "faces";
            string args = _LodOpt(lod) + Q(lccPath) +
                          " -N -G" +
                          " --voxel-params " + F(voxelSize) + "," + F(opacityThreshold) +
                          " --collision-mesh " + mode +
                          " -w " + Q(outGlb);
            return Run(args, onLine);
        }

        /// LCC → 희소 복셀 옥트리 JSON (Unity 라이트 콜라이더용).
        public static int ExportVoxelJson(string lccPath, int lod,
            float voxelSize, float opacityThreshold, string outVoxelJson, Action<string> onLine)
        {
            string args = _LodOpt(lod) + Q(lccPath) +
                          " --voxel-params " + F(voxelSize) + "," + F(opacityThreshold) +
                          " -w " + Q(outVoxelJson);
            return Run(args, onLine);
        }

        /// LCC → SOG 압축 번들 (PlayCanvas viewer/HTML 호환).
        ///   shBands: 0~3 (SH 밴드 절단 → 75% 차지하는 SH 슬림화)
        ///   decimatePct: 0 또는 음수면 미적용, 그 외 1~100 %
        ///   morton: Z-order 재정렬 (GPU 캐시 + 압축률)
        ///   iterations: SH 양자화 반복 (10 기본 — 클수록 품질↑)
        public static int CompressToSog(string lccPath, int lod,
            int shBands, int decimatePct, bool morton, int iterations,
            string outSog, Action<string> onLine)
        {
            var sb = new StringBuilder();
            sb.Append(_LodOpt(lod));
            sb.Append(Q(lccPath));
            sb.Append(" -N -G");
            if (shBands >= 0 && shBands <= 3) sb.Append(" -H ").Append(shBands);
            if (decimatePct > 0 && decimatePct < 100) sb.Append(" -F ").Append(decimatePct).Append('%');
            if (morton) sb.Append(" -M");
            if (iterations > 0) sb.Append(" -i ").Append(iterations);
            sb.Append(" -w ").Append(Q(outSog));
            return Run(sb.ToString(), onLine);
        }

        /// 시드 위치 기반 연결 클러스터만 유지 (외곽 노이즈/도로 제거).
        public static int FilterClusterToPly(string lccPath, int lod,
            Vector3 seedPos, string outPly, Action<string> onLine)
        {
            string args = _LodOpt(lod) + Q(lccPath) +
                          " -N -D" +
                          " --seed-pos " + F(seedPos.x) + "," + F(seedPos.y) + "," + F(seedPos.z) +
                          " -w " + Q(outPly);
            return Run(args, onLine);
        }

        /// 컬럼 통계 출력 (분석용 — null sink 으로 변환 안 함).
        public static int SummarizeLcc(string lccPath, int lod, Action<string> onLine)
            => Run(_LodOpt(lod) + Q(lccPath) + " --summary -w null", onLine);

        // ── helpers ──────────────────────────────────────────────────────
        static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
        static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        static string _LodOpt(int lod) => lod >= 0 ? "-O " + lod + " " : "";
    }
}
