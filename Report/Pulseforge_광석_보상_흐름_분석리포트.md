# Pulseforge 광석 보상 흐름 분석 리포트

**작성일:** 2025년  
**목적:** 광석 파괴 시 보상 지급 흐름 분석 및 ExpManager Hook 지점 설계

---

## 목차

1. [광석 관련 스크립트 구조 분석](#1-광석-관련-스크립트-구조-분석)
2. [피격 → HP 감소 → 파괴 → 보상 지급 흐름](#2-피격--hp-감소--파괴--보상-지급-흐름)
3. [광석별 보상 데이터 관리 방식](#3-광석별-보상-데이터-관리-방식)
4. [경험치 Hook 후보 지점 제안](#4-경험치-hook-후보-지점-제안)
5. [즉시 경험치 vs 세션 종료 시 경험치 비교](#5-즉시-경험치-vs-세션-종료-시-경험치-비교)
6. [데이터 구조 관점 코멘트](#6-데이터-구조-관점-코멘트)

---

## 1. 광석 관련 스크립트 구조 분석

### 1.1 광석 역할을 하는 스크립트 목록

#### 1) Ore (메인 광석 스크립트)

**클래스명:** `Pulseforge.Systems.Ore`  
**파일 경로:** `Assets/Systems/Ore.cs`  
**Prefab 경로:** `Assets/Prefabs/OrePrefab.prefab`  
**씬 내 인스턴스 경로:** `PF_Mining/_GamePlay/OreManager` 하위 (Runtime 생성)

**Inspector 주요 필드:**

| 필드명 | 타입 | 기본값 | 설명 |
|--------|------|--------|------|
| **maxHp** | `float` | `30.0` | 최대 HP |
| **destroyOnBreak** | `bool` | `true` | 파괴 시 오브젝트 삭제 여부 |
| **rewardType** | `RewardType` | `Crystal` | 보상 타입 (enum) |
| **rewardAmount** | `int` | `1` | 보상 수량 |
| **wobbleOnHit** | `bool` | `true` | 피격 시 흔들림 여부 |
| **showHpBar** | `bool` | `true` | HP 바 표시 여부 |
| **revealBarOnFirstHit** | `bool` | `true` | 첫 피격 시 HP 바 표시 |

**역할:**
- HP 관리 및 데미지 처리
- 파괴 시 보상 지급 (`RewardManager.Instance.Add()` 호출)
- HP 바 표시 및 피격 피드백 (Wobble 애니메이션)

---

#### 2) OreSpawner (광석 스폰 관리)

**클래스명:** `Pulseforge.Systems.OreSpawner`  
**파일 경로:** `Assets/Systems/OreSpawner.cs`  
**GameObject 경로:** `PF_Mining/_GamePlay/OreManager`

**역할:**
- 광석 프리팹 스폰/리스폰 관리
- 세션 시작/종료 시 광석 정리
- **보상 지급과는 직접 연관 없음** (스폰만 담당)

---

#### 3) DrillCursor (광석 공격)

**클래스명:** `Pulseforge.Systems.DrillCursor`  
**파일 경로:** `Assets/Systems/DrillCursor.cs`  
**Prefab 경로:** `Assets/Prefabs/DrillCursor.prefab`  
**씬 내 인스턴스 경로:** `PF_Mining/_GamePlay/DrillCursor`

**Inspector 주요 필드:**

| 필드명 | 타입 | 기본값 | 설명 |
|--------|------|--------|------|
| **swingInterval** | `float` | `0.18` | 한 번 스윙 간격 (초) |
| **damagePerSwing** | `float` | `3.0` | 한 번 스윙 당 피해량 |
| **fixedRadius** | `float` | `0.45` | 고정 반경 (월드 단위) |
| **oreMask** | `LayerMask` | (비어있음) | Ore가 있는 레이어 |

**역할:**
- 마우스/터치 입력 추적
- 주기적으로 주변 광석 감지 및 데미지 적용
- `Ore.ApplyHit(damagePerSwing)` 호출

---

#### 4) CoreOrePulse (비주얼 효과, 레거시)

**클래스명:** `Pulseforge.Visuals.CoreOrePulse`  
**파일 경로:** `Assets/Visual/CoreOrePulse.cs`  
**씬 내 위치:** `PF_Mining/_GamePlay/Legacy_OreRoot` (비활성화)

**역할:**
- 리듬 비주얼 피드백 (레거시 시스템)
- **현재 사용되지 않음** (비활성화 상태)

---

### 1.2 스크립트 관계도

```
DrillCursor (공격)
    │
    │ DoSwingHit() → Physics2D.OverlapCircleNonAlloc()
    │
    ▼
Ore (광석)
    │
    │ ApplyHit(damage) → hp 감소
    │
    │ hp <= 0 → OnBroken()
    │
    ▼
RewardManager (보상 지급)
    │
    │ Instance.Add(rewardType, rewardAmount)
    │
    ▼
자원 누적
```

---

## 2. 피격 → HP 감소 → 파괴 → 보상 지급 흐름

### 2.1 전체 호출 흐름

```
1. DrillCursor.Update()
   └─ swingInterval 경과 시 DoSwingHit() 호출
   
2. DrillCursor.DoSwingHit()
   └─ Physics2D.OverlapCircleNonAlloc()로 주변 광석 감지
   └─ 각 광석에 대해:
      └─ col.TryGetComponent<Ore>(out var ore)
      └─ ore.ApplyHit(damagePerSwing) 호출
   
3. Ore.ApplyHit(float damage)
   └─ hp = Mathf.Max(0f, hp - damage)  // HP 감소
   └─ UpdateBarFill()  // HP 바 업데이트
   └─ PlayWobble()  // 피격 피드백
   └─ if (hp <= 0f) OnBroken() 호출
   
4. Ore.OnBroken()
   └─ if (rewardAmount > 0)
      └─ RewardManager.Instance?.Add(rewardType, rewardAmount)  // 보상 지급
   └─ if (destroyOnBreak)
      └─ Destroy(gameObject)  // 오브젝트 삭제
```

---

### 2.2 상세 코드 위치

#### 1) 데미지 적용 (DrillCursor)

**파일:** `Assets/Systems/DrillCursor.cs`  
**메서드:** `DoSwingHit()` (129-157줄)

```csharp
// 129줄: DoSwingHit() 메서드 시작
private void DoSwingHit()
{
    // 131-136줄: 주변 광석 감지
    var radius = GetCurrentRadius();
    var total = Physics2D.OverlapCircleNonAlloc(
        (Vector2)transform.position,
        radius + detectPadding,
        _hits
    );
    
    // 141-153줄: 감지된 광석에 데미지 적용
    for (int i = 0; i < total; i++)
    {
        var col = _hits[i];
        if (col.TryGetComponent<Ore>(out var ore))
        {
            ore.ApplyHit(damagePerSwing);  // ← 여기서 데미지 적용
            applied++;
        }
    }
}
```

**호출 위치:** `DrillCursor.Update()` (79-84줄)에서 `swingInterval` 경과 시 호출

---

#### 2) HP 감소 및 파괴 체크 (Ore)

**파일:** `Assets/Systems/Ore.cs`  
**메서드:** `ApplyHit(float damage)` (71-84줄)

```csharp
// 71줄: ApplyHit() 메서드 시작
public void ApplyHit(float damage)
{
    if (damage <= 0f) return;
    
    // 75줄: 첫 피격 시 HP 바 표시
    if (revealBarOnFirstHit && showHpBar) SetBarVisible(true);
    
    // 77줄: HP 감소
    hp = Mathf.Max(0f, hp - damage);
    UpdateBarFill();  // HP 바 업데이트
    
    // 80줄: 피격 피드백
    if (wobbleOnHit) PlayWobble();
    
    // 82-83줄: HP가 0 이하가 되면 파괴 처리
    if (hp <= 0f)
        OnBroken();
}
```

---

#### 3) 보상 지급 (Ore)

**파일:** `Assets/Systems/Ore.cs`  
**메서드:** `OnBroken()` (86-99줄)

```csharp
// 86줄: OnBroken() 메서드 시작
private void OnBroken()
{
    // 88-90줄: 보상 지급
    if (rewardAmount > 0)
        RewardManager.Instance?.Add(rewardType, rewardAmount);
    
    // 92-98줄: 오브젝트 삭제 또는 리스폰
    if (destroyOnBreak)
        Destroy(gameObject);
    else
    {
        hp = maxHp;  // 리스폰 모드
        UpdateBarFill();
    }
}
```

**보상 지급 메서드:**
- `RewardManager.Instance.Add(RewardType type, int delta)`
- **위치:** `Assets/Systems/RewardManager.cs` (72-78줄)
- **동작:** `_amounts[type] += delta` 후 이벤트 발생

---

### 2.3 호출 흐름 다이어그램

```
┌─────────────────────┐
│ DrillCursor         │
│ Update()            │
│   swingTimer += dt  │
└──────────┬──────────┘
           │ swingInterval 경과
           ▼
┌─────────────────────┐
│ DrillCursor         │
│ DoSwingHit()         │
│   OverlapCircle()    │
└──────────┬──────────┘
           │ 각 광석에 대해
           ▼
┌─────────────────────┐
│ Ore                 │
│ ApplyHit(damage)    │
│   hp -= damage      │
│   UpdateBarFill()   │
│   PlayWobble()       │
└──────────┬──────────┘
           │ hp <= 0
           ▼
┌─────────────────────┐
│ Ore                 │
│ OnBroken()          │
│   RewardManager     │
│   .Instance.Add()   │ ← 보상 지급
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ RewardManager       │
│ Add(type, amount)   │
│   _amounts[type] += │
│   FireEvents()      │
└─────────────────────┘
```

---

## 3. 광석별 보상 데이터 관리 방식

### 3.1 현재 구조

**방식:** MonoBehaviour 필드로 직접 보유

**위치:** `Assets/Systems/Ore.cs` (14-16줄)

```csharp
[Header("Reward")]
[SerializeField] private RewardType rewardType = RewardType.Crystal;
[SerializeField] private int rewardAmount = 1;
```

**특징:**
- 각 광석 인스턴스가 **자신의 보상 값**을 직접 들고 있음
- Prefab 단위로 설정 가능 (Inspector에서 설정)
- Runtime에 동적으로 변경 가능

---

### 3.2 보상 데이터 흐름

```
OrePrefab.prefab (프리팹)
    │
    │ Inspector 설정:
    │ - rewardType: Crystal
    │ - rewardAmount: 1
    │
    ▼
OreSpawner.SpawnBatch()
    │
    │ Instantiate(orePrefab)
    │
    ▼
Ore 인스턴스 (씬 내 생성)
    │
    │ 각 인스턴스가 독립적으로 보상 값 보유
    │ (같은 프리팹에서 생성되어도 개별 설정 가능)
    │
    ▼
Ore.OnBroken()
    │
    │ RewardManager.Instance.Add(rewardType, rewardAmount)
    │
    ▼
RewardManager._amounts[type] += amount
```

---

### 3.3 보상 타입 정의

**파일:** `Assets/Systems/RewardType.cs`

```csharp
public enum RewardType
{
    Crystal = 0,
    Gold = 1,
    Shard = 2,
}
```

**특징:**
- Enum 기반 타입 시스템
- 확장 가능 (새로운 타입 추가 용이)
- `RewardManager`에서 딕셔너리 키로 사용

---

### 3.4 외부 계산 여부

**결론:** 외부 계산 없음, 광석이 직접 보유

- `RewardManager`는 보상 값을 계산하지 않음
- `OreSpawner`도 보상과 무관 (스폰만 담당)
- 각 `Ore` 인스턴스가 `rewardType`과 `rewardAmount`를 직접 보유
- `OnBroken()`에서 `RewardManager.Instance.Add(rewardType, rewardAmount)` 호출

---

## 4. 경험치 Hook 후보 지점 제안

### 4.1 후보 1: Ore.OnBroken() 내부 (추천)

**위치:** `Assets/Systems/Ore.cs` - `OnBroken()` 메서드 (86-99줄)

**현재 코드 구조:**
```csharp
private void OnBroken()
{
    // 보상 지급
    if (rewardAmount > 0)
        RewardManager.Instance?.Add(rewardType, rewardAmount);
    
    // 경험치 지급 (추가 예정)
    // ExpManager.Instance?.AddExp(expAmount);
    
    if (destroyOnBreak)
        Destroy(gameObject);
    // ...
}
```

**장점:**
1. **일관성**: 보상 지급과 동일한 위치에서 경험치 지급 → 코드 구조 일관성 유지
2. **단순성**: 광석 파괴 시점에 바로 처리 → 추가 로직 최소화
3. **명확성**: "광석 파괴 = 보상 + 경험치"라는 관계가 명확함
4. **즉시 피드백**: 플레이어가 광석을 파괴하는 순간 경험치 획득 → 즉각적인 피드백

**단점:**
1. **세션 종료 시 집계 불가**: 세션 종료 시 총 경험치를 한 번에 보여주기 어려움 (별도 집계 필요)
2. **광석별 경험치 값 관리**: 각 광석에 경험치 값을 추가해야 함

**구현 시 고려사항:**
- `Ore` 클래스에 `expAmount` 필드 추가 필요
- 또는 `RewardManager`와 유사하게 `RewardType` 기반 경험치 매핑 사용 가능

---

### 4.2 후보 2: RewardManager.Add() 내부

**위치:** `Assets/Systems/RewardManager.cs` - `Add()` 메서드 (72-78줄)

**현재 코드 구조:**
```csharp
public void Add(RewardType type, int delta)
{
    if (delta <= 0) return;
    var now = Get(type) + delta;
    _amounts[type] = now;
    FireEvents(type, now);
    
    // 경험치 지급 (추가 예정)
    // ExpManager.Instance?.AddExp(CalculateExpFromReward(type, delta));
}
```

**장점:**
1. **중앙 집중식**: 모든 보상 지급이 `RewardManager`를 거치므로, 한 곳에서 경험치 처리 가능
2. **보상 타입 기반 계산**: `RewardType`에 따라 경험치를 자동 계산 가능 (예: Crystal = 10exp, Gold = 5exp)
3. **기존 코드 수정 최소화**: `Ore.OnBroken()`은 수정 불필요

**단점:**
1. **의존성 증가**: `RewardManager`가 `ExpManager`에 의존하게 됨 (순환 참조 주의)
2. **책임 분리 위반**: `RewardManager`가 보상뿐만 아니라 경험치까지 관리하게 됨
3. **유연성 저하**: 광석별로 다른 경험치를 주고 싶을 때 제약 (보상 타입만으로는 구분 불가)

**구현 시 고려사항:**
- `RewardManager`와 `ExpManager` 간 순환 참조 방지 필요
- 보상 타입 → 경험치 매핑 테이블 필요

---

### 4.3 후보 3: SessionController.EndSession() 내부

**위치:** `Assets/Systems/SessionController.cs` - `EndSession()` 메서드 (86-118줄)

**현재 코드 구조:**
```csharp
public void EndSession()
{
    // ...
    // 보상 스냅샷 생성
    IReadOnlyDictionary<RewardType, int> snapshot =
        _rewards != null ? _rewards.GetAll() : new Dictionary<RewardType, int>();
    
    // 경험치 계산 (추가 예정)
    // int totalExp = CalculateExpFromRewards(snapshot);
    // ExpManager.Instance?.AddExp(totalExp);
    
    ClearWorld();
    if (popup != null)
        popup.Show(snapshot, this);
    // ...
}
```

**장점:**
1. **세션 단위 집계**: 세션 종료 시 총 경험치를 한 번에 계산하여 지급 가능
2. **보상 기반 계산**: 세션 동안 얻은 보상 총량을 기반으로 경험치 계산 가능
3. **UI 연동 용이**: `SessionEndPopup`에서 경험치 획득량 표시 가능

**단점:**
1. **즉시 피드백 부족**: 광석 파괴 시점이 아닌 세션 종료 시점에 경험치 지급 → 피드백 지연
2. **구조 변경 필요**: 현재는 광석 파괴 즉시 보상 지급하는 구조인데, 경험치는 세션 종료 시 지급 → 일관성 저하
3. **임시 데이터 관리**: 세션 동안 경험치를 임시로 저장할 구조 필요 (별도 필드 또는 딕셔너리)

**구현 시 고려사항:**
- 세션 동안 경험치를 임시로 저장할 필드 필요 (예: `SessionController._sessionExp`)
- 또는 보상 총량을 기반으로 경험치 계산

---

### 4.4 추천 순위

**1순위: 후보 1 (Ore.OnBroken() 내부)**

**이유:**
- 현재 구조와 가장 일관성 있음 (보상 지급과 동일한 위치)
- 즉시 피드백 제공
- 구현이 단순함
- 광석별 경험치 값 관리가 명확함

**2순위: 후보 2 (RewardManager.Add() 내부)**

**이유:**
- 중앙 집중식 관리 가능
- 보상 타입 기반 자동 계산 가능
- 단, 의존성 관리 주의 필요

**3순위: 후보 3 (SessionController.EndSession() 내부)**

**이유:**
- 세션 단위 집계 가능
- 단, 즉시 피드백 부족 및 구조 변경 필요

---

## 5. 즉시 경험치 vs 세션 종료 시 경험치 비교

### 5.1 즉시 경험치 지급 방식

**구현 위치:** `Ore.OnBroken()` 내부

**흐름:**
```
광석 파괴
  ↓
Ore.OnBroken()
  ↓
RewardManager.Instance.Add()  // 보상 즉시 지급
ExpManager.Instance.AddExp()  // 경험치 즉시 지급
  ↓
즉시 피드백 (UI 업데이트)
```

**장점:**
1. **즉시 피드백**: 플레이어가 광석을 파괴하는 순간 경험치 획득 → 만족도 향상
2. **구조 일관성**: 보상 지급과 동일한 위치에서 처리 → 코드 일관성 유지
3. **구현 단순**: 추가 데이터 구조 불필요 (임시 저장 없음)
4. **디버깅 용이**: 광석 파괴 시점에 바로 경험치 지급되므로 추적 용이

**단점:**
1. **세션 종료 시 집계 어려움**: 세션 종료 시 "이번 세션에서 얻은 경험치"를 보여주려면 별도 집계 필요
2. **UI 표시 제약**: 세션 종료 팝업에서 경험치 획득량을 표시하려면 `ExpManager`에서 세션 시작 시점의 경험치를 기록해야 함

**현재 구조와의 호환성:**
- ✅ **매우 호환**: 현재 보상 지급 구조와 동일한 패턴
- ✅ **최소 변경**: `Ore.OnBroken()`에 한 줄만 추가하면 됨
- ✅ **데이터 구조 불필요**: 임시 저장 구조 불필요

---

### 5.2 세션 종료 시 경험치 지급 방식

**구현 위치:** `SessionController.EndSession()` 내부

**흐름:**
```
광석 파괴
  ↓
Ore.OnBroken()
  ↓
RewardManager.Instance.Add()  // 보상 즉시 지급
SessionController._sessionExp += expAmount  // 경험치 임시 저장
  ↓
세션 종료
  ↓
SessionController.EndSession()
  ↓
ExpManager.Instance.AddExp(_sessionExp)  // 경험치 일괄 지급
SessionEndPopup.Show()  // 경험치 획득량 표시
```

**장점:**
1. **세션 단위 집계**: 세션 종료 시 "이번 세션에서 얻은 경험치"를 명확히 표시 가능
2. **UI 연동 용이**: `SessionEndPopup`에서 경험치 획득량을 보상과 함께 표시 가능
3. **일괄 처리**: 여러 광석을 파괴해도 마지막에 한 번만 처리 → 성능상 이점 (미미함)

**단점:**
1. **즉시 피드백 부족**: 광석 파괴 시점이 아닌 세션 종료 시점에 경험치 지급 → 피드백 지연
2. **구조 변경 필요**: 현재는 즉시 지급 구조인데, 경험치만 세션 종료 시 지급 → 일관성 저하
3. **임시 데이터 관리**: 세션 동안 경험치를 임시로 저장할 필드 필요 (`SessionController._sessionExp`)
4. **세션 재시작 시 초기화**: `RestartSessionFromPopup()`에서 임시 경험치 초기화 필요

**현재 구조와의 호환성:**
- ⚠️ **보통 호환**: 보상은 즉시 지급, 경험치는 세션 종료 시 지급 → 일관성 저하
- ⚠️ **중간 변경**: `SessionController`에 임시 저장 필드 추가 필요
- ⚠️ **초기화 로직 추가**: 세션 재시작 시 임시 경험치 초기화 필요

---

### 5.3 하이브리드 방식 (즉시 지급 + 세션 종료 시 집계)

**구현 위치:**
- 즉시 지급: `Ore.OnBroken()` 내부
- 집계: `SessionController.EndSession()` 내부

**흐름:**
```
광석 파괴
  ↓
Ore.OnBroken()
  ↓
RewardManager.Instance.Add()  // 보상 즉시 지급
ExpManager.Instance.AddExp()  // 경험치 즉시 지급
SessionController._sessionExpGained += expAmount  // 세션 집계용 (표시만)
  ↓
세션 종료
  ↓
SessionController.EndSession()
  ↓
SessionEndPopup.Show()  // "이번 세션에서 얻은 경험치: _sessionExpGained" 표시
```

**장점:**
1. **즉시 피드백**: 광석 파괴 시점에 경험치 즉시 지급
2. **세션 집계**: 세션 종료 시 "이번 세션에서 얻은 경험치" 표시 가능
3. **구조 일관성**: 보상과 경험치 모두 즉시 지급 → 일관성 유지

**단점:**
1. **중복 집계**: `ExpManager`에 지급 + `SessionController`에 집계 → 약간의 중복
2. **임시 데이터 관리**: 세션 집계용 필드 필요 (`SessionController._sessionExpGained`)

**현재 구조와의 호환성:**
- ✅ **매우 호환**: 즉시 지급 방식과 동일하되, 세션 집계만 추가
- ✅ **최소 변경**: `Ore.OnBroken()`에 경험치 지급 추가 + `SessionController`에 집계 필드 추가

---

### 5.4 추천 방안

**추천: 즉시 경험치 지급 방식 (후보 1)**

**이유:**
1. **구조 일관성**: 현재 보상 지급 구조와 완벽히 일치
2. **구현 단순**: 최소한의 코드 변경으로 구현 가능
3. **즉시 피드백**: 플레이어 경험 향상
4. **세션 집계 필요 시**: 하이브리드 방식으로 확장 가능 (나중에 추가)

**세션 종료 시 경험치 표시가 필요한 경우:**
- 하이브리드 방식 채택
- `SessionController`에 `_sessionExpGained` 필드 추가
- `Ore.OnBroken()`에서 경험치 지급 + 집계
- `SessionController.EndSession()`에서 집계값을 `SessionEndPopup`에 전달

---

## 6. 데이터 구조 관점 코멘트

### 6.1 옵션 1: 광석 ScriptableObject에 필드로 넣는 방식

**구조:**
```csharp
[CreateAssetMenu(fileName = "OreData", menuName = "Pulseforge/Ore Data")]
public class OreData : ScriptableObject
{
    public RewardType rewardType;
    public int rewardAmount;
    public int expAmount;  // 경험치 추가
    public float maxHp;
    // ...
}
```

**장점:**
1. **데이터와 로직 분리**: 광석 데이터를 ScriptableObject로 관리 → 로직과 분리
2. **재사용성**: 같은 데이터를 여러 프리팹에서 참조 가능
3. **디자이너 친화적**: ScriptableObject 에셋으로 관리 → 디자이너가 쉽게 수정 가능
4. **확장성**: 새로운 광석 타입 추가 시 ScriptableObject만 생성하면 됨

**단점:**
1. **현재 구조와 불일치**: 현재는 MonoBehaviour 필드로 직접 관리 → 구조 변경 필요
2. **프리팹 구조 변경**: `Ore` 프리팹이 ScriptableObject를 참조하도록 변경 필요
3. **초기 구현 비용**: ScriptableObject 시스템 구축 필요

**현재 프로젝트 스타일과의 호환성:**
- ⚠️ **낮음**: 현재는 MonoBehaviour 필드로 직접 관리하는 스타일
- ⚠️ **구조 변경 필요**: 기존 프리팹 구조 변경 필요

---

### 6.2 옵션 2: 광석 MonoBehaviour에 직렬화 필드로 두는 방식 (현재 방식)

**구조:**
```csharp
public class Ore : MonoBehaviour
{
    [SerializeField] private RewardType rewardType = RewardType.Crystal;
    [SerializeField] private int rewardAmount = 1;
    [SerializeField] private int expAmount = 10;  // 경험치 추가
    // ...
}
```

**장점:**
1. **현재 구조와 완벽 일치**: 기존 `rewardType`, `rewardAmount` 필드와 동일한 패턴
2. **구현 단순**: 필드만 추가하면 됨 → 최소 변경
3. **프리팹 단위 설정**: 각 프리팹 인스턴스에서 개별 설정 가능
4. **런타임 변경 가능**: 필요 시 런타임에 경험치 값 변경 가능

**단점:**
1. **데이터 중복**: 같은 타입의 광석이라도 프리팹마다 설정 필요 (재사용성 낮음)
2. **관리 복잡도**: 광석 종류가 많아지면 프리팹 관리가 복잡해짐
3. **디자이너 친화성 낮음**: ScriptableObject보다는 덜 직관적

**현재 프로젝트 스타일과의 호환성:**
- ✅ **매우 높음**: 현재 보상 관리 방식과 완벽히 일치
- ✅ **최소 변경**: 필드만 추가하면 됨

---

### 6.3 옵션 3: RewardManager 쪽에서 "광석 타입 → 경험치"를 매핑으로 관리하는 방식

**구조:**
```csharp
public class RewardManager : MonoBehaviour
{
    // 보상 타입 → 경험치 매핑
    private static readonly Dictionary<RewardType, int> ExpTable = new()
    {
        { RewardType.Crystal, 10 },
        { RewardType.Gold, 5 },
        { RewardType.Shard, 3 },
    };
    
    public static int GetExpForReward(RewardType type)
    {
        return ExpTable.TryGetValue(type, out var exp) ? exp : 0;
    }
}
```

**장점:**
1. **중앙 집중식 관리**: 경험치 값을 한 곳에서 관리 → 수정 용이
2. **보상 타입 기반 자동 계산**: 광석의 `rewardType`만으로 경험치 자동 계산
3. **광석 프리팹 수정 불필요**: 기존 프리팹 구조 변경 없이 구현 가능
4. **일관성**: 같은 보상 타입은 항상 같은 경험치 → 일관성 유지

**단점:**
1. **유연성 부족**: 같은 보상 타입이라도 광석마다 다른 경험치를 주고 싶을 때 제약
2. **의존성 증가**: `RewardManager`가 경험치까지 관리하게 됨 (책임 분리 위반 가능성)
3. **보상 타입과 경험치 강결합**: 보상 타입이 경험치를 결정 → 확장성 제약

**현재 프로젝트 스타일과의 호환성:**
- ⚠️ **보통**: 현재는 광석이 직접 보상 값을 보유하는 구조
- ⚠️ **의존성 주의**: `RewardManager`와 `ExpManager` 간 의존성 관리 필요

---

### 6.4 추천 방안

**추천: 옵션 2 (MonoBehaviour 직렬화 필드)**

**이유:**
1. **현재 구조와 완벽 일치**: 기존 `rewardType`, `rewardAmount` 필드와 동일한 패턴
2. **구현 단순**: 필드만 추가하면 됨 → 최소 변경
3. **유연성**: 광석별로 다른 경험치 값 설정 가능
4. **일관성**: 보상 관리 방식과 동일한 패턴 → 코드 일관성 유지

**구현 예시:**
```csharp
[Header("Reward")]
[SerializeField] private RewardType rewardType = RewardType.Crystal;
[SerializeField] private int rewardAmount = 1;

[Header("Experience")]
[SerializeField] private int expAmount = 10;  // 추가
```

**대안 (필요 시):**
- 광석 종류가 많아지고 데이터 관리가 복잡해지면, 나중에 ScriptableObject로 전환 가능
- 현재는 단순한 구조가 더 적합

---

## 부록: Hook 지점 비교표

| 후보 | 위치 | 장점 | 단점 | 현재 구조 호환성 |
|------|------|------|------|-----------------|
| **후보 1** | `Ore.OnBroken()` | 일관성, 즉시 피드백, 단순 | 세션 집계 어려움 | ✅ 매우 높음 |
| **후보 2** | `RewardManager.Add()` | 중앙 집중, 자동 계산 | 의존성 증가, 유연성 저하 | ⚠️ 보통 |
| **후보 3** | `SessionController.EndSession()` | 세션 집계 용이 | 즉시 피드백 부족, 구조 변경 | ⚠️ 보통 |

---

**문서 버전:** 1.0  
**마지막 업데이트:** 2025년



















