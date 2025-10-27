using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 채굴 대상 Ore.
    /// - HP를 가지며 0이 되면 파괴/비활성화
    /// - 채굴 중 색/스케일 히트 이펙트
    /// - 하단 HP바 표시(스프라이트 렌더러로 구성)
    /// - 파괴 시 RewardManager에 보상 지급
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class Ore : MonoBehaviour
    {
        // ================== Stats ==================
        [Header("Stats")]
        [SerializeField] private float maxHP = 10f;
        [SerializeField] private bool destroyOnDeath = true;

        // ================== FX ==================
        [Header("FX")]
        [SerializeField] private Color baseColor = Color.white;
        [SerializeField] private Color minedTint = new Color(0.75f, 1f, 1f, 1f);
        [SerializeField, Range(1f, 1.3f)] private float hitScale = 1.12f;
        [SerializeField, Range(1f, 20f)] private float relaxSpeed = 10f;

        // ================== HP Bar ==================
        [Header("HP Bar")]
        [SerializeField] private bool showHpBar = true;
        [SerializeField] private float barWidth = 0.9f;
        [SerializeField] private float barHeight = 0.12f;
        [SerializeField] private float barYOffset = 0f; // 0이면 자동 배치
        [SerializeField] private Color barBackColor = new(0f, 0f, 0f, 0.75f);
        [SerializeField] private Color barFillColor = new(1f, 0.25f, 0.25f, 1f);

        // ================== Reward ==================
        // 전역 enum RewardType(Pulseforge.Systems.RewardType)를 사용합니다. (Ore 내부에 enum 정의 X)
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

        // ================== Runtime ==================
        private float _hp;
        private SpriteRenderer _sr;
        private Color _targetColor;
        private Vector3 _targetScale;

        // HP Bar runtime parts
        private Transform _barRoot;
        private SpriteRenderer _barBack;
        private SpriteRenderer _barFill;

        // ================== Unity ==================
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

        private void Update()
        {
            // 히트 이펙트 감쇠
            if (_sr != null)
            {
                _sr.color = Color.Lerp(_sr.color, _targetColor, Time.deltaTime * relaxSpeed);
                transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.deltaTime * relaxSpeed);
                // 다음 프레임을 위해 타깃 복원
                _targetColor = baseColor;
                _targetScale = Vector3.one;
            }
        }

        private void OnDisable()
        {
            DestroyHpBar();
        }

        // ================== Public API ==================
        /// <summary> 커서/채굴기 측에서 호출: dps * dt 만큼 HP를 줄인다. </summary>
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

        // ================== Death / Reward ==================
        private void OnDeath()
        {
            // 보상 지급
            if (rewards != null && rewards.Count > 0)
            {
                foreach (var r in rewards)
                {
                    int minA = Mathf.Min(r.minAmount, r.maxAmount);
                    int maxA = Mathf.Max(r.minAmount, r.maxAmount);
                    int amt = UnityEngine.Random.Range(minA, maxA + 1);
                    if (amt <= 0) continue;

                    var rm = RewardManager.SafeInstance;
                    if (rm != null) rm.Add(r.type, amt);

                    OnDestroyedWithReward?.Invoke(r.type, amt);
                }
            }

            if (destroyOnDeath) Destroy(gameObject);
            else gameObject.SetActive(false);
        }

        // ================== HP Bar ==================
        private void BuildHpBar()
        {
            if (!showHpBar)
            {
                DestroyHpBar();
                return;
            }
            if (_barRoot != null) return;

            _barRoot = new GameObject("HPBar").transform;
            _barRoot.SetParent(transform, false);

            _barBack = new GameObject("Back").AddComponent<SpriteRenderer>();
            _barFill = new GameObject("Fill").AddComponent<SpriteRenderer>();
            _barBack.transform.SetParent(_barRoot, false);
            _barFill.transform.SetParent(_barRoot, false);

            int baseOrder = _sr ? _sr.sortingOrder : 0;
            int layerId = _sr ? _sr.sortingLayerID : 0;

            _barBack.sortingLayerID = layerId; _barFill.sortingLayerID = layerId;
            _barBack.sortingOrder = baseOrder + 10; // 광석보다 앞으로
            _barFill.sortingOrder = baseOrder + 11;

            _barBack.color = barBackColor;
            _barFill.color = barFillColor;

            // 배치: 수동이 0이면 자동 추정(광석 아래쪽)
            float yOff = barYOffset;
            if (Mathf.Approximately(yOff, 0f) && _sr && _sr.sprite)
                yOff = -(_sr.bounds.extents.y + 0.12f);
            _barRoot.localPosition = new Vector3(0f, yOff, 0f);

            UpdateHpBar();
        }

        private void DestroyHpBar()
        {
            if (_barBack != null) Destroy(_barBack.gameObject);
            if (_barFill != null) Destroy(_barFill.gameObject);
            if (_barRoot != null) Destroy(_barRoot.gameObject);
            _barBack = null; _barFill = null; _barRoot = null;
        }

        private void UpdateHpBar()
        {
            if (_barRoot == null) return;

            var quad = Sprite.Create(Texture2D.whiteTexture,
                                     new Rect(0, 0, 1, 1),
                                     new Vector2(0.5f, 0.5f));

            if (_barBack != null)
            {
                _barBack.sprite = quad;
                _barBack.transform.localScale = new Vector3(barWidth, barHeight, 1f);
                _barBack.transform.localPosition = Vector3.zero;
            }

            if (_barFill != null)
            {
                _barFill.sprite = quad;

                float t = Mathf.Clamp01(_hp / Mathf.Max(0.0001f, maxHP));
                _barFill.transform.localScale = new Vector3(barWidth * t, barHeight, 1f);
                _barFill.transform.localPosition = new Vector3(-(barWidth * (1f - t)) * 0.5f, 0f, 0f);
            }
        }
    }
}
