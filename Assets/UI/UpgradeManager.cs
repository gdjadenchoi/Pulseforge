using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 전체를 관리하는 매니저.
    /// - string upgradeId 기반으로 레벨을 관리한다. (예: "OreAmount", "CursorDamage")
    /// - UpgradeDefinition(SO)를 등록해두면 최대 레벨 / 비용 곡선 / 프리리퀴짓 등의 메타데이터를 함께 사용한다.
    /// - DontDestroyOnLoad 싱글톤.
    /// - PlayerPrefs 를 사용해 업그레이드 상태를 저장/로드한다.
    /// </summary>
    [DefaultExecutionOrder(-200)] // ★ 항상 가장 먼저 초기화되도록 실행 순서 앞으로 당김
    public class UpgradeManager : MonoBehaviour
    {
        //==============================================================
        //  싱글톤
        //==============================================================

        private static UpgradeManager _instance;
        public static UpgradeManager Instance => _instance;

        //==============================================================
        //  인스펙터 설정
        //==============================================================

        [Header("정의(선택 사항) - ScriptableObject")]
        [Tooltip("UpgradeDefinition SO 들을 등록해두면, 이름/설명/최대 레벨/비용 곡선/프리리퀴짓 등의 메타데이터로 사용한다.")]
        [SerializeField] private UpgradeDefinition[] _definitions;

        [Serializable]
        public class InitialState
        {
            [Tooltip("업그레이드 ID (UpgradeDefinition.Id 와 동일하게 맞추기)")]
            public string id;

            [Tooltip("시작 레벨 (디버그용)")]
            public int level;
        }

        [Header("디버그용 초기 레벨 설정")]
        [Tooltip("에디터 테스트를 위해 특정 업그레이드의 시작 레벨을 지정할 수 있다.")]
        [SerializeField] private InitialState[] _initialStates = Array.Empty<InitialState>();

        [Header("기본 최대 레벨 (정의가 없을 때 사용)")]
        [Min(1)]
        [SerializeField] private int _defaultMaxLevel = 99;

        [Header("디버그 옵션")]
        [SerializeField] private bool _logUpgrade = false;

        //==============================================================
        //  런타임 상태
        //==============================================================

        /// <summary>실제 업그레이드 상태</summary>
        private class RuntimeState
        {
            public string id;
            public int level;
            public UpgradeDefinition definition;
        }

        private readonly Dictionary<string, RuntimeState> _statesById = new();
        private readonly Dictionary<string, UpgradeDefinition> _defsById =
            new(StringComparer.Ordinal);

        //==============================================================
        //  저장 관련
        //==============================================================

        private const string PlayerPrefsKey = "PF_Upgrades_v1";

        [Serializable]
        private struct SaveDataEntry
        {
            public string id;
            public int level;
        }

        [Serializable]
        private struct SaveData
        {
            public SaveDataEntry[] entries;
        }

        //==============================================================
        //  이벤트
        //==============================================================

        /// <summary>특정 업그레이드의 레벨이 변경됐을 때 호출 (id, newLevel)</summary>
        public event Action<string, int> OnLevelChanged;

        //==============================================================
        //  Unity 라이프사이클
        //==============================================================

        private void Awake()
        {
            // 싱글톤 세팅
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (transform.parent != null)
                transform.SetParent(null);

            DontDestroyOnLoad(gameObject);

            BuildDefinitionLookup();

            // 저장된 상태를 먼저 시도해서 로드, 없으면 인스펙터 초기값 사용
            if (!TryLoadFromPlayerPrefs())
            {
                InitializeStatesFromInspector();
            }
        }

        private void OnApplicationQuit()
        {
            SaveToPlayerPrefs();
        }

        //==============================================================
        //  초기화
        //==============================================================

        /// <summary>
        /// _definitions 배열을 기반으로 id → UpgradeDefinition 테이블을 만든다.
        /// </summary>
        private void BuildDefinitionLookup()
        {
            _defsById.Clear();

            if (_definitions == null)
                return;

            foreach (var def in _definitions)
            {
                if (def == null)
                    continue;

                var id = def.Id;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (_defsById.ContainsKey(id))
                {
                    Debug.LogWarning(
                        $"[UpgradeManager] 중복된 UpgradeDefinition Id 발견: '{id}'. 첫 번째 것만 사용됩니다.",
                        def);
                    continue;
                }

                _defsById.Add(id, def);
            }
        }

        /// <summary>
        /// 인스펙터에서 지정한 _initialStates 를 바탕으로 런타임 상태를 구성한다.
        /// 업그레이드가 한 번도 언급되지 않으면 레벨 0으로 간주한다.
        /// </summary>
        private void InitializeStatesFromInspector()
        {
            _statesById.Clear();

            if (_initialStates != null)
            {
                foreach (var s in _initialStates)
                {
                    if (string.IsNullOrWhiteSpace(s.id))
                        continue;

                    var id = s.id.Trim();
                    var runtime = new RuntimeState
                    {
                        id = id,
                        level = Mathf.Max(0, s.level),
                        definition = GetDefinitionInternal(id)
                    };

                    // 최대 레벨로 클램프
                    int maxLevel = GetMaxLevelInternal(runtime.definition);
                    if (runtime.level > maxLevel)
                        runtime.level = maxLevel;

                    _statesById[id] = runtime;
                }
            }

            EnsureDefinitionStates();
        }

        /// <summary>
        /// _defsById 에 있는 모든 정의가 _statesById 에 최소 0레벨 상태로 존재하도록 보장한다.
        /// </summary>
        private void EnsureDefinitionStates()
        {
            foreach (var kvp in _defsById)
            {
                string id = kvp.Key;
                if (_statesById.ContainsKey(id))
                    continue;

                _statesById[id] = new RuntimeState
                {
                    id = id,
                    level = 0,
                    definition = kvp.Value
                };
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
                if (data.entries == null || data.entries.Length == 0)
                    return false;

                _statesById.Clear();

                foreach (var entry in data.entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.id))
                        continue;

                    string id = entry.id.Trim();
                    int level = Mathf.Max(0, entry.level);
                    var def = GetDefinitionInternal(id);

                    var state = new RuntimeState
                    {
                        id = id,
                        level = level,
                        definition = def
                    };

                    int maxLevel = GetMaxLevelInternal(def);
                    if (state.level > maxLevel)
                        state.level = maxLevel;

                    _statesById[id] = state;
                }

                // 정의는 있는데 세이브 데이터에는 없는 항목들 0레벨로 채우기
                EnsureDefinitionStates();

                if (_logUpgrade)
                    Debug.Log($"[UpgradeManager] 업그레이드 상태 로드 완료: {_statesById.Count}개");

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpgradeManager] 저장된 업그레이드 상태를 불러오는 중 오류 발생: {ex}");
                return false;
            }
        }

        private void SaveToPlayerPrefs()
        {
            try
            {
                var list = new List<SaveDataEntry>(_statesById.Count);
                foreach (var kvp in _statesById)
                {
                    var state = kvp.Value;
                    if (state == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(state.id))
                        continue;
                    // 0레벨은 저장 안 해도 되게 스킵 (선택 사항)
                    if (state.level <= 0)
                        continue;

                    list.Add(new SaveDataEntry
                    {
                        id = state.id,
                        level = state.level
                    });
                }

                var data = new SaveData
                {
                    entries = list.ToArray()
                };

                string json = JsonUtility.ToJson(data);
                PlayerPrefs.SetString(PlayerPrefsKey, json);
                PlayerPrefs.Save();

                if (_logUpgrade)
                    Debug.Log($"[UpgradeManager] 업그레이드 상태 저장 완료: {list.Count}개");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UpgradeManager] 업그레이드 상태 저장 중 오류 발생: {ex}");
            }
        }

        /// <summary>
        /// 모든 업그레이드를 0레벨로 리셋하고, 저장 데이터도 삭제한다.
        /// (디버그/테스트용, 버튼에서 호출 가능)
        /// </summary>
        [ContextMenu("Reset All Upgrades")]
        public void ResetAllUpgrades()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);

            _statesById.Clear();
            InitializeStatesFromInspector();

            if (_logUpgrade)
                Debug.Log("[UpgradeManager] 모든 업그레이드 리셋 + 저장 데이터 삭제");

            // UI 갱신을 위해 현재 상태를 모두 이벤트로 쏴준다.
            foreach (var kvp in _statesById)
            {
                var state = kvp.Value;
                if (state == null) continue;
                OnLevelChanged?.Invoke(state.id, state.level);
            }
        }

        //==============================================================
        //  내부 유틸
        //==============================================================

        private UpgradeDefinition GetDefinitionInternal(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            _defsById.TryGetValue(id, out var def);
            return def;
        }

        private RuntimeState GetOrCreateState(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            id = id.Trim();

            if (_statesById.TryGetValue(id, out var state))
                return state;

            var def = GetDefinitionInternal(id);
            state = new RuntimeState
            {
                id = id,
                level = 0,
                definition = def
            };
            _statesById.Add(id, state);
            return state;
        }

        private int GetMaxLevelInternal(UpgradeDefinition def)
        {
            if (def == null)
                return _defaultMaxLevel;

            return Mathf.Max(1, def.MaxLevel);
        }

        //==============================================================
        //  퍼블릭 조회 API
        //==============================================================

        /// <summary>특정 업그레이드의 현재 레벨을 반환 (정의/상태가 없으면 0)</summary>
        public int GetLevel(string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return 0;

            upgradeId = upgradeId.Trim();

            return _statesById.TryGetValue(upgradeId, out var state)
                ? Mathf.Max(0, state.level)
                : 0;
        }

        /// <summary>특정 업그레이드의 최대 레벨을 반환</summary>
        public int GetMaxLevel(string upgradeId)
        {
            var def = GetDefinitionInternal(upgradeId);
            return GetMaxLevelInternal(def);
        }

        /// <summary>특정 업그레이드의 ScriptableObject 정의를 반환 (없을 수 있음)</summary>
        public UpgradeDefinition GetDefinition(string upgradeId)
        {
            return GetDefinitionInternal(upgradeId);
        }

        /// <summary>현재 레벨이 최대 레벨인지 여부</summary>
        public bool IsMaxLevel(string upgradeId)
        {
            int level = GetLevel(upgradeId);
            int max = GetMaxLevel(upgradeId);
            return level >= max;
        }

        //==============================================================
        //  프리리퀴짓 체크
        //==============================================================

        /// <summary>
        /// 이 업그레이드가 해금 조건(플레이어 레벨, 선행 업그레이드)을 만족하는지 여부.
        /// 해금 조건이 없다면 항상 true 를 반환한다.
        /// </summary>
        public bool MeetsPrerequisites(string upgradeId)
        {
            var def = GetDefinitionInternal(upgradeId);
            if (def == null)
                return true;

            int playerLevel = 0;
            var levelManager = LevelManager.Instance;
            if (levelManager != null)
                playerLevel = levelManager.Level;

            // UpgradeDefinition 내부의 CheckPrerequisites 로 위임
            return def.CheckPrerequisites(playerLevel, GetLevel);
        }

        //==============================================================
        //  업그레이드 처리
        //==============================================================

        /// <summary>
        /// 업그레이드를 시도한다.
        /// - 아직 최대 레벨이 아니고, 해금 조건을 모두 만족하면 레벨을 1 올리고 true 반환.
        /// - 이미 최대 레벨이거나 조건을 만족하지 못하면 false 반환.
        /// </summary>
        public bool TryUpgrade(string upgradeId)
        {
            var state = GetOrCreateState(upgradeId);
            if (state == null)
                return false;

            int maxLevel = GetMaxLevelInternal(state.definition);
            if (state.level >= maxLevel)
            {
                if (_logUpgrade)
                    Debug.Log($"[UpgradeManager] '{upgradeId}' 이미 최대 레벨({maxLevel})입니다.");
                return false;
            }

            // 선행 조건(플레이어 레벨, 다른 업그레이드 레벨) 검사
            if (!MeetsPrerequisites(upgradeId))
            {
                if (_logUpgrade)
                    Debug.Log($"[UpgradeManager] '{upgradeId}' 업그레이드 실패: 선행 조건을 만족하지 않습니다.");
                return false;
            }

            state.level++;

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] '{upgradeId}' 업그레이드 → Lv.{state.level}");

            OnLevelChanged?.Invoke(upgradeId, state.level);

            // 레벨 변경 후 즉시 저장
            SaveToPlayerPrefs();

            return true;
        }

        /// <summary>
        /// 강제로 레벨을 설정하는 디버그용 함수.
        /// </summary>
        public void SetLevel(string upgradeId, int level)
        {
            var state = GetOrCreateState(upgradeId);
            if (state == null)
                return;

            int maxLevel = GetMaxLevelInternal(state.definition);
            state.level = Mathf.Clamp(level, 0, maxLevel);

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] '{upgradeId}' 레벨 강제 설정 → Lv.{state.level}");

            OnLevelChanged?.Invoke(upgradeId, state.level);

            // 강제 설정도 저장
            SaveToPlayerPrefs();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_initialStates == null)
                _initialStates = Array.Empty<InitialState>();

            if (_definitions == null)
                _definitions = Array.Empty<UpgradeDefinition>();
        }
#endif
    }
}
