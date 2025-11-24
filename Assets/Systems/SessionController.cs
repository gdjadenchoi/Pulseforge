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
            // 처음 씬에 들어왔을 때 바로 세션 시작
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

            Debug.Log("[SessionController] EndSession() called");

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

            Debug.Log("[SessionController] popup found? " + (popup != null));

            // *** 여기서는 "화면 클리어 + 리스폰 정지" 까지만 ***
            ClearWorld();

            if (popup != null)
            {
                // 팝업은 클리어된 화면 위에만 뜨고,
                // MineAgain 버튼을 눌러야만 다시 스폰이 시작되도록 controller를 넘긴다.
                popup.Show(snapshot, this);
            }

            OnSessionEnd?.Invoke();
        }

        /// <summary>
        /// MineAgain 버튼에서 호출할 재시작 전용 함수.
        /// 순서: 팝업에서 이 함수 호출 → OreSpawner.StartFresh() → 세션 타이머 StartSession()
        /// </summary>
        public void RestartSessionFromPopup()
        {
            Debug.Log("[SessionController] RestartSessionFromPopup() called");

            // 1) 광석 스패너를 다시 활성화 + 초기 스폰
            if (_spawner != null)
            {
                _spawner.StartFresh();
            }
            else
            {
                // 혹시라도 스패너를 못 찾는 상황이면 최소한 기존 월드는 정리
                ClearWorld();
            }

            // 2) 세션 타이머 재시작
            StartSession();
        }

        // Ore 정리 (OreSpawner와 연동되도록 우선 사용)
        public void ClearWorld()
        {
            // 스패너가 있으면 스패너에게 "멈추고, 다 지우고, 리스폰도 멈춰" 라고만 시킨다.
            if (_spawner != null)
            {
                _spawner.PauseAndClear();
                return;
            }

            // 예외적으로 oreRootOverride만 사용해야 하는 경우를 대비한 폴백
            Transform root =
                oreRootOverride != null ? oreRootOverride : null;

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
