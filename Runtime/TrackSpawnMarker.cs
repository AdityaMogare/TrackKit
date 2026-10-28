using UnityEngine;

namespace TrackKit
{
    /// <summary>
    /// One start-grid slot on the start piece. slot is the start-grid index (0 = front); the adapter decides how slots map to the game.
    /// Forward (+Z) is the direction the robot faces.
    /// </summary>
    public sealed class TrackSpawnMarker : MonoBehaviour
    {
        public int slot;

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, 0.8f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2.5f);
        }
#endif
    }
}
