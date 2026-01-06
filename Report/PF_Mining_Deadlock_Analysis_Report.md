# PF_Mining 씬 진입 후 데드락 현상 분석 리포트

## 문제 현상
- 카메라 줌아웃이 안 됨
- 스폰이 없음
- 시간이 흐르지 않음

---

## 1. Time.timeScale 데드락 분석

### 1.1 Time.timeScale을 0으로 만드는 코드 검색

**검색 결과**: 프로젝트 전체에서 `Time.timeScale = 0` 또는 `timeScale = 0`을 직접 설정하는 코드를 찾지 못했습니다.

**결론**: 
- ⚠️ **Time.timeScale이 외부에서 0으로 설정되는 경우는 없음**
- 하지만 `MiningCameraZoom`의 줌 애니메이션이 `Time.deltaTime`을 사용하므로, 만약 timeScale이 0이면 데드락 발생 가능

### 1.2 MiningCameraZoom의 줌 애니메이션 타이밍 방식

**위치**: `Assets/Systems/MiningCameraZoom.cs`

**줌 애니메이션 코드** (329-356줄):
```csharp
private IEnumerator ZoomRoutine(float from, float to)
{
    if (zoomDelay > 0f)
        yield return useUnscaledTime ? new WaitForSecondsRealtime(zoomDelay) : new WaitForSeconds(zoomDelay);
    
    float elapsed = 0f;
    while (elapsed < zoomDuration)
    {
        elapsed += Time.deltaTime;  // ⚠️ Time.deltaTime 사용 (timeScale 영향받음)
        // ...
        yield return null;
    }
    // ...
}
```

**문제점**:
- `zoomDelay`는 `useUnscaledTime = true`일 때 `WaitForSecondsRealtime` 사용 (timeScale 영향 안 받음)
- **하지만 `elapsed` 계산은 `Time.deltaTime` 사용** (timeScale = 0이면 0이 되어 무한 루프)
- `useUnscaledTime` 필드는 있지만, `elapsed` 계산에는 적용되지 않음

**영향**:
- `Time.timeScale = 0`이면 `elapsed`가 증가하지 않아 `while (elapsed < zoomDuration)` 무한 루프
- 줌 애니메이션이 완료되지 않아 `TryStartSessionOnce()` 호출 안 됨
- 세션이 시작되지 않아 스폰/타이머도 시작 안 됨

### 1.3 줌 완료 후 세션 시작 연결 고리

**호출 체인**:
```
MiningCameraZoom.OnEnable()
  → StartZoom() (144줄)
    → ZoomRoutine() 코루틴 시작 (214줄)
      → ZoomRoutine 완료 시
        → TryStartSessionOnce() (355줄)
          → SessionController.BeginSessionFlow() (373줄)
            → OreSpawner.StartFresh() (SessionController 134줄)
            → StartSession() (SessionController 145줄)
```

**조건 체크**:
1. `startSessionOnZoomComplete = true` (31줄) - 기본값 true
2. `_sessionStarted = false` (366줄) - 한 번만 실행
3. `sessionController != null` (371줄) - Awake에서 자동 찾기

### 1.4 줌 로직 조기 return 조건

**위치**: `Assets/Systems/MiningCameraZoom.cs`

**조기 return 가능한 지점**:

1. **targetCamera가 null** (155줄)
   ```csharp
   if (!targetCamera)
       return;  // StartZoom() 조기 종료
   ```

2. **startSize == targetSize** (205줄)
   ```csharp
   if (Mathf.Approximately(startSize, targetSize) || zoomDuration <= 0f)
   {
       // 즉시 적용 + 세션 시작 (정상 경로)
   }
   ```

3. **scaleLevel 계산 실패** (178-183줄)
   ```csharp
   int scaleLevel = 0;
   var scaleManager = MiningScaleManager.Instance;
   if (scaleManager != null)
   {
       scaleLevel = Mathf.Max(0, scaleManager.CurrentScaleLevel);
   }
   // scaleManager가 null이면 scaleLevel = 0 (폴백)
   ```

4. **viewHeightTiers가 비어있음** (ViewHeightTierTable 모드일 때, 262줄)
   ```csharp
   if (viewHeightTiers == null || viewHeightTiers.Length == 0)
       return false;  // 선형 방식으로 fallback
   ```

5. **컴파일 에러 가능성** (221줄)
   ```csharp
   if (preferMiningScaleManager)  // ⚠️ 변수 정의되지 않음
   {
       return Mathf.Clamp(msm.GetFinalCameraOrthoSize(), ...);  // ⚠️ 메서드 없음
   }
   ```

### 1.5 가장 가능성 높은 원인 TOP 3

#### **원인 1: 컴파일 에러로 인한 StartZoom() 실행 불가**
- **위치**: `MiningCameraZoom.cs` 221줄, 226줄
- **문제**: 
  - `preferMiningScaleManager` 변수가 정의되지 않음
  - `MiningScaleManager.GetFinalCameraOrthoSize()` 메서드가 존재하지 않음
- **영향**: 컴파일 에러로 `StartZoom()` 메서드가 실행되지 않을 수 있음
- **확인 필요**: Unity 콘솔에 컴파일 에러가 있는지 확인

#### **원인 2: Time.timeScale = 0으로 인한 ZoomRoutine 무한 루프**
- **위치**: `MiningCameraZoom.cs` 337줄
- **문제**: `elapsed += Time.deltaTime`이 timeScale = 0일 때 0이 되어 무한 루프
- **영향**: 줌 애니메이션이 완료되지 않아 세션 시작 안 됨
- **확인 필요**: 런타임에 `Time.timeScale` 값 확인

#### **원인 3: sessionController가 null**
- **위치**: `MiningCameraZoom.cs` 371줄
- **문제**: `Awake()`에서 찾지 못하면 `TryStartSessionOnce()`가 경고만 출력하고 종료
- **영향**: 세션이 시작되지 않음
- **확인 필요**: `SessionController`가 씬에 존재하는지 확인

---

## 2. MiningAreaExpansion → scaleLevel 변환 검증

### 2.1 업그레이드 레벨 → 스케일 레벨 변환 로직

**위치**: `Assets/Systems/MiningScaleManager.cs`

**MainUpgradeId 설정**:
- **코드 기본값**: `"MiningAreaExpansion"` (18줄)
- **인스펙터 값**: 확인 필요 (기본값과 다를 수 있음)

**변환 로직** (92-119줄):
```csharp
private void RefreshScaleLevel(bool force)
{
    int upgradeLevel = 0;
    var um = UpgradeManager.Instance;
    if (um != null && !string.IsNullOrEmpty(mainUpgradeId))
    {
        upgradeLevel = um.GetLevel(mainUpgradeId);  // "MiningAreaExpansion" 레벨 조회
    }
    
    int newScale = 0;
    switch (scaleRule)  // 기본값: TierTable
    {
        case ScaleRule.Linear:
            newScale = Mathf.Clamp(upgradeLevel / linearStep, 0, maxScaleLevel);
            break;
            
        case ScaleRule.TierTable:
            newScale = GetScaleFromTierTable(upgradeLevel);
            break;
    }
    
    if (force || newScale != CurrentScaleLevel)
    {
        CurrentScaleLevel = newScale;
        OnScaleChanged?.Invoke(CurrentScaleLevel);
    }
}
```

**TierTable 매핑** (31-36줄):
```csharp
public List<Tier> tierTable = new List<Tier>()
{
    new Tier(){ minUpgradeLevel = 0, maxUpgradeLevel = 0, scaleLevel = 0 },
    new Tier(){ minUpgradeLevel = 1, maxUpgradeLevel = 1, scaleLevel = 1 },
    new Tier(){ minUpgradeLevel = 2, maxUpgradeLevel = 999, scaleLevel = 2 },
};
```

**변환 예시**:
- `MiningAreaExpansion` 레벨 0 → scaleLevel 0
- `MiningAreaExpansion` 레벨 1 → scaleLevel 1
- `MiningAreaExpansion` 레벨 2 이상 → scaleLevel 2

### 2.2 CurrentScaleLevel 계산/갱신 타이밍

**위치**: `Assets/Systems/MiningScaleManager.cs`

**갱신 시점**:
1. **Start()** (84줄): `RefreshScaleLevel(force: true)` - 강제 갱신
2. **Update()** (89줄): `RefreshScaleLevel(force: false)` - 변경 시에만 갱신

**실행 순서**:
```
MiningScaleManager.Awake() (72줄)
  → Instance 설정
  
MiningScaleManager.Start() (82줄)
  → RefreshScaleLevel(force: true)
    → UpgradeManager에서 레벨 조회
    → CurrentScaleLevel 계산
    → OnScaleChanged 이벤트 발생
    
MiningScaleManager.Update() (87줄)
  → 매 프레임마다 RefreshScaleLevel(force: false)
    → 변경 시에만 갱신
```

**주의사항**:
- `UpgradeManager.Instance`가 `Start()` 시점에 준비되어 있어야 함
- `mainUpgradeId`가 정확히 `"MiningAreaExpansion"`이어야 함

### 2.3 소비자가 scaleLevel을 읽는 지점

#### **MiningCameraZoom**
- **위치**: `MiningCameraZoom.cs` 178-183줄
- **시점**: `StartZoom()` 호출 시 (OnEnable에서 호출)
- **코드**:
  ```csharp
  int scaleLevel = 0;
  var scaleManager = MiningScaleManager.Instance;
  if (scaleManager != null)
  {
      scaleLevel = Mathf.Max(0, scaleManager.CurrentScaleLevel);
  }
  ```

**문제점**:
- `OnEnable()`이 `MiningScaleManager.Start()`보다 먼저 실행될 수 있음
- 이 경우 `CurrentScaleLevel`이 아직 계산되지 않아 0일 수 있음

#### **OreSpawner**
- **위치**: `OreSpawner.cs` 387-389줄
- **시점**: `TryGetSpawnRect()` 호출 시 (스폰할 때마다)
- **코드**:
  ```csharp
  int level = 0;
  var sm = MiningScaleManager.Instance;
  if (sm != null) level = sm.CurrentScaleLevel;
  ```

### 2.4 결론: scaleLevel 문제 판정

**가능성 1: scaleLevel은 제대로 바뀌는데 소비자가 안 읽는 문제**
- **증거**: 
  - 로그에 `scaleLevel 0 -> 1`이 찍혔다면 `CurrentScaleLevel`은 정상 갱신됨
  - 하지만 `MiningCameraZoom.StartZoom()`이 `OnEnable()`에서 호출되어 `MiningScaleManager.Start()`보다 먼저 실행될 수 있음
- **확인 필요**: `MiningCameraZoom.OnEnable()`과 `MiningScaleManager.Start()` 실행 순서

**가능성 2: scaleLevel 자체가 다시 0으로 덮이는 문제**
- **증거**:
  - `MiningScaleManager.Update()`가 매 프레임마다 `RefreshScaleLevel()` 호출
  - `UpgradeManager.Instance`가 null이거나 `GetLevel()`이 0을 반환하면 scaleLevel이 0으로 리셋됨
- **확인 필요**: `UpgradeManager.Instance` 준비 상태 및 `GetLevel("MiningAreaExpansion")` 반환값

**판정**: 
- **가능성 1이 더 높음** - `MiningCameraZoom.OnEnable()`이 `MiningScaleManager.Start()`보다 먼저 실행되어 초기값(0)을 읽을 가능성

---

## 3. Tier Table 실제 사용 소스 확인

### 3.1 MiningCameraZoom의 ViewHeightTier 소스

**위치**: `MiningCameraZoom.cs` 258-298줄

**사용 소스**: **자체 `viewHeightTiers` 배열** (108줄)
```csharp
[SerializeField] private ViewHeightTier[] viewHeightTiers;

private bool TryGetSpawnHeightFromTier(int scaleLevel, out float spawnWorldHeight)
{
    if (viewHeightTiers == null || viewHeightTiers.Length == 0)
        return false;
    
    // 자체 배열 순회
    for (int i = 0; i < viewHeightTiers.Length; i++)
    {
        if (viewHeightTiers[i].scaleLevel == scaleLevel)
        {
            spawnWorldHeight = viewHeightTiers[i].spawnWorldHeight;
            return true;
        }
    }
    // ...
}
```

**확인**: `MiningScaleManager`의 데이터를 읽지 않음

### 3.2 OreSpawner의 ViewHeightTier 소스

**위치**: `OreSpawner.cs` 361-380줄

**사용 소스**: **자체 `viewHeightTiers` 리스트** (61줄)
```csharp
[SerializeField] private List<ViewHeightTier> viewHeightTiers = new List<ViewHeightTier>()
{
    new ViewHeightTier(){ scaleLevel = 0, spawnWorldHeight = 10f },
    new ViewHeightTier(){ scaleLevel = 1, spawnWorldHeight = 13f },
    new ViewHeightTier(){ scaleLevel = 2, spawnWorldHeight = 16f },
};

private float GetSpawnWorldHeightByScaleLevel(int scaleLevel)
{
    if (viewHeightTiers == null || viewHeightTiers.Count == 0)
        return 10f;
    
    // 자체 리스트 순회
    for (int i = 0; i < viewHeightTiers.Count; i++)
    {
        var t = viewHeightTiers[i];
        if (t.scaleLevel <= scaleLevel && t.scaleLevel > chosenLevel)
        {
            chosen = t.spawnWorldHeight;
        }
    }
    return chosen;
}
```

**확인**: `MiningScaleManager`의 데이터를 읽지 않음

### 3.3 MiningScaleManager의 ViewHeightTier

**위치**: `MiningScaleManager.cs` 40-45줄

**제공 데이터**: `viewHeightTiers` 리스트
**제공 메서드**: `GetFinalSpawnWorldHeight()` (121줄)

**하지만**: `MiningCameraZoom`과 `OreSpawner`가 이를 사용하지 않음

### 3.4 결론: 실제 사용 소스

**단일 소스 없음** - 각 컴포넌트가 자체 테이블 사용:
1. `MiningCameraZoom.viewHeightTiers` (배열)
2. `OreSpawner.viewHeightTiers` (리스트)
3. `MiningScaleManager.viewHeightTiers` (리스트, 사용 안 됨)

**불일치 가능성**:
- `MiningCameraZoom`이 `spawnWorldHeight = 13` 기준으로 줌아웃
- `OreSpawner`가 `spawnWorldHeight = 16` 기준으로 스폰 영역 계산
- → 스폰 영역이 카메라보다 크거나 작아질 수 있음

---

## 4. 세션 시작/스폰 트리거 최종 확인

### 4.1 OreSpawner.StartFresh() 호출 경로

**경로 1**: `SessionController.BeginSessionFlow()` (117줄)
```csharp
public void BeginSessionFlow()
{
    if (_spawner != null)
    {
        _spawner.StartFresh();  // 134줄
    }
    StartSession();  // 145줄
}
```

**경로 2**: 직접 호출 (없음 - `StartFresh()`는 public이지만 직접 호출하는 코드 없음)

### 4.2 SessionController.BeginSessionFlow() 호출 경로

**경로 1**: `MiningCameraZoom.TryStartSessionOnce()` (361줄)
```csharp
private void TryStartSessionOnce()
{
    if (!startSessionOnZoomComplete) return;
    if (_sessionStarted) return;
    _sessionStarted = true;
    
    if (sessionController != null)
    {
        sessionController.BeginSessionFlow();  // 373줄
    }
}
```

**호출 시점**:
- `ZoomRoutine()` 완료 시 (355줄)
- 또는 `startSize == targetSize`일 때 즉시 (210줄)

**경로 2**: `SessionController.RestartSessionFromPopup()` (199줄)
```csharp
public void RestartSessionFromPopup()
{
    BeginSessionFlow();  // 202줄
}
```

### 4.3 세션 타이머 시작 함수

**위치**: `SessionController.StartSession()` (152줄)
```csharp
public void StartSession()
{
    Remaining = baseDurationSec;
    _firedCritical = false;
    IsRunning = true;
    
    if (drillCursor) drillCursor.enabled = true;
    OnSessionStart?.Invoke();
}
```

**호출 경로**: `BeginSessionFlow()` 내부에서 호출 (145줄)

### 4.4 카메라 줌 완료 → 세션 시작 연결 고리

**연결 체인**:
```
MiningCameraZoom.OnEnable()
  → StartZoom()
    → ZoomRoutine() 코루틴 시작
      → (1초 후) ZoomRoutine 완료
        → TryStartSessionOnce()
          → SessionController.BeginSessionFlow()
            → OreSpawner.StartFresh()
            → StartSession()
```

**조건**:
1. `startSessionOnZoomComplete = true` (31줄)
2. `_sessionStarted = false` (366줄)
3. `sessionController != null` (371줄)
4. `ZoomRoutine()`이 정상 완료되어야 함

### 4.5 현재 현상과 가장 직접적으로 연결되는 지점

**가장 가능성 높은 지점**: **`MiningCameraZoom.StartZoom()` 메서드 실행 실패**

**이유**:
1. **컴파일 에러**: `preferMiningScaleManager` 변수 미정의, `GetFinalCameraOrthoSize()` 메서드 없음
2. **Time.timeScale = 0**: `ZoomRoutine()` 무한 루프
3. **targetCamera null**: `StartZoom()` 조기 return
4. **실행 순서 문제**: `OnEnable()`이 `MiningScaleManager.Start()`보다 먼저 실행

**영향**:
- `StartZoom()`이 실행되지 않거나 조기 종료
- `ZoomRoutine()`이 시작되지 않음
- `TryStartSessionOnce()`가 호출되지 않음
- `BeginSessionFlow()`가 호출되지 않음
- `OreSpawner.StartFresh()`가 호출되지 않음
- `StartSession()`이 호출되지 않음
- → **스폰 없음, 시간 흐르지 않음**

---

## 최종 결론

### 가장 가능성 높은 원인 TOP 3

#### **1위: 컴파일 에러로 인한 StartZoom() 실행 불가**
- **파일**: `MiningCameraZoom.cs`
- **라인**: 221줄, 226줄
- **메서드**: `CalculateTargetOrthoSize()`
- **문제**: 
  - `preferMiningScaleManager` 변수 미정의
  - `MiningScaleManager.GetFinalCameraOrthoSize()` 메서드 없음
- **영향**: 컴파일 에러로 전체 시스템이 동작하지 않을 수 있음

#### **2위: Time.timeScale = 0으로 인한 ZoomRoutine 무한 루프**
- **파일**: `MiningCameraZoom.cs`
- **라인**: 337줄
- **메서드**: `ZoomRoutine()`
- **문제**: `elapsed += Time.deltaTime`이 timeScale = 0일 때 0이 되어 무한 루프
- **영향**: 줌 애니메이션이 완료되지 않아 세션 시작 안 됨

#### **3위: 실행 순서 문제로 인한 scaleLevel = 0 읽기**
- **파일**: `MiningCameraZoom.cs`, `MiningScaleManager.cs`
- **라인**: 144줄 (OnEnable), 82줄 (Start)
- **문제**: `MiningCameraZoom.OnEnable()`이 `MiningScaleManager.Start()`보다 먼저 실행되어 초기값(0)을 읽음
- **영향**: 줌 계산이 잘못되어 세션이 시작되지 않을 수 있음

### 권장 확인 사항

1. **Unity 콘솔에 컴파일 에러 확인**
2. **런타임에 `Time.timeScale` 값 확인**
3. **`MiningCameraZoom.OnEnable()`과 `MiningScaleManager.Start()` 실행 순서 확인**
4. **`sessionController`가 null인지 확인**
5. **`targetCamera`가 null인지 확인**







