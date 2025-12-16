﻿using System;
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

        public bool IsRunning { get; private set; }
        public float Remaining { get; private set; }

        public event Action OnSessionStart;
        public event Action OnSessionEnd;
        public event Action<float, float> OnTimeChanged; // (remainingSec, normalized 0~1)
        public event Action<float> OnCritical;

        private bool _firedCritical;
        private OreSpawner _spawner;
        private RewardManager _rewards;

        void Awake()
        {
            CacheSceneRefs();

            // 시작 상태에서는 세션이 "미시작"이어야 한다(커서 비활성).
            ApplyNonRunningStateForScene(SceneManager.GetActiveScene());
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 씬이 바뀔 때마다(Outpost -> Mining 포함) 레퍼런스 재확보
            CacheSceneRefs();

            // 씬 진입 시점의 기본 상태(세션 미시작)를 확실히 고정한다.
            ApplyNonRunningStateForScene(scene);

            // 타임스케일은 SessionController가 중앙 관리한다.
            EnsureTimeScaleForGameplayScene(scene);
        }

        private void CacheSceneRefs()
        {
            _rewards = RewardManager.Instance;

#if UNITY_6000_0_OR_NEWER
            _spawner = FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Exclude);
#else
            _spawner = FindObjectOfType<OreSpawner>();
#endif

            // drillCursor를 인스펙터에 안 꽂았으면, Mining 씬에서 자동으로 찾아서 채워줌
            if (drillCursor == null)
            {
#if UNITY_6000_0_OR_NEWER
                drillCursor = FindFirstObjectByType<DrillCursor>(FindObjectsInactive.Exclude);
#else
                drillCursor = FindObjectOfType<DrillCursor>();
#endif
            }
        }

        void Update()
        {
            if (!IsRunning) return;

            Remaining -= Time.deltaTime;
            if (Remaining < 0f) Remaining = 0f;

            float normalized =
                baseDurationSec <= 0f
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

        // =====================================================================
        //  세션 오케스트레이션 (줌 완료 후 호출됨)
        // =====================================================================

        /// <summary>
        /// "이번 세션을 새로 시작"하는 표준 진입점.
        /// - OreSpawner.StartFresh() 호출(월드 정리 + 스폰 + 리스폰 루프)
        /// - 세션 타이머(StartSession) 시작
        /// - 커서는 StartSession에서 활성화
        /// </summary>
        public void BeginSessionFlow()
        {
            Debug.Log("[SessionController] BeginSessionFlow()");

            // 세션 시작은 항상 정상 timeScale에서 진행되어야 한다.
            if (Mathf.Approximately(Time.timeScale, 0f))
                Time.timeScale = 1f;

            // 안전장치: Outpost에서 null 잡고 살아남아도, Mining 들어온 뒤 다시 찾게
            if (_spawner == null)
            {
#if UNITY_6000_0_OR_NEWER
                _spawner = FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Exclude);
#else
                _spawner = FindObjectOfType<OreSpawner>();
#endif
            }

            // 1) 광석 스패너가 있다면, 새 세션용으로 초기화 + 스폰
            if (_spawner != null)
            {
                _spawner.StartFresh();
            }
            else
            {
                Debug.LogWarning("[SessionController] OreSpawner not found. Cannot begin session.");
                return;
            }

            // 2) 타이머 + 커서 활성
            StartSession();
        }

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

            // 화면 클리어 + 리스폰 정지
            ClearWorld();

            if (popup != null)
            {
                popup.Show(snapshot, this);
            }

            OnSessionEnd?.Invoke();
        }

        private void ClearWorld()
        {
            if (_spawner != null)
            {
                // ✅ OreSpawner의 실제 메서드명은 StopSpawner()
                _spawner.StopSpawner();

                // oreRootOverride가 있으면 그쪽만 정리
                Transform root = oreRootOverride != null ? oreRootOverride : _spawner.transform;
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    Destroy(root.GetChild(i).gameObject);
                }
            }
        }

        // =====================================================================
        //  TimeScale / 초기 상태 관리 (중앙집중)
        // =====================================================================

        /// <summary>
        /// 씬 진입 시점에 "세션 미시작" 상태를 강제로 고정한다.
        /// </summary>
        private void ApplyNonRunningStateForScene(Scene scene)
        {
            if (!IsRunning && drillCursor)
                drillCursor.enabled = false;
        }

        /// <summary>
        /// PF_Mining에서는 timeScale==0이면 줌/스폰/타이머가 모두 멈춰 보일 수 있으므로
        /// 씬 진입 시점에 기본값(1)으로 복구한다.
        /// </summary>
        private void EnsureTimeScaleForGameplayScene(Scene scene)
        {
            if (scene.name == "PF_Mining")
            {
                if (Mathf.Approximately(Time.timeScale, 0f))
                    Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// 외부에서 일시정지가 필요할 때는 여기로만 들어오도록 한다(정책).
        /// </summary>
        public void SetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
        }

        public void RestartSession()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void GoToUpgrade()
        {
            Debug.Log("[SessionController] GoToUpgrade() — 추후 씬 전환 연결 예정");
        }
    }
}
