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
    /// 핵심 변경:
    /// 1) SpawnWorldHeight는 MiningScaleManager.GetFinalSpawnWorldHeight()를 "우선" 사용(단일 소스)
    /// 2) 현재 계산된 스폰 Rect를 외부에서 조회할 수 있도록 공개 API 제공
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

        // Runtime
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private Coroutine _respawnRoutine;
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
            ClearWorld();
            InvalidateClusters();
            EnsureClusters();

            int target = GetTargetCount();
            int initialSpawnCount = Mathf.Max(initialCount, target);

            SpawnInitial(initialSpawnCount);

            _isActive = true;

            if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
            _respawnRoutine = StartCoroutine(RespawnLoop());
        }

        public void StopSpawner()
        {
            _isActive = false;
            if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }

        /// <summary>
        /// 인트로 연출용(1개만 깔기 등)
        /// </summary>
        public void SpawnPreview(int count = 1)
        {
            ClearWorld();
            InvalidateClusters();
            EnsureClusters();
            SpawnInitial(Mathf.Max(0, count));
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

        private void SpawnInitial(int count)
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
                    // MiningScaleManager가 제공하는 단일 소스
                    float h = msm.GetFinalSpawnWorldHeight();
                    if (h > 0.1f) return h;
                }
            }

            // fallback: 로컬 테이블
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

        /// <summary>
        /// 외부(카메라/디버그)에서 "현재 스폰 영역"을 알고 싶을 때 사용.
        /// </summary>
        public bool TryGetCurrentSpawnRect(out Vector3 center, out Vector2 halfSize)
        {
            return TryGetSpawnRect(out center, out halfSize);
        }

        private bool TryGetSpawnRect(out Vector3 center, out Vector2 halfSize)
        {
            // Rect 기반(권장): “줌아웃 = 스폰영역 확장”을 보장
            if (useRectArea)
            {
                int level = 0;
                var sm = MiningScaleManager.Instance;
                if (sm != null) level = sm.CurrentScaleLevel;

                float worldHeight = GetFinalSpawnWorldHeight(level);

                // 상/하 Safe Percent 적용 (UI 고려)
                float safeMul = Mathf.Clamp01(1f - topSafePercent - bottomSafePercent);
                if (safeMul <= 0.0001f) safeMul = 0.0001f;

                float usableHeight = worldHeight * safeMul;
                float usableWidth = usableHeight * fixedAspect;

                center = new Vector3(areaOffset.x, areaOffset.y, 0f);
                halfSize = new Vector2(usableWidth * 0.5f, usableHeight * 0.5f);
                return true;
            }

            // 카메라 기반(예비)
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
