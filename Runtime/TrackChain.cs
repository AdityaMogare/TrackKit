using System.Collections.Generic;
using UnityEngine;

namespace TrackKit
{
    /// <summary>
    /// Game-independent track logic. No UnityEditor references, so it can also run in a player
    /// build (a future runtime assembler).
    ///
    ///   OrderChain    pieces -> driving order (which piece follows which, and which way round)
    ///   CollectGates  gate markers in driving order, start gate first
    ///   CollectSpawns start-grid markers in slot order
    ///   SampleGround  down-rays along a line of points to find gaps or foreign floor
    ///
    /// Turning the ordered markers into the game's own gates is the adapter's job.
    /// </summary>
    public static class TrackChain
    {
        public const int DefaultFloorLayer = 8;
        public const float DefaultJoinTolerance = 0.35f;

        public struct Link
        {
            public TrackPiece piece;
            public bool reversed;
            public Transform In { get { return reversed ? piece.exit : piece.entry; } }
            public Transform Out { get { return reversed ? piece.entry : piece.exit; } }
        }

        public struct GateSource
        {
            public TrackGateMarker marker;
            public Link link;
            public Vector3 position;
            public Quaternion rotation; // driving direction
            public float HalfWidth { get { return marker.segmentWidth > 0f ? marker.segmentWidth : link.piece.roadHalfWidth; } }
        }

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
        }

        public static TrackGateMarker FindStartGate(IList<TrackPiece> pieces, TrackReport report)
        {
            TrackGateMarker found = null;
            int count = 0;
            for (int i = 0; i < pieces.Count; i++)
                foreach (TrackGateMarker m in pieces[i].GetComponentsInChildren<TrackGateMarker>(true))
                {
                    if (!m.isStartGate) continue;
                    count++;
                    if (found == null) found = m;
                }
            if (count == 0) report.Error("No start piece: no gate marker has isStartGate.");
            if (count > 1) report.Error("Found " + count + " start gates; a track needs exactly one start piece.");
            return found;
        }

        /// <summary>
        /// Walks from the start piece, attaching whichever unused piece has a socket on the current
        /// open end. Works with pieces in any hierarchy order and either way round.
        /// </summary>
        public static List<Link> OrderChain(IList<TrackPiece> pieces, TrackReport report, out bool closed, float joinTolerance = DefaultJoinTolerance)
        {
            closed = false;
            var chain = new List<Link>();
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i].entry == null || pieces[i].exit == null)
                    report.Error(pieces[i].name + " has no entry/exit socket.");
            if (!report.Ok) return chain;

            TrackGateMarker startGate = FindStartGate(pieces, report);
            if (startGate == null) return chain;
            TrackPiece startPiece = startGate.GetComponentInParent<TrackPiece>();

            // The start piece runs the way its start gate faces.
            float dotExit = Vector3.Dot(Flat(startPiece.exit.forward), Flat(startGate.transform.forward));
            chain.Add(new Link { piece = startPiece, reversed = dotExit < 0f });

            var used = new HashSet<TrackPiece> { startPiece };
            while (true)
            {
                Transform open = chain[chain.Count - 1].Out;
                TrackPiece best = null;
                bool bestReversed = false;
                float bestDist = joinTolerance;
                for (int i = 0; i < pieces.Count; i++)
                {
                    TrackPiece p = pieces[i];
                    if (used.Contains(p)) continue;
                    float dEntry = Vector3.Distance(open.position, p.entry.position);
                    float dExit = Vector3.Distance(open.position, p.exit.position);
                    if (dEntry <= bestDist) { best = p; bestReversed = false; bestDist = dEntry; }
                    if (dExit < bestDist) { best = p; bestReversed = true; bestDist = dExit; }
                }

                if (best == null)
                {
                    float toStart = Vector3.Distance(open.position, chain[0].In.position);
                    if (chain.Count > 1 && toStart <= joinTolerance)
                    {
                        closed = true;
                        CheckJoint(chain[chain.Count - 1], chain[0], report);
                    }
                    else
                    {
                        report.Error("Open joint after piece #" + (chain.Count - 1) + " '" + chain[chain.Count - 1].piece.name +
                                     "' at " + open.position.ToString("F2") + " (start piece is " + toStart.ToString("F2") + " m away).");
                    }
                    break;
                }

                var link = new Link { piece = best, reversed = bestReversed };
                CheckJoint(chain[chain.Count - 1], link, report);
                chain.Add(link);
                used.Add(best);
            }

            for (int i = 0; i < pieces.Count; i++)
                if (!used.Contains(pieces[i]))
                    report.Error("Piece '" + pieces[i].name + "' at " + pieces[i].transform.position.ToString("F1") + " is not connected to the chain.");
            return chain;
        }

        static void CheckJoint(Link from, Link to, TrackReport report)
        {
            float angle = Vector3.Angle(Flat(from.Out.forward), -Flat(to.In.forward));
            if (angle > 2f)
                report.Error("Joint '" + from.piece.name + "' -> '" + to.piece.name + "' is bent by " + angle.ToString("F1") + " deg.");
            float dy = Mathf.Abs(from.Out.position.y - to.In.position.y);
            if (dy > 0.1f)
                report.Error("Joint '" + from.piece.name + "' -> '" + to.piece.name + "' has a " + dy.ToString("F2") + " m step.");
        }

        public static List<TrackGateMarker> MarkersInDrivingOrder(Link link)
        {
            var markers = new List<TrackGateMarker>(link.piece.GetComponentsInChildren<TrackGateMarker>(true));
            Vector3 inPos = link.In.position;
            markers.Sort((a, b) => Vector3.Distance(inPos, a.transform.position).CompareTo(Vector3.Distance(inPos, b.transform.position)));
            return markers;
        }

        public static List<GateSource> CollectGates(List<Link> chain, TrackReport report)
        {
            var list = new List<GateSource>();
            int startIndex = -1;
            foreach (Link link in chain)
            {
                List<TrackGateMarker> markers = MarkersInDrivingOrder(link);
                if (markers.Count == 0) report.Warn("Piece '" + link.piece.name + "' has no gate markers.");
                foreach (TrackGateMarker m in markers)
                {
                    Vector3 fwd = link.reversed ? -m.transform.forward : m.transform.forward;
                    if (m.isStartGate) startIndex = list.Count;
                    list.Add(new GateSource { marker = m, link = link, position = m.transform.position, rotation = Quaternion.LookRotation(Flat(fwd), Vector3.up) });
                }
            }
            if (startIndex > 0)
            {
                var rotated = new List<GateSource>(list.Count);
                for (int i = 0; i < list.Count; i++) rotated.Add(list[(startIndex + i) % list.Count]);
                list = rotated;
            }
            return list;
        }

        public static List<TrackSpawnMarker> CollectSpawns(List<Link> chain)
        {
            var spawns = new List<TrackSpawnMarker>();
            if (chain.Count == 0) return spawns;
            spawns.AddRange(chain[0].piece.GetComponentsInChildren<TrackSpawnMarker>(true));
            spawns.Sort((a, b) => a.slot.CompareTo(b.slot));
            return spawns;
        }

        /// <summary>
        /// Casts down at 1 m steps between consecutive points (closed loop), on the centre and at
        /// +-(halfWidth - 1.5 m). Floor under trackRoot counts as road; any other Floor hit is foreign.
        /// </summary>
        public static void SampleGround(IList<Vector3> points, IList<float> halfWidths, Transform trackRoot, int floorLayer, TrackReport report)
        {
            Physics.SyncTransforms();
            int mask = 1 << floorLayer;
            int samples = 0, gaps = 0, foreign = 0;
            var firstGaps = new List<string>();
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Count];
                float side = Mathf.Max(0f, halfWidths[i] - 1.5f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1f));
                Vector3 right = Vector3.Cross(Vector3.up, Flat(b - a));
                for (int s = 0; s < steps; s++)
                {
                    Vector3 c = Vector3.Lerp(a, b, s / (float)steps);
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector3 p = c + right * (side * k);
                        samples++;
                        bool ours = false;
                        foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 30f, mask, QueryTriggerInteraction.Ignore))
                        {
                            if (trackRoot != null && h.collider.transform.IsChildOf(trackRoot)) ours = true;
                            else foreign++;
                        }
                        if (!ours)
                        {
                            gaps++;
                            if (firstGaps.Count < 8) firstGaps.Add(p.ToString("F1") + " (after point " + i + ")");
                        }
                    }
                }
            }
            report.Note("Ground samples along centre and shoulders: " + samples + ", no road under " + gaps + ".");
            if (gaps > 0) report.Error("Gaps under the racing line at " + gaps + " sample(s): " + string.Join("; ", firstGaps.ToArray()));
            if (foreign > 0) report.Error(foreign + " ray hits on Floor colliders outside the track root (old road still active).");
        }
    }
}
