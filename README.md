# LCC → Unity 파이프라인

XGRIDS Lixel CyberColor `.lcc` (3D Gaussian Splatting) 스캔을 Unity 에서
**포토리얼하게 렌더하고, 걸어다닐 수 있게 콜라이더까지 붙이는** 파이프라인.

**Unity 6000.3.10f1 · URP 17.3 · Render Graph**

> 이 레포는 **파이프라인(코드 + 씬 + 설정)만** 담는다. 스캔 데이터(`.lcc`)와
> 빌드된 `GaussianSplatAsset` 은 들어있지 않다 — [왜 없나](#왜-데이터가-없나).
> 전체 707 KB.

---

## 목차

- [30초 요약](#30초-요약)
- [사전 준비물](#사전-준비물)
- [Quick start](#quick-start)
- [처음 열면 이렇게 보인다](#처음-열면-이렇게-보인다)
- [3-Stage 워크플로](#3-stage-워크플로-locked)
- [렌더 경로 두 가지](#렌더-경로-두-가지)
- [충돌 / 콜라이더](#충돌-콜라이더)
- [메뉴 레퍼런스](#메뉴-레퍼런스)
- [흔한 함정](#흔한-함정)
- [폴더 구조](#폴더-구조)
- [왜 데이터가 없나](#왜-데이터가-없나)
- [라이선스 주의](#라이선스-주의)

---

## 30초 요약

```
.lcc  ──drop──►  Assets/LCC_Drops/
                      │
                      ▼  Stage 1 (자동)
            Splat-PLY 변환 → GaussianSplatAsset 빌드 → 씬 spawn
                      │
                      ▼  Stage 2 — 메뉴 🎬 Wire up
            __ArasRoot 가 -90X 부담 / 자식은 identity  (Z-up → Y-up)
                      │
                      ▼  Stage 3 — 메뉴 📦 Fit into Cube (선택)
            박스 기준 Z축 회전만으로 정렬
                      │
                      ▼
            포토리얼 splat + MeshCollider 완성
```

핵심 규칙 세 개만 기억하면 된다.

1. **렌더는 aras-p** — 자작 셰이더 튜닝으로 시간 쓰지 말 것.
2. **URP 피처 설치는 필수** — 빠지면 화면이 그냥 빈다.
3. **검증은 스크린샷으로** — 인스펙터 값만 보고 "됐다" 하지 말 것.

깔아야 할 것은 [사전 준비물](#사전-준비물)에 전부 정리해뒀다. 요약하면
**Unity 6 + (자동으로 받아지는 URP·aras-p) + 선택적으로 `splat-transform` CLI.**

---

## 사전 준비물

LCC 를 Unity 에 올리려면 **아래 스택이 갖춰져 있어야 한다.** 필수 4개 +
선택 3개다. 하나씩 설치법과 확인법을 적는다.

### 한눈에

| # | 항목 | 필수 | 어디서 오나 |
|---|---|---|---|
| 1 | Unity **6.0 이상** (권장 `6000.3.10f1`) | ✅ | Unity Hub |
| 2 | **URP** `com.unity.render-pipelines.universal` 17.3 | ✅ | `manifest.json` 자동 |
| 3 | **URP Render Graph 켜짐** (Compatibility Mode OFF) | ✅ | 이 레포 설정에 포함 |
| 4 | **aras-p UnityGaussianSplatting** | ✅ | `manifest.json` 자동 (GitHub) |
| 5 | `com.unity.mathematics` 1.3.2 | ✅ | `manifest.json` 자동 |
| 6 | **Node.js + `splat-transform` CLI** | ⬜ 선택 | `npm` |
| 7 | Lixel Studio 또는 LUC | ⬜ 선택 | XGRIDS |
| 8 | Python 3.x | ⬜ 선택 | v1 백엔드 탭 전용 |

2·4·5 는 프로젝트를 열면 Unity 가 알아서 받는다. **사람이 직접 챙길 건
1번(에디터 버전)과, 필요시 6·7번(변환 도구)뿐이다.**

---

### 1. Unity 6.0 이상 — 필수

aras-p 의 URP 피처가 **Render Graph 전용**이라 Unity 6 미만에서는 아예
컴파일되지 않는다 (패키지 안에 `#error` 가 박혀 있다).

```
Unity Hub → Installs → Install Editor → 6000.3.10f1
```

이 레포의 `ProjectSettings/ProjectVersion.txt` 가 `6000.3.10f1` 이다. 같은
6000.3 라인이면 Hub 가 그대로 열어준다. 다른 라인(6000.0 등)으로 열면 패키지가
자동 승급되면서 경고가 날 수 있다.

**확인:** Unity 타이틀바 또는 `Help → About Unity`

---

### 2·5. URP + Mathematics — 자동

`Packages/manifest.json` 에 이미 박혀 있다. 프로젝트를 열면 Unity Package
Manager 가 받아온다.

```jsonc
"com.unity.render-pipelines.universal": "17.3.0",
"com.unity.mathematics": "1.3.2",
```

**확인:** `Window → Package Manager → In Project` 에 두 개가 보이면 된다.

---

### 3. URP Render Graph — 필수 (이 레포에 이미 설정됨)

Compatibility Mode 가 켜져 있으면 splat 이 **렌더되지 않는다.**

```
Project Settings → Graphics → URP Global Settings
  → Render Graph → Compatibility Mode (Render Graph Disabled)  ☐ 꺼짐이어야 함
```

이 레포는 `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` 에
`m_EnableRenderCompatibilityMode: 0` 으로 저장돼 있다. **건드리지 말 것.**

---

### 4. aras-p UnityGaussianSplatting — 필수, 자동 (네트워크 필요)

포토리얼 렌더의 본체다. `manifest.json` 이 GitHub 에서 직접 받아온다.

```jsonc
"org.nesnausk.gaussian-splatting":
  "https://github.com/aras-p/UnityGaussianSplatting.git?path=/package"
```

> ⚠️ **첫 실행 때 네트워크가 반드시 필요하다.** git URL 패키지라 사내망에서
> GitHub 가 막혀 있으면 프로젝트가 안 열린다. 그 경우 저장소를 사내 미러로
> 옮기고 manifest 의 URL 을 바꿔야 한다.

받아온 뒤 **반드시 URP 피처를 설치해야 한다** — 이건 자동이 아니다:

```
메뉴: Virnect → LCC → 🔧 Install Aras URP Feature
```

**확인 두 가지:**
- `Window → Package Manager → In Project` 에 *Gaussian Splatting* 이 보인다
- `Assets/Settings/PC_Renderer.asset` 인스펙터의 Renderer Features 에
  `GaussianSplatURPFeature` 가 있다

---

### 6. Node.js + `splat-transform` CLI — 선택 (Stage 1 자동변환용)

`.lcc` 를 드롭했을 때 **자동으로 Splat-PLY 로 변환**해주는 도구다.
없으면 Stage 1 의 자동 변환만 안 되고, 나머지 파이프라인은 그대로 돈다.

```bash
# 1) Node.js 설치 (https://nodejs.org, LTS 권장)
node --version     # v18 이상

# 2) CLI 전역 설치
npm i -g @playcanvas/splat-transform

# 3) 확인
splat-transform --version    # v0.14.0 이상이어야 .lcc 직접 읽기 지원
```

Unity 는 이 실행파일을 **PATH 에서 찾는다.** 다른 위치에 있으면
`Virnect → LCC Importer` 창의 splat-transform 탭에서 절대경로를 지정할 수 있다.

**확인:** `Virnect → LCC Importer` → splat-transform 탭 → 버전이 표시되면 OK

---

### 7. Lixel Studio 또는 LUC — 선택 (6번의 대안)

XGRIDS 공식 도구. `.lcc` 를 열어 **Export → Gaussian Splat PLY** 로 뽑는다.
6번 CLI 를 안 쓸 거라면 이 경로로 PLY 를 만들어 `Tools → Gaussian Splats →
Create GaussianSplatAsset` 에 직접 넣으면 된다.

콜라이더용 프록시 메쉬(`mesh-files/<scene>.ply`)도 이 도구가 함께 내보낸다.

---

### 8. Python 3.x — 선택 (v1 백엔드 탭 전용)

`Virnect → LCC Importer` 의 **v1 서버 탭**(포인트클라우드 → Poisson/BPA 재구성)
에서만 쓴다. 표준 파이프라인에는 **불필요하다.**

쓸 거라면 `Virnect → LCC Importer` 에서 Python 경로와 v1 루트를 지정한다
(`EditorPrefs` 에 저장되며, 기본값은 비어 있다).

---

### 설치 안 해도 되는 것

- **XGRIDS Unity SDK** — 이 파이프라인은 aras-p 경로를 쓴다. SDK 는 대안일 뿐.
- **CUDA / GPU 학습 환경** — 이미 재구성된 `.lcc` 를 쓰는 것이라 불필요.
- **CloudCompare / Open3D** — 오프라인 분석용. 파이프라인과 무관.

---

## Quick start

```bash
git clone https://github.com/virnect3d-cpu/-unity_to_lcc.git
```

**1. Unity Hub 에서 프로젝트 열기** — 6000.3.10f1 또는 동일 6000.3 라인

첫 실행 때 URP · Mathematics · **aras-p Gaussian Splatting** 을 받아온다.
aras-p 는 GitHub git URL 이라 **네트워크가 필요하고** 수 분 걸린다.
([준비물 4번](#4-aras-p-unitygaussiansplatting-—-필수-자동-네트워크-필요))

콘솔에 빨간 에러 없이 임포트가 끝나면 다음으로.

**2. URP 렌더러 피처 설치** — 프로젝트당 딱 1회

```
메뉴: Virnect → LCC → 🔧 Install Aras URP Feature
```

> 이게 빠지면 `GaussianSplatRenderer.HasValidRenderSetup = false` 가 되어
> **화면이 그냥 비어 보인다.** 가장 흔한 사고다.

**3. `.lcc` 폴더를 `Assets/LCC_Drops/` 에 드롭**

`LccDropAutoImporter` 가 자동으로 변환 · 빌드 · 씬 spawn 까지 수행 (Stage 1).

**4. 좌표계 정리**

```
메뉴: Virnect → LCC → 🎬 Wire up aras-p Gaussian Splats
```

프로젝트 안의 `GaussianSplatAsset` 을 전부 찾아 `__ArasRoot` 아래에 붙이고
Stage 2 (`-90X` freeze) 를 적용한다.

**5. 검증은 반드시 스크린샷으로**

```
메뉴: Virnect → LCC → 📸 Screenshot Game View
```

인스펙터 값만 보고 판단하지 말 것. [함정 3번](#흔한-함정) 참고.

---

## 처음 열면 이렇게 보인다

`Assets/Scenes/` 에 씬이 두 개 있다.

| 씬 | 용도 |
|---|---|
| `SampleScene.unity` | **여기서 시작.** 비어 있는 기본 씬 — 본인 `.lcc` 드롭용 |
| `LccMain.unity` | **구조 참고용.** 실제 운영 씬의 계층을 그대로 남겨둔 것 |

### `LccMain.unity` 의 계층

```
__ArasRoot                          rotation (-90, 0, 0)   ← 좌표계 변환 담당
├── ArasSplat_Scan_A_Cutter         identity               ← aras-p 렌더 경로
│   └── (GaussianSplatRenderer)
├── ArasSplat_Scan_B_Facility01     identity
└── ... (6개)

__LccRoot
├── Splat_Scan_A_Cutter                                    ← 폴백 렌더 경로
│   ├── __LccCollider  (MeshCollider)
│   └── (LccSplatRenderer)
└── ... (6개)

Cube                                                       ← Stage 3 fit 기준 박스
Main Camera
Directional Light
```

### ⚠️ `LccMain.unity` 은 슬롯이 비어 있다 — 정상이다

데이터를 레포에 넣지 않았으므로, 이 씬을 열면:

- `GaussianSplatRenderer.m_Asset` 6개 → **None**
- `MeshCollider.m_Mesh` 4개 → **None**

**하지만 "missing script" 빨간 경고는 뜨지 않는다.** 컴포넌트와 스크립트는
정상적으로 붙고 슬롯만 비어 있는 상태다. 계층 구조와 컴포넌트 설정값을
확인하는 목적으로는 그대로 쓸 수 있다.

> 오브젝트 이름의 `Scan_A` ~ `Scan_F` 는 익명화한 것이다. 실제로는 스캔 폴더명이
> 그대로 들어간다 (`ArasSplat_<폴더명>`).

본인 데이터로 시작하려면 `SampleScene.unity` 를 열고 [Quick start 3번](#quick-start)부터.

---

## 3-Stage 워크플로 (locked)

각 stage 는 다음 stage 의 전제 조건이다. **순서대로 진행하고 건너뛰지 말 것.**

| Stage | 내용 | 결과 transform |
|---|---|---|
| **Stage 1** | `.lcc` 드롭 → 자동 import + `GaussianSplatAsset`(Quality=High) 빌드 + 씬 spawn | spawn 직후 raw |
| **Stage 2** | X 축 `-90°` 회전 후 **Freeze Transformations** | `__ArasRoot` 가 `-90X` 부담, 각 `ArasSplat_*` = identity |
| **Stage 3** | 씬의 박스(`Cube`) 기준 **Z 축 회전만** 으로 fit | `localPosition=(0,0,0)`, `localScale=(1,1,1)`, `localRotation=(0,0,±90 or 0)` |

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

| GameObject | localPosition | localRotation | localScale |
|---|---|---|---|
| `__ArasRoot` | `(0,0,0)` | **`(-90,0,0)`** | `(1,1,1)` |
| `ArasSplat_*` | `(0,0,0)` | **`(0,0,0)`** | `(1,1,1)` |

Maya 의 Freeze 는 회전을 vertex 좌표에 구워 transform 을 identity 로 만든다.
`GaussianSplatAsset` 의 position bytes 를 직접 재인코딩하는 것도 가능하지만
(SH 계수까지 Wigner-D 로 회전시켜야 해서 비용이 크다), 같은 사용자 효과를
부모 wrapper 로 달성한다. Z-up(LCC) → Y-up(Unity) 변환은 부모가 짊어진다.

덕분에 각 `ArasSplat_*` 의 local transform 이 identity 라, 이후 정합·이동을
여기에 그대로 얹을 수 있다 (identity 가 anchor).

**화면이 거꾸로 / 옆으로 보이면 transform 을 손대지 말고:**

1. Splat-PLY 가 Z-up (XGRIDS 기본) 으로 export 됐는지 확인
2. Y-up 으로 잘못 나왔으면 converter 옵션 점검 → `.asset` 재생성

### Stage 3 — 박스 기준 fit

씬의 `Cube` 를 컨테이너로 삼아 각 `ArasSplat_*` 을 정렬한다.

**철칙:**

- `localPosition` 건드리지 말 것 — 항상 `(0,0,0)`
- `localScale` 건드리지 말 것 — 항상 `(1,1,1)`
- 허용된 변경은 **`localRotation` 의 Z 축 회전 (`0°` 또는 `±90°`)** 뿐

> **호출 순서 주의** — Fit 을 연속 두 번 호출하면 첫 호출이 회전을 풀어버리는
> 케이스가 있다 (회전된 splat 의 bounds 가 swap 되어 보여서).
> 항상 **Reset → Fit** 순서로.

스케일 fitting (박스 안에 완전히 넣기) 은 디테일 손실을 동반하므로 명시적으로
요청받지 않는 한 쓰지 않는다.

---

## 렌더 경로 두 가지

| 경로 | 언제 | 품질 |
|---|---|---|
| **aras-p UnityGaussianSplatting** (표준) | 기본값. photoreal 필요할 때 | 풀 3DGS — per-Gaussian 회전, 3축 스케일, opacity, SH-3 |
| `LccSplatRenderer` (자체 구현, 폴백) | aras-p 를 못 쓸 때 (라이선스 등) | 등방 빌보드 — SH 없음, 눈에 띄게 덜 포토리얼 |

**자작 셰이더 튜닝으로 시간 태우지 말 것.** photoreal 이 목표면 aras-p 로 직행한다.
폴백 경로의 `_MaxRadius` / scaleMul / falloff 조정은 aras-p 를 쓸 수 없을 때만.

### aras-p photoreal 프리셋

`🎬 Wire up` 메뉴가 자동으로 이 값들을 넣는다.

| 필드 | 값 | 비고 |
|---|---|---|
| `m_SplatScale` | **1.0** | 원본 스케일 사용 |
| `m_OpacityScale` | **1.0** | 올리면 분필처럼 됨 |
| `m_SHOrder` | **3** | 풀 view-dependent 컬러 |
| `m_SHOnly` | false | |
| `m_SortNthFrame` | **1** | 매 프레임 재정렬 (알파 합성) |
| `m_RenderMode` | **0 / Splats** | 1·2·3 은 디버그용 points 모드 |

> **용량이 문제면** `m_SHOrder` 를 1 로 낮춘다. `_shs.bytes` 가 거의 사라져
> 씬당 **70~80% 감소**. 대가는 view-dependent 반사감 손실 — 실내 설비 스캔이면
> 체감이 작다.

---

## 충돌 / 콜라이더

Gaussian splat 자체에는 **지오메트리가 없다.** 걷거나 부딪히려면 프록시 메쉬가
필요하다.

Stage 1 이 `__LccCollider` (MeshCollider + Z-up→Y-up 변환된 프록시 메쉬) 를
자동으로 붙인다. 프록시 PLY 는 XGRIDS 가 `.lcc` 와 함께 export 하는
`mesh-files/<scene>.ply` 를 쓴다.

- 생성물: `Assets/LCC_Generated/<sceneName>_ProxyMesh.asset`
- 컨벤션: `Splat_<sceneName>` → 자식 `__LccCollider` → `MeshCollider.sharedMesh`

씬을 열었을 때 미연결 콜라이더가 있으면 `LccColliderAutoHealer` 가 감지해
베이크 다이얼로그를 띄운다. 미연결이 0 이면 아무 일도 일어나지 않는다.

---

## 메뉴 레퍼런스

전부 `Virnect → LCC →` 아래에 있다.

| 메뉴 | 단계 | 하는 일 |
|---|---|---|
| 🔧 Install Aras URP Feature | 셋업 | URP 렌더러에 `GaussianSplatURPFeature` 등록 (**프로젝트당 1회 필수**) |
| 🎬 Wire up aras-p Gaussian Splats | Stage 2 | 모든 `GaussianSplatAsset` 자동 발견 → `__ArasRoot` 아래 배치 + photoreal 프리셋 + `-90X` freeze |
| 📦 Fit ArasSplats into Cube | Stage 3 | `Cube` 기준 Z축 회전만으로 정렬 |
| 📦 Fit ArasSplats into Cube (refine) | Stage 3 | 회전 유지, position 만 `(0,0,0)` 재핀 |
| 📦 Reset ArasSplats to Frozen (Stage 1) | Stage 3 | frozen identity 상태로 되돌리기 |
| ▶ Activate Splats + Frame Camera | 유틸 | 폴백 렌더러 활성화 + 카메라를 bbox 에 맞춤 |
| Bake Mesh Colliders (Active Scene) | 콜라이더 | 활성 씬의 `Splat_*` 전부 프록시 메쉬 베이크 + 연결 |
| Bake Mesh Colliders (· Rebuild Assets) | 콜라이더 | 위와 같되 자산 강제 재생성 |
| 📸 Screenshot Game View | 검증 | Game View 를 PNG 로 저장 (URP RenderGraph 대응) |
| `Virnect → LCC Importer` | 창 | 임포터 메인 UI (탭: LCC / 콜라이더 / splat-transform / v1 백엔드) |

> **에셋 이름 규칙** — `🎬 Wire up` 은 `<name>_lod0`, `<name>_lod1` … 접미사를
> 벗겨 `<name>` 으로 묶고, 가장 낮은 LOD 번호(= 최고 밀도)를 대표로 고른다.
> 접미사가 없으면 그대로 쓴다.

---

## 흔한 함정

**1. `HasValidRenderSetup: false` → 화면이 빔**
URP 피처 미설치. `🔧 Install Aras URP Feature` 실행. ([Quick start 2번](#quick-start))

**2. `splatCount > 0` 인데 화면이 빔**
카메라가 `m_BoundsMin..m_BoundsMax` 밖에 있다. per-camera gather 가 컬링한다.
카메라를 bbox 안쪽 15~25 m 로 옮길 것.

**3. Game View 는 멀쩡한데 스크린샷 PNG 만 비어있음**
`Camera.Render()` 가 URP RenderGraph 피처를 건너뛴다. Unity 6 의
`SubmitRenderRequest` 를 써야 한다:

```csharp
var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
if (RenderPipeline.SupportsRenderRequest(cam, req))
    RenderPipeline.SubmitRenderRequest(cam, req);
else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
```

`📸 Screenshot Game View` 메뉴가 이미 이 처리를 한다.

**4. "immutable packages were unexpectedly altered" 경고**
`Packages/com.unity.render-pipelines.universal/` 안의 렌더러 데이터를 고치면
Unity 가 경고하고, 패키지 업데이트 때 변경이 날아간다. 프로젝트 로컬
렌더러(`Assets/Settings/PC_Renderer.asset`)도 같이 패치해야 한다 —
설치 메뉴는 양쪽 다 처리한다.

**5. 점처럼 보이거나 거대한 흐린 방울**
폴백 렌더러(`LccSplatRenderer`)를 쓸 때만 발생한다. 카메라 거리와 `_MaxRadius`
클램프 문제다. aras-p 경로면 해당 없음.

**6. `🎬 Wire up` 이 "0 개 wired" 를 찍음**
프로젝트에 `GaussianSplatAsset` 이 하나도 없다. `.lcc` 를 `Assets/LCC_Drops/` 에
드롭했는지, 또는 `Tools → Gaussian Splats → Create GaussianSplatAsset` 으로
에셋을 만들었는지 확인할 것. (콘솔에 안내가 찍힌다.)

**7. 인스펙터 값만 보고 판단 금지**
렌더링 변경은 반드시 Game View 를 PNG 로 떠서 픽셀을 확인한다.

---

## 폴더 구조

```
Assets/
  Scenes/
    SampleScene.unity      ★ 여기서 시작 (빈 씬)
    LccMain.unity          구조 참고용 (에셋 슬롯 None — 정상)
  LCC_Drops/               ★ 여기에 .lcc 폴더를 드롭      (gitignore)
  GaussianAssets/          빌드된 .asset + .bytes 출력    (gitignore)
  LCC_Generated/           프록시 메쉬 출력               (gitignore)
  Settings/                URP 렌더러 / 파이프라인 에셋
Packages/
  manifest.json            aras-p 패키지 참조
  com.virnect.lcc/         임베드 UPM 패키지 (아래)
```

### `Packages/com.virnect.lcc/`

| 폴더 | 내용 |
|---|---|
| `Editor/` | 15 스크립트 / 약 3,200 줄 |
| `Runtime/` | 10 스크립트 / 약 800 줄 |
| `Server~/` | Python 보조 스크립트 (`~` 라 Unity 가 무시) |

주요 Editor 스크립트:

| 파일 | 역할 |
|---|---|
| `LccImporterWindow.cs` | 임포터 메인 UI (최대 모듈) |
| `LccScriptedImporter.cs` | `.lcc` 드롭 자동 감지 |
| `LccArasSetup.cs` | `🎬 Wire up` — 에셋 자동 발견 + Stage 2 |
| `LccArasUrpFeatureInstaller.cs` | `🔧 Install Aras URP Feature` |
| `LccCubeFitter.cs` | `📦 Fit into Cube` — Stage 3 |
| `LccProxyMeshBaker.cs` | 프록시 PLY → Mesh 에셋 |
| `LccColliderBuilder.cs` | MeshCollider 부착 |
| `LccColliderAutoHealer.cs` | 미연결 콜라이더 감지 → 베이크 제안 |
| `LccScreenshot.cs` | `📸 Screenshot Game View` |
| `LccSplatTransformCli.cs` | splat-transform CLI 래퍼 |

주요 Runtime 스크립트: `LccSplatDecoder` (v5 디코더), `LccManifest`,
`LccLodStreamer`, `LccSplatRenderer` (폴백), `LccSceneMerger`,
`LccCollisionLoader`.

---

## 데이터 준비 — `.lcc` → Splat-PLY

`GaussianSplatAsset` 을 만들려면 Splat-PLY 가 먼저 필요하다.

- **Lixel Studio** → Export → Gaussian Splat PLY
- 또는 **Lixel Universal Converter (LUC)**
- 또는 `splat-transform` CLI (v0.14.0+ 는 `.lcc` 를 직접 읽음)

그 다음 Unity 메뉴 `Tools → Gaussian Splats → Create GaussianSplatAsset` 에
PLY 를 넣으면 `.asset` + 사이드카 `.bytes` 5개(`_pos` `_col` `_oth` `_shs` `_chk`)
가 나온다. Stage 1 자동 import 를 쓰면 이 과정이 자동으로 돈다.

> `.lcc` 컨테이너를 직접 파싱하지 말 것 — XGRIDS 독점 포맷이고 Lixel Studio
> 버전마다 on-disk 스키마가 바뀐다. 지원 경로는 SDK / LUC / Splat-PLY 셋이다.

---

## 왜 데이터가 없나

**1. 용량** — 빌드된 `GaussianSplatAsset` 은 씬 하나당 **200~620 MB** 다.
용량의 약 80% 가 SH-3 view-dependent 컬러(`_shs.bytes`)다. 실제 운영 프로젝트
기준 8개 씬 합계 **2.6 GB** — git 에 넣을 물건이 아니다.

| 구성 | 용량 |
|---|---|
| 이 레포 (파이프라인만) | **707 KB** |
| 운영 프로젝트의 `GaussianAssets/` | 2,656 MB |
| 프록시 메쉬 3개 | 63 MB |

**2. 라이선스** — aras-p 의 **런타임은 MIT 지만 creator 도구는 원본 3DGS
라이선스**(비상업 제한)를 따른다. 그 도구로 만든 SH 인코딩 에셋을 재배포하는
건 이 제약을 탄다.

그래서 이 레포는 **레시피와 자동화 코드**만 담는다. 각자 자기 `.lcc` 를 넣고
직접 빌드하면 된다.

---

## 라이선스 주의

- **aras-p UnityGaussianSplatting 런타임** — MIT
- **aras-p creator 도구** — 원본 3DGS 라이선스 (비상업 제한).
  이 도구로 만든 SH 인코딩 에셋을 상업적으로 재배포하는 건 제약을 탄다.
- 상업 배포가 필요하면 폴백 경로(`LccSplatRenderer`) 또는
  **XGRIDS 공식 Unity SDK** 를 검토할 것.

---

## 참고

- [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)
- [PlayCanvas splat-transform](https://github.com/playcanvas/splat-transform)
- [XGRIDS LCC Whitepaper](https://github.com/xgrids/LCCWhitepaper)
- 패키지 상세: [`Packages/com.virnect.lcc/README.md`](Packages/com.virnect.lcc/README.md)
