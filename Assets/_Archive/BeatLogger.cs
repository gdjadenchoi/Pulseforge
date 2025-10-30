using UnityEngine;

namespace Pulseforge.Core
{
    public class BeatLogger : MonoBehaviour
    {
        public void LogBeat()
        {
            Debug.Log("Beat!");
        }

        public void LogHalf()
        {
            Debug.Log("Half Beat");
        }
    }
}
