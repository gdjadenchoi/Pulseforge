using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Pulseforge.Core;
using Pulseforge.Systems;

namespace Pulseforge.UI
{
    /// <summary>
    /// 다음 "노트 박자"까지 남은 진행도를 링(Image.fillAmount)으로 표시.
    /// - DSP 시계(AudioSettings.dspTime)로 계산하여 오디오와 정확히 동기화.
    /// - MiningController의 BeatsPerNote/NotePhase 규칙을 그대로 따름.
    /// </summary>
    public class BeatRingUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RhythmConductor conductor;
        [SerializeField] private MiningController mining;   // 생략 가능(없으면 아래 수동 설정 사용)
        [SerializeField] private Image ring;                // UI Image (Type=Filled, Radial360)
        [SerializeField] private TMP_Text label;            // 선택: 남은 박자 표시

        [Header("If MiningController is null")]
        [SerializeField, Tooltip("몇 박마다 노트를 치는지")]
        private int beatsPerNote = 2;
        [SerializeField, Tooltip("위상(0~beatsPerNote-1)")]
        private int notePhase = 0;

        [Header("Style")]
        [SerializeField] private Color idleColor = new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField] private Color readyColor = new Color(1f, 0.95f, 0.3f, 1f);
        [SerializeField, Tooltip("노트 직전 강조 임계값(0~1)")]
        private float readyThreshold = 0.85f;

        // 내부 상태(DSP 기반)
        private int beatIndex = -1;
        private double lastNoteDSP = 0.0;
        private double nextNoteDSP = 0.0;
        private double cycleSeconds = 0.0; // = beatsPerNote * beatInterval

        private void OnEnable()
        {
            if (conductor != null)
            {
                conductor.OnBeat.AddListener(OnBeat);
                RecalcCycle();
            }
        }

        private void OnDisable()
        {
            if (conductor != null)
                conductor.OnBeat.RemoveListener(OnBeat);
        }

        private void Update()
        {
            if (ring == null || conductor == null) return;

            // BPM 변경 대응
            RecalcCycle();

            if (nextNoteDSP <= 0.0 || cycleSeconds <= 1e-6)
            {
                ring.fillAmount = 0f;
                if (label) label.text = string.Empty;
                return;
            }

            double now = AudioSettings.dspTime;

            // 진행도(마지막 노트 → 다음 노트)
            float t = Mathf.InverseLerp((float)lastNoteDSP, (float)nextNoteDSP, (float)now);
            ring.fillAmount = Mathf.Clamp01(t);

            // 색 전환
            ring.color = (ring.fillAmount >= readyThreshold) ? readyColor : idleColor;

            // 라벨(선택): 남은 박자
            if (label)
            {
                double timeLeft = System.Math.Max(0.0, nextNoteDSP - now);
                double beatsLeft = timeLeft / conductor.BeatIntervalSeconds;
                label.text = $"{beatsLeft:0.0} beats";
            }
        }

        private void OnBeat()
        {
            beatIndex++;

            int bpn = mining ? mining.BeatsPerNote : Mathf.Max(1, beatsPerNote);
            int phase = mining ? mining.NotePhase : Mathf.FloorToInt(Mathf.Repeat(notePhase, bpn));

            // 이번 박자가 "노트 박자"면 주기 리셋(DSP 기준)
            if ((beatIndex % bpn) == phase)
            {
                double now = AudioSettings.dspTime;
                lastNoteDSP = now;
                nextNoteDSP = lastNoteDSP + (bpn * conductor.BeatIntervalSeconds);
            }
            else if (nextNoteDSP != 0.0 && AudioSettings.dspTime > nextNoteDSP)
            {
                // 싱크 유실 안전장치
                double now = AudioSettings.dspTime;
                lastNoteDSP = now - (bpn * conductor.BeatIntervalSeconds);
                nextNoteDSP = now;
            }
        }

        private void RecalcCycle()
        {
            int bpn = mining ? mining.BeatsPerNote : Mathf.Max(1, beatsPerNote);
            cycleSeconds = bpn * conductor.BeatIntervalSeconds;
        }
    }
}
