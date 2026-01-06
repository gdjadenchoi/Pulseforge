using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Pulseforge.Systems;


namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 한 줄 UI.
    /// - UpgradeDefinition 데이터를 기반으로 제목/설명/아이콘/레벨/코스트를 표시
    /// - 버튼 클릭 시 업그레이드 시도
    /// - 자원/레벨/플레이어 레벨/선행 업그레이드 변화에 따라 자동으로 잠금/해금 상태를 갱신
    /// </summary>
    public class UpgradeEntry : MonoBehaviour
    {
        [Header("Definition")]
        [Tooltip("표시할 업그레이드 정의 (선택). 비워두면 UpgradeIdOverride 로만 동작할 수 있다.")]
        [SerializeField] private UpgradeDefinition _definition;

        [Tooltip("강제로 사용할 업그레이드 ID. 비워두면 Definition.Id 를 사용.")]
        [SerializeField] private string _upgradeIdOverride;

        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _levelText;
        [SerializeField] private TextMeshProUGUI _descText;
        [SerializeField] private TextMeshProUGUI _costText;
        [SerializeField] private Button _button;
        [SerializeField] private Image _iconImage;

        [Header("Cost Settings")]
        [Tooltip("이 업그레이드에 사용할 화폐 타입")]
        [SerializeField] private RewardType _costCurrency = RewardType.Crystal;

        [Header("Colors")]
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _notEnoughColor = Color.red;
        [SerializeField] private Color _maxLevelColor = Color.yellow;
        [Tooltip("잠금 상태일 때 코스트 텍스트 색상. 지정하지 않으면 NotEnoughColor 를 사용.")]
        [SerializeField] private Color _lockedColor = Color.gray;

        [Header("Texts")]
        [Tooltip("잠금 상태일 때 코스트 텍스트에 표시될 문자열")]
        [SerializeField] private string _lockedText = "LOCKED";

        // ─────────────────────────────────────────────────────────────
        //  런타임 참조
        // ─────────────────────────────────────────────────────────────
        private UpgradeManager _upgradeManager;
        private RewardManager _rewardManager;
        private LevelManager _levelManager;

        /// <summary>
        /// 이 엔트리가 참조하는 실제 업그레이드 ID.
        /// Override 가 있으면 그걸 사용하고, 없으면 Definition.Id 를 사용.
        /// </summary>
        private string EffectiveId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_upgradeIdOverride))
                    return _upgradeIdOverride.Trim();

                if (_definition != null)
                    return _definition.Id;

                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Unity
        // ─────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            if (_button != null)
                _button.onClick.AddListener(OnClickUpgrade);

            if (_upgradeManager == null)
                _upgradeManager = UpgradeManager.Instance;
            if (_rewardManager == null)
                _rewardManager = RewardManager.SafeInstance;
            if (_levelManager == null)
                _levelManager = LevelManager.Instance;

            if (_upgradeManager != null)
                _upgradeManager.OnLevelChanged += HandleUpgradeLevelChanged;
            if (_rewardManager != null)
                _rewardManager.OnResourceChanged += HandleResourceChanged;
            if (_levelManager != null)
                _levelManager.OnLevelChanged += HandlePlayerLevelChanged;

            RefreshUI();
        }

        private void OnDisable()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClickUpgrade);

            if (_upgradeManager != null)
                _upgradeManager.OnLevelChanged -= HandleUpgradeLevelChanged;
            if (_rewardManager != null)
                _rewardManager.OnResourceChanged -= HandleResourceChanged;
            if (_levelManager != null)
                _levelManager.OnLevelChanged -= HandlePlayerLevelChanged;
        }

        // ─────────────────────────────────────────────────────────────
        //  외부에서 호출하는 초기화
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// UpgradePanel 이 동적으로 행을 생성할 때 호출되는 설정 함수.
        /// </summary>
        public void Setup(UpgradeDefinition definition, UpgradeManager manager = null)
        {
            _definition = definition;

            if (_definition != null && string.IsNullOrWhiteSpace(_upgradeIdOverride))
                _upgradeIdOverride = _definition.Id;

            _upgradeManager = manager ?? UpgradeManager.Instance;
            _rewardManager = RewardManager.SafeInstance;
            _levelManager = LevelManager.Instance;

            RefreshUI();
        }

        // ─────────────────────────────────────────────────────────────
        //  이벤트 핸들러
        // ─────────────────────────────────────────────────────────────

        private void HandleUpgradeLevelChanged(string changedId, int _)
        {
            var id = EffectiveId;
            if (string.IsNullOrWhiteSpace(id))
                return;

            // 1) 자기 자신 업그레이드 레벨 변경
            if (string.Equals(changedId, id, StringComparison.Ordinal))
            {
                RefreshUI();
                return;
            }

            // 2) 프리리퀴짓으로 참조하는 업그레이드 레벨 변경
            //    (예: OreAmount 가 오르면, 그것을 요구하는 CursorDamage 가 잠금 해제될 수 있음)
            if (_definition != null && _definition.Prerequisites != null)
            {
                foreach (var pre in _definition.Prerequisites)
                {
                    if (string.IsNullOrWhiteSpace(pre.requiredUpgradeId))
                        continue;

                    if (string.Equals(pre.requiredUpgradeId, changedId, StringComparison.Ordinal))
                    {
                        RefreshUI();
                        return;
                    }
                }
            }
        }

        private void HandleResourceChanged(RewardType type, int _)
        {
            // 다른 화폐 타입 변화는 무시
            if (type != _costCurrency)
                return;

            RefreshUI();
        }

        private void HandlePlayerLevelChanged(int _)
        {
            // 플레이어 레벨이 변하면 잠금/해금 상태가 바뀔 수 있으므로 항상 갱신
            RefreshUI();
        }

        // ─────────────────────────────────────────────────────────────
        //  UI 갱신
        // ─────────────────────────────────────────────────────────────

        private void RefreshUI()
        {
            var id = EffectiveId;

            // 필수 참조가 없는 경우
            if (string.IsNullOrWhiteSpace(id) || _upgradeManager == null)
            {
                if (_titleText != null)
                    _titleText.text = _definition != null ? _definition.DisplayName : "(Invalid)";
                if (_levelText != null)
                    _levelText.text = string.Empty;
                if (_costText != null)
                {
                    _costText.text = "-";
                    _costText.color = _notEnoughColor;
                }
                if (_button != null)
                    _button.interactable = false;
                return;
            }

            var def = _upgradeManager.GetDefinition(id) ?? _definition;
            if (def == null)
            {
                if (_titleText != null)
                    _titleText.text = id;
                if (_levelText != null)
                    _levelText.text = string.Empty;
                if (_costText != null)
                {
                    _costText.text = "-";
                    _costText.color = _notEnoughColor;
                }
                if (_button != null)
                    _button.interactable = false;
                return;
            }

            // 기본 텍스트/아이콘
            if (_titleText != null)
                _titleText.text = def.DisplayName;
            if (_descText != null)
                _descText.text = def.Description;
            if (_iconImage != null)
                _iconImage.sprite = def.Icon;

            int currentLevel = _upgradeManager.GetLevel(id);
            int maxLevel = _upgradeManager.GetMaxLevel(id);

            if (_levelText != null)
                _levelText.text = $"Lv {currentLevel}/{maxLevel}";

            bool isMax = currentLevel >= maxLevel;

            // ── 잠금 / 해금 / 최대 레벨 상태 판단 ────────────────
            bool isUnlocked = _upgradeManager.MeetsPrerequisites(id);

            // 1) 잠금 상태
            if (!isUnlocked)
            {
                if (_costText != null)
                {
                    _costText.text = string.IsNullOrEmpty(_lockedText) ? "LOCKED" : _lockedText;
                    // lockedColor 가 기본값(0,0,0,0)이면 notEnoughColor 재사용
                    var colorToUse = (_lockedColor.a > 0.0001f) ? _lockedColor : _notEnoughColor;
                    _costText.color = colorToUse;
                }

                if (_button != null)
                    _button.interactable = false;

                return;
            }

            // 2) 최대 레벨 상태
            if (isMax)
            {
                if (_costText != null)
                {
                    _costText.text = "MAX";
                    _costText.color = _maxLevelColor;
                }

                if (_button != null)
                    _button.interactable = false;

                return;
            }

            // 3) 일반 상태 (해금 & 아직 최대 레벨 아님)
            int nextLevel = currentLevel + 1;
            int cost = def.GetCostForLevel(nextLevel);

            int currentCurrency = _rewardManager != null
                ? _rewardManager.Get(_costCurrency)
                : 0;

            bool affordable = currentCurrency >= cost;

            if (_costText != null)
            {
                _costText.text = cost.ToString("N0");
                _costText.color = affordable ? _normalColor : _notEnoughColor;
            }

            if (_button != null)
                _button.interactable = affordable;
        }

        // ─────────────────────────────────────────────────────────────
        //  버튼 클릭
        // ─────────────────────────────────────────────────────────────

        private void OnClickUpgrade()
        {
            var id = EffectiveId;
            if (string.IsNullOrWhiteSpace(id) || _upgradeManager == null || _rewardManager == null)
                return;

            // 1) 잠금 상태라면 아무 것도 하지 않음
            if (!_upgradeManager.MeetsPrerequisites(id))
            {
                RefreshUI();
                return;
            }

            int currentLevel = _upgradeManager.GetLevel(id);
            int maxLevel = _upgradeManager.GetMaxLevel(id);
            if (currentLevel >= maxLevel)
            {
                RefreshUI();
                return;
            }

            var def = _upgradeManager.GetDefinition(id) ?? _definition;
            if (def == null)
                return;

            int nextLevel = currentLevel + 1;
            int cost = def.GetCostForLevel(nextLevel);
            int currentCurrency = _rewardManager.Get(_costCurrency);

            if (currentCurrency < cost)
            {
                // 자원이 부족하면 단순히 UI만 갱신
                RefreshUI();
                return;
            }

            // 자원 차감
            _rewardManager.Set(_costCurrency, currentCurrency - cost);

            // 실제 업그레이드 시도 (프리리퀴짓 + 최대 레벨 체크는 내부에서 한 번 더 검사)
            _upgradeManager.TryUpgrade(id);

            // OnLevelChanged / OnResourceChanged 이벤트를 통해 자동으로 RefreshUI 가 호출되지만,
            // 방어적 차원에서 한 번 더 갱신해준다.
            RefreshUI();
        }
    }
}
