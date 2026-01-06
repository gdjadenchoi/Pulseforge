using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Pulseforge.Systems
{
    /// <summary>
    /// 마우스/터치 포인터를 따라다니는 "노란 원" 커서.
    ///
    /// 핵심:
    /// 1) 자동 채굴 데미지: 기존 Ore와 동일하게 OverlapCircle 기반으로 적용
    /// 2) 리듬 판정 게이트: BigOreRhythmRingUI에서 "노란 원 콜라이더"와 "BigOre 콜라이더" 오버랩을 체크하므로,
    ///    이 커서의 CircleCollider2D 반경을 채굴 반경(+padding)과 동기화한다.
    ///
    /// 주의:
    /// - CircleCollider2D.radius는 "로컬" 단위다. (Transform 스케일 영향을 받음)
    ///   => 월드 반경을 로컬로 환산하여 넣어준다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class DrillCursor : MonoBehaviour
    {
        public enum RadiusSource { Fixed, FromSprite }

        // 🔸 업그레이드 키(string)
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
        [Tooltip("RadiusSource=FromSprite일 때, 스프라이트 반지름(월드) * 배율")]
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
        private CircleCollider2D _circle;
        private float _swingTimer;

        // 커서 원래 스케일 저장용(시각만)
        private Vector3 _baseCursorScale = Vector3.one;

        private const int kBuffer = 64;
        private readonly Collider2D[] _hits = new Collider2D[kBuffer];

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _circle = GetComponent<CircleCollider2D>();

            // 커서는 "겹침 판정용" 트리거로만 사용
            if (_circle != null) _circle.isTrigger = true;

            // 물리 반응 제거(안전)
            if (_rb != null)
            {
                _rb.gravityScale = 0f;
                _rb.freezeRotation = true;
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

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

            // 자동 채굴 데미지(스윙 타이머)
            _swingTimer += Time.deltaTime;
            if (_swingTimer >= swingInterval)
            {
                _swingTimer = 0f;
                DoSwingHit();
            }

            // 시각적 스케일 갱신(업그레이드 반영)
            UpdateVisualScale();

            // ✅ 노란 원 콜라이더 반경을 “채굴 반경(+padding)”과 동기화
            SyncColliderRadiusToMiningRadius();
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

        /// <summary>
        /// 현재 채굴 반경(월드 단위) 계산.
        /// - RadiusSource 기준
        /// - CursorRadius 업그레이드 반영
        /// </summary>
        private float GetCurrentRadiusWorld()
        {
            float r;

            // 1) 기본 반경(월드) 계산
            if (radiusSource == RadiusSource.FromSprite && cursorRenderer != null)
            {
                // renderer.bounds.extents는 월드 단위
                var ext = cursorRenderer.bounds.extents;
                r = Mathf.Max(ext.x, ext.y) * spriteRadiusScale;
            }
            else
            {
                r = fixedRadius;
            }

            // 2) 업그레이드: CursorRadius 레벨에 따른 고정 증가(월드)
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

        /// <summary>
        /// BigOreRhythmRingUI의 "콜라이더 오버랩 게이트"가 정확하게 동작하도록,
        /// 커서 CircleCollider2D의 radius(로컬)를 채굴 반경(월드)로부터 환산해서 세팅.
        /// </summary>
        private void SyncColliderRadiusToMiningRadius()
        {
            if (_circle == null) return;

            float worldR = Mathf.Clamp(GetCurrentRadiusWorld() + detectPadding, 0.05f, 3.0f);

            // CircleCollider2D.radius는 로컬 단위이므로 월드 반경을 로컬로 환산
            float sx = Mathf.Abs(transform.lossyScale.x);
            if (sx <= 0.0001f) sx = 1f;

            _circle.radius = worldR / sx;
            _circle.offset = Vector2.zero;
        }

        /// <summary>
        /// (외부에서 필요 시) 현재 커서 콜라이더(World 기준 반경에 가까운 값) 제공.
        /// </summary>
        public float CurrentCollisionRadiusWorld => Mathf.Clamp(GetCurrentRadiusWorld() + detectPadding, 0.05f, 3.0f);

        public Collider2D CursorCollider => _circle;

        private void DoSwingHit()
        {
            float radiusWorld = GetCurrentRadiusWorld();

            int total = Physics2D.OverlapCircleNonAlloc(
                (Vector2)transform.position,
                radiusWorld + detectPadding,
                _hits
            );

            int applied = 0;
            bool useMask = oreMask.value != 0;

            // 기본 데미지 + 업그레이드 레벨에 따른 고정 증가
            float finalDamage = damagePerSwing;
            var upgradeManager = UpgradeManager.Instance;
            if (upgradeManager != null)
            {
                int dmgLevel = upgradeManager.GetLevel(UpgradeIdCursorDamage);
                const float flatPerLevel = 1f;
                finalDamage += dmgLevel * flatPerLevel;
                if (finalDamage < 0f) finalDamage = 0f;
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
                Debug.Log($"[DrillCursor] Hit ores: {applied} (r={radiusWorld:F2}, pad={detectPadding:F2}, dmg={finalDamage:F1})");
            }
        }

        private void OnDrawGizmosSelected()
        {
            float r = Application.isPlaying ? GetCurrentRadiusWorld() : fixedRadius;
            Gizmos.color = gizmoRadiusColor;
            Gizmos.DrawWireSphere(transform.position, r + detectPadding);
        }

        /// <summary>
        /// CursorRadius 업그레이드 레벨에 따라 커서 스프라이트 크기를 조정(시각용)
        /// - 판정 반경/콜라이더 반경은 GetCurrentRadiusWorld/SyncColliderRadiusToMiningRadius에서 처리
        /// </summary>
        private void UpdateVisualScale()
        {
            if (cursorRenderer == null)
                return;

            var upgradeManager = UpgradeManager.Instance;
            if (upgradeManager == null)
            {
                cursorRenderer.transform.localScale = _baseCursorScale;
                return;
            }

            int radiusLevel = upgradeManager.GetLevel(UpgradeIdCursorRadius);

            // 레벨 1당 7%씩 커지도록
            const float scalePerLevel = 0.07f;
            float scaleFactor = 1f + radiusLevel * scalePerLevel;
            if (scaleFactor < 0.1f) scaleFactor = 0.1f;

            cursorRenderer.transform.localScale = _baseCursorScale * scaleFactor;
        }
    }
}
