using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 아웃포스트(베이스 캠프) 씬 전체를 관리하는 컨트롤러.
/// 현재 역할:
/// - Start Mining 버튼을 눌렀을 때 채굴 씬(PF_Mining)으로 이동.
/// 나중에:
/// - 업그레이드 버튼, 패널, 연출 등도 여기서 관리.
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
        // 버튼이 씬에 없더라도 에러 안 나게 방어 코드
        if (startMiningButton != null)
        {
            startMiningButton.onClick.AddListener(OnClickStartMining);
        }
        else
        {
            Debug.LogWarning("[OutpostController] startMiningButton이 지정되지 않았습니다.");
        }
    }

    private void OnDestroy()
    {
        // 리스너 정리 (에디터에서 재생/정지 반복 시 중복 방지용)
        if (startMiningButton != null)
        {
            startMiningButton.onClick.RemoveListener(OnClickStartMining);
        }
    }

    /// <summary>
    /// Start Mining 버튼 클릭 시 호출.
    /// </summary>
    private void OnClickStartMining()
    {
        if (string.IsNullOrEmpty(miningSceneName))
        {
            Debug.LogError("[OutpostController] miningSceneName이 비어 있습니다.");
            return;
        }

        Debug.Log($"[OutpostController] Loading mining scene: {miningSceneName}");
        SceneManager.LoadScene(miningSceneName);
    }
}
