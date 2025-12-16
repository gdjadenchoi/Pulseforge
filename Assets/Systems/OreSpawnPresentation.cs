using System.Collections;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 광석(오브젝트) "스폰 연출" 전용 컴포넌트.
    /// - 스폰될 때 점(0)에서 시작해서 overshoot -> 원래 스케일까지 팝 애니메이션
    /// - 프리팹 스케일(예: 0.5)을 "기준 스케일"로 자동 보존함
    /// - 스폰 중 콜라이더를 잠깐 끌 수 있음(클릭/충돌 어색함 방지)
    /// </summary>
    public class OreSpawnPresentation : MonoBehaviour
    {
        [Header("Auto Play")]
        [Tooltip("오브젝트 활성화 시 자동으로 팝 연출을 재생")]
        [SerializeField] private bool playOnEnable = true;

        [Tooltip("스폰 연출이 끝날 때까지 스케일을 고정(중간에 외부 스케일 변경 방지)")]
        [SerializeField] private bool lockScaleWhilePlaying = true;

        [Header("Timing")]
        [Tooltip("연출 시작 전 딜레이(뾰뾰뾱 느낌)")]
        [SerializeField] private Vector2 startDelayRange = new Vector2(0f, 0.08f);

        [Tooltip("전체 팝 길이(초)")]
        [SerializeField] private float duration = 0.12f;

        [Tooltip("오버슈트 배율(1.0이면 오버슈트 없음, 1.15 추천)")]
        [SerializeField] private float overshoot = 1.15f;

        [Tooltip("Time.timeScale 영향을 무시하고(일시정지 중에도) 연출을 돌릴지")]
        [SerializeField] private bool useUnscaledTime = false;

        [Header("Curve")]
        [Tooltip("0->1 구간 커브. 기본은 EaseInOut")]
        [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Collider")]
        [Tooltip("팝 중 콜라이더를 꺼둘지")]
        [SerializeField] private bool disableCollidersWhilePlaying = true;

        private Coroutine _routine;
        private Vector3 _baseScale;
        private Collider2D[] _colliders;

        private void Awake()
        {
            // 프리팹에 설정된 스케일(예: 0.5)을 그대로 기준값으로 사용
            _baseScale = transform.localScale;

            if (disableCollidersWhilePlaying)
                _colliders = GetComponentsInChildren<Collider2D>(true);
        }

        private void OnEnable()
        {
            if (!playOnEnable) return;
            Play();
        }

        private void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            // 꺼질 때는 안전하게 원복
            transform.localScale = _baseScale;
            SetCollidersEnabled(true);
        }

        [ContextMenu("Play Pop")]
        public void Play()
        {
            // 중복 재생 방지
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            _routine = StartCoroutine(CoPlay());
        }

        private IEnumerator CoPlay()
        {
            // 랜덤 딜레이(뾰뾰뾱)
            float delay = 0f;
            if (startDelayRange.y > 0f)
            {
                float min = Mathf.Min(startDelayRange.x, startDelayRange.y);
                float max = Mathf.Max(startDelayRange.x, startDelayRange.y);
                delay = Random.Range(min, max);
            }

            if (delay > 0f)
            {
                float tDelay = 0f;
                while (tDelay < delay)
                {
                    tDelay += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                    yield return null;
                }
            }

            // 팝 시작
            SetCollidersEnabled(false);

            float dur = Mathf.Max(0.01f, duration);
            float up = dur * 0.6f;            // 0 -> overshoot
            float down = dur - up;            // overshoot -> base
            float over = Mathf.Max(1f, overshoot);

            // 시작: 점
            transform.localScale = Vector3.zero;

            // 1) 0 -> overshoot
            float e = 0f;
            while (e < up)
            {
                e += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float tt = Mathf.Clamp01(e / up);
                float c = curve != null ? curve.Evaluate(tt) : tt;

                float s = Mathf.Lerp(0f, over, c);
                if (lockScaleWhilePlaying)
                    transform.localScale = _baseScale * s;
                else
                    transform.localScale = transform.localScale.normalized * (_baseScale.magnitude * s); // 안전장치

                yield return null;
            }
            transform.localScale = _baseScale * over;

            // 2) overshoot -> base
            e = 0f;
            float denom = Mathf.Max(0.01f, down);
            while (e < down)
            {
                e += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float tt = Mathf.Clamp01(e / denom);
                float c = curve != null ? curve.Evaluate(tt) : tt;

                float s = Mathf.Lerp(over, 1f, c);
                transform.localScale = _baseScale * s;
                yield return null;
            }

            transform.localScale = _baseScale;

            SetCollidersEnabled(true);
            _routine = null;
        }

        private void SetCollidersEnabled(bool enabled)
        {
            if (!disableCollidersWhilePlaying) return;
            if (_colliders == null) return;

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i].enabled = enabled;
            }
        }
    }
}
