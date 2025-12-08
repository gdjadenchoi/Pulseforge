using System;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 1개에 대한 데이터 정의 ScriptableObject
    /// - UpgradeManager / UpgradeEntry 에서 참조해서 사용
    /// </summary>
    [CreateAssetMenu(
        fileName = "UpgradeDefinition_",
        menuName = "Pulseforge/Upgrade Definition",
        order = 0)]
    public class UpgradeDefinition : ScriptableObject
    {
        //==============================================================
        //  기본 정보
        //==============================================================

        [Header("기본 정보")]
        [Tooltip("이 업그레이드를 식별하기 위한 고유 ID (예: \"OreAmount\", \"CursorDamage\")")]
        [SerializeField] private string id;

        [Tooltip("UI에 표시될 이름 (비어있으면 id를 사용)")]
        [SerializeField] private string displayName;

        [Tooltip("UI 등에 표시될 설명 텍스트")]
        [TextArea]
        [SerializeField] private string description;

        [Tooltip("업그레이드 아이콘 (선택 사항)")]
        [SerializeField] private Sprite icon;

        //==============================================================
        //  레벨 설정
        //==============================================================

        [Header("레벨 설정")]
        [Tooltip("이 업그레이드의 최대 레벨")]
        [Min(1)]
        [SerializeField] private int maxLevel = 10;

        //==============================================================
        //  비용 곡선
        //==============================================================

        public enum UpgradeCostCurveType
        {
            /// <summary>선형: baseCost + costStep * (레벨-1)</summary>
            Linear = 0,

            /// <summary>지수: baseCost * costFactor^(레벨-1)</summary>
            Exponential = 1,

            /// <summary>커스텀 (나중에 확장용)</summary>
            Custom = 2,
        }

        [Header("비용 설정")]
        [Tooltip("비용 계산 방식")]
        [SerializeField] private UpgradeCostCurveType costCurveType = UpgradeCostCurveType.Linear;

        [Tooltip("1레벨 업그레이드 비용의 기본값")]
        [Min(0)]
        [SerializeField] private int baseCost = 10;

        [Tooltip("선형 증가 시, 레벨당 추가되는 고정 비용 (Linear 전용)")]
        [Min(0)]
        [SerializeField] private int costStep = 5;

        [Tooltip("지수 곡선 사용 시, 레벨당 곱해지는 계수 (Exponential 전용)")]
        [Min(0f)]
        [SerializeField] private float costFactor = 1.5f;

        //==============================================================
        //  효과 설정
        //==============================================================

        public enum UpgradeEffectValueType
        {
            Flat = 0,    // 고정 값 증가
            Percent = 1, // 퍼센트 증가
        }

        [Header("효과 설정")]
        [Tooltip("실제 적용 로직에서 사용할 효과 키 (비어있으면 id와 동일하게 사용)")]
        [SerializeField] private string effectKey;

        [Tooltip("효과 값 타입 (고정 or 퍼센트)")]
        [SerializeField] private UpgradeEffectValueType valueType = UpgradeEffectValueType.Flat;

        [Tooltip("레벨당 증가량 (Flat이면 +1, Percent면 +0.1 = 10% 같은 식으로 사용)")]
        [SerializeField] private float valuePerLevel = 1f;

        //==============================================================
        //  해금 조건 (트리 구조용)
        //==============================================================

        [Serializable]
        public struct UpgradePrerequisite
        {
            [Tooltip("필요한 업그레이드 ID (예: \"OreAmount\")")]
            public string requiredUpgradeId;

            [Tooltip("위 업그레이드가 최소 몇 레벨 이상이어야 하는지")]
            public int requiredLevel;
        }

        [Header("해금 조건 (트리)")]
        [Tooltip("이 업그레이드를 찍기 위해 먼저 찍어야 하는 업그레이드들")]
        [SerializeField] private UpgradePrerequisite[] prerequisites = Array.Empty<UpgradePrerequisite>();

        //==============================================================
        //  프로퍼티
        //==============================================================

        public string Id => string.IsNullOrWhiteSpace(id) ? name : id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public int MaxLevel => Mathf.Max(1, maxLevel);

        public UpgradeCostCurveType CostType => costCurveType;
        public int BaseCost => Mathf.Max(0, baseCost);
        public int CostStep => Mathf.Max(0, costStep);
        public float CostFactor => Mathf.Max(0f, costFactor);

        public string EffectKey => string.IsNullOrWhiteSpace(effectKey) ? Id : effectKey;
        public UpgradeEffectValueType ValueType => valueType;
        public float ValuePerLevel => valuePerLevel;

        public UpgradePrerequisite[] Prerequisites => prerequisites;

        //==============================================================
        //  비용 계산
        //==============================================================

        public int GetCostForLevel(int nextLevel)
        {
            if (nextLevel <= 0)
                nextLevel = 1;

            switch (costCurveType)
            {
                case UpgradeCostCurveType.Linear:
                    return Mathf.Max(0, BaseCost + CostStep * (nextLevel - 1));

                case UpgradeCostCurveType.Exponential:
                    if (CostFactor <= 0f)
                        return BaseCost;
                    float raw = BaseCost * Mathf.Pow(CostFactor, nextLevel - 1);
                    return Mathf.Max(0, Mathf.RoundToInt(raw));

                case UpgradeCostCurveType.Custom:
                default:
                    return Mathf.Max(0, BaseCost + CostStep * (nextLevel - 1));
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id))
                id = name;

            if (maxLevel < 1) maxLevel = 1;
            if (baseCost < 0) baseCost = 0;
            if (costStep < 0) costStep = 0;
            if (costFactor < 0f) costFactor = 0f;

            if (prerequisites == null)
                prerequisites = Array.Empty<UpgradePrerequisite>();
        }
#endif
    }
}
