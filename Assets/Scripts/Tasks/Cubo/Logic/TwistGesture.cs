using System;

namespace PlayCT.Tasks.Cubo
{
    public enum GestureKind
    {
        /// <summary>Movement that means neither a tap nor a clear twist; nothing happens.</summary>
        Ignored,
        Tap,
        Clockwise,
        CounterClockwise,
    }

    /// <summary>
    /// Turns what the selecting hand did on a face into a discrete intent: a tap selects the face, a clear twist
    /// around the face centre asks for a quarter turn in that direction. The hand cannot rotate anything freely.
    /// </summary>
    [Serializable]
    public struct TwistGestureSettings
    {
        public float tapMaxTravel;
        public float minTwistAngle;
        public float minStartRadius;

        public static TwistGestureSettings Default => new TwistGestureSettings { tapMaxTravel = 0.03f, minTwistAngle = 25f, minStartRadius = 0.02f };
    }

    public static class TwistGesture
    {
        /// <param name="signedAngleDegrees">Angle swept around the face centre from start to end; positive = clockwise seen from outside.</param>
        /// <param name="travel">Straight-line distance the hand moved in the plane of the face, in metres.</param>
        /// <param name="startRadius">Distance from the face centre to where the gesture started, in metres.</param>
        public static GestureKind Classify(double signedAngleDegrees, double travel, double startRadius, TwistGestureSettings settings)
        {
            if (travel <= settings.tapMaxTravel) return GestureKind.Tap;
            if (startRadius < settings.minStartRadius) return GestureKind.Ignored;
            if (signedAngleDegrees >= settings.minTwistAngle) return GestureKind.Clockwise;
            if (signedAngleDegrees <= -settings.minTwistAngle) return GestureKind.CounterClockwise;
            return GestureKind.Ignored;
        }
    }
}
