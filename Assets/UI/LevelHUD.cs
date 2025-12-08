using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Pulseforge.Systems;

/// <summary>
/// 레벨 / 경험치 표시 HUD
/// - LevelManager(싱글톤)를 바라보고 텍스트 + 게이지 갱신
/// - PF_Outpost, PF_Mining 양쪽에서 동일 프리팹 사용 전제
/// </summary>
public class LevelHUD : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private TMP_Text levelText;   // "Lv. 1" 텍스트
    [SerializeField] private TMP_Text expText;     // "13 / 40" 텍스트
    [SerializeField] private Image expFill;        // fillAmount 0~1

    [Header("Behavior")]
    [Tooltip("씬에 LevelManager 가 없으면 이 HUD 를 자동으로 비활성화할지 여부")]
    [SerializeField] private bool disableIfNoManager = true;

    [Header("Format")]
    [SerializeField] private string levelFormat = "Lv. {0}";
    [SerializeField] private string expFormat   = "{0} / {1}";

    private LevelManager _levelManager;

    private void OnEnable()
    {
        _levelManager = LevelManager.Instance ?? FindAnyObjectByType<LevelManager>();

        if (_levelManager == null)
        {
            if (disableIfNoManager)
                gameObject.SetActive(false);
            return;
        }

        _levelManager.OnLevelChanged += HandleLevelChanged;
        _levelManager.OnExpChanged   += HandleExpChanged;

        RefreshAll();
    }

    private void OnDisable()
    {
        if (_levelManager == null) return;

        _levelManager.OnLevelChanged -= HandleLevelChanged;
        _levelManager.OnExpChanged   -= HandleExpChanged;
    }

    private void HandleLevelChanged(int newLevel)
    {
        RefreshLevel(newLevel);
        RefreshExp(_levelManager.CurrentExp, _levelManager.ExpToNext);
    }

    private void HandleExpChanged(long current, long required)
    {
        RefreshExp(current, required);
    }

    private void RefreshAll()
    {
        if (_levelManager == null) return;

        RefreshLevel(_levelManager.Level);
        RefreshExp(_levelManager.CurrentExp, _levelManager.ExpToNext);
    }

    private void RefreshLevel(int level)
    {
        if (levelText == null) return;

        try
        {
            // 포맷이 비어있거나 {0} 이 없으면 기본 포맷 사용
            if (string.IsNullOrEmpty(levelFormat) || !levelFormat.Contains("{0}"))
            {
                levelText.text = $"Lv. {level}";
            }
            else
            {
                levelText.text = string.Format(levelFormat, level);
            }
        }
        catch (FormatException)
        {
            // 포맷 에러가 나면 안전하게 기본 포맷으로 표시
            levelText.text = $"Lv. {level}";
        }
    }

    private void RefreshExp(long current, long required)
    {
        if (expText != null)
        {
            try
            {
                if (string.IsNullOrEmpty(expFormat) ||
                    !expFormat.Contains("{0}") || !expFormat.Contains("{1}"))
                {
                    expText.text = $"{current} / {required}";
                }
                else
                {
                    expText.text = string.Format(expFormat, current, required);
                }
            }
            catch (FormatException)
            {
                expText.text = $"{current} / {required}";
            }
        }

        if (expFill != null)
        {
            float ratio = 0f;
            if (required > 0)
            {
                ratio = Mathf.Clamp01((float)current / (float)required);
            }
            expFill.fillAmount = ratio;
        }
    }
}
