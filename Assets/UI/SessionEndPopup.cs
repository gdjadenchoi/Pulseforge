using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Pulseforge.Systems;   // RewardType

namespace Pulseforge.UI
{
    public class SessionEndPopup : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("�˾� ��ü�� ���δ� ������(�г�) ������Ʈ")]
        public GameObject panel;

        [Tooltip("��� ����� ������ TextMeshProUGUI")]
        public TMP_Text summaryText;

        [Tooltip("�ٽ� ä���ϱ� ��ư")]
        public Button mineAgainButton;

        [Tooltip("���׷��̵� ȭ������ ���� ��ư (������ ���۸� ����)")]
        public Button upgradeButton;

        CanvasGroup _canvasGroup;
        SessionController _controller;

        void Awake()
        {
            // Panel �� ��� ������ �ڱ� �ڽ��� �гη� ���
            if (panel == null)
                panel = gameObject;

            // ��Ʈ�� CanvasGroup �� �޷� ������ ������ (��� ��� ����)
            _canvasGroup = GetComponent<CanvasGroup>();

            // ���� �ÿ��� �׻� ���� ���¿��� ���
            HideImmediate();
            if (mineAgainButton != null)
            mineAgainButton.onClick.AddListener(OnClickMineAgain);

            if (upgradeButton != null)
            upgradeButton.onClick.AddListener(OnClickUpgrade);
        }

        /// <summary>
        /// �˾��� ��� ���� (�ִϸ��̼� ����)
        /// </summary>
        public void HideImmediate()
        {
            // ���� Ȯ���ϰ�: �˾� ��Ʈ ������Ʈ ��ü�� �� ����
            gameObject.SetActive(false);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        /// <summary>
        /// ���� ���� �� ��Ʈ�ѷ����� ȣ���ϴ� ������
        /// </summary>
        public void Show(IReadOnlyDictionary<RewardType, int> rewards, SessionController session)
        {
            _controller = session;

            // �˾� ��Ʈ �ѱ�
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }

            // ===== �ؽ�Ʈ ä��� =====
            if (summaryText != null)
            {
                if (rewards == null || rewards.Count == 0)
                {
                    summaryText.text = "���� ä���� �ڿ��� �����ϴ�.";
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("�̹� ���� ���");

                    foreach (var kv in rewards)
                    {
                        sb.AppendLine($"{kv.Key}: {kv.Value}");
                    }

                    summaryText.text = sb.ToString();
                }
            }

            // ===== ��ư ������ ���� =====
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
        /// "�ٽ� ä���ϱ�" ��ư Ŭ��
        /// </summary>
        private void OnClickMineAgain()
        {
            if (_controller == null)
                return;

            // �˾� ���� �����
            HideImmediate();

            // ���� �ٽ� ����
            _controller.StartSession();
        }

        /// <summary>
        /// "���׷��̵�" ��ư Ŭ�� (������ �˾��� �ݰ�, ���߿� ȭ�� �̵� ����)
        /// </summary>
        void OnClickUpgrade()
        {
            // TODO: ���׷��̵� ȭ�� ���� ���� ����
            HideImmediate();
            SceneManager.LoadScene("PF_Outpost");
        }
    }
}
