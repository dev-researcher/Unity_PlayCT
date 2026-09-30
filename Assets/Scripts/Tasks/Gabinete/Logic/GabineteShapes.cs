using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Gabinete
{
    public readonly struct Vec2
    {
        public float X { get; }
        public float Y { get; }

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Small helpers for simple polygons in the (x, z) plane of the board.</summary>
    public static class Polygon2D
    {
        /// <summary>Shoelace area; positive when the vertices run counter-clockwise (x to the right, y up).</summary>
        public static double SignedArea(IReadOnlyList<Vec2> polygon)
        {
            double sum = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return sum * 0.5;
        }

        public static Vec2 Centroid(IReadOnlyList<Vec2> polygon)
        {
            double cx = 0, cy = 0, area2 = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var cross = (double)a.X * b.Y - (double)b.X * a.Y;
                area2 += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }
            return new Vec2((float)(cx / (3 * area2)), (float)(cy / (3 * area2)));
        }

        public static List<Vec2> CounterClockwise(IReadOnlyList<Vec2> polygon)
        {
            var result = new List<Vec2>(polygon);
            if (SignedArea(result) < 0) result.Reverse();
            return result;
        }

        /// <summary>Counter-clockwise copy whose area centroid is the origin.</summary>
        public static List<Vec2> Recentered(IReadOnlyList<Vec2> polygon)
        {
            var ccw = CounterClockwise(polygon);
            var c = Centroid(ccw);
            for (var i = 0; i < ccw.Count; i++) ccw[i] = new Vec2(ccw[i].X - c.X, ccw[i].Y - c.Y);
            return ccw;
        }

        public static List<Vec2> Scaled(IReadOnlyList<Vec2> polygon, float scale, Vec2 offset = default)
        {
            var result = new List<Vec2>(polygon.Count);
            foreach (var p in polygon) result.Add(new Vec2(p.X * scale + offset.X, p.Y * scale + offset.Y));
            return result;
        }

        public static List<Vec2> Regular(int sides, float radius, double startAngle = 0)
        {
            var result = new List<Vec2>(sides);
            for (var i = 0; i < sides; i++)
            {
                var angle = startAngle + i * 2 * Math.PI / sides;
                result.Add(new Vec2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius)));
            }
            return result;
        }

        public static List<Vec2> Rectangle(float width, float depth)
        {
            var w = width * 0.5f;
            var d = depth * 0.5f;
            return new List<Vec2> { new Vec2(-w, -d), new Vec2(w, -d), new Vec2(w, d), new Vec2(-w, d) };
        }

        public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>
    /// The shapes the cabinet can hold. The first six are the required set; the last two only exist so that the
    /// 8-shape configuration has eight different pieces.
    /// </summary>
    public enum GabineteShapeType
    {
        Prism,
        Cylinder,
        TruncatedCone,
        Wedge,
        LBlock,
        Hexagon,
        HalfCylinder,
        TBlock,
    }

    /// <summary>
    /// One physical shape: its footprint (centred on its centroid, so the piece origin is the centroid), height, taper,
    /// how it repeats when turned about the vertical axis, and the orientation it starts in on the tray.
    /// </summary>
    public sealed class GabineteShapeSpec
    {
        public GabineteShapeType Type { get; }
        public string Id { get; }
        public IReadOnlyList<Vec2> Footprint { get; }
        public float Height { get; }

        /// <summary>Footprint scale at the bottom and top of the piece (a truncated cone is narrower at the bottom).</summary>
        public float BaseScale { get; }
        public float TopScale { get; }

        /// <summary>
        /// Turning the piece by this many degrees about the vertical axis gives the same footprint. 360 means it fits
        /// in one orientation only; 0 means every orientation fits (a circle).
        /// </summary>
        public int SymmetryDegrees { get; }

        /// <summary>Yaw the piece has on the tray, relative to the yaw its opening accepts.</summary>
        public double StartYawDegrees { get; }

        public GabineteShapeSpec(GabineteShapeType type, string id, IReadOnlyList<Vec2> footprint, float height, float baseScale, float topScale,
            int symmetryDegrees, double startYawDegrees)
        {
            Type = type;
            Id = id;
            Footprint = Polygon2D.Recentered(footprint);
            Height = height;
            BaseScale = baseScale;
            TopScale = topScale;
            SymmetryDegrees = symmetryDegrees;
            StartYawDegrees = startYawDegrees;
        }

        public float ScaleAtHeight(float y) => BaseScale + (TopScale - BaseScale) * (y / Height);
    }

    public static class GabineteShapes
    {
        public const float PieceHeight = 0.06f;
        /// <summary>Openings are this much larger than the piece footprint, so a correctly placed piece drops in with a little play.</summary>
        public const float OpeningClearance = 1.04f;

        static readonly Dictionary<GabineteShapeType, GabineteShapeSpec> catalog = Build();

        public static GabineteShapeSpec Get(GabineteShapeType type) => catalog[type];

        public static string Id(GabineteShapeType type) => catalog[type].Id;

        public static bool TryParse(string text, out GabineteShapeType type)
        {
            type = GabineteShapeType.Prism;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var key = text.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
            foreach (var spec in catalog.Values)
            {
                if (spec.Id.Replace("_", "") == key || spec.Type.ToString().ToLowerInvariant() == key)
                {
                    type = spec.Type;
                    return true;
                }
            }
            return false;
        }

        static Dictionary<GabineteShapeType, GabineteShapeSpec> Build()
        {
            var h = PieceHeight;
            var specs = new[]
            {
                // Long thin block: fits its opening in two orientations 180 degrees apart, so 90 degrees off is wrong.
                new GabineteShapeSpec(GabineteShapeType.Prism, "prism", Polygon2D.Rectangle(0.070f, 0.034f), h, 1f, 1f, 180, 90),
                new GabineteShapeSpec(GabineteShapeType.Cylinder, "cylinder", Polygon2D.Regular(32, 0.024f), h, 1f, 1f, 0, 0),
                new GabineteShapeSpec(GabineteShapeType.TruncatedCone, "truncated_cone", Polygon2D.Regular(32, 0.036f), h, 0.55f, 1f, 0, 0),
                new GabineteShapeSpec(GabineteShapeType.Wedge, "wedge",
                    new List<Vec2> { new Vec2(0, 0), new Vec2(0.070f, 0), new Vec2(0, 0.040f) }, h, 1f, 1f, 360, 0),
                new GabineteShapeSpec(GabineteShapeType.LBlock, "l_block",
                    new List<Vec2>
                    {
                        new Vec2(0, 0), new Vec2(0.070f, 0), new Vec2(0.070f, 0.028f),
                        new Vec2(0.028f, 0.028f), new Vec2(0.028f, 0.070f), new Vec2(0, 0.070f),
                    }, h, 1f, 1f, 360, 90),
                new GabineteShapeSpec(GabineteShapeType.Hexagon, "hexagon", Polygon2D.Regular(6, 0.035f), h, 1f, 1f, 60, 30),
                new GabineteShapeSpec(GabineteShapeType.HalfCylinder, "half_cylinder", HalfDisc(0.034f, 16), h, 1f, 1f, 360, 0),
                new GabineteShapeSpec(GabineteShapeType.TBlock, "t_block",
                    TShape(), h, 1f, 1f, 360, 90),
            };
            var map = new Dictionary<GabineteShapeType, GabineteShapeSpec>();
            foreach (var spec in specs) map[spec.Type] = spec;
            return map;
        }

        static List<Vec2> HalfDisc(float radius, int segments)
        {
            var points = new List<Vec2>();
            for (var i = 0; i <= segments; i++)
            {
                var angle = Math.PI * i / segments;
                points.Add(new Vec2((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius)));
            }
            return points;
        }

        static List<Vec2> TShape()
        {
            // Bar 0.070 x 0.024 on top of a stem 0.024 x 0.046.
            return new List<Vec2>
            {
                new Vec2(0.023f, 0), new Vec2(0.047f, 0), new Vec2(0.047f, 0.046f), new Vec2(0.070f, 0.046f),
                new Vec2(0.070f, 0.070f), new Vec2(0, 0.070f), new Vec2(0, 0.046f), new Vec2(0.023f, 0.046f),
            };
        }

        /// <summary>Footprint scale of the opening at the board surface and at its floor for a piece seated to <paramref name="pocketDepth"/>.</summary>
        public static (float top, float bottom) OpeningScales(GabineteShapeSpec shape, float pocketDepth)
        {
            var top = shape.ScaleAtHeight(Math.Min(pocketDepth, shape.Height)) * OpeningClearance;
            var bottom = shape.BaseScale * OpeningClearance;
            return (top, bottom);
        }
    }
}
