using System;
using System.Diagnostics;

namespace PlayCT.Research
{
    public interface IClock
    {
        DateTime UtcNow { get; }
        double MonotonicSeconds { get; }
    }

    public sealed class SystemClock : IClock
    {
        static readonly Stopwatch Watch = Stopwatch.StartNew();

        public DateTime UtcNow => DateTime.UtcNow;
        public double MonotonicSeconds => Watch.Elapsed.TotalSeconds;
    }
}
