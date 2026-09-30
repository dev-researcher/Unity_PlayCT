using System;
using UnityEngine;

namespace PlayCT.Research
{
    public sealed class UnityClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public double MonotonicSeconds => Time.realtimeSinceStartupAsDouble;
    }
}
