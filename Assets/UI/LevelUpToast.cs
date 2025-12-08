using System.Collections;
using TMPro;
using UnityEngine;
using Pulseforge.Systems;

namespace Pulseforge.UI
{
    /// <summary>
    /// 레벨업 시 잠깐 떠서 보여주는 토스트 UI
    /// - LevelManager.OnLevelUp 이벤트를 구독해서 동작
    /// - 알파 페이드 + 살짝 위로 떠오르는 연출
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class LevelUpToast : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private TMP_Text messageText;      // "Level Up! Lv. 3" 같은 텍스트
        [SerializeField] private CanvasGroup canvasGroup;   // 알파 페이드용
        [SerializeField] private RectTransform moveTarget;  // 위로 살짝 이동시킬 RectTransform (보통 자기 자신)

        [Header("Animation")]
        [SerializeField] private float fadeInDuration = 0.15f;
        [SerializeField] private float stayDuration   = 0.85f;
        [SerializeField] private float fadeOutDuration = 0.5f;
        [SerializeField] private float moveUpDistance = 40f;
        [SerializeField] private AnimationCurve moveCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Behavior")]
        [SerializeField] private bool disableIfNoManager = true;

        private LevelManager _levelManager;
        private Coroutine _routine;
        private Vector2 _initialAnchoredPos;

        private void Awake()
        {
            // 🔹 인스펙터에서 안 넣어줘도 자동으로 할당되도록 처리
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (moveTarget == null)
                moveTarget = GetComponent<RectTransform>();

            if (moveTarget != null)
                _initialAnchoredPos = moveTarget.anchoredPosition;

            // 시작할 때는 항상 안 보이게
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable   = false;
                canvasGroup.blocksRaycasts = false;
            }
            else
            {
                Debug.LogWarning("[LevelUpToast] CanvasGroup not found. Toast will not be visible.", this);
            }
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        // -----------------------------
        // 이벤트 구독 / 해제
        // -----------------------------
        private void TrySubscribe()
        {
            _levelManager = LevelManager.Instance ?? FindAnyManager();

            if (_levelManager == null)
            {
                Debug.LogWarning("[LevelUpToast] LevelManager not found. Toast will not work.", this);
                if (disableIfNoManager)
                    enabled = false;
                return;
            }

            _levelManager.OnLevelUp -= HandleLevelUp;
            _levelManager.OnLevelUp += HandleLevelUp;
        }

        private LevelManager FindAnyManager()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<LevelManager>();
#else
            return FindObjectOfType<LevelManager>();
#endif
        }

        private void Unsubscribe()
        {
            if (_levelManager != null)
                _levelManager.OnLevelUp -= HandleLevelUp;
        }

        // -----------------------------
        // 콜백 & 연출
        // -----------------------------
        private void HandleLevelUp(int newLevel)
        {
            if (!isActiveAndEnabled)
                return;

            Debug.Log($"[LevelUpToast] OnLevelUp received: {newLevel}", this);

            if (_routine != null)
                StopCoroutine(_routine);

            _routine = StartCoroutine(PlayToastRoutine(newLevel));
        }

        private IEnumerator PlayToastRoutine(int newLevel)
        {
            if (canvasGroup == null)
                yield break;

            // 텍스트 세팅
            if (messageText != null)
                messageText.text = $"Level Up! Lv. {newLevel}";

            // 위치 초기화
            if (moveTarget != null)
                moveTarget.anchoredPosition = _initialAnchoredPos;

            // --- 페이드 인 + 위로 이동 ---
            float t = 0f;
            while (t < fadeInDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / fadeInDuration);

                canvasGroup.alpha = k;

                if (moveTarget != null)
                {
                    float moveK = moveCurve.Evaluate(k);
                    moveTarget.anchoredPosition =
                        _initialAnchoredPos + Vector2.up * (moveUpDistance * moveK);
                }

                yield return null;
            }

            // --- 유지 구간 ---
            canvasGroup.alpha = 1f;
            float stayT = 0f;
            while (stayT < stayDuration)
            {
                stayT += Time.unscaledDeltaTime;
                yield return null;
            }

            // --- 페이드 아웃 ---
            float outT = 0f;
            while (outT < fadeOutDuration)
            {
                outT += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(outT / fadeOutDuration);
                canvasGroup.alpha = k;
                yield return null;
            }

            canvasGroup.alpha = 0f;
            _routine = null;
        }
    }
}
