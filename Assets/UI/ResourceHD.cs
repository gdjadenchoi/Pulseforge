// Assets/Systems/ResourceHUD.cs
using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using Pulseforge.Systems;

/// <summary>
/// 상단 TopBar에서 자원(Crystal, Gold, Shard 등)을 표시하는 HUD.
/// - 단일 타입 또는 전체 타입을 한 줄에 표시 가능
/// - 현재는 주기적 폴링 방식으로 RewardManager 값을 읽어온다.
/// </summary>
public class ResourceHUD : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private TMP_Text resourceText;
    [SerializeField] private RewardManager rewardManager;

    [Header("Display Mode")]
    [Tooltip("true면 모든 리소스를 한 줄에 표시, false면 특정 타입만 표시")]
    [SerializeField] private bool showAllTypes = false;

    [Tooltip("단일 표시 모드일 때 사용할 리소스 타입")]
    [SerializeField] private RewardType rewardType = RewardType.Crystal;

    [Header("Formats")]
    [Tooltip("단일 리소스 표시 포맷. {label} 과 {value}를 사용할 수 있음")]
    [SerializeField] private string singleFormat = "{label} {value}";

    [Tooltip("모든 리소스 표시 시, 항목 사이에 넣을 구분자")]
    [SerializeField] private string allJoinSeparator = "  |  ";

    [Tooltip("라벨 텍스트를 강제로 지정하고 싶으면 입력 (비우면 Enum 이름 사용)")]
    [SerializeField] private string labelOverride = "";

    [Header("Refresh")]
    [Tooltip("UI 갱신 주기(초). 이벤트 기반이 아니라 폴링으로 갱신할 때 사용")]
    [SerializeField] private float refreshInterval = 0.1f;

    private Coroutine refreshRoutine;

    private void Awake()
    {
        if (!resourceText)
            resourceText = GetComponentInChildren<TMP_Text>();

        // 🔧 기존: FindAnyObjectByType 로 검색
        // → 수정: 싱글톤(SafeInstance / Instance)에서 직접 가져오기
        EnsureRewardManagerReference();
    }

    private void OnEnable()
    {
        // 혹시 씬 전환 중에 참조가 끊겼을 수 있으니 한 번 더 시도
        EnsureRewardManagerReference();

        // 폴링 루프 시작
        refreshRoutine = StartCoroutine(RefreshLoop());
    }

    private void OnDisable()
    {
        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);
    }

    private IEnumerator RefreshLoop()
    {
        var wait = new WaitForSeconds(refreshInterval);
        while (true)
        {
            RefreshImmediate();
            yield return wait;
        }
    }

    private void RefreshImmediate()
    {
        if (!resourceText)
            return;

        // 매 프레임은 아니지만, 갱신 시점마다 RewardManager를 한 번 더 확인
        if (!EnsureRewardManagerReference())
            return;

        if (showAllTypes)
        {
            // 모든 RewardType을 나열해서 한 줄로 붙인다.
            var items = Enum.GetValues(typeof(RewardType))
                .Cast<RewardType>()
                .Select(rt => $"{GetLabel(rt)} {SafeGet(rt)}");

            resourceText.text = string.Join(allJoinSeparator, items);
        }
        else
        {
            // 단일 타입 표시
            string label = string.IsNullOrWhiteSpace(labelOverride)
                ? GetLabel(rewardType)
                : labelOverride;

            resourceText.text = singleFormat
                .Replace("{label}", label)
                .Replace("{value}", SafeGet(rewardType).ToString());
        }
    }

    /// <summary>
    /// RewardManager 싱글톤에서 참조를 확보한다.
    /// - 이미 있으면 그대로 사용
    /// - 없으면 SafeInstance → Instance 순으로 시도
    /// </summary>
    private bool EnsureRewardManagerReference()
    {
        if (rewardManager != null)
            return true;

        // SafeInstance 가 있으면 우선 사용, 없으면 Instance 시도
        rewardManager = RewardManager.SafeInstance ?? RewardManager.Instance;

        return rewardManager != null;
    }

    private int SafeGet(RewardType rt)
    {
        try
        {
            return rewardManager != null ? rewardManager.Get(rt) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private string GetLabel(RewardType rt)
    {
        // 필요하면 여기서 한글 라벨로 바꿔도 됨.
        return rt switch
        {
            RewardType.Crystal => "Crystal",
            RewardType.Gold    => "Gold",
            RewardType.Shard   => "Shard",
            _                  => rt.ToString()
        };
    }
}
