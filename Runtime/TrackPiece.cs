using UnityEngine;

namespace TrackKit
{
    /// <summary>
    /// Marks a road prefab as a TrackKit piece. Data only: no Update, no Awake logic.
    ///
    /// Child layout (made by Tools/TrackKit/1. Build Kit):
    ///   entry, exit   sockets. Forward (+Z) points OUT of the piece. The next piece's socket
    ///                 sits on the same point, facing the opposite way.
    ///   gates/*       TrackGateMarker, in order from entry to exit.
    ///   walls/*       solid colliders with the wall tag, not on the floor layer.
    ///   spawns/*      TrackSpawnMarker (start piece only, 7 of them).
    ///
    /// A piece can be used either way round ("reversed"): a bend then turns the other way and an
    /// incline goes down instead of up. Direction is worked out from how pieces touch, so nothing
    /// about it has to be stored, and Prefab Builder SAVE/LOAD keeps working unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrackPiece : MonoBehaviour
    {
        public enum PieceKind { Straight, Bend, Incline, Start, Other }

        public PieceKind kind = PieceKind.Straight;
        public Transform entry;
        public Transform exit;

        [Tooltip("Drivable half-width at the centre of the piece (curb to centre), metres.")]
        public float roadHalfWidth = 6.5f;

        [Tooltip("Signed turn from entry to exit, degrees, measured when the kit was built. 0 for straights.")]
        public float turnDegrees;

        [Tooltip("Height change from entry to exit, metres, measured when the kit was built.")]
        public float rise;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            DrawSocket(entry, new Color(0.2f, 0.9f, 0.3f));
            DrawSocket(exit, new Color(0.95f, 0.3f, 0.2f));
        }

        static void DrawSocket(Transform socket, Color color)
        {
            if (socket == null)
                return;
            Gizmos.color = color;
            Gizmos.DrawSphere(socket.position, 0.6f);
            Gizmos.DrawLine(socket.position, socket.position + socket.forward * 4f);
        }
#endif
    }
}
