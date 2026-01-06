using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Pulseforge.Systems;   // RewardType, SessionController

namespace Pulseforge.UI
{
    /// <summary>
    /// 세션 종료 시 표시되는 결과 요약 팝업.
    /// - 이번 세션에서 얻은 보상 목록을 문자열로 만들어 보여준다.
    /// - Mine Again: "세션 플로우"로 재시작(월드 정리/스폰/인트로 포함)
    /// - Upgrade : 아웃포스트(PF_Outpost) 씬으로 이동
    /// </summary>
    public class SessionEndPopup : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("팝업 전체를 담고 있는 루트(패널) 오브젝트")]
        public GameObject panel;

        [Tooltip("보상 요약을 표시할 TextMeshProUGUI")]
        public TMP_Text summaryText;

        [Tooltip("다시 채굴하기 버튼")]
        public Button mineAgainButton;

        [Tooltip("업그레이드 화면으로 이동 버튼 (PF_Outpost 씬 로드)")]
        public Button upgradeButton;

        private CanvasGroup _canvasGroup;
        private SessionController _controller;

        private void Awake()
        {
            if (panel == null)
                panel = gameObject;

            _canvasGroup = GetComponent<CanvasGroup>();

            HideImmediate();

            if (mineAgainButton != null)
                mineAgainButton.onClick.AddListener(OnClickMineAgain);

            if (upgradeButton != null)
                upgradeButton.onClick.AddListener(OnClickUpgrade);
        }

        /// <summary>
        /// 팝업을 즉시 숨기기 (애니메이션 없이)
        /// </summary>
        public void HideImmediate()
        {
            gameObject.SetActive(false);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        /// <summary>
        /// 세션 종료 시 SessionController에서 호출되는 진입 함수.
        /// </summary>
        public void Show(IReadOnlyDictionary<RewardType, int> rewards, SessionController session)
        {
            _controller = session;

            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }

            if (summaryText != null)
            {
                if (rewards == null || rewards.Count == 0)
                {
                    summaryText.text = "이번 세션에서는 아무 보상도 얻지 못했습니다.";
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("이번 세션 보상");

                    foreach (var kv in rewards)
                        sb.AppendLine($"{kv.Key}: {kv.Value}");

                    summaryText.text = sb.ToString();
                }
            }

            // 버튼 리스너 정리
            if (mineAgainButton != null)
            {
                mineAgainButton.onClick.RemoveAllListeners();
                mineAgainButton.onClick.AddListener(OnClickMineAgain);
            }

            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveAllListeners();
                upgradeButton.onClick.AddListener(OnClickUpgrade);
            }
        }

        /// <summary>
        /// "다시 채굴하기" 버튼 클릭
        /// </summary>
        private void OnClickMineAgain()
        {
            if (_controller == null)
                return;

            // 팝업 먼저 닫고
            HideImmediate();

            // ✅ 중요:
            // StartSession() 직접 호출은 "월드 정리/스폰/인트로"가 생략될 수 있어
            // BigOre 잔재/상태 꼬임의 원인이 된다.
            // 따라서 재시작은 반드시 BeginSessionFlow()로 들어가야 한다.
            _controller.RestartSessionFromPopup(); // 내부에서 BeginSessionFlow() 호출
        }

        /// <summary>
        /// "업그레이드" 버튼 클릭 (PF_Outpost 씬으로 이동)
        /// </summary>
        private void OnClickUpgrade()
        {
            HideImmediate();
            SceneManager.LoadScene("PF_Outpost");
        }
    }
}
