using UnityEngine;
using UnityEngine.Events;

namespace Pulseforge.Core
{
    /// <summary>
    /// BPM 기반 비트 이벤트 발행기.
    /// - 오디오 소스와 동기화 옵션 제공(권장): Audio DSP 시간에 맞춰 정확히 OnBeat/OnHalfBeat를 발생.
    /// - 오디오 미사용 시: 일반 타이머(Time.time)로 동작.
    /// - MiningController 등 기존 의존 코드를 그대로 사용 가능(메서드/이벤트 호환).
    /// </summary>
    public class RhythmConductor : MonoBehaviour
    {
        [Header("리듬 설정")]
        [SerializeField, Tooltip("기준 BPM (Beat Per Minute)")]
        private float bpm = 120f;

        [SerializeField, Tooltip("시작 시 자동 스타트")]
        private bool autoStart = true;

        [Header("오디오 동기화(선택)")]
        [SerializeField, Tooltip("비트와 동기화할 AudioSource (테스트용 mp3/ogg)")]
        private AudioSource audioSource;

        [SerializeField, Tooltip("오디오 DSP시간과 비트 동기화 사용")]
        private bool syncWithAudio = true;

        [SerializeField, Tooltip("오디오 시작 지연(초). DSP 스케줄용 권장: 0.05~0.1")]
        private double audioStartDelay = 0.07;

        [Header("이벤트")]
        public UnityEvent OnBeat;      // 매 비트
        public UnityEvent OnHalfBeat;  // 하프 비트

        // 내부 상태
        private float beatInterval;         // 1박 간격(초)
        private float nextBeatTime;         // Time.time 기준 다음 비트(타이머 모드)
        private float nextHalfTime;         // Time.time 기준 하프 비트(타이머 모드)

        private bool isRunning;

        // 오디오 동기화용
        private double dspSongStart;        // DSP 기준 곡 시작시각
        private double nextBeatDSP;         // DSP 기준 다음 비트 시각
        private double nextHalfDSP;         // DSP 기준 다음 하프비트 시각

        void Awake()
        {
            RecalcInterval();
        }

        void Start()
        {
            if (autoStart) StartBeat();
        }

        /// <summary>비트를 시작한다. 오디오가 있으면 DSP 스케줄, 없으면 타이머 시작.</summary>
        public void StartBeat()
        {
            isRunning = true;

            if (syncWithAudio && audioSource != null)
            {
                // DSP 기반으로 정확히 시작
                dspSongStart = AudioSettings.dspTime + audioStartDelay;
#if UNITY_6000_0_OR_NEWER
                audioSource.PlayScheduled(dspSongStart);
#else
                audioSource.PlayScheduled(dspSongStart);
#endif
                nextBeatDSP = dspSongStart + BeatIntervalSeconds;
                nextHalfDSP = dspSongStart + BeatIntervalSeconds * 0.5;
            }
            else
            {
                // 일반 타이머 모드
                float t = Time.time;
                nextBeatTime = t + BeatIntervalSeconds;
                nextHalfTime = t + BeatIntervalSeconds * 0.5f;
            }
        }

        public void StopBeat()
        {
            isRunning = false;
            if (audioSource != null && audioSource.isPlaying)
                audioSource.Stop();
        }

        void Update()
        {
            if (!isRunning) return;

            if (syncWithAudio && audioSource != null)
            {
                double now = AudioSettings.dspTime;

                if (now >= nextBeatDSP)
                {
                    OnBeat?.Invoke();
                    nextBeatDSP += BeatIntervalSeconds;
                }

                if (now >= nextHalfDSP)
                {
                    OnHalfBeat?.Invoke();
                    nextHalfDSP += BeatIntervalSeconds;
                }
            }
            else
            {
                float now = Time.time;

                if (now >= nextBeatTime)
                {
                    OnBeat?.Invoke();
                    nextBeatTime += BeatIntervalSeconds;
                }

                if (now >= nextHalfTime)
                {
                    OnHalfBeat?.Invoke();
                    nextHalfTime += BeatIntervalSeconds;
                }
            }
        }

        /// <summary>BPM 변경(실행 중 호출 가능). 다음 박자 기준 재계산.</summary>
        public void SetBPM(float newBpm)
        {
            bpm = Mathf.Max(30f, newBpm);
            RecalcInterval();

            if (syncWithAudio && audioSource != null)
            {
                // 오디오 동기화 모드에서는 다음 박자를 DSP 기준으로 재설정
                double now = AudioSettings.dspTime;
                // 직전 비트를 추정하여 가장 가까운 미래 박자로 스냅
                double beatsFromStart = (now - dspSongStart) / BeatIntervalSeconds;
                double nextIndex = Mathf.Ceil((float)beatsFromStart) + 1.0; // 다음 박자
                nextBeatDSP = dspSongStart + nextIndex * BeatIntervalSeconds;
                nextHalfDSP = nextBeatDSP - BeatIntervalSeconds * 0.5f;
            }
            else
            {
                float now = Time.time;
                nextBeatTime = now + BeatIntervalSeconds;
                nextHalfTime = now + BeatIntervalSeconds * 0.5f;
            }
        }

        private void RecalcInterval()
        {
            beatInterval = 60f / Mathf.Max(1f, bpm);
        }

        /// <summary>
        /// 지금 시각 기준 가장 가까운 박자까지의 절대 오차(초).
        /// 오디오 동기화 모드일 때는 DSP시간 기준으로 계산.
        /// </summary>
        public float GetOffsetToNearestBeat(float currentTime)
        {
            if (syncWithAudio && audioSource != null)
            {
                // currentTime이 Time.time으로 들어오므로 DSP로 변환 불가 → DSP 현재시간 사용
                double now = AudioSettings.dspTime;
                double beatsFromStart = (now - dspSongStart) / BeatIntervalSeconds;
                double nearestIndex = System.Math.Round(beatsFromStart);
                double nearestBeatTime = dspSongStart + nearestIndex * BeatIntervalSeconds;
                return Mathf.Abs((float)(nearestBeatTime - now));
            }
            else
            {
                // 타이머 기준: lastBeatTime 추정 후 가장 가까운 박자 계산
                float lastBeatTime = nextBeatTime - BeatIntervalSeconds;
                float beatsFromStart = (currentTime - lastBeatTime) / BeatIntervalSeconds;
                float nearestIndex = Mathf.Round(beatsFromStart);
                float nearestBeatTime = lastBeatTime + nearestIndex * BeatIntervalSeconds;
                return Mathf.Abs(nearestBeatTime - currentTime);
            }
        }

        // 접근자
        public float BeatIntervalSeconds => beatInterval;
        public float BPM => bpm;

        // 인스펙터 편의용
        public void SetAudioSource(AudioSource src) => audioSource = src;
        public void SetSyncWithAudio(bool on) => syncWithAudio = on;
    }
}
