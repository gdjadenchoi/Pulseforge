using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    public class OreSpawner : MonoBehaviour
    {
        private const string UpgradeIdOreAmount = "OreAmount";

        [Header("Spawn Settings")]
        [Tooltip("생성할 광석 프리팹")]
        public GameObject orePrefab;

        [Tooltip("게임 시작 시 기본 생성 개수")]
        public int initialCount = 12;

        [Tooltip("Rect 영역을 쓰지 않을 때 사용하는 원형 반경 (레거시용)")]
        public float radius = 5f;

        [Header("Rect Spawn Area (New)")]
        [Tooltip("Rect 영역 기반 스폰을 사용할지 여부")]
        public bool useRectArea = true;

        [Tooltip("스폰 영역의 크기 (폭, 높이)")]
        public Vector2 areaSize = new Vector2(10f, 6f);

        [Tooltip("스폰 영역의 중심 오프셋 (Spawner 기준)")]
        public Vector2 areaOffset = Vector2.zero;

        [Header("Grid Settings")]
        [Tooltip("그리드 스냅을 사용할지 여부")]
        public bool useGrid = true;

        [Tooltip("그리드 셀 간격 (월드 단위)")]
        public float gridSize = 0.5f;

        [Header("Spawn Distance Rules")]
        [Tooltip("광석 간 최소 거리")]
        public float minDistance = 0.5f;

        [Tooltip("너무 많이 실패했을 때, 강제로 배치 시도하는 최대 횟수")]
        public int maxAttemptsPerSpawn = 32;

        [Header("Respawn Settings")]
        [Tooltip("광석 개수를 유지하려는 목표 수(초기값은 initialCount와 동일하게 두어도 됨)")]
        public int baseTargetCount = 12;

        [Tooltip("리스폰 체크 주기(초)")]
        public float respawnInterval = 0.3f;

        [Tooltip("한 번에 리스폰할 수 있는 최대 수")]
        public int batchMax = 3;

        [Header("Time Ramp Settings")]
        [Tooltip("시간 경과에 따라 목표 광석 수를 늘릴지 여부")]
        public bool useTimeRamp = false;

        [Tooltip("광석 수가 증가하기까지 걸리는 시간(초)")]
        public float timeRampInterval = 10f;

        [Tooltip("증가하는 광석 수(한 단계마다 추가)")]
        public int addAmount = 5;

        [Tooltip("추가되는 광석 수의 최대치(0 이하면 무제한)")]
        public int maxExtraFromTimeRamp = 0;

        // 내부 관리용 리스트
        private readonly List<Transform> _spawned = new List<Transform>();

        // 리스폰/타임램프용 타이머
        private float _respawnTimer;
        private float _timeRampTimer;
        private int _extraFromTimeRamp;

        // 세션 중 활성 여부 (세션 끝나면 false, 다시 시작하면 true)
        private bool _isActive = true;

        /// <summary>
        /// 현재 세션에서 유지하려는 목표 광석 수.
        /// - baseTargetCount + 시간 램프 증가분 + 업그레이드 보너스를 모두 포함.
        /// </summary>
        private int TargetCount
        {
            get
            {
                int extra = 0;
                var upgrade = UpgradeManager.Instance;
                if (upgrade != null)
                {
                    // 예전 GetOreAmountBonus()와 동일한 로직:
                    // level * 5
                    int level = upgrade.GetLevel(UpgradeIdOreAmount);
                    const int amountPerLevel = 5;
                    extra = level * amountPerLevel;
                }

                return baseTargetCount + extra;
            }
        }

        #region Unity Lifecycle

        private void Awake()
        {
            // 초기 리스트 클리어 (혹시 에디터 상에 남아 있을 수 있으므로)
            _spawned.Clear();

            // baseTargetCount를 초기값 기준으로 자동 세팅해두기
            if (baseTargetCount < initialCount)
            {
                baseTargetCount = initialCount;
            }

            // 초기 스폰
            SpawnInitial();
        }

        private void Update()
        {
            if (!_isActive)
                return;

            // 리스폰 타이머
            _respawnTimer += Time.deltaTime;
            if (_respawnTimer >= respawnInterval)
            {
                _respawnTimer -= respawnInterval;
                HandleRespawn();
            }

            // 시간 경과에 따른 목표 수 증가
            if (useTimeRamp)
            {
                _timeRampTimer += Time.deltaTime;
                if (_timeRampTimer >= timeRampInterval)
                {
                    _timeRampTimer -= timeRampInterval;
                    IncreaseTargetFromTimeRamp();
                }
            }
        }

        #endregion

        #region Public API (SessionController 등에서 사용)

        /// <summary>
        /// 세션이 끝날 때 호출: 리스폰 정지 + 기존 광석 삭제
        /// </summary>
        public void PauseAndClear()
        {
            _isActive = false;
            ClearAll();
        }

        /// <summary>
        /// 세션 재시작 시 호출: 내부 상태 리셋 + 초기 스폰 다시 수행
        /// </summary>
        public void StartFresh()
        {
            _isActive = true;

            // 타이머 및 시간 램프 누적치 초기화
            _respawnTimer = 0f;
            _timeRampTimer = 0f;
            _extraFromTimeRamp = 0;

            ClearAll();
            SpawnInitial();
        }

        #endregion

        #region Spawn Core

        private void SpawnInitial()
        {
            if (orePrefab == null)
            {
                Debug.LogWarning("[OreSpawner] orePrefab이 설정되어 있지 않습니다.");
                return;
            }

            int toSpawn = Mathf.Max(0, initialCount);
            for (int i = 0; i < toSpawn; i++)
            {
                TrySpawnOne();
            }
        }

        private void HandleRespawn()
        {
            // 현재 살아있는(존재하는) 광석 수
            CleanupNulls();
            int current = _spawned.Count;

            int target = TargetCount;
            if (current >= target)
                return;

            int needed = target - current;

            // 한 번에 1 ~ batchMax 개,
            int batch = Mathf.Min(needed, batchMax);
            for (int i = 0; i < batch; i++)
            {
                TrySpawnOne();
            }
        }

        private void IncreaseTargetFromTimeRamp()
        {
            // maxExtraFromTimeRamp 가 0 이하이면 무제한으로 증가
            if (maxExtraFromTimeRamp > 0 &&
                _extraFromTimeRamp >= maxExtraFromTimeRamp)
                return;

            _extraFromTimeRamp += addAmount;
        }

        private void TrySpawnOne()
        {
            if (orePrefab == null)
                return;

            Vector3 position;
            bool success = FindValidPosition(out position);

            if (!success)
            {
                // 실패 시에는 그냥 포기 (너무 좁거나 조건이 빡셀 수 있음)
                // 필요하다면 여기서 로그를 찍어도 됨
                return;
            }

            GameObject oreObj = Instantiate(orePrefab, position, Quaternion.identity, transform);
            _spawned.Add(oreObj.transform);
        }

        #endregion

        #region Positioning / Validation

        private bool FindValidPosition(out Vector3 result)
        {
            result = Vector3.zero;

            // 스폰 영역 기준 중심
            Vector3 center = transform.position;
            if (useRectArea)
            {
                center += (Vector3)areaOffset;
            }

            // 여러 번 시도해서, 기존 광석과의 최소 거리 조건을 만족하는 위치를 찾는다.
            for (int attempt = 0; attempt < maxAttemptsPerSpawn; attempt++)
            {
                Vector3 candidate;

                if (useRectArea)
                {
                    // Rect 영역 내부의 랜덤 위치
                    float halfX = areaSize.x * 0.5f;
                    float halfY = areaSize.y * 0.5f;
                    float x = Random.Range(-halfX, halfX);
                    float y = Random.Range(-halfY, halfY);
                    candidate = center + new Vector3(x, y, 0f);
                }
                else
                {
                    // 원형 반경 내 랜덤 위치 (레거시)
                    Vector2 rand = Random.insideUnitCircle * radius;
                    candidate = center + (Vector3)rand;
                }

                // 그리드 스냅 적용
                if (useGrid && gridSize > 0f)
                {
                    candidate.x = Mathf.Round(candidate.x / gridSize) * gridSize;
                    candidate.y = Mathf.Round(candidate.y / gridSize) * gridSize;
                }

                // 기존 광석들과의 최소 거리 체크
                if (!IsTooClose(candidate))
                {
                    result = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool IsTooClose(Vector3 candidate)
        {
            float minSqr = minDistance * minDistance;
            for (int i = 0; i < _spawned.Count; i++)
            {
                Transform t = _spawned[i];
                if (t == null)
                    continue;

                float sqr = (t.position - candidate).sqrMagnitude;
                if (sqr < minSqr)
                    return true;
            }
            return false;
        }

        #endregion

        #region Maintenance Helpers

        private void ClearAll()
        {
            // 실제 오브젝트 삭제
            for (int i = 0; i < _spawned.Count; i++)
            {
                Transform t = _spawned[i];
                if (t != null)
                {
                    Destroy(t.gameObject);
                }
            }
            _spawned.Clear();
        }

        private void CleanupNulls()
        {
            // 리스트에서 누락된 항목 제거
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] == null)
                {
                    _spawned.RemoveAt(i);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;

            if (useRectArea)
            {
                Vector3 center = transform.position + (Vector3)areaOffset;
                Gizmos.DrawWireCube(center, new Vector3(areaSize.x, areaSize.y, 0f));
            }
            else
            {
                Gizmos.DrawWireSphere(transform.position, radius);
            }
        }

        #endregion
    }
}
