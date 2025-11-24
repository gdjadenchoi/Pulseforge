using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    public class OreSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("생성할 광석 프리팹")]
        public GameObject orePrefab;

        [Tooltip("게임 시작 시 기본 생성 개수")]
        public int initialCount = 12;

        [Tooltip("Rect 영역을 쓰지 않을 때 사용하는 원형 반경 (레거시용)")]
        public float radius = 5f;

        [Header("Spawn Area (Rect)")]
        [Tooltip("체크하면 사각형 영역을 사용해서 스폰")]
        public bool useRectArea = true;

        [Tooltip("사각형 스폰 영역 크기 (월드 기준)")]
        public Vector2 areaSize = new Vector2(4f, 6f);

        [Tooltip("스폰 영역 중심의 오프셋 (Spawner 위치 기준)")]
        public Vector2 areaOffset = Vector2.zero;

        [Header("Placement / 간격 & 타일 느낌")]
        [Tooltip("광석들 사이의 최소 거리 (월드 단위)")]
        public float minDistance = 0.6f;

        [Tooltip("겹치지 않는 위치를 찾기 위한 최대 시도 횟수")]
        public int maxPlacementAttempts = 30;

        [Tooltip("체크하면 그리드에 스냅되어 '타일'처럼 배치됨")]
        public bool useGrid = true;

        [Tooltip("그리드 셀 크기 (월드 단위)")]
        public float gridCellSize = 0.5f;

        [Header("Respawn / 리스폰 설정")]
        [Tooltip("항상 유지하려는 기본 광석 개수")]
        public int baseTargetCount = 12;

        [Tooltip("리스폰 배치 사이의 최소 딜레이 (초)")]
        public float respawnDelay = 0.3f;

        [Tooltip("리스폰 한 번에 생성할 최소 개수")]
        public int batchMin = 1;

        [Tooltip("리스폰 한 번에 생성할 최대 개수")]
        public int batchMax = 3;

        [Header("Upgrade Modifiers (아직 사용 X, 자리만 잡아둠)")]
        [Tooltip("업그레이드로 추가되는 고정 보너스 수량")]
        public int upgradeFlatBonus = 0;

        [Tooltip("업그레이드로 곱해지는 배수")]
        public float upgradeMultiplier = 1f;

        [Header("Debug Time Ramp (시간 지나면서 점점 많아지게)")]
        [Tooltip("체크하면 시간이 지날수록 목표 개수를 늘림")]
        public bool timeRampEnabled = false;

        [Tooltip("몇 초마다")]
        public float addEveryNSeconds = 10f;

        [Tooltip("한 번에 추가할 개수")]
        public int addAmount = 5;

        [Tooltip("추가되는 최대 개수 (baseTargetCount 기준)")]
        public int maxExtra = 50;

        // ==== 내부 상태 ====
        private readonly List<Transform> _spawned = new List<Transform>();
        private float _respawnTimer;
        private float _timeRampTimer;
        private int _extraFromTimeRamp;

        // 세션 중 활성 여부 (세션 끝나면 false, 다시 시작하면 true)
        private bool _isActive = true;

        private int TargetCount => baseTargetCount + _extraFromTimeRamp;

        private void Awake()
        {
            // 인스펙터에서 baseTargetCount를 안 건드렸다면 initialCount 기준으로 맞춰줌
            if (baseTargetCount <= 0)
                baseTargetCount = initialCount;

            // 첫 세션 시작용 초기 스폰
            SpawnInitial();
        }

        private void Update()
        {
            // 세션이 끝난 동안에는 리스폰 로직 완전히 멈춤
            if (!_isActive)
                return;

            CleanupDestroyed();
            HandleTimeRamp();
            HandleRespawn();
        }

        #region 초기 스폰

        private void SpawnInitial()
        {
            _spawned.Clear();

            int count = Mathf.Max(initialCount, 0);
            SpawnBatch(count);
        }

        #endregion

        #region 리스폰 로직

        private void HandleRespawn()
        {
            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer > 0f)
                return;

            int missing = TargetCount - _spawned.Count;
            if (missing <= 0)
                return;

            // 한 번에 1 ~ batchMax 개, 하지만 부족한 수량 이상으로는 안 뽑게
            int desiredBatch = Random.Range(batchMin, batchMax + 1);
            int batchSize = Mathf.Clamp(desiredBatch, 1, missing);

            SpawnBatch(batchSize);

            _respawnTimer = respawnDelay;
        }

        private void HandleTimeRamp()
        {
            if (!timeRampEnabled)
                return;

            if (addEveryNSeconds <= 0f || addAmount <= 0)
                return;

            _timeRampTimer += Time.deltaTime;
            while (_timeRampTimer >= addEveryNSeconds)
            {
                _timeRampTimer -= addEveryNSeconds;
                _extraFromTimeRamp = Mathf.Clamp(_extraFromTimeRamp + addAmount, 0, maxExtra);
            }
        }

        #endregion

        #region 스폰 배치

        private void SpawnBatch(int count)
        {
            if (orePrefab == null || count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                if (!TryGetFreePosition(out Vector3 pos))
                    break;

                var instance = Instantiate(orePrefab, pos, Quaternion.identity, transform);
                _spawned.Add(instance.transform);
            }
        }

        private bool TryGetFreePosition(out Vector3 position)
        {
            Vector3 center = transform.position + (Vector3)areaOffset;

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                Vector3 candidate;

                if (useRectArea)
                {
                    float x = (Random.value - 0.5f) * areaSize.x;
                    float y = (Random.value - 0.5f) * areaSize.y;
                    candidate = center + new Vector3(x, y, 0f);
                }
                else
                {
                    Vector2 inside = Random.insideUnitCircle * radius;
                    candidate = transform.position + (Vector3)inside;
                }

                // 타일 느낌을 위해 그리드 스냅
                if (useGrid && gridCellSize > 0.0001f)
                {
                    candidate.x = Mathf.Round(candidate.x / gridCellSize) * gridCellSize;
                    candidate.y = Mathf.Round(candidate.y / gridCellSize) * gridCellSize;
                }

                // 기존 광석들과 최소 거리 체크
                bool overlapped = false;
                float minSqr = minDistance * minDistance;

                for (int i = 0; i < _spawned.Count; i++)
                {
                    var t = _spawned[i];
                    if (t == null) continue;

                    float sqr = (t.position - candidate).sqrMagnitude;
                    if (sqr < minSqr)
                    {
                        overlapped = true;
                        break;
                    }
                }

                if (!overlapped)
                {
                    position = candidate;
                    return true;
                }
            }

            // 실패
            position = Vector3.zero;
            return false;
        }

        #endregion

        #region 세션 연동용 API

        /// <summary>
        /// 세션 종료 시 호출.  
        /// - 모든 광석 삭제  
        /// - 리스폰/타임램프 타이머 리셋  
        /// - 리스폰 비활성화 (Update 정지)
        /// </summary>
        public void PauseAndClear()
        {
            _isActive = false;

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i].gameObject);
            }

            _spawned.Clear();
            _respawnTimer = 0f;
            _timeRampTimer = 0f;
            _extraFromTimeRamp = 0;
        }

        /// <summary>
        /// MineAgain 버튼을 눌러 세션을 다시 시작할 때 호출.  
        /// - 스패너를 활성화하고  
        /// - 완전 초기 상태에서 다시 스폰 시작
        /// </summary>
        public void StartFresh()
        {
            _isActive = true;
            ResetSpawner();
        }

        /// <summary>
        /// 외부에서 "완전 리셋 후 바로 다시 채우기"용으로 쓸 수 있는 함수
        /// </summary>
        public void ResetSpawner()
        {
            // 1. 모든 기존 광석 삭제
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i].gameObject);
            }

            _spawned.Clear();

            // 2. 내부 타이머 리셋
            _respawnTimer = 0f;
            _timeRampTimer = 0f;
            _extraFromTimeRamp = 0;

            // 3. 초기 스폰 다시
            SpawnInitial();
        }

        public void ForceClear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    DestroyImmediate(_spawned[i].gameObject);
            }

            _spawned.Clear();
        }

        #endregion

        #region 기타 유틸

        private void CleanupDestroyed()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] == null)
                    _spawned.RemoveAt(i);
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
