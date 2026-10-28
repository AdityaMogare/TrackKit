using System.Collections.Generic;
using System.IO;
using System.Text;
using PrefabBuilder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrackKit.EditorTools
{
    /// <summary>
    /// 4. Check Joints and 6. Bake Race. Check Joints is game-independent. Bake orders the
    /// pieces and markers, then hands them to the project's ITrackKitAdapter, which creates the
    /// game's own race objects (waypoints, spawns, race manager wiring).
    /// </summary>
    public static class TrackKitSceneTools
    {
        public const string LayoutRootName = "PREFAB_LAYOUT";

        // ------------------------------------------------------------------ helpers

        public static GameObject FindLayoutRoot(out int count)
        {
            GameObject found = null;
            count = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == LayoutRootName) { count++; if (found == null) found = t.gameObject; }
            }
            return found;
        }

        public static GameObject GetOrCreateLayoutRoot()
        {
            int count;
            GameObject root = FindLayoutRoot(out count);
            if (root != null) return root;
            root = new GameObject(LayoutRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + LayoutRootName);
            return root;
        }

        /// <summary>Direct children only, the same rule Prefab Builder SAVE uses.</summary>
        public static List<TrackPiece> PiecesUnder(GameObject root, TrackReport report)
        {
            var list = new List<TrackPiece>();
            foreach (Transform child in root.transform)
            {
                if (!child.gameObject.activeInHierarchy) continue;
                TrackPiece p = child.GetComponent<TrackPiece>();
                if (p != null) list.Add(p);
                else if (report != null && child.GetComponent<PlacedPrefabIdentity>() != null)
                    report.Error("'" + child.name + "' under " + LayoutRootName + " is not a TrackKit piece (no sockets or race data). Remove it or use CLEAR LAYOUT first.");
            }
            foreach (TrackPiece p in root.GetComponentsInChildren<TrackPiece>(true))
                if (p.transform.parent != root.transform && report != null)
                    report.Error("'" + p.name + "' is nested inside another object; Prefab Builder SAVE would not keep it.");
            return list;
        }

        static string Write(string name, string header, TrackReport r)
        {
            string text = header + "  " + System.DateTime.Now + "\n" + r;
            File.WriteAllText(TrackKitKitBuilder.ReportPath(name), text);
            if (r.Ok) Debug.Log("[TrackKit] " + text); else Debug.LogError("[TrackKit] " + text);
            return text;
        }

        // ------------------------------------------------------------------ Check Joints (week 2)

        [MenuItem("Tools/TrackKit/4. Check Joints", false, 320)]
        public static void CheckJointsMenu()
        {
            Write("CheckJoints", "TrackKit Check Joints, scene " + SceneManager.GetActiveScene().path, CheckJoints(out _));
        }

        public static TrackReport CheckJoints(out List<TrackChain.Link> chain)
        {
            var r = new TrackReport();
            chain = new List<TrackChain.Link>();
            int roots;
            GameObject root = FindLayoutRoot(out roots);
            if (root == null) { r.Error("No " + LayoutRootName + " in the open scene."); return r; }
            if (roots > 1) r.Error(roots + " objects are named " + LayoutRootName + "; Prefab Builder only uses the first.");

            List<TrackPiece> pieces = PiecesUnder(root, r);
            if (pieces.Count == 0) { r.Error("No TrackKit pieces under " + LayoutRootName + "."); return r; }
            bool closed;
            TrackKitSettings settings = TrackKitSettings.Get();
            chain = TrackChain.OrderChain(pieces, r, out closed, settings.joinTolerance);
            r.Note("Pieces " + pieces.Count + ", in chain " + chain.Count + ", loop closed " + closed);

            // Ground under centre and shoulders, stacked pieces, old road underneath.
            int floorMask = 1 << settings.floorLayer;
            Physics.SyncTransforms();
            int samples = 0, gaps = 0, stacked = 0, foreign = 0, walled = 0;
            var notes = new List<string>();
            for (int c = 0; c < chain.Count; c++)
            {
                var link = chain[c];
                var pts = new List<Vector3> { link.In.position };
                foreach (var m in TrackChain.MarkersInDrivingOrder(link)) pts.Add(m.transform.position);
                pts.Add(link.Out.position);
                float side = Mathf.Max(0f, link.piece.roadHalfWidth - 1.5f);

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    float len = Vector3.Distance(pts[i], pts[i + 1]);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
                    Vector3 right = Vector3.Cross(Vector3.up, TrackChain.Flat(pts[i + 1] - pts[i]));
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector3 centre = Vector3.Lerp(pts[i], pts[i + 1], s / (float)steps);
                        bool nearJoint = Vector3.Distance(centre, link.In.position) < 1.5f || Vector3.Distance(centre, link.Out.position) < 1.5f;
                        for (int k = -1; k <= 1; k++)
                        {
                            Vector3 p = centre + right * side * k;
                            samples++;
                            var ours = new List<RaycastHit>();
                            foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 30f, floorMask, QueryTriggerInteraction.Ignore))
                            {
                                if (h.collider.transform.IsChildOf(root.transform)) ours.Add(h);
                                else foreign++;
                            }
                            foreach (Collider w in Physics.OverlapSphere(p + Vector3.up * 1.2f, 0.4f, ~floorMask, QueryTriggerInteraction.Ignore))
                            {
                                if (!w.CompareTag(settings.wallTag)) continue;
                                walled++;
                                if (notes.Count < 10) notes.Add("wall '" + w.name + "' of '" + (w.GetComponentInParent<TrackPiece>() != null ? w.GetComponentInParent<TrackPiece>().name : w.name) + "' stands on the racing line at " + p.ToString("F1"));
                                break;
                            }
                            if (ours.Count == 0)
                            {
                                gaps++;
                                if (notes.Count < 10) notes.Add("gap at " + p.ToString("F1") + " on '" + link.piece.name + "'");
                                continue;
                            }
                            if (nearJoint) continue;
                            TrackPiece top = null;
                            float topY = float.NegativeInfinity;
                            foreach (var h in ours) if (h.point.y > topY) { topY = h.point.y; top = h.collider.GetComponentInParent<TrackPiece>(); }
                            foreach (var h in ours)
                            {
                                TrackPiece other = h.collider.GetComponentInParent<TrackPiece>();
                                if (other != top && other != null && Mathf.Abs(h.point.y - topY) < 3f)
                                {
                                    stacked++;
                                    if (notes.Count < 10) notes.Add("'" + other.name + "' overlaps '" + (top != null ? top.name : "?") + "' at " + p.ToString("F1"));
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            r.Note("Ground samples " + samples + ", gaps " + gaps + ", overlapping/stacked " + stacked + ", walls on the racing line " + walled + ", hits on Floor outside " + LayoutRootName + " " + foreign);
            foreach (string n in notes) r.Note("  " + n);
            if (gaps > 0) r.Error(gaps + " sample(s) with no road underneath.");
            if (stacked > 0) r.Error(stacked + " sample(s) where two pieces overlap.");
            if (walled > 0) r.Error(walled + " sample(s) where a wall stands on the road between the shoulders.");
            if (foreign > 0) r.Warn(foreign + " ray hit(s) on Floor colliders outside " + LayoutRootName + " (old road still active; Bake will refuse).");
            return r;
        }

        // ------------------------------------------------------------------ Bake Race

        [MenuItem("Tools/TrackKit/6. Bake Race", false, 331)]
        public static void BakeMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            var r = new TrackReport();
            Bake(scene, r);
            Write("Bake", "TrackKit Bake Race, scene " + scene.path, r);
        }

        public static void Bake(Scene scene, TrackReport r)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { r.Error("Stop Play mode first."); return; }
            TrackKitSettings settings = TrackKitSettings.Get();
            if (!string.IsNullOrEmpty(settings.bakeSceneFolder) && !scene.path.StartsWith(settings.bakeSceneFolder.TrimEnd('/') + "/"))
            {
                r.Error("Bake only runs in scenes under " + settings.bakeSceneFolder + "/ (TrackKit settings), so other levels are never changed.");
                return;
            }
            ITrackKitAdapter adapter = TrackKitAdapters.Active;
            if (adapter == null)
            {
                r.Error("No ITrackKitAdapter in this project. Bake needs one to create the game's race objects (see the TrackKit README, 'Writing an adapter').");
                return;
            }

            List<TrackChain.Link> chain;
            TrackReport joints = CheckJoints(out chain);
            foreach (string s in joints.Info) r.Note(s);
            foreach (string s in joints.Warnings) r.Error(s);   // old floor under the track is fatal for a bake
            foreach (string s in joints.Errors) r.Error(s);
            if (!r.Ok) { r.Note("Bake stopped: fix the joints above first."); return; }

            int roots;
            var context = new TrackBakeContext
            {
                scene = scene,
                settings = settings,
                layoutRoot = FindLayoutRoot(out roots),
                chain = chain,
                gates = TrackChain.CollectGates(chain, r),
                spawns = TrackChain.CollectSpawns(chain)
            };
            r.Note("Adapter: " + adapter.DisplayName);
            adapter.Bake(context, r);

            EditorSceneManager.MarkSceneDirty(scene);
            if (r.Ok) EditorSceneManager.SaveScene(scene);
            else r.Note("Scene NOT saved because validation failed. Undo (Ctrl/Cmd+Z) reverts the bake.");
        }
    }
}
