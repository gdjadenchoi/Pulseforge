using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 마우스/터치를 따라다니는 채굴 커서.
    /// - Rigidbody2D(Kinematic) + Collider2D(isTrigger) 필수.
    /// - Ore와 접촉 중일 때 DPS로 HP를 깎는다.
    /// - 물리 스텝과 정합을 위해 FixedUpdate에서 MovePosition 사용.
    /// - 첫 접촉 프레임에도 즉시 1틱 데미지를 입힌다(OnTriggerEnter2D).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class DrillCursor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float followLerp = 14f;

        [Header("Mining")]
        [Tooltip("초당 데미지(Damage Per Second)")]
        [SerializeField] private float mineDps = 6f;

        private Camera cam;
        private Rigidbody2D rb;
        private Vector3 targetWorldPos;

        private void Awake()
        {
            cam = Camera.main;

            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var sp = UnityEngine.InputSystem.Mouse.current != null
                ? (Vector3)UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                : (Vector3)Input.mousePosition;
#else
            var sp = (Vector3)Input.mousePosition;
#endif
            targetWorldPos = cam ? cam.ScreenToWorldPoint(sp) : Vector3.zero;
            targetWorldPos.z = 0f;
        }

        private void FixedUpdate()
        {
            // 물리 스텝 기준 보간 이동(프레임레이트 독립)
            var p = Vector3.Lerp(transform.position, targetWorldPos,
                1f - Mathf.Exp(-followLerp * Time.fixedDeltaTime));
            rb.MovePosition(p);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (mineDps <= 0f) return;
            if (other.TryGetComponent<Ore>(out var ore))
            {
                // 첫 접촉 프레임에도 즉시 1틱
                ore.ApplyDamage(mineDps, Time.fixedDeltaTime);
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (mineDps <= 0f) return;
            if (other.TryGetComponent<Ore>(out var ore))
            {
                ore.ApplyDamage(mineDps, Time.fixedDeltaTime);
            }
        }
    }
}
