using System;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 세션 단위 타이머를 관리.
    /// - 기본 시간 + 업그레이드 추가시간
    /// - OnSessionStart / OnTimeChanged / OnCritical / OnSessionEnd 이벤트 발행
    /// </summary>
    public class SessionController : MonoBehaviour
    {
        [Header("Session Settings")]
        [Tooltip("기본 세션 시간 (초)")]
        public float baseDuration = 15f;

        [Tooltip("업그레이드로 추가된 시간 (초)")]
        public float extraDuration = 0f;

        [Tooltip("자동으로 시작할지 여부")]
        public bool autoStart = true;

        [Tooltip("3초 이하일 때 경고 상태로 진입")]
        public float criticalThreshold = 3f;

        [Tooltip("시간 흐름 배속 (디버그용)")]
        public float timeScale = 1f;

        [Tooltip("포커스를 잃으면 일시정지")]
        public bool pauseOnFocusLost = true;

        public event Action OnSessionStart;
        public event Action<float, float> OnTimeChanged;
        public event Action<float> OnCritical;
        public event Action OnSessionEnd;

        private float _remaining;
        private bool _isRunning;
        private bool _criticalTriggered;
        private bool _paused;

        public float Remaining => Mathf.Max(0, _remaining);
        public float Duration => baseDuration + extraDuration;
        public bool IsRunning => _isRunning;

        private void Start()
        {
            if (autoStart)
                StartSession();
        }

        private void Update()
        {
            if (!_isRunning || _paused) return;

            _remaining -= Time.deltaTime * timeScale;
            _remaining = Mathf.Max(0, _remaining);

            float normalized = Mathf.Clamp01(_remaining / Duration);
            OnTimeChanged?.Invoke(_remaining, normalized);

            if (!_criticalTriggered && _remaining <= criticalThreshold)
            {
                _criticalTriggered = true;
                OnCritical?.Invoke(_remaining);
            }

            if (_remaining <= 0)
            {
                EndSession();
            }
        }

        public void StartSession()
        {
            _remaining = Duration;
            _isRunning = true;
            _criticalTriggered = false;
            _paused = false;

            OnSessionStart?.Invoke();
            OnTimeChanged?.Invoke(_remaining, 1f);
        }

        public void EndSession()
        {
            if (!_isRunning) return;
            _isRunning = false;
            OnSessionEnd?.Invoke();
        }

        public void RestartSession()
        {
            StartSession();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!pauseOnFocusLost) return;
            _paused = !hasFocus;
        }
    }
}
