using System.Collections;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// PF_Mining 진입 시 카메라를 "스폰 영역(SpawnWorldHeight) 기준"으로 줌 맞춘 뒤,
    /// 줌 완료 시점에 세션 시작(SessionController.BeginSessionFlow)을 트리거하는 안정 버전.
    ///
    /// 목표 흐름:
    /// 아웃게임 -> 인게임 진입 -> (스케일/업그레이드 로드 완료) -> 카메라 줌 -> 줌 완료 -> 스폰/시간/커서 시작
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MiningCameraZoom : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        [Header("References")]
        [Tooltip("줌 완료 후 BeginSessionFlow()를 호출할 세션 컨트롤러")]
        [SerializeField] private SessionController sessionController;

        [Header("Animation")]
        [SerializeField] private float zoomDuration = 0.5f;
        [SerializeField] private float zoomDelay = 0.2f;
        [SerializeField] private AnimationCurve zoomCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Match Spawn Area (same logic as OreSpawner)")]
        [Tooltip("OreSpawner와 동일하게 상/하 UI safe percent를 반영해서 카메라 목표 높이를 계산")]
        [Range(0f, 0.4f)] [SerializeField] private float topSafePercent = 0.10f;
        [Range(0f, 0.4f)] [SerializeField] private float bottomSafePercent = 0.10f;

        [Tooltip("스폰 영역(usableHeight) 대비 카메라가 추가로 확보할 세로 여유(연출/시야)")]
        [Range(0f, 0.5f)] [SerializeField] private float verticalPaddingPercent = 0.10f;

        [Header("Clamp (safety)")]
        [SerializeField] private float minOrthographicSize = 4.5f;
        [SerializeField] private float maxOrthographicSize = 10f;

        [Header("Flow")]
        [Tooltip("줌 완료 후 세션 시작을 자동으로 호출할지")]
        [SerializeField] private bool startSessionOnZoomComplete = true;

        private Coroutine _zoomRoutine;
        private Coroutine _autoStartRoutine;

        // "줌 완료 시 세션 시작"은 1회만
        private bool _sessionStartedOnce;

        // ✅ 안전장치: 줌이 진행 중이었는지 추적 (Disable 등으로 중단될 때 유실 방지)
        private bool _zoomInProgress;

        private void Awake()
        {
            if (!targetCamera) targetCamera = GetComponent<Camera>();
            if (!sessionController) sessionController = FindObjectOfType<SessionController>();
        }

        private void OnEnable()
        {
            // 중요: 스케일/업그레이드 로드 타이밍 이슈 방지 (1프레임 대기)
            if (_autoStartRoutine != null) StopCoroutine(_autoStartRoutine);
            _autoStartRoutine = StartCoroutine(StartZoomNextFrame());
        }

        private void OnDisable()
        {
            // ✅ 줌이 진행 중이었다면, "줌 완료 트리거"가 유실될 수 있다.
            // 정책(Q1=A): 줌 완료 후에만 세션 시작.
            // 여기서는 "중단 시점의 카메라 상태를 최종값으로 확정"하고, 세션 시작을 진행한다.
            if (Application.isPlaying && startSessionOnZoomComplete && !_sessionStartedOnce && _zoomInProgress)
            {
                // 가능한 한 '최종 목표 사이즈'로 고정한 뒤 시작한다.
                ForceApplyTargetSize();
                TryStartSessionOnce();
            }

            if (_autoStartRoutine != null) StopCoroutine(_autoStartRoutine);
            _autoStartRoutine = null;

            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
            _zoomRoutine = null;

            _zoomInProgress = false;
        }

        private IEnumerator StartZoomNextFrame()
        {
            // MiningScaleManager.Start / UpgradeManager 로드가 먼저 끝나도록 한 프레임 양보
            yield return null;
            StartZoom();
        }

        [ContextMenu("Start Zoom")]
        public void StartZoom()
        {
            if (!targetCamera) return;

            float startSize = targetCamera.orthographicSize;
            float targetSize = CalculateTargetOrthoSize();
            targetSize = Mathf.Max(targetSize, startSize); // 🔥 줌인 방지

#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                float camWorldH = targetSize * 2f;
                Debug.Log($"[MiningCameraZoom] targetOrtho={targetSize:F3} (camWorldH={camWorldH:F3}) " +
                          $"safeTop={topSafePercent}, safeBottom={bottomSafePercent}, pad={verticalPaddingPercent}");
            }
#endif

            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);

            // 줌이 필요 없으면 즉시 적용 + 세션 시작(옵션)
            if (Mathf.Approximately(startSize, targetSize) || zoomDuration <= 0f)
            {
                targetCamera.orthographicSize = targetSize;
                _zoomInProgress = false;
                TryStartSessionOnce();
            }
            else
            {
                _zoomInProgress = true;
                _zoomRoutine = StartCoroutine(ZoomRoutine(startSize, targetSize));
            }
        }

        private void ForceApplyTargetSize()
        {
            if (!targetCamera) return;

            float startSize = targetCamera.orthographicSize;
            float targetSize = CalculateTargetOrthoSize();
            targetSize = Mathf.Max(targetSize, startSize); // 줌인 방지

            targetCamera.orthographicSize = targetSize;
        }

        private float CalculateTargetOrthoSize()
        {
            // 1) scaleLevel 기반 spawnWorldHeight를 "단일 소스"에서 가져옴
            float spawnWorldHeight = 10f;

            var msm = MiningScaleManager.Instance;
            if (msm != null)
                spawnWorldHeight = msm.GetFinalSpawnWorldHeight();

            // 2) OreSpawner와 동일하게 safe 적용해서 usableHeight 산출
            float safeMul = Mathf.Clamp01(1f - topSafePercent - bottomSafePercent);
            if (safeMul <= 0.0001f) safeMul = 0.0001f;

            float usableHeight = spawnWorldHeight * safeMul;

            // 3) 카메라는 usableHeight를 기본으로, 패딩을 더해서 보여줌
            float paddedHeight = usableHeight * (1f + verticalPaddingPercent);

            // 4) orthographicSize는 "세로 높이의 절반"
            float ortho = paddedHeight * 0.5f;
            return Mathf.Clamp(ortho, minOrthographicSize, maxOrthographicSize);
        }

        private IEnumerator ZoomRoutine(float from, float to)
        {
            if (zoomDelay > 0f) yield return new WaitForSeconds(zoomDelay);

            float elapsed = 0f;
            while (elapsed < zoomDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / zoomDuration);

                float curveT = zoomCurve != null ? zoomCurve.Evaluate(t) : t;
                float size = Mathf.Lerp(from, to, curveT);

                if (targetCamera) targetCamera.orthographicSize = size;
                yield return null;
            }

            if (targetCamera) targetCamera.orthographicSize = to;
            _zoomRoutine = null;

            _zoomInProgress = false;

            // 줌 완료 후 세션 시작
            TryStartSessionOnce();
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
            else
            {
#if UNITY_EDITOR
                Debug.LogWarning("[MiningCameraZoom] SessionController not found. Cannot start session.");
#endif
            }
        }
    }
}
