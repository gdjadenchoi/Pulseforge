using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 싱글톤 매니저(DDOL)
    ///
    /// 이 교체본의 목적:
    /// - UpgradeEntry.cs가 요구하는 API를 다시 제공:
    ///   - GetDefinition(string id)
    ///   - MeetsPrerequisites(string id)
    ///   - TryUpgrade(string id)
    /// - GetLevel MISS 시 자동으로 0레벨 상태 생성(안정성)
    ///
    /// 주의:
    /// - UpgradeDefinition.cs는 프로젝트에 이미 존재(여기서 선언하지 않음)
    /// - UpgradeDefinition의 필드/프로퍼티 이름이 프로젝트마다 다를 수 있어 리플렉션으로 안전하게 읽음
    /// </summary>
    public class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance => _instance;
        private static UpgradeManager _instance;

        private const string PlayerPrefsKey = "PF_Upgrades_v1";

        [Header("Definitions")]
        [SerializeField] private List<UpgradeDefinition> definitions = new List<UpgradeDefinition>();

        [Header("Initial States (optional)")]
        [SerializeField] private List<InitialState> initialStates = new List<InitialState>();

        [Header("Debug")]
        [SerializeField] private bool _logUpgrade = false;
        [SerializeField] private bool _logMissWarnings = true;

        private readonly Dictionary<string, UpgradeState> _statesById = new Dictionary<string, UpgradeState>(StringComparer.Ordinal);
        private readonly Dictionary<string, UpgradeDefinition> _defById = new Dictionary<string, UpgradeDefinition>(StringComparer.Ordinal);

        public event Action<string, int> OnLevelChanged;

        [Serializable]
        public struct InitialState
        {
            public string id;
            public int level;
        }

        [Serializable]
        private class SaveData
        {
            public Entry[] entries;
        }

        [Serializable]
        private struct Entry
        {
            public string id;
            public int level;
        }

        private struct UpgradeState
        {
            public int level;
            public UpgradeState(int level) => this.level = Mathf.Max(0, level);
        }

        private void Awake()
        {
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

            bool loaded = TryLoadFromPlayerPrefs();
            if (!loaded)
                InitializeStatesFromInspector();

            // 정의에 있는 모든 업그레이드 ID는 항상 states에 존재하도록 보장
            EnsureDefinitionStates();

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] Awake done. loaded={loaded} defs={definitions?.Count ?? 0} states={_statesById.Count} inst={GetInstanceID()}");
        }

        private void OnApplicationQuit()
        {
            SaveToPlayerPrefs();
        }

        // =========================================================
        // ✅ UpgradeEntry.cs가 기대하는 API (컴파일 복구 핵심)
        // =========================================================

        /// <summary>
        /// UpgradeEntry.cs가 정의를 받아 UI를 구성할 때 사용.
        /// </summary>
        public UpgradeDefinition GetDefinition(string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return null;

            upgradeId = upgradeId.Trim();

            if (_defById.TryGetValue(upgradeId, out var def))
                return def;

            // 혹시 definitions가 늦게 채워졌다면 재구축 시도
            BuildDefinitionLookup();
            return _defById.TryGetValue(upgradeId, out def) ? def : null;
        }

        /// <summary>
        /// UpgradeEntry.cs가 "선행조건 충족 여부"를 체크할 때 사용.
        /// 프로젝트마다 prerequisite 구조가 달라서:
        /// - 정의에 prerequisite 정보를 못 찾으면 "막지 않기" 위해 true 반환(보수적)
        /// </summary>
        public bool MeetsPrerequisites(string upgradeId)
        {
            var def = GetDefinition(upgradeId);
            if (def == null)
                return true;

            // 1) 흔한 형태: string prerequisiteId / string[] prerequisiteIds / List<string> prerequisiteIds
            // 2) 혹은 struct/클래스 배열: (id, levelRequired) 형태
            // 프로젝트 구현을 모르므로, "찾히는 것만" 검사하고, 못 찾으면 true로 둔다.

            // (A) 단일 prerequisiteId
            string singleId = ReadStringMember(def, new[]
            {
                "PrerequisiteId","prerequisiteId","RequireId","requireId","UnlockPrerequisiteId","unlockPrerequisiteId"
            });
            if (!string.IsNullOrWhiteSpace(singleId))
            {
                return GetLevel(singleId) > 0;
            }

            // (B) string 리스트/배열
            var idList = ReadStringListMember(def, new[]
            {
                "PrerequisiteIds","prerequisiteIds","Requires","requires","UnlockPrerequisiteIds","unlockPrerequisiteIds"
            });
            if (idList != null && idList.Count > 0)
            {
                for (int i = 0; i < idList.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(idList[i])) continue;
                    if (GetLevel(idList[i].Trim()) <= 0)
                        return false;
                }
                return true;
            }

            // (C) (id, requiredLevel) 형태의 리스트/배열을 추정
            // 멤버명이 아래 중 하나면 탐색해본다.
            var reqObjects = ReadObjectListMember(def, new[]
            {
                "Prerequisites","prerequisites","Requirements","requirements","UnlockRequirements","unlockRequirements"
            });

            if (reqObjects != null && reqObjects.Count > 0)
            {
                for (int i = 0; i < reqObjects.Count; i++)
                {
                    object req = reqObjects[i];
                    if (req == null) continue;

                    string rid = ReadStringFromAny(req, new[] { "id", "Id", "upgradeId", "UpgradeId", "requireId", "RequireId" });
                    int rlv = ReadIntFromAny(req, new[] { "level", "Level", "requiredLevel", "RequiredLevel", "minLevel", "MinLevel" }, defaultValue: 1);

                    if (!string.IsNullOrWhiteSpace(rid))
                    {
                        if (GetLevel(rid.Trim()) < rlv)
                            return false;
                    }
                }
                return true;
            }

            // prerequisite 정보를 정의에서 못 찾으면 막지 않는다.
            return true;
        }

        /// <summary>
        /// UpgradeEntry.cs가 버튼 클릭 시 호출하는 업그레이드 시도 함수.
        /// 비용/재화 차감은 프로젝트마다 RewardManager/Wallet 등이 따로 있으니
        /// 여기서는 "레벨업 가능 여부 + 선행조건 + Max"만 최소 보장.
        /// </summary>
        public bool TryUpgrade(string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return false;

            upgradeId = upgradeId.Trim();

            if (!MeetsPrerequisites(upgradeId))
                return false;

            if (IsMaxed(upgradeId))
                return false;

            int before = GetLevel(upgradeId);
            SetLevel(upgradeId, before + 1, raiseEvent: true);
            return true;
        }

        // =========================================================
        // Public API (기본)
        // =========================================================

        public int GetLevel(string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return 0;

            upgradeId = upgradeId.Trim();

            if (_statesById.TryGetValue(upgradeId, out var state))
                return Mathf.Max(0, state.level);

            // MISS면 자동 생성
            _statesById[upgradeId] = new UpgradeState(0);

            if (_logMissWarnings)
                Debug.LogWarning($"[UpgradeManager] GetLevel MISS id='{upgradeId}' (states={_statesById.Count}) -> auto-create level=0");

            return 0;
        }

        public int GetMaxLevel(string upgradeId)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return 0;

            upgradeId = upgradeId.Trim();
            var def = GetDefinition(upgradeId);
            if (def == null)
                return 0;

            return Mathf.Max(0, ReadDefMaxLevel(def));
        }

        public bool IsMaxed(string upgradeId)
        {
            int lv = GetLevel(upgradeId);
            int max = GetMaxLevel(upgradeId);
            return max > 0 && lv >= max;
        }

        public void SetLevel(string upgradeId, int level, bool raiseEvent = true)
        {
            if (string.IsNullOrWhiteSpace(upgradeId))
                return;

            upgradeId = upgradeId.Trim();
            level = Mathf.Max(0, level);

            if (!_statesById.TryGetValue(upgradeId, out var state))
                state = new UpgradeState(0);

            int max = GetMaxLevel(upgradeId);
            if (max > 0) level = Mathf.Min(level, max);

            if (state.level == level)
                return;

            state.level = level;
            _statesById[upgradeId] = state;

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] SetLevel id='{upgradeId}' -> {level}");

            if (raiseEvent)
                OnLevelChanged?.Invoke(upgradeId, level);

            SaveToPlayerPrefs();
        }

        // =========================================================
        // Definitions / States
        // =========================================================

        private void BuildDefinitionLookup()
        {
            _defById.Clear();

            if (definitions == null)
                return;

            for (int i = 0; i < definitions.Count; i++)
            {
                var def = definitions[i];
                if (def == null) continue;

                string id = ReadDefId(def);
                if (string.IsNullOrWhiteSpace(id)) continue;

                id = id.Trim();

                if (_defById.ContainsKey(id))
                {
                    if (_logUpgrade)
                        Debug.LogWarning($"[UpgradeManager] Duplicate definition id='{id}'. Later one will be ignored.");
                    continue;
                }

                _defById.Add(id, def);
            }

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] BuildDefinitionLookup defs={_defById.Count}");
        }

        private void InitializeStatesFromInspector()
        {
            _statesById.Clear();

            if (initialStates != null)
            {
                for (int i = 0; i < initialStates.Count; i++)
                {
                    var s = initialStates[i];
                    if (string.IsNullOrWhiteSpace(s.id)) continue;

                    string id = s.id.Trim();
                    int lv = Mathf.Max(0, s.level);

                    _statesById[id] = new UpgradeState(lv);
                }
            }

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] InitializeStatesFromInspector done. states={_statesById.Count}");
        }

        private void EnsureDefinitionStates()
        {
            if (_defById.Count == 0)
                BuildDefinitionLookup();

            foreach (var kv in _defById)
            {
                string id = kv.Key;
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (!_statesById.ContainsKey(id))
                    _statesById.Add(id, new UpgradeState(0));
            }

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] EnsureDefinitionStates done. states={_statesById.Count}, defs={_defById.Count}");
        }

        // =========================================================
        // Save / Load
        // =========================================================

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
                if (data == null || data.entries == null || data.entries.Length == 0)
                    return false;

                _statesById.Clear();

                for (int i = 0; i < data.entries.Length; i++)
                {
                    var e = data.entries[i];
                    if (string.IsNullOrWhiteSpace(e.id))
                        continue;

                    string id = e.id.Trim();
                    int lv = Mathf.Max(0, e.level);

                    _statesById[id] = new UpgradeState(lv);
                }

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

        public void SaveToPlayerPrefs()
        {
            // 기존 정책 유지: 0레벨은 저장 스킵
            var list = new List<Entry>(_statesById.Count);

            foreach (var kv in _statesById)
            {
                string id = kv.Key;
                int lv = kv.Value.level;

                if (lv <= 0)
                    continue;

                list.Add(new Entry { id = id, level = lv });
            }

            var data = new SaveData { entries = list.ToArray() };
            string json = JsonUtility.ToJson(data);

            PlayerPrefs.SetString(PlayerPrefsKey, json);
            PlayerPrefs.Save();

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] 업그레이드 상태 저장 완료: {list.Count}개");
        }

        [ContextMenu("Reset All Upgrades")]
        public void ResetAllUpgrades()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);

            _statesById.Clear();
            InitializeStatesFromInspector();
            EnsureDefinitionStates();

            if (_logUpgrade)
                Debug.Log("[UpgradeManager] ResetAllUpgrades done.");
        }

        // =========================================================
        // Reflection helpers (UpgradeDefinition 구현 차이 흡수)
        // =========================================================

        private static readonly string[] DefIdCandidates =
        {
            "Id", "id",
            "UpgradeId", "upgradeId",
            "ID", "UpgradeID"
        };

        private static readonly string[] DefMaxCandidates =
        {
            "MaxLevel", "maxLevel",
            "Max", "max",
        };

        private static string ReadDefId(UpgradeDefinition def)
        {
            if (def == null) return null;

            var t = def.GetType();
            for (int i = 0; i < DefIdCandidates.Length; i++)
            {
                var name = DefIdCandidates[i];

                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string))
                {
                    try { return p.GetValue(def) as string; }
                    catch { }
                }

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(string))
                {
                    try { return f.GetValue(def) as string; }
                    catch { }
                }
            }

            return null;
        }

        private static int ReadDefMaxLevel(UpgradeDefinition def)
        {
            if (def == null) return 0;

            var t = def.GetType();
            for (int i = 0; i < DefMaxCandidates.Length; i++)
            {
                var name = DefMaxCandidates[i];

                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int))
                {
                    try { return (int)p.GetValue(def); }
                    catch { }
                }

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    try { return (int)f.GetValue(def); }
                    catch { }
                }
            }

            // maxLevel이 정의에 없으면 제한 없음 취급
            return 999;
        }

        private static string ReadStringMember(object obj, string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];

                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string))
                {
                    try { return p.GetValue(obj) as string; } catch { }
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(string))
                {
                    try { return f.GetValue(obj) as string; } catch { }
                }
            }

            return null;
        }

        private static List<string> ReadStringListMember(object obj, string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];

                // string[]
                var fArr = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fArr != null && fArr.FieldType == typeof(string[]))
                {
                    try
                    {
                        var arr = (string[])fArr.GetValue(obj);
                        return arr != null ? new List<string>(arr) : null;
                    }
                    catch { }
                }

                var pArr = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pArr != null && pArr.PropertyType == typeof(string[]))
                {
                    try
                    {
                        var arr = (string[])pArr.GetValue(obj);
                        return arr != null ? new List<string>(arr) : null;
                    }
                    catch { }
                }

                // List<string>
                if (fArr != null && typeof(IList<string>).IsAssignableFrom(fArr.FieldType))
                {
                    try
                    {
                        var list = fArr.GetValue(obj) as IList<string>;
                        return list != null ? new List<string>(list) : null;
                    }
                    catch { }
                }

                if (pArr != null && typeof(IList<string>).IsAssignableFrom(pArr.PropertyType))
                {
                    try
                    {
                        var list = pArr.GetValue(obj) as IList<string>;
                        return list != null ? new List<string>(list) : null;
                    }
                    catch { }
                }
            }

            return null;
        }

        private static List<object> ReadObjectListMember(object obj, string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    try
                    {
                        var v = f.GetValue(obj);
                        var list = ConvertToObjectList(v);
                        if (list != null) return list;
                    }
                    catch { }
                }

                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    try
                    {
                        var v = p.GetValue(obj);
                        var list = ConvertToObjectList(v);
                        if (list != null) return list;
                    }
                    catch { }
                }
            }

            return null;
        }

        private static List<object> ConvertToObjectList(object v)
        {
            if (v == null) return null;

            // 배열
            if (v is Array arr)
            {
                var list = new List<object>(arr.Length);
                foreach (var e in arr) list.Add(e);
                return list;
            }

            // IList
            if (v is System.Collections.IList ilist)
            {
                var list = new List<object>(ilist.Count);
                foreach (var e in ilist) list.Add(e);
                return list;
            }

            return null;
        }

        private static string ReadStringFromAny(object obj, string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];

                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(string))
                {
                    try { return p.GetValue(obj) as string; } catch { }
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(string))
                {
                    try { return f.GetValue(obj) as string; } catch { }
                }
            }

            return null;
        }

        private static int ReadIntFromAny(object obj, string[] names, int defaultValue)
        {
            if (obj == null) return defaultValue;
            var t = obj.GetType();

            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];

                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(int))
                {
                    try { return (int)p.GetValue(obj); } catch { }
                }

                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(int))
                {
                    try { return (int)f.GetValue(obj); } catch { }
                }
            }

            return defaultValue;
        }
    }
}
