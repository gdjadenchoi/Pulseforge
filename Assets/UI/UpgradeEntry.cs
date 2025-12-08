using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 한 줄(버튼 + 텍스트) UI
    /// - UpgradeDefinition + UpgradeManager + RewardManager 를 묶어서
    ///   "코스트 표시 + 구매 처리"까지 담당.
    /// - AutoUpgradePanel 에서 Setup(...)으로 세팅해 쓰는 용도.
    /// </summary>
    public class UpgradeEntry : MonoBehaviour
    {
        //==============================================================
        //  인스펙터 설정
        //==============================================================

        [Header("기본 참조")]
        [Tooltip("이 엔트리가 표시할 업그레이드 정의(SO).\nAutoUpgradePanel에서 Setup()으로 들어올 수도 있음.")]
        [SerializeField] private UpgradeDefinition _definition;

        [Tooltip("정의를 사용하지 않고, ID만 직접 넣어 쓸 경우 (옵션).\n비워두면 definition.Id 사용.")]
        [SerializeField] private string _upgradeIdOverride;

        [Header("UI 참조")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _levelText;
        [SerializeField] private TextMeshProUGUI _descText;
        [SerializeField] private TextMeshProUGUI _costText;
        [SerializeField] private Button _button;
        [SerializeField] private Image _iconImage;

        [Header("비용 설정")]
        [Tooltip("이 업그레이드에 사용할 화폐 타입")]
        [SerializeField] private RewardType _costCurrency = RewardType.Crystal;

        [Header("색상 설정 (옵션)")]
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _notEnoughColor = Color.gray;
        [SerializeField] private Color _maxLevelColor = Color.yellow;

        //==============================================================
        //  내부 상태
        //==============================================================

        private UpgradeManager _upgradeManager;
        private RewardManager _rewardManager;

        //==============================================================
        //  편의 프로퍼티
        //==============================================================

        /// <summary>
        /// 실제로 사용할 업그레이드 ID
        /// - override가 비어있으면 Definition.Id 사용
        /// </summary>
        private string EffectiveId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_upgradeIdOverride))
                    return _upgradeIdOverride.Trim();

                return _definition != null ? _definition.Id : null;
            }
        }

        //==============================================================
        //  외부에서 사용하는 Setup (AutoUpgradePanel용)
        //==============================================================

        /// <summary>
        /// AutoUpgradePanel에서 행 생성 후 호출하는 초기화 함수.
        /// </summary>
        public void Setup(UpgradeDefinition definition, UpgradeManager manager = null)
        {
            _definition = definition;
            if (_definition != null)
            {
                // override가 비어 있으면 정의의 ID를 기본으로 사용
                if (string.IsNullOrWhiteSpace(_upgradeIdOverride))
                    _upgradeIdOverride = _definition.Id;
            }

            _upgradeManager = manager != null ? manager : UpgradeManager.Instance;
            _rewardManager = RewardManager.SafeInstance;

            RefreshUI();
        }

        //==============================================================
        //  Unity 라이프사이클
        //==============================================================

        private void Awake()
        {
            // 혹시 Setup()이 아직 안 불렸을 수 있으니, 안전하게 매니저를 찾아둠
            if (_upgradeManager == null)
                _upgradeManager = UpgradeManager.Instance;

            if (_rewardManager == null)
                _rewardManager = RewardManager.SafeInstance;
        }

        private void OnEnable()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(OnClickUpgrade);
            }

            // 업그레이드 레벨 변경 / 자원 변경 시 UI 갱신
            if (_upgradeManager != null)
                _upgradeManager.OnLevelChanged += HandleUpgradeLevelChanged;

            if (_rewardManager != null)
                _rewardManager.OnResourceChanged += HandleResourceChanged;

            RefreshUI();
        }

        private void OnDisable()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClickUpgrade);
            }

            if (_upgradeManager != null)
                _upgradeManager.OnLevelChanged -= HandleUpgradeLevelChanged;

            if (_rewardManager != null)
                _rewardManager.OnResourceChanged -= HandleResourceChanged;
        }

        //==============================================================
        //  이벤트 핸들러
        //==============================================================

        private void HandleUpgradeLevelChanged(string id, int newLevel)
        {
            // 이 엔트리가 담당하는 업그레이드만 갱신
            if (id == EffectiveId)
            {
                RefreshUI();
            }
        }

        private void HandleResourceChanged(RewardType type, int current)
        {
            // 비용에 사용하는 화폐가 바뀐 것만 반영
            if (type == _costCurrency)
            {
                RefreshUI();
            }
        }

        //==============================================================
        //  메인 로직: 버튼 클릭 → 비용 체크 → 자원 소모 → 업그레이드
        //==============================================================

        private void OnClickUpgrade()
        {
            if (_upgradeManager == null)
            {
                Debug.LogWarning("[UpgradeEntry] UpgradeManager가 없음");
                return;
            }

            string id = EffectiveId;
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogWarning("[UpgradeEntry] 유효한 Upgrade Id가 없음");
                return;
            }

            // 이미 최대 레벨인지 확인
            if (_upgradeManager.IsMaxLevel(id))
            {
                Debug.Log($"[UpgradeEntry] '{id}' 이미 최대 레벨.");
                RefreshUI();
                return;
            }

            // 비용 계산
            var def = GetDefinitionFor(id);
            int currentLevel = _upgradeManager.GetLevel(id);
            int nextLevel = currentLevel + 1;
            int cost = GetCost(def, nextLevel);

            // 자원 확인
            int currentCurrency = _rewardManager != null ? _rewardManager.Get(_costCurrency) : 0;
            if (currentCurrency < cost)
            {
                Debug.Log($"[UpgradeEntry] '{id}' 업그레이드 실패 - 자원 부족 ({currentCurrency}/{cost})");
                RefreshUI();
                return;
            }

            // 자원 소모 (RewardManager.Add는 음수 지원 안하니까 Set 사용)
            if (_rewardManager != null && cost > 0)
            {
                int after = Mathf.Max(0, currentCurrency - cost);
                _rewardManager.Set(_costCurrency, after);
            }

            // 업그레이드 진행
            bool success = _upgradeManager.TryUpgrade(id);
            if (!success)
            {
                Debug.Log($"[UpgradeEntry] '{id}' TryUpgrade 실패 (아마 이미 최대 레벨일 가능성)");
            }

            RefreshUI();
        }

        //==============================================================
        //  UI 갱신
        //==============================================================

        private void RefreshUI()
        {
            if (!isActiveAndEnabled)
                return;

            string id = EffectiveId;

            // 매니저나 ID가 없으면 비활성화
            if (_upgradeManager == null || string.IsNullOrWhiteSpace(id))
            {
                if (_titleText != null) _titleText.text = "(Invalid)";
                if (_levelText != null) _levelText.text = "-";
                if (_costText != null) _costText.text = "-";
                if (_button != null) _button.interactable = false;
                return;
            }

            var def = GetDefinitionFor(id);
            int level = _upgradeManager.GetLevel(id);
            int maxLevel = _upgradeManager.GetMaxLevel(id);
            bool isMax = level >= maxLevel && maxLevel > 0;

            // 타이틀 / 설명 / 아이콘
            if (_titleText != null)
                _titleText.text = def != null ? def.DisplayName : id;

            if (_descText != null)
                _descText.text = def != null ? def.Description : string.Empty;

            if (_iconImage != null)
            {
                if (def != null && def.Icon != null)
                {
                    _iconImage.enabled = true;
                    _iconImage.sprite = def.Icon;
                }
                else
                {
                    _iconImage.enabled = false;
                }
            }

            // 레벨 텍스트
            if (_levelText != null)
            {
                if (isMax)
                    _levelText.text = $"Lv.{level} / MAX";
                else
                    _levelText.text = $"Lv.{level} / {maxLevel}";
            }

            // 비용 / 버튼 상태
            if (_costText != null || _button != null)
            {
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

                int nextLevel = level + 1;
                int cost = GetCost(def, nextLevel);
                int currentCurrency = _rewardManager != null ? _rewardManager.Get(_costCurrency) : 0;
                bool affordable = currentCurrency >= cost;

                if (_costText != null)
                {
                    _costText.text = cost > 0 ? $"Cost: {cost}" : "Free";
                    _costText.color = affordable ? _normalColor : _notEnoughColor;
                }

                if (_button != null)
                    _button.interactable = affordable;
            }
        }

        //==============================================================
        //  헬퍼
        //==============================================================

        private UpgradeDefinition GetDefinitionFor(string id)
        {
            if (_definition != null && _definition.Id == id)
                return _definition;

            // definition이 비어 있으면 UpgradeManager 쪽 정의를 참고 (있으면)
            if (_upgradeManager != null)
            {
                return _upgradeManager.GetDefinition(id);
            }

            return null;
        }

        /// <summary>
        /// 정의가 있으면 정의의 GetCostForLevel 사용, 없으면 간단한 기본 규칙 사용.
        /// </summary>
        private int GetCost(UpgradeDefinition def, int nextLevel)
        {
            if (def != null)
            {
                return def.GetCostForLevel(nextLevel);
            }

            // 정의가 없는 특별 케이스용: 아주 단순한 기본 값
            // (나중에 필요 없으면 지워도 됨)
            return Mathf.Max(1, 10 * nextLevel);
        }
    }
}
