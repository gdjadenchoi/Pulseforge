using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// Ore를 CoreAnchor 주위로 공전시키는 컴포넌트.
    /// - 스폰된 현재 위치를 기준으로 반경/위상을 자동 초기화
    /// - 궤도별 속도/반경/위상 다양화 가능
    /// - 타원 느낌(편심) + 반경 흔들림으로 "그럴싸한" 공전 연출
    /// </summary>
    [DisallowMultipleComponent]
    public class OrbitBody : MonoBehaviour
    {
        [Header("Center (optional)")]
        [Tooltip("공전 중심. 비워두면 씬에서 'CoreAnchor'를 자동으로 찾는다.")]
        [SerializeField] private Transform center;

        [Header("Base Orbit")]
        [Tooltip("공전 속도(도/초). +면 반시계, -면 시계.")]
        [SerializeField] private float angularSpeedDeg = 18f;

        [Tooltip("현재 반경을 그대로 쓸지(스폰 위치 기반), 아니면 랜덤 반경으로 덮어쓸지")]
        [SerializeField] private bool overrideRadius = false;

        [Tooltip("overrideRadius=true일 때 사용할 반경 범위")]
        [SerializeField] private Vector2 radiusRange = new Vector2(2.0f, 5.5f);

        [Header("Variation")]
        [Tooltip("시작 각도에 랜덤 오프셋을 추가할지")]
        [SerializeField] private bool randomizePhase = true;

        [Tooltip("각도 속도 랜덤 범위(±). 예: 6이면 18±6")]
        [SerializeField] private float speedJitter = 8f;

        [Header("Elliptical / Wobble (optional)")]
        [Tooltip("타원 느낌을 위한 편심 강도(0=원). 0.0~0.25 권장")]
        [Range(0f, 0.35f)]
        [SerializeField] private float eccentricity = 0.12f;

        [Tooltip("반경 흔들림(정적 느낌 제거). 0이면 끔")]
        [SerializeField] private float radialBobAmp = 0.10f;

        [Tooltip("반경 흔들림 주파수(Hz 느낌). 0이면 끔")]
        [SerializeField] private float radialBobFreq = 1.2f;

        [Header("Runtime (read-only)")]
        [SerializeField] private float radius;
        [SerializeField] private float phaseDeg;
        [Header("Self Spin (optional)")]
        [SerializeField] private bool enableSelfSpin = true;
        [SerializeField] private Vector2 selfSpinSpeedDegRange = new Vector2(-60f, 60f); // deg/sec
        [Header("Scale Variation (optional)")]
        [SerializeField] private bool randomizeScale = true;
        [SerializeField] private Vector2 scaleRange = new Vector2(0.85f, 1.25f);
        private Vector3 _baseScale;

        private float _selfSpinSpeed;


        private float _baseRadius;
        private float _phaseSeed;
        private float _speed;

        private void Awake()
        {
            ResolveCenter();

            _baseScale = transform.localScale;

            if (randomizeScale)
            {
                float s = Random.Range(scaleRange.x, scaleRange.y);
                transform.localScale = _baseScale * s;
            }

            // 중심이 없으면 공전 불가 → 안전하게 종료
            if (center == null) return;

            // 현재 위치로부터 자동 초기화
            Vector2 toMe = (Vector2)transform.position - (Vector2)center.position;
            float currentRadius = toMe.magnitude;
            float currentPhase = Mathf.Atan2(toMe.y, toMe.x) * Mathf.Rad2Deg;

            // 반경 설정
            if (overrideRadius)
                radius = Random.Range(radiusRange.x, radiusRange.y);
            else
                radius = Mathf.Max(0.1f, currentRadius);

            // 위상 설정
            phaseDeg = currentPhase;
            if (randomizePhase)
                phaseDeg += Random.Range(-180f, 180f);

            _baseRadius = radius;

            // 속도 다양화
            _speed = angularSpeedDeg + Random.Range(-speedJitter, speedJitter);
            if (Mathf.Abs(_speed) < 1f) _speed = Mathf.Sign(_speed == 0 ? 1 : _speed) * 1f;

            // 흔들림/타원 시드
            _phaseSeed = Random.Range(0f, 1000f);
            // 자전 속도 랜덤
            if (enableSelfSpin)
            {
                _selfSpinSpeed = Random.Range(selfSpinSpeedDegRange.x, selfSpinSpeedDegRange.y);
                if (Mathf.Abs(_selfSpinSpeed) < 5f) // 너무 미미하면 체감 없음
                _selfSpinSpeed = Mathf.Sign(_selfSpinSpeed == 0 ? 1 : _selfSpinSpeed) * 5f;
            }
        }

        private void Update()
        {
            if (center == null) return;

            // 각도 진행
            phaseDeg += _speed * Time.deltaTime;

            // 반경 흔들림
            float bob = 0f;
            if (radialBobAmp > 0f && radialBobFreq > 0f)
            {
                bob = Mathf.Sin((Time.time + _phaseSeed) * (Mathf.PI * 2f) * radialBobFreq) * radialBobAmp;
            }

            float r = Mathf.Max(0.1f, _baseRadius + bob);

            // 타원 느낌(편심): x축/ y축 비율로 살짝 찌그러뜨림
            float ex = 1f + eccentricity;
            float ey = 1f - eccentricity;

            float rad = phaseDeg * Mathf.Deg2Rad;
            Vector2 offset = new Vector2(Mathf.Cos(rad) * r * ex, Mathf.Sin(rad) * r * ey);

            transform.position = (Vector2)center.position + offset;
            // 자전(자기 회전)
            if (enableSelfSpin)
            {       
                transform.Rotate(0f, 0f, _selfSpinSpeed * Time.deltaTime);
            }
        }

        private void ResolveCenter()
        {
            if (center != null) return;

            var go = GameObject.Find("CoreAnchor");
            if (go != null) center = go.transform;
        }

        // 인스펙터에서 버튼처럼 눌러 재초기화하고 싶으면, ContextMenu로 제공
        [ContextMenu("Reinitialize Orbit From Current Position")]
        private void ReinitializeFromCurrent()
        {
            if (center == null) ResolveCenter();
            if (center == null) return;

            Vector2 toMe = (Vector2)transform.position - (Vector2)center.position;
            radius = Mathf.Max(0.1f, toMe.magnitude);
            phaseDeg = Mathf.Atan2(toMe.y, toMe.x) * Mathf.Rad2Deg;
            _baseRadius = radius;
        }
    }
}
