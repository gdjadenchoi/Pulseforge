using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 전체를 관리하는 매니저.
    /// - 현재는 "레벨만" 관리하고, 비용/조건은 이후 단계에서 붙일 예정.
    /// - DontDestroyOnLoad 싱글톤.
    /// - 키는 string upgradeId 로 관리한다. (예: "OreAmount", "CursorDamage")
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
        [Tooltip("UpgradeDefinition SO 들을 등록해두면, 이름/설명/최대 레벨/비용 곡선 등의 메타데이터로 사용한다.")]
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
            InitializeStatesFromInspector();
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

            // 정의만 있고 초기 레벨이 없는 항목도 0레벨로 등록
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
        //  업그레이드 처리 (현재는 비용 없이 단순 레벨 +1)
        //==============================================================

        /// <summary>
        /// 업그레이드를 시도한다.
        /// - 아직 최대 레벨이 아니면 레벨을 1 올리고 true 반환.
        /// - 이미 최대 레벨이면 false 반환.
        /// - 이후 비용/조건 로직은 추후 단계에서 추가 예정.
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

            state.level++;

            if (_logUpgrade)
                Debug.Log($"[UpgradeManager] '{upgradeId}' 업그레이드 → Lv.{state.level}");

            OnLevelChanged?.Invoke(upgradeId, state.level);
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
