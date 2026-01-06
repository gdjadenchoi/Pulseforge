# Pulseforge TopBar/HUD 구조 분석 리포트

**작성일:** 2025년  
**목적:** TopBar/HUD 구조 분석 및 ExpManager 연동 설계 포인트 파악

---

## 목차

1. [TopBar / HUD 구조 상세 분석](#1-topbar--hud-구조-상세-분석)
2. [ExpManager 연동 관점에서의 설계 포인트](#2-expmanager-연동-관점에서의-설계-포인트)
3. [향후 확장 관점에서의 위험도 체크](#3-향후-확장-관점에서의-위험도-체크)

---

## 1. TopBar / HUD 구조 상세 분석

### 1.1 TopBar 관련 스크립트 목록

#### 1) TimeHUD (세션 시간 표시)

**클래스명:** `TimeHUD` (네임스페이스 없음, 전역)  
**파일 경로:** `Assets/UI/TimeHud.cs`  
**GameObject 경로:** `PF_Mining/_UI/Canvas/TopBar/TimeText` (TimeHUD 컴포넌트)

**Inspector 주요 필드:**

| 필드명 | 타입 | 설명 |
|--------|------|------|
| **timeText** | `TMP_Text` | 시간 표시 텍스트 |
| **session** | `SessionController` | 세션 컨트롤러 참조 |
| **normalColor** | `Color` | 일반 색상 |
| **criticalColor** | `Color` | 크리티컬 색상 |
| **blinkOnCritical** | `bool` | 크리티컬 구간 깜빡임 여부 |
| **timeFormat** | `string` | 시간 표시 형식 |

**역할:**
- 세션 남은 시간 표시
- 크리티컬 시간 구간 시 색상 변경 및 깜빡임
- 세션 시작/종료 시 자동 표시/숨김

---

#### 2) ResourceHUD (자원 표시)

**클래스명:** `ResourceHUD` (네임스페이스 없음, 전역)  
**파일 경로:** `Assets/UI/ResourceHD.cs`  
**GameObject 경로:** `PF_Mining/_UI/Canvas/TopBar/ResourceText` (ResourceHUD 컴포넌트)

**Inspector 주요 필드:**

| 필드명 | 타입 | 설명 |
|--------|------|------|
| **resourceText** | `TMP_Text` | 자원 표시 텍스트 |
| **rewardManager** | `RewardManager` | 보상 매니저 참조 |
| **showAllTypes** | `bool` | 모든 타입 표시 여부 |
| **rewardType** | `RewardType` | 표시할 단일 타입 |
| **refreshInterval** | `float` | UI 갱신 간격 (초) |

**역할:**
- 보상 자원 실시간 표시
- 단일/전체 타입 표시 모드 지원
- 주기적 폴링으로 업데이트

---

#### 3) TopBar (루트 GameObject)

**Prefab 경로:** `Assets/Prefabs/TopBar.prefab`  
**씬 내 인스턴스 경로:**
- `PF_Mining/_UI/Canvas/TopBar` (Prefab Instance)
- `PF_Outpost/_UI/Canvas/TopBar` (추정, Prefab Instance 또는 직접 생성)

**구조:**
```
TopBar (루트 GameObject)
├── TimeText (GameObject)
│   └── TimeHUD 컴포넌트
│       ├── timeText: TMP_Text (자기 자신)
│       └── session: SessionController (씬에서 연결)
│
└── ResourceText (GameObject)
    └── ResourceHUD 컴포넌트
        ├── resourceText: TMP_Text (자기 자신)
        └── rewardManager: RewardManager (씬에서 연결)
```

**특징:**
- TopBar 자체에는 별도 스크립트 없음 (컨테이너 역할만)
- 각 HUD 컴포넌트가 독립적으로 동작
- PF_Mining에서는 Prefab Instance로 사용, Inspector에서 참조 연결

---

### 1.2 의존성 다이어그램

```
┌─────────────────────┐
│ TopBar (GameObject) │
│ (컨테이너, 스크립트 없음) │
└──────────┬──────────┘
           │
           ├─────────────────┐
           │                 │
           ▼                 ▼
┌─────────────────┐  ┌─────────────────┐
│ TimeText        │  │ ResourceText    │
│ (TimeHUD)       │  │ (ResourceHUD)   │
└────────┬────────┘  └────────┬────────┘
         │                     │
         │ session 필드        │ rewardManager 필드
         │ (Inspector 연결)    │ (Inspector 연결)
         │                     │
         ▼                     ▼
┌─────────────────┐  ┌─────────────────┐
│SessionController│  │ RewardManager   │
│ (_Systems)      │  │ (DontDestroyOnLoad)│
└─────────────────┘  └─────────────────┘
         │                     │
         │ 이벤트 구독         │ Get() 메서드 호출
         │ (OnTimeChanged 등) │ (주기적 폴링)
         │                     │
         └─────────────────────┘
```

**의존성 요약:**

| HUD 컴포넌트 | 참조 대상 | 참조 방식 | 초기화 타이밍 |
|-------------|----------|-----------|--------------|
| **TimeHUD** | `SessionController` | Inspector 필드 또는 `FindObjectOfType` (Start) | `Start()` 메서드 |
| **ResourceHUD** | `RewardManager` | Inspector 필드 또는 `FindAnyObjectByType` (Awake) | `Awake()` 메서드 |

**참조 방식 상세:**

1. **TimeHUD → SessionController:**
   - Inspector에서 `session` 필드에 연결 (PF_Mining 씬에서)
   - 연결되지 않으면 `Start()`에서 `FindObjectOfType<SessionController>()` 호출
   - `Start()`에서 이벤트 구독 (`OnTimeChanged`, `OnCritical`, `OnSessionStart`, `OnSessionEnd`)

2. **ResourceHUD → RewardManager:**
   - Inspector에서 `rewardManager` 필드에 연결 (PF_Mining 씬에서)
   - 연결되지 않으면 `Awake()`에서 `FindAnyObjectByType<RewardManager>()` 호출
   - `OnEnable()`에서 `RefreshLoop()` 코루틴 시작 (주기적 폴링)

---

### 1.3 PF_Mining vs PF_Outpost 비교

#### PF_Mining 씬

**TopBar 구성:**
- **Prefab Instance**: `TopBar.prefab` 사용
- **위치**: `_UI/Canvas/TopBar`
- **구성 요소**:
  - `TimeText` (TimeHUD 컴포넌트) - **활성화**
  - `ResourceText` (ResourceHUD 컴포넌트) - **활성화**

**Inspector 연결:**
- `TimeHUD.session` → `SessionController` (씬 내 연결)
- `ResourceHUD.rewardManager` → `RewardManager` (씬 내 연결)
- `TopBar` 루트에도 `ResourceHUD` 컴포넌트가 있음 (중복, 사용되지 않음)

**사용 기능:**
- ✅ 세션 타이머 표시 (TimeHUD)
- ✅ 자원 표시 (ResourceHUD)
- ✅ 세션 시작/종료 시 TimeHUD 자동 표시/숨김

---

#### PF_Outpost 씬

**TopBar 구성:**
- **Prefab Instance 또는 직접 생성**: 확인 필요 (씬 파일에 TopBar 존재)
- **위치**: `_UI/Canvas/TopBar` (추정)
- **구성 요소**:
  - `TimeText` (TimeHUD 컴포넌트) - **비활성화 또는 없음** (SessionController가 없으므로)
  - `ResourceText` (ResourceHUD 컴포넌트) - **활성화** (추정)

**Inspector 연결:**
- `ResourceHUD.rewardManager` → `null` 또는 `RewardManager` (DontDestroyOnLoad로 유지됨)
- `TimeHUD.session` → `null` (SessionController가 없음)

**사용 기능:**
- ❌ 세션 타이머 표시 (SessionController가 없으므로 불필요)
- ✅ 자원 표시 (ResourceHUD)

---

#### 비교 요약

| 항목 | PF_Mining | PF_Outpost |
|------|-----------|------------|
| **TopBar Prefab** | 동일한 Prefab 사용 (추정) | 동일한 Prefab 사용 (추정) |
| **TimeHUD** | ✅ 활성화, SessionController 연결 | ❌ 비활성화 또는 없음 |
| **ResourceHUD** | ✅ 활성화, RewardManager 연결 | ✅ 활성화, RewardManager 연결 |
| **씬별 차이** | TimeHUD만 추가로 활성화 | TimeHUD 비활성화 |

**현재 구조 특징:**
- 같은 Prefab을 공유하지만, 씬에 따라 일부 요소만 활성화/비활성화하는 방식
- TimeHUD는 SessionController가 있는 씬에서만 동작
- ResourceHUD는 두 씬 모두에서 동작 (RewardManager가 DontDestroyOnLoad이므로)

---

## 2. ExpManager 연동 관점에서의 설계 포인트

### 2.1 ExpManager 의존성 추가 위치 후보

#### 후보 1: ResourceHUD와 유사한 별도 LevelHUD 컴포넌트 (추천)

**구조:**
```
TopBar
├── TimeText (TimeHUD)
├── ResourceText (ResourceHUD)
└── LevelText (LevelHUD) ← 새로 추가
    └── LevelHUD 컴포넌트
        ├── levelText: TMP_Text
        ├── expBar: Image (경험치 바)
        └── expManager: ExpManager (Inspector 연결)
```

**장점:**
1. **일관성**: 현재 `TimeHUD`, `ResourceHUD`와 동일한 패턴 → 코드 구조 일관성 유지
2. **책임 분리**: 각 HUD가 독립적으로 동작 → 유지보수 용이
3. **씬별 제어 용이**: `LevelHUD`만 활성화/비활성화하여 씬별 표시 제어 가능
4. **확장성**: 나중에 다른 HUD 추가 시에도 동일한 패턴 적용 가능

**단점:**
1. **컴포넌트 증가**: TopBar에 컴포넌트가 하나 더 추가됨 (미미한 단점)

**구현 위치:**
- `Assets/UI/LevelHUD.cs` (새 파일)
- `TopBar.prefab`에 `LevelText` GameObject 추가
- `LevelHUD` 컴포넌트를 `LevelText`에 붙임

**의존성:**
- `LevelHUD` → `ExpManager.Instance` (Inspector 또는 `FindObjectOfType`)
- `OnEnable()`에서 주기적 폴링 또는 이벤트 구독

---

#### 후보 2: TopBar 루트에 TopBarController 스크립트 추가

**구조:**
```
TopBar (TopBarController 컴포넌트)
├── TimeText (TimeHUD)
├── ResourceText (ResourceHUD)
└── LevelText (TMP_Text만, TopBarController가 직접 관리)
```

**장점:**
1. **중앙 집중식**: TopBar 전체를 한 스크립트에서 관리
2. **씬 모드 제어**: 인게임/아웃게임 모드를 TopBarController에서 전환 가능

**단점:**
1. **구조 불일치**: 현재는 각 HUD가 독립적으로 동작하는데, LevelHUD만 TopBarController에서 관리 → 일관성 저하
2. **책임 집중**: TopBarController가 너무 많은 책임을 가지게 됨
3. **확장성 저하**: 새로운 HUD 추가 시 TopBarController 수정 필요

**현재 프로젝트 스타일과의 호환성:**
- ⚠️ **낮음**: 현재는 각 HUD가 독립적으로 동작하는 구조

---

#### 후보 3: ResourceHUD에 통합

**구조:**
```
TopBar
├── TimeText (TimeHUD)
└── ResourceText (ResourceHUD + LevelHUD 기능 통합)
```

**장점:**
1. **컴포넌트 수 감소**: 새로운 컴포넌트 추가 불필요

**단점:**
1. **책임 혼재**: ResourceHUD가 자원 표시 + 레벨 표시를 모두 담당 → 책임 분리 위반
2. **확장성 저하**: 나중에 레벨 관련 기능이 복잡해지면 ResourceHUD가 비대해짐
3. **일관성 저하**: TimeHUD는 별도인데 LevelHUD만 ResourceHUD에 통합 → 구조 불일치

**현재 프로젝트 스타일과의 호환성:**
- ⚠️ **낮음**: 현재는 각 HUD가 독립적으로 동작하는 구조

---

### 2.2 추천 방안

**추천: 후보 1 (별도 LevelHUD 컴포넌트)**

**이유:**
1. **현재 구조와 완벽 일치**: `TimeHUD`, `ResourceHUD`와 동일한 패턴
2. **책임 분리**: 각 HUD가 독립적으로 동작 → 유지보수 용이
3. **씬별 제어 용이**: `LevelHUD`만 활성화/비활성화하여 씬별 표시 제어
4. **확장성**: 나중에 다른 HUD 추가 시에도 동일한 패턴 적용 가능

**의존성 방향:**
- `LevelHUD` → `ExpManager.Instance` (RewardManager와 동일한 패턴)
- 충돌 없음: `TimeHUD`는 `SessionController`, `ResourceHUD`는 `RewardManager`, `LevelHUD`는 `ExpManager`로 각각 독립적

---

### 2.3 씬별 모드 제어 방식 비교

#### 옵션 1: 씬에 따라 일부 요소만 켜고 끄는 방식 (현재 방식, 추천)

**구조:**
```
TopBar Prefab (공용)
├── TimeText (TimeHUD) - PF_Mining에서만 활성화
├── ResourceText (ResourceHUD) - 두 씬 모두 활성화
└── LevelText (LevelHUD) - 두 씬 모두 활성화
```

**제어 방식:**
- 각 HUD 컴포넌트가 `Awake()` 또는 `Start()`에서 필요한 매니저 존재 여부 확인
- 없으면 자동으로 비활성화 또는 오류 없이 동작

**장점:**
1. **현재 구조와 일치**: TimeHUD가 SessionController 없을 때 자동으로 처리하는 방식과 동일
2. **단순성**: 별도 모드 전환 로직 불필요
3. **유연성**: 씬에 따라 자연스럽게 동작

**단점:**
1. **명시적 제어 부족**: 씬에서 명시적으로 활성화/비활성화하지 않으면 Prefab 기본값에 의존

**현재 프로젝트 스타일과의 호환성:**
- ✅ **매우 높음**: 현재 TimeHUD가 SessionController 없을 때 자동으로 처리하는 방식과 동일

---

#### 옵션 2: 공용 Prefab + 씬별 모드 전환

**구조:**
```
TopBar Prefab (공용)
└── TopBarController 컴포넌트
    ├── mode: InGame / Outpost
    ├── TimeText 활성화 제어
    ├── ResourceText 활성화 제어
    └── LevelText 활성화 제어
```

**제어 방식:**
- `TopBarController.Awake()`에서 씬 이름 또는 별도 플래그로 모드 판단
- 모드에 따라 각 HUD 활성화/비활성화

**장점:**
1. **명시적 제어**: 씬별로 어떤 요소를 보여줄지 명확히 제어 가능
2. **중앙 집중식**: TopBar 전체를 한 곳에서 관리

**단점:**
1. **구조 변경 필요**: 현재는 각 HUD가 독립적으로 동작하는데, TopBarController 추가 필요
2. **복잡도 증가**: 모드 전환 로직 추가 필요
3. **현재 구조와 불일치**: TimeHUD, ResourceHUD가 독립적으로 동작하는 구조와 맞지 않음

**현재 프로젝트 스타일과의 호환성:**
- ⚠️ **보통**: 현재 구조와 다소 다름

---

### 2.4 추천 방안

**추천: 옵션 1 (씬에 따라 일부 요소만 켜고 끄는 방식)**

**이유:**
1. **현재 구조와 완벽 일치**: TimeHUD가 SessionController 없을 때 자동으로 처리하는 방식과 동일
2. **단순성**: 별도 모드 전환 로직 불필요
3. **유연성**: 각 HUD가 독립적으로 동작 → 유지보수 용이

**구현 방식:**
- `LevelHUD`가 `Awake()`에서 `ExpManager.Instance` 확인
- 없으면 경고 로그만 출력하고 비활성화 (또는 오류 없이 동작)
- PF_Mining, PF_Outpost 모두에서 `LevelHUD` 활성화 (ExpManager가 DontDestroyOnLoad이므로 항상 존재)

---

## 3. 향후 확장 관점에서의 위험도 체크

### 3.1 위험도 1: ResourceHUD의 주기적 폴링 방식

**위치:** `Assets/UI/ResourceHD.cs` - `RefreshLoop()` 코루틴 (56-64줄)

**현재 구조:**
```csharp
IEnumerator RefreshLoop()
{
    var wait = new WaitForSeconds(refreshInterval);
    while (true)
    {
        RefreshImmediate();
        yield return wait;
    }
}
```

**문제점:**
1. **성능 이슈**: `refreshInterval` (기본 0.1초)마다 `RewardManager.GetAll()` 호출 → 불필요한 CPU 사용
2. **확장성 부족**: RewardManager에 자원 타입이 많아지면 `GetAll()` 호출 비용 증가
3. **이벤트 미사용**: RewardManager에 `OnChanged` 이벤트가 있지만 사용하지 않음

**향후 영향:**
- 업그레이드 시스템, 스킬트리 등으로 자원 타입이 많아지면 성능 저하
- LevelHUD도 동일한 폴링 방식을 사용하면 중복 오버헤드

**레벨 시스템 브랜치에서 의식할 포인트:**
- LevelHUD는 이벤트 기반으로 구현하는 것을 고려 (ExpManager에 `OnExpChanged`, `OnLevelUp` 이벤트 추가)
- ResourceHUD도 나중에 이벤트 기반으로 전환 가능하도록 구조 설계

---

### 3.2 위험도 2: TopBar Prefab의 씬별 오버라이드 의존성

**위치:** `Assets/Prefabs/TopBar.prefab` 및 씬 파일

**현재 구조:**
- PF_Mining 씬에서 TopBar Prefab 인스턴스의 Inspector 참조를 오버라이드
- `TimeHUD.session`, `ResourceHUD.rewardManager` 등을 씬에서 연결

**문제점:**
1. **Prefab 연결 복잡도**: 씬별로 다른 참조를 연결해야 함 → 실수 가능성
2. **확장성 부족**: 새로운 HUD 추가 시 각 씬에서 참조 연결 필요
3. **유지보수 어려움**: Prefab 수정 시 씬 오버라이드 확인 필요

**향후 영향:**
- 행성 모드, 스킬트리 등으로 씬이 많아지면 각 씬에서 참조 연결 작업 증가
- LevelHUD 추가 시에도 각 씬에서 ExpManager 참조 연결 필요

**레벨 시스템 브랜치에서 의식할 포인트:**
- LevelHUD는 `FindObjectOfType` 또는 `ExpManager.Instance`로 자동 참조하도록 구현 (Inspector 연결 의존성 최소화)
- 또는 TopBarController를 추가하여 중앙에서 참조 관리

---

### 3.3 위험도 3: HUD 컴포넌트의 네임스페이스 부재

**위치:** `Assets/UI/TimeHud.cs`, `Assets/UI/ResourceHD.cs`

**현재 구조:**
- `TimeHUD`, `ResourceHUD` 모두 네임스페이스 없음 (전역)
- `SessionController`, `RewardManager`는 `Pulseforge.Systems` 네임스페이스 사용

**문제점:**
1. **네이밍 충돌 가능성**: 다른 라이브러리나 에셋과 이름 충돌 가능
2. **일관성 부족**: 시스템 스크립트는 네임스페이스를 사용하는데 UI 스크립트는 사용하지 않음
3. **확장성 저하**: 나중에 UI 관련 스크립트가 많아지면 관리 어려움

**향후 영향:**
- 업그레이드 시스템, 스킬트리 등으로 UI 스크립트가 많아지면 네이밍 충돌 가능성 증가
- LevelHUD 추가 시에도 네임스페이스 없이 추가하면 일관성 저하

**레벨 시스템 브랜치에서 의식할 포인트:**
- LevelHUD는 `Pulseforge.UI` 네임스페이스를 사용하는 것을 고려 (일관성 향상)
- 또는 기존 HUD도 나중에 네임스페이스로 이동 가능하도록 구조 설계

---

### 3.4 위험도 4: RewardManager의 DontDestroyOnLoad 싱글톤 패턴

**위치:** `Assets/Systems/RewardManager.cs` - `Awake()` 메서드 (47-59줄)

**현재 구조:**
```csharp
private void Awake()
{
    if (_instance != null && _instance != this)
    {
        Destroy(gameObject);
        return;
    }
    _instance = this;
    if (transform.parent != null) transform.SetParent(null);
    DontDestroyOnLoad(gameObject);
}
```

**문제점:**
1. **씬 전환 시 인스턴스 관리**: 각 씬에 RewardManager가 있으면 첫 번째 씬에서만 생성되고 이후 유지
2. **초기화 순서 의존성**: 다른 스크립트들이 `RewardManager.Instance`를 참조하므로 초기화 순서 중요
3. **확장성 제약**: 행성 모드 등으로 씬이 많아지면 각 씬에 RewardManager를 배치해야 함 (비활성화되더라도)

**향후 영향:**
- ExpManager도 동일한 패턴을 사용하면, 두 매니저 간 초기화 순서 관리 필요
- 행성 모드 등으로 씬이 많아지면 각 씬에 매니저 배치 작업 증가

**레벨 시스템 브랜치에서 의식할 포인트:**
- ExpManager도 RewardManager와 동일한 패턴 사용 시, `[DefaultExecutionOrder]` 속성으로 초기화 순서 제어
- 또는 부트 씬에서 모든 매니저를 한 번에 초기화하는 방식 고려

---

### 3.5 위험도 5: TimeHUD의 이벤트 구독/해제 방식

**위치:** `Assets/UI/TimeHud.cs` - `Start()`, `OnDestroy()` 메서드 (26-56줄)

**현재 구조:**
```csharp
private void Start()
{
    if (session == null)
        session = FindObjectOfType<SessionController>();
    if (session != null)
    {
        session.OnTimeChanged += HandleTimeChanged;
        // ...
    }
}

private void OnDestroy()
{
    if (session != null)
    {
        session.OnTimeChanged -= HandleTimeChanged;
        // ...
    }
}
```

**문제점:**
1. **OnDestroy 타이밍 이슈**: 씬 전환 시 `OnDestroy()`가 호출되기 전에 `SessionController`가 파괴될 수 있음
2. **메모리 누수 가능성**: 이벤트 구독이 제대로 해제되지 않으면 메모리 누수 가능
3. **확장성 부족**: LevelHUD도 동일한 방식 사용 시 동일한 문제 발생

**향후 영향:**
- 씬 전환이 빈번해지면 이벤트 구독/해제 타이밍 이슈 발생 가능
- LevelHUD도 ExpManager 이벤트를 구독할 경우 동일한 문제 발생

**레벨 시스템 브랜치에서 의식할 포인트:**
- LevelHUD는 `OnEnable()`/`OnDisable()`에서 이벤트 구독/해제하는 것을 고려 (더 안전)
- 또는 `OnDestroy()`에서 null 체크 강화

---

### 3.6 위험도 6: TopBar Prefab의 하드코딩된 레이아웃

**위치:** `Assets/Prefabs/TopBar.prefab` - RectTransform 설정

**현재 구조:**
- TimeText: 왼쪽 상단 앵커
- ResourceText: 오른쪽 상단 앵커
- 레이아웃이 Prefab에 하드코딩됨

**문제점:**
1. **확장성 부족**: LevelHUD 추가 시 레이아웃 재조정 필요
2. **반응형 부족**: 화면 크기 변경 시 레이아웃 자동 조정 어려움
3. **유지보수 어려움**: 레이아웃 변경 시 Prefab 수정 필요

**향후 영향:**
- 행성 모드, 스킬트리 등으로 UI 요소가 많아지면 레이아웃 관리 복잡도 증가
- LevelHUD 추가 시 기존 레이아웃과 충돌 가능

**레벨 시스템 브랜치에서 의식할 포인트:**
- LevelHUD 추가 시 레이아웃을 고려하여 배치 (예: 중앙 또는 왼쪽 하단)
- 또는 Unity UI Layout Group (HorizontalLayoutGroup 등) 사용 고려

---

## 부록: 의존성 체인 요약

```
TopBar (GameObject, 스크립트 없음)
    │
    ├── TimeText (TimeHUD)
    │   └── SessionController (PF_Mining에서만 존재)
    │
    ├── ResourceText (ResourceHUD)
    │   └── RewardManager (DontDestroyOnLoad, 두 씬 모두 접근 가능)
    │
    └── LevelText (LevelHUD) ← 추가 예정
        └── ExpManager (DontDestroyOnLoad, 두 씬 모두 접근 가능)
```

**의존성 특징:**
- 각 HUD는 독립적으로 동작
- 각 HUD는 자신이 필요한 매니저만 참조
- 씬별로 필요한 HUD만 활성화

---

**문서 버전:** 1.0  
**마지막 업데이트:** 2025년



















