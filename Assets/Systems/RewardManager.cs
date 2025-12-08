using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 보상(자원) 누적 매니저. 씬을 넘어 유지되는 싱글톤.
    /// PlayerPrefs 를 사용해 재화 상태를 저장/로드한다.
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

        [Header("디버그 옵션")]
        [SerializeField] private bool _logRewards = false;

        // ── 저장 관련 (PlayerPrefs) ─────────────────────────────────────────────
        private const string PlayerPrefsKey = "PF_Rewards_v1";

        [Serializable]
        private struct RewardSaveEntry
        {
            public RewardType type;
            public int amount;
        }

        [Serializable]
        private struct RewardSaveData
        {
            public RewardSaveEntry[] entries;
        }

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

            // 저장된 재화 상태 먼저 로드 시도 (없으면 전부 0으로 시작)
            TryLoadFromPlayerPrefs();
        }

        private void OnApplicationQuit()
        {
            SaveToPlayerPrefs();
        }

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>현재 타입의 보유량을 반환 (없으면 0)</summary>
        public int Get(RewardType type) =>
            _amounts.TryGetValue(type, out var v) ? v : 0;

        /// <summary>보유량을 강제로 설정 (0 미만이면 0으로 클램프)</summary>
        public void Set(RewardType type, int value)
        {
            value = Mathf.Max(0, value);
            _amounts[type] = value;
            FireEvents(type, value);
            SaveToPlayerPrefs();
        }

        /// <summary>보유량에 delta만큼 추가 (delta &gt; 0 인 경우에만)</summary>
        public void Add(RewardType type, int delta)
        {
            if (delta <= 0) return;
            var now = Get(type) + delta;
            _amounts[type] = now;
            FireEvents(type, now);
            SaveToPlayerPrefs();
        }

        /// <summary>⛳ HUD 호환용: 전체 보유량 읽기 (읽기 전용 Dictionary)</summary>
        public IReadOnlyDictionary<RewardType, int> GetAll() => _amounts;

        /// <summary>
        /// ✅ 세션 초기화용: 메모리 상 재화만 0으로 만든다.
        /// PlayerPrefs 저장 데이터는 건드리지 않는다.
        /// (기존 ClearAll 과 의미 동일하게 유지)
        /// </summary>
        public void ClearAll()
        {
            var keys = new List<RewardType>(_amounts.Keys);
            _amounts.Clear();

            // UI/HUD 갱신을 위해 0으로 이벤트 쏴줌
            foreach (var type in keys)
            {
                FireEvents(type, 0);
            }

            // ❌ 여기서 SaveToPlayerPrefs() 호출하지 않는다.
            //    → 기존 코드에서 ClearAll 은 "런타임 초기화" 의미였기 때문.
            if (_logRewards)
                Debug.Log("[RewardManager] ClearAll() 호출됨");
        }

        /// <summary>
        /// 🔥 완전 리셋용: PlayerPrefs 저장 데이터까지 모두 삭제.
        /// 인스펙터 ContextMenu 나 디버그 버튼에서만 쓰는 걸 권장.
        /// </summary>
        [ContextMenu("Reset All Rewards")]
        public void ResetAllRewards()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);

            // 메모리도 0으로
            ClearAll();

            // 0 상태를 새로 저장해서 일관성 유지 (선택 사항이지만 넣어 둠)
            SaveToPlayerPrefs();

            if (_logRewards)
                Debug.Log("[RewardManager] 모든 재화 리셋 + 저장 데이터 삭제 완료");
        }

        // ── 저장 / 로드 ─────────────────────────────────────────────────────────

        private bool TryLoadFromPlayerPrefs()
        {
            if (!PlayerPrefs.HasKey(PlayerPrefsKey))
                return false;

            string json = PlayerPrefs.GetString(PlayerPrefsKey, null);
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                var data = JsonUtility.FromJson<RewardSaveData>(json);
                if (data.entries == null || data.entries.Length == 0)
                    return false;

                _amounts.Clear();

                foreach (var entry in data.entries)
                {
                    int amount = Mathf.Max(0, entry.amount);
                    _amounts[entry.type] = amount;
                }

                if (_logRewards)
                    Debug.Log($"[RewardManager] 재화 상태 로드 완료: {_amounts.Count}개 타입");

                // 로드된 값으로 HUD 갱신
                foreach (var kvp in _amounts)
                {
                    FireEvents(kvp.Key, kvp.Value);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RewardManager] 재화 로드 중 오류 발생: {ex}");
                return false;
            }
        }

        private void SaveToPlayerPrefs()
        {
            try
            {
                var list = new List<RewardSaveEntry>(_amounts.Count);
                foreach (var kvp in _amounts)
                {
                    // 0인 건 굳이 저장 안 해도 되니 스킵
                    if (kvp.Value <= 0)
                        continue;

                    list.Add(new RewardSaveEntry
                    {
                        type = kvp.Key,
                        amount = kvp.Value
                    });
                }

                var data = new RewardSaveData
                {
                    entries = list.ToArray()
                };

                string json = JsonUtility.ToJson(data);
                PlayerPrefs.SetString(PlayerPrefsKey, json);
                PlayerPrefs.Save();

                if (_logRewards)
                    Debug.Log($"[RewardManager] 재화 상태 저장 완료: {list.Count}개 타입");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RewardManager] 재화 저장 중 오류 발생: {ex}");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private void FireEvents(RewardType type, int current)
        {
            OnChanged?.Invoke(type, current);          // UnityEvent
            OnResourceChanged?.Invoke(type, current);  // C# event (호환)
        }
    }
}
