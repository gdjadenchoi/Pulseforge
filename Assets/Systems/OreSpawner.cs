using System.Collections.Generic;
using UnityEngine;

namespace Pulseforge.Systems
{
    public class OreSpawner : MonoBehaviour
    {
        // ====== Inspector ======
        [Header("Prefabs")]
        [SerializeField] private Ore orePrefab;

        [Header("Grid Size (count)")]
        public int columns = 12;
        public int rows = 7;

        public enum TileSizeSource { UsePrefabSize, ManualTileSize }

        [Header("Tile Size")]
        public TileSizeSource tileSizeSource = TileSizeSource.UsePrefabSize;
        public Vector2 manualTileSize = new(1f, 1f);

        [Header("Spacing")]
        public float gapX = 0.06f;
        public float gapY = 0.06f;
        public Vector2 jitter = new(0.02f, 0.02f);

        [Header("Layout")]
        public bool useRowStagger = true;
        [Range(0f, 1f)] public float fillChance = 0.35f;

        [Header("Overlap Check")]
        public string oreLayer = "Ore";
        public bool preventOverlap = true;

        [Header("Refill")]
        [Tooltip("이 값보다 적게 남으면 보충 시도")]
        public int minAliveToRefill = 30;
        [Tooltip("보충 체크 주기(초)")]
        public float refillInterval = 1.0f;

        // ====== Runtime ======
        private readonly List<Vector3> slots = new();
        private float nextRefill;
        private int oreLayerIndex = -1;
        private float effectiveRadius = 0.3f; // 슬롯 충돌 반경

        // ====== Unity ======
        private void Start()
        {
            oreLayerIndex = LayerMask.NameToLayer(oreLayer);
            if (oreLayerIndex < 0)
                Debug.LogWarning($"[OreSpawner] Layer '{oreLayer}' not found. Overlap mask will be 'Everything'.");

            BuildSlots();
            SpawnInitial();
            nextRefill = Time.time + refillInterval;
        }

        private void Update()
        {
            if (Time.time >= nextRefill)
            {
                nextRefill = Time.time + refillInterval;
                RefillIfNeeded();
            }
        }

        // ====== Build & Spawn ======
        private void BuildSlots()
        {
            slots.Clear();

            Vector2 unitSize = GetTileSize();
            float w = unitSize.x + gapX;
            float h = unitSize.y + gapY;

            // 슬롯 충돌 반경(타일 크기 기반) 설정
            effectiveRadius = Mathf.Min(unitSize.x, unitSize.y) * 0.48f;

            var cam = Camera.main;
            var lb = cam.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
            var rt = cam.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));

            float margin = 0.6f;
            float startX = lb.x + margin;
            float startY = rt.y - margin;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    float x = startX + c * w;
                    if (useRowStagger && (r % 2 == 1)) x += w * 0.5f;

                    float y = startY - r * h;
                    slots.Add(new Vector3(x, y, 0f));
                }
            }
        }

        private void SpawnInitial()
        {
            foreach (var pos in slots)
            {
                if (Random.value > fillChance) continue;
                TrySpawnAt(pos);
            }
        }

        private void RefillIfNeeded()
        {
            int alive = CountAlive();
            if (alive >= minAliveToRefill) return;

            // 시작 인덱스를 랜덤으로 — 항상 좌상단부터 채우는 현상 방지
            int start = Random.Range(0, slots.Count);

            for (int i = 0; i < slots.Count && alive < minAliveToRefill; i++)
            {
                int idx = (start + i) % slots.Count;
                var slot = slots[idx];

                if (!HasOreNear(slot, effectiveRadius) && TrySpawnAt(slot))
                    alive++;
            }
        }

        // ====== Helpers ======
        private int CountAlive()
        {
            int cnt = 0;
            foreach (Transform child in transform)
                if (child != null) cnt++;
            return cnt;
        }

        private bool TrySpawnAt(Vector3 slotPos)
        {
            if (!orePrefab) return false;

            // 슬롯 기준으로 약간의 지터
            Vector3 pos = slotPos + (Vector3)new Vector2(
                Random.Range(-jitter.x, jitter.x),
                Random.Range(-jitter.y, jitter.y));

            if (preventOverlap && HasOreNear(pos, effectiveRadius))
                return false;

            var o = Instantiate(orePrefab, pos, Quaternion.identity, transform);
            if (!o) return false;

            // 안전장치: 프리팹과 모든 자식의 레이어를 Ore로 강제 세팅
            if (oreLayerIndex >= 0)
                SetLayerRecursively(o.gameObject, oreLayerIndex);

            return true;
        }

        private bool HasOreNear(Vector3 pos, float radius)
        {
            int mask = (oreLayerIndex < 0) ? ~0 : (1 << oreLayerIndex);
            var col = Physics2D.OverlapCircle(pos, radius, mask);
            return col != null;
        }

        private Vector2 GetTileSize()
        {
            if (tileSizeSource == TileSizeSource.ManualTileSize)
                return manualTileSize;

            if (orePrefab != null &&
                orePrefab.TryGetComponent<SpriteRenderer>(out var sr) &&
                sr.sprite != null)
                return sr.bounds.size;

            return new Vector2(1f, 1f);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
        }

        // 디버그: 씬에서 반경 확인용
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            foreach (var s in slots)
                Gizmos.DrawWireSphere(s, effectiveRadius);
        }
    }
}
