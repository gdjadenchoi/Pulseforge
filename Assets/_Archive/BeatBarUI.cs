using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Pulseforge.Core;
using Pulseforge.Systems;

namespace Pulseforge.UI
{
    /// <summary>
    /// 하단 스캐닝 바 UI.
    /// - 한 노트 주기(= beatsPerNote * beatInterval) 동안 인디케이터가 좌→우 진행.
    /// - 오른쪽 끝에 Good/Perfect 창을 띠로 시각화.
    /// - DSP(AudioSettings.dspTime) 기준으로 오디오와 정확히 동기화.
    /// - MiningController의 BeatsPerNote / NotePhase / Windows를 그대로 따름.
    /// </summary>
    public class BeatBarUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RhythmConductor conductor;   // 필수
        [SerializeField] private MiningController mining;     // 권장(주기/창 읽음)

        [Header("UI Parts")]
        [SerializeField] private RectTransform barArea;       // 바 영역
        [SerializeField] private Image barBg;                 // 배경 바
        [SerializeField] private Image zoneGood;              // Good 영역
        [SerializeField] private Image zonePerfect;           // Perfect 영역
        [SerializeField] private RectTransform indicator;     // 진행 인디케이터(세로 라인)
        [SerializeField] private TMP_Text beatLabel;          // 선택: 남은 박자 표시

        [Header("Style")]
        [SerializeField] private Color bgColor = new Color(0.12f, 0.12f, 0.16f, 1f);
        [SerializeField] private Color goodColor = new Color(0.18f, 0.65f, 1f, 0.45f);
        [SerializeField] private Color perfectColor = new Color(1f, 0.95f, 0.35f, 0.9f);
        [SerializeField] private Color indicatorColor = Color.white;
        [SerializeField] private float indicatorHeightScale = 1.2f;

        [Header("If MiningController is null")]
        [SerializeField] private int fallbackBeatsPerNote = 2;
        [SerializeField] private int fallbackNotePhase = 0;
        [SerializeField] private float fallbackGoodWindow = 0.25f;
        [SerializeField] private float fallbackPerfectWindow = 0.10f;

        // 내부 상태(DSP 기반)
        private int beatIndex = -1;
        private double lastNoteDSP = 0.0;  // 직전 노트 기준 시각
        private double nextNoteDSP = 0.0;  // 다음 노트 기준 시각
        private double cycleSeconds = 0.0;  // 한 주기 길이(초)

        // 캐시
        private Image indicatorImg;
        private RectTransform zoneGoodRT;
        private RectTransform zonePerfectRT;

        private void Awake()
        {
            indicatorImg = indicator ? indicator.GetComponent<Image>() : null;
            zoneGoodRT = zoneGood ? zoneGood.rectTransform : null;
            zonePerfectRT = zonePerfect ? zonePerfect.rectTransform : null;

            // ✅ Indicator를 '왼쪽-중앙' 기준으로 강제 세팅
            if (indicator != null)
            {
                indicator.anchorMin = new Vector2(0f, 0.5f);
                indicator.anchorMax = new Vector2(0f, 0.5f);
                indicator.pivot = new Vector2(0f, 0.5f);
                var p = indicator.anchoredPosition;
                p.x = 0f; p.y = 0f;
                indicator.anchoredPosition = p;
            }

            if (barBg) barBg.color = bgColor;
            if (zoneGood) zoneGood.color = goodColor;
            if (zonePerfect) zonePerfect.color = perfectColor;
            if (indicatorImg) indicatorImg.color = indicatorColor;
        }

        private void OnEnable()
        {
            if (conductor != null)
            {
                conductor.OnBeat.AddListener(OnBeat);
                RecalcCycle();
                LayoutWindows();

                // ▶ 첫 OnBeat 전에라도 인디케이터가 움직이도록 임시 주기 초기화
                if (nextNoteDSP <= 0.0)
                {
                    double now = AudioSettings.dspTime;
                    lastNoteDSP = now - (cycleSeconds * 0.5);      // 중간쯤에서 시작
                    nextNoteDSP = lastNoteDSP + cycleSeconds;
                }
            }
        }

        private void OnDisable()
        {
            if (conductor != null)
                conductor.OnBeat.RemoveListener(OnBeat);
        }

        private void Update()
        {
            if (conductor == null || barArea == null || indicator == null) return;

            // BPM/설정 변경 대응
            RecalcCycle();
            LayoutWindows();

            if (nextNoteDSP <= 0.0 || cycleSeconds <= 1e-6) return;

            double now = AudioSettings.dspTime;

            // 진행도 0~1
            float t = Mathf.InverseLerp((float)lastNoteDSP, (float)nextNoteDSP, (float)now);
            t = Mathf.Clamp01(t);

            // 인디케이터 위치 적용 (좌→우)
            float width = barArea.rect.width;
            float x = t * width;

            var p = indicator.anchoredPosition;
            p.x = x;
            indicator.anchoredPosition = p;

            // 인디케이터 높이 보정
            var s = indicator.sizeDelta;
            s.y = barArea.rect.height * indicatorHeightScale;
            indicator.sizeDelta = s;

            // 라벨(선택)
            if (beatLabel)
            {
                double timeLeft = System.Math.Max(0.0, nextNoteDSP - now);
                double beatsLeft = timeLeft / conductor.BeatIntervalSeconds;
                beatLabel.text = $"{beatsLeft:0.0} beats";
            }

            // 영역 진입에 따른 색상 강조
            if (indicatorImg && zoneGoodRT && zonePerfectRT)
            {
                float gx0 = width - zoneGoodRT.rect.width;     // Good 시작 X
                float px0 = width - zonePerfectRT.rect.width;  // Perfect 시작 X

                if (x >= px0)
                    indicatorImg.color = Color.Lerp(indicatorImg.color, perfectColor, Time.deltaTime * 15f);
                else if (x >= gx0)
                    indicatorImg.color = Color.Lerp(indicatorImg.color, goodColor, Time.deltaTime * 10f);
                else
                    indicatorImg.color = Color.Lerp(indicatorImg.color, indicatorColor, Time.deltaTime * 8f);
            }
        }

        private void OnBeat()
        {
            beatIndex++;

            int bpn = mining ? mining.BeatsPerNote : Mathf.Max(1, fallbackBeatsPerNote);
            int phase = mining ? mining.NotePhase : Mathf.FloorToInt(Mathf.Repeat(fallbackNotePhase, bpn));

            // ▶ MiningController의 NotePhase를 정확히 따름
            if ((beatIndex % bpn) == phase)
            {
                double now = AudioSettings.dspTime;
                lastNoteDSP = now;
                nextNoteDSP = lastNoteDSP + (bpn * conductor.BeatIntervalSeconds);
            }
            else if (nextNoteDSP != 0.0 && AudioSettings.dspTime > nextNoteDSP)
            {
                // 유실 보호
                double now = AudioSettings.dspTime;
                lastNoteDSP = now - (bpn * conductor.BeatIntervalSeconds);
                nextNoteDSP = now;
            }
        }

        private void RecalcCycle()
        {
            int bpn = mining ? mining.BeatsPerNote : Mathf.Max(1, fallbackBeatsPerNote);
            cycleSeconds = bpn * conductor.BeatIntervalSeconds;
        }

        /// <summary>Good/Perfect 영역을 오른쪽 끝 기준으로 배치.</summary>
        private void LayoutWindows()
        {
            if (barArea == null || zoneGood == null || zonePerfect == null || conductor == null) return;

            float gw = mining ? mining.GoodWindow : fallbackGoodWindow;
            float pw = mining ? mining.PerfectWindow : fallbackPerfectWindow;

            float width = barArea.rect.width;
            float height = barArea.rect.height;

            // 창 폭(px) = (창 시간 / 주기) * 전체 폭
            float goodPx = Mathf.Clamp01(gw / (float)System.Math.Max(1e-6, cycleSeconds)) * width;
            float perfPx = Mathf.Clamp01(pw / (float)System.Math.Max(1e-6, cycleSeconds)) * width;

            var goodRT = zoneGood.rectTransform;
            var perfRT = zonePerfect.rectTransform;

            // Good (오른쪽 정렬)
            goodRT.anchorMin = new Vector2(0f, 0.5f);
            goodRT.anchorMax = new Vector2(0f, 0.5f);
            goodRT.pivot = new Vector2(1f, 0.5f);
            goodRT.sizeDelta = new Vector2(goodPx, height);
            goodRT.anchoredPosition = new Vector2(width, 0f);

            // Perfect (오른쪽 정렬, Good 안쪽)
            perfRT.anchorMin = new Vector2(0f, 0.5f);
            perfRT.anchorMax = new Vector2(0f, 0.5f);
            perfRT.pivot = new Vector2(1f, 0.5f);
            perfRT.sizeDelta = new Vector2(perfPx, height);
            perfRT.anchoredPosition = new Vector2(width, 0f);

            if (barBg) barBg.color = bgColor;
        }
    }
}
