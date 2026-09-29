using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>
    /// Where things sit on the cabinet, in board space: x along the board, z toward the participant (negative),
    /// y up from the board surface. The openings are in one row; the pieces wait on a tray in front of it.
    /// </summary>
    public static class GabineteLayout
    {
        public const int MinPieces = 1;
        public const int MaxPieces = 8;
        public const float Spacing = 0.105f;
        public const float TrayZ = -0.20f;
        public const float BoardDepth = 0.15f;
        public const float BoardThickness = 0.05f;
        public const float PocketDepth = 0.035f;
        public const float SideMargin = 0.03f;
        public const float TrayDepth = 0.12f;
        public const float TrayThickness = 0.012f;

        public static float RowX(int index, int count) => (index - (count - 1) * 0.5f) * Spacing;

        public static Vec2 OpeningCenter(int index, int count) => new Vec2(RowX(index, count), 0f);

        public static Vec2 TrayCenter(int slot, int count) => new Vec2(RowX(slot, count), TrayZ);

        public static float BoardWidth(int count) => count * Spacing + 2 * SideMargin;
    }

    /// <summary>How closely a released piece has to match an opening. All values are placeholders to be calibrated in headset.</summary>
    [Serializable]
    public struct GabineteRules
    {
        /// <summary>A released piece is judged against the nearest opening only if its centre is this close (metres).</summary>
        public float captureRadius;
        /// <summary>Largest horizontal offset from the opening centre for a placement to count (metres).</summary>
        public float positionTolerance;
        /// <summary>Largest yaw error, after allowing for the shape's own symmetry (degrees).</summary>
        public float orientationTolerance;
        /// <summary>Largest tilt away from upright (degrees).</summary>
        public float maxTilt;
        /// <summary>Highest the piece may be above the board surface when released (metres).</summary>
        public float maxReleaseHeight;
        /// <summary>A grab in which the yaw changes by at least this much counts as a rotation (degrees).</summary>
        public float rotationThreshold;

        public static GabineteRules Default => new GabineteRules
        {
            captureRadius = 0.05f,
            positionTolerance = 0.010f,
            orientationTolerance = 10f,
            maxTilt = 25f,
            maxReleaseHeight = 0.08f,
            rotationThreshold = 15f,
        };

        public void Validate()
        {
            if (captureRadius <= 0 || captureRadius >= GabineteLayout.Spacing * 0.5f + 1e-4f)
                throw new InvalidOperationException("Capture radius must be positive and no more than half the opening spacing.");
            if (positionTolerance <= 0 || positionTolerance > captureRadius) throw new InvalidOperationException("Position tolerance must be positive and within the capture radius.");
            if (orientationTolerance <= 0 || orientationTolerance >= 45) throw new InvalidOperationException("Orientation tolerance must be between 0 and 45 degrees.");
            if (maxTilt <= 0 || maxTilt > 90) throw new InvalidOperationException("Maximum tilt must be between 0 and 90 degrees.");
            if (maxReleaseHeight <= 0) throw new InvalidOperationException("Maximum release height must be positive.");
            if (rotationThreshold <= 0) throw new InvalidOperationException("Rotation threshold must be positive.");
        }
    }

    /// <summary>One piece of a trial: which shape it is, which opening takes it, where it waits and how it is turned.</summary>
    public sealed class GabinetePieceSpec
    {
        public string Id { get; }
        public GabineteShapeType Shape { get; }
        public int OpeningIndex { get; }
        public int TraySlot { get; }
        public double StartYawDegrees { get; }

        public GabinetePieceSpec(string id, GabineteShapeType shape, int openingIndex, int traySlot, double startYawDegrees)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Shape = shape;
            OpeningIndex = openingIndex;
            TraySlot = traySlot;
            StartYawDegrees = GabineteGeometry.NormalizeDegrees(startYawDegrees);
        }

        public GabineteShapeSpec ShapeSpec => GabineteShapes.Get(Shape);

        /// <summary>Degrees the participant has to turn the piece (the smaller way round) before its opening accepts it.</summary>
        public double RequiredRotationDegrees => GabineteGeometry.OrientationError(ShapeSpec, StartYawDegrees);
    }

    /// <summary>Everything that defines one trial: the pieces, their openings and trays slots, and the rules. The views read none of the research state.</summary>
    public sealed class GabineteTrialConfig
    {
        public IReadOnlyList<GabinetePieceSpec> Pieces { get; set; } = new GabinetePieceSpec[0];
        public GabineteRules Rules { get; set; } = GabineteRules.Default;

        /// <summary>Maximum trial duration in seconds; 0 means no limit. The participant is never shown a timer.</summary>
        public double DurationSeconds { get; set; }

        public int ShapeCount => Pieces.Count;

        public void Validate()
        {
            if (Pieces == null || Pieces.Count < GabineteLayout.MinPieces || Pieces.Count > GabineteLayout.MaxPieces)
                throw new InvalidOperationException($"A trial needs between {GabineteLayout.MinPieces} and {GabineteLayout.MaxPieces} pieces.");
            if (DurationSeconds < 0) throw new InvalidOperationException("Duration cannot be negative.");
            Rules.Validate();

            var ids = new HashSet<string>();
            var openings = new HashSet<int>();
            var slots = new HashSet<int>();
            foreach (var piece in Pieces)
            {
                if (!ids.Add(piece.Id)) throw new InvalidOperationException($"Duplicate piece id '{piece.Id}'.");
                if (piece.OpeningIndex < 0 || piece.OpeningIndex >= Pieces.Count) throw new InvalidOperationException($"Opening {piece.OpeningIndex} is outside the board.");
                if (piece.TraySlot < 0 || piece.TraySlot >= Pieces.Count) throw new InvalidOperationException($"Tray slot {piece.TraySlot} is outside the tray.");
                if (!openings.Add(piece.OpeningIndex)) throw new InvalidOperationException($"Opening {piece.OpeningIndex} is used twice.");
                if (!slots.Add(piece.TraySlot)) throw new InvalidOperationException($"Tray slot {piece.TraySlot} is used twice.");
            }
        }
    }

    /// <summary>
    /// Text form of a trial for the inspector or a config file. Leave <see cref="shapes"/> empty for the standard
    /// shapes of <see cref="shapeCount"/> (4, 6 or 8); otherwise it lists shape ids in opening order, e.g.
    /// "prism, cylinder, wedge". Tray slots and starting yaws default to the standard deterministic arrangement.
    /// </summary>
    [Serializable]
    public class GabineteTrialSpec
    {
        public int shapeCount = 4;
        public string shapes = "";
        public string traySlots = "";
        public string startYaws = "";
        public float durationSeconds;

        public GabineteTrialConfig ToConfig()
        {
            var types = string.IsNullOrWhiteSpace(shapes)
                ? new List<GabineteShapeType>(GabineteProtocol.StandardShapes(shapeCount))
                : ParseShapes(shapes);
            var count = types.Count;

            var slots = string.IsNullOrWhiteSpace(traySlots) ? GabineteProtocol.StandardTraySlots(count) : ParseInts(traySlots, "tray slots");
            var yaws = string.IsNullOrWhiteSpace(startYaws) ? null : ParseDoubles(startYaws);
            if (slots.Count != count) throw new FormatException($"Expected {count} tray slots but found {slots.Count}.");
            if (yaws != null && yaws.Count != count) throw new FormatException($"Expected {count} starting yaws but found {yaws.Count}.");

            var pieces = new List<GabinetePieceSpec>(count);
            for (var i = 0; i < count; i++)
            {
                var yaw = yaws != null ? yaws[i] : GabineteShapes.Get(types[i]).StartYawDegrees;
                pieces.Add(new GabinetePieceSpec($"piece_{i + 1:D2}_{GabineteShapes.Id(types[i])}", types[i], i, slots[i], yaw));
            }

            var config = new GabineteTrialConfig { Pieces = pieces, DurationSeconds = durationSeconds };
            config.Validate();
            return config;
        }

        static List<GabineteShapeType> ParseShapes(string text)
        {
            var result = new List<GabineteShapeType>();
            foreach (var part in text.Split(','))
            {
                if (!GabineteShapes.TryParse(part, out var type)) throw new FormatException($"Unknown shape '{part.Trim()}'.");
                result.Add(type);
            }
            return result;
        }

        static List<int> ParseInts(string text, string what)
        {
            var result = new List<int>();
            foreach (var part in text.Split(','))
            {
                if (!int.TryParse(part.Trim(), out var value)) throw new FormatException($"Invalid number '{part.Trim()}' in {what}.");
                result.Add(value);
            }
            return result;
        }

        static List<double> ParseDoubles(string text)
        {
            var result = new List<double>();
            foreach (var part in text.Split(','))
            {
                if (!double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    throw new FormatException($"Invalid number '{part.Trim()}' in starting yaws.");
                result.Add(value);
            }
            return result;
        }
    }

    public static class GabineteProtocol
    {
        public static readonly int[] ShapeCounts = { 4, 6, 8 };

        /// <summary>
        /// The shapes for a standard configuration, in opening order: 4 uses a subset of the six defined shapes (two of
        /// which need a quarter turn), 6 uses all of them, 8 adds two more so that eight different pieces exist.
        /// </summary>
        public static IReadOnlyList<GabineteShapeType> StandardShapes(int count)
        {
            switch (count)
            {
                case 4:
                    return new[] { GabineteShapeType.Prism, GabineteShapeType.Cylinder, GabineteShapeType.Wedge, GabineteShapeType.LBlock };
                case 6:
                    return new[]
                    {
                        GabineteShapeType.Prism, GabineteShapeType.Cylinder, GabineteShapeType.TruncatedCone,
                        GabineteShapeType.Wedge, GabineteShapeType.LBlock, GabineteShapeType.Hexagon,
                    };
                case 8:
                    return new[]
                    {
                        GabineteShapeType.Prism, GabineteShapeType.Cylinder, GabineteShapeType.TruncatedCone,
                        GabineteShapeType.Wedge, GabineteShapeType.LBlock, GabineteShapeType.Hexagon,
                        GabineteShapeType.HalfCylinder, GabineteShapeType.TBlock,
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(count), "Standard configurations have 4, 6 or 8 shapes.");
            }
        }

        /// <summary>
        /// Deterministic tray order: piece i waits in slot (i * step) mod n. The step is coprime with n, so the pieces
        /// never wait in the same order as their openings.
        /// </summary>
        public static List<int> StandardTraySlots(int count)
        {
            var step = 1;
            for (var candidate = 3; candidate < count; candidate += 2)
            {
                if (Gcd(candidate, count) == 1)
                {
                    step = candidate;
                    break;
                }
            }
            var slots = new List<int>(count);
            for (var i = 0; i < count; i++) slots.Add(i * step % count);
            return slots;
        }

        public static GabineteTrialSpec SpecFor(int shapeCount, float durationSeconds = 0) =>
            new GabineteTrialSpec { shapeCount = shapeCount, durationSeconds = durationSeconds };

        /// <summary>The default protocol: the 4, 6 and 8 shape cabinets in order.</summary>
        public static IReadOnlyList<GabineteTrialSpec> DefaultSpecs() => new[]
        {
            SpecFor(4, 300),
            SpecFor(6, 420),
            SpecFor(8, 540),
        };

        public static GabineteTrialConfig ForShapeCount(int count) => SpecFor(count).ToConfig();

        public static List<GabineteTrialConfig> Default()
        {
            var configs = new List<GabineteTrialConfig>();
            foreach (var spec in DefaultSpecs()) configs.Add(spec.ToConfig());
            return configs;
        }

        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
    }
}
