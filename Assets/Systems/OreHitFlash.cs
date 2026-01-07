using System.Collections;
using UnityEngine;

namespace Pulseforge.Systems
{
    [DisallowMultipleComponent]
    public class OreHitFlash : MonoBehaviour
    {
        [Header("Auto Setup")]
        [Tooltip("비어있으면 자동으로 FlashOverlay 자식을 만들고 SpriteRenderer를 붙임")]
        [SerializeField] private SpriteRenderer flashOverlay;

        [Tooltip("기준이 되는 원본 스프라이트(비어있으면 같은 오브젝트에서 자동 탐색)")]
        [SerializeField] private SpriteRenderer sourceSprite;

        [Tooltip("오버레이가 원본보다 얼마나 앞에 그려질지")]
        [SerializeField] private int sortingOrderOffset = 50;

        [Tooltip("오버레이에 적용할 머터리얼(권장: Additive). 비워두면 Sprites-Default 그대로 사용")]
        [SerializeField] private Material flashMaterial;

        [Header("Flash")]
        [SerializeField] private float flashDuration = 0.06f;
        [Range(0f, 1f)]
        [SerializeField] private float flashAlpha = 1.0f;

        [Header("Colors")]
        [SerializeField] private Color normalHitColor = Color.white;
        [SerializeField] private Color criticalHitColor = new Color(1f, 0.55f, 0.1f, 1f);

        private Coroutine co;

        private void Awake()
        {
            EnsureSetup();
            SetOverlayAlpha(0f);
        }

        private void Reset()
        {
            // 인스펙터에서 컴포넌트 붙일 때도 자동으로 잡히게
            EnsureSetup();
            SetOverlayAlpha(0f);
        }

        private void EnsureSetup()
        {
            // 1) sourceSprite 찾기 (루트에 붙어있는 구조 지원)
            if (sourceSprite == null)
                sourceSprite = GetComponent<SpriteRenderer>();

            // 2) flashOverlay가 없으면 생성
            if (flashOverlay == null)
            {
                Transform t = transform.Find("FlashOverlay");
                if (t == null)
                {
                    var go = new GameObject("FlashOverlay");
                    go.transform.SetParent(transform, false);
                    t = go.transform;
                }

                flashOverlay = t.GetComponent<SpriteRenderer>();
                if (flashOverlay == null)
                    flashOverlay = t.gameObject.AddComponent<SpriteRenderer>();
            }

            if (sourceSprite == null || flashOverlay == null) return;

            // 3) 오버레이 기본 세팅: 원본과 완전히 동일한 스프라이트/정렬/트랜스폼
            flashOverlay.sprite = sourceSprite.sprite;
            flashOverlay.flipX = sourceSprite.flipX;
            flashOverlay.flipY = sourceSprite.flipY;

            flashOverlay.sortingLayerID = sourceSprite.sortingLayerID;
            flashOverlay.sortingOrder = sourceSprite.sortingOrder + sortingOrderOffset;

            flashOverlay.drawMode = SpriteDrawMode.Simple;

            // 위치/스케일: 자식이므로 (0,0) 고정이면 원본과 겹쳐짐
            flashOverlay.transform.localPosition = Vector3.zero;
            flashOverlay.transform.localRotation = Quaternion.identity;
            flashOverlay.transform.localScale = Vector3.one;

            // 머터리얼(선택)
            if (flashMaterial != null)
                flashOverlay.sharedMaterial = flashMaterial;

            // 원본이 바뀌면(예: 스프라이트 교체) 오버레이도 따라가게
            // → 매 프레임까지는 과한데, 최소한 Awake/Reset에서 맞춰줌.
        }

        public void PlayNormal()
        {
            Play(normalHitColor);
        }

        public void PlayCritical()
        {
            Play(criticalHitColor);
        }

        private void Play(Color c)
        {
            EnsureSetup();
            if (flashOverlay == null || sourceSprite == null) return;

            // 원본 스프라이트가 런타임에 바뀔 수 있으니 여기서도 한번 동기화
            if (flashOverlay.sprite != sourceSprite.sprite)
                flashOverlay.sprite = sourceSprite.sprite;

            if (co != null) StopCoroutine(co);
            co = StartCoroutine(CoFlash(c));
        }

        private IEnumerator CoFlash(Color c)
        {
            // 켜기
            c.a = flashAlpha;
            flashOverlay.color = c;

            // 타임스케일 영향 없이 확실히 보이게(원하면 WaitForSeconds로 바꿔도 됨)
            yield return new WaitForSecondsRealtime(flashDuration);

            // 끄기
            SetOverlayAlpha(0f);

            co = null;
        }

        private void SetOverlayAlpha(float a)
        {
            if (!flashOverlay) return;

            var c = flashOverlay.color;
            c.a = a;
            flashOverlay.color = c;
        }
    }
}
