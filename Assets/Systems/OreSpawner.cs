using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 균등 분포 스폰:
    /// - 모든 후보 위치를 생성 → Fisher-Yates 셔플 → quota만큼 순회 배치.
    /// - Pass: GridCenters(무겹침) → Edge → Corner → RandomFill(살짝 겹침 허용)
    /// - 성장: n초마다 k개씩 목표치 증가 (풀 리스폰/증분 둘 다 지원)
    /// </summary>
    public class OreSpawner : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Transform oreRoot;         // 없으면 자동 생성
        [SerializeField] private GameObject orePrefab;

        [Header("Spawn Base")]
        [SerializeField] private int initialCount = 12;
        [SerializeField] private int maxCount = 300;
        [SerializeField] private float borderPadding = 0.5f;
        [SerializeField] private float extraPadding = 0.05f;

        [Header("Grid")]
        [SerializeField] private float cellX = 0.9f;
        [SerializeField] private float cellY = 0.9f;
        [SerializeField] private float jitter = 0.12f;

        [Header("Overfill Layers")]
        [Range(0.5f, 1.2f)] [SerializeField] private float minRadiusFactor = 0.9f;
        [SerializeField] private bool enableEdgeLayer = true;
        [SerializeField] private bool enableCornerLayer = true;
        [SerializeField] private bool enableRandomFill = true;

        [Header("Growth (Play Mode)")]
        [SerializeField] private bool enableGrowth = true;
        [SerializeField] private float increaseIntervalSec = 10f;
        [SerializeField] private int increaseBy = 6;
        [SerializeField] private bool fullRespawnOnGrowth = true;

        [Header("Debug")]
        [SerializeField] private bool autoRespawnOnStart = true;

        // runtime
        private Rect _worldRect;
        private float _oreRadius = 0.45f;
        private int _targetCount;
        private float _nextTick;
        private int _rngSeedSalt; // 프레임 간 분포 다양화

        void Reset() { TryAutoWire(); TryInferRadius(); }
        void Awake() { TryAutoWire(); TryInferRadius(); }

        void Start()
        {
            BuildWorldRect();
            _targetCount = Mathf.Clamp(initialCount, 0, maxCount);
            if (autoRespawnOnStart) Respawn(_targetCount);
            if (enableGrowth) _nextTick = Time.time + Mathf.Max(0.1f, increaseIntervalSec);
        }

        void Update()
        {
            if (!enableGrowth) return;
            if (Time.time >= _nextTick)
            {
                _nextTick += Mathf.Max(0.1f, increaseIntervalSec);
                int newTarget = Mathf.Min(maxCount, _targetCount + Mathf.Max(0, increaseBy));
                if (newTarget != _targetCount)
                {
                    _targetCount = newTarget;
                    if (fullRespawnOnGrowth) Respawn(_targetCount);
                    else TopUpTo(_targetCount);
                }
            }
        }

        [ContextMenu("Respawn (to current target)")]
        public void RespawnContext() => Respawn(Mathf.Max(0, _targetCount));

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (!oreRoot) return;
            for (int i = oreRoot.childCount - 1; i >= 0; --i)
                Destroy(oreRoot.GetChild(i).gameObject);
        }

        // ---------------- core ----------------
        private void TryAutoWire()
        {
            if (!targetCamera) targetCamera = Camera.main;
            if (!oreRoot)
            {
                var t = transform.Find("_OreRoot");
                oreRoot = t ? t as Transform : new GameObject("_OreRoot").transform;
                oreRoot.SetParent(transform, false);
            }
        }

        private void TryInferRadius()
        {
            if (!orePrefab) return;
            var col = orePrefab.GetComponent<CircleCollider2D>();
            if (col) _oreRadius = Mathf.Abs(col.radius) * orePrefab.transform.lossyScale.x;
        }

        private void BuildWorldRect()
        {
            if (!targetCamera) return;
            float halfH = targetCamera.orthographicSize;
            float halfW = halfH * targetCamera.aspect;
            Vector2 c = targetCamera.transform.position;
            _worldRect = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
        }

        private Rect InnerRect() => new Rect(
            _worldRect.xMin + borderPadding,
            _worldRect.yMin + borderPadding,
            _worldRect.width - borderPadding * 2f,
            _worldRect.height - borderPadding * 2f
        );

        private void Respawn(int target)
        {
            Clear();
            BuildWorldRect();
            InternalSpawn(target, incremental: false);
        }

        private void TopUpTo(int target)
        {
            BuildWorldRect();
            int have = oreRoot.childCount;
            if (have >= target) return;
            InternalSpawn(target - have, incremental: true);
        }

        private void InternalSpawn(int need, bool incremental)
        {
            if (!orePrefab || !oreRoot || !targetCamera) return;
            if (need <= 0) return;

            Rect inner = InnerRect();

            float diameter = Mathf.Max(_oreRadius * 2f, 0.1f);
            float cellW = Mathf.Max(diameter + extraPadding, cellX);
            float cellH = Mathf.Max(diameter + extraPadding, cellY);

            int cols = Mathf.Max(1, Mathf.FloorToInt(inner.width / cellW));
            int rows = Mathf.Max(1, Mathf.FloorToInt(inner.height / cellH));

            // Pass A: Grid centers (no overlap)
            need -= SpawnFromCandidates(BuildGridCenters(inner, cols, rows, jitter),
                                        need,
                                        _oreRadius * 0.98f);

            // Pass B: Edge layer
            if (need > 0 && enableEdgeLayer)
                need -= SpawnFromCandidates(BuildEdgeCandidates(inner, cols, rows, jitter * 0.7f),
                                            need,
                                            _oreRadius * minRadiusFactor);

            // Pass C: Corner layer
            if (need > 0 && enableCornerLayer)
                need -= SpawnFromCandidates(BuildCornerCandidates(inner, cols, rows, jitter * 0.6f),
                                            need,
                                            _oreRadius * minRadiusFactor);

            // Pass D: Random fill (slight overlap allowed)
            if (need > 0 && enableRandomFill)
                need -= SpawnFromCandidates(BuildRandomCandidates(inner, need * 10),
                                            need,
                                            _oreRadius * Mathf.Clamp(minRadiusFactor * 0.85f, 0.45f, 1.0f));
        }

        // -------------- candidates & spawn --------------
        private int SpawnFromCandidates(List<Vector2> candidates, int quota, float overlapRadius)
        {
            if (candidates == null || candidates.Count == 0 || quota <= 0) return 0;

            // Fisher–Yates shuffle (seed salt로 프레임 간 다양화)
            Shuffle(candidates);

            int placed = 0;
            int layer = LayerMask.GetMask("Ore");
            foreach (var pos in candidates)
            {
                if (placed >= quota) break;
                if (Physics2D.OverlapCircle(pos, overlapRadius, layer) != null) continue;
                Instantiate(orePrefab, pos, Quaternion.identity, oreRoot);
                placed++;
            }
            _rngSeedSalt++; // 다음 호출 셔플 다양화
            return placed;
        }

        private List<Vector2> BuildGridCenters(Rect inner, int cols, int rows, float j)
        {
            var list = new List<Vector2>(cols * rows);
            float stepX = inner.width / cols;
            float stepY = inner.height / rows;

            // 모든 셀의 중심을 후보로
            for (int r = 0; r < rows; ++r)
                for (int c = 0; c < cols; ++c)
                {
                    Vector2 center = new Vector2(
                        inner.xMin + (c + 0.5f) * stepX,
                        inner.yMin + (r + 0.5f) * stepY
                    );
                    // 셀 내부 소폭 흔들기
                    center += new Vector2(
                        RandomRangeSym(j),
                        RandomRangeSym(j)
                    );
                    list.Add(center);
                }
            return list;
        }

        private List<Vector2> BuildEdgeCandidates(Rect inner, int cols, int rows, float j)
        {
            var list = new List<Vector2>(cols * rows);
            float stepX = inner.width / cols;
            float stepY = inner.height / rows;

            for (int r = 0; r < rows; ++r)
                for (int c = 0; c < cols; ++c)
                {
                    Vector2 baseCenter = new Vector2(
                        inner.xMin + (c + 0.5f) * stepX,
                        inner.yMin + (r + 0.5f) * stepY
                    );

                    bool useVertical = ((r + c) & 1) == 0;
                    Vector2 pos = baseCenter;
                    if (useVertical) pos.x += (Random.value < 0.5f ? -0.5f : 0.5f) * (stepX * 0.48f);
                    else pos.y += (Random.value < 0.5f ? -0.5f : 0.5f) * (stepY * 0.48f);

                    pos += new Vector2(RandomRangeSym(j), RandomRangeSym(j));
                    list.Add(pos);
                }
            return list;
        }

        private List<Vector2> BuildCornerCandidates(Rect inner, int cols, int rows, float j)
        {
            var list = new List<Vector2>(cols * rows);
            float stepX = inner.width / cols;
            float stepY = inner.height / rows;

            for (int r = 0; r < rows; ++r)
                for (int c = 0; c < cols; ++c)
                {
                    Vector2 baseCenter = new Vector2(
                        inner.xMin + (c + 0.5f) * stepX,
                        inner.yMin + (r + 0.5f) * stepY
                    );

                    float sx = ((c & 1) == 0) ? 0.25f : -0.25f;
                    float sy = ((r & 1) == 0) ? -0.25f : 0.25f;

                    Vector2 pos = baseCenter + new Vector2(stepX * sx, stepY * sy);
                    pos += new Vector2(RandomRangeSym(j), RandomRangeSym(j));
                    list.Add(pos);
                }
            return list;
        }

        private List<Vector2> BuildRandomCandidates(Rect inner, int count)
        {
            var list = new List<Vector2>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new Vector2(
                    Random.Range(inner.xMin, inner.xMax),
                    Random.Range(inner.yMin, inner.yMax)
                ));
            }
            return list;
        }

        // -------------- utils --------------
        private void Shuffle(List<Vector2> list)
        {
            // frame/seed salt를 섞어 반복 실행에도 패턴화 방지
            int n = list.Count;
            int seed = (int)(Time.frameCount * 73856093 ^ _rngSeedSalt * 19349663);
            System.Random rng = new System.Random(seed);
            while (n > 1)
            {
                int k = rng.Next(n--);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }

        private static float RandomRangeSym(float a) => (Random.value * 2f - 1f) * a;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!targetCamera) return;
            BuildWorldRect();
            Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
            Gizmos.DrawCube(_worldRect.center, _worldRect.size);

            Rect inner = InnerRect();
            Gizmos.color = new Color(1f, 1f, 0f, 0.18f);
            Gizmos.DrawCube(inner.center, inner.size);
        }
#endif
    }
}
