using System.IO;
using System.Text;
using PrefabBuilder;
using UnityEditor;
using UnityEngine;

namespace TrackKit.EditorTools
{
    /// <summary>
    /// 2. Validate Kit: checks every kit piece is raceable on its own (solid floor collider, walls,
    /// sockets on the road, gate markers, start grid on the start piece, Prefab Builder id).
    /// Read-only. Report: TrackKit_ValidateKit_Report.txt in the project folder.
    /// </summary>
    public static class TrackKitValidateKit
    {
        [MenuItem("Tools/TrackKit/2. Validate Kit", false, 301)]
        public static void ValidateMenu()
        {
            string text = Validate(out bool ok);
            File.WriteAllText(TrackKitKitBuilder.ReportPath("ValidateKit"), text);
            if (ok) Debug.Log("[TrackKit] " + text); else Debug.LogError("[TrackKit] " + text);
        }

        public static string Validate(out bool ok)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TrackKit Validate Kit  " + System.DateTime.Now);
            ok = true;
            var kit = TrackKitKitBuilder.LoadKit();
            if (kit.Count == 0)
            {
                ok = false;
                sb.AppendLine("No TrackKit pieces in " + TrackKitKitBuilder.KitFolder + ". Run Tools/TrackKit/1. Build Kit first.");
                return sb.ToString();
            }
            PrefabCatalog catalog = string.IsNullOrEmpty(TrackKitKitBuilder.CatalogPath) ? null : AssetDatabase.LoadAssetAtPath<PrefabCatalog>(TrackKitKitBuilder.CatalogPath);

            foreach (GameObject asset in kit)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                var r = new TrackReport();
                try
                {
                    Check(root, catalog, r);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                if (!r.Ok) ok = false;
                sb.AppendLine(asset.name + "  " + (r.Ok ? "OK" : "FAIL"));
                foreach (string e in r.Errors) sb.AppendLine("    ERROR   " + e);
                foreach (string w in r.Warnings) sb.AppendLine("    WARNING " + w);
                foreach (string n in r.Info) sb.AppendLine("    " + n);
            }
            sb.Insert(0, (ok ? "RESULT: PASS" : "RESULT: FAIL") + "\n");
            return sb.ToString();
        }

        static void Check(GameObject root, PrefabCatalog catalog, TrackReport r)
        {
            TrackKitSettings S = TrackKitSettings.Get();
            int floorLayer = S.floorLayer;
            int spawnCount = S.spawnSlots.Length;
            TrackPiece piece = root.GetComponent<TrackPiece>();
            if (piece == null) { r.Error("no TrackPiece component"); return; }

            // sockets
            if (piece.entry == null || piece.exit == null) r.Error("entry/exit socket missing");
            else
            {
                if (Mathf.Abs(piece.entry.forward.y) > 0.01f || Mathf.Abs(piece.exit.forward.y) > 0.01f) r.Error("sockets must face horizontally");
                if (Vector3.Distance(piece.entry.position, piece.exit.position) < 1f) r.Error("entry and exit are on top of each other");
                foreach (Transform s in new[] { piece.entry, piece.exit })
                {
                    float y;
                    if (!TrackKitMeasure.SurfaceY(root, s.position - s.forward * 0.05f, out y)) r.Error(s.name + " socket has no road under it");
                    else if (Mathf.Abs(y - s.position.y) > 0.05f) r.Error(s.name + " socket is " + (s.position.y - y).ToString("F2") + " m off the road surface");
                }
                r.Note("length " + Vector3.Distance(piece.entry.position, piece.exit.position).ToString("F2") + " m, turn " + piece.turnDegrees.ToString("F1") + " deg, rise " + piece.rise.ToString("F2") + " m");
            }

            // R1, R2, R5: floor
            int floor = 0;
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.gameObject.layer == floorLayer)
                {
                    if (c.isTrigger) r.Error("Floor collider '" + c.name + "' is a trigger");
                    else floor++;
                }
            }
            if (floor == 0) r.Error("no solid Floor (layer 8) collider");
            r.Note(floor + " Floor collider(s)");

            // R6: walls
            int walls = 0, left = 0, right = 0;
            Vector3 mid = piece.entry != null && piece.exit != null ? (piece.entry.position + piece.exit.position) * 0.5f : Vector3.zero;
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                if (!c.CompareTag(S.wallTag)) continue;
                if (c.gameObject.layer == floorLayer) r.Error("wall '" + c.name + "' is on layer 8");
                if (c.isTrigger) r.Error("wall '" + c.name + "' is a trigger");
                walls++;
                if (piece.entry != null)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, TrackChain.Flat(piece.exit.position - piece.entry.position));
                    if (Vector3.Dot(c.bounds.center - mid, side) >= 0f) right++; else left++;
                }
            }
            if (walls == 0) r.Error("no colliders tagged '" + S.wallTag + "'");
            else if (piece.kind != TrackPiece.PieceKind.Bend && (left == 0 || right == 0)) r.Error("walls only on one side");
            r.Note(walls + " wall collider(s)");

            // gates
            var markers = root.GetComponentsInChildren<TrackGateMarker>(true);
            int starts = 0;
            foreach (var m in markers)
            {
                if (m.isStartGate) starts++;
                float y;
                if (!TrackKitMeasure.SurfaceY(root, m.transform.position, out y)) r.Error("gate marker '" + m.name + "' is off the road");
            }
            if (markers.Length == 0) r.Error("no gate markers");
            r.Note(markers.Length + " gate marker(s)");

            // start piece: S1-S4
            var spawns = root.GetComponentsInChildren<TrackSpawnMarker>(true);
            if (piece.kind == TrackPiece.PieceKind.Start)
            {
                if (starts != 1) r.Error("start piece needs exactly 1 start gate, has " + starts);
                if (spawns.Length != spawnCount) r.Error("start piece needs " + spawnCount + " spawns, has " + spawns.Length);
                var slots = new bool[spawnCount];
                TrackGateMarker gate = null;
                foreach (var m in markers) if (m.isStartGate) gate = m;
                foreach (var s in spawns)
                {
                    if (s.slot < 0 || s.slot >= slots.Length || slots[s.slot]) r.Error("spawn slot " + s.slot + " is out of range or duplicated");
                    else slots[s.slot] = true;
                    float y;
                    if (!TrackKitMeasure.SurfaceY(root, s.transform.position, out y)) r.Error("spawn " + s.slot + " is off the road");
                    if (gate != null)
                    {
                        float ahead = gate.transform.InverseTransformPoint(s.transform.position).z;
                        if (ahead < S.gateTriggerFront + S.spawnClearance)
                            r.Error("spawn " + s.slot + " is only " + ahead.ToString("F1") + " m past the start gate");
                        if (Vector3.Angle(s.transform.forward, gate.transform.forward) > 1f) r.Error("spawn " + s.slot + " does not face along the start gate");
                    }
                }
            }
            else
            {
                if (starts > 0) r.Error("only the start piece may have a start gate");
                if (spawns.Length > 0) r.Error("only the start piece may have spawns");
            }

            // Prefab Builder SAVE/LOAD
            var identity = root.GetComponent<PlacedPrefabIdentity>();
            if (identity == null) r.Error("no PlacedPrefabIdentity, Prefab Builder SAVE would skip it");
            else if (catalog == null) r.Warn("no Prefab Builder catalog configured; LOAD of this piece is not checked");
            else if (!catalog.CanResolve(identity.pieceId)) r.Error("Prefab Builder catalog cannot resolve '" + identity.pieceId + "', LOAD would fail");
        }
    }
}
