using System.Collections.Generic;
using PrefabBuilder;
using UnityEditor;
using UnityEngine;

namespace TrackKit.EditorTools
{
    /// <summary>
    /// 3. Track Builder window. "Place Next": click a piece and it is attached to the open end of the selected
    /// piece, socket to socket, so bends and slopes join exactly. Pieces land as direct children
    /// of PREFAB_LAYOUT with PlacedPrefabIdentity, so Prefab Builder SAVE/LOAD works on the result
    /// with no change to the Prefab Builder package.
    /// </summary>
    public sealed class TrackKitWindow : EditorWindow
    {
        bool flip;
        Vector2 scroll;
        string lastMessage = "";

        [MenuItem("Tools/TrackKit/3. Track Builder Window", false, 310)]
        public static void Open()
        {
            GetWindow<TrackKitWindow>("TrackKit").Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Kit", EditorStyles.boldLabel);
            if (GUILayout.Button("1. Build Kit")) TrackKitKitBuilder.BuildKitMenu();
            if (GUILayout.Button("2. Validate Kit")) TrackKitValidateKit.ValidateMenu();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Place Next", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select a placed piece (or nothing, to start), then click a piece to attach it to the open end.\n" +
                                    "Flip = use the piece the other way round: bends turn left instead of right, the incline goes up instead of down.", MessageType.None);
            flip = EditorGUILayout.ToggleLeft("Flip (turn left / go up)", flip);

            List<GameObject> kit = TrackKitKitBuilder.LoadKit();
            if (kit.Count == 0) EditorGUILayout.HelpBox("No kit yet. Click 1. Build Kit.", MessageType.Warning);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(220));
            foreach (GameObject prefab in kit)
            {
                TrackPiece p = prefab.GetComponent<TrackPiece>();
                string label = prefab.name + "   (" + p.kind + (p.turnDegrees != 0f ? ", " + Mathf.Abs(p.turnDegrees).ToString("0") + " deg" : "") +
                               (Mathf.Abs(p.rise) > 0.05f ? ", " + Mathf.Abs(p.rise).ToString("0.0") + " m" : "") + ")";
                if (GUILayout.Button(label)) lastMessage = PlaceNext(prefab, flip);
            }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(lastMessage)) EditorGUILayout.HelpBox(lastMessage, MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Check and bake", EditorStyles.boldLabel);
            if (GUILayout.Button("4. Check Joints")) TrackKitSceneTools.CheckJointsMenu();
            if (GUILayout.Button("6. Bake Race")) TrackKitSceneTools.BakeMenu();

            ITrackKitAdapter adapter = TrackKitAdapters.Active;
            EditorGUILayout.Space();
            if (adapter == null)
            {
                EditorGUILayout.HelpBox("No game adapter in this project: Bake is disabled. See the README, 'Writing an adapter'.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(adapter.DisplayName, EditorStyles.boldLabel);
                if (adapter.Actions != null)
                    foreach (TrackKitAction action in adapter.Actions)
                        if (GUILayout.Button(action.label)) action.run();
            }
            EditorGUILayout.Space();
            if (GUILayout.Button("Settings")) Selection.activeObject = TrackKitSettings.Get();
        }

        /// <summary>Instantiates prefab and attaches it to the open socket of the selected piece.</summary>
        public static string PlaceNext(GameObject prefab, bool flipped)
        {
            if (EditorApplication.isPlaying) return "Stop Play mode first.";
            GameObject root = TrackKitSceneTools.GetOrCreateLayoutRoot();

            TrackPiece previous = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<TrackPiece>() : null;
            if (previous != null && previous.transform.parent != root.transform) previous = null;
            Transform open = previous != null ? OpenSocket(previous, root) : null;
            if (previous != null && open == null) return "'" + previous.name + "' is joined at both ends. Select a piece with a free end.";

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            Undo.RegisterCreatedObjectUndo(go, "TrackKit Place " + prefab.name);
            TrackPiece piece = go.GetComponent<TrackPiece>();
            var identity = go.GetComponent<PlacedPrefabIdentity>();
            if (identity != null && string.IsNullOrEmpty(identity.pieceId)) identity.pieceId = prefab.name;

            if (open == null)
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                Selection.activeGameObject = go;
                return "Placed " + prefab.name + " at the origin of " + TrackKitSceneTools.LayoutRootName + ".";
            }

            Transform inSocket = flipped ? piece.exit : piece.entry;
            Vector3 want = -TrackChain.Flat(open.forward);
            Vector3 have = TrackChain.Flat(inSocket.forward);
            float yaw = Vector3.SignedAngle(have, want, Vector3.up);
            go.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * go.transform.rotation;
            go.transform.position += open.position - inSocket.position;
            go.transform.SetSiblingIndex(previous.transform.GetSiblingIndex() + 1);
            Selection.activeGameObject = go;
            return "Attached " + prefab.name + (flipped ? " (flipped)" : "") + " to '" + previous.name + "'. Joint offset " +
                   Vector3.Distance(open.position, inSocket.position).ToString("F3") + " m.";
        }

        /// <summary>The socket of p that no other piece is touching. Prefers the exit (or the start gate's direction).</summary>
        static Transform OpenSocket(TrackPiece p, GameObject root)
        {
            bool entryUsed = false, exitUsed = false;
            foreach (Transform child in root.transform)
            {
                TrackPiece o = child.GetComponent<TrackPiece>();
                if (o == null || o == p || o.entry == null || o.exit == null) continue;
                if (Touches(p.entry, o)) entryUsed = true;
                if (Touches(p.exit, o)) exitUsed = true;
            }
            if (!entryUsed && !exitUsed)
            {
                foreach (TrackGateMarker m in p.GetComponentsInChildren<TrackGateMarker>(true))
                    if (m.isStartGate && Vector3.Dot(p.entry.forward, m.transform.forward) > 0f) return p.entry;
                return p.exit;
            }
            if (!exitUsed) return p.exit;
            if (!entryUsed) return p.entry;
            return null;
        }

        static bool Touches(Transform socket, TrackPiece other)
        {
            return Vector3.Distance(socket.position, other.entry.position) <= TrackKitSettings.Get().joinTolerance ||
                   Vector3.Distance(socket.position, other.exit.position) <= TrackKitSettings.Get().joinTolerance;
        }
    }
}
