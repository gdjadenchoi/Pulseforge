# Unity 모바일 게임 프로젝트 구조 분석 리포트

## 1. 핵심 게임 루프 구조 요약

### 각 컴포넌트의 책임

#### **SessionController**
- **역할**: 세션 생명주기 관리자 (오케스트레이터)
- **주요 책임**:
  - 세션 시작/종료 타이머 관리 (`baseDurationSec` 기준)
  - `BeginSessionFlow()` 호출 시 `OreSpawner.StartFresh()` 트리거
  - 세션 종료 시 월드 정리 및 팝업 표시
  - 크리티컬 시간(`criticalThreshold`) 이벤트 발생
- **이벤트**: `OnSessionStart`, `OnSessionEnd`, `OnTimeChanged`, `OnCritical`

#### **MiningCameraZoom**
- **역할**: 카메라 줌 연출 및 세션 시작 트리거
- **주요 책임**:
  - `MiningScaleManager.CurrentScaleLevel` 기반 카메라 `orthographicSize` 계산
  - 줌 연출 (1초 동안 부드러운 전환, `zoomDuration`)
  - 줌 완료 후 `SessionController.BeginSessionFlow()` 자동 호출
  - 프리뷰 ore 스폰 (`OreSpawner.SpawnPreview()`)
  - 고정 비율(`fixedAspect`) 및 UI Safe 영역(`topSafePercent`, `bottomSafePercent`) 반영
- **두 가지 줌 모드**:
  - `LinearByScaleLevel`: 선형 증가 (`baseOrthographicSize + sizePerScaleLevel * scaleLevel`)
  - `ViewHeightTierTable`: 티어 테이블 기반 (`spawnWorldHeight` → 카메라 높이 계산)

#### **MiningScaleManager**
- **역할**: 스케일 레벨 중앙 관리자 (싱글톤)
- **주요 책임**:
  - `OreAmount` 업그레이드 레벨 → 스케일 레벨 변환
  - 티어 테이블 또는 선형 방식으로 스케일 레벨 계산
  - 스케일 레벨 변경 시 `OnScaleChanged` 이벤트 발생
  - `DontDestroyOnLoad`로 씬 전환 시 유지

#### **OreSpawner**
- **역할**: 광석 스폰/리스폰 담당
- **주요 책임**:
  - 초기 스폰 (`initialCount`)
  - 리스폰 루프 (`respawnInterval`마다 `TargetCount` 유지)
  - 스폰 영역 계산 (`ViewHeightTier` 기반 또는 카메라 뷰포트)
  - 클러스터 분포 관리
  - `MiningScaleManager.OnScaleChanged` 구독하여 클러스터 재계산

### 세션 시작부터 플레이 중까지 호출 순서

```
1. [씬 로드] PF_Mining 씬 진입
   ↓
2. [MiningCameraZoom.OnEnable]
   → StartZoom() 호출
   → ApplyFixedAspectRect() (고정 비율 적용)
   → OreSpawner.SpawnPreview() (프리뷰 ore 1개)
   → CalculateTargetOrthoSize() (목표 카메라 크기 계산)
   → ZoomRoutine() 코루틴 시작 (1초 동안 줌아웃)
   ↓
3. [MiningScaleManager.Start]
   → RecalculateScaleLevel() (업그레이드 레벨 → 스케일 레벨 변환)
   → OnScaleChanged 이벤트 발생 (OreSpawner가 구독)
   ↓
4. [OreSpawner.OnEnable]
   → MiningScaleManager.OnScaleChanged 구독
   → HandleScaleChanged() → 클러스터 무효화
   ↓
5. [MiningCameraZoom.ZoomRoutine 완료]
   → TryStartSessionOnce()
   → SessionController.BeginSessionFlow() 호출
   ↓
6. [SessionController.BeginSessionFlow]
   → OreSpawner.StartFresh()
     → ClearWorld()
     → InvalidateClusters()
     → EnsureClusters() (스폰 영역 기준으로 클러스터 생성)
     → SpawnInitial(initialCount)
     → RespawnLoop() 코루틴 시작
   → StartSession() (타이머 시작)
   ↓
7. [플레이 중]
   → OreSpawner.RespawnLoop() (0.3초마다)
     → HandleRespawn()
       → SyncScaleLevelChangeIfNeeded() (스케일 레벨 변경 감지)
       → GetTargetCount() (업그레이드 + 스케일 레벨 반영)
       → 부족한 만큼 리스폰
   → SessionController.Update() (타이머 감소)
   ↓
8. [세션 종료]
   → SessionController.EndSession()
   → OreSpawner.PauseAndClear()
   → 팝업 표시
```

---

## 2. OreSpawner의 현재 동작 방식 요약

### 광석 스폰 시점

#### **초기 스폰**
- **시점**: `StartFresh()` 호출 시
- **개수**: `Mathf.Max(initialCount, GetTargetCount())`
- **목적**: 세션 시작 시 즉시 플레이 가능한 상태로 만듦

#### **유지 (리스폰)**
- **시점**: `RespawnLoop()` 코루틴이 `respawnInterval` (0.3초)마다 실행
- **조건**: `respawnToTargetCount = true`이고 현재 개수 < `TargetCount`
- **배치**: 한 번에 최대 `batchMax` (3개)까지 스폰

#### **프리뷰 스폰**
- **시점**: `MiningCameraZoom.StartZoom()` 호출 시 (줌 연출 전)
- **개수**: 기본 1개 (`SpawnPreview(1)`)
- **목적**: 줌 연출 중 시각적 피드백 제공

### TargetCount 계산 로직

```csharp
TargetCount = baseTargetCount
            + (업그레이드 반영 여부)
            + (스케일 레벨 반영 여부)
```

#### **업그레이드 반영**
- **조건**: `useUpgradeOreAmount = true`
- **계산**: `UpgradeManager.GetLevel("OreAmount") * amountPerUpgradeLevel`
- **기본값**: 레벨당 5개 추가

#### **스케일 레벨 반영**
- **조건**: `addByScaleLevel = true`이고 `extraPerScaleLevel > 0`
- **계산**: `MiningScaleManager.CurrentScaleLevel * extraPerScaleLevel`
- **기본값**: 스케일 레벨당 4개 추가

### 스폰 영역 계산 방식

#### **Rect Area 모드 (권장, `useRectArea = true`)**

**계산 순서**:
1. **스케일 레벨 가져오기**: `MiningScaleManager.CurrentScaleLevel`
2. **ViewHeightTier 테이블에서 월드 높이 조회**:
   - 정확히 일치하는 `scaleLevel` 찾기
   - 없으면 `scaleLevel <= 현재 레벨` 중 가장 큰 값 사용
   - 그래도 없으면 첫 번째 항목 사용
3. **Safe Percent 적용**:
   ```
   usableHeight = spawnWorldHeight * (1 - topSafePercent - bottomSafePercent)
   ```
4. **고정 비율로 너비 계산**:
   ```
   usableWidth = usableHeight * fixedAspect (기본 0.5625 = 9:16)
   ```
5. **최종 Rect**:
   ```
   center = areaOffset
   halfSize = (usableWidth * 0.5, usableHeight * 0.5)
   ```

**예시**:
- `scaleLevel = 1` → `spawnWorldHeight = 13f`
- `topSafePercent = 0.10`, `bottomSafePercent = 0.10`
- `usableHeight = 13 * 0.8 = 10.4`
- `usableWidth = 10.4 * 0.5625 = 5.85`
- `halfSize = (2.925, 5.2)`

#### **카메라 뷰포트 모드 (예비, `useRectArea = false`)**

- `Camera.main.ViewportToWorldPoint()` 사용
- 뷰포트 (0,0) ~ (1,1)을 월드 좌표로 변환
- **문제점**: 카메라 크기가 변경 중일 때 중간 상태를 사용할 수 있음

---

## 3. MiningCameraZoom의 현재 역할 요약

### 카메라 Orthographic Size 계산 기준

#### **선형 방식 (`ZoomMode.LinearByScaleLevel`)**

```csharp
targetSize = baseOrthographicSize + sizePerScaleLevel * scaleLevel
targetSize = Clamp(targetSize, minOrthographicSize, maxOrthographicSize)
```

- **기본값**: `baseOrthographicSize = 0` (현재 카메라 값 사용)
- **증가량**: `sizePerScaleLevel = 0.4` (스케일 레벨당)
- **제한**: `min = 4.5`, `max = 10`

#### **티어 테이블 방식 (`ZoomMode.ViewHeightTierTable`)**

**계산 순서**:
1. **ViewHeightTier 테이블에서 `spawnWorldHeight` 조회**
   - `MiningCameraZoom.viewHeightTiers` 사용 (OreSpawner와 별도 테이블)
2. **여유 공간 추가**:
   ```
   viewH = spawnWorldHeight * (1 + verticalPaddingPercent)
   ```
3. **UI Safe 영역 고려**:
   ```
   safeFactor = 1 - topSafePercent - bottomSafePercent
   cameraWorldHeight = viewH / safeFactor
   ```
4. **최종 orthographicSize**:
   ```
   orthographicSize = cameraWorldHeight * 0.5
   ```

**예시**:
- `scaleLevel = 1` → `spawnWorldHeight = 13f`
- `verticalPaddingPercent = 0.10` → `viewH = 14.3`
- `topSafePercent = 0.10`, `bottomSafePercent = 0.10` → `safeFactor = 0.8`
- `cameraWorldHeight = 14.3 / 0.8 = 17.875`
- `orthographicSize = 17.875 * 0.5 = 8.9375`

### ScaleLevel 반영

- **소스**: `MiningScaleManager.Instance.CurrentScaleLevel`
- **시점**: `StartZoom()` 호출 시점에 한 번만 읽음
- **동적 반영**: 줌 연출 중 스케일 레벨 변경 시 자동 반영 안 됨 (수동 호출 필요)

### ViewHeightTier 반영

- **MiningCameraZoom**: 자체 `viewHeightTiers` 테이블 사용
- **OreSpawner**: 별도 `viewHeightTiers` 테이블 사용
- **주의**: 두 테이블이 동기화되지 않으면 카메라와 스폰 영역 불일치 발생 가능

### UI Safe Percent 반영

#### **고정 비율 적용 (`enforceFixedGameplayAspect = true`)**
- `Camera.rect`를 조정하여 화면 비율 강제
- 예: 9:16 비율이면 pillarbox/letterbox 적용

#### **Safe Percent 계산**
- **상단 여백**: `topSafePercent` (기본 0.10 = 10%)
- **하단 여백**: `bottomSafePercent` (기본 0.10 = 10%)
- **실제 플레이 가능 높이**: `전체 높이 * (1 - topSafe - bottomSafe)`
- **카메라 높이**: `스폰 높이 / safeFactor` (더 크게 보여줌)

#### **Vertical Padding Percent**
- 스폰 영역 대비 카메라가 추가로 보여줄 여유 (기본 0.10 = 10%)
- **목적**: 스폰 영역 경계 근처 ore도 화면에 보이도록

---

## 4. 현재 구조에서 '의도된 설계'와 '실제 동작'이 어긋날 수 있는 지점

### 4.1 스폰 영역과 카메라 화면 불일치 가능성

#### **문제 1: ViewHeightTier 테이블 이중 관리**

**의도된 설계**:
- `MiningCameraZoom`과 `OreSpawner`가 동일한 스케일 레벨에 대해 동일한 `spawnWorldHeight`를 사용해야 함

**실제 동작**:
- 두 컴포넌트가 **별도의 `viewHeightTiers` 테이블**을 가지고 있음
- 인스펙터에서 수동으로 동기화해야 함
- 하나만 수정하면 불일치 발생

**영향**:
- 카메라는 `spawnWorldHeight = 13` 기준으로 줌아웃
- 스폰 영역은 `spawnWorldHeight = 16` 기준으로 계산
- → 스폰 영역이 카메라보다 크거나 작아질 수 있음

#### **문제 2: 줌 연출 중 리스폰 발생**

**의도된 설계**:
- 줌 연출이 완료된 후 세션이 시작되어야 함

**실제 동작**:
- `OreSpawner`가 `OnScaleChanged` 이벤트를 받으면 즉시 클러스터 재계산
- 줌 연출 중간에 리스폰이 발생할 수 있음 (이전 분석 리포트 참조)
- 카메라 크기가 중간 상태일 때 스폰 영역 계산

**영향**:
- 중간 크기의 카메라를 기준으로 스폰 영역이 계산되어
- 최종 줌아웃된 크기보다 작은 영역이 계산될 수 있음
- 일부 ore가 카메라를 벗어난 위치에 스폰

#### **문제 3: Safe Percent 불일치**

**의도된 설계**:
- `MiningCameraZoom`과 `OreSpawner`가 동일한 `topSafePercent`, `bottomSafePercent`를 사용해야 함

**실제 동작**:
- 두 컴포넌트가 **별도 필드**로 관리
- 인스펙터에서 수동 동기화 필요

**영향**:
- 카메라는 `topSafe = 0.10` 기준으로 계산
- 스폰 영역은 `topSafe = 0.15` 기준으로 계산
- → 스폰 영역이 카메라보다 작아져서 경계 근처 ore가 화면 밖에 스폰

#### **문제 4: Fixed Aspect 불일치**

**의도된 설계**:
- `MiningCameraZoom.fixedAspect`와 `OreSpawner.fixedAspect`가 동일해야 함

**실제 동작**:
- 별도 필드로 관리
- 기본값은 동일하지만 수정 시 불일치 가능

**영향**:
- 카메라는 9:16 비율로 렌더링
- 스폰 영역은 3:4 비율로 계산
- → 가로/세로 비율 불일치로 일부 영역이 화면 밖

### 4.2 모바일 해상도 / 비율 차이에 따른 영향 포인트

#### **문제 5: 다양한 화면 비율 대응**

**의도된 설계**:
- `enforceFixedGameplayAspect = true`일 때 모든 기기에서 동일한 플레이 영역 비율 유지

**실제 동작**:
- `Camera.rect`로 pillarbox/letterbox 적용
- 하지만 `OreSpawner`는 `fixedAspect`로 스폰 영역 계산
- **화면 비율이 다른 기기에서**:
  - 카메라는 pillarbox로 좌우가 잘림
  - 스폰 영역은 고정 비율로 계산되어 실제 화면과 불일치 가능

**예시**:
- 18:9 기기 (비율 0.5)
- `fixedAspect = 0.5625` (9:16)
- 카메라: 좌우 pillarbox 적용
- 스폰 영역: 9:16 비율로 계산
- → 스폰 영역이 실제 보이는 영역과 다를 수 있음

#### **문제 6: Safe Area 계산의 모바일 특화 부재**

**의도된 설계**:
- `topSafePercent`, `bottomSafePercent`로 UI 영역 제외

**실제 동작**:
- 고정된 비율 사용 (예: 10%)
- **실제 모바일 기기의 Safe Area** (노치, 홈 인디케이터 등)를 고려하지 않음
- Unity의 `Screen.safeArea` API 미사용

**영향**:
- iPhone X 시리즈: 상단 노치로 실제 Safe Area가 더 작음
- Android 기기: 다양한 Safe Area 크기
- → 고정된 10%가 실제와 다를 수 있음

#### **문제 7: 해상도별 픽셀 밀도 차이**

**의도된 설계**:
- 월드 단위(`spawnWorldHeight`)로 계산하여 해상도 독립적

**실제 동작**:
- 월드 단위 사용으로 해상도 영향은 적음
- 하지만 `Camera.rect` 적용 시 실제 픽셀 크기가 달라질 수 있음

**영향**:
- 고해상도 기기: 더 많은 픽셀로 렌더링
- 저해상도 기기: 적은 픽셀로 렌더링
- → UI 요소 크기는 동일하지만 실제 보이는 영역이 다를 수 있음

### 4.3 타이밍 관련 불일치

#### **문제 8: 스케일 레벨 변경 시 즉시 반영 안 됨**

**의도된 설계**:
- 업그레이드로 스케일 레벨이 변경되면 카메라와 스폰 영역이 즉시 업데이트

**실제 동작**:
- `MiningScaleManager.OnScaleChanged` 이벤트 발생
- `OreSpawner`는 구독하여 클러스터 재계산
- **하지만 `MiningCameraZoom`은 구독하지 않음**
- 카메라는 씬 재진입 시에만 업데이트

**영향**:
- 인게임에서 업그레이드 시
- 스폰 영역은 즉시 확대
- 카메라는 이전 크기 유지
- → 스폰된 ore가 카메라 밖에 있을 수 있음

---

## 5. 코드 관점에서의 리스크 요약

### 5.1 책임이 겹치는 부분

#### **리스크 1: ViewHeightTier 테이블 이중 관리**

**위치**:
- `MiningCameraZoom.viewHeightTiers` (104줄)
- `OreSpawner.viewHeightTiers` (61줄)

**문제**:
- 동일한 데이터를 두 곳에서 관리
- 수정 시 두 곳 모두 업데이트 필요
- 하나만 수정하면 불일치 발생

**영향도**: ⚠️ **높음** (스폰 영역과 카메라 불일치)

#### **리스크 2: Safe Percent 이중 관리**

**위치**:
- `MiningCameraZoom.topSafePercent`, `bottomSafePercent` (80, 84줄)
- `OreSpawner.topSafePercent`, `bottomSafePercent` (54, 58줄)

**문제**:
- 동일한 설정값을 두 곳에서 관리
- 인스펙터에서 수동 동기화 필요

**영향도**: ⚠️ **중간** (스폰 영역 크기 불일치)

#### **리스크 3: Fixed Aspect 이중 관리**

**위치**:
- `MiningCameraZoom.fixedAspect` (76줄)
- `OreSpawner.fixedAspect` (50줄)

**문제**:
- 동일한 비율값을 두 곳에서 관리
- 기본값은 동일하지만 수정 시 불일치 가능

**영향도**: ⚠️ **중간** (가로/세로 비율 불일치)

### 5.2 추후 수정 시 같이 깨질 가능성이 높은 지점

#### **리스크 4: 스케일 레벨 계산 로직 변경**

**위치**:
- `MiningScaleManager.RecalculateScaleLevel()` (136줄)
- `MiningScaleManager.GetScaleFromTierTable()` (190줄)

**영향받는 코드**:
- `MiningCameraZoom.CalculateTargetOrthoSize()` (214줄)
- `OreSpawner.GetSpawnWorldHeightByScaleLevel()` (361줄)
- `OreSpawner.GetTargetCount()` (205줄)

**문제**:
- 스케일 레벨 계산 방식 변경 시
- 카메라 줌, 스폰 영역, 목표 개수 모두 영향
- 한 곳만 수정하면 불일치 발생

**영향도**: ⚠️ **높음** (전체 시스템 불일치)

#### **리스크 5: ViewHeightTier 테이블 구조 변경**

**위치**:
- `MiningCameraZoom.ViewHeightTier` 구조체 (94줄)
- `OreSpawner.ViewHeightTier` 구조체 (69줄)

**문제**:
- 구조체 필드 추가/변경 시
- 두 곳 모두 수정 필요
- 직렬화 데이터 호환성 문제 가능

**영향도**: ⚠️ **중간** (인스펙터 데이터 손실 가능)

#### **리스크 6: Safe Percent 계산 로직 변경**

**위치**:
- `MiningCameraZoom.CalculateTargetOrthoSize()` (226줄)
- `OreSpawner.TryGetSpawnRect()` (394줄)

**문제**:
- Safe Percent 적용 방식 변경 시
- 두 곳 모두 수정 필요
- 계산식이 다르면 불일치 발생

**영향도**: ⚠️ **중간** (스폰 영역 크기 불일치)

#### **리스크 7: 이벤트 구독/해제 누락**

**위치**:
- `OreSpawner.OnEnable()` (117줄) - 구독
- `OreSpawner.OnDisable()` (128줄) - 해제
- `MiningCameraZoom` - 구독 없음

**문제**:
- `MiningCameraZoom`이 `OnScaleChanged`를 구독하지 않음
- 씬 재진입 시에만 카메라 업데이트
- 인게임 업그레이드 시 카메라가 업데이트 안 됨

**영향도**: ⚠️ **높음** (인게임 업그레이드 시 불일치)

#### **리스크 8: 클러스터 재계산 타이밍**

**위치**:
- `OreSpawner.HandleScaleChanged()` (341줄)
- `OreSpawner.SyncScaleLevelChangeIfNeeded()` (348줄)
- `OreSpawner.EnsureClusters()` (312줄)

**문제**:
- 클러스터가 `_clustersInitialized` 플래그로 한 번만 초기화
- 스폰 영역 변경 시 무효화 후 재계산
- 하지만 **리스폰 루프 중간에** 스폰 영역이 변경되면
- 이전 클러스터를 사용하여 스폰할 수 있음

**영향도**: ⚠️ **중간** (일시적 불일치 가능)

#### **리스크 9: 세션 시작 타이밍 의존성**

**위치**:
- `MiningCameraZoom.TryStartSessionOnce()` (347줄)
- `SessionController.BeginSessionFlow()` (117줄)
- `OreSpawner.StartFresh()` (143줄)

**문제**:
- `MiningCameraZoom`이 줌 완료 후 세션 시작
- 하지만 `OreSpawner`가 `autoSpawnOnAwake = true`이면
- 줌 연출 전에 이미 스폰 시작 가능
- → 타이밍 충돌 가능

**영향도**: ⚠️ **낮음** (현재 코드에서는 `autoSpawnOnAwake = false`)

### 5.3 데이터 일관성 리스크

#### **리스크 10: 스케일 레벨과 ViewHeightTier 매핑 누락**

**문제**:
- `MiningScaleManager`의 스케일 레벨이 0, 1, 2, 3... 으로 증가
- 하지만 `ViewHeightTier` 테이블에 모든 레벨이 정의되지 않을 수 있음
- 예: 스케일 레벨 5인데 테이블에는 0, 1, 2만 있음
- → 폴백 로직에 의존 (첫 번째 항목 또는 가장 가까운 값)

**영향도**: ⚠️ **중간** (예상치 못한 동작 가능)

#### **리스크 11: 인스펙터 설정 누락/오류**

**문제**:
- 여러 컴포넌트의 인스펙터 설정이 서로 의존
- 하나라도 잘못 설정하면 전체 시스템 불일치
- 런타임 검증 부재

**영향도**: ⚠️ **높음** (디버깅 어려움)

---

## 요약

### 핵심 문제점
1. **데이터 이중 관리**: ViewHeightTier, Safe Percent, Fixed Aspect가 여러 곳에서 관리
2. **이벤트 구독 불완전**: `MiningCameraZoom`이 스케일 변경 이벤트를 구독하지 않음
3. **타이밍 불일치**: 줌 연출 중 리스폰 발생 가능
4. **모바일 특화 부재**: 실제 Safe Area API 미사용

### 가장 위험한 지점
1. **ViewHeightTier 테이블 불일치** → 카메라와 스폰 영역 크기 불일치
2. **인게임 업그레이드 시 카메라 미업데이트** → 스폰된 ore가 화면 밖
3. **Safe Percent 불일치** → 스폰 영역이 실제 보이는 영역과 다름

### 수정 시 주의사항
- ViewHeightTier 테이블 수정 시 두 컴포넌트 모두 업데이트
- Safe Percent 변경 시 두 컴포넌트 모두 동기화
- 스케일 레벨 계산 로직 변경 시 전체 시스템 영향 검토
- 이벤트 구독/해제 누락 주의








