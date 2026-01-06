using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    public class BigOreSpawner : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private BigOreSpawnSettings settings;

        [Header("Prefab")]
        [SerializeField] private GameObject bigOrePrefab;

        [Header("Wiring")]
        [SerializeField] private BigOreEventController eventController;

        [Tooltip("비워두면 UpgradeManager.Instance 사용")]
        [SerializeField] private UpgradeManager upgradeManager;

        [Tooltip("비워두면 런타임에 SessionController 자동 탐색")]
        [SerializeField] private SessionController sessionController;

        [Header("Spawn Placement")]
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Transform spawnParent;
        [SerializeField] private bool singleActiveOnly = true;

        [Header("Fail Behavior")]
        [SerializeField] private bool destroyBigOreOnFail = true;

        [Header("Delay-When-Occupied Policy")]
        [Tooltip("예정된 스폰 시점에 BigOre가 살아있으면, 이 간격만큼 뒤로 미루며 재시도")]
        [Min(0.05f)] [SerializeField] private float occupiedRetryIntervalSec = 0.5f;

        [Header("(4) Cleanup Policy")]
        [Tooltip("기본은 OFF. ON이면 예외적으로 '세션 시작 시'에도 잔재 BigOre를 정리한다.")]
        [SerializeField] private bool clearLeftoverOnSessionStart = false;

        [Header("(11) Spawn Clears Existing Ores")]
        [Tooltip("BigOre가 등장하는 위치에 기존 Ore가 있으면 '보상/경험치 지급' 되도록 강제 파괴 후 등장")]
        [SerializeField] private bool clearOresOnSpawn = true;

        [Tooltip("지울 Ore 레이어 마스크(권장: Ore 레이어). 비어있으면 모든 레이어에서 Ore 컴포넌트로 판별")]
        [SerializeField] private LayerMask oreMask;

        [Tooltip("스폰 지우기 반경. 0이면 BigOre 프리팹의 Collider2D bounds로 자동 추정")]
        [Min(0f)] [SerializeField] private float clearRadiusOverride = 0f;

        [Tooltip("Ore를 즉시 파괴하기 위한 매우 큰 데미지")]
        [Min(1f)] [SerializeField] private float clearDamage = 999999f;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private readonly List<GameObject> _spawned = new();
        private Coroutine _routine;

        private float _sessionStartUnscaled;
        private float _sessionDurationSec;

        private bool _hookedSession;
        private bool _hookedEvent;

        private const int kOverlapBuffer = 128;
        private readonly Collider2D[] _overlaps = new Collider2D[kOverlapBuffer];

        private void Awake()
        {
            AutoBindIfNeeded();
            HookSessionEvents();
            HookEventControllerEvents();
        }

        private void OnEnable()
        {
            AutoBindIfNeeded();
            HookSessionEvents();
            HookEventControllerEvents();
        }

        private void OnDisable()
        {
            UnhookSessionEvents();
            UnhookEventControllerEvents();
        }

        private void AutoBindIfNeeded()
        {
            if (upgradeManager == null) upgradeManager = UpgradeManager.Instance;

#if UNITY_6000_0_OR_NEWER
            if (eventController == null)
                eventController = FindFirstObjectByType<BigOreEventController>(FindObjectsInactive.Exclude);

            if (sessionController == null)
                sessionController = FindFirstObjectByType<SessionController>(FindObjectsInactive.Exclude);
#else
            if (eventController == null)
                eventController = FindObjectOfType<BigOreEventController>();

            if (sessionController == null)
                sessionController = FindObjectOfType<SessionController>();
#endif

            if (debugLog)
            {
                Debug.Log($"[BigOreSpawner] AutoBind settings={(settings ? "OK" : "NULL")} prefab={(bigOrePrefab ? "OK" : "NULL")} " +
                          $"eventController={(eventController ? eventController.name : "NULL")} sessionController={(sessionController ? sessionController.name : "NULL")} " +
                          $"upgradeManager={(upgradeManager ? upgradeManager.name : "NULL")}", this);
            }
        }

        // ======================================================
        // Hook: Session
        // ======================================================

        private void HookSessionEvents()
        {
            if (_hookedSession) return;
            if (sessionController == null) return;

            sessionController.OnSessionStart -= HandleSessionStart;
            sessionController.OnSessionStart += HandleSessionStart;

            sessionController.OnSessionEnd -= HandleSessionEnd;
            sessionController.OnSessionEnd += HandleSessionEnd;

            _hookedSession = true;

            if (debugLog)
                Debug.Log("[BigOreSpawner] Hooked SessionController.OnSessionStart/OnSessionEnd", this);
        }

        private void UnhookSessionEvents()
        {
            if (!_hookedSession) return;
            if (sessionController == null) { _hookedSession = false; return; }

            sessionController.OnSessionStart -= HandleSessionStart;
            sessionController.OnSessionEnd -= HandleSessionEnd;

            _hookedSession = false;

            if (debugLog)
                Debug.Log("[BigOreSpawner] Unhooked SessionController events", this);
        }

        // ======================================================
        // Hook: BigOreEventController (Fail -> Cleanup)
        // ======================================================

        private void HookEventControllerEvents()
        {
            if (_hookedEvent) return;
            if (eventController == null) return;

            eventController.OnEventEndFail -= HandleEventFail;
            eventController.OnEventEndFail += HandleEventFail;

            _hookedEvent = true;

            if (debugLog)
                Debug.Log("[BigOreSpawner] Hooked BigOreEventController.OnEventEndFail", this);
        }

        private void UnhookEventControllerEvents()
        {
            if (!_hookedEvent) return;

            if (eventController != null)
                eventController.OnEventEndFail -= HandleEventFail;

            _hookedEvent = false;

            if (debugLog)
                Debug.Log("[BigOreSpawner] Unhooked BigOreEventController events", this);
        }

        private void HandleEventFail()
        {
            if (debugLog) Debug.Log("[BigOreSpawner] EventFail -> OnBigOreFailed()", this);
            OnBigOreFailed();
        }

        // ======================================================
        // Session handlers
        // ======================================================

        private void HandleSessionStart()
        {
            // (4) 요구사항 반영:
            // - "세션 시작 시 삭제"를 기본적으로 하지 않는다.
            // - 단, 예외적으로 잔재를 확실히 없애고 싶으면 옵션으로 켤 수 있게 한다.
            if (clearLeftoverOnSessionStart)
            {
                if (debugLog) Debug.Log("[BigOreSpawner] SessionStart -> clearLeftoverOnSessionStart ON, clearing.", this);
                ClearActiveBigOres();
            }

            StopAll();

            float duration = sessionController != null ? sessionController.baseDurationSec : 0f;

            if (debugLog)
                Debug.Log($"[BigOreSpawner] SessionStart -> BeginSession({duration:0.###})", this);

            BeginSession(duration);
        }

        private void HandleSessionEnd()
        {
            // (4) 요구사항: 세션 종료 시 BigOre 정리
            if (debugLog) Debug.Log("[BigOreSpawner] SessionEnd -> ClearActiveBigOres()", this);

            ClearActiveBigOres();
            StopAll();
        }

        // ======================================================
        // Session Entry
        // ======================================================

        public void BeginSession(float sessionDurationSec)
        {
            AutoBindIfNeeded();
            HookEventControllerEvents();

            if (settings == null || bigOrePrefab == null)
            {
                if (debugLog) Debug.LogWarning("[BigOreSpawner] Missing settings or prefab.", this);
                return;
            }

            StopAll();

            _sessionStartUnscaled = Time.unscaledTime;
            _sessionDurationSec = Mathf.Max(0.1f, sessionDurationSec);

            int spawnCount = settings.RollSpawnCount(upgradeManager);
            if (debugLog)
                Debug.Log($"[BigOreSpawner] BeginSession duration={_sessionDurationSec:0.###} spawnCount={spawnCount}", this);

            if (spawnCount <= 0) return;

            var schedule = settings.BuildSpawnSchedule(spawnCount, _sessionDurationSec);

            if (schedule == null || schedule.Count == 0)
            {
                if (debugLog) Debug.Log("[BigOreSpawner] Schedule empty.", this);
                return;
            }

            if (debugLog)
            {
                for (int i = 0; i < schedule.Count; i++)
                    Debug.Log($"[BigOreSpawner] schedule[{i}] = {schedule[i]:0.###}s", this);

                if (schedule.Count < spawnCount)
                    Debug.LogWarning(
                        $"[BigOreSpawner] Requested spawnCount={spawnCount} but scheduleCount={schedule.Count}. " +
                        $"(minGap/noSpawnLastSeconds/sessionDuration constraints)", this);
            }

            _routine = StartCoroutine(CoSpawn(schedule));
        }

        public void StopAll()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;

            _spawned.RemoveAll(x => x == null);
            _spawned.Clear();
        }

        // ======================================================
        // Spawn Routine
        // ======================================================

        private IEnumerator CoSpawn(List<float> schedule)
        {
            for (int i = 0; i < schedule.Count; i++)
            {
                float plannedT = schedule[i];
                yield return WaitUntilSessionTime(plannedT);

                while (IsOccupied())
                {
                    float nowT = Time.unscaledTime - _sessionStartUnscaled;
                    float latestAllowed = Mathf.Max(0f, _sessionDurationSec - Mathf.Max(0f, settings.noSpawnLastSeconds));

                    if (nowT + occupiedRetryIntervalSec >= latestAllowed)
                    {
                        if (debugLog)
                            Debug.Log($"[BigOreSpawner] Spawn#{i + 1} skipped (occupied, no time left). now={nowT:0.###}, latest={latestAllowed:0.###}", this);
                        yield break;
                    }

                    if (debugLog)
                        Debug.Log($"[BigOreSpawner] Spawn#{i + 1} delayed (occupied). retry in {occupiedRetryIntervalSec:0.###}s", this);

                    yield return new WaitForSecondsRealtime(occupiedRetryIntervalSec);
                }

                TrySpawnOne(i + 1);
            }
        }

        private IEnumerator WaitUntilSessionTime(float t)
        {
            float target = _sessionStartUnscaled + Mathf.Max(0f, t);
            while (Time.unscaledTime < target)
                yield return null;
        }

        private bool IsOccupied()
        {
            if (!singleActiveOnly) return false;

            _spawned.RemoveAll(x => x == null);
            return _spawned.Count > 0;
        }

        private void TrySpawnOne(int ordinal)
        {
            if (IsOccupied()) return;

            Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            Transform parent = spawnParent != null ? spawnParent : transform;

            // (11) 스폰 위치의 기존 Ore를 "보상/경험치 포함"해서 파괴
            if (clearOresOnSpawn)
                ClearOresAt(pos);

            var go = Instantiate(bigOrePrefab, pos, Quaternion.identity, parent);
            _spawned.Add(go);

            // 리듬 데미지 대상 Ore 연결
            if (eventController != null)
            {
                var ore = go.GetComponent<Ore>();
                if (ore == null) ore = go.GetComponentInChildren<Ore>(true);

                if (ore != null)
                {
                    eventController.SetTargetOre(ore);

                    if (debugLog)
                        Debug.Log("[BigOreSpawner] SetTargetOre(Ore) bound for rhythm damage.", go);
                }
                else
                {
                    if (debugLog)
                        Debug.LogWarning("[BigOreSpawner] Ore component not found on BigOre prefab. Rhythm damage will not apply.", go);
                }
            }

            // 프리팹에서 이벤트 시간 읽어서 적용
            if (eventController != null)
            {
                var cfg = go.GetComponent<BigOreEventConfig>();
                if (cfg != null)
                {
                    if (debugLog)
                        Debug.Log($"[BigOreSpawner] BigOreEventConfig found. base={cfg.baseEventDurationSec:0.###}, max={cfg.maxEventDurationSec:0.###}", go);

                    eventController.BeginEvent(cfg.baseEventDurationSec, cfg.maxEventDurationSec);
                }
                else
                {
                    if (debugLog)
                        Debug.Log("[BigOreSpawner] BigOreEventConfig not found. Using controller defaults.", go);

                    eventController.BeginEvent();
                }
            }

            if (debugLog)
                Debug.Log($"[BigOreSpawner] BigOre spawned (#{ordinal})", this);
        }

        // ======================================================
        // (11) Clear ores at spawn
        // ======================================================

        private void ClearOresAt(Vector3 worldPos)
        {
            float radius = ResolveClearRadius();
            if (radius <= 0.001f) radius = 0.5f;

            int count;
            if (oreMask.value != 0)
            {
                count = Physics2D.OverlapCircleNonAlloc(worldPos, radius, _overlaps, oreMask);
            }
            else
            {
                count = Physics2D.OverlapCircleNonAlloc(worldPos, radius, _overlaps);
            }

            int cleared = 0;

            for (int i = 0; i < count; i++)
            {
                var col = _overlaps[i];
                if (!col) continue;

                // BigOre 자신(아직 스폰 전이라 이 시점엔 없음) / 기타 트리거 등은 Ore 컴포넌트로 필터
                if (col.TryGetComponent<Ore>(out var ore))
                {
                    // 보상/경험치 포함 파괴: Ore.ApplyHit -> OnBroken()에서 처리됨
                    ore.ApplyHit(clearDamage);
                    cleared++;
                }
            }

            if (debugLog && cleared > 0)
                Debug.Log($"[BigOreSpawner] Cleared ores at spawn. cleared={cleared}, r={radius:0.###}", this);
        }

        private float ResolveClearRadius()
        {
            if (clearRadiusOverride > 0f) return clearRadiusOverride;
            if (bigOrePrefab == null) return 0f;

            // 프리팹에 Collider2D가 있으면 bounds 기반으로 추정
            var col = bigOrePrefab.GetComponent<Collider2D>();
            if (col == null) col = bigOrePrefab.GetComponentInChildren<Collider2D>(true);
            if (col != null)
            {
                var b = col.bounds;
                float r = Mathf.Max(b.extents.x, b.extents.y);
                // 여유 조금
                return Mathf.Max(0f, r * 1.05f);
            }

            return 0f;
        }

        // ======================================================
        // Cleanup
        // ======================================================

        private void ClearActiveBigOres()
        {
            _spawned.RemoveAll(x => x == null);

            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        // ======================================================
        // External Callbacks
        // ======================================================

        public void OnBigOreFailed()
        {
            if (!destroyBigOreOnFail) return;

            ClearActiveBigOres();

            if (debugLog)
                Debug.Log("[BigOreSpawner] OnBigOreFailed -> destroyed active big ores", this);
        }
    }
}
