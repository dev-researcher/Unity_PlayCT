using System;
using System.Collections.Generic;

namespace PlayCT.Tasks.Correo
{
    public enum Settlement
    {
        A,
        B,
        C,
        D,
        E,
    }

    public static class Settlements
    {
        public static readonly Settlement Origin = Settlement.A;
        public static readonly Settlement Destination = Settlement.E;

        public static readonly IReadOnlyList<Settlement> All = new[] { Settlement.A, Settlement.B, Settlement.C, Settlement.D, Settlement.E };

        public static string Letter(Settlement settlement) => settlement.ToString();

        public static bool TryParse(string text, out Settlement settlement)
        {
            settlement = Settlement.A;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    settlement = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>A directed path between two settlements and the most packages one shipment may carry along it.</summary>
    public sealed class CorreoPath
    {
        public Settlement From { get; }
        public Settlement To { get; }
        public int Capacity { get; }

        public CorreoPath(Settlement from, Settlement to, int capacity)
        {
            if (from == to) throw new ArgumentException("A path connects two different settlements.");
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "A path needs a capacity of at least 1.");
            From = from;
            To = to;
            Capacity = capacity;
        }

        public string Label => $"{From}>{To}:{Capacity}";
    }

    /// <summary>
    /// The postal network: which directed paths exist and how many packages each carries. It is fixed data, never
    /// inferred or changed while a task runs, and it knows nothing about how the network is drawn.
    /// </summary>
    public sealed class CorreoNetwork
    {
        readonly List<CorreoPath> paths;
        readonly Dictionary<(Settlement, Settlement), CorreoPath> lookup = new Dictionary<(Settlement, Settlement), CorreoPath>();

        public IReadOnlyList<CorreoPath> Paths => paths;

        public CorreoNetwork(IEnumerable<CorreoPath> paths)
        {
            this.paths = new List<CorreoPath>(paths ?? throw new ArgumentNullException(nameof(paths)));
            foreach (var path in this.paths)
            {
                if (lookup.ContainsKey((path.From, path.To))) throw new ArgumentException($"Path {path.From}>{path.To} is defined twice.");
                lookup[(path.From, path.To)] = path;
            }
        }

        public bool TryGetPath(Settlement from, Settlement to, out CorreoPath path) => lookup.TryGetValue((from, to), out path);

        public bool HasPath(Settlement from, Settlement to) => lookup.ContainsKey((from, to));

        public IReadOnlyList<string> Labels()
        {
            var labels = new List<string>();
            foreach (var path in paths) labels.Add(path.Label);
            return labels;
        }

        /// <summary>The experimental network: A>B 2, A>C 1, B>D 2, C>D 1, C>E 2, D>E 1. There is no B>C path.</summary>
        public static CorreoNetwork Standard { get; } = new CorreoNetwork(new[]
        {
            new CorreoPath(Settlement.A, Settlement.B, 2),
            new CorreoPath(Settlement.A, Settlement.C, 1),
            new CorreoPath(Settlement.B, Settlement.D, 2),
            new CorreoPath(Settlement.C, Settlement.D, 1),
            new CorreoPath(Settlement.C, Settlement.E, 2),
            new CorreoPath(Settlement.D, Settlement.E, 1),
        });
    }
}
