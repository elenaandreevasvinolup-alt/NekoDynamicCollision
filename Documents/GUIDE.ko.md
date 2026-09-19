# Neko Dynamic Collision (NDC) — 설치와 매뉴얼

Unity용 베이크된 볼록체(convex hull) 콜리전입니다. 비용이 큰 작업은 모두 Editor에서 일어나며,
런타임은 로드하고 라우팅만 합니다. 스킨드 캐릭터나 정적 메시는 **프레임당 비용 제로**로
본(bone)별·영역별 볼록체 집합이 되고, 내장 파티션 이벤트와 브러시로 칠한
영역별 PhysicMaterial을 갖습니다.

컴포넌트 자체에는 생성된 코드도, 속성도, 플러그인의 에디터 절반에 대한 런타임
의존성도 없습니다. `Editor/`를 삭제해도 런타임은 그대로 작동합니다.

## 목차

- [파트 A — 빠른 도입](#sec-partA)
  - [1. 첫 캐릭터 베이크하기](#sec-1)
  - [2. 머티리얼 영역 칠하기](#sec-2)
  - [3. 10분 코스](#sec-3)
- [파트 B — 매뉴얼](#sec-partB)
  - [4. 핵심 개념](#sec-4)
  - [5. 설치와 요구 사항](#sec-5)
  - [6. 베이커 창](#sec-6)
  - [7. 메뉴 레퍼런스](#sec-7)
  - [8. 정밀도](#sec-8)
  - [9. 브러시](#sec-9)
  - [10. 머티리얼 그룹과 프리셋](#sec-10)
  - [11. 커버리지 진단](#sec-11)
  - [12. 콜리전 상태 점검](#sec-12)
  - [13. 이벤트와 연동](#sec-13)
  - [14. Trigger 폴링](#sec-14)
  - [15. 질량, 자기 충돌, LOD](#sec-15)
  - [16. 로컬라이제이션](#sec-16)
  - [17. 디렉터리 구조](#sec-17)
  - [18. 제거](#sec-18)
  - [19. 문제 해결과 FAQ](#sec-19)
  - [20. 연락처](#sec-20)
- [부록 A. PhysicMaterial 프리셋](#sec-appA)
- [부록 B. 프로젝트 물리 설정 점검](#sec-appB)

---

<a id="sec-partA"></a>
# 파트 A — 빠른 도입

<a id="sec-1"></a>
## 1. 첫 캐릭터 베이크하기

1. 캐릭터를 선택하고 **Dynamic Collision** 컴포넌트를 추가합니다
   (`Add Component → Neko → Dynamic Collision`, 또는 `GameObject` 메뉴).
   생성 시 컴포넌트는 자체 `SkinnedMeshRenderer`를 찾아 하나의
   기본 머티리얼 그룹을 만듭니다. 아무것도 채울 필요가 없습니다.
2. 인스펙터에서 **Bake**를 누릅니다.
3. 바인드 포즈 와이어프레임으로 볼록체가 Scene 뷰에 나타납니다. 캐릭터를
   선택하면 볼 수 있습니다; Gizmo는 기본적으로 선택을 따라갑니다.

베이크는 씬 옆에 세 종류의 에셋을 씁니다:

```
Assets/NekoDynamicCollision/Baked/<SceneName>/
├── <Object>_Baked.asset        hull meshes + bind matrices + edges + volumes
├── <Object>_Paint.asset        the brush labels, one byte per triangle
└── Materials/DYC_<preset>.physicMaterial
```

볼록체 메시는 `_Baked.asset`의 하위 에셋이므로 그것과 함께 이동하며
Play 모드와 빌드에서도 유지됩니다. 런타임에 다시 계산되는 것은 없습니다.

<a id="sec-2"></a>
## 2. 머티리얼 영역 칠하기

브러시가 "하나의 오브젝트, 여러 PhysicMaterial"을 가능하게 하는 핵심입니다.

1. 베이커를 엽니다 (`NekoWorks → NekoDynamicCollision → Open Main Window`).
2. **Materials** 탭으로 가서 영역마다 그룹을 추가합니다 — 예를 들어
   `Hard`와 `Soft`. 각각 프리셋을 고릅니다.
3. **Paint** 탭으로 가서 칠할 그룹을 고르고 **Start painting**을 누릅니다.
4. Scene 뷰에서 해당 그룹에 넣을 면 위를 드래그합니다. 칠해진
   삼각형은 즉시 그룹 색으로 채워집니다.
5. 다시 베이크합니다. 이제 각 영역이 자체 PhysicMaterial을 가진 자체 볼록체를 갖습니다.

칠하기는 Play 모드가 정지되어 있고 Animation 창이 프리뷰 중이 아닐 때만
가능합니다. 포즈 자체는 중요하지 않습니다 — 라벨은 삼각형 인덱스별로
저장되므로 현재 포즈는 무관합니다.

<a id="sec-3"></a>
## 3. 10분 코스

| 분 | 할 일 |
|---|---|
| 0–2 | 컴포넌트를 추가하고 Bake를 누르고 Gizmo를 봅니다. |
| 2–4 | **Health** 탭을 열고 빨간 항목을 모두 고칩니다. |
| 4–6 | **Bake** 탭을 열고 커버리지 수치를 봅니다. 약 95% 미만이면 정밀도를 올립니다. |
| 6–9 | 영역마다 머티리얼 그룹을 추가하고, 칠하고, 다시 베이크합니다. |
| 9–10 | 인터랙트 레이어를 `Bullet`으로 설정하고, 이벤트 이름을 연결하고, Play 모드에서 테스트합니다. |

---

<a id="sec-partB"></a>
# 파트 B — 매뉴얼

<a id="sec-4"></a>
## 4. 핵심 개념

### 삼각형 덩어리가 아닌 볼록체

동적(비키네마틱) `Rigidbody`는 볼록하지 않은 `MeshCollider`를 사용할 수 없습니다 — 이는
Unity가 아니라 PhysX의 제약입니다. 따라서 움직이는 바디의 모든 콜리전 형상은
볼록해야 합니다. NDC는 **볼록체**를 베이크하고, 원시 삼각형 부분집합이 아니라 볼록체
자체를 Unity에 공급합니다. 그래서 정점 수가 PhysX 한계인 255에
도달할 수 없습니다.

### 본 고정 볼록체로 충분한 이유

바인드 포즈에서 `bone.localToWorldMatrix · bindposes[i] = I`입니다. 따라서
하나의 본에 100% 가중치로 가중된 정점의 스키닝은 정확히 바인드 포즈 메시로
평가됩니다. 다시 말해, **본 로컬 공간에서 베이크된 볼록체는 강체 가중 정점에 대해
매 프레임 다시 쿠킹한 것과 비트 단위로 동일**합니다.

차이가 나는 것은 관절을 가로지르는 블렌드 가중 정점뿐입니다. 그것들은 구조상
겹치는 이웃 볼록체가 커버합니다. 그래서 NDC는 런타임에 무료이면서도
중요한 곳에서는 정확할 수 있습니다.

### 인덱스 순서 청킹이 아닌 공간 클러스터링

NDC는 삼각형을 위치로 그룹화합니다(최원점 시딩과 간선 인접성을 따라가는 Dijkstra
방식 성장). 대안인 인덱스 순서대로 삼각형을 취하는 방식은 서로 겹치고 공기를 감싸는
볼록체를 만들며, 요청하는 볼록체가 많을수록 더 나빠집니다.

### 파트와 머티리얼 그룹

두 개의 독립적인 축이 있습니다:

- **파트 (Element)** — *어디*. 하나의 본(Skin 모드) 또는 메시 전체(Mesh 모드).
  자식 파트는 항상 조상보다 우선하므로 이벤트가 두 번 디스패치되는 일은 없습니다.
- **머티리얼 그룹** — *무엇*. 하나의 PhysicMaterial과 하나의 밀도를 공유하는 면의 집합이며,
  칠하기로 만들어집니다.

볼록체는 하나의 파트와 하나의 머티리얼 그룹의 교집합입니다. 어떤 그룹이 특정 본 안에
칠해진 면을 갖지 않으면 그 쌍에 대한 볼록체는 생성되지 않습니다.

### 런타임이 하는 일

1. 베이크된 세트를 로드합니다.
2. 올바른 본 아래에 각 볼록체마다 하나의 숨겨진 자식 오브젝트를 항등 변환으로
   만들고, 볼록 `MeshCollider`를 할당합니다.
3. `Collider → hull` 조회 테이블을 만듭니다.
4. 볼록체를 소유한 모든 `Rigidbody`에 `Dyc_Relay`를 배치합니다.
5. 레이어, 자기 충돌, 질량을 설정합니다.

그다음 멈춥니다. 선택적인 Trigger 폴링과 LOD용 거리 검사 외에는
`Update` 작업이 없습니다.

<a id="sec-5"></a>
## 5. 설치와 요구 사항

- Unity 2022.3 이상.
- `Assets/NekoDynamicCollision`을 프로젝트에 복사합니다. 설정할 것은
  없습니다; 어셈블리는 어셈블리 정의로 스코프됩니다.
- 두 개의 어셈블리:
  - `Neko.DynamicCollision.Runtime` — 컴포넌트, 릴레이, Trigger 폴링, 이벤트
    구조체, 연동 파사드. `UnityEditor`를 절대 참조하지 않습니다.
  - `Neko.DynamicCollision.Editor` — 베이커, 볼록체 수학, 클러스터링, 브러시,
    Gizmo, 상태 점검, 프리셋, 창. 에디터 전용 플랫폼.

<a id="sec-6"></a>
## 6. 베이커 창

`NekoWorks → NekoDynamicCollision → Open Main Window`(`Cmd/Ctrl+Shift+D`).

| 탭 | 기능 |
|---|---|
| **Bake** | 소스, 모드, 정밀도, 베이크/클리어/재빌드, 통계, 커버리지 |
| **Paint** | 칠해진 삼각형 수가 있는 그룹 목록, 브러시 설정, 포즈 리셋 |
| **Gizmo** | Scene 뷰가 무엇을 어떻게 그리는지 |
| **Parts** | 파트 목록 — 본, 자식 포함, 이벤트 이름, 데미지 배율 |
| **Materials** | 머티리얼 그룹, 프리셋, 쌍 동작 테이블, 프로젝트 물리 감사 |
| **Health** | 100점 만점 점수, 모든 문제, 원클릭 수정 |
| **Settings** | 언어, 진단, 베이크된 폴더 열기, 환경 설정 초기화 |

<a id="sec-7"></a>
## 7. 메뉴 레퍼런스

모든 것이 하나의 최상위 슬롯 아래에 있어, NekoWorks 플러그인을 더 설치해도
메뉴 바가 넓어지지 않습니다.

```
NekoWorks
└── NekoDynamicCollision
    ├── Window
    │   └── Open Main Window              Cmd/Ctrl+Shift+D
    ├── Bake
    │   ├── Bake Selected                 Cmd/Ctrl+Shift+B
    │   ├── Rebuild Selected
    │   └── Bake Contact Map
    ├── Gizmo
    │   ├── Toggle Hull Gizmo
    │   └── Highlight Uncovered Faces
    ├── Tools
    │   ├── Bone Precision (Expert)
    │   ├── Material Forge
    │   ├── Mirror RTL Interface
    │   └── Refresh Component Icon
    ├── Diagnostics
    │   └── Collision Health
    └── Help
        └── About Neko Dynamic Collision
```

메뉴 캡션은 로드 시점과 언어 변경 시점에 로컬라이즈됩니다; 속성의
정적 영어 문자열은 사용 중인 버전에서 Unity의 내부 메뉴 API를 사용할 수 없을
때의 대체 값입니다.

<a id="sec-8"></a>
## 8. 정밀도

노브 하나, 네 단계. 내부적으로 네 개의 값으로 확장됩니다:

| 정밀도 | 볼록체당 삼각형 | 파트당 볼록체 | 이음새 겹침 | 가중치 임계값 |
|---|---|---|---|---|
| Coarse | 120 | 1 | 4.0 mm | 0.35 |
| Normal | 250 | 2 | 3.0 mm | 0.25 |
| Fine | 500 | 4 | 2.0 mm | 0.15 |
| Ultra | 1000 | 8 | 1.0 mm | 0.05 |

- **볼록체당 삼각형**은 하나의 볼록체가 흡수할 수 있는 지오메트리 양의 상한입니다. 볼록체당
  삼각형이 많을수록 더 적고, 크고, 느슨한 볼록체가 됩니다.
- **파트당 볼록체**는 파트별 클러스터의 목표 개수입니다. 클러스터가 많을수록
  더 밀착되고 콜라이더가 많아집니다.
- **이음새 겹침**은 각 볼록체를 바깥으로 부풀려 이웃 영역이 틈을 남기는 대신 겹치도록
  합니다. 겹침은 안전합니다(히트를 놓치지 않고, 같은 프레임 중복 제거가
  이중 이벤트를 막습니다); 틈은 안전하지 않습니다.
- **가중치 임계값**은 지배적인 본에 대한 가중치가 이 값 미만인 정점을
  버립니다. 높을수록 더 밀착되고 "강체적으로 올바른" 볼록체가 됩니다.

볼록체의 정점 수는 클러스터의 고유 정점 수를 넘을 수 없으며, 그 값은 250으로
제한됩니다 — PhysX 한계 255를 안전하게 밑돕니다. 상태 점검은 255를 넘는 볼록체를
빨간색으로 표시합니다.

### 8.1 볼록 모드는 분해한다 — CC와 내장 복합 콜라이더를 넘어서

**볼록** 모드에서는 오목한 메시가 그 오목한 곳을 따라 볼록 조각으로 잘립니다. 내장 복합 콜라이더와 V-HACD 방식 도구(CC)와 같은 발상입니다. 어떤 메시든 볼록 조각으로 만들어 냅니다. NDC는 그 폭을 유지하면서 상한을 높입니다:

| | 복합 콜라이더 / CC | NDC |
|---|---|---|
| 모든 메시 | 예 | 예 |
| 잘 최적화됨 | 예 | 예 — 백그라운드 베이크 작업, 진행률, 취소, 본별 예산 |
| 조각이 관절을 존중함 | **아니오** — 순수하게 기하학적이라 어깨가 팔을 삼킬 수 있음 | **예** — 조각이 가중치 필드에서 나온 본 라벨을 가짐 |
| 움직이는 바디에서 사용 가능 | **아니오** — PhysX는 비키네마틱 `Rigidbody`의 비볼록 콜라이더를 거부함 | **예** — 출력이 볼록체임 |
| 런타임 비용 | 로드 시 콜라이더 쿠킹 | 0 — 에셋에 베이크됨 |
| 폴백 | — | 공간 클러스터링, 그래서 퇴화한 메시도 콜라이더를 얻음 |

Expert 창은 조각이 **분해**(오목한 곳을 따라 절단)에서 왔는지 **공간 클러스터링**(폴백)에서 왔는지 보고하므로, 차이는 추측이 아니라 숫자입니다.

**얼마나 잘게 자르는지**는 슬라이더 하나 — **Decomposition detail** — 로 정하며, 바로 아래 숫자 필드에 복셀 크기가 밀리미터로 표시됩니다. 둘은 *하나의* 숫자를 보는 두 가지 방식이므로 항상 일치합니다: 슬라이더를 끌면 필드가 따라오고, 필드에 입력하면 슬라이더가 움직입니다. 동기화할 두 번째 설정은 없습니다.

- **왼쪽** — 거친 복셀: 조각이 더 적고 큽니다. 가장 저렴하고 보통 소품에는 충분합니다.
- **오른쪽** — 가장 고운 복셀. 조각이 표면을 따라가므로 비용이 **비볼록 모드와 같아집니다**: 더 고운 것으로 얻을 것은 없고 비용만 늘어납니다.

위의 정밀도 표는 *클러스터 피팅* — 각 조각이 얼마나 밀착하는지 — 에 적용되는 것이지, 콜라이더가 몇 개 나오는지에 적용되는 것이 아닙니다.

같은 슬라이더가 두 모드에 모두 나타납니다: 볼록 분해와 비볼록 단순화는 *얼마나 세밀하게 원하는가*라는 같은 질문에 답하는 두 가지 방식입니다.

### 8.2 비볼록 세부는 슬라이더 하나

**Collider shape → Non-convex surface**로 바꾸면 **Surface detail** 슬라이더가 나타납니다.

| 슬라이더 | 결과 |
|---|---|
| 맨 왼쪽 | 크게 단순화된 표면 — 삼각형이 적고 면이 눈에 띔 |
| 중간 | 좋은 절충: 형상이 제대로 읽히고 콜라이더는 저렴하게 유지됨 |
| **맨 오른쪽** | **전혀 단순화하지 않음** — 표면을 메시에서 그대로 가져옴 |

슬라이더이지 삼각형 수가 아닌 이유: "조각당 삼각형 수"는 메시에 삼각형이 몇 개 있는지 모르면 합리적으로 고를 수 없습니다 — 500은 몸통에는 거칠고 손가락에는 정밀합니다. 슬라이더는 사용자가 실제로 물을 수 있는 유일한 질문에 답합니다: *정확한 형상을 얼마나 신경 쓰는가*. 맨 오른쪽은 "거의 정확"이 아니라 정확입니다: 단순화가 완전히 꺼집니다.

작은 클러스터는 슬라이더 위치와 무관하게 절대 단순화되지 않습니다 — 얇은 손가락을 거친 그리드로 끌어내리면 아무것도 남지 않고, 빈 콜라이더는 비싼 콜라이더보다 나쁩니다.

Skin 모드와 Mesh 모드 모두 같은 슬라이더를 사용합니다.

<a id="sec-9"></a>
## 9. 브러시

브러시는 면을 "제외"하지 않습니다. 면을 **태그**하며, 그 태그가 베이크를 이끕니다.

| 동작 | 효과 |
|---|---|
| 왼쪽 드래그 | 커서 아래 삼각형에 현재 그룹을 할당 |
| Shift + 드래그 | 그룹 0으로 되돌려 지우고 칠하기 플래그를 해제 |
| 마우스 휠 | 브러시 반경 |
| `X` 토글(창) | 모든 스트로크를 오브젝트의 로컬 X = 0 기준으로 미러링 |

설정: 반경, X-Ray(뒤를 향한 삼각형 무시), X 미러.

요구 사항은 조용히 실패하는 대신 명시적인 메시지로 강제됩니다:

1. Play 모드가 정지되어 있어야 합니다.
2. Animation 창이 프리뷰 중이 아니어야 합니다.

리그는 바인드 포즈일 필요가 **없습니다**. 브러시는 메시에 대해
**현재** 포즈로 레이캐스트합니다; 스키닝은 토폴로지를 바꾸지 않으므로
삼각형 인덱스가 1:1로 대응하고 라벨은 올바르게 유지됩니다.

그래도 리그를 바인드 포즈로 두고 싶다면, **Reset to bind pose** 버튼이
로컬 변환을 `bindposes[i].inverse`에서 풀어 되돌리기와 함께 기록합니다.

<a id="sec-10"></a>
## 10. 머티리얼 그룹과 프리셋

각 그룹은 `PhysicMaterial`, kg/m³ 단위의 밀도, 선택적 이벤트 이름,
그리고 데미지 배율을 갖습니다.

**프리셋.** 14개 카테고리에 걸친 224개 프리셋(Metal, Ceramic, Plastic, Glass, Wood,
Stone & Concrete, Rubber, Fabric & Leather, Body parts, Tissue & organs, Organic, Ice,
Food, Other) — [부록 A](#appendix-a-physic-material-presets) 참고.
프리셋을 적용하면 `Baked/<Scene>/Materials/` 아래에 실제 `.physicMaterial` 에셋이
생성되므로 참조하고, 비교하고, Addressables에 넣고, 아티스트에게 넘길 수 있습니다.

**재질 단조.** 표의 행은 '무엇으로 만들어졌는가'에 답합니다. 단조는 '지금 어떤가'에
답합니다. 기본 프리셋에 **표면 상태**(Dry, Wet, Oiled, Bloody, Sweaty, Icy, Frozen,
Dusty, Rough, Polished, Rusted, Worn, Charred, Clothed, Armoured)를 곱합니다. `Dry`는
항등이므로 `steel` 옆에 `steel_dry`가 생기는 일은 없습니다. 마찰은 정적·동적 계수에
동시에 곱해집니다.

**신체 부위는 유도이지 입력이 아닙니다.** '신체 부위'와 '조직과 장기'는 표의 행이
아니라 조직 배합에서 계산됩니다 — 밀도는 가산, 부드러움은 가산 + 쿠션 항, 마찰과
반발은 부드러움을 따릅니다. '가슴'은 80% 지방 + 10% 근육 + 10% 피부, '두개골'은
95% 뼈 + 5% 피부입니다.

**에셋 생성.** *변형 에셋 생성*은 상태마다 `.physicMaterial`을
`Baked/<Scene>/Materials/`에 씁니다.

**합성 전략.** 라이브러리 전체가 마찰에는 `Multiply`를, 반발에는 `Maximum`을
사용합니다. Unity의 합성 우선순위는
`Average < Minimum < Multiply < Maximum`이므로, 이 전략에서는 미끄러운 표면이 마찰 결과를
지배하고 잘 튀는 머티리얼이 반발 결과를 지배합니다 —
이는 사람들이 직관적으로 기대하는 동작입니다.

**쌍 동작 테이블.** Materials 탭은 프로젝트의 모든 그룹 쌍을 Unity의 실제
우선순위 규칙으로 해석하여 실제로 적용될 값과 평이한 판정
("잘 붙음 / 안 튐")을 보여줍니다. 이것은 "내 얼음이 왜 안 미끄러운가"에
답하는 가장 빠른 방법입니다.

**이름으로 자동 할당.** 그룹 이름을 영어, 중국어, 러시아어 키워드와
대조해 모든 그룹을 프리셋으로 채웁니다.

**정직한 한계.** `PhysicMaterial`에는 네 개의 숫자와 두 개의 합성 모드가 있습니다.
구름 마찰, 이방성 마찰, 점성, 소성 변형, 온도, 마모를 표현할 수 없습니다.
여기서 "실제 세계 파라미터"는 출처가 있는 조회 테이블과 쓸 만한 프리셋을
뜻하며, 물리 시뮬레이션이 아닙니다.

<a id="sec-11"></a>
## 11. 커버리지 진단

Bake 탭은 보통 추측에 의존하는 질문에 답합니다: **어떤 삼각형이 볼록체를
전혀 갖지 않는가?**

바인드 포즈의 소스 메시를 가져와 모든 삼각형의 중심을 모든 볼록체의
평면에 대해 검사하고 다음을 보고합니다:

- 전체 백분율과 진행 막대;
- 파트별 내역;
- 커버되지 않은 삼각형 목록. Scene 뷰에서 빨간색으로 그릴 수 있습니다
  (**Show uncovered faces**).

약 95% 미만은 문제로 보세요: 정밀도를 올리거나, 파트 본이 실제로
전체 스켈레톤을 커버하는지 확인하세요.

<a id="sec-12"></a>
## 12. 콜리전 상태 점검

100점 만점 점수와 함께 모든 문제를 나열하고, 가능하면 원클릭 수정을 제공합니다.

검사 항목에는 다음이 포함됩니다: 아무것도 베이크되지 않음; PhysX 정점 상한을 넘은 볼록체;
상한에 근접한 볼록체; 퇴화한 클러스터; 어떤 파트에도 속하지 않는 삼각형; 마지막 베이크 이후
변경된 소스 메시; 머티리얼이 없거나, 볼록체가 없거나, 파편화된 볼록체가 너무 많은
머티리얼 그룹; 인터랙트 레이어가 없는 Hitbox/Trigger 역할; 부모 체인에
없는 `Rigidbody`; 너무 작거나 너무 큰 리지드바디 질량; 완전히 켜진 래그돌
자기 충돌; 리스너가 없는 디스패치된 이벤트; 그리고
[부록 B](#appendix-b-project-physics-checks)의 프로젝트 수준 물리 검사.

<a id="sec-13"></a>
## 13. 이벤트와 연동

모든 이벤트는 완전한 컨텍스트를 담으므로, 다시는 무언가를 조회할 필요가 없습니다:

```csharp
public struct DycEvent
{
    public DycEventKind kind;          // CollisionEnter/Exit, TriggerEnter/Exit
    public int elementIndex;
    public string elementName;
    public Transform bone;             // the bone the hull is attached to
    public int groupIndex;
    public string groupName;
    public PhysicMaterial material;
    public Collider selfCollider;      // which of your hulls
    public Collider otherCollider;
    public Rigidbody otherBody;
    public GameObject otherRoot;
    public Vector3 point;
    public Vector3 normal;
    public float relativeSpeed;
    public float damageMultiplier;     // element × group
    public string eventName;
    public float time;
}
```

이를 소비하는 세 가지 방법:

1. **UnityEvent** — 컴포넌트의 `onEvent`, 코드로 등록하는 리스너용.
2. **문자열 레지스트리** — 파트나 그룹에 이벤트 이름을 주고
   `Dyc_Events.Register("Hit.Head", handler)`로 리스닝합니다. 철자를 틀려도 오류는 나지 않지만,
   상태 점검은 아무도 받지 못한 디스패치를 보고합니다.
3. **정적 파사드** — 외부 도구용 `Dyc_Api`:

```csharp
Dyc_Api.Register("Hit.Head", OnHeadHit);
Dyc_Api.GlobalEvent += e => Debug.Log(e.kind);
Dyc_Api.GlobalFilter = e => e.otherCollider.CompareTag("Friendly");  // swallow
Dyc_Api.Rebuild(gameObject);
Dyc_Api.SwapBaked(gameObject, otherSet);       // e.g. runtime precision switching
Dyc_Api.SetCollidersEnabled(gameObject, false);
Dyc_Api.GetStats(gameObject, out int parts, out int hulls);
```

데미지 배율은 파트와 그룹에 있으며 서로 곱해집니다 —
머리 ×4는 하나의 숫자이며, 글루 코드의 층이 아닙니다.

**중복 제거.** 이음새 겹침 때문에 인접한 두 그룹이 같은 프레임에 같은
외부 콜라이더에 닿을 수 있습니다. NDC는 프레임당 `(파트, 외부 콜라이더)`마다
최대 하나의 이벤트만 디스패치하므로, 분할된 머티리얼이 이벤트를 중복시키지 않습니다.

<a id="sec-14"></a>
## 14. Trigger 폴링

Unity는 Trigger 콜백을 **Rigidbody 쌍별**로 전달하므로, 래그돌 하나는
하나의 리지드바디 쌍이고 물리 레이어는 어떤 본이 볼륨에 들어왔는지
알려줄 수 없습니다. 모든 본에 자체 `Rigidbody`를 주면 제로 비용 약속이 깨집니다.

그래서 분할된 Trigger는 샘플링됩니다:

- 각 파트는 콜라이더의 월드 공간 바운드에 대해 `Physics.OverlapBoxNonAlloc`으로
  검사됩니다.
- 실제 Trigger만 고려되며, 자신의 콜라이더는 절대 포함되지 않습니다.
- 파트는 슬라이스로 처리됩니다: 프레임당 `elements / frames-per-pass`.
- 진입과 이탈은 각 파트의 이전 패스와 비교(diff)됩니다.

**기억할 의미론:** 이것은 샘플링이며 이벤트가 아닙니다.
아주 빠른 통과는 놓칠 수 있습니다. 샘플 레이트를 올리거나 **스윕 마진**으로 쿼리 박스를 넓히세요.

<a id="sec-15"></a>
## 15. 질량, 자기 충돌, LOD

**밀도로부터의 질량.** NDC는 모든 볼록체의 부피를 알기 때문에 질량을 제대로
계산할 수 있습니다: `mass = 볼록체 부피 × 그룹 밀도`. 선택적으로 캐릭터 전체가
목표 총 질량에 맞도록 정규화할 수 있습니다. 이것은 Unity 래그돌에서 가장 오래된
수동 조정 작업을 없애줍니다. 하나의 리지드바디는 자신이 소유한 볼록체 부피의 합을 받습니다.

**자기 충돌.** `Ignore`(모든 쌍), `Adjacent`(같은 파트, 또는 조상과
자손), `On`. 래그돌 본끼리 충돌하는 것은 지터의 흔한 원인이며,
보통 `Adjacent`가 답입니다. 콜라이더가 200개를 넘으면 `Awake`를 막는
대신 경고와 함께 이 단계를 건너뜁니다.

**LOD.** `Disable`은 일정 거리를 넘으면 콜라이더를 끕니다; `Reduce`는 파트당
가장 큰 볼록체만 남깁니다. 검사는 네 프레임마다 실행됩니다.

**리지드바디.** Unity는 `Rigidbody`를 소유한 GameObject에만 콜리전 콜백을
전달합니다. 래그돌은 모든 본이 이미 하나씩 갖고 있습니다. 그 외의 경우
**Auto-add Rigidbody**를 켜면 NDC가 컴포넌트 오브젝트에 키네마틱 리지드바디를 만듭니다.

<a id="sec-16"></a>
## 16. 로컬라이제이션

창, 인스펙터, 상태 메시지, 메뉴 캡션은
**15개 언어**로 로컬라이즈됩니다:

`en`(내장) · `zh-Hans` · `ru` · `zh-Hant` · `fr` · `de` · `it` · `es` · `pt` ·
`ja` · `ko` · `tr` · `pl` · `ar` · `he`

- 영어는 어셈블리에 내장되어 누락된 키의 대체 값이 되므로,
  부분적으로 번역된 언어는 깨지는 대신 저하됩니다.
- 다른 모든 언어는 `Locale/<code>/strings.json`의 순수 데이터입니다 — 추가에
  재컴파일이 필요 없습니다.
- 아랍어와 히브리어는 완전한 우에서 좌 방향입니다: 레이아웃은 `style.direction`에
  의존하지 않고 미러링합니다. 그 UI Toolkit 지원은 불완전하고 버전에 따라 다릅니다.
- 언어는 **Settings** 탭에서 변경합니다. 창과 메뉴는 즉시
  갱신되며 도메인 리로드가 없습니다.
- Settings 탭은 해석된 로케일 경로와 발견된 언어 수를 보여주므로,
  패키징 실수가 조용히 넘어가지 않고 드러납니다.

<a id="sec-17"></a>
## 17. 디렉터리 구조

```
Assets/NekoDynamicCollision/
├── Runtime/                  runtime assembly (no UnityEditor references)
│   ├── Dyc_DynamicCollision.cs    the component
│   ├── Dyc_BakedSet.cs            baked data asset
│   ├── Dyc_PaintMask.cs           brush labels
│   ├── Dyc_Types.cs               enums, elements, groups, event struct
│   ├── Dyc_Events.cs              string registry
│   ├── Dyc_Relay.cs               collision forwarding
│   ├── Dyc_TriggerPoll.cs         partitioned trigger sampling
│   └── Dyc_Api.cs                 integration facade
├── Editor/                   editor-only assembly
│   ├── Core/                      baker, hull maths, clustering, health, presets
│   └── UI/                        window, inspector, brush, gizmo, icon, style
├── Locale/<code>/strings.json     14 language packs (+ built-in English)
├── Documents/GUIDE.<code>.md      15 manuals
├── Baked/<Scene>/                 bake output (generated)
├── README.md
└── package.json
```

<a id="sec-18"></a>
## 18. 제거

1. 프리팹과 씬에서 **Dynamic Collision** 컴포넌트를 제거합니다.
2. `Assets/NekoDynamicCollision`을 삭제합니다.

베이크된 에셋은 플러그인 폴더 안 `Baked/` 아래에 있어 함께 사라집니다.
플러그인 폴더 밖에는 아무것도 기록되지 않으며, 런타임에는 에디터 절반에
의존하는 코드가 없습니다.

<a id="sec-19"></a>
## 19. 문제 해결과 FAQ

**아무것도 충돌하지 않고 이벤트도 발생하지 않는다.**
부모 체인에 `Rigidbody`가 없습니다. Unity는 리지드바디를 소유한 오브젝트에만
콜리전 콜백을 보냅니다. **Auto-add Rigidbody**를 켜거나 직접 추가하세요.

**볼록체가 보이는 것과 일치하지 않는다.**
Gizmo는 기본적으로 **바인드 포즈**를 그립니다 — 그것이 베이크된 것입니다.
Gizmo 탭에서 **Bind pose**를 끄면 현재 포즈로 볼 수 있습니다.

**"Hull has N vertices, over the PhysX limit of 255."**
Unity는 한계를 넘은 볼록체를 조용히 무시합니다. 정밀도를 한 단계 낮추세요;
상태 점검이 바로 그것을 원클릭 수정으로 제공합니다.

**일부 위치에서 히트를 놓친다.**
먼저 커버리지 백분율을 확인하세요. 약 95% 미만은 실제 구멍을 뜻합니다.
다음으로 현재 정밀도 단계의 **이음새 겹침**을 확인하세요.

**페인트 스트로크가 두 영역 사이에 틈을 남겼다.**
그것이 이음새 문제입니다. 정밀도를 올리거나(이음새 겹침이 줄어듭니다) 경계를
조금 넘겨 칠하세요. 같은 프레임 중복 제거가 겹침으로 인한 이중 이벤트를
이미 막아줍니다.

**한 번의 히트에 이벤트가 두 번 발생한다.**
같은 프레임에 두 개의 서로 다른 파트가 맞은 것이며 이는 정상입니다. 정말로
오브젝트 쌍당 하나의 이벤트를 원한다면 핸들러에서 `elementIndex`로 필터링하세요.

**칠했는데 베이크 후 아무것도 바뀌지 않았다.**
삼각형 수가 소스 메시와 맞지 않으면 라벨이 무시됩니다 —
보통 재임포트나 토폴로지 변경 후입니다. 다시 칠하거나, 라벨 에셋이 올바른
크기로 생성되도록 먼저 베이크하세요.

**브러시가 시작되지 않는다.**
Play 모드가 실행 중이거나 Animation 창이 프리뷰 중입니다. 둘 다 Paint 탭에
명시적인 이유로 표시됩니다.

**업데이트 후 이전 브러시 작업이 사라졌다.**
그러면 안 됩니다: 칠하기 플래그가 생기기 전에 만들어진 마스크는 마이그레이션되며,
0이 아닌 라벨은 칠해진 것으로 취급됩니다. 마스크가 지워졌다면 다시 칠하고 다시 베이크하세요.

**런타임 비용이 정말 제로인가?**
정상 상태에서는 그렇습니다: 볼록체는 에셋이고, 변환은 계층이 따라가며,
메시 작업은 전혀 없습니다. 프레임당 작업은 선택적 Trigger 폴링과
LOD 거리 검사뿐입니다.

**하나의 오브젝트에 두 개의 Dynamic Collision 컴포넌트를 둘 수 있나?**
아니요, 의도적으로 막혀 있습니다. 두 컴포넌트는 같은 면 위에 중복 볼록체를
만들어 콘택트를 두 배로, 이벤트를 두 배로 만듭니다. 대신 파트와
머티리얼 그룹으로 분할하세요.

<a id="sec-20"></a>
## 20. 연락처

NekoAndreeva — 저장소 URL은 `package.json`을 참고하세요.

---

<a id="sec-appA"></a>
## 21. 일반적인 사용법과 RASCAL 동등성

### 21.1 시스템 콜라이더로서의 NDC

설계 원칙은 `Collider`와 `Rigidbody`를 아는 개발자는 이미 NDC를 안다는 것입니다. NDC는 평범한 콜라이더를 *만들기* 때문입니다: 숨겨진 자식의 `MeshCollider`, 오브젝트의 `Rigidbody`, 표준 메시지, 평범한 레이어와 물리 머티리얼. `Physics.Raycast`와 `Physics.OverlapSphere`는 전혀 바꿀 필요가 없습니다.

```csharp
using NekoDynamicCollision;

void Start()
{
    Dyc_Collision.Attach(gameObject);      // 추가 + 빌드
    Dyc_Collision.SetMaterial(gameObject, myMaterial);
}

void OnCollisionEnter(Collision c) { }     // 표준 Unity 메시지
void OnTriggerStay(Collider other) { }     // 표준 Unity 메시지
```

| 호출 | 의미 |
|---|---|
| `Find(go)` | 컴포넌트, 오브젝트 또는 부모에서 |
| `Attach(go, generateNow)` | 컴포넌트를 추가하고 빌드 |
| `Build(go)` / `Rebuild(go)` | 베이크된 세트에서 빌드하거나 런타임에 생성 |
| `IsBuilt(go)` / `GetEnabled(go)` / `SetEnabled(go, v)` | 상태, 모든 볼록체를 한 번에 |
| `SetTrigger(go, v)` | 모든 볼록체의 `Collider.isTrigger` |
| `SetMaterial(go, pm)` | 즉시; 재빌드를 넘어 유지되지 않음 |
| `GetColliders(go)` / `ForEachCollider(go, a)` | 볼록체를 평범한 `Collider`로 |
| `SetReceiver(go, t)` | 표준 메시지를 `t`에도 보냄 |

**추가되는 것은 존 지정뿐입니다.** 존, 칠해진 머티리얼, LOD, 이벤트, 밀도로부터의 질량, 상태 점검에는 NDC API가 필요합니다 — 시스템 콜라이더가 할 수 없는 일들입니다.

**베이크 단계가 없습니다.** **Advanced ▸ Build at startup when nothing is baked**를 켜거나 `Attach`를 호출하세요. 콜라이더는 `Awake`에서 메시로부터 본별로 만들어집니다 — 본마다 볼록체 하나, RASCAL의 기본과 같습니다. 존, 분해, 커버리지, 정밀도를 얻는 길은 여전히 베이크입니다.

**스크립트에 도달하는 메시지.** Unity는 `OnCollision*`을 `Rigidbody`가 있는 오브젝트에 전달합니다. 스크립트가 다른 곳에 있다면(캐릭터 루트에 스크립트가 있고 Rigidbody가 본에 있는 경우), `Advanced ▸ Also send OnCollision*/OnTrigger* to`를 설정하세요 — 그러면 메시지가 `SendMessage`로 전달되며, 충돌이 없는 프레임에서는 아무 비용도 들지 않습니다.

### 21.2 라이브 업데이트 — 베이크가 대신할 수 없는 능력

본에 붙인 베이크된 볼록체는 바인드 포즈에서 정확하고 그 뒤로는 강체입니다. 강한 변형 — 웅크림, 눌린 사지, 팽팽한 천 — 아래에서는 볼록체가 표면을 잘못 보고합니다. 라이브 업데이트는 **현재** 스키닝 포즈에서 볼록체를 다시 만듭니다.

**Advanced ▸ Live update** 또는 `Dyc_Collision.EnableLiveUpdate(go)`로 켭니다.

| 설정 | 기본값 | 의미 |
|---|---|---|
| `liveUpdate` | 끔 | 현재 포즈에서 볼록체 재빌드 |
| `liveUpdateContinuous` | 켬 | 계속하거나 요청 시 한 번 실행 |
| `idleCpuBudgetMs` | 0.2 | 메시가 거의 움직이지 않을 때의 예산 |
| `activeCpuBudgetMs` | 1.0 | 빠르게 움직일 때의 예산 |
| `meshUpdateThreshold` | 0.02 | 이 이동량(미터) 미만이면 패스 건너뜀 |
| `maxColliderTriangles` | 5000 | 콜라이더당 상한, 무거운 본이 예산을 다 먹지 않도록 |

예산은 메시가 실제로 얼마나 움직였는지로 선택되므로, 서 있는 캐릭터에는 유휴 요율이, 달리는 캐릭터에는 활성 요율이 부과됩니다. 맞지 않는 작업은 다음 프레임으로 미뤄지고, `OnUpdateYield` / `OnPassComplete`가 경과 밀리초를 보고합니다.

**매 프레임 전부를 다시 만들지는 않습니다.** 세 가지 메커니즘이 비용을 예측 가능하게 유지합니다:

1. **증분식.** 각 클러스터의 중심을 이전 패스와 비교해 실제로 움직인 클러스터만 다시 만듭니다. 앵커에 매달린 소프트 바디는 밑단이 떨리고 가운데는 거의 멈춰 있습니다 — 가운데는 아무 비용도 들지 않습니다.
2. **우선순위 정렬.** 큐는 각 클러스터가 얼마나 움직였는지로 정렬됩니다. 예산이 떨어지면 가장 차분한 클러스터 — 부정확함이 가장 덜 보이는 곳 — 에서 떨어집니다. 이것이 없으면 예산은 목록에서 우연히 앞에 있는 것에 쓰였을 것입니다.
3. **클러스터 수가 아니라 시계로 예산 책정**: 프레임당 비용은 바디가 가진 클러스터 수에 따라 늘지 않습니다.

`LastDirtyCount`와 `LastBuiltCount`는 마지막 패스가 실제로 무엇을 했는지 보고합니다. 절약을 확인하는 가장 정직한 방법입니다.

```csharp
var live = Dyc_Collision.EnableLiveUpdate(gameObject);
live.OnPassComplete += ms => Debug.Log($"pass done in {ms:F2} ms");
live.StopAfterPass();          // 현재 패스를 끝낸 뒤 중지
live.UpdateNow();              // 예산 밖에서 한 번의 완전한 패스
```

**요구 사항.** 볼록체에는 베이크가 기록하는 `sourceVertices`가 필요합니다. 오래된 캐릭터는 라이브 업데이트를 켜려면 다시 베이크하세요. 런타임 비용은 실재합니다 — "프레임당 비용 0"과 모순되는 유일한 기능이며, 그래서 기본적으로 꺼져 있습니다.

### 21.3 본별 재정의

`Dyc_BoneProperties`는 본 자체에 둡니다 (Add Component ▸ NekoWorks ▸ Dynamic Collision ▸ Bone Properties):

| 필드 | 효과 |
|---|---|
| `overrideMaterial` + `physicsMaterial` | 이 본의 볼록체가 그 머티리얼을 사용 |
| `overrideConvex` + `convex` | 이 본에서 표면 대신 볼록체(또는 그 반대) |
| `overrideWeightThreshold` + `boneWeightThreshold` | 본별 가중치 컷오프 |
| `exclude` | 이 본에는 콜라이더 없음 |

본에 다는 것은 이름이 바뀌어도 살아남는다는 뜻입니다 — 경로가 아니라 참조를 들고 있습니다.

### 21.4 소스 머티리얼별 머티리얼

`Advanced ▸ Materials by source material`은 소스 `Material`을 `PhysicMaterial`에 매핑합니다. 볼록체는 주로 어디에서 왔는지에 따라 서브메시로 귀속되며, 베이크 시 `Dyc_BakedSet.sourceMaterials`로 해석됩니다. 우선순위는 높은 순서로:

1. `Dyc_BoneProperties.physicsMaterial`;
2. 볼록체의 소스 머티리얼에 대한 머티리얼 연결;
3. 칠해진 그룹의 머티리얼.

### 21.5 정점 제외 맵

`Advanced ▸ Exclusion map`은 텍스처 채널(R/G/B/A, 임계값 포함)을 읽고 채널 값이 임계값 이상인 정점을 제외합니다. 브러시는 *면*을, 맵은 *정점*을 표시합니다 — 서로를 보완합니다. 메시에는 UV가, 텍스처에는 **Read/Write Enabled**가 필요하며, 삼각형은 세 정점이 모두 제외될 때만 제외됩니다.

### 21.6 스켈레톤 리타기팅

`Advanced ▸ Attach hulls to another skeleton`은 이 메시에서 볼록체를 만들지만 다른 루트의 같은 이름 본에 답니다 — `RetargetSkeleton` 사례로, Puppet Master 같은 구성에 쓰입니다. 본은 상대 경로로 해석됩니다. 같은 이름의 대응이 없는 볼록체는 자기 스켈레톤에 남고, 베이크 보고서가 그 개수를 알려줍니다.

### 21.7 소프트 모드 — 본 없음, 코드 또는 솔버 구동

소프트 모드(`Mode → Soft`)는 스키닝 리그가 **아닙니다**. **스켈레톤이 없고** 형상이 솔버나 코드로 생성되는 메시를 위한 것입니다 — NekoDynamicSoftbody 등. 소프트 모드에서는 아무것도 본을 읽지 않으며, 메시 지오메트리를 그대로 사용합니다.

**베이크가 만들어 내는 것.** 메시는 번호가 붙은 공간 클러스터로 나뉩니다. 각 볼록체는 *자기 클러스터 중심에 상대적으로* 만들어지고, 중심은 그 클러스터의 정지 포즈(`clusterRest`)로 저장됩니다. 이것이 프레임이 볼록체를 한 덩어리로 **이동하고 회전**할 수 있게 해 줍니다.

**솔버가 없을 때.** 프레임은 정지 포즈에 만들어지므로 볼록체가 실제 메시 지오메트리에 정확히 놓이고 오브젝트와 함께 움직입니다. 형상은 맞고, 단지 역학이 없을 뿐입니다. 이것은 의도된 성능 저하이지 실패가 아닙니다 — "NDSC 없이 실제 형상을 계산한다"는 뜻이 바로 이것입니다.

**솔버가 있을 때.** 솔버가 프레임을 밀고(`Push` → `Apply`) 완전히 넘겨받아 완전한 시뮬레이션을 제공합니다. 주소 지정 가능한 푸시는 같은 프레임의 전역 폴링에 덮어써지지 않으며, 이는 바디가 둘 이상 존재하면 바로 중요해집니다.

**라이브 업데이트는 스켈레톤 없이도 동작합니다.** `Dyc_LiveUpdate`는 `MeshFilter`의 CPU 정점을 그대로 읽으므로, 메시를 변형하는 모든 코드 — 소프트 바디 솔버, 프로시저럴 스크립트, 커스텀 디포머 — 가 플러그인 전용 접착제 없이 정확한 볼록체를 구동합니다. 볼록체에는 베이크가 기록하는 `sourceVertices`가 필요합니다. 오래된 에셋은 다시 베이크하세요.

**정직한 한계:** GPU에만 존재하는 변형(버텍스 셰이더, GPU 스키닝)은 CPU에서 다시 읽을 수 없으므로 라이브 업데이트가 보지 못합니다. 변형을 CPU로 옮기거나, 베이크된 볼록체를 유지하세요.

### 21.8 개별 볼록체 구동

```csharp
int n = component.HullCount;
for (int i = 0; i < n; i++)
{
    Collider c = component.HullCollider(i);
    int hullIndex = component.HullIndexOf(c);
    DycBakedHull hull = component.BakedSet.hulls[hullIndex];
}
```

`HullCollider`, `HullIndexOf`, `HullCount`, `BakedSet`, `Elements`, `Groups`는 공개되어 있으므로 외부 도구가 리플렉션 없이 개별 볼록체를 순회하고 구동할 수 있습니다.

---

## 부록 A. PhysicMaterial 프리셋

224개 프리셋, 14개 카테고리. 값은 출처가 있는 공학적 근사치이며 Unity의 4개 파라미터 모델에 매핑됩니다. '신체 부위'와 '조직과 장기'는 표에 적는 대신 단조가 조직 배합에서 유도합니다.

| 카테고리 | 개수 | 프리셋 |
|---|---|---
| Metal | 26 | Steel, Stainless, Cast Iron, Aluminium, Cast Aluminium, Anodised, Copper, Brass, Bronze, Titanium, Lead, Tungsten, Chrome, Nickel, Zinc, Magnesium, Gold, Silver, Platinum, Galvanised, Rusted Steel, Gun Steel, Tool Steel, Armour Steel, Sheet Metal, Rebar |
| Ceramic | 10 | Porcelain, Bone China, Ceramic Tile, Terracotta, Alumina, Silicon Carbide, Zirconia, Glass-Ceramic, Enamel, Unfired Clay |
| Plastic | 18 | ABS, PVC, Nylon, Acrylic, Polycarbonate, HDPE, PTFE, PEEK, POM, PET, Polypropylene, Polystyrene, TPU, PLA, Bakelite, Melamine, Vinyl, Rigid Urethane |
| Glass | 9 | Glass, Tempered, Laminated, Frosted, Thick, Armoured, Mirror, Crystal, Borosilicate |
| Wood | 16 | Oak, Pine, Plywood, Cork, Birch, Maple, Walnut, Teak, Mahogany, Balsa, MDF, Particleboard, Bamboo, Wet Wood, Charred Wood, Round Timber |
| Stone & Concrete | 15 | Concrete, Wet Concrete, Brick, Asphalt, Granite, Marble, Polished Marble, Limestone, Sandstone, Slate, Basalt, Cobblestone, Gravel, Rubble, Sand |
| Rubber | 12 | Rubber, Tire, SBR, Latex, EPDM, Butyl, Neoprene, Silicone, Hose Rubber, Rubber Mat, Rubber Foam, Shock Gel |
| Fabric & Leather | 25 | Leather, Canvas, Carpet, Kevlar, Body Armour, Vest Shell, Cotton, Denim, Silk, Satin, Wool, Linen, Burlap, Velvet, Felt, Nomex, Spandex, Gore-Tex, Parachute, Nylon, Suit Fabric, Upholstery, Heavy Tarp, Blanket, Towel |
| Body parts | 39 | Head, Skull, Scalp, Hair, Face, Cheek, Lip, Jaw, Nose, Ear, Eyeball, Tooth, Tongue, Neck, **Chest**, Breast, Abdomen, Groin, Back, Hip, Pelvis, Shoulder, Bicep, Elbow, Forearm, Wrist, Hand, Palm, Fist, Knuckle, Nail, Glute, Thigh, Knee, Calf, Shin, Ankle, Foot, Sole |
| Tissue & organs | 10 | Bone, Cartilage, Tendon, Muscle, Fat, Skin, Organ, Lung, Brain, Keratin |
| Organic | 3 | Flesh, Bone, Ballistic Gel |
| Ice, snow & mud | 10 | Ice, Wet Ice, Black Ice, Snow, Powder Snow, Packed Snow, Slush, Hail, Frozen Ground, Mud |
| Food | 9 | Bread, Fruit, Vegetable, Raw Meat, Cooked Meat, Fish, Cheese, Chocolate, Ice Cream |
| Other | 22 | Cardboard, Wet Cardboard, Paper, Foam, Sponge, Bubble Wrap, Drywall, Tarp, Sandbag, Dirt, Grass, Hay, Leaf Litter, Trash Bag, Mineral Wool, Rope, Net, Coal, Ash, Rock Salt, Sugar, Wax |


모든 프리셋은 자동 질량용 **밀도**(kg/m³)도 가지며, 고무 계열 프리셋은
튀기 위해 필요한 `bounceThreshold`를 갖습니다.

<a id="sec-appB"></a>
## 부록 B. 프로젝트 물리 설정 점검

상태 점검은 프로젝트의 `Physics` 설정을 감사합니다. 머티리얼 프리셋으로는
전역 설정을 고칠 수 없기 때문입니다:

| 설정 | 중요한 이유 |
|---|---|
| `bounceThreshold` | 이보다 느린 충격은 절대 튀지 않습니다. Unity 기본값 2에서는 고무 프리셋이 망가진 것처럼 보입니다. 탄성 머티리얼을 쓰려면 0.2–0.5로 낮추세요. |
| `defaultSolverVelocityIterations` | 1이면 스택과 빠른 충격이 지터하거나 터널링합니다. 보통 2–4가 더 좋으며, 래그돌 지터의 흔한 근본 원인입니다. |
| `gravity` | −9.81이 아니면 −9.81에서 나온 모든 질량과 임펄스 감각이 같은 계수만큼 어긋나며, 밀도 프리셋을 보정해야 합니다. |
| `defaultContactOffset` | 넓은 콘택트 간격은 얇은 물체가 떠 있는 것처럼 보이게 합니다. |

권장 값 적용은 Materials 탭이나 상태 점검에서
원클릭으로 할 수 있습니다.
