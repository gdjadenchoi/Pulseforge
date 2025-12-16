using System.Collections;
using TMPro;
using UnityEngine;

namespace Pulseforge.UI
{
    public class StartTextPresenter : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private TMP_Text startText;

        [Header("Timing")]
        [Min(0f)] public float appearDelay = 0.0f;
        [Min(0.05f)] public float totalDuration = 0.6f;

        [Header("Scale Pop")]
        [Min(0f)] public float startScale = 0.8f;
        [Min(1f)] public float overshootScale = 1.1f;

        public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public bool IsPlaying { get; private set; }

        private void Reset()
        {
            startText = GetComponent<TMP_Text>();
        }

        private void Awake()
        {
            if (startText == null)
                startText = GetComponent<TMP_Text>();

            // 기본은 숨김으로 시작(씬 들어오자마자 보이는 것 방지)
            gameObject.SetActive(false);
            IsPlaying = false;
        }

        /// <summary>
        /// 반드시 "활성 오브젝트(SessionController 등)"가 StartCoroutine으로 돌려야 함.
        /// (StartText 오브젝트가 비활성이라도 코루틴은 호출자에서 돌기 때문에 문제 없음)
        /// </summary>
        public IEnumerator PlayRoutine(string text)
        {
            if (startText == null) yield break;

            // 중복 재생 방지(겹쳐 보이는 문제 방지)
            if (IsPlaying) yield break;
            IsPlaying = true;

            // 표시 시작
            gameObject.SetActive(true);

            startText.text = text;

            if (appearDelay > 0f)
                yield return new WaitForSecondsRealtime(appearDelay);

            RectTransform rt = startText.rectTransform;

            float dur = Mathf.Max(0.05f, totalDuration);
            float inDur = dur * 0.35f;
            float holdDur = dur * 0.45f;
            float outDur = dur - inDur - holdDur;

            rt.localScale = Vector3.one * startScale;

            // 1) startScale -> overshoot
            float t = 0f;
            while (t < inDur)
            {
                t += Time.unscaledDeltaTime;
                float tt = Mathf.Clamp01(t / Mathf.Max(0.0001f, inDur));
                float c = (scaleCurve != null) ? scaleCurve.Evaluate(tt) : tt;
                float s = Mathf.Lerp(startScale, overshootScale, c);
                rt.localScale = Vector3.one * s;
                yield return null;
            }
            rt.localScale = Vector3.one * overshootScale;

            // 2) overshoot -> 1.0 settle
            t = 0f;
            float settleDur = Mathf.Min(0.12f, holdDur * 0.35f);
            while (t < settleDur)
            {
                t += Time.unscaledDeltaTime;
                float tt = Mathf.Clamp01(t / Mathf.Max(0.0001f, settleDur));
                float c = (scaleCurve != null) ? scaleCurve.Evaluate(tt) : tt;
                float s = Mathf.Lerp(overshootScale, 1f, c);
                rt.localScale = Vector3.one * s;
                yield return null;
            }
            rt.localScale = Vector3.one;

            // 3) hold
            float remainHold = Mathf.Max(0f, holdDur - settleDur);
            if (remainHold > 0f)
                yield return new WaitForSecondsRealtime(remainHold);

            // 4) out
            if (outDur > 0f)
                yield return new WaitForSecondsRealtime(outDur);

            // 숨김
            gameObject.SetActive(false);

            IsPlaying = false;
        }

        public void HideImmediate()
        {
            IsPlaying = false;
            if (startText != null)
                startText.rectTransform.localScale = Vector3.one;
            gameObject.SetActive(false);
        }
    }
}
