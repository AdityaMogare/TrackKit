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
    /// 1. Build Kit: turns the road prefabs listed in TrackKitSettings into kit pieces in
    /// settings.kitFolder. Each piece gets entry/exit sockets, gate markers, its own walls, and
    /// (start piece) a start gate and spawn markers. Source prefabs are only read. Kit ids are added
    /// to the Prefab Builder catalog; other catalog entries are not touched.
    /// </summary>
    public static class TrackKitKitBuilder
    {
        static TrackKitSettings S { get { return TrackKitSettings.Get(); } }
        public static string KitFolder { get { return S.kitFolder; } }
        public static string CatalogPath { get { return S.prefabBuilderCatalogPath; } }
        static string WallMaterialPath { get { return S.materialFolder + "/tk_wall.mat"; } }

        [MenuItem("Tools/TrackKit/1. Build Kit", false, 300)]
        public static void BuildKitMenu()
        {
            var log = new StringBuilder();
            log.AppendLine("TrackKit Build Kit  " + System.DateTime.Now);
            bool ok = BuildKit(log);
            File.WriteAllText(ReportPath("BuildKit"), log.ToString());
            if (ok) Debug.Log("[TrackKit] " + log);
            else Debug.LogError("[TrackKit] " + log);
        }

        public static bool BuildKit(StringBuilder log)
        {
            TrackKitSettings settings = S;
            if (settings.pieces.Count == 0)
            {
                log.AppendLine("No pieces listed in " + AssetDatabase.GetAssetPath(settings) + ". Add road prefabs under Kit pieces (Tools/TrackKit/Settings).");
                return false;
            }
            EnsureFolder(settings.kitFolder);
            EnsureFolder(settings.materialFolder);
            Material wallMat = WallMaterial();

            // The straight sets the road family's widths (bends are measured against its end width).
            float roadHalfWidth = 6.5f, endWidth = 20f;
            bool ok = true;
            var built = new List<KeyValuePair<string, GameObject>>();

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                TrackKitSettings.PieceDef reference = settings.pieces.Find(d => d.source != null && (d.kind == TrackPiece.PieceKind.Straight || d.kind == TrackPiece.PieceKind.Start));
                if (reference != null)
                {
                    GameObject probe = Spawn(reference.source, preview);
                    TrackKitMeasure.Measure s = TrackKitMeasure.MeasureStraight(TrackKitMeasure.LocalVertices(probe), reference.axisHint);
                    roadHalfWidth = s.innerHalfWidth;
                    endWidth = s.outerHalfWidth * 2f;
                    Object.DestroyImmediate(probe);
                    log.AppendLine("Road family from " + reference.source.name + ": drivable half-width " + roadHalfWidth.ToString("F2") + " m, end width " + endWidth.ToString("F2") + " m");
                }
                else log.AppendLine("No straight piece listed; using default widths (half-width 6.5 m, end width 20 m).");

                foreach (TrackKitSettings.PieceDef def in settings.pieces)
                {
                    try
                    {
                        GameObject prefab = BuildPiece(def, preview, roadHalfWidth, endWidth, wallMat, log);
                        built.Add(new KeyValuePair<string, GameObject>(def.id, prefab));
                    }
                    catch (System.Exception ex)
                    {
                        ok = false;
                        log.AppendLine("  FAILED " + def.id + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            PrefabCatalog catalog = string.IsNullOrEmpty(CatalogPath) ? null : AssetDatabase.LoadAssetAtPath<PrefabCatalog>(CatalogPath);
            if (catalog == null)
            {
                log.AppendLine("Prefab Builder catalog not found at '" + CatalogPath + "'; kit pieces were not added to the palette.");
            }
            else
            {
                foreach (var kv in built)
                {
                    PrefabCatalogEntry e = catalog.FindEntryById(kv.Key);
                    if (e == null) { e = new PrefabCatalogEntry(); catalog.Entries.Add(e); }
                    e.id = kv.Key;
                    e.prefab = kv.Value;
                    e.gridOverride = settings.catalogGrid;
                    e.heightOverride = 0f;
                    e.tiltOverride = 0f;
                }
                EditorUtility.SetDirty(catalog);
                log.AppendLine("Prefab Builder catalog: " + built.Count + " kit entries added or refreshed (other entries untouched).");
            }
            AssetDatabase.SaveAssets();
            return ok;
        }

        static GameObject Spawn(GameObject source, Scene scene)
        {
            if (source == null) throw new System.InvalidOperationException("no source prefab assigned");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            return go;
        }

        static GameObject BuildPiece(TrackKitSettings.PieceDef def, Scene preview, float roadHalfWidth, float endWidth, Material wallMat, StringBuilder log)
        {
            GameObject root = Spawn(def.source, preview);
            root.name = def.id;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = S.floorLayer;
            foreach (MeshCollider c in root.GetComponentsInChildren<MeshCollider>(true))
            {
                c.isTrigger = false;
                c.convex = false;
            }
            if (root.GetComponentsInChildren<Collider>(true).Length == 0)
                throw new System.InvalidOperationException("source has no collider; it cannot be driven on.");

            Vector3[] verts = TrackKitMeasure.LocalVertices(root);
            bool bend = def.kind == TrackPiece.PieceKind.Bend;
            TrackKitMeasure.Measure m = bend
                ? TrackKitMeasure.MeasureBend(verts, endWidth, def.expectedTurn)
                : TrackKitMeasure.MeasureStraight(verts, def.axisHint);

            Transform entry = MakeSocket(root, "entry", m.entry);
            Transform exit = MakeSocket(root, "exit", m.exit);

            TrackPiece piece = root.AddComponent<TrackPiece>();
            piece.kind = def.kind;
            piece.entry = entry;
            piece.exit = exit;
            piece.roadHalfWidth = roadHalfWidth;
            piece.turnDegrees = bend ? TrackKitMeasure.TurnAngle(m.entry, m.exit) : 0f;
            piece.rise = exit.position.y - entry.position.y;

            // Centreline from entry to exit (world == local here, root sits at the origin).
            List<Vector3> line = Centerline(root, entry, exit, bend, bend ? 12 : (def.kind == TrackPiece.PieceKind.Incline ? 8 : 2));

            var gates = new GameObject("gates").transform;
            gates.SetParent(root.transform, false);
            if (def.kind == TrackPiece.PieceKind.Start)
            {
                Vector3 fwd = TrackChain.Flat(exit.position - entry.position);
                Vector3 p = entry.position + fwd * S.startGateFromEntry;
                MakeGate(root, gates, "gate_start", p, fwd).isStartGate = true;
                MakeSpawns(root, p, fwd);
            }
            else
            {
                for (int g = 0; g < def.gates; g++)
                {
                    float t = (g + 1f) / (def.gates + 1f);
                    Vector3 p, fwd;
                    PointAlong(line, t, out p, out fwd);
                    MakeGate(root, gates, "gate_" + g, p, fwd);
                }
            }

            int wallCount = MakeWalls(root, line, wallMat);

            PlacedPrefabIdentity identity = root.GetComponent<PlacedPrefabIdentity>();
            if (identity == null) identity = root.AddComponent<PlacedPrefabIdentity>();
            identity.pieceId = def.id;

            string path = KitFolder + "/" + def.id + ".prefab";
            string line1 = string.Format("  {0,-12} from {1}: entry {2} exit {3} turn {4:F1} deg rise {5:F2} m, {6} gate marker(s), {7} wall(s) -> {8}",
                def.id, def.source.name, entry.localPosition.ToString("F2"), exit.localPosition.ToString("F2"),
                piece.turnDegrees, piece.rise, gates.childCount, wallCount, path);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            log.AppendLine(line1);
            return saved;
        }

        static Transform MakeSocket(GameObject root, string name, TrackKitMeasure.Socket s)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            Vector3 p = s.center;
            float y;
            if (!TrackKitMeasure.SurfaceY(root, p - s.outward * 0.05f, out y))
                throw new System.InvalidOperationException("no road surface under the " + name + " socket at " + p.ToString("F2"));
            p.y = y;
            t.localPosition = p;
            t.localRotation = Quaternion.LookRotation(s.outward, Vector3.up);
            return t;
        }

        static List<Vector3> Centerline(GameObject root, Transform entry, Transform exit, bool bend, int steps)
        {
            var pts = new List<Vector3>();
            Vector3 a = entry.position, b = exit.position;
            Vector3 center = Vector3.zero;
            bool arc = false;
            if (bend)
            {
                // Circle through both sockets, tangent to their directions.
                Vector3 tin = -entry.forward, tout = exit.forward;
                Vector2 o = new Vector2(a.x, a.z), e = new Vector2(b.x, b.z);
                Vector2 dIn = new Vector2(-tin.z, tin.x), dOut = new Vector2(-tout.z, tout.x);
                float den = dIn.x * dOut.y - dIn.y * dOut.x;
                if (Mathf.Abs(den) > 1e-4f)
                {
                    float t = ((e.x - o.x) * dOut.y - (e.y - o.y) * dOut.x) / den;
                    Vector2 c = o + dIn * t;
                    center = new Vector3(c.x, 0f, c.y);
                    arc = true;
                }
            }

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p;
                if (arc)
                {
                    Vector3 ra = a - center; ra.y = 0f;
                    Vector3 rb = b - center; rb.y = 0f;
                    float sweep = Vector3.SignedAngle(ra, rb, Vector3.up);
                    p = center + Quaternion.AngleAxis(sweep * t, Vector3.up) * ra;
                }
                else
                {
                    p = Vector3.Lerp(a, b, t);
                }
                float y;
                p.y = TrackKitMeasure.SurfaceY(root, p, out y) ? y : Mathf.Lerp(a.y, b.y, t);
                pts.Add(p);
            }
            return pts;
        }

        static void PointAlong(List<Vector3> line, float t, out Vector3 pos, out Vector3 fwd)
        {
            float total = 0f;
            for (int i = 0; i < line.Count - 1; i++) total += Vector3.Distance(line[i], line[i + 1]);
            float target = total * t, run = 0f;
            for (int i = 0; i < line.Count - 1; i++)
            {
                float seg = Vector3.Distance(line[i], line[i + 1]);
                if (run + seg >= target || i == line.Count - 2)
                {
                    float u = seg > 1e-4f ? Mathf.Clamp01((target - run) / seg) : 0f;
                    pos = Vector3.Lerp(line[i], line[i + 1], u);
                    fwd = TrackChain.Flat(line[i + 1] - line[i]);
                    return;
                }
                run += seg;
            }
            pos = line[0];
            fwd = TrackChain.Flat(line[1] - line[0]);
        }

        static TrackGateMarker MakeGate(GameObject root, Transform parent, string name, Vector3 pos, Vector3 fwd)
        {
            var go = new GameObject(name);
            go.layer = 0;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
            return go.AddComponent<TrackGateMarker>();
        }

        static void MakeSpawns(GameObject root, Vector3 gatePos, Vector3 fwd)
        {
            var parent = new GameObject("spawns").transform;
            parent.SetParent(root.transform, false);
            Vector2[] slots = S.spawnSlots;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float rearZ = 0f;
            foreach (Vector2 v in slots) rearZ = Mathf.Min(rearZ, v.y);
            float advance = S.gateTriggerFront + S.spawnClearance + 1f - rearZ; // rear slot 1 m beyond the clearance
            Vector3 origin = gatePos + fwd * advance;
            for (int i = 0; i < slots.Length; i++)
            {
                Vector3 p = origin + right * slots[i].x + fwd * slots[i].y;
                float y;
                if (!TrackKitMeasure.SurfaceY(root, p, out y))
                    throw new System.InvalidOperationException("spawn slot " + i + " is off the road at " + p.ToString("F1"));
                p.y = y + S.spawnHeight;
                var go = new GameObject("spawn_" + i);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up));
                go.AddComponent<TrackSpawnMarker>().slot = i;
            }
        }

        static int MakeWalls(GameObject root, List<Vector3> line, Material mat)
        {
            var parent = new GameObject("walls").transform;
            parent.SetParent(root.transform, false);
            int count = 0;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector3 a = line[i], b = line[i + 1];
                Vector3 right = Vector3.Cross(Vector3.up, TrackChain.Flat(b - a));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 dir = right * side;
                    // Measure the road edge at the middle of the segment, never at a socket: at the
                    // piece ends half the sideways rays fall off the mesh and the wall would cut
                    // diagonally into the road.
                    float offset = WallOffset(root, (a + b) * 0.5f, dir);
                    Vector3 ea = a + dir * offset;
                    Vector3 eb = b + dir * offset;
                    Vector3 along = eb - ea;
                    if (along.sqrMagnitude < 0.01f) continue;
                    var wall = new GameObject("wall_" + count);
                    wall.tag = S.wallTag;
                    wall.layer = S.wallLayer;
                    wall.transform.SetParent(parent, false);
                    wall.transform.SetPositionAndRotation((ea + eb) * 0.5f + Vector3.up * 2f, Quaternion.LookRotation(along.normalized, Vector3.up));
                    var box = wall.AddComponent<BoxCollider>();
                    box.isTrigger = false;
                    box.size = new Vector3(S.wallThickness, S.wallHeight, along.magnitude + 1f);

                    // Built-in cube mesh, no collider (the BoxCollider above is the wall).
                    var vis = new GameObject("mesh");
                    vis.tag = S.wallTag;
                    vis.layer = S.wallLayer;
                    vis.transform.SetParent(wall.transform, false);
                    vis.transform.localScale = box.size;
                    vis.AddComponent<MeshFilter>().sharedMesh = CubeMesh();
                    vis.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    count++;
                }
            }
            return count;
        }

        /// <summary>How far the road surface reaches from the centreline, minus room for the wall. Same rule as the cyber walls.</summary>
        static float WallOffset(GameObject root, Vector3 origin, Vector3 dir)
        {
            float reach = 0f;
            for (float d = 0.5f; d <= 16f; d += 0.5f)
            {
                float y;
                if (!TrackKitMeasure.SurfaceY(root, origin + dir * d, out y) || Mathf.Abs(y - origin.y) > 1.0f) break;
                reach = d;
            }
            return Mathf.Max(S.wallThickness, reach - S.wallInset);
        }

        static Mesh cube;
        static Mesh CubeMesh()
        {
            if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            return cube;
        }

        static Material WallMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
            if (mat != null) return mat;
            Shader shader = null;
            foreach (string name in new[] { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Lit", "Unlit/Color", "Standard" })
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }
            mat = new Material(shader);
            Color c = S.wallColor;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            AssetDatabase.CreateAsset(mat, WallMaterialPath);
            return mat;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static string ReportPath(string name)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "TrackKit_" + name + "_Report.txt");
        }

        public static List<GameObject> LoadKit()
        {
            var list = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(KitFolder)) return list;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { KitFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.GetComponent<TrackPiece>() != null) list.Add(go);
            }
            list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
            return list;
        }
    }
}
