using System;
using System.Collections;
using UnityEngine;

namespace Pulseforge.Systems
{
    [RequireComponent(typeof(Collider2D))]
    [DisallowMultipleComponent]
    public class Ore : MonoBehaviour
    {
        /// <summary>
        /// 이 Ore가 "깨졌을 때(HP<=0 판정 시점)" 1회 호출되는 이벤트.
        /// - destroyOnBreak=true면 Destroy 직전에 호출됨
        /// - destroyOnBreak=false면 HP 리셋 직전에 호출됨
        ///
        /// 주의:
        /// - 구독자는 OnDisable/OnDestroy에서 구독 해제 권장
        /// </summary>
        public event Action<Ore> OnBrokenEvent;

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

        [Header("Hit Flash (OreHitFlash)")]
        [Tooltip("OreHitFlash 컴포넌트를 통해 Additive 오버레이 플래시를 재생")]
        [SerializeField] private bool flashOnHit = true;
        [SerializeField] private OreHitFlash hitFlash;

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

        // HP Bar
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

        // break guard (중복 파괴/중복 호출 방지)
        private bool _brokenFired;

        // ─────────────────────────────────────────────────────────────
        // Public read-only state (SSOT helpers)
        // ─────────────────────────────────────────────────────────────
        public float MaxHp => Mathf.Max(1f, maxHp);
        public float CurrentHp => hp;
        public bool IsBroken => _brokenFired;

        private void Awake()
        {
            hp = Mathf.Max(1f, maxHp);

            // Ore는 "자식이 없고 루트에 SpriteRenderer가 붙어있는" 구조도 있으니
            // GetComponentInChildren로 잡되, 자기 자신도 포함이라 안전.
            mySprite = GetComponentInChildren<SpriteRenderer>();
            mySortingOrder = mySprite ? mySprite.sortingOrder : 0;

            // OreHitFlash 자동 연결(드래그 안 해도 돌아가게)
            if (hitFlash == null) hitFlash = GetComponent<OreHitFlash>();

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

        /// <summary>
        /// 고정 데미지(절대값)를 적용.
        /// </summary>
        public void ApplyHit(float damage)
        {
            if (damage <= 0f) return;
            if (_brokenFired) return; // 이미 깨진 처리 들어갔으면 무시(안전)

            if (revealBarOnFirstHit && showHpBar) SetBarVisible(true);

            hp = Mathf.Max(0f, hp - damage);
            UpdateBarFill();

            // ✅ 타격 플래시(오버레이 Additive)
            if (flashOnHit && hitFlash != null)
                hitFlash.PlayNormal();

            // ✅ 기존 흔들림 유지
            if (wobbleOnHit)
                PlayWobble();

            if (hp <= 0f)
                OnBroken();
        }

        /// <summary>
        /// MaxHP의 %만큼 데미지를 적용.
        /// - percentOfMax: 0.2f = 20%
        /// - ceilToIntDamage: true면 데미지를 올림(ceil) 처리해 체감 보장
        /// - minOneDamageIfHit: true면 Good/Perfect 같은 "히트"에서 최소 1데미지 보장
        /// </summary>
        public void ApplyHitPercentOfMax(float percentOfMax, bool ceilToIntDamage = true, bool minOneDamageIfHit = true)
        {
            if (percentOfMax <= 0f) return;
            if (_brokenFired) return;

            float dmg = MaxHp * percentOfMax;

            if (ceilToIntDamage)
                dmg = Mathf.Ceil(dmg);

            if (minOneDamageIfHit)
                dmg = Mathf.Max(1f, dmg);

            ApplyHit(dmg);
        }

        private void OnBroken()
        {
            if (_brokenFired) return;
            _brokenFired = true;

            // ✅ 보상 지급 (RewardManager 연동)
            if (rewardAmount > 0)
                RewardManager.Instance?.Add(rewardType, rewardAmount);

            // ✅ 경험치 지급 (LevelManager 연동)
            if (expAmount > 0 && LevelManager.Instance != null)
                LevelManager.Instance.AddExp(expAmount);

            // ✅ 외부 훅(예: BigOreSuccessHook)에게 "깨짐" 알림
            try
            {
                OnBrokenEvent?.Invoke(this);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }

            if (destroyOnBreak)
                Destroy(gameObject);
            else
            {
                // 리셋형 광석이면 다시 깨질 수 있으므로 가드 해제
                hp = maxHp;
                _brokenFired = false;

                UpdateBarFill();
            }
        }

        // ── HP Bar 생성/갱신 ─────────────────────────────────────────────────────

        /// <summary>
        /// 런타임에서 HP바 표시 여부를 제어.
        /// "일반 Ore HP바 제거" 같은 요구를 코드 1줄로 처리하기 위한 API.
        /// </summary>
        public void SetHpBarEnabled(bool enabled, bool revealOnFirstHitMode = true)
        {
            showHpBar = enabled;
            revealBarOnFirstHit = revealOnFirstHitMode;

            if (!showHpBar)
            {
                if (barRoot != null) barRoot.gameObject.SetActive(false);
                return;
            }

            EnsureBarBuilt();
            SetBarVisible(!revealBarOnFirstHit);
            MarkBarDirty();
        }

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

            // sorting
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
