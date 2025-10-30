using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 보상(자원) 누적 매니저. 씬을 넘어 유지되는 싱글톤.
    /// 기존 HUD 호환을 위해 OnResourceChanged, GetAll()도 제공.
    /// </summary>
    public class RewardManager : MonoBehaviour
    {
        // ── Singleton ───────────────────────────────────────────────────────────
        private static RewardManager _instance;
        public static RewardManager Instance => _instance;

        /// <summary>씬 어디에 있든 안전하게 찾아서 반환(없으면 null)</summary>
        public static RewardManager SafeInstance
        {
            get
            {
                if (_instance != null) return _instance;
#if UNITY_2023_1_OR_NEWER
                var found = FindFirstObjectByType<RewardManager>();
#else
                var found = FindObjectOfType<RewardManager>();
#endif
                if (found != null) _instance = found;
                return _instance;
            }
        }

        // ── Events (신규 & 호환) ────────────────────────────────────────────────
        /// <summary>신규 UnityEvent (type, currentAmount)</summary>
        public UnityEvent<RewardType, int> OnChanged = new();

        /// <summary>
        /// ⛳ HUD 호환용(C# event). 일부 스크립트가 이 이름으로 구독하고 있을 수 있음.
        /// </summary>
        public event Action<RewardType, int> OnResourceChanged;

        // ── Data ────────────────────────────────────────────────────────────────
        private readonly Dictionary<RewardType, int> _amounts = new();

        // ── Lifecycle ───────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // 루트로 이동 + 씬 유지
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        // ── API ─────────────────────────────────────────────────────────────────
        public int Get(RewardType type) =>
            _amounts.TryGetValue(type, out var v) ? v : 0;

        public void Set(RewardType type, int value)
        {
            value = Mathf.Max(0, value);
            _amounts[type] = value;
            FireEvents(type, value);
        }

        public void Add(RewardType type, int delta)
        {
            if (delta <= 0) return;
            var now = Get(type) + delta;
            _amounts[type] = now;
            FireEvents(type, now);
        }

        /// <summary>⛳ HUD 호환용: 전체 보유량 읽기</summary>
        public IReadOnlyDictionary<RewardType, int> GetAll() => _amounts;

        public void ClearAll()
        {
            _amounts.Clear();
            // 필요하면 전체 초기화 알림 로직 추가
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private void FireEvents(RewardType type, int current)
        {
            OnChanged?.Invoke(type, current);          // UnityEvent
            OnResourceChanged?.Invoke(type, current);  // C# event (호환)
        }
    }
}
