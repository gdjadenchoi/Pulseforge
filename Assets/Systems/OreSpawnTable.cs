using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// OreSpawnTable (ScriptableObject)
    /// - OreSpawner가 "이번에 스폰할 프리팹 1개"를 뽑을 때 사용하는 테이블
    /// - 총 스폰 수(TargetCount)는 OreSpawner/OreAmount 업그레이드가 담당하고,
    ///   이 테이블은 "종류 비율(가중치)"만 결정한다. (충돌 방지 핵심)
    /// 
    /// 업그레이드 연동 규칙:
    /// - Unlock: alwaysActive=false이면 unlockUpgradeId 레벨이 unlockMinLevel 이상일 때만 활성
    /// - Weight: finalWeight = baseWeight + (level(weightUpgradeId) * weightPerLevel)
    /// - weightUpgradeId가 비어있으면 baseWeight만 사용
    /// </summary>
    [CreateAssetMenu(menuName = "Pulseforge/Ore Spawn Table", fileName = "OreSpawnTable")]
    public class OreSpawnTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Header("Identity")]
            public string id;

            [Header("Prefab")]
            public GameObject prefab;

            [Header("Weight")]
            [Min(0f)] public float baseWeight = 1f;

            [Tooltip("가중치에 영향을 주는 업그레이드 ID (예: UD_Ore2_Weight). 비워두면 baseWeight만 사용")]
            public string weightUpgradeId;

            [Tooltip("업그레이드 레벨당 가중치 증가량 (예: 2면 레벨 1당 +2)")]
            public float weightPerLevel = 0f;

            [Header("Unlock")]
            [Tooltip("true면 항상 활성(해금 조건 무시)")]
            public bool alwaysActive = true;

            [Tooltip("해금 업그레이드 ID (예: UD_Ore3_Unlock). alwaysActive=false일 때만 사용")]
            public string unlockUpgradeId;

            [Tooltip("해금에 필요한 최소 레벨 (보통 1)")]
            [Min(0)] public int unlockMinLevel = 1;

            // ---- runtime helpers (디버그 표시용) ----
            [NonSerialized] public float _lastFinalWeight;
            [NonSerialized] public bool _lastActive;
        }

        [Header("Entries")]
        public List<Entry> entries = new List<Entry>();

        [Header("Spawn Count Compensation (Info)")]
        [Tooltip("참고용: 이 값은 '총 스폰 수'를 바꾸지 않는다. 총량은 OreSpawner/OreAmount가 담당.")]
        [SerializeField] private bool note_TotalCountIsNotHandledHere = true;

        [Header("Diagnostics")]
        [Tooltip("활성 엔트리들의 baseWeight 합 (인스펙터 디버그용)")]
        [SerializeField] private float baselineWeightSum = 0f;

        [Tooltip("Entry.baseWeight를 기준으로 baselineWeightSum을 자동 계산(디버그용)")]
        [SerializeField] private bool autoComputeBaselineFromAllEntries = true;

        [Tooltip("Pick 결과를 Debug.Log로 출력")]
        public bool debugLogPick = false;

        [Tooltip("Pick 때 활성/가중치 상태를 업데이트해서 인스펙터에서 확인 가능하게 함(개발용)")]
        public bool debugUpdateEntryRuntimeFields = true;

        // 내부 캐시(매번 리스트 할당 최소화)
        private readonly List<Entry> _active = new List<Entry>(32);

        private void OnValidate()
        {
            if (!autoComputeBaselineFromAllEntries)
                return;

            float sum = 0f;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e == null) continue;
                    if (e.prefab == null) continue;
                    sum += Mathf.Max(0f, e.baseWeight);
                }
            }
            baselineWeightSum = sum;
        }

        /// <summary>
        /// 현재 업그레이드 상태를 기준으로 "스폰 가능한 프리팹 1개"를 가중치 랜덤으로 선택한다.
        /// - 활성 엔트리가 없거나, 모든 finalWeight가 0이면 null 반환
        /// </summary>
        public GameObject PickPrefab()
        {
            BuildActiveListAndWeights();

            if (_active.Count == 0)
            {
                if (debugLogPick) Debug.Log("[OreSpawnTable] PickPrefab -> no active entries.");
                return null;
            }

            float total = 0f;
            for (int i = 0; i < _active.Count; i++)
                total += Mathf.Max(0f, _active[i]._lastFinalWeight);

            if (total <= 0f)
            {
                if (debugLogPick) Debug.Log("[OreSpawnTable] PickPrefab -> total weight is 0.");
                return null;
            }

            float r = UnityEngine.Random.value * total;
            float acc = 0f;

            for (int i = 0; i < _active.Count; i++)
            {
                var e = _active[i];
                float w = Mathf.Max(0f, e._lastFinalWeight);
                acc += w;
                if (r <= acc)
                {
                    if (debugLogPick)
                        Debug.Log($"[OreSpawnTable] PickPrefab -> '{e.id}' (w={w:0.###}, total={total:0.###})");

                    return e.prefab;
                }
            }

            // 부동소수 오차 안전장치: 마지막 엔트리 반환
            var last = _active[_active.Count - 1];
            if (debugLogPick)
                Debug.Log($"[OreSpawnTable] PickPrefab -> fallback last '{last.id}'");

            return last.prefab;
        }

        /// <summary>
        /// 외부(디버그/툴링)에서 현재 활성 엔트리와 가중치 합을 확인하고 싶을 때 사용.
        /// </summary>
        public void GetDebugSnapshot(out int activeCount, out float totalWeight)
        {
            BuildActiveListAndWeights();

            activeCount = _active.Count;
            totalWeight = 0f;
            for (int i = 0; i < _active.Count; i++)
                totalWeight += Mathf.Max(0f, _active[i]._lastFinalWeight);
        }

        // =====================================================================
        // Internals
        // =====================================================================

        private void BuildActiveListAndWeights()
        {
            _active.Clear();

            if (entries == null || entries.Count == 0)
                return;

            var up = UpgradeManager.Instance;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;

                bool active = IsEntryActive(e, up);
                float w = active ? ComputeFinalWeight(e, up) : 0f;

                if (debugUpdateEntryRuntimeFields)
                {
                    e._lastActive = active;
                    e._lastFinalWeight = w;
                }

                if (!active) continue;
                if (e.prefab == null) continue;
                if (w <= 0f) continue;

                _active.Add(e);
            }
        }

        private static bool IsEntryActive(Entry e, UpgradeManager up)
        {
            if (e.alwaysActive)
                return true;

            // alwaysActive=false면 unlock 조건이 필요
            if (string.IsNullOrEmpty(e.unlockUpgradeId))
                return false;

            if (up == null)
                return false;

            int lv = up.GetLevel(e.unlockUpgradeId);
            return lv >= Mathf.Max(0, e.unlockMinLevel);
        }

        private static float ComputeFinalWeight(Entry e, UpgradeManager up)
        {
            float baseW = Mathf.Max(0f, e.baseWeight);

            if (string.IsNullOrEmpty(e.weightUpgradeId) || e.weightPerLevel == 0f)
                return baseW;

            if (up == null)
                return baseW;

            int lv = up.GetLevel(e.weightUpgradeId);
            float add = lv * e.weightPerLevel;

            // 최종 가중치가 음수가 되지 않도록 클램프
            return Mathf.Max(0f, baseW + add);
        }
    }
}
