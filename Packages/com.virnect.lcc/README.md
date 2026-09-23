# com.virnect.lcc — Virnect LCC Importer (Unity)

XGrids PortalCam `.lcc` 파일을 Unity 에서 바로 쓰기 위한 UPM 패키지.

## 설치

```
Window → Package Manager → + → Add package from git URL
```

```
https://github.com/virnect3d-cpu/-unity_to_lcc.git?path=/Packages/com.virnect.lcc
```

## ✅ 표준 LCC 셋팅 (2026-05 확정) — aras-p UnityGaussianSplatting 경유

**원칙: 이 패키지의 자체 `LccSplatRenderer` (등방 빌보드) 는 "fallback" 이다. photoreal 결과가 필요하면 무조건 [aras-p/UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) 를 쓴다.** 자작 셰이더로 시간 태우지 말 것. 진단 매트릭스와 함정은 레포 루트 [`README.md`](../../README.md) 의 "흔한 함정" 절 참고.

### 사전 조건

- **Unity 6.0+** (aras-p URP feature 가 RenderGraph 만 지원, Unity 6 미만에서는 컴파일 에러)
- **URP Render Graph 활성화** — Project Settings → Graphics → URP Global Settings 에서 Compatibility Mode 가 꺼져 있어야 한다 (`m_EnableRenderCompatibilityMode: 0`).

### 1) 패키지 등록 — `Packages/manifest.json`

```jsonc
{
  "dependencies": {
    "com.virnect.lcc": "file:.../Packages/com.virnect.lcc",
    "org.nesnausk.gaussian-splatting":
      "https://github.com/aras-p/UnityGaussianSplatting.git?path=/package"
  }
}
```

### 2) GaussianSplatAsset 준비 — `Assets/GaussianAssets/`

`<name>.asset` 한 개당 다섯 개의 사이드카 `.bytes` 파일 (`_pos`, `_col`, `_oth`, `_shs`, `_chk`) 가 한 세트.

**A. 이미 빌드해둔 자산 재사용:**
```
<기존 프로젝트>/Assets/GaussianAssets/*
  → 대상 프로젝트의 Assets/GaussianAssets/ 로 복사
```
> 이 레포에는 빌드된 에셋이 들어있지 않다 (씬당 200~620MB). 아래 B 로 직접 만든다.

**B. LCC 로부터 새로 생성:**
1. `Lixel Studio` 또는 `Lixel Universal Converter (LUC)` 로 `.lcc` → **Splat-PLY** 변환.
2. Unity 메뉴 `Tools → Gaussian Splats → Create GaussianSplatAsset` 창에서 PLY 입력 → `.asset` + `.bytes` 5개 생성.

### 3) URP 렌더러 피처 설치 — 1회

**핵심 — 이거 빠지면 `GaussianSplatRenderer.HasValidRenderSetup = false` 가 되어 화면이 비어 보인다.** Lcc_Injector 가 한 방에 처리하는 메뉴:

```
Virnect → LCC → 🔧 Install Aras URP Feature
```

이 메뉴는 프로젝트 안의 모든 `UniversalRendererData` 자산 (`PC_Renderer.asset`, `Mobile_Renderer.asset` 등) 에 `GaussianSplatURPFeature` 를 sub-asset 으로 추가하고 `m_RendererFeatures` 리스트에 등록한다.

수동으로 하려면: 각 `*_Renderer.asset` 을 인스펙터에서 열고 → `Add Renderer Feature` → `Gaussian Splat URP Feature`.

### 4) 씬에 GaussianSplatRenderer 부착 — 자동

```
Virnect → LCC → 🎬 Wire up aras-p Gaussian Splats
```

이 메뉴는:
- `__ArasRoot` GameObject 생성 (이미 있으면 재사용) + `-90X` 적용
- **프로젝트 안의 모든 `GaussianSplatAsset` 을 자동 발견** — 이름 하드코딩 없음
  - `<name>_lod0`, `<name>_lod1` … 접미사는 벗겨 `<name>` 으로 묶고,
    가장 낮은 LOD 번호(= 최고 밀도)를 대표로 고른다
  - 접미사가 없으면 파일명 그대로 사용
- 에셋마다 자식 `ArasSplat_<name>` 생성 + `GaussianSplatRenderer` 부착
- **자체 `LccSplatRenderer` 들은 비활성화** (이중 렌더 방지)

> 에셋이 하나도 없으면 콘솔에 안내를 찍고 중단한다.

부착되는 photoreal 프리셋:

| 필드 | 값 | 비고 |
|---|---|---|
| `SplatScale` | **1.0** | 그대로 |
| `OpacityScale` | **1.0** | 키우지 말 것 — 분필 느낌 됨 |
| `SHOrder` | **3** | 완전 SH (view-dependent color) |
| `SHOnly` | false | |
| `SortNthFrame` | **1** | 매 프레임 재정렬 |
| `RenderMode` | **0 / Splats** | 1/2/3 은 디버그 점 모드 |
| `PointDisplaySize` | 3 | 디버그 모드 전용 |

### LCC 작업 3-Stage 워크플로 (locked, 2026-05-12)

| Stage | 내용 | 자동 메뉴 / 결과 |
|---|---|---|
| **Stage 1** | `.lcc` 폴더 드롭 → 자동 import + GaussianSplatAsset(Quality=High) 빌드 + 씬 spawn | `LccDropAutoImporter` (AssetPostprocessor) |
| **Stage 2** | X 축 `-90°` 회전 + Freeze Transformations | `🎬 Wire up aras-p Gaussian Splats` |
| **Stage 3** | 사용자 박스(Cube) 기준 Z 축 회전 fit (position/scale 금지) | `📦 Fit ArasSplats into Cube` |

---

#### Stage 1 — 자동 import 파이프

```
.lcc 폴더 드롭 (Assets/LCC_Drops/)
   → LccDropAutoImporter  (AssetPostprocessor 자동 발동)
   → LccConverter         (splat-transform → .ply 임시)
   → GaussianAssetBuilder (Aras-P GaussianSplatAsset 빌드, Quality=High)
   → 활성 씬에 자동 spawn:
       Splat_<name>            (Z-up→Y-up 회전)
         ├─ __LccCollider      (MeshCollider, proxy mesh, Z-up→Y-up 변환된 mesh, world identity)
         └─ _ArasP             (GaussianSplatRenderer + asset, world identity)
   → Hierarchy 자동 선택 + ping
```

**Quality = High**: PSNR 57.77 dB, 2.94× 압축, Position **Norm16**, Scale **Norm16**, Color **Float16×4**, SH **Norm11**. 이 프리셋 외로 만들지 말 것.

---

#### Stage 2 — X 축 -90° + Freeze Transform (Maya 와 동일)

Stage 1 직후 raw 상태에 X 축 `-90°` 회전을 적용 + Maya 의 *Freeze Transformations + Delete History* 와 동일한 결과 만들기.

결과적으로 만들어지는 구조:

```
__ArasRoot                              ← localRotation = (-90, 0, 0)    [Z-up→Y-up 변환 담당]
├── ArasSplat_Scan_A_Cutter             ← pure identity (Frozen child)
├── ArasSplat_Scan_B_Facility01         ← pure identity
└── ...
```

| GameObject | localPosition | localRotation (Euler) | localScale |
|---|---|---|---|
| `__ArasRoot` (부모) | `(0, 0, 0)` | **`(-90, 0, 0)`** | `(1, 1, 1)` |
| `ArasSplat_<scene>` (자식 × 6) | `(0, 0, 0)` | **`(0, 0, 0)`** | `(1, 1, 1)` |

**왜 두 단인가?** Maya Freeze 는 회전을 vertex 좌표에 굽어버린다. aras-p GaussianSplatAsset 의 position bytes 를 직접 다시 인코딩하는 건 가능하지만 (Wigner-D SH 회전까지 다시 짜야 함, 비용 큼). 동일한 사용자 효과 (= `ArasSplat_` 의 Inspector 가 완벽 identity) 를 *wrapper hoist* 로 즉시 달성. Maya 에서 "group → freeze group" 으로 같은 효과 내는 패턴과 동일.

`🎬 Wire up aras-p Gaussian Splats` 메뉴가 위 transform 을 자동 적용 + 기존 GO 도 다시 덮어씀.

후속 Stage 2 정합 / Stage 3 collider 베이크는 ArasSplat_ 의 identity 를 anchor 로 활용 — 정합 변환은 ArasSplat_ 자체에 직접 적용 가능 (현재 transform 이 깨끗하므로 어디로 가는지 명확).

화면이 거꾸로 보이거나 옆으로 누우면 transform 만지지 말고:
- GaussianSplatAsset 의 Splat-PLY 가 Z-up (XGrids 기본) 으로 export 됐는지 확인
- Y-up 으로 잘못 export 됐으면 converter 옵션 점검 후 .asset 재생성

---

#### Stage 3 — 사용자 박스(Cube) 기준 Z 축 회전 fit

씬에 만들어둔 `Cube` GameObject 를 컨테이너로 삼아 각 `ArasSplat_*` 을 박스 안으로 정렬.

**철칙 (사용자 명시, 2026-05-12):**
- `localPosition` 절대 건드리지 말 것 — **항상 `(0, 0, 0)` 유지**
- `localScale` 절대 건드리지 말 것 — **항상 `(1, 1, 1)` 유지**
- 허용된 변경은 **`localRotation` 의 Z 축 회전 (`0°` 또는 `±90°`) 단 한 가지**

판정 — Cube 의 긴 horizontal 축이 Z 인 경우:
- scan world X > Z → `localRotation = (0, 0, 90)` (X↔Z 스왑)
- 아니면 → `localRotation = (0, 0, 0)`

제외: 움직이면 안 되는 앵커 스캔이 있으면 `LccCubeFitter.s_Excluded` 에
`"ArasSplat_<name>"` 을 넣는다. 기본값은 빈 배열(전부 fit 대상).

**자동 메뉴:**
- `Virnect/LCC/📦 Fit ArasSplats into Cube` — 회전만, position=(0,0,0) 강제
- `Virnect/LCC/📦 Fit ArasSplats into Cube (refine)` — 회전 유지하면서 position 만 (0,0,0) 재핀
- `Virnect/LCC/📦 Reset ArasSplats to Frozen (Stage 1)` — Stage 2 frozen identity 로 되돌리기

**주의:** Fit 연속 호출 시 이미 회전된 splat 의 bounds 가 swap 되어 보여서 회전이 풀리는 케이스 있음. 항상 **Reset → Fit** 순서로 호출. 박스 안에 *완전히* 들어가게 하려면 scale fitting 필요한데, 디테일 손실 동반하므로 명시 요청 없으면 추가 X.

---

### 6) 검증 — 스크린샷 (필수)

`Camera.Render()` 로 RT 에 그리면 URP RenderGraph feature 가 누락되어 PNG 가 비어 보이는 케이스가 있다. Unity 6 의 `RenderPipeline.SubmitRenderRequest` 를 써야 한다. 이 패키지가 제공하는 메뉴:

```
Virnect → LCC → 📸 Screenshot Game View
→ 결과: <project>/_shots/lcc_gameview.png
```

내부 구현:
```csharp
var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
if (RenderPipeline.SupportsRenderRequest(cam, req))
    RenderPipeline.SubmitRenderRequest(cam, req);
else { cam.targetTexture = rt; cam.Render(); }
```

**컴포넌트 값만 보고 "됐다" 하지 말 것 — 항상 PNG 를 열어 픽셀로 확인.**

### 7) 흔한 함정

| 증상 | 원인 | 해결 |
|---|---|---|
| 화면 비어 있음, `HasValidRenderSetup: false` | URP 피처 미설치 | `🔧 Install Aras URP Feature` 메뉴 |
| 화면 비어 있음, `HasValidRenderSetup: true`, `splatCount > 0`, Game View 는 보임 | 스크린샷 path 가 feature 스킵 | `📸 Screenshot Game View` 메뉴 (SubmitRenderRequest 사용) |
| Splat 이 거꾸로 / 위아래 뒤집힘 | 변환기가 Y/Z flip 안 했음 | `__ArasRoot/ArasSplat_*` 의 local rotation = `(180, 0, 0)` |
| `Editor → recompile` 후 메뉴 없음 | 새 .cs 의 `.meta` 미생성 | `Assets/Refresh` 후 6-8초 대기 후 재시도 |
| `immutable packages were unexpectedly altered` 경고 | 패키지 캐시의 URP renderer 가 함께 수정됨 | 무시 가능 — 우리 프로젝트 로컬 `Assets/Settings/PC_Renderer.asset` 변경분이 진짜 source of truth |

---

## 사용 흐름 (요약)

1. Unity Project 창에 `.lcc` 파일 드래그 → `LccScriptedImporter` 가 자동으로 읽어 `LccScene` 에셋 생성
2. 위의 "표준 LCC 셋팅" 7 단계대로 aras-p 경유 photoreal 셋업
3. 여러 스캔본을 합치려면 `Virnect → LCC Importer` 창에서 `LccScene` 여러 개 등록 → "월드로 인스턴스화"
4. (선택) 자체 `LccSplatRenderer` fallback 이 필요하면 `_MaxRadius` 클램프 + 기하평균 radius 가 패치된 셰이더 사용 — 자세한 건 cursor rule 참조

## 메쉬 콜라이더 (자동 베이크)

XGrids 가 `.lcc` 와 함께 export 한 `mesh-files/<scene>.ply` (proxy 트라이앵글 메쉬) 를 그대로 활용해
Unity Mesh 자산을 만들고 씬의 MeshCollider 에 연결합니다. **Python 서버 불필요.**

- `Virnect → LCC → Bake Mesh Colliders (Active Scene)` — 활성 씬의 `Splat_*` / `ArasSplat_*` 모두 자동 베이크
- `Virnect → LCC → Bake Mesh Colliders (Active Scene · Rebuild Assets)` — 자산 강제 재생성
- `Virnect → LCC Importer` 창의 **콜라이더 탭** 상단에서 1-클릭 실행

생성물: `Assets/LCC_Generated/<sceneName>_ProxyMesh.asset`
컨벤션: `Splat_<sceneName>` GameObject → 자식 `__LccCollider` (없으면 생성) → MeshCollider.sharedMesh 자동 연결

씬을 열었을 때 미연결 콜라이더가 있으면 **`LccColliderAutoHealer`** 가 이를 감지해 베이크 다이얼로그를 띄웁니다 — 한 번 클릭으로 콜라이더가 동작. 미연결이 0 이면 아무 일도 일어나지 않습니다.

## 현재 상태

- ✅ aras-p UnityGaussianSplatting 경유 photoreal 셋업 (이 README 의 7 단계) — **표준 path**
- ✅ Mesh Collider 자동 베이크 (PLY → Unity Mesh asset → MeshCollider wiring)
- ✅ 자체 `LccSplatRenderer` (등방 빌보드) — fallback, `_MaxRadius` 클램프 + 기하평균 radius 패치 적용
- 자세한 설계 / 진단: 레포 루트 [`README.md`](../../README.md)

## 테스트 데이터

이 레포에는 스캔 데이터가 포함되어 있지 않다. 개발 중 검증에 쓴 기준 샘플은
단일 `.lcc` 한 개 (9.97M splats · 5 LOD · 326 MB) 규모였고, 같은 급이면
동일한 절차로 동작한다. 데이터 준비 방법은 레포 루트
[`README.md`](../../README.md) 의 "데이터 준비" 참고.
