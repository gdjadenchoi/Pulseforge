using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Pulseforge.Systems;   // RewardType

namespace Pulseforge.UI
{
    public class SessionEndPopup : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("팝업 전체를 감싸는 윈도우(패널) 오브젝트")]
        public GameObject panel;

        [Tooltip("결과 요약을 보여줄 TextMeshProUGUI")]
        public TMP_Text summaryText;

        [Tooltip("다시 채굴하기 버튼")]
        public Button mineAgainButton;

        [Tooltip("업그레이드 화면으로 가는 버튼 (지금은 동작만 숨김)")]
        public Button upgradeButton;

        CanvasGroup _canvasGroup;
        SessionController _controller;

        void Awake()
        {
            // Panel 이 비어 있으면 자기 자신을 패널로 사용
            if (panel == null)
                panel = gameObject;

            // 루트에 CanvasGroup 이 달려 있으면 가져옴 (없어도 상관 없음)
            _canvasGroup = GetComponent<CanvasGroup>();

            // 시작 시에는 항상 숨긴 상태에서 출발
            HideImmediate();
        }

        /// <summary>
        /// 팝업을 즉시 숨김 (애니메이션 없이)
        /// </summary>
        public void HideImmediate()
        {
            // 가장 확실하게: 팝업 루트 오브젝트 자체를 꺼 버림
            gameObject.SetActive(false);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        /// <summary>
        /// 세션 종료 시 컨트롤러에서 호출하는 진입점
        /// </summary>
        public void Show(IReadOnlyDictionary<RewardType, int> rewards, SessionController session)
        {
            _controller = session;

            // 팝업 루트 켜기
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }

            // ===== 텍스트 채우기 =====
            if (summaryText != null)
            {
                if (rewards == null || rewards.Count == 0)
                {
                    summaryText.text = "아직 채굴한 자원이 없습니다.";
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("이번 세션 결과");

                    foreach (var kv in rewards)
                    {
                        sb.AppendLine($"{kv.Key}: {kv.Value}");
                    }

                    summaryText.text = sb.ToString();
                }
            }

            // ===== 버튼 리스너 설정 =====
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

            // 팝업 먼저 숨기고
            HideImmediate();

            // 세션 다시 시작
            _controller.StartSession();
        }

        /// <summary>
        /// "업그레이드" 버튼 클릭 (지금은 팝업만 닫고, 나중에 화면 이동 연결)
        /// </summary>
        void OnClickUpgrade()
        {
            // TODO: 업그레이드 화면 열기 연결 예정
            HideImmediate();
        }
    }
}
