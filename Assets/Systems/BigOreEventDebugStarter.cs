using UnityEngine;
using UnityEngine.InputSystem;

namespace Pulseforge.Systems
{
    /// <summary>
    /// BigOreEventController를 씬에서 빠르게 테스트하기 위한 디버그 스타터.
    /// (Input System Package 사용)
    ///
    /// F8  : 이벤트 시작/실패 종료 토글
    /// F9  : Perfect 히트(시간 증가 테스트)
    /// F10 : Miss 히트(패널티 테스트)
    /// </summary>
    public class BigOreEventDebugStarter : MonoBehaviour
    {
        public BigOreEventController controller;

        void Update()
        {
            if (controller == null) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f8Key.wasPressedThisFrame)
            {
                Debug.Log($"[BigOreEventDebug] F8 pressed. IsActive(before)={controller.IsActive}");

                if (!controller.IsActive)
                {
                    controller.BeginEvent();
                    Debug.Log($"[BigOreEventDebug] BeginEvent() called. IsActive(after)={controller.IsActive}");
                }
                else
                {
                    controller.EndFail();
                    Debug.Log($"[BigOreEventDebug] EndFail() called. IsActive(after)={controller.IsActive}");
                }
            }

            if (kb.f9Key.wasPressedThisFrame)
            {
                Debug.Log($"[BigOreEventDebug] F9 pressed. Calling ReportHit(Perfect). IsActive={controller.IsActive}");
                controller.ReportHit(BigOreEventController.HitResult.Perfect);
            }

            if (kb.f10Key.wasPressedThisFrame)
            {
                Debug.Log($"[BigOreEventDebug] F10 pressed. Calling ReportHit(Miss). IsActive={controller.IsActive}");
                controller.ReportHit(BigOreEventController.HitResult.Miss);
            }
        }
    }
}
