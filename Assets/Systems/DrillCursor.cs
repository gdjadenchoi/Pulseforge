using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Pulseforge.Systems
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class DrillCursor : MonoBehaviour
    {
        public enum RadiusSource { Fixed, FromSprite }

        // 🔸 새 string 기반 업그레이드 키
        private const string UpgradeIdCursorRadius = "CursorRadius";
        private const string UpgradeIdCursorDamage = "CursorDamage";

        [Header("Movement")]
        [SerializeField] private float followLerp = 14f;

        [Header("Mining")]
        [Tooltip("한 번 스윙 간격(초)")]
        [SerializeField] private float swingInterval = 0.18f;
        [Tooltip("한 번 스윙 당 피해량")]
        [SerializeField] private float damagePerSwing = 3f;

        [Header("Detection")]
        [SerializeField] private RadiusSource radiusSource = RadiusSource.Fixed;
        [Tooltip("RadiusSource=Fixed일 때 사용되는 고정 반경(월드 단위)")]
        [SerializeField] private float fixedRadius = 0.45f;
        [Tooltip("RadiusSource=FromSprite일 때, 스프라이트 반지름에 곱해지는 배율")]
        [SerializeField] private float spriteRadiusScale = 1.0f;
        [Tooltip("히트 여유(월드 단위). 살짝 겹쳐도 맞도록 여백을 더함")]
        [SerializeField] private float detectPadding = 0.06f;
        [Tooltip("Ore가 있는 레이어. 비워도 동작은 하지만 지정 권장")]
        [SerializeField] private LayerMask oreMask;

        [Header("Visual Sorting")]
        [SerializeField] private SpriteRenderer cursorRenderer;
        [SerializeField] private int cursorSortingOrder = 100;

        [Header("Debug")]
        [SerializeField] private bool logHitCount = false;
        [SerializeField] private Color gizmoRadiusColor = new Color(1f, 0.9f, 0.2f, 0.3f);

        private Camera _cam;
        private Rigidbody2D _rb;
        private float _swingTimer;

        // 커서 원래 스케일 저장용
        private Vector3 _baseCursorScale = Vector3.one;

        private const int kBuffer = 64;
        private readonly Collider2D[] _hits = new Collider2D[kBuffer];

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();

            // 스프라이트 렌더러 없으면 자식에서 찾아보기
            if (cursorRenderer == null)
                cursorRenderer = GetComponentInChildren<SpriteRenderer>(true);

            if (cursorRenderer != null)
            {
                cursorRenderer.sortingOrder = cursorSortingOrder;
                _baseCursorScale = cursorRenderer.transform.localScale; // 원래 스케일 저장
            }
        }

        private void OnEnable()
        {
            if (_cam == null)
                _cam = Camera.main;
        }

        private void Update()
        {
            // 포인터 위치 따라가기
            if (!TryGetPointerScreenPosition(out var screenPos))
                return;

            if (_cam != null)
            {
                var world = (Vector3)_cam.ScreenToWorldPoint(screenPos);
                world.z = 0f;

                transform.position = Vector3.Lerp(
                    transform.position,
                    world,
                    1f - Mathf.Exp(-followLerp * Time.deltaTime)
                );
            }

            // 스윙 타이머
            _swingTimer += Time.deltaTime;
            if (_swingTimer >= swingInterval)
            {
                _swingTimer = 0f;
                DoSwingHit();
            }

            // 🔸 업그레이드 레벨에 따라 시각적 스케일 갱신
            UpdateVisualScale();
        }

        private bool TryGetPointerScreenPosition(out Vector3 screenPos)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null)
            {
                foreach (var t in Touchscreen.current.touches)
                {
                    if (t.press.isPressed)
                    {
                        screenPos = t.position.ReadValue();
                        return true;
                    }
                }
            }
#endif
#pragma warning disable CS0618
            screenPos = Input.mousePosition; // Both/Old 입력 폴백
#pragma warning restore CS0618
            return true;
        }

        private float GetCurrentRadius()
        {
            float r;

            // 1) 기본 반경 계산 (기존 로직 유지)
            if (radiusSource == RadiusSource.FromSprite && cursorRenderer != null)
            {
                var ext = cursorRenderer.bounds.extents;
                r = Mathf.Max(ext.x, ext.y) * spriteRadiusScale;
            }
            else
            {
                r = fixedRadius;
            }

            // 2) 업그레이드: CursorRadius 레벨에 따른 고정 증가
            var upgradeManager = UpgradeManager.Instance;
            if (upgradeManager != null)
            {   
                int radiusLevel = upgradeManager.GetLevel(UpgradeIdCursorRadius);
                const float radiusPerLevel = 0.05f;
                r += radiusLevel * radiusPerLevel;
            }

            // 3) 과도한 값 방지
            return Mathf.Clamp(r, 0.05f, 2.5f);
        }

        private void DoSwingHit()
        {
            var radius = GetCurrentRadius();
            var total = Physics2D.OverlapCircleNonAlloc(
                (Vector2)transform.position,
                radius + detectPadding,
                _hits
            );

            int applied = 0;
            bool useMask = oreMask.value != 0;

            // 🔸 기본 데미지 + 업그레이드 레벨에 따른 고정 증가
            float finalDamage = damagePerSwing;
            var upgradeManager = UpgradeManager.Instance;
            if (upgradeManager != null)
            {
                int dmgLevel = upgradeManager.GetLevel(UpgradeIdCursorDamage);
                const float flatPerLevel = 1f;
                finalDamage += dmgLevel * flatPerLevel;

                if (finalDamage < 0f)
                    finalDamage = 0f;
            }
            for (int i = 0; i < total; i++)
            {
                var col = _hits[i];
                if (!col) continue;
                if (useMask && (oreMask.value & (1 << col.gameObject.layer)) == 0)
                    continue;

                if (col.TryGetComponent<Ore>(out var ore))
                {
                    ore.ApplyHit(finalDamage);
                    applied++;
                }
            }

            if (logHitCount && applied > 0)
            {
                Debug.Log($"[DrillCursor] Hit ores: {applied} (r={radius:F2}, pad={detectPadding:F2}, dmg={finalDamage:F1})");
            }
        }

        private void OnDrawGizmosSelected()
        {
            float r = Application.isPlaying ? GetCurrentRadius() : fixedRadius;
            Gizmos.color = gizmoRadiusColor;
            Gizmos.DrawWireSphere(transform.position, r + detectPadding);
        }

        /// <summary>
        /// CursorRadius 업그레이드 레벨에 따라 커서 스프라이트 크기를 조정
        /// (판정 반경은 이미 GetCurrentRadius()에서 따로 처리 중)
        /// </summary>
        private void UpdateVisualScale()
        {
            if (cursorRenderer == null)
                return;

            var upgradeManager = UpgradeManager.Instance;
            if (upgradeManager == null)
            {
                // 업그레이드 매니저가 없으면 기본 스케일 유지
                cursorRenderer.transform.localScale = _baseCursorScale;
                return;
            }

            int radiusLevel = upgradeManager.GetLevel(UpgradeIdCursorRadius);

            // 레벨 1당 7%씩 커지도록
            const float scalePerLevel = 0.07f;
            float scaleFactor = 1f + radiusLevel * scalePerLevel;

            if (scaleFactor < 0.1f)
                scaleFactor = 0.1f;

            cursorRenderer.transform.localScale = _baseCursorScale * scaleFactor;
        }
    }
}
