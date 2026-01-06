using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 광산 줌/채굴영역 스케일을 관리하는 매니저.
    /// - 기본적으로 MiningAreaExpansion(=채굴영역 확장) 업그레이드 레벨을 보고 스케일 레벨을 결정한다.
    /// - 현재 스케일 레벨(CurrentScaleLevel)을 기반으로 카메라 줌, 스폰 영역 확장 등 연동 가능
    /// </summary>
    public class MiningScaleManager : MonoBehaviour
    {
        public static MiningScaleManager Instance { get; private set; }

        [Header("Main Scale Source")]
        [Tooltip("줌/채굴영역 스케일에 가장 크게 영향을 줄 업그레이드 ID (예: \"MiningAreaExpansion\")")]
        [SerializeField] private string mainUpgradeId = "MiningAreaExpansion";

        [Header("Scale Level / Rules")]
        [Tooltip("업그레이드 레벨을 어떻게 스케일레벨로 변환할지")]
        public ScaleRule scaleRule = ScaleRule.TierTable;

        [Tooltip("선형 방식일 때: 업그레이드 레벨당 스케일 레벨 증가량")]
        public int linearStep = 1;

        [Tooltip("선형 방식일 때: 최대 스케일 레벨")]
        public int maxScaleLevel = 2;

        [Tooltip("티어 테이블 방식일 때: 업그레이드 레벨 구간 -> 스케일 레벨 매핑")]
        public List<Tier> tierTable = new List<Tier>()
        {
            new Tier(){ minUpgradeLevel = 0, maxUpgradeLevel = 0, scaleLevel = 0 },
            new Tier(){ minUpgradeLevel = 1, maxUpgradeLevel = 1, scaleLevel = 1 },
            new Tier(){ minUpgradeLevel = 2, maxUpgradeLevel = 999, scaleLevel = 2 },
        };

        [Header("Spawn Area (for Zoom tier table)")]
        [Tooltip("스케일 레벨별 스폰 영역 높이(월드 단위). 예: 0=10, 1=13, 2=16")]
        public List<ViewHeightTier> viewHeightTiers = new List<ViewHeightTier>()
        {
            new ViewHeightTier(){ scaleLevel = 0, spawnWorldHeight = 10f },
            new ViewHeightTier(){ scaleLevel = 1, spawnWorldHeight = 13f },
            new ViewHeightTier(){ scaleLevel = 2, spawnWorldHeight = 16f },
        };

        public int CurrentScaleLevel { get; private set; }

        public event Action<int> OnScaleChanged;

        public enum ScaleRule
        {
            Linear,
            TierTable
        }

        [Serializable]
        public struct Tier
        {
            public int minUpgradeLevel;
            public int maxUpgradeLevel;
            public int scaleLevel;
        }

        [Serializable]
        public struct ViewHeightTier
        {
            public int scaleLevel;
            public float spawnWorldHeight;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            RefreshScaleLevel(force: true);
        }

        private void Update()
        {
            RefreshScaleLevel(force: false);
        }

        private void RefreshScaleLevel(bool force)
        {
            int upgradeLevel = 0;
            var um = Pulseforge.Systems.UpgradeManager.Instance;
            if (um != null && !string.IsNullOrEmpty(mainUpgradeId))
            {
                upgradeLevel = um.GetLevel(mainUpgradeId);
            }

            int newScale = 0;
            switch (scaleRule)
            {
                case ScaleRule.Linear:
                    newScale = Mathf.Clamp(upgradeLevel / Mathf.Max(1, linearStep), 0, maxScaleLevel);
                    break;

                case ScaleRule.TierTable:
                    newScale = GetScaleFromTierTable(upgradeLevel);
                    break;
            }

            if (force || newScale != CurrentScaleLevel)
            {
                 Debug.Log($"[MiningScaleManager] mainUpgradeId={mainUpgradeId}, upgradeLevel={upgradeLevel} => scaleLevel {CurrentScaleLevel} -> {newScale}");
                CurrentScaleLevel = newScale;
                OnScaleChanged?.Invoke(CurrentScaleLevel);
            }
        }

        public float GetFinalSpawnWorldHeight()
        {
            int scaleLevel = CurrentScaleLevel;
            if (viewHeightTiers == null || viewHeightTiers.Count == 0)
                return 10f;

            float chosen = viewHeightTiers[0].spawnWorldHeight;
            int chosenLevel = int.MinValue;

            for (int i = 0; i < viewHeightTiers.Count; i++)
            {
                var t = viewHeightTiers[i];
                if (t.scaleLevel <= scaleLevel && t.scaleLevel > chosenLevel)
                {
                    chosenLevel = t.scaleLevel;
                    chosen = t.spawnWorldHeight;
                }
            }
            return chosen;
        }

        public Vector2 GetFinalSpawnAreaSize(float fixedAspect)
        {
            float h = GetFinalSpawnWorldHeight();
            float w = h * fixedAspect;
            return new Vector2(w, h);
        }

        private int GetScaleFromTierTable(int upgradeLevel)
        {
            if (tierTable == null || tierTable.Count == 0)
                return 0;

            for (int i = 0; i < tierTable.Count; i++)
            {
                var t = tierTable[i];
                if (upgradeLevel >= t.minUpgradeLevel && upgradeLevel <= t.maxUpgradeLevel)
                    return t.scaleLevel;
            }

            // fallback: 마지막 tier
            return tierTable[tierTable.Count - 1].scaleLevel;
        }
    }
}
