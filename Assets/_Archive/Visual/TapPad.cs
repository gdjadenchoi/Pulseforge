using UnityEngine;
using UnityEngine.InputSystem;

namespace Pulseforge.Visuals
{
    /// <summary>
    /// PlayerInput의 Tap 액션에 연결해 순간 스케일 압축(눌림)만 준다.
    /// 시각 피드백용. 중앙 Ore 위나 근처에 배치.
    /// </summary>
    public class TapPad : MonoBehaviour
    {
        [SerializeField] private Transform target;      // 없으면 자기 자신
        [SerializeField] private float pressScale = 0.92f;
        [SerializeField] private float releaseLerp = 10f;

        Vector3 _baseScale;
        Vector3 _targetScale;

        void Awake()
        {
            if (!target) target = transform;
            _baseScale = target.localScale;
            _targetScale = _baseScale;
        }

        void Update()
        {
            target.localScale = Vector3.Lerp(target.localScale, _targetScale, Time.deltaTime * releaseLerp);
        }

        public void OnTap(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;
            // 순간 압축 후 원래 크기로 복귀
            _targetScale = _baseScale * pressScale;
            // 바로 다음 프레임부터 복귀를 시작하게 살짝 되돌림
            Invoke(nameof(Release), 0.03f);
        }

        void Release()
        {
            _targetScale = _baseScale;
        }
    }
}
