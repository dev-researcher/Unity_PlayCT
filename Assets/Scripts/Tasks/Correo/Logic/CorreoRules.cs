using System.Collections.Generic;
using System.Linq;

namespace PlayCT.Tasks.Correo
{
    /// <summary>Why a shipment was refused, in a form that can be counted. It is only ever logged, never shown to the participant.</summary>
    public static class RejectionCategory
    {
        public const string Path = "path";
        public const string Selection = "selection";
        public const string Capacity = "capacity";
        public const string Constraint = "constraint";
    }

    public static class RejectionReason
    {
        public const string NoPath = "no_path";
        public const string NoPackages = "no_packages";
        public const string PackageNotAtSource = "package_not_at_source";
        public const string CapacityExceeded = "capacity_exceeded";
        public const string P1AndP3Together = "p1_p3_shared";
        public const string P1WaitingInD = "p1_waiting_in_d";
        public const string PriorityViolation = "priority_violation";
    }

    public readonly struct ShipmentVerdict
    {
        public bool Valid { get; }
        public string Reason { get; }
        public string Category { get; }

        /// <summary>Capacity of the selected path, or null when there is no such path.</summary>
        public int? PathCapacity { get; }

        public ShipmentVerdict(bool valid, string reason, string category, int? pathCapacity)
        {
            Valid = valid;
            Reason = reason;
            Category = category;
            PathCapacity = pathCapacity;
        }
    }

    /// <summary>The experimental rules of El Correo, decided purely from the network, the current state and the shipment.</summary>
    public static class CorreoRules
    {
        /// <summary>
        /// Checks a shipment in the order: path exists, packages selected and present at the source, capacity, package
        /// restrictions (P1 and P3 apart), and finally the state that would result (P1 may never be left waiting in D; no
        /// low-priority package reaches E before P1 has).
        /// </summary>
        public static ShipmentVerdict Evaluate(CorreoNetwork network, CorreoState state, CorreoShipment shipment)
        {
            if (!network.TryGetPath(shipment.Source, shipment.Destination, out var path))
                return Reject(RejectionReason.NoPath, RejectionCategory.Path, null);

            var capacity = path.Capacity;
            if (shipment.Packages.Count == 0)
                return Reject(RejectionReason.NoPackages, RejectionCategory.Selection, capacity);
            if (shipment.Packages.Any(p => !state.IsAt(p, shipment.Source)))
                return Reject(RejectionReason.PackageNotAtSource, RejectionCategory.Selection, capacity);
            if (shipment.Packages.Count > capacity)
                return Reject(RejectionReason.CapacityExceeded, RejectionCategory.Capacity, capacity);

            if (shipment.Contains(CorreoPackage.P1) && shipment.Contains(CorreoPackage.P3))
                return Reject(RejectionReason.P1AndP3Together, RejectionCategory.Constraint, capacity);

            var after = state.Apply(shipment);
            // No shipment runs on from D in the same action, so any shipment that ends with P1 in D leaves it waiting there.
            if (after.IsAt(CorreoPackage.P1, Settlement.D))
                return Reject(RejectionReason.P1WaitingInD, RejectionCategory.Constraint, capacity);
            if (!after.PrioritySatisfied)
                return Reject(RejectionReason.PriorityViolation, RejectionCategory.Constraint, capacity);

            return new ShipmentVerdict(true, "shipped", null, capacity);
        }

        /// <summary>Replays a shipment sequence from the initial state; false if any shipment would have been refused.</summary>
        public static bool TryReplay(CorreoNetwork network, IEnumerable<CorreoShipment> shipments, out CorreoState finalState)
        {
            finalState = CorreoState.Initial;
            foreach (var shipment in shipments)
            {
                if (!Evaluate(network, finalState, shipment).Valid) return false;
                finalState = finalState.Apply(shipment);
            }
            return true;
        }

        static ShipmentVerdict Reject(string reason, string category, int? capacity) => new ShipmentVerdict(false, reason, category, capacity);
    }
}
