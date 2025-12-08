using UnityEngine;
using TMPro;
using Pulseforge.Systems;

/// <summary>
/// 세션 남은 시간을 표시하는 HUD.
/// - 기본은 흰색 숫자
/// - SessionController에서 criticalThreshold 이하로 들어가면 빨간색/깜빡임 처리
/// </summary>
public class TimeHUD : MonoBehaviour
{
    [Header("References")]
    public TMP_Text timeText;
    public SessionController session;

    [Header("Visuals")]
    public Color normalColor = Color.white;
    public Color criticalColor = new Color(1f, 0.3f, 0.3f, 1f);
    public bool blinkOnCritical = true;
    public float blinkSpeed = 6f;
    public string timeFormat = "{0:0.0}s";

    private bool _isCritical;
    private float _blinkTimer;

    private void Start()
    {
        if (session == null)
            session = FindObjectOfType<SessionController>();

        if (session != null)
        {
            // SessionController 이벤트 구독
            session.OnTimeChanged   += HandleTimeChanged;
            session.OnCritical      += HandleCritical;
            session.OnSessionStart  += HandleSessionStart;
            session.OnSessionEnd    += HandleSessionEnd;

            // 시작 시 한 번 초기 표시
            UpdateDisplay(session.Remaining);
        }
        else
        {
            UpdateDisplay(0f);
        }
    }

    private void OnDestroy()
    {
        if (session != null)
        {
            session.OnTimeChanged   -= HandleTimeChanged;
            session.OnCritical      -= HandleCritical;
            session.OnSessionStart  -= HandleSessionStart;
            session.OnSessionEnd    -= HandleSessionEnd;
        }
    }

    private void Update()
    {
        // 크리티컬 구간에서 깜빡임 처리
        if (_isCritical && blinkOnCritical && timeText != null)
        {
            _blinkTimer += Time.deltaTime * blinkSpeed;
            float t = (Mathf.Sin(_blinkTimer) * 0.5f) + 0.5f;
            timeText.color = Color.Lerp(criticalColor * 0.3f, criticalColor, t);
        }
    }

    private void HandleSessionStart()
    {
        _isCritical = false;
        _blinkTimer = 0;
        UpdateDisplay(session.Remaining);

        if (timeText != null)
            timeText.color = normalColor;

        gameObject.SetActive(true);
    }

    private void HandleSessionEnd()
    {
        // 세션 종료 시에는 타이머를 숨겨준다.
        gameObject.SetActive(false);
    }

    // SessionController.OnTimeChanged(remaining, normalized) 대응
    private void HandleTimeChanged(float remaining, float normalized)
    {
        UpdateDisplay(remaining);
    }

    private void HandleCritical(float remaining)
    {
        _isCritical = true;
        _blinkTimer = 0;

        if (!blinkOnCritical && timeText != null)
            timeText.color = criticalColor;
    }

    private void UpdateDisplay(float remaining)
    {
        if (timeText == null)
            return;

        timeText.text = string.Format(timeFormat, remaining);

        if (!_isCritical)
            timeText.color = normalColor;
    }
}
