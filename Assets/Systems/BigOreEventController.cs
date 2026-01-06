using System;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// Big Ore "이벤트 타임"의 코어 컨트롤러 (독립 컴포넌트)
    /// - 이벤트 타이머(별도 시간)
    /// - BPM 기반 비트(메트로놈)
    /// - 간단 UI 펄스
    /// - 외부에서 판정 결과를 주입(ReportHit)
    /// </summary>
    public class BigOreEventController : MonoBehaviour
    {
        public enum HitResult
        {
            Miss = 0,
            Good = 1,
            Perfect = 2
        }

        [Header("Event Timer")]
        [Min(0.1f)] public float baseEventDurationSec = 6.0f;
        [Min(0.1f)] public float maxEventDurationSec = 10.0f;
        public bool endOnZero = true;

        [Header("Beat (Metronome)")]
        [Min(30f)] public float bpm = 120f;
        public bool incrementBeatIndex = true;

        [Header("Time Gain Rules")]
        [Min(0f)] public float addTimeOnGood = 0.05f;
        [Min(0f)] public float addTimeOnPerfect = 0.15f;
        [Min(0f)] public float penaltyOnMiss = 0.00f;

        [Header("Sprite UI (Optional)")]
        public SpriteRenderer indicator;
        public bool autoToggleIndicator = true;
        [Min(1f)] public float pulseScale = 1.12f;
        [Min(0.01f)] public float pulseReturnSec = 0.10f;
        public bool pulseAlpha = true;
        [Range(0f, 1f)] public float baseAlpha = 0.35f;
        [Range(0f, 1f)] public float pulseAlphaAdd = 0.35f;

        // ===== 런타임 상태 =====
        public bool IsActive { get; private set; }
        public float Remaining { get; private set; }
        public int BeatIndex { get; private set; }

        /// <summary>현재 BPM 기준 비트 간격(초)</summary>
        public float BeatIntervalSec => _beatInterval;

        /// <summary>현재 비트 진행률(0~1). 0에 가까울수록 "비트 직후", 1에 가까울수록 "다음 비트 직전"</summary>
        public float BeatPhase01
        {
            get
            {
                if (_beatInterval <= 0.0001f) return 0f;
                return Mathf.Clamp01(_beatAccum / _beatInterval);
            }
        }

        // 이벤트 훅
        public event Action OnEventStart;
        public event Action OnEventEndSuccess;
        public event Action OnEventEndFail;
        public event Action<float, float> OnEventTimeChanged; // remaining, normalized(0~1)
        public event Action<int> OnBeat; // beatIndex

        // 내부
        float _beatInterval;
        float _beatAccum;
        Vector3 _indicatorBaseScale;
        float _pulseT; // 0~1
        bool _pulsing;

        void Awake()
        {
            _beatInterval = CalcBeatInterval(bpm);

            if (indicator != null)
            {
                _indicatorBaseScale = indicator.transform.localScale;
                ApplyIndicatorAlpha(baseAlpha);
                if (autoToggleIndicator) indicator.gameObject.SetActive(false);
            }
        }

        void OnValidate()
        {
            _beatInterval = CalcBeatInterval(bpm);
            if (pulseScale < 1f) pulseScale = 1f;
            if (pulseReturnSec < 0.01f) pulseReturnSec = 0.01f;
            if (maxEventDurationSec < baseEventDurationSec) maxEventDurationSec = baseEventDurationSec;
        }

        void Update()
        {
            if (!IsActive) return;

            // 1) 이벤트 타이머 감소 (unscaled)
            Remaining -= Time.unscaledDeltaTime;
            if (Remaining < 0f) Remaining = 0f;

            float norm = Mathf.Clamp01(Remaining / Mathf.Max(0.0001f, baseEventDurationSec));
            OnEventTimeChanged?.Invoke(Remaining, norm);

            // 2) 비트 진행
            _beatAccum += Time.unscaledDeltaTime;
            while (_beatAccum >= _beatInterval)
            {
                _beatAccum -= _beatInterval;
                FireBeat();
            }

            // 3) 펄스 애니메이션
            UpdatePulse();

            // 4) 종료 판정
            if (endOnZero && Remaining <= 0f)
            {
                EndFail();
            }
        }

        // ============================================================
        // Public API
        // ============================================================

        public void BeginEvent()
        {
            BeginEvent(baseEventDurationSec, maxEventDurationSec);
        }

        public void BeginEvent(float durationSec, float maxDurationSec)
        {
            float safeBase = Mathf.Max(0.1f, durationSec);
            float safeMax = Mathf.Max(safeBase, maxDurationSec);

            baseEventDurationSec = safeBase;
            maxEventDurationSec = safeMax;

            IsActive = true;
            Remaining = Mathf.Clamp(safeBase, 0.1f, safeMax);
            BeatIndex = 0;

            _beatInterval = CalcBeatInterval(bpm);
            _beatAccum = 0f;

            if (indicator != null && autoToggleIndicator)
            {
                indicator.gameObject.SetActive(true);
                indicator.transform.localScale = _indicatorBaseScale;
                ApplyIndicatorAlpha(baseAlpha);
            }

            OnEventStart?.Invoke();
        }

        public void EndSuccess()
        {
            if (!IsActive) return;

            IsActive = false;

            if (indicator != null && autoToggleIndicator)
                indicator.gameObject.SetActive(false);

            OnEventEndSuccess?.Invoke();
        }

        public void EndFail()
        {
            if (!IsActive) return;

            IsActive = false;

            if (indicator != null && autoToggleIndicator)
                indicator.gameObject.SetActive(false);

            OnEventEndFail?.Invoke();
        }

        public void ReportHit(HitResult result)
        {
            if (!IsActive) return;

            switch (result)
            {
                case HitResult.Good:
                    AddEventTime(addTimeOnGood);
                    break;
                case HitResult.Perfect:
                    AddEventTime(addTimeOnPerfect);
                    break;
                case HitResult.Miss:
                    if (penaltyOnMiss > 0f)
                        AddEventTime(-penaltyOnMiss);
                    break;
            }
        }

        public void AddEventTime(float deltaSec)
        {
            if (!IsActive) return;

            Remaining = Mathf.Clamp(Remaining + deltaSec, 0f, maxEventDurationSec);
            float norm = Mathf.Clamp01(Remaining / Mathf.Max(0.0001f, baseEventDurationSec));
            OnEventTimeChanged?.Invoke(Remaining, norm);
        }

        // ============================================================
        // Internal
        // ============================================================

        void FireBeat()
        {
            if (incrementBeatIndex) BeatIndex++;

            OnBeat?.Invoke(BeatIndex);

            StartPulse();
        }

        void StartPulse()
        {
            if (indicator == null) return;

            _pulsing = true;
            _pulseT = 0f;

            indicator.transform.localScale = _indicatorBaseScale * pulseScale;

            if (pulseAlpha)
            {
                float a = Mathf.Clamp01(baseAlpha + pulseAlphaAdd);
                ApplyIndicatorAlpha(a);
            }
        }

        void UpdatePulse()
        {
            if (!_pulsing || indicator == null) return;

            _pulseT += Time.unscaledDeltaTime / Mathf.Max(0.0001f, pulseReturnSec);
            float t = Mathf.Clamp01(_pulseT);

            float s = Mathf.Lerp(pulseScale, 1f, t);
            indicator.transform.localScale = _indicatorBaseScale * s;

            if (pulseAlpha)
            {
                float a0 = Mathf.Clamp01(baseAlpha + pulseAlphaAdd);
                float a = Mathf.Lerp(a0, baseAlpha, t);
                ApplyIndicatorAlpha(a);
            }

            if (t >= 1f)
            {
                _pulsing = false;
                indicator.transform.localScale = _indicatorBaseScale;
                ApplyIndicatorAlpha(baseAlpha);
            }
        }

        void ApplyIndicatorAlpha(float a)
        {
            if (indicator == null) return;
            Color c = indicator.color;
            c.a = a;
            indicator.color = c;
        }

        static float CalcBeatInterval(float bpmValue)
        {
            float safe = Mathf.Max(1f, bpmValue);
            return 60f / safe;
        }
        private Ore _targetOre;
        /// <summary>
        /// 현재 이벤트의 타겟 BigOre를 연결한다. (Spawner에서 호출)
        /// </summary>
        public void SetTargetOre(Ore ore)
        {
            _targetOre = ore;
        }
        /// <summary>
        /// (선택) 외부에서 참조할 수 있게 getter 제공
        /// </summary>
        public Ore TargetOre => _targetOre;

    }
}
