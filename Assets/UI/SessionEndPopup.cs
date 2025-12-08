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
    /// - Mine Again: 같은 세션을 다시 시작
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
            // 패널이 따로 지정되지 않았다면 자기 자신을 패널로 사용
            if (panel == null)
                panel = gameObject;

            // CanvasGroup이 있으면 페이드/입력 제어에 사용
            _canvasGroup = GetComponent<CanvasGroup>();

            // 시작 시 항상 비활성 상태로 두기
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
        /// <param name="rewards">RewardType별 누적 보상</param>
        /// <param name="session">해당 세션 컨트롤러</param>
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

            // ===== 요약 텍스트 구성 =====
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
                    {
                        sb.AppendLine($"{kv.Key}: {kv.Value}");
                    }

                    summaryText.text = sb.ToString();
                }
            }

            // ===== 버튼 리스너 정리 =====
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

            // 세션 다시 시작
            // (현재 구현은 StartSession()을 직접 호출하는 구조)
            _controller.StartSession();
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
