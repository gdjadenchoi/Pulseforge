using System;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 단일 업그레이드 정의 ScriptableObject.
    /// 비용 곡선, 효과 타입, 선행 조건(업그레이드 / 플레이어 레벨)을 모두 포함한다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "UpgradeDefinition",
        menuName = "Pulseforge/Upgrade Definition",
        order = 0)]
    public class UpgradeDefinition : ScriptableObject
    {
        // ==========================
        //  식별 / 표시용
        // ==========================
        [Header("Identity")]
        [Tooltip("업그레이드 고유 ID. 비워두면 Asset 이름을 그대로 사용한다.")]
        [SerializeField] private string id;

        [Tooltip("UI에 표시될 이름")]
        [SerializeField] private string displayName;

        [TextArea]
        [Tooltip("업그레이드 설명 텍스트")]
        [SerializeField] private string description;

        [Tooltip("업그레이드 아이콘")]
        [SerializeField] private Sprite icon;

        /// <summary>업그레이드 고유 ID. 비워져 있으면 Asset 이름을 사용.</summary>
        public string Id => string.IsNullOrWhiteSpace(id) ? name : id;

        /// <summary>UI 표시용 이름. 비어 있으면 Id 사용.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        public string Description => description;
        public Sprite Icon => icon;

        // ==========================
        //  레벨 / 비용
        // ==========================
        public enum UpgradeCostCurveType
        {
            Linear,
            Exponential
        }

        [Header("Level & Cost")]
        [Tooltip("최대 레벨 (0 이하는 1로 취급)")]
        [SerializeField] private int maxLevel = 5;

        [Tooltip("비용 곡선 타입")]
        [SerializeField] private UpgradeCostCurveType costCurveType = UpgradeCostCurveType.Linear;

        [Tooltip("1레벨 기준 기본 비용")]
        [Min(0)]
        [SerializeField] private int baseCost = 10;

        [Tooltip("Linear 곡선에서 레벨당 추가 비용 (2레벨부터 적용)")]
        [Min(0)]
        [SerializeField] private int costStep = 5;

        [Tooltip("Exponential 곡선에서 레벨당 곱해지는 계수")]
        [Min(1f)]
        [SerializeField] private float costFactor = 1.5f;

        /// <summary>최대 레벨 (최소 1 보장)</summary>
        public int MaxLevel => Mathf.Max(1, maxLevel);

        /// <summary>
        /// 주어진 "다음 레벨(nextLevel)"에 필요한 비용을 반환.
        /// nextLevel = 1 이면 1레벨로 올리는 비용.
        /// </summary>
        public int GetCostForLevel(int nextLevel)
        {
            if (nextLevel <= 1)
                return Mathf.Max(0, baseCost);

            switch (costCurveType)
            {
                case UpgradeCostCurveType.Linear:
                {
                    // baseCost + costStep * (level - 1)
                    var cost = baseCost + costStep * (nextLevel - 1);
                    return Mathf.Max(0, cost);
                }
                case UpgradeCostCurveType.Exponential:
                {
                    // baseCost * costFactor^(level-1)
                    float f = baseCost * Mathf.Pow(costFactor, nextLevel - 1);
                    int cost = Mathf.RoundToInt(f);
                    return Mathf.Max(0, cost);
                }
                default:
                    return Mathf.Max(0, baseCost);
            }
        }

        // ==========================
        //  효과 값
        // ==========================
        public enum UpgradeValueType
        {
            Flat,
            Percent
        }

        [Header("Effect")]
        [Tooltip("이 업그레이드가 어떤 효과 키에 매핑되는지. 비우면 Id 사용.")]
        [SerializeField] private string effectKey;

        [Tooltip("값 타입 (고정 수치 / 퍼센트 배율)")]
        [SerializeField] private UpgradeValueType valueType = UpgradeValueType.Flat;

        [Tooltip("레벨당 증가량 (Flat: +X, Percent: +X%)")]
        [SerializeField] private float valuePerLevel = 1f;

        /// <summary>효과 키. 비워두면 Id와 동일.</summary>
        public string EffectKey => string.IsNullOrWhiteSpace(effectKey) ? Id : effectKey;
        public UpgradeValueType ValueType => valueType;
        public float ValuePerLevel => valuePerLevel;

        /// <summary>
        /// 주어진 레벨에서의 총 효과 값.
        /// Flat = level * valuePerLevel
        /// Percent = level * valuePerLevel (% 단위, 10이라면 +10%)
        /// </summary>
        public float GetTotalValueForLevel(int level)
        {
            if (level <= 0) return 0f;
            return level * valuePerLevel;
        }

        // ==========================
        //  선행 조건 (업그레이드 / 플레이어 레벨)
        // ==========================

        [Serializable]
        public struct UpgradePrerequisite
        {
            [Tooltip("필요한 선행 업그레이드 ID")]
            public string requiredUpgradeId;

            [Tooltip("해당 업그레이드의 최소 레벨")]
            public int requiredLevel;
        }

        [Header("Unlock Requirements")]
        [Tooltip("이 업그레이드를 열기 위한 최소 플레이어 레벨 (0 이하면 레벨 제한 없음).")]
        [SerializeField] private int requiredPlayerLevel = 0;

        [Tooltip("선행 업그레이드 조건 목록. 모두 만족해야 잠금 해제.")]
        [SerializeField] private UpgradePrerequisite[] prerequisites = Array.Empty<UpgradePrerequisite>();

        /// <summary>플레이어 최소 레벨 (0 이하면 제한 없음).</summary>
        public int RequiredPlayerLevel => Mathf.Max(0, requiredPlayerLevel);

        public UpgradePrerequisite[] Prerequisites => prerequisites;

        /// <summary>
        /// 전달받은 플레이어 레벨 / 업그레이드 레벨 조회 함수를 이용해
        /// 이 업그레이드의 모든 해금 조건을 만족하는지 검사한다.
        /// </summary>
        /// <param name="currentPlayerLevel">현재 플레이어 레벨</param>
        /// <param name="getUpgradeLevel">
        /// string upgradeId → 해당 업그레이드 현재 레벨을 반환하는 함수.
        /// null 이면 업그레이드 조건은 검사하지 않는다.
        /// </param>
        public bool CheckPrerequisites(int currentPlayerLevel, Func<string, int> getUpgradeLevel)
        {
            // 1) 플레이어 레벨 조건
            if (RequiredPlayerLevel > 0 && currentPlayerLevel < RequiredPlayerLevel)
                return false;

            // 2) 업그레이드 레벨 조건
            if (prerequisites == null || prerequisites.Length == 0)
                return true;

            if (getUpgradeLevel == null)
            {
                // 방어적 코드: 함수가 없으면 조건을 통과시키거나,
                // 필요하다면 false 로 바꿀 수 있음. 여기서는 "통과" 쪽으로.
                return true;
            }

            foreach (var pre in prerequisites)
            {
                if (string.IsNullOrWhiteSpace(pre.requiredUpgradeId))
                    continue;

                int currentLevel = getUpgradeLevel(pre.requiredUpgradeId);
                if (currentLevel < pre.requiredLevel)
                    return false;
            }

            return true;
        }
    }
}
