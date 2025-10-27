using UnityEngine;
using System.Collections.Generic;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 런타임 디버깅을 위한 헬퍼 클래스
    /// </summary>
    public class DebugHelper : MonoBehaviour
    {
        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private float debugInterval = 0.5f;
        
        private float nextDebugTime;
        private List<Ore> trackedOres = new List<Ore>();
        
        void Start()
        {
            nextDebugTime = Time.time + debugInterval;
        }
        
        void Update()
        {
            if (!enableDebugLogs) return;
            
            if (Time.time >= nextDebugTime)
            {
                nextDebugTime = Time.time + debugInterval;
                LogDebugInfo();
            }
        }
        
        void LogDebugInfo()
        {
            // RewardManager 인스턴스 수 확인
            var rewardManagers = FindObjectsByType<RewardManager>(FindObjectsSortMode.None);
            Debug.Log($"[DebugHelper] RewardManager 인스턴스 수: {rewardManagers.Length}");
            foreach (var rm in rewardManagers)
            {
                Debug.Log($"[DebugHelper] RewardManager 경로: {GetFullPath(rm.transform)}");
            }
            
            // Ore 개수 확인
            var ores = FindObjectsByType<Ore>(FindObjectsSortMode.None);
            Debug.Log($"[DebugHelper] 화면 내 Ore 개수: {ores.Length}");
            
            // 첫 번째 Ore 상세 정보
            if (ores.Length > 0)
            {
                var ore = ores[0];
                Debug.Log($"[DebugHelper] 첫 번째 Ore HP: {ore.GetComponent<Ore>()?.GetType().GetField("_hp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(ore) ?? "N/A"}");
                
                // HP바 존재 여부 확인
                var hpBar = ore.transform.Find("HPBar");
                if (hpBar != null)
                {
                    var back = hpBar.Find("Back");
                    var fill = hpBar.Find("Fill");
                    Debug.Log($"[DebugHelper] HP바 존재: Back={back != null}, Fill={fill != null}");
                    
                    if (back != null)
                    {
                        var backSr = back.GetComponent<SpriteRenderer>();
                        Debug.Log($"[DebugHelper] HPBar Back sortingOrder: {backSr.sortingOrder}");
                    }
                    if (fill != null)
                    {
                        var fillSr = fill.GetComponent<SpriteRenderer>();
                        Debug.Log($"[DebugHelper] HPBar Fill sortingOrder: {fillSr.sortingOrder}");
                        Debug.Log($"[DebugHelper] HPBar Fill localScale.x: {fill.localScale.x}");
                    }
                }
                else
                {
                    Debug.LogWarning("[DebugHelper] HP바가 존재하지 않습니다!");
                }
            }
            
            // DrillCursor 정보
            var drillCursor = FindFirstObjectByType<DrillCursor>();
            if (drillCursor != null)
            {
                var rb = drillCursor.GetComponent<Rigidbody2D>();
                var col = drillCursor.GetComponent<Collider2D>();
                Debug.Log($"[DebugHelper] DrillCursor - bodyType: {rb.bodyType}, isTrigger: {col.isTrigger}");
            }
            
            // Project Settings 확인
            Debug.Log($"[DebugHelper] Fixed Timestep: {Time.fixedDeltaTime}");
        }
        
        string GetFullPath(Transform transform)
        {
            if (transform.parent == null)
                return transform.name;
            return GetFullPath(transform.parent) + "/" + transform.name;
        }
        
        // 커서가 Ore 위에 있을 때 HP 변화 추적
        void OnTriggerStay2D(Collider2D other)
        {
            if (!enableDebugLogs) return;
            
            if (other.TryGetComponent<Ore>(out var ore))
            {
                // HP 값은 private이므로 리플렉션으로 접근
                var hpField = typeof(Ore).GetField("_hp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var hp = (float)(hpField?.GetValue(ore) ?? 0f);
                
                var maxHpField = typeof(Ore).GetField("maxHP", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var maxHp = (float)(maxHpField?.GetValue(ore) ?? 10f);
                
                float ratio = hp / maxHp;
                Debug.Log($"[DebugHelper] Ore HP: {hp:F2}/{maxHp:F2} (비율: {ratio:P1})");
            }
        }
    }
}
