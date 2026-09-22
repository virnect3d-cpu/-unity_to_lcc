# LCC → Unity 파이프라인

> XGRIDS Lixel CyberColor `.lcc` (3D Gaussian Splatting) 스캔을 Unity 에서
> **포토리얼하게 렌더 + 충돌 가능한 프록시 메쉬**로 올리는 파이프라인.
> 이 레포는 **파이프라인(코드 + 씬 + 설정)만** 담는다. 스캔 데이터는 들어있지 않다.

**Unity 6000.3.10f1 · URP 17.3 · Render Graph 필수**

---

## 왜 데이터가 없나

빌드된 `GaussianSplatAsset` 은 씬 하나당 **200~620 MB** 다. 용량의 약 80% 가
SH-3 view-dependent 컬러(`_shs.bytes`)다. 실제 운영 프로젝트 기준 8개 씬 합계가
**2.6 GB** 라 git 에 넣을 물건이 아니다.

추가로 aras-p 의 **런타임은 MIT 지만 creator 도구는 원본 3DGS 라이선스**(비상업
제한)를 따른다. 빌드된 에셋 자체를 재배포하는 건 그 제약을 탄다.

그래서 이 레포는 **레시피와 자동화 코드**만 담는다. 각자 자기 `.lcc` 를 넣고
직접 빌드하면 된다. 아래 Quick start 대로 하면 드롭 한 번으로 끝난다.

---

## 사전 준비물

| 항목 | 필요한 이유 | 없으면 |
|---|---|---|
| **Unity 6.0+** (권장 6000.3.10f1) | aras-p URP 피처가 Render Graph 전용 | 컴파일 에러 |
| **URP Render Graph 켜짐** | 위와 동일 (Compatibility Mode 꺼야 함) | splat 안 보임 |
| **네트워크** | 첫 실행 시 aras-p 패키지를 GitHub 에서 받음 | 패키지 해석 실패 |
| `splat-transform` CLI *(선택)* | `.lcc` → Splat-PLY 자동 변환 (Stage 1) | 드롭 자동변환 불가 — Lixel Studio 로 수동 export 후 진행 |
| Python *(선택)* | v1 백엔드(포인트클라우드 재구성) 기능만 사용 | 해당 탭만 비활성. 표준 경로엔 불필요 |

`splat-transform` 설치 (Node.js 필요):
```bash
npm i -g @playcanvas/splat-transform     # v0.14.0+ 에서 .lcc 입력 지원
```
> 이게 없어도 **Lixel Studio / LUC 로 뽑은 Splat-PLY** 를 직접 넣으면 나머지
> 파이프라인(에셋 빌드 → Stage 2 → Stage 3 → 콜라이더)은 그대로 동작한다.

---

## Quick start

```bash
git clone <이 레포 URL>
```

1. **Unity Hub 에서 프로젝트 열기** (6000.3.10f1 또는 동일 6000.3 라인)
   - 첫 실행 시 `org.nesnausk.gaussian-splatting` 을 GitHub 에서 받아오므로
     네트워크가 필요하고 수 분 걸린다.

2. **URP 렌더러 피처 설치** — 프로젝트당 1회
   ```
   메뉴: Virnect → LCC → 🔧 Install Aras URP Feature
   ```
   > 이게 빠지면 `GaussianSplatRenderer.HasValidRenderSetup = false` 가 되어
   > **화면이 그냥 비어 보인다.** 가장 흔한 함정이다.

3. **`.lcc` 폴더를 `Assets/LCC_Drops/` 에 드롭**
   → `LccDropAutoImporter` 가 자동으로 변환 · 빌드 · 씬 spawn 까지 수행 (Stage 1).

4. **좌표계 정리**
   ```
   메뉴: Virnect → LCC → 🎬 Wire up aras-p Gaussian Splats
   ```
   → Stage 2 (`-90X` freeze) 적용.

5. **검증은 반드시 스크린샷으로**
   ```
   메뉴: Virnect → LCC → 📸 Screenshot Game View
   ```
   > 인스펙터 값만 보고 "됐다" 판단하지 말 것. 아래 함정 6번 참고.

---

## 3-Stage 워크플로 (locked, 2026-05-12)

각 stage 는 다음 stage 의 전제 조건이다. **순서대로 진행하고 건너뛰지 말 것.**

| Stage | 내용 | 결과 transform |
|---|---|---|
| **Stage 1** | `.lcc` 폴더 드롭 → 자동 import + `GaussianSplatAsset`(Quality=High) 빌드 + 씬 spawn | spawn 직후 raw |
| **Stage 2** | X 축 `-90°` 회전 후 **Freeze Transformations** | `__ArasRoot` 가 `-90X` 부담, 각 `ArasSplat_*` = identity |
| **Stage 3** | 씬의 박스(`Cube`) 기준 **Z 축 회전만** 으로 fit. 위치·스케일 변경 금지 | `localPosition=(0,0,0)`, `localScale=(1,1,1)`, `localRotation=(0,0,±90 or 0)` |

### Stage 1 — 자동 import 파이프

```
.lcc 폴더 드롭 (Assets/LCC_Drops/)
   → LccDropAutoImporter   (AssetPostprocessor 자동 발동)
   → LccConverter          (splat-transform → .ply 임시)
   → GaussianAssetBuilder  (aras-p GaussianSplatAsset, Quality=High)
   → 활성 씬에 자동 spawn:
       Splat_<name>
         ├─ __LccCollider   (MeshCollider, proxy mesh, Z-up→Y-up 변환)
         └─ _ArasP          (GaussianSplatRenderer + asset)
```

**Quality = High 프리셋** — 이 값 외로 만들지 말 것 (디테일 깨짐 / 색 어긋남):

| 항목 | 값 |
|---|---|
| PSNR | 57.77 dB |
| 압축 | 2.94× |
| Position quantization | Norm16 |
| Scale quantization | Norm16 |
| Color | Float16 ×4 |
| SH coefficients | Norm11 |

### Stage 2 — 왜 2단 구조인가

Maya 의 **Freeze Transformations + Delete History** 와 같은 결과를 만들기 위한
wrapper hoist 구조다.

```
__ArasRoot                          ← rotation (-90, 0, 0)
├── ArasSplat_<scene_A>             ← pure identity
├── ArasSplat_<scene_B>             ← pure identity
└── ...
```

| GameObject | localPosition | localRotation | localScale |
|---|---|---|---|
| `__ArasRoot` | `(0,0,0)` | **`(-90,0,0)`** | `(1,1,1)` |
| `ArasSplat_*` | `(0,0,0)` | **`(0,0,0)`** | `(1,1,1)` |

Maya 의 Freeze 는 회전을 vertex 좌표에 구워 transform 을 identity 로 만든다.
`GaussianSplatAsset` 의 position bytes 를 직접 재인코딩하는 것도 가능하지만
(SH 계수까지 Wigner-D 로 회전시켜야 해서 비용이 크다), 같은 사용자 효과를
부모 wrapper 로 달성한다. Z-up(LCC) → Y-up(Unity) 변환은 부모가 짊어진다.

화면이 거꾸로 / 옆으로 보이면 **transform 을 손대지 말고**:
1. Splat-PLY 가 Z-up(XGRIDS 기본)으로 export 됐는지 확인
2. Y-up 으로 잘못 나왔으면 converter 옵션 점검 → `.asset` 재생성

### Stage 3 — 박스 기준 fit

씬의 `Cube` 를 컨테이너로 삼아 각 `ArasSplat_*` 을 정렬한다.

**철칙:**
- `localPosition` 건드리지 말 것 — 항상 `(0,0,0)`
- `localScale` 건드리지 말 것 — 항상 `(1,1,1)`
- 허용된 변경은 **`localRotation` 의 Z 축 회전 (`0°` 또는 `±90°`)** 뿐

메뉴:
- `Virnect/LCC/📦 Fit ArasSplats into Cube` — 회전만 결정/적용
- `Virnect/LCC/📦 Fit ArasSplats into Cube (refine)` — 회전 유지, position 재핀
- `Virnect/LCC/📦 Reset ArasSplats to Frozen (Stage 1)` — frozen identity 복귀

> **호출 순서 주의** — Fit 을 연속 두 번 호출하면 첫 호출이 회전을 풀어버리는
> 케이스가 있다(회전된 splat 의 bounds 가 swap 되어 보여서). 항상 **Reset → Fit**.

스케일 fitting(박스 안에 완전히 넣기)은 디테일 손실을 동반하므로 명시적으로
요청받지 않는 한 쓰지 않는다.

---

## 렌더 경로 두 가지

| 경로 | 언제 | 품질 |
|---|---|---|
| **aras-p UnityGaussianSplatting** (표준) | 기본값. photoreal 필요할 때 | 풀 3DGS — per-Gaussian 회전, 3축 스케일, opacity, SH-3 |
| `LccSplatRenderer` (이 패키지 자체 구현, 폴백) | aras-p 의존성을 못 쓸 때 (라이선스 등) | 등방 빌보드 — SH 없음, 눈에 띄게 덜 포토리얼 |

**자작 셰이더 튜닝으로 시간 태우지 말 것.** photoreal 이 목표면 aras-p 로 직행한다.
폴백 경로의 `_MaxRadius` / scaleMul / falloff 조정은 aras-p 를 쓸 수 없을 때만.

### aras-p photoreal 프리셋

| 필드 | 값 | 비고 |
|---|---|---|
| `m_SplatScale` | **1.0** | 원본 스케일 사용 |
| `m_OpacityScale` | **1.0** | 올리면 분필같이 됨 |
| `m_SHOrder` | **3** | 풀 view-dependent 컬러 |
| `m_SHOnly` | false | |
| `m_SortNthFrame` | **1** | 매 프레임 재정렬 (알파 합성) |
| `m_RenderMode` | **0 / Splats** | 1·2·3 은 디버그용 points 모드 |

> 용량을 줄여야 하면 `m_SHOrder` 를 1 로 낮춘다. `_shs.bytes` 가 거의 사라져
> **씬당 70~80% 감소**. 대가는 view-dependent 반사감 손실(실내 설비 스캔이면 체감 작음).

---

## 흔한 함정

1. **`HasValidRenderSetup: false`** → URP 피처 미설치. Quick start 2번.
2. **`splatCount > 0` 인데 화면이 빔** → 카메라가 `m_BoundsMin..m_BoundsMax` 밖.
   per-camera gather 가 컬링한다.
3. **Game View 는 멀쩡한데 스크린샷 PNG 만 비어있음** → `Camera.Render()` 가
   URP RenderGraph 피처를 건너뛴다. Unity 6 의 `SubmitRenderRequest` 를 쓸 것:
   ```csharp
   var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
   if (RenderPipeline.SupportsRenderRequest(cam, req))
       RenderPipeline.SubmitRenderRequest(cam, req);
   else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
   ```
   `📸 Screenshot Game View` 메뉴가 이미 이 처리를 한다.
4. **immutable package 경고** — `Packages/com.unity.render-pipelines.universal/`
   안의 렌더러 데이터를 고치면 Unity 가 경고하고 패키지 업데이트 시 날아간다.
   프로젝트 로컬 렌더러(`Assets/Settings/PC_Renderer.asset`)도 같이 패치할 것.
   설치 메뉴는 양쪽 다 처리한다.
5. **점처럼 보임 / 거대한 흐린 방울** → 폴백 렌더러 쓸 때만 발생. 카메라 거리와
   `_MaxRadius` 클램프 문제다. aras-p 경로면 해당 없음.
6. **인스펙터 값만 보고 판단 금지** — 렌더링 변경은 반드시 Game View 를 PNG 로
   떠서 픽셀을 확인한다.

---

## 폴더 구조

```
Assets/
  Scenes/
    LccMain.unity        참조용 씬 — 실제 운영 씬의 계층 구조 그대로
    SampleScene.unity    빈 기본 씬
  LCC_Drops/             ★ 여기에 .lcc 폴더를 드롭 (gitignore)
  GaussianAssets/        빌드된 .asset + .bytes 출력 (gitignore)
  LCC_Generated/         프록시 메쉬 출력 (gitignore)
  Settings/              URP 렌더러 / 파이프라인 에셋
Packages/
  manifest.json          aras-p 패키지 참조
  com.virnect.lcc/       임베드 UPM 패키지 (아래)
```

> ⚠️ **`LccMain.unity` 는 에셋 참조가 전부 missing 으로 열린다.** 의도된 상태다.
> 실제 스캔 데이터가 레포에 없기 때문이다. 이 씬은 **계층 구조 참고용** —
> `__ArasRoot` / `ArasSplat_*` / `__LccCollider` / `Cube` 의 배치를 보라는 것이지
> 그대로 실행하라는 게 아니다. 본인 데이터로 시작하려면 `SampleScene.unity` 에서
> Quick start 3번(드롭)부터 하면 된다.

### `Packages/com.virnect.lcc/`

| 폴더 | 내용 |
|---|---|
| `Editor/` | 15 스크립트 / 3,258 줄 — 임포터 창, 자동 import, 프록시 메쉬 베이커, Cube fitter, aras-p 셋업, URP 피처 설치, 스크린샷 |
| `Runtime/` | 10 스크립트 / 814 줄 — LCC 디코더, 매니페스트 파서, LOD 스트리머, 폴백 splat/포인트 렌더러, 씬 머저, 콜리전 로더 |
| `Server~/` | Python 보조 스크립트 (`~` 라 Unity 가 무시) |

주요 Editor 스크립트:

| 파일 | 역할 |
|---|---|
| `LccImporterWindow.cs` (1,288줄) | 임포터 메인 UI |
| `LccScriptedImporter.cs` | `.lcc` 드롭 자동 감지 |
| `LccArasSetup.cs` | `🎬 Wire up aras-p Gaussian Splats` (Stage 2) |
| `LccArasUrpFeatureInstaller.cs` | `🔧 Install Aras URP Feature` |
| `LccCubeFitter.cs` | `📦 Fit ArasSplats into Cube` (Stage 3) |
| `LccProxyMeshBaker.cs` | 프록시 PLY → Mesh 에셋 |
| `LccColliderBuilder.cs` | MeshCollider 부착 |
| `LccScreenshot.cs` | `📸 Screenshot Game View` |

---

## 충돌 / 콜라이더

Gaussian splat 자체에는 **지오메트리가 없다.** 걷거나 부딪히려면 프록시 메쉬가
필요하다. Stage 1 이 `__LccCollider` (MeshCollider + Z-up→Y-up 변환된 프록시 메쉬)
를 자동으로 붙인다. 프록시 PLY 는 Lixel Studio / LUC export 에서 나온다.

---

## 데이터 준비 — `.lcc` → Splat-PLY

`GaussianSplatAsset` 을 만들려면 Splat-PLY 가 먼저 필요하다.

- **Lixel Studio** → Export → Gaussian Splat PLY
- 또는 **Lixel Universal Converter (LUC)**

그 다음 Unity 메뉴 `Tools → Gaussian Splats → Create GaussianSplatAsset` 에
PLY 를 넣으면 `.asset` + 사이드카 `.bytes` 5개(`_pos` `_col` `_oth` `_shs` `_chk`)
가 나온다. Stage 1 자동 import 를 쓰면 이 과정이 자동으로 돈다.

> `.lcc` 컨테이너 자체를 직접 파싱하지 말 것 — XGRIDS 독점 포맷이고 Lixel Studio
> 버전마다 on-disk 스키마가 바뀐다. 지원 경로는 SDK / LUC / Splat-PLY 세 가지다.

---

## 라이선스 주의

- **aras-p UnityGaussianSplatting 런타임** — MIT
- **aras-p creator 도구** — 원본 3DGS 라이선스 (비상업 제한). 이 도구로 만든
  SH 인코딩 에셋을 상업적으로 재배포하는 건 제약을 탄다.
- 상업 배포가 필요하면 폴백 경로(`LccSplatRenderer`) 또는 XGRIDS 공식 Unity SDK 를 검토할 것.

---

## 참고

- [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)
- [XGRIDS LCC Whitepaper](https://github.com/xgrids/LCCWhitepaper)
- 패키지 상세 문서: [`Packages/com.virnect.lcc/README.md`](Packages/com.virnect.lcc/README.md)
