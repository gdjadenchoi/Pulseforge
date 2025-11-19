using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pulseforge.Systems
{
    public class SessionController : MonoBehaviour
    {
        [Header("Session Time")]
        [Min(1f)] public float baseDurationSec = 15f;
        [Min(0f)] public float criticalThreshold = 3f;

        [Header("Scene Refs (Optional)")]
        public Transform oreRootOverride;
        public Behaviour drillCursor;   // DrillCursor 컴포넌트 넣어두면 됨

        // ----- 상태 & 프로퍼티 -----
        public bool IsRunning { get; private set; }
        public float Remaining { get; private set; }

        // remaining, normalized(0~1)
        public event Action OnSessionStart;
        public event Action OnSessionEnd;
        public event Action<float, float> OnTimeChanged;
        public event Action<float> OnCritical;   // 크리티컬 순간 한 번 호출

        bool _firedCritical;
        RewardManager _rewards;
        OreSpawner _spawner;

        void Awake()
        {
            _rewards = RewardManager.Instance;
#if UNITY_6000_0_OR_NEWER
            _spawner = FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Exclude);
#else
            _spawner = FindObjectOfType<OreSpawner>();
#endif
        }

        void Start()
        {
            StartSession();
        }

        void Update()
        {
            if (!IsRunning) return;

            Remaining -= Time.deltaTime;
            if (Remaining < 0f) Remaining = 0f;

            float normalized = baseDurationSec <= 0f
                ? 0f
                : Mathf.Clamp01(Remaining / baseDurationSec);

            OnTimeChanged?.Invoke(Remaining, normalized);

            if (!_firedCritical && Remaining <= criticalThreshold)
            {
                _firedCritical = true;
                OnCritical?.Invoke(Remaining);
            }

            if (Remaining <= 0f)
            {
                EndSession();
            }
        }

        // ----- 세션 제어 -----

        public void StartSession()
        {
            Remaining = baseDurationSec;
            _firedCritical = false;
            IsRunning = true;

            if (drillCursor) drillCursor.enabled = true;

            OnSessionStart?.Invoke();
        }

        public void EndSession()
        {
            if (!IsRunning) return;

            Debug.Log("[SessionController] EndSession() called");  // ★ 추가 1

            IsRunning = false;
            if (drillCursor) drillCursor.enabled = false;

            // 보상 스냅샷
            IReadOnlyDictionary<RewardType, int> snapshot =
                _rewards != null ? _rewards.GetAll() : new Dictionary<RewardType, int>();

#if UNITY_6000_0_OR_NEWER
            var popup = FindFirstObjectByType<Pulseforge.UI.SessionEndPopup>(FindObjectsInactive.Include);
#else
            var popup = FindObjectOfType<Pulseforge.UI.SessionEndPopup>(true);
#endif

            Debug.Log("[SessionController] popup found? " + (popup != null)); // ★ 추가 2

            if (popup != null)
            {
                popup.Show(snapshot, this);
            }

            ClearWorld();
            OnSessionEnd?.Invoke();
        }

        // Ore 정리 (OreSpawner가 사용하는 root와 맞춰줌)
        public void ClearWorld()
        {
            Transform root =
                oreRootOverride != null ? oreRootOverride :
                _spawner ? _spawner.transform :
                null;

            if (!root) return;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Destroy(root.GetChild(i).gameObject);
            }
        }

        // 씬 전체 리스타트용(지금은 안 써도 됨, 남겨둠)
        public void RestartSession()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // 업그레이드 씬 전환용(나중에 연결 예정)
        public void GoToUpgrade()
        {
            Debug.Log("[SessionController] GoToUpgrade() — 추후 씬 전환 연결 예정");
        }
    }
}
