using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace TrackKit.EditorTools
{
    /// <summary>
    /// Finds where a road mesh starts and ends ("sockets"). Straights/inclines: caps along the
    /// travel axis. Bends: the two end faces of matching width on the 2D convex hull.
    /// All results are in the piece root's local space.
    /// </summary>
    public static class TrackKitMeasure
    {
        public struct Socket
        {
            public Vector3 center;   // y filled in by surface raycast
            public Vector3 outward;  // horizontal, pointing out of the piece
        }

        public struct Measure
        {
            public Socket entry;
            public Socket exit;
            public float innerHalfWidth;
            public float outerHalfWidth;
        }

        const float SeamInset = 0.03f;

        public static Vector3[] LocalVertices(GameObject root)
        {
            var all = new List<Vector3>();
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                Vector3[] v = ReadVertices(f.sharedMesh);
                for (int i = 0; i < v.Length; i++)
                    all.Add(root.transform.InverseTransformPoint(f.transform.TransformPoint(v[i])));
            }
            return all.ToArray();
        }

        static Vector3[] ReadVertices(Mesh mesh)
        {
            if (mesh.isReadable)
                return mesh.vertices;
            Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                Mesh.MeshData slice = data[0];
                using (var native = new NativeArray<Vector3>(slice.vertexCount, Allocator.Temp))
                {
                    slice.GetVertices(native);
                    var verts = new Vector3[native.Length];
                    native.CopyTo(verts);
                    return verts;
                }
            }
            finally
            {
                data.Dispose();
            }
        }

        // -------------------------------------------------------------- straights and inclines

        /// <param name="axisHint">Local travel axis if known (Horizon tiles run along local Z). Zero = estimate from the curbs.</param>
        public static Measure MeasureStraight(Vector3[] verts, Vector3 axisHint)
        {
            float minY = float.PositiveInfinity;
            foreach (Vector3 v in verts) minY = Mathf.Min(minY, v.y);
            var raised = new List<Vector3>();
            foreach (Vector3 v in verts) if (v.y > minY + 0.04f) raised.Add(v);

            // A 20 x 20 tile is square, so the outline alone cannot tell length from width; the
            // raised curbs run along the road, which is what the cyber builder relied on too.
            Vector3 axis = axisHint.sqrMagnitude > 0.5f
                ? new Vector3(axisHint.x, 0f, axisHint.z).normalized
                : PrincipalAxis(raised.Count >= 4 ? raised : new List<Vector3>(verts));
            float minP = float.PositiveInfinity, maxP = float.NegativeInfinity;
            foreach (Vector3 v in verts)
            {
                float p = Vector3.Dot(v, axis);
                minP = Mathf.Min(minP, p);
                maxP = Mathf.Max(maxP, p);
            }

            var m = new Measure
            {
                entry = CapSocket(verts, axis, minP, -axis),
                exit = CapSocket(verts, axis, maxP, axis),
                innerHalfWidth = InnerHalfWidth(raised, axis),
                outerHalfWidth = OuterHalfWidth(verts, axis)
            };
            Inset(ref m.entry);
            Inset(ref m.exit);
            return m;
        }

        static Socket CapSocket(Vector3[] verts, Vector3 axis, float projection, Vector3 outward)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 v in verts)
            {
                if (Mathf.Abs(Vector3.Dot(v, axis) - projection) > 0.2f) continue;
                sum += v;
                count++;
            }
            if (count == 0) throw new System.InvalidOperationException("Road end cap has no vertices.");
            Vector3 c = sum / count;
            c.y = 0f;
            return new Socket { center = c, outward = outward.normalized };
        }

        static float InnerHalfWidth(List<Vector3> raised, Vector3 axis)
        {
            if (raised.Count < 4) return 6.5f;
            Vector3 mean = Vector3.zero;
            foreach (Vector3 v in raised) mean += v;
            mean /= raised.Count;
            float min = float.PositiveInfinity;
            foreach (Vector3 v in raised)
            {
                Vector3 flat = v - mean; flat.y = 0f;
                float side = (flat - axis * Vector3.Dot(flat, axis)).magnitude;
                if (side > 1f && side < min) min = side;
            }
            return float.IsPositiveInfinity(min) ? 6.5f : min;
        }

        static float OuterHalfWidth(Vector3[] verts, Vector3 axis)
        {
            Vector3 mean = Vector3.zero;
            foreach (Vector3 v in verts) mean += v;
            mean /= Mathf.Max(1, verts.Length);
            float max = 0f;
            foreach (Vector3 v in verts)
            {
                Vector3 flat = v - mean; flat.y = 0f;
                max = Mathf.Max(max, (flat - axis * Vector3.Dot(flat, axis)).magnitude);
            }
            return max > 1f ? max : 10f;
        }

        static Vector3 PrincipalAxis(List<Vector3> points)
        {
            Vector2 mean = Vector2.zero;
            foreach (Vector3 p in points) mean += new Vector2(p.x, p.z);
            mean /= Mathf.Max(1, points.Count);
            float cxx = 0f, czz = 0f, cxz = 0f;
            foreach (Vector3 p in points)
            {
                float x = p.x - mean.x, z = p.z - mean.y;
                cxx += x * x; czz += z * z; cxz += x * z;
            }
            float angle = 0.5f * Mathf.Atan2(2f * cxz, cxx - czz);
            Vector3 axis = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            // Snap near-axis results so straights come out exactly square.
            if (Mathf.Abs(axis.x) < 1e-3f) axis = new Vector3(0f, 0f, Mathf.Sign(axis.z));
            if (Mathf.Abs(axis.z) < 1e-3f) axis = new Vector3(Mathf.Sign(axis.x), 0f, 0f);
            return axis.normalized;
        }

        // -------------------------------------------------------------- bends

        struct EndFace { public Vector2 a, b; public float length; public int support; }

        /// <summary>Finds the two end faces of a bend whose width matches endWidth and whose turn is closest to expectedTurn.</summary>
        public static Measure MeasureBend(Vector3[] verts, float endWidth, float expectedTurn)
        {
            var cloud = new List<Vector2>(verts.Length);
            foreach (Vector3 v in verts) cloud.Add(new Vector2(v.x, v.z));
            List<Vector2> hull = ConvexHull(cloud);
            List<EndFace> faces = CollectEndFaces(hull, cloud, endWidth);
            if (faces.Count < 2)
                throw new System.InvalidOperationException("Bend end faces of width " + endWidth.ToString("0.0") + " m not found (" + faces.Count + ").");

            float bestErr = float.PositiveInfinity;
            int bestSupport = -1;
            Socket bestEntry = default(Socket), bestExit = default(Socket);
            for (int i = 0; i < faces.Count; i++)
            {
                for (int j = 0; j < faces.Count; j++)
                {
                    if (i == j) continue;
                    Socket a = FaceSocket(faces[i], cloud), b = FaceSocket(faces[j], cloud);
                    float turn = TurnAngle(a, b);
                    if (turn <= 0f) continue; // keep one orientation: entry->exit turns positive
                    float err = Mathf.Abs(turn - expectedTurn);
                    int support = faces[i].support + faces[j].support;
                    if (err < bestErr - 0.5f || (Mathf.Abs(err - bestErr) <= 0.5f && support > bestSupport))
                    {
                        bestErr = err; bestSupport = support; bestEntry = a; bestExit = b;
                    }
                }
            }
            if (bestErr > 10f)
                throw new System.InvalidOperationException("No pair of bend ends turns " + expectedTurn + " deg (best error " + bestErr.ToString("0.0") + ").");

            var m = new Measure { entry = bestEntry, exit = bestExit, innerHalfWidth = endWidth * 0.5f, outerHalfWidth = endWidth * 0.5f };
            Inset(ref m.entry);
            Inset(ref m.exit);
            return m;
        }

        public static float TurnAngle(Socket entry, Socket exit)
        {
            return Vector3.SignedAngle(-entry.outward, exit.outward, Vector3.up);
        }

        static List<EndFace> CollectEndFaces(List<Vector2> hull, List<Vector2> cloud, float target)
        {
            var faces = new List<EndFace>();
            int count = hull.Count;
            for (int start = 0; start < count; start++)
            {
                Vector2 origin = hull[start];
                Vector2 seed = hull[(start + 1) % count] - origin;
                if (seed.sqrMagnitude < 0.0001f) continue;
                Vector2 dir = seed.normalized;
                int end = start;
                float path = 0f;
                for (int step = 0; step < count - 1; step++)
                {
                    int from = (start + step) % count, to = (start + step + 1) % count;
                    Vector2 edge = hull[to] - hull[from];
                    float len = edge.magnitude;
                    if (len < 0.05f) { end = to; continue; }
                    if (Vector2.Dot(edge.normalized, dir) < 0.985f) break;
                    if (path + len > target + 1.5f) break;
                    path += len;
                    end = to;
                }
                if (end == start) continue;
                float chord = (hull[end] - origin).magnitude;
                if (Mathf.Abs(chord - target) > 1f || path - chord > 0.75f) continue;
                int support = VerticesNearSegment(cloud, origin, hull[end], 0.4f);
                if (support < 6) continue;
                Vector2 mid = (origin + hull[end]) * 0.5f;
                bool dup = false;
                foreach (EndFace f in faces)
                    if (((f.a + f.b) * 0.5f - mid).sqrMagnitude < 1f) { dup = true; break; }
                if (!dup) faces.Add(new EndFace { a = origin, b = hull[end], length = chord, support = support });
            }
            return faces;
        }

        static Socket FaceSocket(EndFace face, List<Vector2> cloud)
        {
            Vector2 mid = (face.a + face.b) * 0.5f;
            Vector2 edge = (face.b - face.a).normalized;
            Vector2 normal = new Vector2(-edge.y, edge.x);
            Vector2 centroid = Vector2.zero;
            foreach (Vector2 p in cloud) centroid += p;
            centroid /= Mathf.Max(1, cloud.Count);
            if (Vector2.Dot(normal, mid - centroid) < 0f) normal = -normal;
            return new Socket { center = new Vector3(mid.x, 0f, mid.y), outward = new Vector3(normal.x, 0f, normal.y).normalized };
        }

        static int VerticesNearSegment(List<Vector2> pts, Vector2 a, Vector2 b, float maxD)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-4f) return 0;
            int n = 0;
            foreach (Vector2 p in pts)
            {
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                if ((p - (a + ab * t)).sqrMagnitude <= maxD * maxD) n++;
            }
            return n;
        }

        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var pts = new List<Vector2>(points);
            pts.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector2 p = pts[pass == 0 ? i : pts.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        static void Inset(ref Socket s)
        {
            s.center -= s.outward * SeamInset;
        }

        // -------------------------------------------------------------- surface

        /// <summary>Highest non-trigger collider surface under (x,z) on this object, in world space.</summary>
        public static bool SurfaceY(GameObject root, Vector3 worldPoint, out float y)
        {
            return SurfaceY(root, worldPoint, TrackKitSettings.Get().floorLayer, out y);
        }

        public static bool SurfaceY(GameObject root, Vector3 worldPoint, int floorLayer, out float y)
        {
            y = 0f;
            bool any = false;
            var ray = new Ray(new Vector3(worldPoint.x, worldPoint.y + 60f, worldPoint.z), Vector3.down);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.gameObject.layer != floorLayer) continue;
                RaycastHit hit;
                if (c.Raycast(ray, out hit, 200f) && (!any || hit.point.y > y))
                {
                    y = hit.point.y;
                    any = true;
                }
            }
            return any;
        }
    }
}
