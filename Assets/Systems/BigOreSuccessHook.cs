using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// BigOre가 파괴(성공)됐을 때 BigOreEventController.EndSuccess()를 호출해
    /// 이벤트 타임을 성공 종료시키는 훅.
    ///
    /// 변경점:
    /// - Ore.cs에 추가된 OnBrokenEvent(깨짐 이벤트)에 "구독"해서 자동으로 성공 처리한다.
    /// - Prefab Asset에서는 Scene Object를 직접 참조할 수 없으므로, eventController가 비어있으면 런타임 자동 탐색.
    ///
    /// 주의:
    /// - destroyOnBreak=false(리셋형 Ore)도 있을 수 있으므로, 필요하면 "BigOre 전용"으로만 붙여서 사용.
    /// </summary>
    public class BigOreSuccessHook : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("비워두면 런타임에 자동으로 씬에서 BigOreEventController를 찾아 연결한다.")]
        [SerializeField] private BigOreEventController eventController;

        [Tooltip("eventController가 null이면 씬에서 자동으로 찾아 연결")]
        [SerializeField] private bool autoFindControllerIfNull = true;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private Ore _ore;
        private bool _subscribed;

        private void Awake()
        {
            // Ore 참조(동일 오브젝트 우선)
            _ore = GetComponent<Ore>();
            if (_ore == null)
                _ore = GetComponentInChildren<Ore>(true);

            // Controller 자동 연결
            AutoBindControllerIfNeeded();

            // 깨짐 이벤트 구독
            Subscribe();
        }

        private void OnEnable()
        {
            // 프리팹 인스턴스가 비활성->활성되는 케이스에서도 안전하게
            AutoBindControllerIfNeeded();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        // =====================================================================
        // Wiring
        // =====================================================================

        private void AutoBindControllerIfNeeded()
        {
            if (eventController != null) return;
            if (!autoFindControllerIfNull) return;

#if UNITY_2023_1_OR_NEWER
            eventController = FindFirstObjectByType<BigOreEventController>();
#else
            eventController = FindObjectOfType<BigOreEventController>();
#endif
            if (debugLog)
                Debug.Log($"[BigOreSuccessHook] autoFindController -> {(eventController ? eventController.name : "null")}", this);
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (_ore == null)
            {
                if (debugLog) Debug.LogWarning("[BigOreSuccessHook] Ore component not found. Subscribe skipped.", this);
                return;
            }

            _ore.OnBrokenEvent += HandleOreBroken;
            _subscribed = true;

            if (debugLog) Debug.Log("[BigOreSuccessHook] Subscribed to Ore.OnBrokenEvent", this);
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (_ore != null)
                _ore.OnBrokenEvent -= HandleOreBroken;

            _subscribed = false;

            if (debugLog) Debug.Log("[BigOreSuccessHook] Unsubscribed from Ore.OnBrokenEvent", this);
        }

        // =====================================================================
        // Callback
        // =====================================================================

        private void HandleOreBroken(Ore ore)
        {
            // 깨진 Ore가 내가 구독한 대상이 아닌 경우(이론상 거의 없음)
            if (ore == null || _ore == null || ore != _ore)
                return;

            if (eventController == null)
            {
                // 혹시 씬 로드 순서로 아직 못 찾았으면 1회 재시도
                AutoBindControllerIfNeeded();

                if (eventController == null)
                {
                    if (debugLog) Debug.LogWarning("[BigOreSuccessHook] eventController is null. EndSuccess skipped.", this);
                    return;
                }
            }

            if (eventController.IsActive)
            {
                if (debugLog) Debug.Log("[BigOreSuccessHook] EndSuccess()", this);
                eventController.EndSuccess();
            }
            else
            {
                if (debugLog) Debug.Log("[BigOreSuccessHook] EventController is not active. Ignored.", this);
            }
        }

        // =====================================================================
        // Backward-compatible Manual Trigger (optional)
        // =====================================================================

        /// <summary>
        /// (호환용) 외부에서 수동 호출하고 싶을 때 사용.
        /// 이제는 Ore.OnBrokenEvent를 통해 자동 호출되지만,
        /// 기존 흐름을 깨지 않기 위해 남겨둔다.
        /// </summary>
        public void NotifyBigOreBroken()
        {
            // Ore 이벤트 경로와 동일 로직 사용
            HandleOreBroken(_ore);
        }
    }
}
