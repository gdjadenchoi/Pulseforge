using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// Big Ore 스폰 룰/확률/시간대 데이터를 "커브 없이" 테이블로 관리하는 SO.
    ///
    /// 핵심 설계(확장형):
    /// - slots[0] = 1번째 Big Ore 판정
    /// - slots[1] = 2번째 Big Ore 판정
    /// - ...
    /// - i번째는 (i-1)번째가 "성공"했을 때만 굴린다.
    ///
    /// 해금:
    /// - unlockUpgradeId 레벨이 unlockMinLevel 이상이면 활성화
    /// - 해금되면 slots[0].baseChance를 기본 확률(예: 5%)로 사용
    ///
    /// 확률 업그레이드:
    /// - 각 슬롯은 업그레이드 ID를 가지고, 레벨에 따라 확률이 증가(또는 테이블/스텝)
    ///
    /// 시간:
    /// - 각 슬롯은 스폰 시점을 세션 내 랜덤 시간으로 뽑되,
    ///   세션 종료 직전(noSpawnLastSeconds)에는 스폰되지 않게 클램프.
    /// - 슬롯 간 최소 간격(minGapBetweenSpawns)도 적용 가능.
    /// </summary>
    [CreateAssetMenu(menuName = "Pulseforge/Big Ore Spawn Settings", fileName = "BigOreSpawnSettings")]
    public class BigOreSpawnSettings : ScriptableObject
    {
        public enum ChanceMode
        {
            /// <summary>
            /// baseChance + (upgradeLevel * perLevelAdd)
            /// </summary>
            LinearPerLevel = 0,

            /// <summary>
            /// 레벨별 "추가 확률" 테이블 합산:
            /// final = baseChance + sum(addTable[0..lv-1])
            /// 예) [0.01, 0.01, 0.02]이면
            /// lv1:+1%, lv2:+2%, lv3:+4%
            /// </summary>
            AdditiveTableByLevel = 1,

            /// <summary>
            /// 레벨별 "최종 확률" 테이블:
            /// final = finalTable[lv] (범위 밖이면 마지막 값)
            /// </summary>
            FinalTableByLevel = 2
        }

        [Serializable]
        public class SpawnSlot
        {
            [Header("Identity")]
            [Tooltip("디버그/표시용 슬롯 ID (예: Spawn1, Spawn2, Spawn3...)")]
            public string slotId = "Spawn1";

            [Header("Unlock (Optional)")]
            [Tooltip("true면 이 슬롯은 별도 해금 조건 없이 사용 가능(단, i번째는 i-1 성공 조건은 그대로 적용됨)")]
            public bool alwaysUnlocked = true;

            [Tooltip("alwaysUnlocked=false일 때만 사용. 이 업그레이드 레벨이 unlockMinLevel 이상이면 슬롯 활성")]
            public string unlockUpgradeId;

            [Min(0)] public int unlockMinLevel = 1;

            [Header("Chance")]
            [Range(0f, 1f)] public float baseChance = 0.05f; // 기본 5%
            public ChanceMode chanceMode = ChanceMode.LinearPerLevel;

            [Tooltip("확률 증가 업그레이드 ID (예: UD_BigOre_Spawn2Chance)")]
            public string chanceUpgradeId;

            [Tooltip("LinearPerLevel일 때: 레벨당 추가 확률(예: 0.01 = +1%)")]
            [Range(0f, 1f)] public float perLevelAdd = 0.00f;

            [Tooltip("AdditiveTableByLevel일 때: 레벨별 추가 확률 목록(인덱스 0이 레벨1에 해당)")]
            public List<float> additiveByLevel = new List<float>();

            [Tooltip("FinalTableByLevel일 때: 레벨별 최종 확률 목록(인덱스 0이 레벨0, 1이 레벨1...)")]
            public List<float> finalChanceByLevel = new List<float>();

            [Header("Clamp")]
            [Tooltip("최종 확률 상한(안전장치). 0이면 100% 상한으로 취급")]
            [Range(0f, 1f)] public float maxChance = 0f;

            // ---- runtime debug ----
            [NonSerialized] public bool _lastUnlocked;
            [NonSerialized] public float _lastFinalChance;
            [NonSerialized] public bool _lastRolledSuccess;
        }

        [Header("Global Unlock (BigOre 전체 해금)")]
        [Tooltip("BigOre 전체 해금 업그레이드 ID. 이 레벨이 unlockMinLevel 미만이면 BigOre 스폰을 아예 시도하지 않음.")]
        public string globalUnlockUpgradeId = "UD_BigOre_Unlock";

        [Min(0)] public int globalUnlockMinLevel = 1;

        [Header("Spawn Slots (1..N)")]
        [Tooltip("slots[0]=1번째, slots[1]=2번째 ... i번째는 i-1 성공 시에만 판정됨")]
        public List<SpawnSlot> slots = new List<SpawnSlot>()
        {
            new SpawnSlot()
            {
                slotId = "Spawn1",
                alwaysUnlocked = true,
                baseChance = 0.05f,
                chanceMode = ChanceMode.LinearPerLevel,
                chanceUpgradeId = "UD_BigOre_Spawn1Chance",
                perLevelAdd = 0.00f,
                maxChance = 0f
            },
            new SpawnSlot()
            {
                slotId = "Spawn2",
                alwaysUnlocked = false,
                unlockUpgradeId = "UD_BigOre_Spawn2Unlock",
                unlockMinLevel = 1,
                baseChance = 0.05f,
                chanceMode = ChanceMode.LinearPerLevel,
                chanceUpgradeId = "UD_BigOre_Spawn2Chance",
                perLevelAdd = 0.00f,
                maxChance = 0f
            }
        };

        [Header("Timing (Session-relative)")]
        [Tooltip("세션 시작 후 최소 지연(초). (Start 연출/스폰 안정화 이후를 가정)")]
        [Min(0f)] public float minSpawnDelay = 1.5f;

        [Tooltip("세션 내 랜덤 스폰 추가 지연 최대(초). 실제 스폰 시점은 [minSpawnDelay, maxSpawnDelay] 사이에서 랜덤.")]
        [Min(0f)] public float maxSpawnDelay = 8.0f;

        [Tooltip("세션 종료 직전 이 시간(초) 안에는 스폰 금지. (예: 5초)")]
        [Min(0f)] public float noSpawnLastSeconds = 5.0f;

        [Tooltip("여러 번 스폰될 때 스폰 간 최소 간격(초). (0이면 제한 없음)")]
        [Min(0f)] public float minGapBetweenSpawns = 8.0f;

        [Header("Diagnostics")]
        public bool debugLog = false;

        // ============================================================
        // Public API (스포너에서 호출)
        // ============================================================

        /// <summary>
        /// 이번 세션에 스폰될 BigOre 횟수를 "연쇄 판정"으로 계산한다.
        /// i번째는 i-1이 성공했을 때만 굴린다.
        /// </summary>
        public int RollSpawnCount(UpgradeManager up, System.Random deterministic = null, bool updateRuntimeDebug = true)
        {
            if (!IsGloballyUnlocked(up))
            {
                if (updateRuntimeDebug) ClearRuntimeDebug();
                if (debugLog) Debug.Log("[BigOreSpawnSettings] Global locked -> spawnCount=0");
                return 0;
            }

            if (slots == null || slots.Count == 0)
            {
                if (updateRuntimeDebug) ClearRuntimeDebug();
                return 0;
            }

            int count = 0;
            bool prevSuccess = true;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) continue;

                bool unlocked = IsSlotUnlocked(s, up);

                float chance = unlocked ? ComputeFinalChance01(s, up) : 0f;
                if (s.maxChance > 0f) chance = Mathf.Min(chance, s.maxChance);
                chance = Mathf.Clamp01(chance);

                bool rolled = false;

                // i번째는 i-1 성공했을 때만
                if (prevSuccess && unlocked && chance > 0f)
                {
                    float r = deterministic != null ? (float)deterministic.NextDouble() : UnityEngine.Random.value;
                    rolled = (r <= chance);

                    if (rolled) count++;
                }
                else
                {
                    rolled = false;
                }

                if (updateRuntimeDebug)
                {
                    s._lastUnlocked = unlocked;
                    s._lastFinalChance = chance;
                    s._lastRolledSuccess = rolled;
                }

                if (debugLog)
                {
                    Debug.Log($"[BigOreSpawnSettings] {s.slotId} unlocked={unlocked} chance={chance:0.###} prevSuccess={prevSuccess} -> success={rolled}");
                }

                // 연쇄 규칙
                prevSuccess = rolled;
                if (!prevSuccess) break; // 실패하면 이후는 전부 미굴림
            }

            return count;
        }

        /// <summary>
        /// 세션 길이(초)를 입력받아, spawnCount개 만큼의 "세션 경과 시간" 스폰 시점을 만든다.
        /// - [minSpawnDelay, (sessionLen-noSpawnLastSeconds)] 범위 내에서 랜덤
        /// - minGapBetweenSpawns로 최소 간격 보장
        /// </summary>
        public List<float> BuildSpawnSchedule(int spawnCount, float sessionDurationSec, System.Random deterministic = null)
        {
            var result = new List<float>(Mathf.Max(0, spawnCount));
            if (spawnCount <= 0) return result;

            float latestAllowed = Mathf.Max(0f, sessionDurationSec - Mathf.Max(0f, noSpawnLastSeconds));
            float earliest = Mathf.Max(0f, minSpawnDelay);

            if (latestAllowed <= earliest)
            {
                if (debugLog) Debug.Log($"[BigOreSpawnSettings] No valid spawn window. earliest={earliest:0.###}, latestAllowed={latestAllowed:0.###}");
                return result;
            }

            float lastT = -9999f;

            for (int i = 0; i < spawnCount; i++)
            {
                float a = earliest;
                float b = Mathf.Min(maxSpawnDelay, latestAllowed);
                if (b < a) b = latestAllowed;

                // 최소 간격 반영
                if (minGapBetweenSpawns > 0f && i > 0)
                {
                    a = Mathf.Max(a, lastT + minGapBetweenSpawns);
                }

                if (a >= latestAllowed)
                {
                    if (debugLog)
                    {
                        Debug.Log($"[BigOreSpawnSettings] schedule stopped: cannot fit spawn#{i + 1}. " +
                                $"a={a:0.###} >= latestAllowed={latestAllowed:0.###}. " +
                                $"(session={sessionDurationSec:0.###}, noSpawnLast={noSpawnLastSeconds:0.###}, minGap={minGapBetweenSpawns:0.###})");
                    }
                    break;
                }

                float t = LerpRand(a, b, deterministic);
                t = Mathf.Min(t, latestAllowed);

                result.Add(t);
                lastT = t;
            }

            result.Sort();
            return result;
        }

        // ============================================================
        // Internals
        // ============================================================

        private bool IsGloballyUnlocked(UpgradeManager up)
        {
            if (string.IsNullOrEmpty(globalUnlockUpgradeId))
                return true;

            if (up == null) up = UpgradeManager.Instance;
            if (up == null) return false;

            int lv = up.GetLevel(globalUnlockUpgradeId);
            return lv >= Mathf.Max(0, globalUnlockMinLevel);
        }

        private static bool IsSlotUnlocked(SpawnSlot s, UpgradeManager up)
        {
            if (s.alwaysUnlocked) return true;

            if (string.IsNullOrEmpty(s.unlockUpgradeId)) return false;

            if (up == null) up = UpgradeManager.Instance;
            if (up == null) return false;

            int lv = up.GetLevel(s.unlockUpgradeId);
            return lv >= Mathf.Max(0, s.unlockMinLevel);
        }

        private static float ComputeFinalChance01(SpawnSlot s, UpgradeManager up)
        {
            float baseC = Mathf.Clamp01(s.baseChance);

            // 업그레이드가 없으면 base만
            if (string.IsNullOrEmpty(s.chanceUpgradeId))
                return baseC;

            if (up == null) up = UpgradeManager.Instance;
            if (up == null) return baseC;

            int lv = up.GetLevel(s.chanceUpgradeId);
            if (lv <= 0) return baseC;

            switch (s.chanceMode)
            {
                case ChanceMode.LinearPerLevel:
                    return Mathf.Clamp01(baseC + (lv * Mathf.Max(0f, s.perLevelAdd)));

                case ChanceMode.AdditiveTableByLevel:
                {
                    float add = 0f;
                    if (s.additiveByLevel != null && s.additiveByLevel.Count > 0)
                    {
                        int n = Mathf.Min(lv, s.additiveByLevel.Count);
                        for (int i = 0; i < n; i++)
                            add += Mathf.Max(0f, s.additiveByLevel[i]);
                    }
                    return Mathf.Clamp01(baseC + add);
                }

                case ChanceMode.FinalTableByLevel:
                {
                    if (s.finalChanceByLevel == null || s.finalChanceByLevel.Count == 0)
                        return baseC;

                    int idx = Mathf.Clamp(lv, 0, s.finalChanceByLevel.Count - 1);
                    return Mathf.Clamp01(s.finalChanceByLevel[idx]);
                }
            }

            return baseC;
        }

        private static float LerpRand(float a, float b, System.Random deterministic)
        {
            if (b < a) { var t = a; a = b; b = t; }
            float r = deterministic != null ? (float)deterministic.NextDouble() : UnityEngine.Random.value;
            return Mathf.Lerp(a, b, r);
        }

        private void ClearRuntimeDebug()
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                s._lastUnlocked = false;
                s._lastFinalChance = 0f;
                s._lastRolledSuccess = false;
            }
        }
    }
}
