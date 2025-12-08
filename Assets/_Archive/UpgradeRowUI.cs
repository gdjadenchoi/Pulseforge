using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Pulseforge.Systems;

namespace Pulseforge.UI
{
    /// <summary>
    /// ScriptableObject(UpgradeDefinition) 1개를 표시하는 업그레이드 행 UI
    /// - 이름 / 레벨 / 버튼 표시
    /// - 버튼 클릭 시 UpgradeManager.TryUpgrade 호출
    /// </summary>
    public class UpgradeRowUI : MonoBehaviour
    {
        [Header("UI 참조")]
        [SerializeField] private TMP_Text nameText;   // "광석 개수 증가" 같은 이름
        [SerializeField] private TMP_Text levelText;  // "Lv. 3" 같은 표시
        [SerializeField] private Button upgradeButton;

        private UpgradeManager _manager;
        private UpgradeDefinition _definition;
        private string _upgradeId;

        /// <summary>
        /// 패널에서 한 번만 호출해서 이 행을 초기화
        /// </summary>
        public void Initialize(UpgradeDefinition definition, UpgradeManager manager)
        {
            _definition = definition;
            _manager = manager;

            if (_definition != null)
                _upgradeId = _definition.Id;
            else
                _upgradeId = string.Empty;

            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveListener(OnClickUpgrade);
                upgradeButton.onClick.AddListener(OnClickUpgrade);
            }

            Refresh();
        }

        private void OnEnable()
        {
            if (_manager != null)
            {
                _manager.OnLevelChanged -= HandleLevelChanged;
                _manager.OnLevelChanged += HandleLevelChanged;
            }
        }

        private void OnDisable()
        {
            if (_manager != null)
                _manager.OnLevelChanged -= HandleLevelChanged;
        }

        private void HandleLevelChanged(string id, int newLevel)
        {
            if (string.IsNullOrEmpty(_upgradeId))
                return;

            if (!string.Equals(id, _upgradeId, System.StringComparison.Ordinal))
                return;

            Refresh();
        }

        /// <summary>
        /// 이름 / 레벨 / 버튼 인터랙션 갱신
        /// </summary>
        private void Refresh()
        {
            if (_definition == null || _manager == null)
            {
                if (nameText != null)  nameText.text = "(no definition)";
                if (levelText != null) levelText.text = "Lv. -";
                if (upgradeButton != null) upgradeButton.interactable = false;
                return;
            }

            int level = _manager.GetLevel(_upgradeId);
            int maxLevel = _manager.GetMaxLevel(_upgradeId);
            bool isMax = level >= maxLevel;

            if (nameText != null)
                nameText.text = _definition.DisplayName;

            if (levelText != null)
                levelText.text = $"Lv. {level}";

            if (upgradeButton != null)
                upgradeButton.interactable = !isMax;
        }

        private void OnClickUpgrade()
        {
            if (_manager == null || string.IsNullOrEmpty(_upgradeId))
                return;

            bool upgraded = _manager.TryUpgrade(_upgradeId);

            // 결과와 무관하게 UI 갱신
            Refresh();

            // 디버그 로그
            if (upgraded)
                Debug.Log($"[UpgradeRowUI] Upgraded '{_upgradeId}' → Lv.{_manager.GetLevel(_upgradeId)}");
            else
                Debug.Log($"[UpgradeRowUI] Upgrade failed for '{_upgradeId}' (max or locked)");
        }
    }
}
