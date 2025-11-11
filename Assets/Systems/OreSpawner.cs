using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 한 세션 동안 활성 Ore 수를 유지(리스폰)하고,
    /// 수량에 따라 Grid/Scatter를 자동 전환(하이브리드)하는 스포너.
    /// </summary>
    public class OreSpawner : MonoBehaviour
    {
        // ====== 기본 스폰 설정 ======
        [Header("Spawn Settings")]
        [Tooltip("생성할 Ore 프리팹")]
        public GameObject orePrefab;

        [Tooltip("씬 중앙(스포너 기준) 원형 영역 반지름")]
        public float radius = 5f;

        [Tooltip("세션 시작 시 보장할 최소 활성 수량 (리스폰 유지 기준)")]
        public int minActiveCount = 12;

        [Tooltip("강제 상한 (업그레이드/시간 램프 포함 후 캡)")]
        public int hardCap = 200;

        // ====== 배치 모드 ======
        public enum SpawnMode { GridOnly, ScatterOnly, Hybrid }
        [Header("Placement Mode")]
        public SpawnMode spawnMode = SpawnMode.Hybrid;

        [Tooltip("하이브리드 전환 임계치 (활성 수량이 이 값 미만이면 Grid, 이상이면 Scatter)")]
        public int hybridThreshold = 24;

        [Header("Grid Placement")]
        [Tooltip("타일(셀) 크기(월드 단위). Ore 크기에 맞춰 0.5~1.2 정도 추천")]
        public float cellSize = 1.0f;

        [Tooltip("그리드 배치 시 약간의 랜덤 흔들림(0이면 완전 격자)")]
        [Range(0f, 0.49f)] public float gridJitter = 0.12f;

        [Header("Scatter Placement")]
        [Tooltip("스캐터 충돌 반경(겹침 방지). Ore 반지름보다 약간 작거나 비슷하게")]
        public float noOverlapRadius = 0.35f;

        [Tooltip("스캐터 시 포지션 재시도 횟수")]
        public int scatterMaxTries = 12;

        [Tooltip("오버랩 검사에 사용할 레이어마스크(없으면 모든 충돌체 검사)")]
        public LayerMask overlapMask = default;

        // ====== 업그레이드 계수 ======
        [Header("Upgrade Modifiers")]
        [Tooltip("고정 보정치 (예: 업그레이드로 +n)")]
        public int upgradeFlatBonus = 0;

        [Tooltip("배수 보정 (예: 업그레이드로 x1.25)")]
        public float upgradeMultiplier = 1f;

        // ====== 시간 램프(테스트/메타 진행용) ======
        [Header("Time Ramp (Optional)")]
        public bool timeRampEnabled = false;

        [Tooltip("N초마다 AddAmount 만큼 최대치 상향(SoftCap을 키우는 개념)")]
        public float addEveryNSeconds = 10f;

        [Tooltip("N초마다 늘리는 양")]
        public int addAmount = 5;

        [Tooltip("시간 램프로 추가할 수 있는 최대치(SoftCap 누적 상한)")]
        public int maxExtra = 50;

        // ====== 런타임 상태 ======
        private SessionController _session;       // IsRunning 만 사용
        private readonly List<Transform> _pool = new(); // 자식 중 살아있는 Ore Transform 캐시
        private float _timer;
        private int _softCapExtra;                // 시간 램프로 늘어난 추가치 누적

        private int EffectiveTargetCount =>
            Mathf.Min(
                Mathf.RoundToInt(minActiveCount * upgradeMultiplier) + upgradeFlatBonus + _softCapExtra,
                hardCap
            );

        private void Awake()
        {
            _pool.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                var t = transform.GetChild(i);
                if (t != null) _pool.Add(t);
            }
        }

        private void Start()
        {
            _session = FindObjectOfType<SessionController>();
            TopUpToTarget(forceGrid: true);
        }

        private void Update()
        {
            if (_session != null && !_session.IsRunning) return;

            if (timeRampEnabled)
            {
                _timer += Time.deltaTime;
                if (_timer >= addEveryNSeconds)
                {
                    _timer = 0f;
                    _softCapExtra = Mathf.Min(_softCapExtra + addAmount, maxExtra);
                }
            }

            PruneNulls();
            TopUpToTarget();
        }

        // 목표 수량까지 보충
        private void TopUpToTarget(bool forceGrid = false)
        {
            int target = EffectiveTargetCount;
            int active = _pool.Count;
            int need = target - active;
            if (need <= 0) return;

            SpawnMode mode = spawnMode;
            if (mode == SpawnMode.Hybrid)
                mode = (active < hybridThreshold || forceGrid) ? SpawnMode.GridOnly : SpawnMode.ScatterOnly;

            switch (mode)
            {
                case SpawnMode.GridOnly: SpawnGrid(need); break;
                case SpawnMode.ScatterOnly: SpawnScatter(need); break;
                default: SpawnGrid(need); break;
            }
        }

        // === Grid 배치 ===
        private void SpawnGrid(int toSpawn)
        {
            var candidates = BuildGridCandidates();
            Shuffle(candidates);

            int spawned = 0;
            foreach (var pos in candidates)
            {
                if (TrySpawnAt(pos))
                {
                    spawned++;
                    if (spawned >= toSpawn) break;
                }
            }

            // 후보가 모자라면 스캐터로 보충
            if (spawned < toSpawn)
                SpawnScatter(toSpawn - spawned);
        }

        private List<Vector2> BuildGridCandidates()
        {
            var list = new List<Vector2>(256);
            float half = radius;
            int xn = Mathf.Max(1, Mathf.CeilToInt((half * 2f) / cellSize));
            int yn = xn;
            Vector2 origin = transform.position;

            for (int yi = 0; yi < yn; yi++)
                for (int xi = 0; xi < xn; xi++)
                {
                    float px = -half + (xi + 0.5f) * cellSize;
                    float py = -half + (yi + 0.5f) * cellSize;
                    var p = new Vector2(origin.x + px, origin.y + py);

                    if ((p - origin).sqrMagnitude <= radius * radius)
                    {
                        if (gridJitter > 0f)
                            p += Random.insideUnitCircle * gridJitter;
                        list.Add(p);
                    }
                }
            return list;
        }

        // === Scatter 배치 ===
        private void SpawnScatter(int toSpawn)
        {
            Vector2 center = transform.position;

            for (int i = 0; i < toSpawn; i++)
            {
                bool placed = false;

                for (int tries = 0; tries < scatterMaxTries; tries++)
                {
                    Vector2 p = center + Random.insideUnitCircle * radius;
                    if (IsPositionFree(p))
                    {
                        TrySpawnAt(p);
                        placed = true;
                        break;
                    }
                }

                if (!placed) // 끝내 못찾으면 한 번은 그냥 배치(밀집 허용)
                {
                    Vector2 p = center + Random.insideUnitCircle * radius;
                    TrySpawnAt(p, checkOverlap: false);
                }
            }
        }

        // === 공통 ===
        private bool TrySpawnAt(Vector2 pos, bool checkOverlap = true)
        {
            if (orePrefab == null) return false;
            if (checkOverlap && !IsPositionFree(pos)) return false;

            var go = Instantiate(orePrefab, pos, Quaternion.identity, transform);
            _pool.Add(go.transform);
            return true;
        }

        private bool IsPositionFree(Vector2 pos)
        {
            if (noOverlapRadius <= 0f) return true;
            var hit = Physics2D.OverlapCircle(pos, noOverlapRadius, overlapMask);
            return hit == null;
        }

        private void PruneNulls()
        {
            for (int i = _pool.Count - 1; i >= 0; i--)
                if (_pool[i] == null) _pool.RemoveAt(i);
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int j = Random.Range(i, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, radius);

            if (spawnMode == SpawnMode.GridOnly || spawnMode == SpawnMode.Hybrid)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.15f);
                float half = radius;
                int xn = Mathf.Max(1, Mathf.CeilToInt((half * 2f) / Mathf.Max(0.01f, cellSize)));
                int yn = xn;
                Vector2 origin = transform.position;

                for (int yi = 0; yi < yn; yi++)
                    for (int xi = 0; xi < xn; xi++)
                    {
                        float px = -half + (xi + 0.5f) * cellSize;
                        float py = -half + (yi + 0.5f) * cellSize;
                        var p = new Vector2(origin.x + px, origin.y + py);
                        if ((p - origin).sqrMagnitude <= radius * radius)
                            Gizmos.DrawSphere(p, 0.05f);
                    }
            }
        }
    }
}
