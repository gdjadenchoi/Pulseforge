using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using Pulseforge.Core;

namespace Pulseforge.Systems
{
    /// <summary>
    /// N박마다 입력해야 하는 노트 판정 컨트롤러.
    /// - RhythmConductor.OnBeat()을 카운트하여 "노트 타겟 박자"에만 판정창을 연다.
    /// - 판정창(goodWindow) 내에서 탭하면 Perfect/Good/Miss 판정.
    /// - 창이 닫힐 때까지 입력이 없으면 Miss.
    /// - 창이 아닐 때 탭하면(옵션) Miss 처리.
    /// </summary>
    public class MiningController : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private RhythmConductor conductor;

        [Header("판정 창(초)")]
        [Tooltip("Perfect 판정 오차 한계(초)")]
        [SerializeField] private float perfectWindow = 0.10f;

        [Tooltip("Good 판정 오차 한계(초). 이 값을 넘으면 Miss")]
        [SerializeField] private float goodWindow = 0.25f;

        [Header("노트 주기")]
        [Tooltip("몇 박마다 한 번 노트를 칠지 (예: 2면 두 박에 한 번)")]
        [SerializeField] private int beatsPerNote = 2;

        [Tooltip("위상 오프셋(0~beatsPerNote-1). 예: 1이면 두 번째 박마다")]
        [SerializeField] private int notePhase = 0;

        [Header("기타 옵션")]
        [Tooltip("노트 창이 아닐 때 탭하면 Miss로 처리")]
        [SerializeField] private bool offbeatTapIsMiss = true;

        [Header("이벤트")]
        public UnityEvent OnPerfect;
        public UnityEvent OnGood;
        public UnityEvent OnMiss;

        public enum Judgement { Perfect, Good, Miss }

        // 내부 상태
        private int beatIndex = -1;                 // OnBeat 호출될 때 증가
        private bool windowOpen = false;            // 현재 노트 판정창 열림 여부
        private bool satisfiedThisNote = false;     // 해당 노트에 유효 입력이 있었는지
        private float currentNoteCenterTime = 0f;   // 이 노트의 기준 시각(Time.time)

        private void OnEnable()
        {
            if (conductor != null)
                conductor.OnBeat.AddListener(HandleBeat);
        }

        private void OnDisable()
        {
            if (conductor != null)
                conductor.OnBeat.RemoveListener(HandleBeat);
        }

        /// <summary>PlayerInput의 Gameplay/Tap에 연결.</summary>
        public void OnTap(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;

            float now = Time.time;

            if (windowOpen)
            {
                float offset = Mathf.Abs(now - currentNoteCenterTime);
                var j = Judge(offset);

                if (j != Judgement.Miss)
                    satisfiedThisNote = true;

                Fire(j, offset);
            }
            else
            {
                if (offbeatTapIsMiss)
                    Fire(Judgement.Miss, goodWindow + 0.001f);
            }
        }

        private void HandleBeat()
        {
            beatIndex++;

            if (beatsPerNote <= 0) beatsPerNote = 1; // 안전장치
            int phase = Mathf.FloorToInt(Mathf.Repeat(notePhase, beatsPerNote));
            bool isNoteBeat = (beatIndex % beatsPerNote) == phase;

            if (isNoteBeat)
                OpenWindow();
        }

        private void OpenWindow()
        {
            windowOpen = true;
            satisfiedThisNote = false;
            currentNoteCenterTime = Time.time;

            CancelInvoke(nameof(CloseWindow)); // 중복 방지
            Invoke(nameof(CloseWindow), goodWindow);
        }

        private void CloseWindow()
        {
            if (windowOpen && !satisfiedThisNote)
                Fire(Judgement.Miss, goodWindow + 0.001f);

            windowOpen = false;
        }

        private Judgement Judge(float offset)
        {
            if (offset <= perfectWindow) return Judgement.Perfect;
            if (offset <= goodWindow) return Judgement.Good;
            return Judgement.Miss;
        }

        private void Fire(Judgement j, float offsetSeconds)
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

        // ---------- 인스펙터/외부 제어용 ----------
        public void SetBeatsPerNote(int v) => beatsPerNote = Mathf.Max(1, v);
        public void SetNotePhase(int phase) => notePhase = phase;
        public void SetWindows(float perfect, float good)
        {
            perfectWindow = Mathf.Max(0f, perfect);
            goodWindow = Mathf.Max(perfectWindow, good);
        }

        // ---------- 읽기용 Getter (UI 등) ----------
        public int BeatsPerNote => Mathf.Max(1, beatsPerNote);
        public int NotePhase => Mathf.FloorToInt(Mathf.Repeat(notePhase, BeatsPerNote));
        public int CurrentBeatIndex => beatIndex;
        public float PerfectWindow => perfectWindow;  // ← BeatBarUI가 참조
        public float GoodWindow => goodWindow;     // ← BeatBarUI가 참조
    }
}
