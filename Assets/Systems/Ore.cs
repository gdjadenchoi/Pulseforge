using System.Collections;
using UnityEngine;

namespace Pulseforge.Systems
{
    [RequireComponent(typeof(Collider2D))]
    [DisallowMultipleComponent]
    public class Ore : MonoBehaviour
    {
        [Header("HP")]
        [SerializeField] private float maxHp = 30f;
        [SerializeField] private bool destroyOnBreak = true;

        [Header("Reward")]
        [SerializeField] private RewardType rewardType = RewardType.Crystal;
        [SerializeField] private int rewardAmount = 1;

        [Header("Experience")]
        [Tooltip("이 광석을 파괴했을 때 부여할 기본 경험치 양")]
        [SerializeField] private int expAmount = 1;

        [Header("Hit Feedback")]
        [SerializeField] private bool wobbleOnHit = true;
        [SerializeField] private float wobblePos = 0.035f;
        [SerializeField] private float wobbleScale = 0.06f;
        [SerializeField] private float wobbleDuration = 0.08f;

        [Header("HP Bar (world-units)")]
        [SerializeField] private bool showHpBar = true;
        [Tooltip("처음엔 숨겨두고, 한 번이라도 맞으면 켜짐")]
        [SerializeField] private bool revealBarOnFirstHit = true;
        [SerializeField] private bool barAbove = false;
        [SerializeField] private float barYOffset = 0.0f;
        [SerializeField] private Vector2 barSize = new Vector2(0.7f, 0.08f);
        [SerializeField] private Color barColor = new Color(1f, 0.12f, 0.12f, 1f);
        [SerializeField] private Color barBgColor = new Color(0, 0, 0, 0.85f);
        [SerializeField] private int barSortingOffsetBG = 90;
        [SerializeField] private int barSortingOffsetFill = 100;

        [Header("Pixel-Perfect (optional)")]
        [SerializeField] private bool minOnePixelThickness = false;
        [SerializeField] private int referencePPU = 128;

        private float hp;
        private Transform barRoot;
        private SpriteRenderer srBg;
        private SpriteRenderer srFill;
        private static Sprite _onePxSprite;
        private SpriteRenderer mySprite;
        private int mySortingOrder;

        private bool barDirty;

        // wobble
        private Coroutine wobbleCo;
        private Vector3 basePos;
        private Vector3 baseScale;

        private void Awake()
        {
            hp = Mathf.Max(1f, maxHp);
            mySprite = GetComponentInChildren<SpriteRenderer>();
            mySortingOrder = mySprite ? mySprite.sortingOrder : 0;

            EnsureBarBuilt();
            SetBarVisible(showHpBar && !revealBarOnFirstHit);
            MarkBarDirty();
        }

        private void LateUpdate()
        {
            if (barDirty)
            {
                barDirty = false;
                LayoutBar();
                UpdateBarFill();
            }
        }

        public void ApplyHit(float damage)
        {
            if (damage <= 0f) return;

            if (revealBarOnFirstHit && showHpBar) SetBarVisible(true);

            hp = Mathf.Max(0f, hp - damage);
            UpdateBarFill();

            if (wobbleOnHit) PlayWobble();

            if (hp <= 0f)
                OnBroken();
        }

        private void OnBroken()
        {
            // ✅ 보상 지급 (RewardManager 연동)
            if (rewardAmount > 0)
                RewardManager.Instance?.Add(rewardType, rewardAmount);

            // ✅ 경험치 지급 (LevelManager 연동)
            //   → 지금은 광석 타입에 따라 계수 안 붙이고, 기본 양만 넘김
            if (expAmount > 0 && LevelManager.Instance != null)
                LevelManager.Instance.AddExp(expAmount);

            if (destroyOnBreak)
                Destroy(gameObject);
            else
            {
                hp = maxHp;
                UpdateBarFill();
            }
        }

        // ── HP Bar 생성/갱신 ─────────────────────────────────────────────────────
        private void EnsureBarBuilt()
        {
            if (!showHpBar) return;

            if (_onePxSprite == null)
                _onePxSprite = BuildOnePxSprite();

            if (barRoot == null)
            {
                barRoot = new GameObject("HPBar").transform;
                barRoot.SetParent(transform, false);
            }

            if (srBg == null)
            {
                var bg = new GameObject("BG");
                bg.transform.SetParent(barRoot, false);
                srBg = bg.AddComponent<SpriteRenderer>();
                srBg.sprite = _onePxSprite;
            }

            if (srFill == null)
            {
                var fill = new GameObject("Fill");
                fill.transform.SetParent(barRoot, false);
                srFill = fill.AddComponent<SpriteRenderer>();
                srFill.sprite = _onePxSprite;
            }

            int bgOrder = mySortingOrder + barSortingOffsetBG;
            int fillOrder = mySortingOrder + barSortingOffsetFill;
            int sortingLayerId = mySprite ? mySprite.sortingLayerID : 0;

            srBg.sortingLayerID = sortingLayerId;
            srFill.sortingLayerID = sortingLayerId;
            srBg.sortingOrder = bgOrder;
            srFill.sortingOrder = fillOrder;

            srBg.color = barBgColor;
            srFill.color = barColor;
        }

        private void SetBarVisible(bool v)
        {
            if (barRoot) barRoot.gameObject.SetActive(showHpBar && v);
        }

        private void LayoutBar()
        {
            float yOff = Mathf.Abs(barYOffset) * (barAbove ? +1f : -1f);

            float height = barSize.y;
            if (minOnePixelThickness && referencePPU > 0)
                height = Mathf.Max(height, 1f / referencePPU);

            if (barRoot)
            {
                barRoot.localPosition = new Vector3(0, yOff, 0);

                if (srBg)
                {
                    srBg.transform.localPosition = Vector3.zero;
                    srBg.transform.localScale = new Vector3(barSize.x, height, 1f);
                }

                if (srFill)
                {
                    srFill.transform.localPosition = new Vector3(-barSize.x * 0.5f, 0f, 0f);
                    srFill.transform.localScale = new Vector3(barSize.x, height, 1f);
                }
            }
        }

        private void UpdateBarFill()
        {
            if (srFill == null) return;

            float t = Mathf.Approximately(maxHp, 0f) ? 0f : Mathf.Clamp01(hp / maxHp);

            var scale = srFill.transform.localScale;
            scale.x = barSize.x * t;
            srFill.transform.localScale = scale;
        }

        private static Sprite BuildOnePxSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            sprite.name = "OnePxSprite_Runtime";
            return sprite;
        }

        private void MarkBarDirty()
        {
            barDirty = true;
        }

        // ── Wobble ─────────────────────────────────────────────────────────────
        private void PlayWobble()
        {
            if (wobbleCo != null)
            {
                StopCoroutine(wobbleCo);
            }

            wobbleCo = StartCoroutine(CoWobble());
        }

        private IEnumerator CoWobble()
        {
            if (mySprite == null)
                mySprite = GetComponentInChildren<SpriteRenderer>();

            basePos = transform.localPosition;
            baseScale = transform.localScale;

            float t = 0f;
            while (t < wobbleDuration)
            {
                t += Time.deltaTime;
                float n = Mathf.Clamp01(t / wobbleDuration);

                float offX = Mathf.Sin(t * 62.83f) * wobblePos * n;
                float scl = 1f + (Mathf.Sin(t * 94.25f) * wobbleScale * n);

                transform.localPosition = basePos + new Vector3(offX, 0f, 0f);
                transform.localScale = baseScale * scl;
                yield return null;
            }

            transform.localPosition = basePos;
            transform.localScale = baseScale;
            wobbleCo = null;
        }

        // ── Gizmo ──────────────────────────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, 0, 0, 0.35f);
            float yOff = Mathf.Abs(barYOffset) * (barAbove ? +1f : -1f);
            Vector3 center = transform.position + new Vector3(0, yOff, 0);
            Vector3 size = new Vector3(barSize.x, barSize.y, 0.001f);
            Gizmos.DrawCube(center, size);
        }
    }
}
