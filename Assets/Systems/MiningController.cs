using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Pulseforge.Core;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 비트 기준 입력 판정 컨트롤러.
    /// - 탭 입력 시: 가장 가까운 비트와 시간차로 Perfect/Good/Miss 판정
    /// - (옵션) 해당 비트 창 내에 "입력이 전혀 없으면" 자동 Miss
    /// </summary>
    public class MiningController : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private RhythmConductor conductor;

        [Header("판정 윈도우(초)")]
        [Tooltip("Perfect 판정 오차 한계(초)")]
        [SerializeField] private float perfectWindow = 0.10f;
        [Tooltip("Good 판정 오차 한계(초). 이 값을 넘으면 Miss")]
        [SerializeField] private float goodWindow = 0.25f;

        [Header("미입력 Miss 옵션")]
        [Tooltip("각 비트마다 goodWindow 동안 입력이 없으면 Miss 처리")]
        [SerializeField] private bool missOnNoInput = true;

        [Header("이벤트 (필요시 인스펙터에서 후킹)")]
        public UnityEvent OnPerfect;
        public UnityEvent OnGood;
        public UnityEvent OnMiss;

        public enum Judgement { Perfect, Good, Miss }

        // 비트 인덱스 트래킹
        private int beatIndex = 0;
        private int lastSatisfiedBeatIndex = -9999; // 이 비트에는 유효 입력이 있었다

        void OnEnable()
        {
            if (conductor != null)
                conductor.OnBeat.AddListener(HandleBeat);
        }

        void OnDisable()
        {
            if (conductor != null)
                conductor.OnBeat.RemoveListener(HandleBeat);
        }

        /// <summary>PlayerInput의 Gameplay/Tap에 연결</summary>
        public void OnTap(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed || conductor == null) return;

            float now = Time.time;
            float offset = conductor.GetOffsetToNearestBeat(now);

            Judgement j;
            if (offset <= perfectWindow) j = Judgement.Perfect;
            else if (offset <= goodWindow) j = Judgement.Good;
            else j = Judgement.Miss;

            // 현재 비트를 만족 처리(Perfect/Good일 때)
            if (j != Judgement.Miss)
                lastSatisfiedBeatIndex = beatIndex;

            FireJudgement(j, offset);
        }

        private void HandleBeat()
        {
            beatIndex++;

            if (!missOnNoInput) return;

            // goodWindow 뒤에 비트가 만족되었는지 체크 → 아니면 Miss
            // 코루틴 대신 간단한 지연 호출
            Invoke(nameof(CheckNoInputMiss), goodWindow);
        }

        private void CheckNoInputMiss()
        {
            // 이 호출 시점의 beatIndex는 이미 다음 비트로 진행되었을 수 있으므로
            // "직전 비트"가 만족됐는지 확인한다.
            int justPassedBeat = beatIndex;
            if (lastSatisfiedBeatIndex < justPassedBeat)
            {
                // 직전 비트가 만족되지 않았음 → Miss
                FireJudgement(Judgement.Miss, offsetSeconds: goodWindow + 0.0001f);
            }
        }

        private void FireJudgement(Judgement j, float offsetSeconds)
        {
            switch (j)
            {
                case Judgement.Perfect:
                    Debug.Log($"Perfect! offset={offsetSeconds:0.000}s");
                    OnPerfect?.Invoke();
                    break;
                case Judgement.Good:
                    Debug.Log($"Good! offset={offsetSeconds:0.000}s");
                    OnGood?.Invoke();
                    break;
                default:
                    Debug.Log($"Miss offset={offsetSeconds:0.000}s");
                    OnMiss?.Invoke();
                    break;
            }
        }

        // 인스펙터 빠른 튜닝용
        public void SetWindows(float perfect, float good)
        {
            perfectWindow = Mathf.Max(0f, perfect);
            goodWindow = Mathf.Max(perfectWindow, good);
        }
    }
}
