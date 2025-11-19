using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// Ore(광석)를 원형 범위 안에 스폰/유지하는 스포너.
    /// - 최소 활성 개수 유지 (Min Active Count)
    /// - 하드캡 (Hard Cap)
    /// - Grid / Scatter / Hybrid 배치
    /// - SessionController의 세션 시작/종료 이벤트에 반응
    /// </summary>
    public class OreSpawner : MonoBehaviour
    {
        // ---------- 인스펙터 ----------

        [Header("Spawn Settings")]
        public GameObject orePrefab;
        [Tooltip("스폰 범위 반경(월드 유닛).")]
        public float radius = 5f;

        [Tooltip("항상 유지하고 싶은 최소 Ore 개수.")]
        public int minActiveCount = 8;

        [Tooltip("씬에서 동시에 존재할 수 있는 Ore의 절대 최대 개수.")]
        public int hardCap = 200;

        public enum SpawnMode
        {
            Grid,
            Scatter,
            Hybrid
        }

        [Header("Placement Mode")]
        public SpawnMode spawnMode = SpawnMode.Hybrid;

        [Tooltip("현재 활성 개수가 이 값보다 작을 때는 Grid, 이상일 때는 Scatter 모드로 전환.")]
        public int hybridThreshold = 24;

        [Header("Grid Placement")]
        [Tooltip("Grid 모드에서 사용하는 셀 크기.")]
        public float cellSize = 1f;

        [Range(0f, 0.5f)]
        [Tooltip("Grid 좌표에서 얼마나 랜덤으로 위치를 흔들지 비율(0~0.5).")]
        public float gridJitter = 0.12f;

        [Header("Scatter Placement")]
        [Tooltip("다른 Ore와 이 거리 미만으로 겹치지 않도록 시도.")]
        public float noOverlapRadius = 0.35f;

        [Tooltip("NoOverlap 조건을 만족하는 위치를 찾기 위해 시도할 최대 횟수.")]
        public int scatterMaxTries = 12;

        [Tooltip("겹침 체크에 사용할 레이어 마스크 (주로 Ore 레이어).")]
        public LayerMask overlapMask = 0;

        [Header("Upgrade Modifiers")]
        [Tooltip("추후 아웃게임 업그레이드에서 추가로 더해질 개수.")]
        public int upgradeFlatBonus = 0;

        [Tooltip("추후 업그레이드에서 곱해질 멀티플라이어.")]
        public float upgradeMultiplier = 1f;

        [Header("Time Ramp (Optional)")]
        [Tooltip("시간이 지나면서 더 많은 Ore를 추가하고 싶을 때 사용.")]
        public bool timeRampEnabled = false;

        [Tooltip("N초마다 Ore를 Add Amount만큼 추가.")]
        public float addEveryNSeconds = 10f;

        public int addAmount = 5;
        public int maxExtra = 50;

        // ---------- 내부 상태 ----------

        SessionController _session;
        float _timer;
        int _extraSpawned; // time ramp로 추가된 개수

        void Awake()
        {
            _session = FindObjectOfType<SessionController>();

            if (_session != null)
            {
                _session.OnSessionStart += HandleSessionStart;
                _session.OnSessionEnd += HandleSessionEnd;
            }
        }

        void OnDestroy()
        {
            if (_session != null)
            {
                _session.OnSessionStart -= HandleSessionStart;
                _session.OnSessionEnd -= HandleSessionEnd;
            }
        }

        void Start()
        {
            // 첫 세션 시작 전에도 한 번 세팅
            HandleSessionStart();
        }

        void Update()
        {
            if (_session == null || !_session.IsRunning)
                return;

            MaintainMinimumCount();

            if (timeRampEnabled)
            {
                _timer += Time.deltaTime;
                if (_timer >= addEveryNSeconds)
                {
                    _timer = 0f;
                    TryAddExtra(addAmount);
                }
            }
        }

        // ---------- 세션 이벤트 ----------

        void HandleSessionStart()
        {
            _timer = 0f;
            _extraSpawned = 0;
            ClearAllOres();
            SpawnInitial();
        }

        void HandleSessionEnd()
        {
            ClearAllOres();
        }

        // ---------- 스폰 로직 ----------

        void SpawnInitial()
        {
            int target = Mathf.RoundToInt(minActiveCount * upgradeMultiplier) + upgradeFlatBonus;
            target = Mathf.Max(0, target);
            target = Mathf.Min(target, hardCap);

            for (int i = 0; i < target; i++)
            {
                SpawnOne();
            }
        }

        void MaintainMinimumCount()
        {
            int current = transform.childCount;
            int targetMin = Mathf.RoundToInt(minActiveCount * upgradeMultiplier) + upgradeFlatBonus;
            targetMin = Mathf.Clamp(targetMin, 0, hardCap);

            int needed = targetMin - current;
            if (needed <= 0)
                return;

            for (int i = 0; i < needed; i++)
            {
                SpawnOne();
            }
        }

        void TryAddExtra(int amount)
        {
            int maxTotal = Mathf.Clamp(minActiveCount + maxExtra, 0, hardCap);
            int current = transform.childCount;

            int canAdd = Mathf.Min(amount, maxTotal - current);
            if (canAdd <= 0)
                return;

            for (int i = 0; i < canAdd; i++)
            {
                SpawnOne();
                _extraSpawned++;
            }
        }

        void ClearAllOres()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        void SpawnOne()
        {
            if (orePrefab == null)
                return;

            Transform parent = _session != null && _session.oreRootOverride != null
                ? _session.oreRootOverride
                : transform;

            Vector2 pos = GetSpawnPosition();
            Instantiate(orePrefab, pos, Quaternion.identity, parent);
        }

        Vector2 GetSpawnPosition()
        {
            switch (spawnMode)
            {
                case SpawnMode.Grid:
                    return GetGridPosition();
                case SpawnMode.Scatter:
                    return GetScatterPosition();
                case SpawnMode.Hybrid:
                    int current = transform.childCount;
                    if (current < hybridThreshold)
                        return GetGridPosition();
                    else
                        return GetScatterPosition();
                default:
                    return GetScatterPosition();
            }
        }

        Vector2 GetGridPosition()
        {
            // 원 안에 들어오는 grid 셀을 대충 골라서 jitter를 준다.
            for (int tries = 0; tries < 12; tries++)
            {
                float maxCell = radius / Mathf.Max(0.001f, cellSize);
                int gx = Random.Range(Mathf.CeilToInt(-maxCell), Mathf.FloorToInt(maxCell) + 1);
                int gy = Random.Range(Mathf.CeilToInt(-maxCell), Mathf.FloorToInt(maxCell) + 1);

                Vector2 basePos = new Vector2(gx, gy) * cellSize;

                if (basePos.magnitude > radius)
                    continue;

                // jitter
                float jitterRange = cellSize * gridJitter;
                basePos.x += Random.Range(-jitterRange, jitterRange);
                basePos.y += Random.Range(-jitterRange, jitterRange);

                return (Vector2)transform.position + basePos;
            }

            // 실패 시 그냥 Scatter 방식으로
            return GetScatterPosition();
        }

        Vector2 GetScatterPosition()
        {
            // NoOverlapRadius가 0이면 단순 Random.insideUnitCircle
            if (noOverlapRadius <= 0f || overlapMask.value == 0)
            {
                return (Vector2)transform.position + Random.insideUnitCircle * radius;
            }

            for (int i = 0; i < scatterMaxTries; i++)
            {
                Vector2 candidate = (Vector2)transform.position + Random.insideUnitCircle * radius;

                if (!Physics2D.OverlapCircle(candidate, noOverlapRadius, overlapMask))
                {
                    return candidate;
                }
            }

            // 여러 번 실패하면 그냥 마지막 위치 리턴
            return (Vector2)transform.position + Random.insideUnitCircle * radius;
        }
    }
}
