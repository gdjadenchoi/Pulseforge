using System;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 계정 단위 레벨 / 경험치 관리 매니저
    /// - PF_Outpost, PF_Mining 등 모든 씬에서 공유
    /// - DontDestroyOnLoad 싱글톤
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        //==============================================================
        //  싱글톤
        //==============================================================
        private static LevelManager _instance;
        public static LevelManager Instance => _instance;

        //==============================================================
        //  인스펙터 설정
        //==============================================================

        [Header("초기 값 설정")]
        [Tooltip("게임 시작 시 기본 레벨")]
        [SerializeField] private int startLevel = 1;

        [Tooltip("레벨 최소값 (보통 1 고정)")]
        [SerializeField] private int minLevel = 1;

        [Tooltip("레벨 최대값 (이 이상은 안 올라감)")]
        [SerializeField] private int maxLevel = 999;

        [Header("현재 상태 (디버그 표시용)")]
        [SerializeField] private int level = 1;
        [SerializeField] private long currentExp = 0;
        [SerializeField] private long expToNext = 40;

        [Header("경험치 증가 업그레이드 연동")]
        [Tooltip("UpgradeDefinition.Id / effectKey 와 동일하게 맞출 업그레이드 ID (예: \"ExpGain\")")]
        [SerializeField] private string expGainUpgradeId = "ExpGain";

        [Tooltip("ExpGain 업그레이드 1레벨당 추가로 더해줄 고정 경험치량")]
        [SerializeField] private int expGainPerLevel = 1;

        [Header("디버그 옵션")]
        [Tooltip("경험치가 추가될 때마다 로그를 찍을지 여부")]
        [SerializeField] private bool logExpGain = false;

        //==============================================================
        //  프로퍼티
        //==============================================================

        /// <summary>현재 레벨</summary>
        public int Level => level;

        /// <summary>현재 경험치</summary>
        public long CurrentExp => currentExp;

        /// <summary>다음 레벨까지 필요한 경험치</summary>
        public long ExpToNext => expToNext;

        /// <summary>설정된 최소 레벨</summary>
        public int MinLevel => minLevel;

        /// <summary>설정된 최대 레벨</summary>
        public int MaxLevel => maxLevel;

        //==============================================================
        //  이벤트
        //==============================================================

        /// <summary>레벨이 바뀌었을 때 호출 (현재 레벨)</summary>
        public event Action<int> OnLevelChanged;

        /// <summary>경험치가 바뀌었을 때 호출 (현재 경험치, 다음 레벨까지 필요량)</summary>
        public event Action<long, long> OnExpChanged;

        /// <summary>레벨업이 발생했을 때 호출 (새 레벨)</summary>
        public event Action<int> OnLevelUp;

        //==============================================================
        //  저장 관련 (PlayerPrefs)
        //==============================================================

        private const string PlayerPrefsKey = "PF_Level_v1";

        [Serializable]
        private struct SaveData
        {
            public int level;
            public long currentExp;
        }

        //==============================================================
        //  Unity 라이프사이클
        //==============================================================

        private void Awake()
        {
            // 싱글톤 유지 + DontDestroyOnLoad
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (transform.parent != null)
                transform.SetParent(null);

            DontDestroyOnLoad(gameObject);

            // 저장된 값 우선 로드, 없으면 기존 초기화 로직 사용
            if (!TryLoadFromPlayerPrefs())
            {
                InitializeLevel();
            }
            else
            {
                // 로드된 레벨/경험치 기준으로 expToNext 재계산 및 범위 클램프
                ClampLevelRange();
                RecalculateExpToNext();
            }
        }

        private void OnEnable()
        {
            RefreshDebugFields();
            FireAllEvents();
        }

        private void OnApplicationQuit()
        {
            SaveToPlayerPrefs();
        }

        //==============================================================
        //  초기화 / 내부 유틸
        //==============================================================

        /// <summary>
        /// 시작 레벨 / 경험치 초기화
        /// </summary>
        private void InitializeLevel()
        {
            if (maxLevel < 1) maxLevel = 1;
            if (minLevel < 1) minLevel = 1;

            level = Mathf.Clamp(startLevel, minLevel, maxLevel);
            currentExp = 0;
            expToNext = GetRequiredExp(level);
        }

        /// <summary>전체 리셋 (디버그용)</summary>
        public void ResetAll()
        {
            // 저장 데이터 삭제 + 초기화
            PlayerPrefs.DeleteKey(PlayerPrefsKey);

            InitializeLevel();
            RefreshDebugFields();
            FireAllEvents();

            // 리셋 상태를 다시 저장 (선택사항이지만, 일관성을 위해)
            SaveToPlayerPrefs();
        }

        /// <summary>디버그용 인스펙터 값 동기화 (현재는 별도 복제 없음)</summary>
        private void RefreshDebugFields()
        {
            // level / currentExp / expToNext 자체가 인스펙터에 노출되어서 별도 작업 없음
        }

        /// <summary>현재 상태를 기반으로 모든 이벤트 한 번씩 쏘기</summary>
        private void FireAllEvents()
        {
            OnLevelChanged?.Invoke(level);
            OnExpChanged?.Invoke(currentExp, expToNext);
        }

        private void ClampLevelRange()
        {
            if (minLevel < 1) minLevel = 1;
            if (maxLevel < 1) maxLevel = 1;
            if (minLevel > maxLevel) minLevel = maxLevel;

            level = Mathf.Clamp(level, minLevel, maxLevel);
        }

        private void RecalculateExpToNext()
        {
            if (level >= maxLevel)
            {
                expToNext = 0;
                currentExp = 0;
            }
            else
            {
                expToNext = GetRequiredExp(level);
                if (currentExp < 0) currentExp = 0;
                if (currentExp > expToNext) currentExp = expToNext;
            }
        }

        //==============================================================
        //  저장 / 로드
        //==============================================================

        private bool TryLoadFromPlayerPrefs()
        {
            if (!PlayerPrefs.HasKey(PlayerPrefsKey))
                return false;

            string json = PlayerPrefs.GetString(PlayerPrefsKey, null);
            if (string.IsNullOrEmpty(json))
                return false;

            try
            {
                var data = JsonUtility.FromJson<SaveData>(json);

                // 로드된 값 적용
                level = data.level;
                currentExp = data.currentExp;

                if (logExpGain)
                    Debug.Log($"[LevelManager] Load level={level}, exp={currentExp}");

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LevelManager] 레벨 저장 데이터 로드 중 오류: {e}");
                return false;
            }
        }

        private void SaveToPlayerPrefs()
        {
            try
            {
                var data = new SaveData
                {
                    level = level,
                    currentExp = currentExp
                };

                string json = JsonUtility.ToJson(data);
                PlayerPrefs.SetString(PlayerPrefsKey, json);
                PlayerPrefs.Save();

                if (logExpGain)
                    Debug.Log($"[LevelManager] Save level={level}, exp={currentExp}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LevelManager] 레벨 저장 데이터 저장 중 오류: {e}");
            }
        }

        //==============================================================
        //  경험치 곡선
        //==============================================================

        /// <summary>
        /// 주어진 레벨에서 다음 레벨까지 필요한 경험치량
        /// RequiredExp(level) = 12 * level^2 + 28 * level
        /// </summary>
        public long GetRequiredExp(int targetLevel)
        {
            if (targetLevel < 1) targetLevel = 1;

            long l = targetLevel;
            return 12L * l * l + 28L * l;
        }

        //==============================================================
        //  ExpGain 업그레이드 연동
        //==============================================================

        /// <summary>
        /// ExpGain 업그레이드에 의해 추가로 더해질 고정 경험치량 계산
        /// (업그레이드 1레벨당 expGainPerLevel 만큼 추가)
        /// </summary>
        private int GetExpBonusFromUpgrade()
        {
            if (string.IsNullOrWhiteSpace(expGainUpgradeId))
                return 0;

            var upgradeManager = UpgradeManager.Instance;
            if (upgradeManager == null)
                return 0;

            int upgradeLevel = upgradeManager.GetLevel(expGainUpgradeId);
            if (upgradeLevel <= 0 || expGainPerLevel <= 0)
                return 0;

            return expGainPerLevel * upgradeLevel;
        }

        /// <summary>
        /// 업그레이드를 포함한 최종 경험치 획득량 계산
        /// </summary>
        private int GetFinalExpAmount(int baseAmount)
        {
            if (baseAmount <= 0)
                return 0;

            int bonus = GetExpBonusFromUpgrade();
            int final = baseAmount + bonus;

            if (final < 0) final = 0;

            if (logExpGain)
            {
                Debug.Log($"[LevelManager] AddExp base:{baseAmount} bonus:{bonus} final:{final}");
            }

            return final;
        }

        //==============================================================
        //  경험치 추가 / 레벨업 처리
        //==============================================================

        /// <summary>
        /// 경험치 추가 (업그레이드 포함)
        /// </summary>
        /// <param name="baseAmount">기본으로 더해줄 경험치량 (광석 기준 값 등)</param>
        public void AddExp(int baseAmount)
        {
            int amount = GetFinalExpAmount(baseAmount);
            if (amount <= 0)
                return;

            // 이미 최댓 레벨이면 더 이상 쌓지 않음
            if (level >= maxLevel)
            {
                level = maxLevel;
                currentExp = 0;
                expToNext = 0;
                RefreshDebugFields();
                FireAllEvents();
                SaveToPlayerPrefs();
                return;
            }

            long prevLevel = level;
            long prevExp = currentExp;

            currentExp += amount;

            // 여러 레벨이 한 번에 오를 수도 있으므로 while
            while (level < maxLevel && currentExp >= expToNext)
            {
                currentExp -= expToNext;
                level++;

                OnLevelUp?.Invoke(level);

                if (level >= maxLevel)
                {
                    level = maxLevel;
                    currentExp = 0;
                    expToNext = 0;
                    break;
                }

                expToNext = GetRequiredExp(level);
            }

            // 레벨업이 하나도 안 되었거나, 중간에 expToNext 갱신 필요할 수 있음
            if (level < maxLevel && expToNext <= 0)
            {
                expToNext = GetRequiredExp(level);
            }

            RefreshDebugFields();

            if (prevLevel != level)
                OnLevelChanged?.Invoke(level);

            if (prevExp != currentExp)
                OnExpChanged?.Invoke(currentExp, expToNext);

            // 변경된 상태 저장
            SaveToPlayerPrefs();
        }

#if UNITY_EDITOR
        // 에디터에서 값이 바뀌었을 때도 expToNext를 바로 맞춰주기 위한 편의 기능
        private void OnValidate()
        {
            if (minLevel < 1) minLevel = 1;
            if (maxLevel < 1) maxLevel = 1;
            if (minLevel > maxLevel) minLevel = maxLevel;

            level = Mathf.Clamp(level, minLevel, maxLevel);
            startLevel = Mathf.Clamp(startLevel, minLevel, maxLevel);

            expToNext = GetRequiredExp(level);
            if (currentExp < 0) currentExp = 0;
        }
#endif
    }
}
