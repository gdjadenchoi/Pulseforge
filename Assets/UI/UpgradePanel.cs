using System;
using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// 업그레이드 리스트 패널
    /// - 지정한 UpgradeDefinition 배열을 기반으로
    ///   UpgradeEntry 프리팹을 자동 생성해서 버튼/텍스트를 세팅해준다.
    /// </summary>
    public class UpgradePanel : MonoBehaviour
    {
        //==============================================================
        // 인스펙터 설정
        //==============================================================

        [Header("생성 설정")]
        [Tooltip("생성된 업그레이드 행(엔트리)들이 들어갈 부모")]
        [SerializeField] private RectTransform contentRoot;

        [Tooltip("한 줄(행)으로 사용할 프리팹. 반드시 UpgradeEntry 컴포넌트가 있어야 한다.")]
        [SerializeField] private UpgradeEntry rowPrefab;

        [Header("표시할 업그레이드 정의들")]
        [Tooltip("이 배열에 들어있는 UpgradeDefinition들을 순서대로 표시한다.")]
        [SerializeField] private UpgradeDefinition[] upgradeDefinitions = Array.Empty<UpgradeDefinition>();

        //==============================================================
        // Unity 라이프사이클
        //==============================================================

        private void Awake()
        {
            if (contentRoot == null)
            {
                Debug.LogError("[UpgradePanel] contentRoot 가 설정되지 않았습니다.", this);
            }

            if (rowPrefab == null)
            {
                Debug.LogError("[UpgradePanel] rowPrefab 이 설정되지 않았습니다.", this);
            }
        }

        private void Start()
        {
            BuildRows();
        }

        //==============================================================
        // 내부 로직
        //==============================================================

        /// <summary>
        /// contentRoot 아래에 기존 자식을 비우고,
        /// upgradeDefinitions 배열을 기반으로 행을 다시 만든다.
        /// </summary>
        private void BuildRows()
        {
            if (contentRoot == null || rowPrefab == null)
                return;

            // 1) 기존 자식 제거
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                var child = contentRoot.GetChild(i);
#if UNITY_EDITOR
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
#else
                Destroy(child.gameObject);
#endif
            }

            // 2) 정의 배열 기반으로 새 엔트리 생성
            if (upgradeDefinitions == null || upgradeDefinitions.Length == 0)
                return;

            foreach (var def in upgradeDefinitions)
            {
                if (def == null)
                    continue;

                // 프리팹 인스턴스 생성
                var entryObj = Instantiate(rowPrefab.gameObject, contentRoot);

                // Attach 된 UpgradeEntry 가져오기
                var entry = entryObj.GetComponent<UpgradeEntry>();
                if (entry == null)
                {
                    Debug.LogError(
                        "[UpgradePanel] rowPrefab 인스턴스에 UpgradeEntry 컴포넌트가 없습니다.",
                        entryObj);
                    continue;
                }

                // 이 Definition으로 행 세팅
                entry.Setup(def);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (upgradeDefinitions == null)
                upgradeDefinitions = Array.Empty<UpgradeDefinition>();
        }
#endif
    }
}
