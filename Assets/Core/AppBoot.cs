using UnityEngine;

namespace Pulseforge.Core
{
    public class AppBoot : MonoBehaviour
    {
        [Header("환경 설정")]
        [SerializeField] int targetFps = 120;
        [SerializeField] bool disableVSync = true;
        [SerializeField] bool runInBackground = true;

        void Awake()
        {
            if (disableVSync) QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps;
            Application.runInBackground = runInBackground;

#if UNITY_ANDROID
            // 세로 고정(프로젝트 설정이 세로여도 확실히 못 박아둠)
            Screen.orientation = ScreenOrientation.Portrait;
            // 화면 꺼짐 방지
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
#endif
        }
    }
}
