using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Pulseforge.Systems;

namespace Pulseforge.UI
{
    /// <summary>
    /// 상단 바에 자원 보유량을 출력.
    /// - crystalLabel: 크리스털 단일 전용(기존 호환)
    /// - bindings: 리워드 타입별로 Text를 매핑(다중 리소스)
    /// </summary>
    public class ResourceHUD : MonoBehaviour
    {
        [Header("Legacy (단일 표시)")]
        [SerializeField] private TMP_Text crystalLabel;

        [Serializable]
        public struct LabelBinding
        {
            public RewardType type;
            public TMP_Text label;
            public string format; // 예: "{0:n0}" 또는 "x{0}"
        }

        [Header("Multi Labels")]
        [SerializeField] private List<LabelBinding> bindings = new();

        Dictionary<RewardType, LabelBinding> _map;

        void OnEnable()
        {
            if (RewardManager.Instance != null)
                RewardManager.Instance.OnResourceChanged += HandleChanged;
        }

        void OnDisable()
        {
            if (RewardManager.Instance != null)
                RewardManager.Instance.OnResourceChanged -= HandleChanged;
        }

        void Start()
        {
            // 바인딩 맵 구성
            _map = new Dictionary<RewardType, LabelBinding>();
            foreach (var b in bindings)
                _map[b.type] = b;

            // 초기값 일괄 반영
            if (RewardManager.Instance != null)
            {
                var all = RewardManager.Instance.GetAll();
                foreach (var kv in all)
                    Apply(kv.Key, kv.Value);
            }
        }

        void HandleChanged(RewardType type, int newValue)
        {
            Apply(type, newValue);
        }

        void Apply(RewardType type, int value)
        {
            if (_map != null && _map.TryGetValue(type, out var b) && b.label != null)
            {
                var fmt = string.IsNullOrEmpty(b.format) ? "{0}" : b.format;
                b.label.text = string.Format(fmt, value);
            }

            // 레거시 호환: 크리스털 라벨이 있으면 같이 갱신
            if (type == RewardType.Crystal && crystalLabel != null)
                crystalLabel.text = value.ToString();
        }

        // 외부에서 직접 호출(레거시)
        public void UpdateCrystalText(int value)
        {
            if (crystalLabel) crystalLabel.text = value.ToString();
        }
    }
}
