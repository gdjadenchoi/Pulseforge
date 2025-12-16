using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 광석 스폰/리스폰 담당
    /// - ScaleLevel에 따라 Spawn World Height(=스폰 영역 높이)를 사용
    /// - Spawn World Height + FixedAspect + SafePercent로 Rect 스폰영역 계산
    ///
    /// 핵심:
    /// 1) SpawnWorldHeight는 MiningScaleManager.GetFinalSpawnWorldHeight()를 "우선" 사용(단일 소스)
    /// 2) 현재 계산된 스폰 Rect를 외부에서 조회할 수 있도록 공개 API 제공
    /// 3) 스폰 시 "팝 애니메이션" 지원 (0 -> overshoot -> 1)
    /// 4) (추가) 세션 시작 초기 스폰을 "순차 스폰"으로 연출 가능
    /// </summary>
    public class OreSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private GameObject orePrefab;

        [Header("Spawn Flow")]
        [SerializeField] private bool autoSpawnOnAwake = false;

        [Tooltip("세션 시작 시 즉시 깔리는 수(연출 포함). 보통 TargetCount 이상으로 깔고 싶으면 SessionController에서 조절")]
        [SerializeField] private int initialCount = 12;

        [Tooltip("유지하려는 기본 목표 수")]
        [SerializeField] private int baseTargetCount = 12;

        [SerializeField] private bool respawnToTargetCount = true;
        [SerializeField] private float respawnInterval = 0.3f;
        [SerializeField] private int batchMax = 3;

        [Header("Target Count: Upgrade (optional)")]
        [SerializeField] private bool useUpgradeOreAmount = true;
        [SerializeField] private string upgradeIdOreAmount = "OreAmount";
        [SerializeField] private int amountPerUpgradeLevel = 5;

        [Header("Target Count: ScaleLevel (optional)")]
        [SerializeField] private bool addByScaleLevel = false;
        [SerializeField] private int extraPerScaleLevel = 4;

        [Header("Spawn Area Mode")]
        [Tooltip("true면 Rect 기반 스폰(추천). false면 카메라 뷰포트 기준 스폰")]
        [SerializeField] private bool useRectArea = true;

        [Tooltip("Rect 중심 오프셋(월드)")]
        [SerializeField] private Vector2 areaOffset = Vector2.zero;

        [Header("Fixed Gameplay Aspect / Safe Area")]
        [Tooltip("9:16 = 0.5625 (세로 고정 플레이 가정)")]
        [SerializeField] private float fixedAspect = 0.5625f;

        [Tooltip("상단 UI 고려 여유(비율)")]
        [Range(0f, 0.4f)]
        [SerializeField] private float topSafePercent = 0.10f;

        [Tooltip("하단 UI 고려 여유(비율)")]
        [Range(0f, 0.4f)]
        [SerializeField] private float bottomSafePercent = 0.10f;

        [Header("SpawnWorldHeight Source")]
        [Tooltip("true면 SpawnWorldHeight는 MiningScaleManager.GetFinalSpawnWorldHeight()를 우선 사용(단일 소스). false면 아래 viewHeightTiers 사용")]
        [SerializeField] private bool preferMiningScaleManagerHeight = true;

        [Header("View Height Tier Table (Fallback)")]
        [SerializeField] private List<ViewHeightTier> viewHeightTiers = new List<ViewHeightTier>()
        {
            new ViewHeightTier(){ scaleLevel = 0, spawnWorldHeight = 10f },
            new ViewHeightTier(){ scaleLevel = 1, spawnWorldHeight = 13f },
            new ViewHeightTier(){ scaleLevel = 2, spawnWorldHeight = 16f },
        };

        [Serializable]
        public struct ViewHeightTier
        {
            public int scaleLevel;
            public float spawnWorldHeight;
        }

        [Header("Spawn Distribution")]
        [SerializeField] private bool useClusterDistribution = true;
        [SerializeField] private int clusterCount = 4;
        [SerializeField] private Vector2 clusterRadiusRange = new Vector2(1f, 2f);
        [Range(0f, 1f)]
        [SerializeField] private float clusterSpawnChance = 0.7f;

        [Header("Distance / Attempts")]
        [SerializeField] private float minDistance = 0.6f;
        [SerializeField] private int maxAttemptsPerSpawn = 32;

        [Header("Spawn Pop Animation")]
        [Tooltip("스폰 시 0 -> overshoot -> 1 팝 애니메이션을 적용")]
        [SerializeField] private bool enableSpawnPop = true;

        [Tooltip("팝 애니메이션 전체 길이(초)")]
        [SerializeField] private float spawnPopDuration = 0.12f;

        [Tooltip("오버슈트 크기(1.0 = 오버슈트 없음, 1.15 = 살짝 튀어나옴)")]
        [SerializeField] private float spawnPopOvershoot = 1.15f;

        [Tooltip("팝 애니메이션 시작 전에 랜덤 딜레이(뾰뾰뾱 느낌). 0이면 즉시 시작")]
        [SerializeField] private Vector2 spawnPopDelayRange = new Vector2(0f, 0.08f);

        [Tooltip("팝 중에는 클릭/충돌이 어색할 수 있어서, Collider2D를 잠깐 꺼둘지 여부")]
        [SerializeField] private bool disableColliderWhilePopping = true;

        [Tooltip("팝 스케일 커브(0~1). 기본은 EaseOut 느낌")]
        [SerializeField] private AnimationCurve spawnPopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Initial Spawn Sequencing (Intro)")]
        [Tooltip("세션 시작 초기 스폰을 한 번에 생성하지 않고, 빠르게 '순차'로 생성(뾰뾰뾱)")]
        [SerializeField] private bool sequenceInitialSpawn = true;

        [Tooltip("순차 스폰 간격(초). 0.02~0.06 추천")]
        [SerializeField] private float initialSpawnInterval = 0.03f;

        [Tooltip("순차 스폰 1틱당 몇 개를 생성할지(1 추천). 2 이상이면 더 빨라짐")]
        [SerializeField] private int initialSpawnBatch = 1;

        [Tooltip("순차 스폰 간격에 랜덤 지터 추가(초). 0이면 일정한 리듬")]
        [SerializeField] private float initialSpawnJitter = 0.01f;

        // Runtime
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Coroutine _respawnRoutine;
        private Coroutine _initialRoutine;
        private bool _isActive;

        // Cluster cache
        private bool _clustersInitialized;
        private readonly List<Cluster> _clusters = new List<Cluster>();

        private MiningScaleManager _scaleManager;
        private int _lastScaleLevel = int.MinValue;

        private struct Cluster
        {
            public Vector2 center;
            public float radius;
        }

        private void Awake()
        {
            _scaleManager = MiningScaleManager.Instance;
            if (_scaleManager != null)
                _lastScaleLevel = _scaleManager.CurrentScaleLevel;
        }

        private void Start()
        {
            if (autoSpawnOnAwake)
                StartFresh();
        }

        private void OnEnable()
        {
            if (_scaleManager == null) _scaleManager = MiningScaleManager.Instance;

            if (_scaleManager != null)
            {
                _scaleManager.OnScaleChanged -= HandleScaleChanged;
                _scaleManager.OnScaleChanged += HandleScaleChanged;
            }
        }

        private void OnDisable()
        {
            if (_scaleManager != null)
                _scaleManager.OnScaleChanged -= HandleScaleChanged;
        }

        /// <summary>
        /// SessionController 호환용: 스폰 중지 + 월드 정리
        /// </summary>
        public void PauseAndClear()
        {
            StopSpawner();
            ClearWorld();
        }

        public void StartFresh()
        {
            StopSpawner(); // 루틴/플래그 정리
            ClearWorld();
            InvalidateClusters();
            EnsureClusters();

            int target = GetTargetCount();
            int initialSpawnCount = Mathf.Max(initialCount, target);

            // 초기 스폰을 연출(순차)로 할지, 기존처럼 즉시 깔지 결정
            if (_initialRoutine != null) StopCoroutine(_initialRoutine);

            if (sequenceInitialSpawn && initialSpawnCount > 0)
            {
                _initialRoutine = StartCoroutine(CoSpawnInitialSequenced(initialSpawnCount));
            }
            else
            {
                SpawnInitialImmediate(initialSpawnCount);
                BeginRespawnLoop();
            }
        }

        public void StopSpawner()
        {
            _isActive = false;

            if (_initialRoutine != null) StopCoroutine(_initialRoutine);
            _initialRoutine = null;

            if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        /// <summary>
        /// 인트로 연출용(1개만 깔기 등)
        /// </summary>
        public void SpawnPreview(int count = 1)
        {
            StopSpawner();
            ClearWorld();
            InvalidateClusters();
            EnsureClusters();
            SpawnInitialImmediate(Mathf.Max(0, count));
        }

        private void BeginRespawnLoop()
        {
            _isActive = true;

            if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
            _respawnRoutine = StartCoroutine(RespawnLoop());
        }

        private IEnumerator CoSpawnInitialSequenced(int totalCount)
        {
            int remaining = totalCount;

            int batch = Mathf.Max(1, initialSpawnBatch);
            float interval = Mathf.Max(0f, initialSpawnInterval);
            float jitter = Mathf.Max(0f, initialSpawnJitter);

            while (remaining > 0)
            {
                int n = Mathf.Min(batch, remaining);
                for (int i = 0; i < n; i++)
                    TrySpawnOne();

                remaining -= n;

                // 다음 틱까지 대기(뾰뾰뾱 템포)
                if (remaining > 0 && interval > 0f)
                {
                    float wait = interval;
                    if (jitter > 0f)
                        wait += UnityEngine.Random.Range(-jitter, jitter);

                    if (wait > 0.0001f)
                        yield return new WaitForSeconds(wait);
                    else
                        yield return null;
                }
                else
                {
                    yield return null;
                }
            }

            _initialRoutine = null;

            // 초기 생성이 끝난 뒤에 리스폰 루프 시작
            BeginRespawnLoop();
        }

        private IEnumerator RespawnLoop()
        {
            while (_isActive)
            {
                HandleRespawn();
                yield return new WaitForSeconds(respawnInterval);
            }
        }

        private void HandleRespawn()
        {
            if (!respawnToTargetCount) return;

            CleanupNulls();
            SyncScaleLevelChangeIfNeeded();

            int current = _spawned.Count;
            int target = GetTargetCount();

            if (current >= target) return;

            int need = Mathf.Min(batchMax, target - current);
            for (int i = 0; i < need; i++)
                TrySpawnOne();
        }

        private int GetTargetCount()
        {
            int total = baseTargetCount;

            if (useUpgradeOreAmount)
            {
                var up = UpgradeManager.Instance;
                if (up != null)
                {
                    int lv = up.GetLevel(upgradeIdOreAmount);
                    total += lv * amountPerUpgradeLevel;
                }
            }

            if (addByScaleLevel && extraPerScaleLevel > 0)
            {
                var sm = MiningScaleManager.Instance;
                if (sm != null)
                    total += Mathf.Max(0, sm.CurrentScaleLevel) * extraPerScaleLevel;
            }

            return Mathf.Max(0, total);
        }

        private void SpawnInitialImmediate(int count)
        {
            if (orePrefab == null) return;

            for (int i = 0; i < count; i++)
                TrySpawnOne();
        }

        private void TrySpawnOne()
        {
            if (orePrefab == null) return;

            if (!TryGetSpawnRect(out Vector3 center, out Vector2 halfSize))
                return;

            if (!TryFindSpawnPosition(center, halfSize, out Vector3 spawnPos))
                return;

            var go = Instantiate(orePrefab, spawnPos, Quaternion.identity, transform);
            _spawned.Add(go);

            // 스폰 팝 연출
            if (enableSpawnPop && go != null && spawnPopDuration > 0.0001f)
                StartCoroutine(CoSpawnPop(go));
        }

        private IEnumerator CoSpawnPop(GameObject go)
        {
            if (go == null) yield break;

            Transform t = go.transform;

            // 프리팹의 원래 스케일(예: 0.5)을 정답으로 저장
            Vector3 baseScale = t.localScale;

            // "툭" 방지: 즉시 0에서 시작
            t.localScale = Vector3.zero;

            // 팝 중 콜라이더 끄기(선택)
            Collider2D[] cols = null;
            if (disableColliderWhilePopping)
            {
                cols = go.GetComponentsInChildren<Collider2D>(true);
                if (cols != null)
                {
                    for (int i = 0; i < cols.Length; i++)
                        cols[i].enabled = false;
                }
            }

            // 랜덤 딜레이 (개별 팝 타이밍 흔들기)
            float delay = 0f;
            if (spawnPopDelayRange.y > 0f)
            {
                float min = Mathf.Min(spawnPopDelayRange.x, spawnPopDelayRange.y);
                float max = Mathf.Max(spawnPopDelayRange.x, spawnPopDelayRange.y);
                delay = UnityEngine.Random.Range(min, max);
            }

            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            if (go == null) yield break;

            float dur = Mathf.Max(0.01f, spawnPopDuration);
            float half = dur * 0.6f;          // 0 -> overshoot
            float rest = dur - half;          // overshoot -> 1
            float over = Mathf.Max(1f, spawnPopOvershoot);

            // 1) 0 -> overshoot
            float e = 0f;
            while (e < half)
            {
                if (go == null) yield break;
                e += Time.deltaTime;
                float tt = Mathf.Clamp01(e / half);
                float c = (spawnPopCurve != null) ? spawnPopCurve.Evaluate(tt) : tt;

                float s = Mathf.Lerp(0f, over, c);
                t.localScale = baseScale * s;
                yield return null;
            }
            if (go == null) yield break;
            t.localScale = baseScale * over;

            // 2) overshoot -> 1
            e = 0f;
            while (e < rest)
            {
                if (go == null) yield break;
                e += Time.deltaTime;
                float tt = Mathf.Clamp01(e / Mathf.Max(0.01f, rest));
                float c = (spawnPopCurve != null) ? spawnPopCurve.Evaluate(tt) : tt;

                float s = Mathf.Lerp(over, 1f, c);
                t.localScale = baseScale * s;
                yield return null;
            }
            if (go == null) yield break;
            t.localScale = baseScale;

            // 콜라이더 복구(선택)
            if (disableColliderWhilePopping && cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                        cols[i].enabled = true;
                }
            }
        }

        private bool TryFindSpawnPosition(Vector3 center, Vector2 halfSize, out Vector3 pos)
        {
            float minDistSqr = minDistance * minDistance;

            for (int attempt = 0; attempt < maxAttemptsPerSpawn; attempt++)
            {
                Vector2 p2 = SampleDistributionPoint(center, halfSize);
                Vector3 candidate = new Vector3(p2.x, p2.y, 0f);

                bool ok = true;
                for (int i = 0; i < _spawned.Count; i++)
                {
                    var go = _spawned[i];
                    if (go == null) continue;
                    float d = (go.transform.position - candidate).sqrMagnitude;
                    if (d < minDistSqr) { ok = false; break; }
                }

                if (ok)
                {
                    pos = candidate;
                    return true;
                }
            }

            pos = center;
            return false;
        }

        private Vector2 SampleDistributionPoint(Vector3 center3, Vector2 halfSize)
        {
            Vector2 center = new Vector2(center3.x, center3.y);

            if (!useClusterDistribution || _clusters.Count == 0)
            {
                return new Vector2(
                    UnityEngine.Random.Range(center.x - halfSize.x, center.x + halfSize.x),
                    UnityEngine.Random.Range(center.y - halfSize.y, center.y + halfSize.y)
                );
            }

            bool useCluster = UnityEngine.Random.value <= clusterSpawnChance;
            if (useCluster)
            {
                int idx = UnityEngine.Random.Range(0, _clusters.Count);
                var c = _clusters[idx];

                Vector2 offset = UnityEngine.Random.insideUnitCircle * c.radius;
                Vector2 p = c.center + offset;

                p.x = Mathf.Clamp(p.x, center.x - halfSize.x, center.x + halfSize.x);
                p.y = Mathf.Clamp(p.y, center.y - halfSize.y, center.y + halfSize.y);
                return p;
            }

            return new Vector2(
                UnityEngine.Random.Range(center.x - halfSize.x, center.x + halfSize.x),
                UnityEngine.Random.Range(center.y - halfSize.y, center.y + halfSize.y)
            );
        }

        private void EnsureClusters()
        {
            if (_clustersInitialized) return;

            _clustersInitialized = true;
            _clusters.Clear();

            if (!TryGetSpawnRect(out Vector3 center3, out Vector2 halfSize))
                return;

            Vector2 center = new Vector2(center3.x, center3.y);

            for (int i = 0; i < clusterCount; i++)
            {
                Vector2 c = new Vector2(
                    UnityEngine.Random.Range(center.x - halfSize.x, center.x + halfSize.x),
                    UnityEngine.Random.Range(center.y - halfSize.y, center.y + halfSize.y)
                );

                float r = UnityEngine.Random.Range(clusterRadiusRange.x, clusterRadiusRange.y);
                _clusters.Add(new Cluster { center = c, radius = r });
            }
        }

        private void InvalidateClusters()
        {
            _clustersInitialized = false;
        }

        private void HandleScaleChanged(int newLevel)
        {
            _lastScaleLevel = newLevel;
            InvalidateClusters();
            EnsureClusters();
        }

        private void SyncScaleLevelChangeIfNeeded()
        {
            var sm = MiningScaleManager.Instance;
            if (sm == null) return;

            int cur = sm.CurrentScaleLevel;
            if (cur == _lastScaleLevel) return;

            _lastScaleLevel = cur;
            InvalidateClusters();
            EnsureClusters();
        }

        // --- 단일 소스: SpawnWorldHeight 산출 ---
        private float GetFinalSpawnWorldHeight(int scaleLevel)
        {
            if (preferMiningScaleManagerHeight)
            {
                var msm = MiningScaleManager.Instance;
                if (msm != null)
                {
                    float h = msm.GetFinalSpawnWorldHeight();
                    if (h > 0.1f) return h;
                }
            }

            return GetSpawnWorldHeightByScaleLevel(scaleLevel);
        }

        private float GetSpawnWorldHeightByScaleLevel(int scaleLevel)
        {
            if (viewHeightTiers == null || viewHeightTiers.Count == 0)
                return 10f;

            float chosen = viewHeightTiers[0].spawnWorldHeight;
            int chosenLevel = int.MinValue;

            for (int i = 0; i < viewHeightTiers.Count; i++)
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

        public bool TryGetCurrentSpawnRect(out Vector3 center, out Vector2 halfSize)
        {
            return TryGetSpawnRect(out center, out halfSize);
        }

        private bool TryGetSpawnRect(out Vector3 center, out Vector2 halfSize)
        {
            if (useRectArea)
            {
                int level = 0;
                var sm = MiningScaleManager.Instance;
                if (sm != null) level = sm.CurrentScaleLevel;

                float worldHeight = GetFinalSpawnWorldHeight(level);

                float safeMul = Mathf.Clamp01(1f - topSafePercent - bottomSafePercent);
                if (safeMul <= 0.0001f) safeMul = 0.0001f;

                float usableHeight = worldHeight * safeMul;
                float usableWidth = usableHeight * fixedAspect;

                center = new Vector3(areaOffset.x, areaOffset.y, 0f);
                halfSize = new Vector2(usableWidth * 0.5f, usableHeight * 0.5f);
                return true;
            }

            if (Camera.main != null)
            {
                var cam = Camera.main;
                float z = -cam.transform.position.z;

                Vector3 min = cam.ViewportToWorldPoint(new Vector3(0f, 0f, z));
                Vector3 max = cam.ViewportToWorldPoint(new Vector3(1f, 1f, z));

                center = (min + max) * 0.5f;
                halfSize = new Vector2(Mathf.Abs(max.x - min.x) * 0.5f, Mathf.Abs(max.y - min.y) * 0.5f);
                return true;
            }

            center = Vector3.zero;
            halfSize = new Vector2(3f, 5f);
            return true;
        }

        private void CleanupNulls()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] == null)
                    _spawned.RemoveAt(i);
            }
        }

        private void ClearWorld()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetSpawnRect(out Vector3 center, out Vector2 halfSize))
                return;

            Gizmos.color = new Color(0.2f, 0.9f, 0.9f, 0.35f);
            Gizmos.DrawWireCube(center, new Vector3(halfSize.x * 2f, halfSize.y * 2f, 0f));
        }
    }
}
