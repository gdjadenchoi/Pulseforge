using UnityEngine;
using UnityEngine.Events;

namespace Pulseforge.Core
{
    /// <summary>
    /// BPM 기준으로 OnBeat/OnHalfBeat 이벤트를 발행.
    /// 판정용으로 현재 시간 대비 가장 가까운 비트까지의 오차(초) 계산 메서드 제공.
    /// </summary>
    public class RhythmConductor : MonoBehaviour
    {
        [Header("리듬 설정")]
        [SerializeField, Tooltip("기준 BPM (Beat Per Minute)")]
        private float bpm = 120f;
        [SerializeField, Tooltip("시작 시 자동 재생 여부")]
        private bool autoStart = true;

        [Header("디버그 로그(선택)")]
        [SerializeField, Tooltip("BeatLogger를 지정하면 코드에서 자동으로 리스너 연결")]
        private BeatLogger debugLogger;
        [SerializeField] private bool autoWireDebugLogger = true;

        [Header("이벤트")]
        public UnityEvent OnBeat;      // 매 비트
        public UnityEvent OnHalfBeat;  // 하프 비트

        // ---- 내부 상태 ----
        private float beatInterval;    // 한 박자 간격(초)
        private float nextBeatTime;
        private float nextHalfTime;
        private bool isRunning;

        void Awake()
        {
            beatInterval = 60f / bpm;
        }

        void Start()
        {
            if (autoWireDebugLogger && debugLogger != null)
            {
                OnBeat.RemoveListener(debugLogger.LogBeat);
                OnHalfBeat.RemoveListener(debugLogger.LogHalf);
                OnBeat.AddListener(debugLogger.LogBeat);
                OnHalfBeat.AddListener(debugLogger.LogHalf);
            }
            if (autoStart) StartBeat();
        }

        public void StartBeat()
        {
            isRunning = true;
            var t = Time.time;
            nextBeatTime = t + beatInterval;
            nextHalfTime = t + beatInterval * 0.5f;
        }

        public void StopBeat() => isRunning = false;

        void Update()
        {
            if (!isRunning) return;

            var t = Time.time;

            if (t >= nextBeatTime)
            {
                OnBeat?.Invoke();
                nextBeatTime += beatInterval;
            }

            if (t >= nextHalfTime)
            {
                OnHalfBeat?.Invoke();
                nextHalfTime += beatInterval;
            }
        }

        public void SetBPM(float newBpm)
        {
            bpm = Mathf.Max(30f, newBpm);
            beatInterval = 60f / bpm;
            var t = Time.time;
            nextBeatTime = t + beatInterval;
            nextHalfTime = t + beatInterval * 0.5f;
        }

        /// <summary>
        /// 지금 시간 기준으로 가장 가까운 비트까지의 절대 오차(초).
        /// (Time.time 사용. 오디오 DSP 동기화가 필요하면 매개변수로 extern 시간을 넣어도 된다)
        /// </summary>
        public float GetOffsetToNearestBeat(float currentTime)
        {
            // 직전 비트 시각을 추정
            float lastBeatTime = nextBeatTime - beatInterval;
            // 현재가 "시작점으로부터 몇 개의 비트만큼 떨어져 있는지"
            float beatsFromStart = (currentTime - lastBeatTime) / beatInterval;
            // 가장 가까운 비트 index
            float nearestIndex = Mathf.Round(beatsFromStart);
            // 그 비트의 실제 시간
            float nearestBeatTime = lastBeatTime + nearestIndex * beatInterval;
            return Mathf.Abs(nearestBeatTime - currentTime);
        }

        public float BeatIntervalSeconds => beatInterval;
        public float BPM => bpm;
    }
}
