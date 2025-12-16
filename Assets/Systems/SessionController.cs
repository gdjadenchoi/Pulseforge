﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Pulseforge.UI;

namespace Pulseforge.Systems
{
    public class SessionController : MonoBehaviour
    {
        [Header("Session Time")]
        [Min(1f)] public float baseDurationSec = 15f;
        [Min(0f)] public float criticalThreshold = 3f;

        [Header("Scene Refs (Optional)")]
        public Transform oreRootOverride;

        [Tooltip("DrillCursor 컴포넌트(Behaviour)")]
        public Behaviour drillCursor;

        [Header("Intro Flow (Zoom -> Spawn -> StartText -> Cursor+Timer)")]
        [Min(0f)] [SerializeField] private float introSpawnStableSec = 0.15f;
        [Min(0.1f)] [SerializeField] private float introMaxWaitSec = 3.0f;

        [Header("Intro Presentation")]
        [SerializeField] private bool hideCursorOnSceneEnter = true;

        [Tooltip("스폰 끝 -> Start 텍스트 전 텀")]
        [Min(0f)] [SerializeField] private float delayAfterSpawn = 0.12f;

        [Tooltip("Start 텍스트 연출이 끝난 뒤, 실제 플레이(커서+타이머) 시작 전 텀")]
        [Min(0f)] [SerializeField] private float delayBeforeGameplay = 0.05f;

        [Header("Start Text Presenter (TMP)")]
        [SerializeField] private StartTextPresenter startTextPresenter;

        // ✅ Start는 1번만
        [SerializeField] private string startTextSingle = "Start!";

        // ----- 상태 -----
        public bool IsRunning { get; private set; }
        public float Remaining { get; private set; }

        // remaining, normalized(0~1)
        public event Action OnSessionStart;
        public event Action OnSessionEnd;
        public event Action<float, float> OnTimeChanged;
        public event Action<float> OnCritical;

        private bool _firedCritical;
        private RewardManager _rewards;
        private OreSpawner _spawner;

        private Coroutine _beginFlowRoutine;
        private bool _beginFlowInProgress;

        void Awake()
        {
            CacheSceneRefs();
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
            CacheSceneRefs();

            if (hideCursorOnSceneEnter)
                SetCursorVisible(false);

            // Start 텍스트가 혹시 켜져있으면 즉시 숨김
            if (startTextPresenter != null)
                startTextPresenter.HideImmediate();
        }

        private void CacheSceneRefs()
        {
            _rewards = RewardManager.Instance;

#if UNITY_6000_0_OR_NEWER
            _spawner = FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Exclude);
#else
            _spawner = FindObjectOfType<OreSpawner>();
#endif

            if (drillCursor == null)
            {
#if UNITY_6000_0_OR_NEWER
                drillCursor = FindFirstObjectByType<DrillCursor>(FindObjectsInactive.Include);
#else
                drillCursor = FindObjectOfType<DrillCursor>(true);
#endif
            }
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
                EndSession();
        }

        // =====================================================================
        //  Intro Flow
        // =====================================================================

        public void BeginSessionFlow()
        {
            // ✅ 중복 호출 방지
            if (_beginFlowInProgress) return;
            _beginFlowInProgress = true;

            Debug.Log("[SessionController] BeginSessionFlow()");

            if (_beginFlowRoutine != null)
            {
                StopCoroutine(_beginFlowRoutine);
                _beginFlowRoutine = null;
            }

            // 세션 정지 + 커서 숨김
            IsRunning = false;
            SetCursorVisible(false);

            if (startTextPresenter != null)
                startTextPresenter.HideImmediate();

            // spawner 재확보
            if (_spawner == null)
            {
#if UNITY_6000_0_OR_NEWER
                _spawner = FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Exclude);
#else
                _spawner = FindObjectOfType<OreSpawner>();
#endif
            }

            if (_spawner == null)
            {
                Debug.LogWarning("[SessionController] OreSpawner not found.");
                _beginFlowInProgress = false;
                return;
            }

            // 1) 스폰 시작
            _spawner.StartFresh();

            // 2) 스폰 안정화 -> 텍스트 1회 -> 커서+타이머 시작
            _beginFlowRoutine = StartCoroutine(CoIntroThenGameplay());
        }

        private IEnumerator CoIntroThenGameplay()
        {
            // --- 스폰 안정화 대기 ---
            Transform root = _spawner != null ? _spawner.transform : null;
            if (root != null)
            {
                float maxWait = Mathf.Max(0.1f, introMaxWaitSec);
                float stableNeed = Mathf.Max(0f, introSpawnStableSec);

                float elapsed = 0f;

                // 최소 1개라도 나올 때까지
                while (root.childCount <= 0 && elapsed < maxWait)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                // 증가 멈춤 안정화
                float stable = 0f;
                int lastCount = root.childCount;

                while (elapsed < maxWait)
                {
                    int cur = root.childCount;

                    if (cur != lastCount)
                    {
                        lastCount = cur;
                        stable = 0f;
                    }
                    else
                    {
                        stable += Time.unscaledDeltaTime;
                        if (stableNeed <= 0f || stable >= stableNeed)
                            break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // --- 스폰 -> 숨고르기 ---
            if (delayAfterSpawn > 0f)
                yield return new WaitForSecondsRealtime(delayAfterSpawn);

            // --- Start! (연출 1회) ---
            if (startTextPresenter != null)
                yield return StartCoroutine(startTextPresenter.PlayRoutine(startTextSingle));

            // --- 텍스트 끝난 뒤 텀 ---
            if (delayBeforeGameplay > 0f)
                yield return new WaitForSecondsRealtime(delayBeforeGameplay);

            // ✅ 여기서부터 실제 플레이 시작: 커서 + 타이머 (동시에)
            SetCursorVisible(true);
            StartSession();

            _beginFlowRoutine = null;
            _beginFlowInProgress = false;
        }

        // =====================================================================
        //  Session Timer
        // =====================================================================

        public void StartSession()
        {
            Remaining = baseDurationSec;
            _firedCritical = false;
            IsRunning = true;

            // ✅ StartTextPresenter는 여기에 절대 연결하지 마(중복 원인)
            OnSessionStart?.Invoke();
        }

        public void EndSession()
        {
            if (!IsRunning) return;

            IsRunning = false;
            SetCursorVisible(false);

            IReadOnlyDictionary<RewardType, int> snapshot =
                _rewards != null ? _rewards.GetAll() : new Dictionary<RewardType, int>();

#if UNITY_6000_0_OR_NEWER
            var popup = FindFirstObjectByType<Pulseforge.UI.SessionEndPopup>(FindObjectsInactive.Include);
#else
            var popup = FindObjectOfType<Pulseforge.UI.SessionEndPopup>(true);
#endif

            ClearWorld();

            if (popup != null)
                popup.Show(snapshot, this);

            OnSessionEnd?.Invoke();

            _beginFlowInProgress = false;
        }

        public void RestartSessionFromPopup()
        {
            BeginSessionFlow();
        }

        public void ClearWorld()
        {
            if (_spawner != null)
            {
                _spawner.PauseAndClear();
                return;
            }

            Transform root = oreRootOverride != null ? oreRootOverride : null;
            if (!root) return;

            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        // =====================================================================
        //  Cursor visibility (완전 비표시)
        // =====================================================================

        private void SetCursorVisible(bool visible)
        {
            if (drillCursor == null) return;

            GameObject go = drillCursor.gameObject;

            if (go.activeSelf != visible)
                go.SetActive(visible);

            drillCursor.enabled = visible;
        }
    }
}
