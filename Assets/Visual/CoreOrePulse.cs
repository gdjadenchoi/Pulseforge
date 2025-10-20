using UnityEngine;

namespace Pulseforge.Visuals
{
    /// <summary>
    /// 중앙 Ore가 비트/판정에 반응해 박동한다.
    /// - OnBeat(): 가벼운 펄스
    /// - OnGood()/OnPerfect(): 강한 펄스
    /// - OnMiss(): 살짝 수축
    /// 자식으로 Base/Glow SpriteRenderer 2개를 사용(Glow는 Particles/Additive 권장).
    /// </summary>
    public class CoreOrePulse : MonoBehaviour
    {
        [Header("Sprites")]
        [SerializeField] private SpriteRenderer baseSprite; // 필수
        [SerializeField] private SpriteRenderer glowSprite; // 선택

        [Header("Colors")]
        [SerializeField] private Color baseColor = Color.cyan;
        [SerializeField] private Color hitColor = new Color(0.9f, 1f, 1f, 1f);

        [Header("Scales")]
        [SerializeField] private float idleScale = 1f;
        [SerializeField] private float beatScale = 1.04f;
        [SerializeField] private float goodScale = 1.08f;
        [SerializeField] private float perfectScale = 1.12f;
        [SerializeField] private float missScale = 0.96f;

        [Header("Glow Alpha")]
        [SerializeField] private float glowIdle = 0.06f;
        [SerializeField] private float glowBeat = 0.12f;
        [SerializeField] private float glowGood = 0.2f;
        [SerializeField] private float glowPerfect = 0.35f;

        [Header("Lerp Speeds")]
        [SerializeField] private float returnSpeed = 6f;
        [SerializeField] private float flashSpeed = 16f;

        Vector3 _targetScale;
        Color _targetColor;
        float _targetGlow;

        void Reset()
        {
            var srs = GetComponentsInChildren<SpriteRenderer>(true);
            if (srs.Length >= 1) baseSprite = srs[0];
            if (srs.Length >= 2) glowSprite = srs[1];
        }

        void Awake()
        {
            _targetScale = Vector3.one * idleScale;
            _targetColor = baseColor;
            _targetGlow = glowIdle;

            if (baseSprite) baseSprite.color = baseColor;
            if (glowSprite)
            {
                var c = baseColor; c.a = glowIdle;
                glowSprite.color = c;
            }
            transform.localScale = Vector3.one * idleScale;
        }

        void Update()
        {
            if (baseSprite)
                baseSprite.color = Color.Lerp(baseSprite.color, _targetColor, Time.deltaTime * returnSpeed);

            if (glowSprite)
            {
                var g = glowSprite.color;
                g.a = Mathf.Lerp(g.a, _targetGlow, Time.deltaTime * returnSpeed);
                glowSprite.color = g;
            }

            transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.deltaTime * returnSpeed);
        }

        // -------- 이벤트용 API --------
        public void OnBeat()
        {
            Pulse(beatScale, glowBeat, keepColor: true);
        }

        public void OnGood()
        {
            Pulse(goodScale, glowGood);
        }

        public void OnPerfect()
        {
            Pulse(perfectScale, glowPerfect);
        }

        public void OnMiss()
        {
            _targetScale = Vector3.one * missScale;
            _targetGlow = glowIdle * 0.5f;
            _targetColor = Color.Lerp(baseColor, Color.gray, 0.25f);
        }

        void Pulse(float scale, float glow, bool keepColor = false)
        {
            _targetScale = Vector3.one * scale;
            _targetGlow = glow;

            if (!keepColor && baseSprite)
                baseSprite.color = Color.Lerp(baseSprite.color, hitColor, Time.deltaTime * flashSpeed);

            // 목표색은 다시 기본색으로 회귀
            _targetColor = baseColor;
        }
    }
}
