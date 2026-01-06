# Ore 스폰 영역 문제 분석 리포트

## 문제 현상
- `OreAmount` 업그레이드를 통해 ore 스폰 개수가 늘어나면 카메라가 줌아웃됨
- 줌아웃된 만큼 스폰 영역도 늘어나고 있음
- **하지만 스폰될 때 카메라를 벗어나서 스폰되는 ore들이 발생**

---

## 원인 분석

### 1. 카메라 줌아웃과 스폰 영역 계산의 타이밍 불일치

#### 문제점
- **MiningCameraZoom.cs**: 카메라 줌아웃은 `zoomDuration` (기본 1.0초) 동안 **부드럽게 진행**됩니다
  - `ZoomRoutine()` 코루틴에서 `Time.deltaTime`으로 점진적으로 `orthographicSize`를 변경
  - 즉, 카메라 크기가 즉시 변경되는 것이 아니라 1초 동안 서서히 변함

- **OreSpawner.cs**: 스폰 영역 계산은 **즉시 현재 카메라 상태를 기준**으로 계산됩니다
  - `TryGetSpawnRect()` 메서드 (354-384줄)에서 `Camera.main.orthographicSize`의 **현재 값**을 사용
  - `ViewportToWorldPoint()`를 호출할 때의 카메라 크기를 기준으로 스폰 영역을 계산

#### 시나리오
1. 업그레이드로 인해 스케일 레벨이 변경됨
2. `MiningCameraZoom.StartZoom()`이 호출되어 줌아웃 시작 (1초 동안 진행)
3. 그 사이 `OreSpawner.HandleRespawn()`이 `respawnInterval` (0.3초)마다 호출됨
4. 리스폰 시점에 카메라는 **아직 줌아웃 중간 상태**일 수 있음
5. 중간 상태의 카메라 크기를 기준으로 스폰 영역을 계산하면, **최종 줌아웃된 크기보다 작은 영역**이 계산될 수 있음
6. 결과적으로 일부 ore가 카메라를 벗어난 위치에 스폰될 수 있음

---

### 2. 클러스터 초기화 타이밍 문제

#### 문제점
- **OreSpawner.cs**의 `EnsureClusters()` 메서드 (322-349줄):
  - `_clustersInitialized` 플래그로 **한 번만 초기화**됨
  - 스폰 영역이 변경되어도 클러스터가 재계산되지 않음

#### 시나리오
1. 초기 스폰 시 작은 스폰 영역 기준으로 클러스터가 생성됨
2. 업그레이드로 인해 카메라가 줌아웃되고 스폰 영역이 커짐
3. 하지만 클러스터는 **이전 작은 영역 기준**으로 유지됨
4. 클러스터 중심이 새로운 큰 영역의 경계 근처에 있으면, 클러스터 내부에서 스폰된 ore가 **카메라를 벗어날 수 있음**

---

### 3. 스폰 영역 계산의 즉시성 문제

#### 문제점
- `TryGetSpawnRect()` 메서드는 **호출 시점의 카메라 상태**를 기준으로 계산
- 카메라가 줌아웃 중일 때는:
  - 중간 상태의 `orthographicSize`를 사용
  - `ViewportToWorldPoint()`가 중간 크기의 카메라를 기준으로 월드 좌표를 계산
  - 결과적으로 **최종 목표 크기보다 작은 스폰 영역**이 계산될 수 있음

#### 코드 위치
```csharp
// OreSpawner.cs 354-370줄
private bool TryGetSpawnRect(out Vector3 center, out Vector2 halfSize)
{
    if (useCameraBounds && Camera.main != null)
    {
        var cam = Camera.main;
        float z = -cam.transform.position.z;
        
        // ⚠️ 이 시점의 cam.orthographicSize를 사용
        Vector3 min = cam.ViewportToWorldPoint(new Vector3(0.08f, 0.18f, z));
        Vector3 max = cam.ViewportToWorldPoint(new Vector3(0.92f, 0.88f, z));
        // ...
    }
}
```

---

### 4. 이벤트 연동 부재

#### 문제점
- **MiningScaleManager.cs**는 `OnScaleChanged` 이벤트를 제공하지만:
  - `OreSpawner`가 이 이벤트를 구독하지 않음
  - `MiningCameraZoom`도 이 이벤트를 구독하지 않음
  - 결과적으로 스케일 레벨 변경 시 스폰 영역과 클러스터를 **즉시 갱신할 수 없음**

#### 현재 동작
- `MiningScaleManager.RecalculateScaleLevel()`에서 스케일 레벨이 변경되면 `OnScaleChanged` 이벤트 발생
- 하지만 `OreSpawner`는 이 이벤트를 듣지 않아서:
  - 클러스터를 무효화하지 않음 (`InvalidateClusters()` 호출 안 됨)
  - 스폰 영역을 재계산하지 않음

---

## 근본 원인 요약

1. **비동기 타이밍 문제**: 카메라 줌아웃은 1초 동안 점진적으로 진행되지만, 스폰 영역 계산은 즉시 현재 카메라 상태를 사용
2. **클러스터 캐싱 문제**: 클러스터가 한 번 초기화되면 스폰 영역이 변경되어도 재계산되지 않음
3. **이벤트 연동 부재**: 스케일 레벨 변경 시 스폰 시스템이 알림을 받지 못함
4. **즉시 계산 방식**: 스폰 영역을 매번 현재 카메라 크기로 계산하므로, 줌아웃 중간 상태의 크기를 사용할 수 있음

---

## 추가 관찰사항

### OreSpawner의 useCameraBounds 설정
- 코드에서 `useCameraBounds = false`로 설정되어 있으면 고정된 `areaSize`를 사용
- 하지만 `useCameraBounds = true`일 때만 문제가 발생하는 것으로 보임
- 실제 인스펙터 설정을 확인해야 함

### MiningCameraZoom의 zoomDelay
- `zoomDelay = 0.2초`로 설정되어 있어, 줌아웃 시작 전 0.2초 대기
- 이 시간 동안 리스폰이 발생하면 이전 카메라 크기를 기준으로 스폰 영역이 계산됨

---

## 결론

**주요 원인**: 카메라 줌아웃이 점진적으로 진행되는 동안, 스폰 시스템이 중간 상태의 카메라 크기를 기준으로 스폰 영역을 계산하여, 최종 줌아웃된 크기보다 작은 영역이 계산되고, 그 결과 일부 ore가 카메라를 벗어난 위치에 스폰되는 문제가 발생합니다.

**부가 원인**: 클러스터가 한 번 초기화되면 재계산되지 않아, 이전 작은 영역 기준의 클러스터를 사용하면서 추가적인 문제가 발생할 수 있습니다.










