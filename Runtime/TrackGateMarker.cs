using UnityEngine;

namespace TrackKit
{
    /// <summary>
    /// Where a waypoint gate goes on a piece, plus the per-gate AI defaults for that piece type.
    /// The marker is NOT a gate. Bake Race (and later a runtime assembler) turns markers into
    /// real game gates through the project's adapter. Field names and defaults follow Robot Race's wpAttribs.
    /// Forward (+Z) points from the piece's entry towards its exit.
    /// </summary>
    public sealed class TrackGateMarker : MonoBehaviour
    {
        [Tooltip("This gate becomes waypoint_0 (start/finish). Exactly one in a track.")]
        public bool isStartGate;

        [Tooltip("0 = use the piece's roadHalfWidth.")]
        public float segmentWidth;
        public float collWidth = 4f;
        public float collOffset;
        public float segmentHeight = 10f;

        [Range(-1f, 1f)] public float optimalOffset;
        public float speedAdjust = 1f;
        public float switchDist = 5f;
        public float aiMulti = 1f;
        public float wpTurnMulti = 1f;
        [Tooltip("0 none, 1 force plane, 2 force car")]
        public int autoTransform;
        public bool doJump;
        public bool abyss;
        public float missleProbability = 0.15f;
        public float mineProbability = 0.15f;
        public float juiceProbability = 0.8f;
        public float dropTimer = 4f;
        public float airTimer = 5f;

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = isStartGate ? Color.yellow : new Color(0.3f, 0.7f, 1f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 0.3f));
            Gizmos.DrawLine(Vector3.up * 1.5f, Vector3.up * 1.5f + Vector3.forward * 3f);
        }
#endif
    }
}
