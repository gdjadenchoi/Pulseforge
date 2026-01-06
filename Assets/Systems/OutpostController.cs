using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 아웃포스트(베이스 캠프) 씬 전체를 관리하는 컨트롤러.
/// - Start Mining 버튼을 눌렀을 때 채굴 씬(PF_Mining)으로 이동.
/// </summary>
public class OutpostController : MonoBehaviour
{
    [Header("Scene Names")]
    [Tooltip("채굴 씬 이름 (Build Settings에 등록된 이름과 동일해야 함)")]
    [SerializeField] private string miningSceneName = "PF_Mining";

    [Header("UI References")]
    [Tooltip("광산으로 이동하는 버튼")]
    [SerializeField] private Button startMiningButton;

    private void Awake()
    {
        if (startMiningButton != null)
            startMiningButton.onClick.AddListener(OnClickStartMining);
        else
            Debug.LogWarning("[OutpostController] startMiningButton이 지정되지 않았습니다.");
    }

    private void OnDestroy()
    {
        if (startMiningButton != null)
            startMiningButton.onClick.RemoveListener(OnClickStartMining);
    }

    private void OnClickStartMining()
    {
        if (string.IsNullOrEmpty(miningSceneName))
        {
            Debug.LogError("[OutpostController] miningSceneName이 비어 있습니다.");
            return;
        }

        // ✅ Outpost → Mining 직전: UpgradeManager 상태 확정 로그 (컴파일 타임 의존성 없음)
        LogUpgradeManagerState("MiningAreaExpansion");

        Debug.Log($"[OutpostController] Loading mining scene: {miningSceneName}");
        SceneManager.LoadScene(miningSceneName);
    }

    private void LogUpgradeManagerState(string upgradeId)
    {
        try
        {
            // 네임스페이스가 바뀌었을 수 있으니 후보를 여러 개 둔다.
            var typeCandidates = new[]
            {
                "UpgradeManager",
                "Pulseforge.UI.UpgradeManager",
                "Pulseforge.Systems.UpgradeManager",
            };

            Type umType = null;

            // 1) 먼저 Type.GetType 시도
            foreach (var name in typeCandidates)
            {
                umType = Type.GetType(name);
                if (umType != null) break;
            }

            // 2) 못 찾으면 현재 AppDomain의 모든 어셈블리에서 스캔
            if (umType == null)
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    foreach (var name in typeCandidates)
                    {
                        umType = asm.GetType(name);
                        if (umType != null) break;
                    }
                    if (umType != null) break;
                }
            }

            if (umType == null)
            {
                Debug.LogWarning("[Outpost->LoadMining] UpgradeManager TYPE not found (namespace/asmdef issue suspected).");
                return;
            }

            // static Instance 프로퍼티/필드 찾기
            object instanceObj = null;

            var instProp = umType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instProp != null)
            {
                instanceObj = instProp.GetValue(null);
            }
            else
            {
                // 혹시 Instance가 필드일 수도 있으니 방어
                var instField = umType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instField != null)
                    instanceObj = instField.GetValue(null);
            }

            if (instanceObj == null)
            {
                Debug.LogWarning($"[Outpost->LoadMining] {umType.FullName}.Instance == null");
                return;
            }

            // UnityEngine.Object 캐스팅(InstanceID 찍기용)
            var unityObj = instanceObj as UnityEngine.Object;
            int instanceId = unityObj != null ? unityObj.GetInstanceID() : -1;

            // GetLevel(string) 호출
            int level = -999;
            var getLevelMethod = umType.GetMethod("GetLevel", BindingFlags.Public | BindingFlags.Instance);
            if (getLevelMethod != null)
            {
                level = (int)getLevelMethod.Invoke(instanceObj, new object[] { upgradeId });
            }
            else
            {
                Debug.LogWarning($"[Outpost->LoadMining] {umType.FullName}.GetLevel(string) not found.");
            }

            Debug.Log($"[Outpost->LoadMining] UM_TYPE={umType.FullName} UM_ID={instanceId} {upgradeId}={level}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Outpost->LoadMining] Failed to log UpgradeManager state: {ex}");
        }
    }
}
