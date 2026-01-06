# PF_Mining 씬 및 시스템 상태 정리 문서

## 1) 씬/구조 개요

### 실제 사용되는 GameObject 구조

#### 활성화된 핵심 오브젝트

1. **`_Systems` (루트)**
   - 위치: 씬 최상위
   - 자식 오브젝트들:
     - `AppBoot`: 앱 부팅 설정 (FPS, VSync 제어)
     - `SessionController`: 세션 타이머 관리
     - `OreManager`: `OreSpawner` 컴포넌트 포함

2. **`_GamePlay` (루트)**
   - 위치: 씬 최상위
   - 자식 오브젝트들:
     - `OreManager`: 광석 스폰 관리 (`OreSpawner` 컴포넌트)
     - `DrillCursor` (Prefab Instance): 드릴 커서 프리팹 인스턴스
       - 프리팹 GUID: `bc44e8fdf2ac30e45846793bace640ae`
       - 위치: `_GamePlay` 하위
       - 실제 위치: `(-0.461, 0.078, 0.006)`

3. **`_UI` (루트)**
   - 위치: 씬 최상위
   - 자식 오브젝트들:
     - `Canvas`: UI 캔버스
       - `TopBar` (Prefab Instance): 상단 바 (TimeText, ResourceHUD 포함)
       - `SessionEndPopup`: 세션 종료 팝업
     - `EventSystem`: UI 이벤트 처리

4. **`RewardManager`**
   - 위치: 씬 최상위 (`DontDestroyOnLoad`)
   - 싱글톤 매니저, 씬 전환 시에도 유지

5. **`Main Camera`**
   - Orthographic 카메라, Size: 5

### 비활성화된 레거시 오브젝트

다음 오브젝트들은 `m_IsActive: 0`으로 설정되어 비활성화 상태입니다:

1. **`Legacy_OreRoot`** (GameObject ID: 42209187)
   - 위치: `_GamePlay` 하위
   - 포함 컴포넌트:
     - `CoreOrePulse`: 리듬 비주얼 피드백 (비활성화)
     - `TapPad`: 탭 패드 (비활성화)

2. **`Legacy_RhythmSystem`** (GameObject ID: 1128913638)
   - 위치: `_Systems` 하위
   - 포함 컴포넌트:
     - `RhythmConductor`: 리듬 컨덕터 (BPM 120)
     - `AudioSource`: 오디오 소스
   - **문제**: `Legacy_OreRoot`의 `CoreOrePulse`가 여전히 이 `Legacy_RhythmSystem`의 `OnBeat` 이벤트에 연결되어 있음 (참조는 남아있지만 오브젝트가 비활성화됨)

3. **`Legacy_Mining`** (GameObject ID: 1625604984)
   - 위치: 씬 최상위
   - 포함 컴포넌트:
     - `MiningController`: 리듬 타이밍 판정 컨트롤러 (비활성화)
     - `PlayerInput`: 입력 시스템 (비활성화)
   - **문제**: `Legacy_RhythmSystem`을 참조하도록 설정되어 있지만 두 오브젝트 모두 비활성화됨

4. **`Legacy_BeatLogger`** (GameObject ID: 2048143736)
   - 위치: `_Systems` 하위
   - 디버그 로거 (비활성화)

### Prefab 인스턴스

1. **`DrillCursor`** (활성화)
   - 프리팹: `Assets/Prefabs/DrillCursor.prefab`
   - GUID: `bc44e8fdf2ac30e45846793bace640ae`
   - 위치 오버라이드: `(-0.461, 0.078, 0.006)`
   - 컴포넌트: `DrillCursor` (활성화)

2. **`TopBar`** (활성화)
   - 프리팹: `Assets/Prefabs/TopBar.prefab`
   - GUID: `69b8e3de8ccf5374caab586fb27b1e02`
   - 오버라이드 사항:
     - Pivot: `(0.5, 1)` - 상단 앵커
     - Anchors: `(0, 1) ~ (1, 1)` - 상단 전체 너비
     - Size: `(0, 60.22)` - 높이 60.22
     - 내부 참조 연결:
       - `resourceText`: 씬 내부의 TMP_Text에 연결
       - `rewardManager`: 씬 내부 `RewardManager`에 연결
       - `session`: `SessionController`에 연결

### UI 구조

#### Canvas 구조
```
Canvas (Screen Space - Overlay)
├── TopBar (Prefab Instance)
│   ├── TimeText (TimeHUD 연결)
│   └── ResourceText (ResourceHUD 연결)
└── SessionEndPopup
    └── Panel
        ├── Title ("Time Out")
        ├── SummaryText (보상 요약)
        └── Buttons
            ├── MineAgainButton → SessionController.RestartSessionFromPopup()
            └── UpgradeButton (미구현)
```

#### UI 컴포넌트 매핑

- **TimeHUD**: `TimeText`에 연결되어 `SessionController.OnTimeChanged` 이벤트를 구독
- **ResourceHUD**: `ResourceText`에 연결되어 `RewardManager`를 주기적으로 폴링
- **SessionEndPopup**: 세션 종료 시 보상 요약 표시 및 재시작 버튼 제공

---

## 2) 구현 완료 항목

### DrillCursor (완료)

**기능 요약:**
- 마우스/터치 입력을 따라다니는 드릴 커서
- 일정 간격(`swingInterval`)마다 주변 광석에 자동 공격
- 반경 감지로 다중 광석 동시 공격 지원
- 스프라이트 기반 또는 고정 반경 선택 가능

**주요 구현 사항:**
- `Rigidbody2D` 기반 부드러운 추적 (`followLerp`)
- `Physics2D.OverlapCircleNonAlloc`로 성능 최적화된 감지
- 레이어 마스크 필터링 지원
- Gizmo로 디버그 시각화

**현재 작동 상태:** ✅ 정상 작동

### Ore (완료)

**기능 요약:**
- HP 시스템 및 데미지 처리
- HP 바 표시 (첫 피격 시 표시 옵션)
- 피격 시 Wobble 애니메이션
- 파괴 시 보상 지급 (`RewardManager` 연동)

**주요 구현 사항:**
- 동적 HP 바 생성 (1픽셀 스프라이트 기반)
- 정렬 레이어/오더 자동 계산
- 파괴 시 `RewardManager.Instance.Add()` 호출

**현재 작동 상태:** ✅ 정상 작동

### OreSpawner (완료)

**기능 요약:**
- 초기 스폰 (`initialCount` 개)
- 리스폰 시스템 (목표 개수 유지)
- 사각형/원형 스폰 영역 선택
- 그리드 스냅 배치 옵션
- 최소 거리 겹침 방지
- 세션 연동 (일시정지/재시작)

**주요 구현 사항:**
- `SpawnInitial()`: 게임 시작 시 초기 생성
- `HandleRespawn()`: 부족한 광석 자동 보충
- `TryGetFreePosition()`: 최대 시도 횟수 내 위치 탐색
- `PauseAndClear()`, `StartFresh()`: 세션 연동 API

**현재 작동 상태:** ✅ 정상 작동 (일부 이슈 있음, 3번 섹션 참조)

### SessionController (완료)

**기능 요약:**
- 세션 타이머 관리 (`baseDurationSec` 초)
- 크리티컬 시간 임계값 (`criticalThreshold`)
- 세션 시작/종료 이벤트
- `OreSpawner`와 연동하여 세션 종료 시 광석 정리
- `SessionEndPopup` 연동

**주요 구현 사항:**
- `Start()`에서 자동 시작
- `Update()`에서 타이머 감소 및 종료 감지
- `EndSession()`: 보상 스냅샷 생성 후 팝업 표시
- `RestartSessionFromPopup()`: 팝업에서 호출되는 재시작 함수
- `ClearWorld()`: `OreSpawner.PauseAndClear()` 호출

**현재 작동 상태:** ✅ 정상 작동

### RewardManager (완료)

**기능 요약:**
- 싱글톤 패턴 (씬 전환 시 유지)
- 자원 타입별 누적 관리 (`RewardType` enum)
- UnityEvent 및 C# event 이벤트 제공
- `ResourceHUD` 호환 API

**주요 구현 사항:**
- `DontDestroyOnLoad`로 씬 전환 시 유지
- `Get()`, `Set()`, `Add()` API
- `GetAll()`: 전체 보유량 딕셔너리 반환
- 이벤트 이중 제공 (UnityEvent + C# event)

**현재 작동 상태:** ✅ 정상 작동

### UI (완료)

#### TimeText (TimeHUD)

**기능 요약:**
- 세션 남은 시간 표시
- 크리티컬 시간 구간 시 색상 변경 및 깜빡임
- 세션 시작/종료 시 자동 표시/숨김

**주요 구현 사항:**
- `SessionController` 이벤트 구독 (`OnTimeChanged`, `OnCritical`)
- 크리티컬 구간 깜빡임 애니메이션
- 형식화된 시간 표시 (`timeFormat`)

**현재 작동 상태:** ✅ 정상 작동

#### ResourceHUD

**기능 요약:**
- 보상 자원 실시간 표시
- 단일/전체 타입 표시 모드
- 주기적 폴링으로 업데이트

**주요 구현 사항:**
- `RefreshLoop()` 코루틴으로 주기적 갱신 (`refreshInterval`)
- `RewardManager.GetAll()` 사용
- 커스텀 포맷 문자열 지원

**현재 작동 상태:** ✅ 정상 작동

#### SessionEndPopup

**기능 요약:**
- 세션 종료 시 보상 요약 표시
- "Mine Again" 버튼으로 세션 재시작
- "Upgrade" 버튼 (현재 미구현)

**주요 구현 사항:**
- `CanvasGroup`로 표시/숨김 제어
- `SessionController.RestartSessionFromPopup()` 호출
- 보상 딕셔너리를 텍스트로 변환

**현재 작동 상태:** ✅ 정상 작동 (일부 이슈 있음, 3번 섹션 참조)

---

## 3) 현재 발견된 이슈

### 3.1 스폰/리스폰 이슈

#### 문제 1: 세션 재시작 시 리스폰 타이머 초기화 누락 가능성

**위치:** `OreSpawner.StartFresh()` → `ResetSpawner()`

**코드 분석:**
```csharp
public void ResetSpawner()
{
    // ... 광석 삭제 ...
    _respawnTimer = 0f;  // ← 즉시 리스폰 가능하게 설정
    // ... 초기 스폰 ...
}
```

**문제:**
- `_respawnTimer = 0f`로 설정하면 `Update()`에서 즉시 리스폰 시도
- 하지만 `SpawnInitial()`이 비동기적으로 광석을 생성하므로, 첫 프레임에서 `_spawned.Count`가 아직 목표 개수에 도달하지 않았을 수 있음
- 이 경우 즉시 리스폰 배치가 실행되어 초기 스폰과 겹칠 수 있음

**영향도:** 중간 (드물게 광석이 예상보다 많이 생성될 수 있음)

#### 문제 2: 배치 스폰 시 목표 개수 초과 가능성

**위치:** `OreSpawner.HandleRespawn()`

**코드 분석:**
```csharp
int missing = TargetCount - _spawned.Count;
int desiredBatch = Random.Range(batchMin, batchMax + 1);
int batchSize = Mathf.Clamp(desiredBatch, 1, missing);
SpawnBatch(batchSize);
```

**문제:**
- `SpawnBatch()` 내부에서 위치 탐색 실패 시 중단되지만, 성공한 개수만큼만 `_spawned`에 추가됨
- 하지만 리스폰 타이머가 계속 감소하므로, 다음 프레임에서 다시 시도할 때 이미 목표 개수에 도달했는지 체크하기 전에 배치가 실행될 수 있음

**영향도:** 낮음 (대부분의 경우 정상 동작)

### 3.2 배치/그리드 이슈

#### 문제 3: 그리드 스냅과 최소 거리 체크의 불일치

**위치:** `OreSpawner.TryGetFreePosition()`

**코드 분석:**
```csharp
if (useGrid && gridCellSize > 0.0001f)
{
    candidate.x = Mathf.Round(candidate.x / gridCellSize) * gridCellSize;
    candidate.y = Mathf.Round(candidate.y / gridCellSize) * gridCellSize;
}
// 그 후 최소 거리 체크
```

**문제:**
- 그리드 스냅 후에도 최소 거리 체크가 `minDistance` (월드 단위)로 이루어짐
- `gridCellSize`와 `minDistance`가 일치하지 않으면, 그리드 셀보다 작은 `minDistance`에서는 불필요한 거부가 발생할 수 있음
- 예: `gridCellSize = 0.5`, `minDistance = 0.6`인 경우, 인접 그리드 셀도 거부됨

**영향도:** 중간 (배치 효율성 저하)

### 3.3 타이머 이슈

#### 문제 4: 세션 종료 후 팝업에서 재시작 시 타이머 중복 시작 가능성

**위치:** `SessionEndPopup.OnClickMineAgain()`

**코드 분석:**
```csharp
private void OnClickMineAgain()
{
    HideImmediate();
    _controller.StartSession();  // ← 여기서 세션 시작
}
```

**문제:**
- `SessionController.RestartSessionFromPopup()`에서 이미 `StartSession()`을 호출함 (124-140줄)
- 하지만 `MineAgainButton`의 OnClick 이벤트는 `SessionController.RestartSessionFromPopup()`를 직접 호출하도록 설정됨 (씬 파일 999줄)
- **실제 문제 없음**: 버튼이 `RestartSessionFromPopup()`를 호출하므로 중복 없음

**영향도:** 없음 (코드 재검토 결과 문제 없음)

#### 문제 5: SessionEndPopup.OnClickMineAgain()의 로직 불일치

**위치:** `SessionEndPopup.cs` 112-122줄

**코드 분석:**
```csharp
private void OnClickMineAgain()
{
    if (_controller == null)
        return;
    
    HideImmediate();
    _controller.StartSession();  // ← 단순히 StartSession()만 호출
}
```

**문제:**
- 실제 버튼은 `RestartSessionFromPopup()`를 호출하도록 설정되어 있음
- 하지만 `SessionEndPopup` 내부의 `OnClickMineAgain()` 메서드는 `StartSession()`만 호출
- **실제 영향 없음**: 버튼이 다른 함수를 직접 호출하므로 이 메서드는 사용되지 않음
- **코드 일관성 문제**: `OnClickMineAgain()`가 실제로는 `RestartSessionFromPopup()`를 호출해야 함

**영향도:** 낮음 (현재 동작에는 문제 없으나 코드 일관성 문제)

### 3.4 팝업 이슈

#### 문제 6: 팝업 표시 시 SessionController 참조 보존

**위치:** `SessionController.EndSession()` → `SessionEndPopup.Show()`

**코드 분석:**
```csharp
if (popup != null)
{
    popup.Show(snapshot, this);  // ← this를 전달
}
```

**문제:**
- `SessionEndPopup.Show()`에서 `_controller = session;`으로 참조 저장 (62줄)
- 세션 종료 후 `SessionController`가 비활성화되거나 파괴되면 참조가 끊김
- 하지만 `SessionController`는 씬 내에서 영구 오브젝트이므로 실제 문제는 없음

**영향도:** 없음 (현재 구조상 문제 없음)

### 3.5 오버라이드 이슈

#### 문제 7: Prefab Override로 인한 원본-씬 불일치

**TopBar Prefab 인스턴스 오버라이드:**

씬 파일에서 확인된 오버라이드:
- Pivot, Anchors, Size 등 레이아웃 속성 변경
- 내부 참조 연결 (resourceText, rewardManager, session)

**문제:**
- 프리팹 원본이 변경되면 오버라이드가 유지되지만, 새로운 필드 추가 시 씬 인스턴스에 반영되지 않을 수 있음
- Prefab 연결 상태 확인 필요

**영향도:** 중간 (프리팹 업데이트 시 주의 필요)

#### 문제 8: DrillCursor Prefab 위치 오버라이드

**위치 오버라이드:**
- 프리팹 원본 위치와 씬 인스턴스 위치가 다름
- `(-0.461, 0.078, 0.006)`

**문제:**
- 위치 오버라이드는 정상적인 사용이지만, 프리팹 원본 위치 변경 시 씬에 반영되지 않음
- 의도적인 오버라이드인지 확인 필요

**영향도:** 낮음 (정상적인 사용일 가능성 높음)

### 3.6 참조 끊김 이슈

#### 문제 9: Legacy 오브젝트 간 참조가 남아있음

**Legacy_RhythmSystem → Legacy_OreRoot 연결:**

씬 파일 1295-1309줄:
```yaml
OnBeat:
  m_PersistentCalls:
    m_Calls:
    - m_Target: {fileID: 42209189}  # Legacy_OreRoot의 CoreOrePulse
      m_TargetAssemblyTypeName: Pulseforge.Visuals.CoreOrePulse
      m_MethodName: OnBeat
```

**문제:**
- 비활성화된 오브젝트 간 참조가 남아있음
- 실제 동작에는 영향 없지만, 씬 파일 크기 증가 및 혼란 유발
- 정리 시 참조도 함께 제거해야 함

**영향도:** 낮음 (기능적 문제 없음)

---

## 4) 우리가 기대하는 동작(사양)

### 4.1 Spawner 유지 정책

**목표 개수 유지:**
- `baseTargetCount` 개의 광석을 항상 유지
- 광석이 파괴되면 `respawnDelay` 간격으로 보충
- 한 번에 `batchMin` ~ `batchMax` 개씩 배치 생성

**초기 스폰:**
- 게임 시작 시 `initialCount` 개 생성
- `baseTargetCount <= 0`이면 `initialCount`를 `baseTargetCount`로 설정

**세션 연동:**
- 세션 종료 시: `PauseAndClear()` 호출 → 모든 광석 삭제, 리스폰 정지
- 세션 재시작 시: `StartFresh()` 호출 → 리스폰 활성화, 초기 스폰 실행

### 4.2 Grid/Hybrid 배치 기준

**배치 모드:**
- `useGrid = true`: 그리드 스냅 활성화
  - `gridCellSize` 단위로 위치 반올림
  - 타일 느낌의 정렬된 배치
- `useGrid = false`: 자유 배치
  - 랜덤 위치 생성

**최소 거리:**
- 모든 광석 간 `minDistance` 이상 거리 유지
- 위치 탐색 실패 시 최대 `maxPlacementAttempts` 번 시도
- 실패 시 해당 배치는 건너뜀

**Hybrid 배치:**
- 그리드 스냅 후에도 최소 거리 체크 수행
- 이상적으로는 `gridCellSize`와 `minDistance`를 조화롭게 설정

### 4.3 세션 시스템

**세션 플로우:**
1. 씬 시작 → `SessionController.Start()` → `StartSession()` 자동 호출
2. 타이머 감소 (`baseDurationSec` → 0)
3. `criticalThreshold` 도달 시 `OnCritical` 이벤트 발생
4. 타이머 0 도달 → `EndSession()` 호출
   - `OreSpawner.PauseAndClear()` 호출
   - 보상 스냅샷 생성
   - `SessionEndPopup.Show()` 호출
5. 팝업에서 "Mine Again" 클릭 → `RestartSessionFromPopup()` 호출
   - `OreSpawner.StartFresh()` 호출
   - `StartSession()` 호출하여 타이머 재시작

**크리티컬 시간:**
- 남은 시간이 `criticalThreshold` 이하일 때 한 번만 `OnCritical` 이벤트 발생
- UI에서 색상 변경 및 깜빡임 표시

### 4.4 UI 동작

**TimeText (TimeHUD):**
- 세션 진행 중: 남은 시간 표시 (예: "12.5s")
- 크리티컬 구간: 색상 변경 및 깜빡임
- 세션 종료: 자동 숨김

**ResourceHUD:**
- 실시간 자원 표시 (예: "Crystal: 12 | Gold: 5 | Shard: 8")
- `refreshInterval` 간격으로 주기적 갱신
- `showAllTypes = true`이면 모든 타입 표시

**SessionEndPopup:**
- 세션 종료 시 자동 표시
- 보상 요약 텍스트 표시
- "Mine Again" 버튼: 세션 재시작
- "Upgrade" 버튼: 향후 구현 예정

---

## 5) 인스펙터 파라미터 정리

### OreSpawner

#### Spawn Settings
- **orePrefab** (GameObject): 생성할 광석 프리팹
  - 현재 값: `OrePrefab.prefab` (GUID: `05322ff31e12e9b49870e05e15098773`)
- **initialCount** (int): 게임 시작 시 기본 생성 개수
  - 현재 값: `12`
- **radius** (float): 원형 반경 (레거시용, `useRectArea = false`일 때 사용)
  - 현재 값: `5.0`

#### Spawn Area (Rect)
- **useRectArea** (bool): 사각형 영역 사용 여부
  - 현재 값: `true`
- **areaSize** (Vector2): 사각형 스폰 영역 크기 (월드 단위)
  - 현재 값: `(5, 9)`
- **areaOffset** (Vector2): 스폰 영역 중심 오프셋 (Spawner 위치 기준)
  - 현재 값: `(0, 0)`

#### Placement / 간격 & 타일 느낌
- **minDistance** (float): 광석들 사이의 최소 거리 (월드 단위)
  - 현재 값: `0.6`
- **maxPlacementAttempts** (int): 겹치지 않는 위치 탐색 최대 시도 횟수
  - 현재 값: `30`
- **useGrid** (bool): 그리드 스냅 배치 여부
  - 현재 값: `true`
- **gridCellSize** (float): 그리드 셀 크기 (월드 단위)
  - 현재 값: `0.5`

#### Respawn / 리스폰 설정
- **baseTargetCount** (int): 항상 유지하려는 기본 광석 개수
  - 현재 값: `12`
- **respawnDelay** (float): 리스폰 배치 사이의 최소 딜레이 (초)
  - 현재 값: `0.3`
- **batchMin** (int): 리스폰 한 번에 생성할 최소 개수
  - 현재 값: `1`
- **batchMax** (int): 리스폰 한 번에 생성할 최대 개수
  - 현재 값: `3`

#### Upgrade Modifiers (아직 사용 X)
- **upgradeFlatBonus** (int): 업그레이드로 추가되는 고정 보너스 수량
  - 현재 값: `0`
- **upgradeMultiplier** (float): 업그레이드로 곱해지는 배수
  - 현재 값: `1.0`

#### Debug Time Ramp
- **timeRampEnabled** (bool): 시간 경과에 따른 목표 개수 증가 여부
  - 현재 값: `false`
- **addEveryNSeconds** (float): 몇 초마다 추가할지
  - 현재 값: `10.0`
- **addAmount** (int): 한 번에 추가할 개수
  - 현재 값: `5`
- **maxExtra** (int): 추가되는 최대 개수 (`baseTargetCount` 기준)
  - 현재 값: `50`

### SessionController

#### Session Time
- **baseDurationSec** (float, Min: 1): 세션 기본 지속 시간 (초)
  - 현재 값: `15.0`
- **criticalThreshold** (float, Min: 0): 크리티컬 시간 임계값 (초)
  - 현재 값: `3.0`

#### Scene Refs (Optional)
- **oreRootOverride** (Transform): 광석 루트 오버라이드 (미사용)
  - 현재 값: `null`
- **drillCursor** (Behaviour): DrillCursor 컴포넌트 참조
  - 현재 값: `DrillCursor` 컴포넌트 (Prefab Instance)

### DrillCursor

#### Movement
- **followLerp** (float): 마우스 추적 속도
  - 기본값: `14.0`

#### Mining
- **swingInterval** (float): 한 번 스윙 간격 (초)
  - 기본값: `0.18`
- **damagePerSwing** (float): 한 번 스윙 당 피해량
  - 기본값: `3.0`

#### Detection
- **radiusSource** (enum): 반경 소스 (Fixed / FromSprite)
  - 기본값: `Fixed`
- **fixedRadius** (float): 고정 반경 (월드 단위, `radiusSource = Fixed`일 때)
  - 기본값: `0.45`
- **spriteRadiusScale** (float): 스프라이트 반지름 배율 (`radiusSource = FromSprite`일 때)
  - 기본값: `1.0`
- **detectPadding** (float): 히트 여유 (월드 단위)
  - 기본값: `0.06`
- **oreMask** (LayerMask): Ore가 있는 레이어
  - 기본값: (비어있음)

#### Visual Sorting
- **cursorRenderer** (SpriteRenderer): 커서 렌더러
  - 기본값: 자동 탐지
- **cursorSortingOrder** (int): 커서 정렬 순서
  - 기본값: `100`

#### Debug
- **logHitCount** (bool): 히트 개수 로그 출력 여부
  - 기본값: `false`
- **gizmoRadiusColor** (Color): Gizmo 반경 색상
  - 기본값: `(1, 0.9, 0.2, 0.3)`

### Ore

#### HP
- **maxHp** (float): 최대 HP
  - 기본값: `30.0`
- **destroyOnBreak** (bool): 파괴 시 오브젝트 삭제 여부
  - 기본값: `true`

#### Reward
- **rewardType** (RewardType): 보상 타입
  - 기본값: `Crystal`
- **rewardAmount** (int): 보상 수량
  - 기본값: `1`

#### Hit Feedback
- **wobbleOnHit** (bool): 피격 시 흔들림 여부
  - 기본값: `true`
- **wobblePos** (float): 흔들림 위치 오프셋
  - 기본값: `0.035`
- **wobbleScale** (float): 흔들림 스케일 변화
  - 기본값: `0.06`
- **wobbleDuration** (float): 흔들림 지속 시간 (초)
  - 기본값: `0.08`

#### HP Bar (world-units)
- **showHpBar** (bool): HP 바 표시 여부
  - 기본값: `true`
- **revealBarOnFirstHit** (bool): 첫 피격 시 HP 바 표시 여부
  - 기본값: `true`
- **barAbove** (bool): HP 바를 광석 위에 표시 여부
  - 기본값: `false`
- **barYOffset** (float): HP 바 Y 오프셋
  - 기본값: `0.0`
- **barSize** (Vector2): HP 바 크기
  - 기본값: `(0.7, 0.08)`
- **barColor** (Color): HP 바 색상
  - 기본값: `(1, 0.12, 0.12, 1)` (빨간색)
- **barBgColor** (Color): HP 바 배경 색상
  - 기본값: `(0, 0, 0, 0.85)` (검은색)
- **barSortingOffsetBG** (int): 배경 정렬 오프셋
  - 기본값: `90`
- **barSortingOffsetFill** (int): 채움 정렬 오프셋
  - 기본값: `100`

#### Pixel-Perfect (optional)
- **minOnePixelThickness** (bool): 최소 1픽셀 두께 보장 여부
  - 기본값: `false`
- **referencePPU** (int): 참조 Pixels Per Unit
  - 기본값: `128`

### RewardManager

인스펙터에 표시되는 필드 없음 (싱글톤, 이벤트만 노출)

### TimeHUD

#### References
- **timeText** (TMP_Text): 시간 표시 텍스트
- **session** (SessionController): 세션 컨트롤러 참조

#### Visuals
- **normalColor** (Color): 일반 색상
  - 기본값: `White`
- **criticalColor** (Color): 크리티컬 색상
  - 기본값: `(1, 0.3, 0.3, 1)` (빨간색)
- **blinkOnCritical** (bool): 크리티컬 구간 깜빡임 여부
  - 기본값: `true`
- **blinkSpeed** (float): 깜빡임 속도
  - 기본값: `6.0`
- **timeFormat** (string): 시간 표시 형식
  - 기본값: `"{0:0.0}s"`

### ResourceHUD

#### Target
- **resourceText** (TMP_Text): 자원 표시 텍스트
- **rewardManager** (RewardManager): 보상 매니저 참조

#### Display Mode
- **showAllTypes** (bool): 모든 타입 표시 여부
  - 기본값: `false`
- **rewardType** (RewardType): 표시할 단일 타입 (`showAllTypes = false`일 때)
  - 기본값: `Crystal`

#### Formats
- **singleFormat** (string): 단일 타입 표시 형식
  - 기본값: `"{label} {value}"`
- **allJoinSeparator** (string): 전체 타입 표시 구분자
  - 기본값: `"  |  "`
- **labelOverride** (string): 라벨 오버라이드
  - 기본값: (비어있음)

#### Refresh
- **refreshInterval** (float): UI 갱신 간격 (초)
  - 기본값: `0.1`

### SessionEndPopup

#### Wiring
- **panel** (GameObject): 팝업 패널 게임오브젝트
- **summaryText** (TMP_Text): 요약 텍스트
- **mineAgainButton** (Button): "Mine Again" 버튼
- **upgradeButton** (Button): "Upgrade" 버튼

---

## 6) 재발 방지 체크리스트

### Prefab Override 관리

- [ ] **프리팹 업데이트 시 오버라이드 확인**
  - 프리팹 원본 수정 시 씬 인스턴스의 오버라이드 상태 확인
  - 오버라이드가 의도적인지 검토
  - 새로운 필드 추가 시 씬 인스턴스에 반영 여부 확인

- [ ] **오버라이드 문서화**
  - TopBar Prefab: 레이아웃 속성 오버라이드 (Pivot, Anchors, Size)
  - DrillCursor Prefab: 위치 오버라이드
  - 각 오버라이드의 이유 명시

- [ ] **Prefab 연결 상태 확인**
  - Unity 에디터에서 Prefab 연결 상태 (연결됨/연결 끊김) 확인
  - 연결 끊김 시 원본 프리팹과 동기화 필요

### 참조 끊김 방지

- [ ] **비활성 오브젝트 참조 정리**
  - Legacy 오브젝트 간 참조 제거 (`Legacy_RhythmSystem` → `Legacy_OreRoot`)
  - 더 이상 사용하지 않는 UnityEvent 연결 제거

- [ ] **씬 내 참조 확인**
  - `SessionController.drillCursor` 참조가 올바른 인스턴스를 가리키는지 확인
  - `TopBar` Prefab 인스턴스의 내부 참조 (`resourceText`, `rewardManager`, `session`) 확인

- [ ] **FindObjectOfType 의존성 최소화**
  - 현재 `SessionController`, `OreSpawner` 등에서 `FindObjectOfType` 사용
  - 가능하면 인스펙터 참조로 대체 권장

### 원본-씬 불일치 방지

- [ ] **프리팹 변경 시 씬 테스트**
  - 프리팹 원본 수정 후 씬에서 정상 동작 확인
  - 오버라이드가 의도한 대로 작동하는지 검증

- [ ] **세션 재시작 플로우 테스트**
  - 세션 종료 → 팝업 표시 → "Mine Again" 클릭 → 재시작 플로우 전체 테스트
  - 광석 정리 및 재스폰이 올바르게 동작하는지 확인

- [ ] **리스폰 타이머 동작 확인**
  - 세션 재시작 시 리스폰 타이머가 올바르게 초기화되는지 확인
  - 초기 스폰과 리스폰이 겹치지 않는지 검증

### 코드 일관성 유지

- [ ] **SessionEndPopup 버튼 연결 일관성**
  - `OnClickMineAgain()` 메서드가 실제 버튼 동작과 일치하도록 수정
  - 또는 사용되지 않는 메서드 제거

- [ ] **이벤트 구독/해제 일관성**
  - `TimeHUD`에서 `OnDestroy`에서 이벤트 해제 (✅ 완료)
  - 다른 UI 컴포넌트에서도 동일하게 적용

### 그리드/배치 시스템 일관성

- [ ] **그리드 크기와 최소 거리 조화**
  - `gridCellSize`와 `minDistance`의 관계 명확화
  - 일반적으로 `minDistance <= gridCellSize` 권장
  - 또는 그리드 모드일 때 최소 거리 체크를 그리드 셀 단위로 변경

- [ ] **배치 실패 시 로그 추가**
  - `TryGetFreePosition()` 실패 시 경고 로그 출력 옵션 추가
  - 디버그 모드에서 배치 통계 표시

---

## 7) 다음 우선순위 작업 제안

### 높은 우선순위

1. **SessionEndPopup 버튼 로직 일관성 수정**
   - `OnClickMineAgain()` 메서드가 `RestartSessionFromPopup()`를 호출하도록 수정
   - 또는 현재 사용되는 직접 호출 방식을 유지하고 메서드 제거

2. **리스폰 타이머 초기화 개선**
   - `ResetSpawner()`에서 초기 스폰 완료 후 리스폰 타이머 설정
   - 또는 초기 스폰 후 한 프레임 대기 후 리스폰 활성화

3. **그리드/최소 거리 조화**
   - `useGrid = true`일 때 최소 거리 체크를 그리드 셀 단위로 변경
   - 또는 `gridCellSize`와 `minDistance`의 관계 문서화 및 검증

### 중간 우선순위

4. **Legacy 오브젝트 정리**
   - 비활성화된 Legacy 오브젝트들 완전 제거
   - 참조 연결도 함께 정리

5. **참조 방식 개선**
   - `FindObjectOfType` 대신 인스펙터 참조 사용
   - 싱글톤 패턴은 유지하되, 씬 내 인스턴스 참조는 명시적으로 연결

6. **디버그 로그 추가**
   - `OreSpawner`에서 배치 실패 시 로그 출력 옵션
   - 세션 재시작 플로우 로그 추가

### 낮은 우선순위

7. **Time Ramp 기능 활성화 검토**
   - `timeRampEnabled` 기능이 필요한지 검토
   - 필요 시 테스트 및 균형 조정

8. **Upgrade 시스템 연동**
   - `upgradeFlatBonus`, `upgradeMultiplier` 파라미터 실제 사용
   - Upgrade 씬으로 이동 기능 구현

9. **성능 최적화**
   - `ResourceHUD`의 폴링 방식을 이벤트 기반으로 변경 검토
   - 광석 수가 많을 때의 성능 테스트

---

## 부록: 씬 구조 트리

```
PF_Mining.unity
├── Main Camera
├── RewardManager (DontDestroyOnLoad)
├── _Systems
│   ├── AppBoot
│   ├── Legacy_RhythmSystem (비활성화)
│   ├── Legacy_BeatLogger (비활성화)
│   └── SessionController
├── _GamePlay
│   ├── OreManager (OreSpawner)
│   ├── DrillCursor (Prefab Instance)
│   └── Legacy_OreRoot (비활성화)
├── _UI
│   ├── Canvas
│   │   ├── TopBar (Prefab Instance)
│   │   └── SessionEndPopup
│   └── EventSystem
└── Legacy_Mining (비활성화)
```

---

**문서 작성일:** 2024년
**문서 버전:** 1.0
**마지막 업데이트:** 프로젝트 전체 코드 분석 기반

