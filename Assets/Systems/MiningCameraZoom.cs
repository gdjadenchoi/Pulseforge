using System.Collections;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// PF_Mining 진입 시 카메라를 "스폰 영역(SpawnWorldHeight) 기준"으로 줌 맞춘 뒤,
    /// 줌 완료 시점에 세션 시작(SessionController.BeginSessionFlow)을 트리거한다.
    ///
    /// 개선 목표:
    /// - 스케일/업그레이드가 늦게 확정돼도(초기 0 -> 이후 2) 줌을 다시 반영
    /// - target == start여도 "줌 연출"은 항상 보여줌(약간 더 줌아웃 -> 목표로 복귀)
    /// - timeScale 영향 제거(unscaled)
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MiningCameraZoom : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        [Header("References")]
        [Tooltip("줌 완료 후 BeginSessionFlow()를 호출할 세션 컨트롤러")]
        [SerializeField] private SessionController sessionController;

        [Header("Animation (unscaled)")]
        [SerializeField] private float zoomDuration = 0.5f;
        [SerializeField] private float zoomDelay = 0.2f;
        [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Presentation (always show even if no zoom needed)")]
        [Tooltip("목표가 start와 같아도 연출을 위해 추가로 줌아웃할 양(orthographicSize 가산)")]
        [SerializeField] private float presentationExtraOrtho = 0.35f;

        [Tooltip("연출(추가 줌아웃) 단계 시간 비율. (0~1)")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float presentationPhaseRatio = 0.45f;

        [Header("Match Spawn Area (same logic as OreSpawner)")]
        [Range(0f, 0.4f)] [SerializeField] private float topSafePercent = 0.10f;
        [Range(0f, 0.4f)] [SerializeField] private float bottomSafePercent = 0.10f;
        [Range(0f, 0.5f)] [SerializeField] private float verticalPaddingPercent = 0.10f;

        [Header("Clamp (safety)")]
        [SerializeField] private float minOrthographicSize = 4.5f;
        [SerializeField] private float maxOrthographicSize = 10f;

        [Header("Flow")]
        [SerializeField] private bool startSessionOnZoomComplete = true;

        [Header("Reapply on Scale Change")]
        [Tooltip("MiningScaleManager scale 변경 시 줌을 다시 계산/적용할지")]
        [SerializeField] private bool reapplyZoomOnScaleChanged = true;

        [Tooltip("scale 변경 후 즉시 줌을 다시 잡지 않고 약간 기다렸다가 적용(업그레이드 로드 타이밍 완충)")]
        [SerializeField] private float reapplyDelayRealtime = 0.05f;

        private Coroutine _zoomRoutine;
        private Coroutine _autoStartRoutine;
        private Coroutine _reapplyRoutine;

        private bool _sessionStartedOnce;
        private bool _zoomInProgress;

        private float _lastAppliedTargetOrtho = -1f;

        private void Awake()
        {
            if (!targetCamera) targetCamera = GetComponent<Camera>();
            if (!sessionController) sessionController = FindObjectOfType<SessionController>();
        }

        private void OnEnable()
        {
            HookScaleEvents();

            if (_autoStartRoutine != null) StopCoroutine(_autoStartRoutine);
            _autoStartRoutine = StartCoroutine(StartZoomNextFrame());
        }

        private void OnDisable()
        {
            UnhookScaleEvents();

            // 줌 도중 disable되면 트리거 유실 방지
            if (Application.isPlaying && startSessionOnZoomComplete && !_sessionStartedOnce && _zoomInProgress)
            {
                ForceApplyTargetSize();
                TryStartSessionOnce();
            }

            if (_autoStartRoutine != null) StopCoroutine(_autoStartRoutine);
            _autoStartRoutine = null;

            if (_reapplyRoutine != null) StopCoroutine(_reapplyRoutine);
            _reapplyRoutine = null;

            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
            _zoomRoutine = null;

            _zoomInProgress = false;
        }

        private void HookScaleEvents()
        {
            if (!reapplyZoomOnScaleChanged) return;

            var msm = MiningScaleManager.Instance;
            if (msm != null)
            {
                msm.OnScaleChanged -= HandleScaleChanged;
                msm.OnScaleChanged += HandleScaleChanged;
            }
        }

        private void UnhookScaleEvents()
        {
            if (!reapplyZoomOnScaleChanged) return;

            var msm = MiningScaleManager.Instance;
            if (msm != null)
            {
                msm.OnScaleChanged -= HandleScaleChanged;
            }
        }

        private void HandleScaleChanged(int newScaleLevel)
        {
            if (!isActiveAndEnabled) return;
            if (!targetCamera) return;

            // 세션이 이미 시작됐더라도, "영역 확장 업그레이드"는 즉시 카메라에 반영되는 게 자연스러움
            if (_reapplyRoutine != null) StopCoroutine(_reapplyRoutine);
            _reapplyRoutine = StartCoroutine(CoReapplyZoomAfterDelay());
        }

        private IEnumerator CoReapplyZoomAfterDelay()
        {
            if (reapplyDelayRealtime > 0f)
                yield return new WaitForSecondsRealtime(reapplyDelayRealtime);

            StartZoom(forceRecalculate: true);
        }

        private IEnumerator StartZoomNextFrame()
        {
            // 스케일 매니저/업그레이드 로드 타이밍 완충(최소 1프레임)
            yield return null;
            StartZoom(forceRecalculate: true);
        }

        [ContextMenu("Start Zoom")]
        public void StartZoom() => StartZoom(forceRecalculate: true);

        private void StartZoom(bool forceRecalculate)
        {
            if (!targetCamera) return;

            float startSize = targetCamera.orthographicSize;

            float targetSize = CalculateTargetOrthoSize();
            // 줌인 방지(원칙 유지): 최종 목표는 start 이상
            targetSize = Mathf.Max(targetSize, startSize);

            // 같은 값이어도 “연출”은 항상 보여줘야 함
            float presentationPeak = Mathf.Clamp(targetSize + Mathf.Max(0f, presentationExtraOrtho), minOrthographicSize, maxOrthographicSize);

#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.Log($"[MiningCameraZoom] start={startSize:F3}, target={targetSize:F3}, peak={presentationPeak:F3}");
            }
#endif

            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);

            // 만약 이미 같은 target을 적용 중이라면 과도한 재시작 방지
            // (단, 처음 진입 연출은 무조건 보여주므로 _lastAppliedTargetOrtho<0 일 때는 통과)
            if (_lastAppliedTargetOrtho >= 0f && Mathf.Approximately(_lastAppliedTargetOrtho, targetSize) && !_zoomInProgress)
            {
                // 이미 목표가 반영된 상태면 세션 시작만 보장
                TryStartSessionOnce();
                return;
            }

            _zoomInProgress = true;
            _zoomRoutine = StartCoroutine(ZoomRoutineTwoPhase(startSize, presentationPeak, targetSize));
        }

        private void ForceApplyTargetSize()
        {
            if (!targetCamera) return;

            float startSize = targetCamera.orthographicSize;
            float targetSize = CalculateTargetOrthoSize();
            targetSize = Mathf.Max(targetSize, startSize);

            targetCamera.orthographicSize = targetSize;
            _lastAppliedTargetOrtho = targetSize;
        }

        private float CalculateTargetOrthoSize()
        {
            float spawnWorldHeight = 10f;

            var msm = MiningScaleManager.Instance;
            if (msm != null)
                spawnWorldHeight = msm.GetFinalSpawnWorldHeight();

            float safeMul = Mathf.Clamp01(1f - topSafePercent - bottomSafePercent);
            if (safeMul <= 0.0001f) safeMul = 0.0001f;

            float usableHeight = spawnWorldHeight * safeMul;
            float paddedHeight = usableHeight * (1f + verticalPaddingPercent);

            float ortho = paddedHeight * 0.5f;
            return Mathf.Clamp(ortho, minOrthographicSize, maxOrthographicSize);
        }

        /// <summary>
        /// 항상 연출을 보여주기 위한 2단계 줌:
        /// 1) start -> peak(살짝 더 줌아웃)
        /// 2) peak  -> finalTarget
        /// unscaled로 구동
        /// </summary>
        private IEnumerator ZoomRoutineTwoPhase(float start, float peak, float finalTarget)
        {
            if (zoomDelay > 0f) yield return new WaitForSecondsRealtime(zoomDelay);

            float total = Mathf.Max(0.0001f, zoomDuration);
            float phaseA = Mathf.Clamp(total * presentationPhaseRatio, 0.0001f, total);
            float phaseB = Mathf.Max(0.0001f, total - phaseA);

            // Phase A: start -> peak
            yield return CoZoomUnscaled(start, peak, phaseA);

            // Phase B: peak -> final
            yield return CoZoomUnscaled(peak, finalTarget, phaseB);

            if (targetCamera) targetCamera.orthographicSize = finalTarget;

            _lastAppliedTargetOrtho = finalTarget;
            _zoomRoutine = null;
            _zoomInProgress = false;

            TryStartSessionOnce();
        }

        private IEnumerator CoZoomUnscaled(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float curveT = zoomCurve != null ? zoomCurve.Evaluate(t) : t;
                float size = Mathf.Lerp(from, to, curveT);

                if (targetCamera) targetCamera.orthographicSize = size;
                yield return null;
            }
        }

        private void TryStartSessionOnce()
        {
            if (!startSessionOnZoomComplete) return;
            if (_sessionStartedOnce) return;

            _sessionStartedOnce = true;

            if (!sessionController)
                sessionController = FindObjectOfType<SessionController>();

            if (sessionController != null)
            {
#if UNITY_EDITOR
                Debug.Log("[MiningCameraZoom] Zoom complete -> BeginSessionFlow()");
#endif
                sessionController.BeginSessionFlow();
            }
#if UNITY_EDITOR
            else
            {
                Debug.LogWarning("[MiningCameraZoom] SessionController not found. Cannot start session.");
            }
#endif
        }
    }
}
