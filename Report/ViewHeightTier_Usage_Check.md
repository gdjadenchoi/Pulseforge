# ViewHeightTier 사용 현황 확인 리포트

## 확인 결과

### ❌ **MiningCameraZoom과 OreSpawner는 MiningScaleManager의 viewHeightTiers를 읽지 않습니다**

---

## 상세 분석

### 1. MiningScaleManager의 ViewHeightTier 제공

**위치**: `Assets/Systems/MiningScaleManager.cs`

```csharp
// 40-45줄
[Header("Spawn Area (for Zoom tier table)")]
[Tooltip("스케일 레벨별 스폰 영역 높이(월드 단위). 예: 0=10, 1=13, 2=16")]
public List<ViewHeightTier> viewHeightTiers = new List<ViewHeightTier>()
{
    new ViewHeightTier(){ scaleLevel = 0, spawnWorldHeight = 10f },
    new ViewHeightTier(){ scaleLevel = 1, spawnWorldHeight = 13f },
    new ViewHeightTier(){ scaleLevel = 2, spawnWorldHeight = 16f },
};
```

**제공 메서드**:
- `GetFinalSpawnWorldHeight()` (121줄): 현재 스케일 레벨에 맞는 spawnWorldHeight 반환
- `GetFinalSpawnAreaSize(float fixedAspect)` (142줄): 고정 비율을 고려한 스폰 영역 크기 반환

---

### 2. MiningCameraZoom의 실제 사용

**위치**: `Assets/Systems/MiningCameraZoom.cs`

**사용하는 테이블**:
- **자체 `viewHeightTiers` 배열 사용** (104줄)
- `TryGetSpawnHeightFromTier()` 메서드 (244줄)에서 **자체 배열을 직접 참조**

```csharp
// 244-283줄
private bool TryGetSpawnHeightFromTier(int scaleLevel, out float spawnWorldHeight)
{
    spawnWorldHeight = 0f;
    
    if (viewHeightTiers == null || viewHeightTiers.Length == 0)  // ⚠️ 자체 배열 확인
        return false;
    
    // 자체 viewHeightTiers 배열을 순회
    for (int i = 0; i < viewHeightTiers.Length; i++)
    {
        if (viewHeightTiers[i].scaleLevel == scaleLevel)  // ⚠️ 자체 배열 사용
        {
            spawnWorldHeight = Mathf.Max(0.1f, viewHeightTiers[i].spawnWorldHeight);
            return true;
        }
    }
    // ...
}
```

**확인 사항**:
- ❌ `MiningScaleManager.Instance.viewHeightTiers` 참조 없음
- ❌ `MiningScaleManager.Instance.GetFinalSpawnWorldHeight()` 호출 없음
- ✅ 자체 `viewHeightTiers` 배열만 사용

---

### 3. OreSpawner의 실제 사용

**위치**: `Assets/Systems/OreSpawner.cs`

**사용하는 테이블**:
- **자체 `viewHeightTiers` 리스트 사용** (61줄)
- `GetSpawnWorldHeightByScaleLevel()` 메서드 (361줄)에서 **자체 리스트를 직접 참조**

```csharp
// 361-380줄
private float GetSpawnWorldHeightByScaleLevel(int scaleLevel)
{
    if (viewHeightTiers == null || viewHeightTiers.Count == 0)  // ⚠️ 자체 리스트 확인
        return 10f;
    
    float chosen = viewHeightTiers[0].spawnWorldHeight;  // ⚠️ 자체 리스트 사용
    int chosenLevel = int.MinValue;
    
    for (int i = 0; i < viewHeightTiers.Count; i++)  // ⚠️ 자체 리스트 순회
    {
        var t = viewHeightTiers[i];
        if (t.scaleLevel <= scaleLevel && t.scaleLevel > chosenLevel)
        {
            chosenLevel = t.scaleLevel;
            chosen = t.spawnWorldHeight;
        }
    }
    
    return Mathf.Max(1f, chosen);
}
```

**확인 사항**:
- ❌ `MiningScaleManager.Instance.viewHeightTiers` 참조 없음
- ❌ `MiningScaleManager.Instance.GetFinalSpawnWorldHeight()` 호출 없음
- ✅ 자체 `viewHeightTiers` 리스트만 사용

---

## 결론

### 현재 상태

1. **MiningScaleManager**는 `viewHeightTiers`를 제공하고 `GetFinalSpawnWorldHeight()` 메서드도 제공하지만
2. **MiningCameraZoom**과 **OreSpawner**는 이를 사용하지 않음
3. 각 컴포넌트가 **자체 ViewHeightTier 테이블**을 가지고 있음

### 문제점

- **데이터 이중 관리**: 동일한 데이터가 3곳에서 관리됨
  - MiningScaleManager.viewHeightTiers
  - MiningCameraZoom.viewHeightTiers
  - OreSpawner.viewHeightTiers
- **동기화 부재**: MiningScaleManager의 테이블을 수정해도 다른 컴포넌트에 반영되지 않음
- **불일치 위험**: 각 컴포넌트의 테이블이 서로 다르면 카메라와 스폰 영역 불일치 발생

### 실제 사용 흐름

```
MiningScaleManager.viewHeightTiers
  ↓ (제공하지만 사용되지 않음)
  
MiningCameraZoom.viewHeightTiers (자체 테이블 사용)
  ↓
TryGetSpawnHeightFromTier() → 자체 배열 참조

OreSpawner.viewHeightTiers (자체 테이블 사용)
  ↓
GetSpawnWorldHeightByScaleLevel() → 자체 리스트 참조
```

---

## 권장 사항

현재 구조에서는 **MiningScaleManager의 viewHeightTiers가 실제로 사용되지 않습니다**. 

만약 중앙 집중식 관리가 목적이라면:
1. MiningCameraZoom과 OreSpawner가 `MiningScaleManager.Instance.GetFinalSpawnWorldHeight()`를 호출하도록 수정
2. 또는 MiningScaleManager의 viewHeightTiers를 제거하고 각 컴포넌트가 자체 테이블을 유지

현재는 **각 컴포넌트가 독립적으로 ViewHeightTier 테이블을 관리**하고 있는 상태입니다.







