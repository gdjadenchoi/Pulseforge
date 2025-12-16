# Pulseforge 전체 구조 분석 리포트

**작성일:** 2024년  
**목적:** 레벨/경험치 시스템(ExpManager) 추가 전, 현재 프로젝트의 전체 구조와 인게임/아웃게임 흐름 파악

---

## 목차

1. [씬 단위 개요](#1-씬-단위-개요)
2. [핵심 매니저/컨트롤러 역할 정리](#2-핵심-매니저컨트롤러-역할-정리)
3. [씬 전환 흐름](#3-씬-전환-흐름)
4. [생명주기 관점 분석](#4-생명주기-관점-분석)

---

## 1. 씬 단위 개요

### 1.1 PF_Mining 씬 구조

#### 루트 오브젝트 계층

```
PF_Mining.unity
├── Main Camera
├── RewardManager (DontDestroyOnLoad) ⭐
│   └── 위치: 씬 최상위, 씬 전환 시 유지
│
├── _Systems (루트)
│   ├── AppBoot
│   │   └── 역할: FPS/VSync 설정 (Awake에서 초기화)
│   ├── SessionController ⭐
│   │   └── 위치: _Systems 하위
│   │   └── 역할: 세션 타이머 관리, 세션 시작/종료 제어
│   ├── Legacy_RhythmSystem (비활성화)
│   └── Legacy_BeatLogger (비활성화)
│
├── _GamePlay (루트)
│   ├── OreManager (OreSpawner 컴포넌트) ⭐
│   │   └── 위치: _GamePlay 하위
│   │   └── 역할: 광석 스폰/리스폰 관리
│   ├── DrillCursor (Prefab Instance) ⭐
│   │   └── 위치: _GamePlay 하위
│   │   └── 역할: 마우스/터치 입력 추적, 광석 공격
│   └── Legacy_OreRoot (비활성화)
│
└── _UI (루트)
    ├── Canvas
    │   ├── TopBar (Prefab Instance) ⭐
    │   │   ├── TimeText (TimeHUD 컴포넌트)
    │   │   └── ResourceText (ResourceHUD 컴포넌트)
    │   └── SessionEndPopup ⭐
    │       └── 역할: 세션 종료 시 보상 요약 표시
    └── EventSystem
```

#### 핵심 오브젝트 위치 요약

| 오브젝트/스크립트 | GameObject 경로 | 비고 |
|-----------------|----------------|------|
| **SessionController** | `_Systems/SessionController` | 세션 타이머 관리 |
| **OreSpawner** | `_GamePlay/OreManager` | 광석 스폰/리스폰 |
| **DrillCursor** | `_GamePlay/DrillCursor` | Prefab 인스턴스 |
| **TopBar** | `_UI/Canvas/TopBar` | Prefab 인스턴스 (TimeHUD, ResourceHUD 포함) |
| **SessionEndPopup** | `_UI/Canvas/SessionEndPopup` | 세션 종료 팝업 |
| **RewardManager** | 씬 최상위 (DontDestroyOnLoad) | 싱글톤, 씬 전환 시 유지 |

---

### 1.2 PF_Outpost 씬 구조

#### 루트 오브젝트 계층

```
PF_Outpost.unity
├── Main Camera
├── RewardManager (DontDestroyOnLoad) ⭐
│   └── 위치: 씬 최상위 (PF_Mining에서 전환 시 유지됨)
│
├── _Systems (루트, 추정)
│   └── OutpostController ⭐
│       └── 역할: 아웃포스트 씬 관리, "Start Mining" 버튼 처리
│
└── _UI (루트, 추정)
    └── Canvas
        └── Start Mining Button
            └── OutpostController.startMiningButton에 연결
```

#### 핵심 오브젝트 위치 요약

| 오브젝트/스크립트 | GameObject 경로 | 비고 |
|-----------------|----------------|------|
| **OutpostController** | `_Systems/OutpostController` (추정) | 씬 전환 제어 |
| **RewardManager** | 씬 최상위 (DontDestroyOnLoad) | PF_Mining에서 유지됨 |

---

## 2. 핵심 매니저/컨트롤러 역할 정리

### 2.1 SessionController

**파일 경로:** `Assets/Systems/SessionController.cs`  
**GameObject 경로:** `PF_Mining/_Systems/SessionController`  
**네임스페이스:** `Pulseforge.Systems`

#### 역할 (게임 플레이 루프 관점)

- **세션 타이머 관리**: `baseDurationSec` 동안 세션 진행, `Update()`에서 `Remaining` 감소
- **세션 시작/종료 제어**: `StartSession()`, `EndSession()` 메서드로 세션 상태 관리
- **크리티컬 시간 감지**: `criticalThreshold` 도달 시 `OnCritical` 이벤트 발생 (한 번만)
- **월드 정리**: 세션 종료 시 `OreSpawner.PauseAndClear()` 호출하여 광석 정리
- **팝업 연동**: 세션 종료 시 `SessionEndPopup.Show()` 호출하여 보상 요약 표시

#### 세션 시작/종료, 보상 지급, 씬 전환에서의 타이밍

| 타이밍 | 동작 |
|--------|------|
| **씬 시작 (Start)** | `StartSession()` 자동 호출 → 세션 시작 |
| **세션 진행 중 (Update)** | 타이머 감소, 크리티컬 감지, 종료 조건 체크 |
| **세션 종료 (Remaining ≤ 0)** | `EndSession()` 호출 → `ClearWorld()` → `SessionEndPopup.Show()` |
| **보상 스냅샷** | `EndSession()`에서 `RewardManager.GetAll()` 호출하여 스냅샷 생성 |
| **재시작 (팝업에서)** | `RestartSessionFromPopup()` 호출 → `OreSpawner.StartFresh()` → `StartSession()` |

#### 참조 관계

- **참조하는 것**: `RewardManager.Instance` (Awake), `OreSpawner` (FindObjectOfType)
- **참조되는 것**: `TimeHUD` (이벤트 구독), `SessionEndPopup` (Show 시 참조 전달)

---

### 2.2 OreSpawner

**파일 경로:** `Assets/Systems/OreSpawner.cs`  
**GameObject 경로:** `PF_Mining/_GamePlay/OreManager`  
**네임스페이스:** `Pulseforge.Systems`

#### 역할 (게임 플레이 루프 관점)

- **초기 스폰**: `Awake()`에서 `SpawnInitial()` 호출하여 `initialCount` 개 광석 생성
- **리스폰 관리**: `Update()`에서 `baseTargetCount` 유지, 부족 시 배치 스폰
- **세션 연동**: `PauseAndClear()` (세션 종료), `StartFresh()` (세션 재시작) API 제공
- **배치 시스템**: 그리드 스냅, 최소 거리 체크, 배치 스폰 지원

#### 세션 시작/종료, 보상 지급, 씬 전환에서의 타이밍

| 타이밍 | 동작 |
|--------|------|
| **씬 시작 (Awake)** | `SpawnInitial()` 호출 → 초기 광석 생성 |
| **세션 진행 중 (Update)** | `HandleRespawn()` → 부족한 광석 자동 보충 |
| **세션 종료** | `SessionController.ClearWorld()` → `PauseAndClear()` 호출 → 모든 광석 삭제, 리스폰 정지 |
| **세션 재시작** | `SessionController.RestartSessionFromPopup()` → `StartFresh()` → `ResetSpawner()` → 초기 스폰 |

#### 참조 관계

- **참조하는 것**: 없음 (독립적)
- **참조되는 것**: `SessionController` (FindObjectOfType으로 찾음)

---

### 2.3 RewardManager

**파일 경로:** `Assets/Systems/RewardManager.cs`  
**GameObject 경로:** 씬 최상위 (DontDestroyOnLoad)  
**네임스페이스:** `Pulseforge.Systems`

#### 역할 (게임 플레이 루프 관점)

- **자원 누적 관리**: `RewardType` enum 기반 자원 타입별 보유량 관리
- **씬 전환 시 유지**: `DontDestroyOnLoad`로 씬 전환 시에도 보유량 유지
- **이벤트 제공**: `OnChanged` (UnityEvent), `OnResourceChanged` (C# event) 이중 제공
- **HUD 호환 API**: `GetAll()` 메서드로 `ResourceHUD`와 호환

#### 세션 시작/종료, 보상 지급, 씬 전환에서의 타이밍

| 타이밍 | 동작 |
|--------|------|
| **씬 시작 (Awake)** | 싱글톤 초기화, `DontDestroyOnLoad` 설정 |
| **광석 파괴 시** | `Ore` 스크립트에서 `RewardManager.Instance.Add()` 호출 |
| **세션 종료** | `SessionController.EndSession()`에서 `GetAll()` 호출하여 스냅샷 생성 |
| **씬 전환** | `DontDestroyOnLoad`로 인스턴스 유지, PF_Outpost ↔ PF_Mining 전환 시에도 보유량 유지 |

#### 참조 관계

- **참조하는 것**: 없음 (독립적)
- **참조되는 것**: 
  - `Ore` (광석 파괴 시 `Add()` 호출)
  - `SessionController` (스냅샷 생성 시 `GetAll()` 호출)
  - `ResourceHUD` (주기적 폴링으로 `GetAll()` 호출)
  - `TopBar` (Inspector 참조)

---

### 2.4 OutpostController

**파일 경로:** `Assets/Systems/OutpostController.cs`  
**GameObject 경로:** `PF_Outpost/_Systems/OutpostController` (추정)  
**네임스페이스:** 없음 (전역)

#### 역할 (게임 플레이 루프 관점)

- **씬 전환 제어**: "Start Mining" 버튼 클릭 시 `PF_Mining` 씬으로 전환
- **버튼 이벤트 관리**: `Awake()`에서 버튼 리스너 등록, `OnDestroy()`에서 해제

#### 세션 시작/종료, 보상 지급, 씬 전환에서의 타이밍

| 타이밍 | 동작 |
|--------|------|
| **씬 시작 (Awake)** | `startMiningButton.onClick.AddListener()` 등록 |
| **버튼 클릭** | `OnClickStartMining()` 호출 → `SceneManager.LoadScene("PF_Mining")` |
| **씬 전환** | PF_Outpost → PF_Mining 전환 |

#### 참조 관계

- **참조하는 것**: `SceneManager` (씬 전환)
- **참조되는 것**: 없음 (독립적)

---

### 2.5 TopBar 및 HUD 관련 스크립트

#### TimeHUD

**파일 경로:** `Assets/UI/TimeHud.cs`  
**GameObject 경로:** `PF_Mining/_UI/Canvas/TopBar/TimeText` (TimeHUD 컴포넌트)  
**네임스페이스:** 없음 (전역)

**역할:**
- 세션 남은 시간 표시 (`SessionController.OnTimeChanged` 이벤트 구독)
- 크리티컬 시간 구간 시 색상 변경 및 깜빡임 (`OnCritical` 이벤트 구독)
- 세션 시작/종료 시 자동 표시/숨김 (`OnSessionStart`, `OnSessionEnd` 이벤트 구독)

**타이밍:**
- `Start()`: `SessionController` 찾기, 이벤트 구독
- `OnDestroy()`: 이벤트 해제
- `Update()`: 크리티컬 구간 깜빡임 애니메이션

#### ResourceHUD

**파일 경로:** `Assets/UI/ResourceHD.cs`  
**GameObject 경로:** `PF_Mining/_UI/Canvas/TopBar/ResourceText` (ResourceHUD 컴포넌트)  
**네임스페이스:** 없음 (전역)

**역할:**
- 보상 자원 실시간 표시 (`RewardManager.GetAll()` 주기적 폴링)
- 단일/전체 타입 표시 모드 지원

**타이밍:**
- `Awake()`: `RewardManager` 찾기
- `OnEnable()`: `RefreshLoop()` 코루틴 시작
- `OnDisable()`: 코루틴 정지
- `RefreshLoop()`: `refreshInterval` 간격으로 `RefreshImmediate()` 호출

---

## 3. 씬 전환 흐름

### 3.1 PF_Outpost → PF_Mining 전환

```
1. 사용자가 "Start Mining" 버튼 클릭
   ↓
2. OutpostController.OnClickStartMining() 호출
   ↓
3. SceneManager.LoadScene("PF_Mining") 실행
   ↓
4. PF_Mining 씬 로드
   ↓
5. RewardManager (DontDestroyOnLoad) 유지됨
   ↓
6. PF_Mining 씬 초기화:
   - AppBoot.Awake() → FPS/VSync 설정
   - RewardManager.Awake() → 싱글톤 체크 (이미 존재하면 유지)
   - OreSpawner.Awake() → SpawnInitial() 호출 (초기 광석 생성)
   - SessionController.Awake() → RewardManager, OreSpawner 참조 찾기
   - SessionController.Start() → StartSession() 자동 호출
   - TimeHUD.Start() → SessionController 이벤트 구독
   - ResourceHUD.OnEnable() → RefreshLoop() 코루틴 시작
```

### 3.2 PF_Mining 세션 종료 → SessionEndPopup 표시

```
1. SessionController.Update()에서 Remaining ≤ 0 감지
   ↓
2. SessionController.EndSession() 호출
   ↓
3. 보상 스냅샷 생성: RewardManager.GetAll()
   ↓
4. SessionController.ClearWorld() 호출
   ↓
5. OreSpawner.PauseAndClear() 호출
   - 모든 광석 삭제
   - 리스폰 정지 (_isActive = false)
   ↓
6. SessionEndPopup.Show(snapshot, this) 호출
   - 팝업 표시
   - 보상 요약 텍스트 설정
   - 버튼 리스너 등록
```

### 3.3 SessionEndPopup → "Mine Again" 버튼 클릭

```
1. 사용자가 "Mine Again" 버튼 클릭
   ↓
2. SessionEndPopup.OnClickMineAgain() 호출
   - (참고: 실제 버튼은 SessionController.RestartSessionFromPopup()를 직접 호출)
   ↓
3. SessionController.RestartSessionFromPopup() 호출
   ↓
4. OreSpawner.StartFresh() 호출
   - _isActive = true
   - ResetSpawner() 호출
     → 모든 광석 삭제
     → 타이머 리셋
     → SpawnInitial() 호출 (초기 광석 생성)
   ↓
5. SessionController.StartSession() 호출
   - 타이머 재시작
   - DrillCursor 활성화
   - OnSessionStart 이벤트 발생
   ↓
6. 세션 재개
```

### 3.4 SessionEndPopup → "Upgrade" 버튼 클릭 (Outpost로 돌아가기)

```
1. 사용자가 "Upgrade" 버튼 클릭
   ↓
2. SessionEndPopup.OnClickUpgrade() 호출
   ↓
3. SceneManager.LoadScene("PF_Outpost") 실행
   ↓
4. PF_Outpost 씬 로드
   ↓
5. RewardManager (DontDestroyOnLoad) 유지됨
   ↓
6. OutpostController.Awake() → 버튼 리스너 등록
```

### 3.5 RewardManager 유지 메커니즘

**싱글톤 패턴:**
- `Awake()`에서 `_instance` 체크
- 이미 존재하면 `Destroy(gameObject)` (중복 방지)
- 없으면 `_instance = this` 설정

**씬 전환 유지:**
- `DontDestroyOnLoad(gameObject)` 호출
- `transform.SetParent(null)` 호출 (루트로 이동)

**참조 방식:**
- **PF_Mining에서**: `RewardManager.Instance` (정적 프로퍼티)
- **ResourceHUD에서**: `FindAnyObjectByType<RewardManager>()` (Awake에서)
- **TopBar Prefab**: Inspector에서 직접 참조 연결

---

## 4. 생명주기 관점 분석

### 4.1 Awake/Start/OnEnable 메서드 사용 현황

#### SessionController

| 메서드 | 동작 | 의존성 |
|--------|------|--------|
| **Awake()** | `RewardManager.Instance` 참조, `OreSpawner` 찾기 (FindObjectOfType) | RewardManager가 먼저 존재해야 함 (DontDestroyOnLoad이므로 보통 존재) |
| **Start()** | `StartSession()` 자동 호출 | Awake 완료 후 실행, OreSpawner 참조 필요 |

**순서 의존성:**
- `RewardManager.Awake()`가 먼저 실행되어 싱글톤 초기화되어야 함
- `OreSpawner`는 FindObjectOfType으로 찾으므로 씬에 존재하기만 하면 됨

---

#### OreSpawner

| 메서드 | 동작 | 의존성 |
|--------|------|--------|
| **Awake()** | `baseTargetCount` 초기화, `SpawnInitial()` 호출 | 독립적 (의존성 없음) |

**순서 의존성:**
- 없음 (독립적)

---

#### RewardManager

| 메서드 | 동작 | 의존성 |
|--------|------|--------|
| **Awake()** | 싱글톤 초기화, `DontDestroyOnLoad` 설정 | 없음 (최우선 실행 필요) |

**순서 의존성:**
- 다른 스크립트들이 `RewardManager.Instance`를 참조하므로, 가장 먼저 초기화되어야 함
- `DontDestroyOnLoad`이므로 첫 씬에서만 생성되고 이후 유지됨

---

#### OutpostController

| 메서드 | 동작 | 의존성 |
|--------|------|--------|
| **Awake()** | `startMiningButton.onClick.AddListener()` 등록 | 버튼이 씬에 존재해야 함 |
| **OnDestroy()** | 버튼 리스너 해제 | 없음 |

**순서 의존성:**
- 버튼이 씬에 존재하기만 하면 됨 (Inspector 참조)

---

#### TimeHUD

| 메서드 | 동작 | 의존성 |
|--------|------|--------|
| **Start()** | `SessionController` 찾기, 이벤트 구독 | `SessionController`가 씬에 존재해야 함 |
| **OnDestroy()** | 이벤트 해제 | 없음 |

**순서 의존성:**
- `SessionController.Start()`가 실행된 후 이벤트 구독해야 함
- 현재는 `Start()`에서 구독하므로 안전함

---

#### ResourceHUD

| 메서드 | 동작 | 의존성 |
|--------|------|--------|
| **Awake()** | `RewardManager` 찾기 | `RewardManager`가 존재해야 함 (DontDestroyOnLoad이므로 보통 존재) |
| **OnEnable()** | `RefreshLoop()` 코루틴 시작 | `RewardManager` 참조 필요 |
| **OnDisable()** | 코루틴 정지 | 없음 |

**순서 의존성:**
- `RewardManager`가 먼저 존재해야 함 (DontDestroyOnLoad이므로 보통 존재)

---

### 4.2 순서 의존 관계 요약

```
초기화 순서 (예상):

1. RewardManager.Awake() ⭐ (최우선)
   - 싱글톤 초기화
   - DontDestroyOnLoad 설정

2. OreSpawner.Awake()
   - 초기 광석 스폰 (독립적)

3. SessionController.Awake()
   - RewardManager.Instance 참조
   - OreSpawner 찾기

4. ResourceHUD.Awake()
   - RewardManager 찾기

5. TimeHUD.Start()
   - SessionController 이벤트 구독

6. SessionController.Start()
   - StartSession() 호출

7. ResourceHUD.OnEnable()
   - RefreshLoop() 코루틴 시작
```

**의존성 체인:**
- `RewardManager` ← `SessionController`, `ResourceHUD`
- `SessionController` ← `TimeHUD`
- `OreSpawner` ← `SessionController` (참조만, 초기화 순서는 독립적)

---

### 4.3 ExpManager 추가 시 초기화 위치 후보

#### 후보 1: RewardManager와 동일한 패턴 (DontDestroyOnLoad 싱글톤)

**위치:** 씬 최상위, `DontDestroyOnLoad` 설정

**이유:**
- 레벨/경험치는 씬 전환 시에도 유지되어야 함
- `RewardManager`와 동일한 패턴으로 일관성 유지
- 다른 매니저들이 `ExpManager.Instance`로 참조 가능

**초기화 타이밍:**
- `Awake()`에서 싱글톤 초기화
- `RewardManager`와 동일하게 최우선 실행

**주의사항:**
- `RewardManager`와 동일한 씬에 배치 시 초기화 순서는 Unity가 결정 (보통 씬 순서대로)
- 필요 시 `[DefaultExecutionOrder]` 속성으로 순서 제어 가능

---

#### 후보 2: SessionController와 함께 _Systems 하위

**위치:** `PF_Mining/_Systems/ExpManager`

**이유:**
- 세션 관련 데이터이므로 `SessionController`와 함께 관리
- 씬 전환 시 유지하려면 `DontDestroyOnLoad` 추가 필요

**초기화 타이밍:**
- `Awake()`에서 싱글톤 초기화
- `SessionController.Awake()`에서 `ExpManager.Instance` 참조 가능

**주의사항:**
- `DontDestroyOnLoad` 설정 시 부모에서 분리 필요 (`transform.SetParent(null)`)

---

#### 후보 3: 별도 매니저 씬 또는 부트 씬에서 초기화

**위치:** `Pulseforge_Boot` 씬 또는 별도 초기화 씬

**이유:**
- 모든 매니저를 한 곳에서 초기화하여 순서 제어 용이
- 게임 시작 시 한 번만 초기화

**초기화 타이밍:**
- 부트 씬에서 `Awake()` 실행
- 이후 씬 전환 시 유지

**주의사항:**
- 현재 프로젝트 구조상 부트 씬이 있지만, 실제 사용 여부 확인 필요
- `RewardManager`도 부트 씬에서 초기화하도록 변경 필요할 수 있음

---

### 4.4 추천 방안

**추천: 후보 1 (RewardManager와 동일한 패턴)**

**이유:**
1. **일관성**: `RewardManager`와 동일한 패턴으로 프로젝트 구조 일관성 유지
2. **단순성**: 별도 씬이나 복잡한 초기화 로직 불필요
3. **안정성**: `DontDestroyOnLoad` 싱글톤 패턴은 검증된 방식
4. **참조 용이성**: `ExpManager.Instance`로 어디서든 접근 가능

**구현 시 주의사항:**
- `[DefaultExecutionOrder(-100)]` 속성으로 `RewardManager`보다 먼저 실행되도록 설정 가능 (필요 시)
- `RewardManager`와 동일하게 `Awake()`에서 싱글톤 초기화
- 씬 전환 시 유지되므로 PF_Outpost에서도 접근 가능

**초기화 순서 보장:**
```csharp
[DefaultExecutionOrder(-100)]  // RewardManager보다 먼저 실행
public class ExpManager : MonoBehaviour
{
    private static ExpManager _instance;
    public static ExpManager Instance => _instance;
    
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
}
```

---

## 부록: 참조 관계 다이어그램

```
┌─────────────────┐
│ RewardManager   │ (DontDestroyOnLoad 싱글톤)
│ (씬 최상위)      │
└────────┬────────┘
         │ Instance 참조
         ├─────────────────┐
         │                 │
         ▼                 ▼
┌─────────────────┐  ┌──────────────┐
│SessionController│  │ ResourceHUD   │
│ (_Systems)      │  │ (TopBar)     │
└────────┬────────┘  └──────────────┘
         │
         │ FindObjectOfType
         ▼
┌─────────────────┐
│ OreSpawner      │
│ (_GamePlay)     │
└─────────────────┘
         │
         │ Instantiate
         ▼
┌─────────────────┐
│ Ore (광석)       │
│ (Runtime 생성)   │
└────────┬────────┘
         │
         │ RewardManager.Instance.Add()
         ▼
┌─────────────────┐
│ RewardManager   │
└─────────────────┘
```

---

**문서 버전:** 1.0  
**마지막 업데이트:** 2025년













