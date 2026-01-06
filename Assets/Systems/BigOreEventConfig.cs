using UnityEngine;

namespace Pulseforge.Systems
{
    /// <summary>
    /// BigOre 프리팹 단에서 이벤트 타임(베이스/맥스)을 설정하기 위한 컴포넌트.
    /// BigOreSpawner가 스폰 직후 읽어서 BigOreEventController에 전달한다.
    /// </summary>
    public class BigOreEventConfig : MonoBehaviour
    {
        [Header("Event Duration")]
        [Min(0.1f)] public float baseEventDurationSec = 6.0f;
        [Min(0.1f)] public float maxEventDurationSec = 10.0f;

        private void OnValidate()
        {
            if (maxEventDurationSec < baseEventDurationSec)
                maxEventDurationSec = baseEventDurationSec;
        }
    }
}
