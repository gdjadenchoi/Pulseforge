// Assets/UI/ResourceHUD.cs
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using Pulseforge.Systems; // RewardType, RewardManager

namespace Pulseforge.UI
{
    /// <summary>
    /// 상단 리소스 바 표시.
    /// - Legacy(단일 텍스트) 모드: "Crystal 10 | Gold 2 | Shard 7" 형태로 1줄 표시
    /// - Multi 라벨 모드: 타입별 TMP 라벨 각각 갱신
    /// - 라벨 바인딩이 비어 있으면 자식에서 이름 기반으로 자동 바인딩 시도
    ///   (예: "Crystal", "Gold", "Shard" 라는 이름/부분이 들어간 TMP 오브젝트)
    /// </summary>
    public class ResourceHUD : MonoBehaviour
    {
        [Header("Legacy (한줄 표기)")]
        [SerializeField] private bool legacySingleText = true;
        [SerializeField] private TMP_Text legacyLabel; // 한 줄 라벨

        [Header("Multi Labels")]
        [SerializeField] private List<Binding> bindings = new(); // 타입별 개별 라벨

        [Serializable]
        public struct Binding
        {
            public RewardType type;
            public TMP_Text label;
        }

        private RewardManager rm;

        void OnEnable()
        {
            HookupManager();
            AutoBindIfNeeded();
            FullRefresh();
        }

        void OnDisable()
        {
            if (rm != null)
            {
                rm.OnResourceChanged -= HandleChanged;
            }
        }

        // --- Manager 연결 & 이벤트 구독 ---
        private void HookupManager()
        {
            rm = RewardManager.SafeInstance;
            if (rm == null)
            {
                // 씬 순서상 늦게 뜨는 경우 대비, 한 번 더 시도
                rm = FindFirstObjectByType<RewardManager>(FindObjectsInactive.Include);
            }

            if (rm != null)
            {
                rm.OnResourceChanged -= HandleChanged; // 중복구독 방지
                rm.OnResourceChanged += HandleChanged;
            }
            else
            {
                Debug.LogWarning("[ResourceHUD] RewardManager를 찾지 못했습니다.");
            }
        }

        // --- 바인딩 없으면 자동 찾기 (이름 기반) ---
        private void AutoBindIfNeeded()
        {
            if (!legacySingleText && bindings != null && bindings.Count > 0) return;

            // 한 줄 라벨 자동 탐색
            if (legacySingleText && legacyLabel == null)
            {
                legacyLabel = GetComponentInChildren<TMP_Text>(true);
            }

            // 멀티 라벨 자동 탐색
            if (!legacySingleText && (bindings == null || bindings.Count == 0))
            {
                bindings = new List<Binding>();
                var texts = GetComponentsInChildren<TMP_Text>(true);
                foreach (RewardType t in Enum.GetValues(typeof(RewardType)))
                {
                    TMP_Text best = null;
                    string key = t.ToString(); // "Crystal" 등

                    foreach (var tx in texts)
                    {
                        if (tx == null) continue;
                        var n = tx.gameObject.name;
                        if (n.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            best = tx; break;
                        }
                    }

                    if (best != null)
                    {
                        bindings.Add(new Binding { type = t, label = best });
                    }
                }
            }
        }

        // --- 이벤트 콜백 ---
        private void HandleChanged(RewardType type, int amount)
        {
            // 단일/멀티 모두 갱신
            UpdateLegacy();
            UpdateBindings(type);
        }

        // --- 전체 갱신 (초기화/풀 리프레시) ---
        public void FullRefresh()
        {
            UpdateLegacy();
            if (!legacySingleText)
            {
                foreach (var b in bindings)
                    UpdateOne(b.type);
            }
        }

        // --- Legacy 한 줄 표기 ---
        private void UpdateLegacy()
        {
            if (!legacySingleText || legacyLabel == null || rm == null) return;

            var sb = new StringBuilder(64);
            bool first = true;
            foreach (RewardType t in Enum.GetValues(typeof(RewardType)))
            {
                if (!first) sb.Append(" | ");
                first = false;
                sb.Append(t.Short()).Append(' ').Append(rm.Get(t));
            }
            legacyLabel.text = sb.ToString();
        }

        // --- Multi 라벨 개별 갱신 ---
        private void UpdateBindings(RewardType changed)
        {
            if (legacySingleText || rm == null || bindings == null) return;

            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].type == changed)
                {
                    UpdateOne(changed);
                    break;
                }
            }
        }

        private void UpdateOne(RewardType t)
        {
            if (bindings == null || rm == null) return;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].type != t) continue;
                var tx = bindings[i].label;
                if (tx != null) tx.text = $"{t.Short()} {rm.Get(t)}";
            }
        }
    }
}
