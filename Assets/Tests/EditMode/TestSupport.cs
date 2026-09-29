using System;
using System.Collections.Generic;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    public sealed class FakeClock : IClock
    {
        public DateTime Now = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);
        public double Seconds = 100.0;

        public DateTime UtcNow => Now;
        public double MonotonicSeconds => Seconds;

        public void Advance(double seconds)
        {
            Seconds += seconds;
            Now = Now.AddSeconds(seconds);
        }
    }

    public sealed class MemorySink : IResearchEventSink
    {
        public readonly List<ResearchEvent> Events = new List<ResearchEvent>();

        public void Log(ResearchEvent researchEvent) => Events.Add(researchEvent);

        public IEnumerable<ResearchEvent> OfType(string type)
        {
            foreach (var e in Events)
                if (e.EventType == type) yield return e;
        }

        public static object Field(ResearchEvent e, string key)
        {
            foreach (var kv in e.Fields)
                if (kv.Key == key) return kv.Value;
            throw new KeyNotFoundException($"Event '{e.EventType}' has no field '{key}'.");
        }
    }

    public static class HanoiSolver
    {
        /// <summary>Optimal solution as (disk, from, to) triples.</summary>
        public static List<(int disk, string from, string to)> Solve(int n)
        {
            var moves = new List<(int, string, string)>();
            Recurse(n, HanoiPegs.Origen, HanoiPegs.Destino, HanoiPegs.Apoyo, moves);
            return moves;
        }

        static void Recurse(int n, string from, string to, string via, List<(int, string, string)> moves)
        {
            if (n == 0) return;
            Recurse(n - 1, from, via, to, moves);
            moves.Add((n, from, to));
            Recurse(n - 1, via, to, from, moves);
        }

        public static void Play(HanoiTrial trial, int n)
        {
            foreach (var (disk, _, to) in Solve(n))
            {
                if (!trial.OnGrab(disk)) throw new InvalidOperationException($"Grab of D{disk} refused.");
                trial.OnRelease(disk, to);
            }
        }
    }
}
