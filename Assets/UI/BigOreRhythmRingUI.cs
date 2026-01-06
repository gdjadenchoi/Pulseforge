using UnityEngine;
using UnityEngine.InputSystem;

namespace Pulseforge.Systems
{
    /// <summary>
    /// BigOre 리듬 링 UI + 입력 + 판정 + (선택) 리듬 데미지 적용 (통합 버전)
    ///
    /// 목표:
    /// - 지금은 한 파일로 동작(프로토타입 속도)
    /// - 나중에 쉽게 분리할 수 있도록 섹션/함수 경계를 명확히 유지
    ///
    /// 핵심 요구:
    /// - 리듬 입력/판정/데미지는 "노란 원(DrillCursor) 콜라이더"가
    ///   "BigOre 콜라이더"와 실제로 겹칠 때만 적용
    /// - Input System only
    /// </summary>
    [DisallowMultipleComponent]
    public class BigOreRhythmRingUI : MonoBehaviour
    {
        // ============================================================
        // Inspector
        // ============================================================

        [Header("Refs")]
        [SerializeField] private BigOreEventController controller;   // 씬 오브젝트 (자동 탐색 가능)
        [SerializeField] private Transform target;                   // BigOre 루트(보통 부모)
        [SerializeField] private SpriteRenderer ring;                // 링 스프라이트

        [Header("Cursor Gate (필수 요구)")]
        [Tooltip("리듬 판정/데미지를 '커서(노란원) 콜라이더가 타겟 콜라이더와 닿을 때만' 적용")]
        [SerializeField] private bool requireCursorOverlap = true;

        [Tooltip("비워두면 런타임에 DrillCursor 자동 탐색")]
        [SerializeField] private DrillCursor drillCursor;

        [Tooltip("비워두면 DrillCursor에서 Collider2D 자동 탐색")]
        [SerializeField] private Collider2D cursorCollider;

        [Tooltip("비워두면 target에서 Collider2D 자동 탐색")]
        [SerializeField] private Collider2D targetCollider;

        [Header("Auto Find")]
        [SerializeField] private bool autoFindControllerIfNull = true;
        [SerializeField] private bool autoFindTargetIfNull = true;

        [Header("Ring Size (world units)")]
        [SerializeField] private float maxRadius = 2.2f;
        [SerializeField] private float minRadius = 0.2f;

        [Header("Judgement Windows (seconds)")]
        [Tooltip("비트 기준 ±perfectWindowSec 이내면 Perfect")]
        [SerializeField] private float perfectWindowSec = 0.06f;

        [Tooltip("비트 기준 ±goodWindowSec 이내면 Good")]
        [SerializeField] private float goodWindowSec = 0.12f;

        [Header("Rhythm Damage")]
        [SerializeField] private bool applyRhythmDamageToOre = true;
        [SerializeField] private float perfectDamage = 8f;
        [SerializeField] private float goodDamage = 4f;
        [SerializeField] private float missDamage = 0f;

        [Header("Visual")]
        [SerializeField] private bool followTarget = true;
        [SerializeField] private Color idleColor = new Color(1, 1, 1, 0.18f);
        [SerializeField] private Color perfectColor = new Color(0.3f, 1f, 0.9f, 0.35f);
        [SerializeField] private Color goodColor = new Color(1f, 0.9f, 0.3f, 0.35f);
        [SerializeField] private Color missColor = new Color(1f, 0.2f, 0.2f, 0.30f);

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        // ============================================================
        // Runtime
        // ============================================================

        private Ore _targetOre;
        private bool _playing;
        private float _flashT;

        // ============================================================
        // Unity Lifecycle
        // ============================================================

        private void Awake()
        {
            if (ring == null) ring = GetComponent<SpriteRenderer>();
            AutoBindAll();
            HideImmediate();
        }

        private void OnEnable()
        {
            AutoBindAll();
            HookControllerEvents();
        }

        private void OnDisable()
        {
            UnhookControllerEvents();
        }

        private void Update()
        {
            if (!_playing) return;

            // 컨트롤러/타겟이 늦게 연결되는 상황 대비
            EnsureRuntimeBindings();
            if (controller == null || ring == null) return;

            // 1) UI: 위치/스케일 업데이트
            UpdateRingTransform();

            // 2) Input: 이번 프레임 입력 감지
            if (!WasPressedThisFrame())
            {
                TickVisualReturn();
                return;
            }

            // 3) Gate: 커서-타겟 오버랩이 아니면 무시 (요구사항)
            if (requireCursorOverlap && !IsCursorOverTargetByColliderOverlap())
            {
                if (debugLog) Debug.Log("[BigOreRhythmRingUI] Input ignored: cursor not overlapping target.", this);
                TickVisualReturn();
                return;
            }

            // 4) Judge: 판정
            var result = JudgeNow(controller);

            // 5) Apply: 시간/데미지 반영
            ApplyResultToController(result);
            if (applyRhythmDamageToOre) ApplyRhythmDamage(result);

            // 6) Visual: 결과 피드백
            FlashByResult(result);

            if (debugLog)
            {
                float phase = controller.BeatPhase01;
                Debug.Log($"[BigOreRhythmRingUI] Hit={result} phase={phase:0.000} interval={controller.BeatIntervalSec:0.000}", this);
            }

            // 7) Visual: 복귀 틱
            TickVisualReturn();
        }

        // ============================================================
        // Binding / Hook
        // ============================================================

        private void AutoBindAll()
        {
            // target 기본: 프리팹 구조상 보통 parent가 BigOre 루트
            if (autoFindTargetIfNull && target == null)
                target = transform.parent != null ? transform.parent : null;

            // target ore
            if (_targetOre == null && target != null)
            {
                _targetOre = target.GetComponent<Ore>();
                if (_targetOre == null) _targetOre = target.GetComponentInChildren<Ore>(true);
            }

            // target collider
            if (targetCollider == null && target != null)
            {
                targetCollider = target.GetComponent<Collider2D>();
                if (targetCollider == null) targetCollider = target.GetComponentInChildren<Collider2D>(true);
            }

            // drill cursor
            if (drillCursor == null)
            {
#if UNITY_6000_0_OR_NEWER
                drillCursor = FindFirstObjectByType<DrillCursor>(FindObjectsInactive.Exclude);
#else
                drillCursor = FindObjectOfType<DrillCursor>();
#endif
            }

            // cursor collider
            if (cursorCollider == null && drillCursor != null)
            {
                cursorCollider = drillCursor.GetComponent<Collider2D>();
                if (cursorCollider == null) cursorCollider = drillCursor.GetComponentInChildren<Collider2D>(true);
            }

            // controller
            if (autoFindControllerIfNull && controller == null)
            {
#if UNITY_6000_0_OR_NEWER
                controller = FindFirstObjectByType<BigOreEventController>(FindObjectsInactive.Exclude);
#else
                controller = FindObjectOfType<BigOreEventController>();
#endif
            }
        }

        private void EnsureRuntimeBindings()
        {
            if (controller == null || target == null || targetCollider == null || drillCursor == null || cursorCollider == null || _targetOre == null)
            {
                AutoBindAll();
                // controller가 새로 잡혔으면 훅도 보강
                HookControllerEvents();
            }
        }

        private void HookControllerEvents()
        {
            if (controller == null) return;

            controller.OnEventStart -= HandleEventStart;
            controller.OnEventStart += HandleEventStart;

            controller.OnEventEndSuccess -= HandleEventEnd;
            controller.OnEventEndSuccess += HandleEventEnd;

            controller.OnEventEndFail -= HandleEventEnd;
            controller.OnEventEndFail += HandleEventEnd;

            controller.OnBeat -= HandleBeat;
            controller.OnBeat += HandleBeat;
        }

        private void UnhookControllerEvents()
        {
            if (controller == null) return;

            controller.OnEventStart -= HandleEventStart;
            controller.OnEventEndSuccess -= HandleEventEnd;
            controller.OnEventEndFail -= HandleEventEnd;
            controller.OnBeat -= HandleBeat;
        }

        // ============================================================
        // Controller Events
        // ============================================================

        private void HandleEventStart()
        {
            _playing = true;
            if (ring != null) ring.enabled = true;
            SetRingColor(idleColor);
        }

        private void HandleEventEnd()
        {
            _playing = false;
            HideImmediate();
        }

        private void HandleBeat(int beatIndex)
        {
            // 비트 시점에 시각적 플래시를 주고 싶으면 여기서 확장
            _flashT = 1f;
        }

        // ============================================================
        // UI / Visual
        // ============================================================

        private void UpdateRingTransform()
        {
            if (followTarget && target != null)
                transform.position = target.position;

            // 링 축소 (비트 진행률)
            float phase = controller.BeatPhase01; // 0~1
            float r = Mathf.Lerp(maxRadius, minRadius, phase);
            transform.localScale = new Vector3(r, r, 1f);
        }

        private void FlashByResult(BigOreEventController.HitResult result)
        {
            switch (result)
            {
                case BigOreEventController.HitResult.Perfect:
                    SetRingColor(perfectColor);
                    break;
                case BigOreEventController.HitResult.Good:
                    SetRingColor(goodColor);
                    break;
                default:
                    SetRingColor(missColor);
                    break;
            }

            // 색 복귀 타이머
            _flashT = 1f;
        }

        private void TickVisualReturn()
        {
            if (_flashT <= 0f) return;

            _flashT -= Time.unscaledDeltaTime * 6f;
            if (_flashT <= 0f)
            {
                _flashT = 0f;
                SetRingColor(idleColor);
            }
        }

        private void HideImmediate()
        {
            if (ring != null) ring.enabled = false;
        }

        private void SetRingColor(Color c)
        {
            if (ring == null) return;
            ring.color = c;
        }

        // ============================================================
        // Input
        // ============================================================

        private bool WasPressedThisFrame()
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                return true;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                return true;

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                return true;

            return false;
        }

        // ============================================================
        // Gate (Overlap)
        // ============================================================

        /// <summary>
        /// "노란 원 콜라이더"와 "BigOre 콜라이더"가 실제로 겹치는지로 판정
        /// - Requirement: 겹치지 않으면 리듬 입력/판정/데미지 모두 무시
        /// </summary>
        private bool IsCursorOverTargetByColliderOverlap()
        {
            if (cursorCollider == null || targetCollider == null)
                return false;

            if (!cursorCollider.enabled || !targetCollider.enabled)
                return false;

            // 둘 중 하나가 비활성이면 false
            if (!cursorCollider.gameObject.activeInHierarchy || !targetCollider.gameObject.activeInHierarchy)
                return false;

            // 가장 신뢰도 높은 콜라이더-콜라이더 오버랩 판정
            var dist = Physics2D.Distance(cursorCollider, targetCollider);
            return dist.isOverlapped;
        }

        // ============================================================
        // Judge
        // ============================================================

        private BigOreEventController.HitResult JudgeNow(BigOreEventController c)
        {
            float interval = Mathf.Max(0.0001f, c.BeatIntervalSec);

            float phase = c.BeatPhase01;
            float distPhase = Mathf.Min(phase, 1f - phase);
            float distSec = distPhase * interval;

            if (distSec <= perfectWindowSec) return BigOreEventController.HitResult.Perfect;
            if (distSec <= goodWindowSec) return BigOreEventController.HitResult.Good;
            return BigOreEventController.HitResult.Miss;
        }

        // ============================================================
        // Apply (Controller / Damage)
        // ============================================================

        private void ApplyResultToController(BigOreEventController.HitResult result)
        {
            if (controller == null) return;
            controller.ReportHit(result);
        }

        private void ApplyRhythmDamage(BigOreEventController.HitResult result)
        {
            if (_targetOre == null) return;

            float dmg =
                result == BigOreEventController.HitResult.Perfect ? perfectDamage :
                result == BigOreEventController.HitResult.Good ? goodDamage :
                missDamage;

            if (dmg > 0f)
                _targetOre.ApplyHit(dmg);
        }
    }
}
