using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>
    /// Ear-clipping triangulation of a simple polygon with optional holes (holes are joined to the outline by a bridge).
    /// Pure geometry so it can be verified without the Unity engine. Output triangles are counter-clockwise (x right, y up).
    /// </summary>
    public static class PolygonTriangulator
    {
        const double Epsilon = 1e-12;

        /// <summary>
        /// Returns triangle indices into <paramref name="points"/>. Vertices that a bridge visits twice appear once in
        /// <paramref name="points"/> and repeat in the indices.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vec2> outline, IReadOnlyList<IReadOnlyList<Vec2>> holes, out List<Vec2> points)
        {
            if (outline == null || outline.Count < 3) throw new ArgumentException("A polygon needs at least three vertices.", nameof(outline));

            points = new List<Vec2>();
            var ring = new List<int>();
            foreach (var p in Polygon2D.CounterClockwise(outline))
            {
                ring.Add(points.Count);
                points.Add(p);
            }

            if (holes != null && holes.Count > 0)
            {
                var ordered = new List<List<Vec2>>();
                foreach (var hole in holes)
                {
                    var clockwise = Polygon2D.CounterClockwise(hole);
                    clockwise.Reverse();
                    ordered.Add(clockwise);
                }
                ordered.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
                foreach (var hole in ordered) Bridge(points, ring, hole);
            }

            return EarClip(points, ring);
        }

        public static List<int> Triangulate(IReadOnlyList<Vec2> outline, out List<Vec2> points) =>
            Triangulate(outline, null, out points);

        static float MaxX(IReadOnlyList<Vec2> polygon)
        {
            var max = float.MinValue;
            foreach (var p in polygon) max = Math.Max(max, p.X);
            return max;
        }

        static void Bridge(List<Vec2> points, List<int> ring, List<Vec2> hole)
        {
            var start = 0;
            for (var i = 1; i < hole.Count; i++)
                if (hole[i].X > hole[start].X) start = i;
            var m = hole[start];

            var holeIndices = new List<int>();
            for (var i = 0; i < hole.Count; i++)
            {
                holeIndices.Add(points.Count);
                points.Add(hole[(start + i) % hole.Count]);
            }

            var bridgeSlot = FindBridgeSlot(points, ring, m);

            var merged = new List<int>();
            for (var i = 0; i <= bridgeSlot; i++) merged.Add(ring[i]);
            merged.AddRange(holeIndices);
            merged.Add(holeIndices[0]);
            merged.Add(ring[bridgeSlot]);
            for (var i = bridgeSlot + 1; i < ring.Count; i++) merged.Add(ring[i]);
            ring.Clear();
            ring.AddRange(merged);
        }

        /// <summary>Position in the ring of an outline vertex that the hole's rightmost vertex can see.</summary>
        static int FindBridgeSlot(List<Vec2> points, List<int> ring, Vec2 m)
        {
            var n = ring.Count;
            var bestX = double.MaxValue;
            var candidate = -1;
            for (var i = 0; i < n; i++)
            {
                var p = points[ring[i]];
                var q = points[ring[(i + 1) % n]];
                if (p.Y == q.Y) continue;
                if (!((p.Y <= m.Y && q.Y >= m.Y) || (p.Y >= m.Y && q.Y <= m.Y))) continue;
                var x = p.X + (double)(m.Y - p.Y) * (q.X - p.X) / (q.Y - p.Y);
                if (x < m.X || x >= bestX) continue;
                bestX = x;
                candidate = p.X > q.X ? i : (i + 1) % n;
            }
            if (candidate < 0) throw new InvalidOperationException("A hole lies outside the polygon.");

            var c = points[ring[candidate]];
            if (Math.Abs(c.Y - m.Y) < 1e-9 && Math.Abs(c.X - bestX) < 1e-9) return candidate;

            // Vertices of the outline inside the triangle (hole vertex, ray hit, candidate) block the view; the reflex vertex
            // with the smallest angle to the ray is the one the hole can see.
            var best = candidate;
            var bestTan = double.MaxValue;
            for (var i = 0; i < n; i++)
            {
                if (i == candidate) continue;
                var p = points[ring[i]];
                if (p.X < m.X) continue;
                if (!InTriangle(m, new Vec2((float)bestX, m.Y), c, p)) continue;
                var prev = points[ring[(i + n - 1) % n]];
                var next = points[ring[(i + 1) % n]];
                if (Cross(prev, p, next) > 0) continue;
                var dx = p.X - m.X;
                if (dx <= 0) continue;
                var tan = Math.Abs(p.Y - m.Y) / dx;
                if (tan < bestTan || (tan == bestTan && p.X > points[ring[best]].X))
                {
                    best = i;
                    bestTan = tan;
                }
            }
            return best;
        }

        static List<int> EarClip(List<Vec2> points, List<int> ring)
        {
            var n = ring.Count;
            var prev = new int[n];
            var next = new int[n];
            for (var i = 0; i < n; i++)
            {
                prev[i] = (i + n - 1) % n;
                next[i] = (i + 1) % n;
            }

            var triangles = new List<int>();
            var remaining = n;
            var current = 0;
            while (remaining > 3)
            {
                var found = -1;
                var slot = current;
                for (var k = 0; k < remaining; k++)
                {
                    if (IsEar(points, ring, prev, next, slot, remaining))
                    {
                        found = slot;
                        break;
                    }
                    slot = next[slot];
                }

                bool emit = true;
                if (found < 0)
                {
                    // No proper ear (numerical trouble or a degenerate bridge): drop a collinear vertex, or else the sharpest one.
                    slot = current;
                    var bestArea = double.MinValue;
                    for (var k = 0; k < remaining; k++)
                    {
                        var area = Cross(points[ring[prev[slot]]], points[ring[slot]], points[ring[next[slot]]]);
                        if (Math.Abs(area) <= Epsilon)
                        {
                            found = slot;
                            emit = false;
                            break;
                        }
                        if (area > bestArea)
                        {
                            bestArea = area;
                            found = slot;
                        }
                        slot = next[slot];
                    }
                }

                var p = prev[found];
                var q = next[found];
                if (emit)
                {
                    triangles.Add(ring[p]);
                    triangles.Add(ring[found]);
                    triangles.Add(ring[q]);
                }
                next[p] = q;
                prev[q] = p;
                remaining--;
                current = q;
            }

            var a = current;
            var b = next[a];
            var c = next[b];
            if (Cross(points[ring[a]], points[ring[b]], points[ring[c]]) > Epsilon)
            {
                triangles.Add(ring[a]);
                triangles.Add(ring[b]);
                triangles.Add(ring[c]);
            }
            return triangles;
        }

        static bool IsEar(List<Vec2> points, List<int> ring, int[] prev, int[] next, int slot, int remaining)
        {
            var ia = prev[slot];
            var ic = next[slot];
            var a = points[ring[ia]];
            var b = points[ring[slot]];
            var c = points[ring[ic]];
            if (Cross(a, b, c) <= Epsilon) return false;

            var j = next[ic];
            while (j != ia)
            {
                var p = points[ring[j]];
                if (!Same(p, a) && !Same(p, b) && !Same(p, c) && InTriangle(a, b, c, p))
                {
                    var pp = points[ring[prev[j]]];
                    var pn = points[ring[next[j]]];
                    if (Cross(pp, p, pn) <= Epsilon) return false;
                }
                j = next[j];
            }
            return true;
        }

        static bool Same(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;

        /// <summary>Twice the signed area of a, b, c: positive when they turn counter-clockwise.</summary>
        static double Cross(Vec2 a, Vec2 b, Vec2 c) =>
            ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

        /// <summary>Inclusive point-in-triangle test for a triangle of either winding.</summary>
        static bool InTriangle(Vec2 a, Vec2 b, Vec2 c, Vec2 p)
        {
            var d1 = Cross(a, b, p);
            var d2 = Cross(b, c, p);
            var d3 = Cross(c, a, p);
            var negative = d1 < -Epsilon || d2 < -Epsilon || d3 < -Epsilon;
            var positive = d1 > Epsilon || d2 > Epsilon || d3 > Epsilon;
            return !(negative && positive);
        }
    }
}
