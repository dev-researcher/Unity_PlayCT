using System;

namespace PlayCT.Tasks.Gabinete
{
    /// <summary>Position and orientation of a released piece in board space. Yaw is about the vertical axis; an opening accepts a piece at yaw 0.</summary>
    public readonly struct PiecePose
    {
        public float X { get; }
        public float Z { get; }
        public float Height { get; }
        public double YawDegrees { get; }
        public double TiltDegrees { get; }

        public PiecePose(float x, float z, double yawDegrees, double tiltDegrees = 0, float height = 0)
        {
            X = x;
            Z = z;
            YawDegrees = GabineteGeometry.NormalizeDegrees(yawDegrees);
            TiltDegrees = tiltDegrees;
            Height = height;
        }

        /// <summary>The pose of a piece lying exactly in the given opening with the given yaw.</summary>
        public static PiecePose At(int openingIndex, int count, double yawDegrees)
        {
            var c = GabineteLayout.OpeningCenter(openingIndex, count);
            return new PiecePose(c.X, c.Y, yawDegrees);
        }
    }

    public static class GabineteGeometry
    {
        /// <summary>Maps an angle to [0, 360).</summary>
        public static double NormalizeDegrees(double degrees)
        {
            var d = degrees % 360.0;
            if (d < 0) d += 360.0;
            return d >= 360.0 ? 0 : d;
        }

        /// <summary>Smallest angle between two yaws, 0 to 180.</summary>
        public static double AngleBetween(double a, double b)
        {
            var d = NormalizeDegrees(a - b);
            return d > 180.0 ? 360.0 - d : d;
        }

        /// <summary>
        /// How far a piece's yaw is from the nearest yaw its opening accepts: the yaw is folded by the shape's own symmetry
        /// (a rectangle repeats every 180 degrees, a hexagon every 60, a circle always fits).
        /// </summary>
        public static double OrientationError(GabineteShapeSpec shape, double yawDegrees)
        {
            if (shape.SymmetryDegrees <= 0) return 0;
            var period = (double)shape.SymmetryDegrees;
            var folded = NormalizeDegrees(yawDegrees) % period;
            return Math.Min(folded, period - folded);
        }

        public static double Distance(float x1, float z1, float x2, float z2)
        {
            var dx = (double)x1 - x2;
            var dz = (double)z1 - z2;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The opening nearest the pose, or -1 when none is within the capture radius.</summary>
        public static int NearestOpening(PiecePose pose, int count, float captureRadius, out double distance)
        {
            var best = -1;
            distance = double.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var c = GabineteLayout.OpeningCenter(i, count);
                var d = Distance(pose.X, pose.Z, c.X, c.Y);
                if (d <= captureRadius && d < distance)
                {
                    best = i;
                    distance = d;
                }
            }
            if (best < 0) distance = 0;
            return best;
        }
    }
}
