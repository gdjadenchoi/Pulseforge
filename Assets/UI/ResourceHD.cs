// Assets/Systems/ResourceHUD.cs
using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using Pulseforge.Systems;
// 또는 RewardType.cs / RewardManager.cs 맨 위에 적힌 네임스페이스 그대로


public class ResourceHUD : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private TMP_Text resourceText;
    [SerializeField] private RewardManager rewardManager;

    [Header("Display Mode")]
    [SerializeField] private bool showAllTypes = false; // true면 모든 리소스를 한 줄에 표시
    [SerializeField] private RewardType rewardType = RewardType.Crystal; // 단일 표시 모드일 때 사용

    [Header("Formats")]
    [Tooltip("단일 리소스 표시 포맷. {label} 과 {value}를 사용할 수 있음")]
    [SerializeField] private string singleFormat = "{label} {value}";
    [Tooltip("모든 리소스 표시 시 항목 사이 구분자")]
    [SerializeField] private string allJoinSeparator = "  |  ";

    [Tooltip("라벨을 강제로 지정하고 싶으면 입력(비우면 Enum 이름 사용)")]
    [SerializeField] private string labelOverride = "";

    [Header("Refresh")]
    [Tooltip("UI 갱신 주기(초). 값 변경 이벤트가 없다면 폴링로 갱신")]
    [SerializeField] private float refreshInterval = 0.1f;

    Coroutine refreshRoutine;

    private void Awake()
    {
        if (!resourceText) resourceText = GetComponentInChildren<TMP_Text>();
        if (!rewardManager) rewardManager = FindAnyObjectByType<RewardManager>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        // 폴링 루프
        refreshRoutine = StartCoroutine(RefreshLoop());
        // RewardManager에 UnityEvent가 있다면 여기에 연결해도 됨:
        // rewardManager.OnChanged.AddListener((type, value) => RefreshImmediate());
    }

    private void OnDisable()
    {
        if (refreshRoutine != null) StopCoroutine(refreshRoutine);
        // rewardManager?.OnChanged.RemoveListener(...);
    }

    IEnumerator RefreshLoop()
    {
        var wait = new WaitForSeconds(refreshInterval);
        while (true)
        {
            RefreshImmediate();
            yield return wait;
        }
    }

    void RefreshImmediate()
    {
        if (!resourceText || !rewardManager) return;

        if (showAllTypes)
        {
            // 모든 RewardType을 나열
            var items = Enum.GetValues(typeof(RewardType))
                .Cast<RewardType>()
                .Select(rt => $"{GetLabel(rt)} {SafeGet(rt)}");
            resourceText.text = string.Join(allJoinSeparator, items);
        }
        else
        {
            // 단일 타입
            string label = string.IsNullOrWhiteSpace(labelOverride) ? GetLabel(rewardType) : labelOverride;
            resourceText.text = singleFormat
                .Replace("{label}", label)
                .Replace("{value}", SafeGet(rewardType).ToString());
        }
    }

    int SafeGet(RewardType rt)
    {
        try { return rewardManager.Get(rt); }
        catch { return 0; }
    }

    string GetLabel(RewardType rt)
    {
        // Enum 이름을 그대로 쓰되, 필요 시 간단히 커스텀
        return rt switch
        {
            RewardType.Crystal => "Crystal",
            RewardType.Gold => "Gold",
            RewardType.Shard => "Shard",
            _ => rt.ToString()
        };
    }
}
