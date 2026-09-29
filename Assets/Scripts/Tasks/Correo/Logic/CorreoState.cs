using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayCT.Tasks.Correo
{
    public enum CorreoPackage
    {
        P1,
        P2,
        P3,
    }

    public static class CorreoPackages
    {
        public static readonly IReadOnlyList<CorreoPackage> All = new[] { CorreoPackage.P1, CorreoPackage.P2, CorreoPackage.P3 };

        public static string Id(CorreoPackage package) => package.ToString();

        /// <summary>P1 is the only high-priority package; P2 and P3 are low priority.</summary>
        public static bool IsHighPriority(CorreoPackage package) => package == CorreoPackage.P1;

        public static string PriorityLabel(CorreoPackage package) => IsHighPriority(package) ? "high" : "low";

        public static bool TryParse(string text, out CorreoPackage package)
        {
            package = CorreoPackage.P1;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.ToString(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    package = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>One shipment: a source, a destination and one or more packages, all moved together.</summary>
    public sealed class CorreoShipment
    {
        public Settlement Source { get; }
        public Settlement Destination { get; }
        public IReadOnlyList<CorreoPackage> Packages { get; }

        public CorreoShipment(Settlement source, Settlement destination, IEnumerable<CorreoPackage> packages)
        {
            Source = source;
            Destination = destination;
            Packages = (packages ?? throw new ArgumentNullException(nameof(packages))).Distinct().OrderBy(p => p).ToList();
        }

        public bool Contains(CorreoPackage package) => Packages.Contains(package);

        public IReadOnlyList<string> PackageIds => Packages.Select(CorreoPackages.Id).ToList();

        public string Label => $"{Source}>{Destination}:{string.Join("+", PackageIds)}";
    }

    /// <summary>
    /// The logical state of the postal task: where each package is, how many shipments have been made, and in which
    /// shipment each package reached E. Immutable; a shipment produces a new state. No Unity types.
    /// </summary>
    public sealed class CorreoState : IEquatable<CorreoState>
    {
        readonly Settlement[] locations;
        readonly int[] arrivals;

        public int ShipmentCount { get; }

        CorreoState(Settlement[] locations, int[] arrivals, int shipmentCount)
        {
            this.locations = locations;
            this.arrivals = arrivals;
            ShipmentCount = shipmentCount;
        }

        /// <summary>Every package waits at A and no shipment has been made.</summary>
        public static CorreoState Initial { get; } = new CorreoState(
            new[] { Settlements.Origin, Settlements.Origin, Settlements.Origin }, new int[3], 0);

        public Settlement Location(CorreoPackage package) => locations[(int)package];

        public bool IsAt(CorreoPackage package, Settlement settlement) => Location(package) == settlement;

        public bool IsDelivered(CorreoPackage package) => Location(package) == Settlements.Destination;

        public bool AllDelivered => CorreoPackages.All.All(IsDelivered);

        /// <summary>The number of the shipment in which the package reached E, or null while it has not.</summary>
        public int? ArrivalShipment(CorreoPackage package) => arrivals[(int)package] > 0 ? arrivals[(int)package] : (int?)null;

        public IReadOnlyList<CorreoPackage> PackagesAt(Settlement settlement) => CorreoPackages.All.Where(p => IsAt(p, settlement)).ToList();

        /// <summary>
        /// The priority constraint as counted in shipments: P1 reached E in a shipment no later than any low-priority
        /// package did. Holds while no low-priority package has arrived before P1.
        /// </summary>
        public bool PrioritySatisfied
        {
            get
            {
                var p1 = ArrivalShipment(CorreoPackage.P1);
                foreach (var package in CorreoPackages.All)
                {
                    if (CorreoPackages.IsHighPriority(package)) continue;
                    var arrival = ArrivalShipment(package);
                    if (!arrival.HasValue) continue;
                    if (!p1.HasValue || p1.Value > arrival.Value) return false;
                }
                return true;
            }
        }

        /// <summary>Moves the shipment's packages to its destination. The caller has already checked that the shipment is valid.</summary>
        public CorreoState Apply(CorreoShipment shipment)
        {
            var newLocations = (Settlement[])locations.Clone();
            var newArrivals = (int[])arrivals.Clone();
            var count = ShipmentCount + 1;
            foreach (var package in shipment.Packages)
            {
                newLocations[(int)package] = shipment.Destination;
                if (shipment.Destination == Settlements.Destination) newArrivals[(int)package] = count;
            }
            return new CorreoState(newLocations, newArrivals, count);
        }

        /// <summary>Compact text form, for example "P1:C,P2:A,P3:A".</summary>
        public string Snapshot() => string.Join(",", CorreoPackages.All.Select(p => $"{CorreoPackages.Id(p)}:{Location(p)}"));

        public bool Equals(CorreoState other) =>
            other != null && ShipmentCount == other.ShipmentCount && locations.SequenceEqual(other.locations) && arrivals.SequenceEqual(other.arrivals);

        public override bool Equals(object obj) => Equals(obj as CorreoState);

        public override int GetHashCode()
        {
            var hash = ShipmentCount;
            foreach (var l in locations) hash = hash * 31 + (int)l;
            foreach (var a in arrivals) hash = hash * 31 + a;
            return hash;
        }

        public override string ToString() => $"#{ShipmentCount} {Snapshot()}";
    }
}
