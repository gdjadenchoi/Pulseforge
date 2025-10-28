// Assets/Systems/Ore.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 채굴 대상 Ore:
    /// - HP를 가지며 0이 되면 파괴/비활성화
    /// - 채굴 중 색/스케일 히트 이펙트
    /// - 하단 HP바(SpriteRenderer) 생성
    /// - 파괴 시 RewardManager에 보상 지급
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class Ore : MonoBehaviour
    {
        // ---------- Stats ----------
        [Header("Stats")]
        [SerializeField] private float maxHP = 10f;
        [SerializeField] private bool destroyOnDeath = true;

        // ---------- FX ----------
        [Header("FX")]
        [SerializeField] private Color baseColor = Color.white;
        [SerializeField] private Color minedTint = new Color(0.75f, 1f, 1f, 1f);
        [SerializeField, Range(1f, 1.3f)] private float hitScale = 1.12f;
        [SerializeField, Range(1f, 20f)] private float relaxSpeed = 10f;

        // ---------- HP Bar ----------
        [Header("HP Bar")]
        [SerializeField] private bool showHpBar = true;
        [SerializeField] private float barWidth = 0.9f;
        [SerializeField] private float barHeight = 0.12f;
        [SerializeField] private float barYOffset = 0f; // 0이면 자동 배치
        [SerializeField] private Color barBackColor = new(0f, 0f, 0f, 0.75f);
        [SerializeField] private Color barFillColor = new(1f, 0.25f, 0.25f, 1f);

        // ---------- Reward ----------
        // 전역 enum RewardType(RewardType.cs)을 사용한다.
        [Serializable]
        public struct RewardEntry
        {
            public RewardType type;
            public int minAmount;
            public int maxAmount;
        }

        [Header("Reward (on death)")]
        [SerializeField]
        private List<RewardEntry> rewards = new()
        {
            new RewardEntry { type = RewardType.Crystal, minAmount = 1, maxAmount = 3 }
        };

        [Header("Events")]
        public UnityEvent<RewardType, int> OnDestroyedWithReward;

        // ---------- Runtime ----------
        private float _hp;
        private SpriteRenderer _sr;
        private Color _targetColor;
        private Vector3 _targetScale;

        // HP bar parts
        private Transform _barRoot;
        private SpriteRenderer _barBack;
        private SpriteRenderer _barFill;

        // 재사용 화이트 스프라이트 (null 한 번만 생성)
        private static Sprite _whiteSprite;
        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                    _whiteSprite = Sprite.Create(
                        Texture2D.whiteTexture,
                        new Rect(0, 0, 1, 1),
                        new Vector2(0.5f, 0.5f),
                        1f
                    );
                return _whiteSprite;
            }
        }

        // ---------- Unity ----------
        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr != null) _sr.color = baseColor;

            var col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void OnEnable()
        {
            _hp = Mathf.Max(1f, maxHP);
            _targetColor = baseColor;
            _targetScale = Vector3.one;

            BuildHpBar();
            UpdateHpBar();
        }

        private void OnDisable()
        {
            DestroyHpBar();
        }

        private void Update()
        {
            if (_sr == null) return;

            // 히트 이펙트 감쇠
            _sr.color = Color.Lerp(_sr.color, _targetColor, Time.deltaTime * relaxSpeed);
            transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.deltaTime * relaxSpeed);

            // 타깃 원복
            _targetColor = baseColor;
            _targetScale = Vector3.one;
        }

        // ---------- Public API ----------
        /// <summary>커서 등에서 dps*dt 만큼 HP 감소</summary>
        public void ApplyDamage(float dps, float dt)
        {
            if (_hp <= 0f || dps <= 0f || dt <= 0f) return;

            _hp -= dps * dt;
            if (_hp < 0f) _hp = 0f;

            // 히트 피드백
            _targetColor = minedTint;
            _targetScale = Vector3.one * hitScale;

            UpdateHpBar();

            if (_hp <= 0f)
                OnDeath();
        }

        // ---------- Death / Reward ----------
        private void OnDeath()
        {
            if (rewards != null && rewards.Count > 0)
            {
                var rm = RewardManager.SafeInstance; // null-safe
                for (int i = 0; i < rewards.Count; i++)
                {
                    var r = rewards[i];
                    int lo = Mathf.Min(r.minAmount, r.maxAmount);
                    int hi = Mathf.Max(r.minAmount, r.maxAmount);
                    int amt = UnityEngine.Random.Range(lo, hi + 1);
                    if (amt <= 0) continue;

                    rm?.Add(r.type, amt);
                    OnDestroyedWithReward?.Invoke(r.type, amt);
                }
            }

            if (destroyOnDeath) Destroy(gameObject);
            else gameObject.SetActive(false);
        }

        // ---------- HP Bar ----------
        private void BuildHpBar()
        {
            DestroyHpBar();
            if (!showHpBar) return;

            _barRoot = new GameObject("HPBar").transform;
            _barRoot.SetParent(transform, false);

            _barBack = new GameObject("Back").AddComponent<SpriteRenderer>();
            _barFill = new GameObject("Fill").AddComponent<SpriteRenderer>();
            _barBack.transform.SetParent(_barRoot, false);
            _barFill.transform.SetParent(_barRoot, false);

            // 광석보다 항상 앞에 보이도록 같은 레이어 + 높은 Order
            int baseOrder = _sr ? _sr.sortingOrder : 0;
            int layerId = _sr ? _sr.sortingLayerID : 0;
            _barBack.sortingLayerID = layerId; _barFill.sortingLayerID = layerId;
            _barBack.sortingOrder = baseOrder + 10;
            _barFill.sortingOrder = baseOrder + 11;

            _barBack.color = barBackColor;
            _barFill.color = barFillColor;

            float yOff = barYOffset;
            if (Mathf.Approximately(yOff, 0f) && _sr && _sr.sprite)
                yOff = -(_sr.bounds.extents.y + 0.12f);
            _barRoot.localPosition = new Vector3(0f, yOff, 0f);

            UpdateHpBar();
        }

        private void UpdateHpBar()
        {
            if (_barBack == null || _barFill == null) return;

            _barBack.sprite = WhiteSprite;
            _barFill.sprite = WhiteSprite;

            _barBack.transform.localScale = new Vector3(barWidth, barHeight, 1f);

            float t = Mathf.Clamp01(_hp / Mathf.Max(0.0001f, maxHP));
            _barFill.transform.localScale = new Vector3(barWidth * t, barHeight, 1f);
            _barFill.transform.localPosition = new Vector3(-(barWidth * (1f - t)) * 0.5f, 0f, 0f);
        }

        private void DestroyHpBar()
        {
            if (_barBack) Destroy(_barBack.gameObject);
            if (_barFill) Destroy(_barFill.gameObject);
            if (_barRoot) Destroy(_barRoot.gameObject);
            _barBack = null; _barFill = null; _barRoot = null;
        }
    }
}
