using System;
using System.Collections.Generic;
using System.Linq;
using PlayCT.Research;

namespace PlayCT.Tasks.Correo
{
    public readonly struct CorreoOutcome
    {
        /// <summary>False when nothing was recorded (trial not running, or the selection was incomplete).</summary>
        public bool Accepted { get; }
        public bool Valid { get; }
        public bool Completed { get; }
        public string Reason { get; }
        public string Category { get; }
        public CorreoShipment Shipment { get; }

        public CorreoOutcome(bool accepted, bool valid, bool completed, string reason, string category, CorreoShipment shipment)
        {
            Accepted = accepted;
            Valid = valid;
            Completed = completed;
            Reason = reason;
            Category = category;
            Shipment = shipment;
        }

        public static CorreoOutcome NotAccepted(string reason) => new CorreoOutcome(false, false, false, reason, null, null);
    }

    /// <summary>
    /// One El Correo trial. Pure logic: it holds the package locations and the participant's pending selection, decides each
    /// shipment with <see cref="CorreoRules"/>, and writes the log events and metrics. A refused shipment never changes the
    /// state; nothing is moved or optimised for the participant. It knows nothing about Unity, XR or how the network is drawn.
    /// </summary>
    public sealed class CorreoTrial
    {
        public const string TaskName = "ElCorreo";

        readonly CorreoTrialConfig config;
        readonly IResearchEventSink sink;
        readonly IClock clock;
        readonly List<CorreoShipment> shipments = new List<CorreoShipment>();
        readonly List<string> actionSequence = new List<string>();
        readonly HashSet<CorreoPackage> selectedPackages = new HashSet<CorreoPackage>();
        readonly double?[] arrivalTimes = new double?[3];

        double beginTime;
        double? firstActionTime;
        double? completionTime;
        int attempts;
        int invalid;
        int rejectedCapacity;
        int rejectedConstraint;
        int rejectedPath;
        int rejectedSelection;
        bool begun;
        bool ended;

        public int TrialIndex { get; }
        public CorreoNetwork Network => config.Network;
        public CorreoState State { get; private set; } = CorreoState.Initial;
        public Settlement? SelectedSource { get; private set; }
        public Settlement? SelectedDestination { get; private set; }
        public IReadOnlyList<CorreoPackage> SelectedPackages => selectedPackages.OrderBy(p => p).ToList();
        public int ShipmentAttempts => attempts;
        public int ValidShipments => shipments.Count;
        public int InvalidShipments => invalid;
        public bool IsCompleted { get; private set; }
        public bool IsRunning => begun && !ended && !IsCompleted;
        public bool IsFinished => ended || IsCompleted;
        public IReadOnlyList<CorreoShipment> Shipments => shipments;
        public IReadOnlyList<string> ActionSequence => actionSequence;

        public CorreoTrial(CorreoTrialConfig config, IResearchEventSink sink, IClock clock, int trialIndex = 1)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            config.Validate();
            TrialIndex = trialIndex;
        }

        public void Begin()
        {
            if (begun) throw new InvalidOperationException("Trial already begun.");
            begun = true;
            beginTime = clock.MonotonicSeconds;

            var priorities = CorreoPackages.All.Select(p => $"{CorreoPackages.Id(p)}:{CorreoPackages.PriorityLabel(p)}").ToList();
            sink.Log(NewEvent("trial_started")
                .Add("trial_index", TrialIndex)
                .Add("network", config.Network.Labels())
                .Add("packages", priorities)
                .Add("time_limit_s", config.DurationSeconds)
                .Add("shipment_number", 0)
                .Add("package_locations", State.Snapshot())
                .Add("completion_status", "in_progress"));
        }

        /// <summary>
        /// The participant touched a settlement. The first touch chooses the source; touching the source again clears the
        /// choice; touching another settlement chooses (or, if it is already chosen, clears) the destination. Nothing is
        /// checked here: the participant may choose any settlement, including ones with no path.
        /// </summary>
        public bool TapSettlement(Settlement settlement)
        {
            if (!IsRunning) return false;
            NoteFirstAction();
            string change;
            if (!SelectedSource.HasValue)
            {
                SelectedSource = settlement;
                change = "source_selected";
            }
            else if (SelectedSource.Value == settlement)
            {
                SelectedSource = null;
                SelectedDestination = null;
                change = "source_cleared";
            }
            else if (SelectedDestination == settlement)
            {
                SelectedDestination = null;
                change = "destination_cleared";
            }
            else
            {
                SelectedDestination = settlement;
                change = "destination_selected";
            }
            LogSelection(change, settlement.ToString());
            return true;
        }

        /// <summary>The participant touched a package: it joins the shipment, or leaves it if it was already selected.</summary>
        public bool TogglePackage(CorreoPackage package)
        {
            if (!IsRunning) return false;
            NoteFirstAction();
            var added = selectedPackages.Add(package);
            if (!added) selectedPackages.Remove(package);
            LogSelection(added ? "package_added" : "package_removed", CorreoPackages.Id(package));
            return true;
        }

        public bool ClearSelection()
        {
            if (!IsRunning) return false;
            NoteFirstAction();
            var hadSelection = SelectedSource.HasValue || SelectedDestination.HasValue || selectedPackages.Count > 0;
            ResetSelection();
            if (hadSelection) LogSelection("selection_cleared", null);
            return true;
        }

        /// <summary>
        /// The participant confirmed the shipment. It needs a source, a destination and at least one package chosen; otherwise
        /// nothing is attempted and the press is only logged. The selection is cleared afterwards either way.
        /// </summary>
        public CorreoOutcome Confirm()
        {
            if (!IsRunning) return CorreoOutcome.NotAccepted("not_running");
            NoteFirstAction();
            if (!SelectedSource.HasValue || !SelectedDestination.HasValue || selectedPackages.Count == 0)
            {
                sink.Log(TrialEvent("confirm_ignored")
                    .Add("source", SelectedSource?.ToString())
                    .Add("destination", SelectedDestination?.ToString())
                    .Add("selected_packages", SelectedPackages.Select(CorreoPackages.Id).ToList())
                    .Add("reason", "incomplete_selection"));
                return CorreoOutcome.NotAccepted("incomplete_selection");
            }

            var shipment = new CorreoShipment(SelectedSource.Value, SelectedDestination.Value, selectedPackages);
            ResetSelection();
            return Ship(shipment);
        }

        public CorreoOutcome Ship(Settlement source, Settlement destination, IEnumerable<CorreoPackage> packages) =>
            Ship(new CorreoShipment(source, destination, packages));

        /// <summary>Attempts one shipment. A valid one moves all its packages at once; a refused one leaves the state as it was.</summary>
        public CorreoOutcome Ship(CorreoShipment shipment)
        {
            if (!IsRunning) return CorreoOutcome.NotAccepted("not_running");
            NoteFirstAction();
            attempts++;

            var before = State;
            var verdict = CorreoRules.Evaluate(config.Network, before, shipment);
            var delivered = new List<string>();

            if (verdict.Valid)
            {
                State = before.Apply(shipment);
                shipments.Add(shipment);
                actionSequence.Add(shipment.Label);
                var now = clock.MonotonicSeconds;
                foreach (var package in shipment.Packages)
                {
                    if (!State.IsDelivered(package)) continue;
                    arrivalTimes[(int)package] = Round(now - beginTime);
                    delivered.Add(CorreoPackages.Id(package));
                }
            }
            else
            {
                invalid++;
                switch (verdict.Category)
                {
                    case RejectionCategory.Capacity: rejectedCapacity++; break;
                    case RejectionCategory.Constraint: rejectedConstraint++; break;
                    case RejectionCategory.Path: rejectedPath++; break;
                    default: rejectedSelection++; break;
                }
                actionSequence.Add($"{shipment.Label}:rejected:{verdict.Reason}");
            }

            var completed = verdict.Valid && State.AllDelivered && State.PrioritySatisfied;
            if (completed) MarkCompleted();

            sink.Log(TrialEvent("shipment_attempt")
                .Add("attempt_number", attempts)
                .Add("source", shipment.Source.ToString())
                .Add("destination", shipment.Destination.ToString())
                .Add("selected_packages", shipment.PackageIds)
                .Add("selected_count", shipment.Packages.Count)
                .Add("path_capacity", verdict.PathCapacity)
                .Add("valid", verdict.Valid)
                .Add("outcome", verdict.Reason)
                .Add("rejection_category", verdict.Category)
                .Add("shipment_number", State.ShipmentCount)
                .Add("delivered_packages", delivered)
                .Add("locations_before", before.Snapshot())
                .Add("locations_after", State.Snapshot()));
            if (completed) EmitCompletion();
            return new CorreoOutcome(true, verdict.Valid, completed, verdict.Reason, verdict.Category, shipment);
        }

        /// <summary>Ends the trial if its duration has run out. Returns true when it was ended by the time limit.</summary>
        public bool CheckTimeLimit()
        {
            if (!IsRunning || config.DurationSeconds <= 0) return false;
            if (clock.MonotonicSeconds - beginTime < config.DurationSeconds) return false;
            End("time_limit");
            return true;
        }

        /// <summary>Ends a trial that was not completed (time limit, researcher stop, end of session).</summary>
        public void End(string reason)
        {
            if (!begun || ended || IsCompleted) return;
            ended = true;
            ResetSelection();
            var e = NewEvent("trial_summary");
            BuildSummary().AddTo(e);
            e.Add("end_reason", reason);
            sink.Log(e);
        }

        public CorreoSummary BuildSummary()
        {
            var now = clock.MonotonicSeconds;
            var summary = new CorreoSummary
            {
                TrialIndex = TrialIndex,
                ShipmentAttempts = attempts,
                ValidShipments = shipments.Count,
                InvalidShipments = invalid,
                ShipmentCount = State.ShipmentCount,
                RejectedCapacityAttempts = rejectedCapacity,
                RejectedConstraintAttempts = rejectedConstraint,
                RejectedPathAttempts = rejectedPath,
                RejectedSelectionAttempts = rejectedSelection,
                FinalLocations = State.Snapshot(),
                P1ArrivalShipment = State.ArrivalShipment(CorreoPackage.P1),
                P2ArrivalShipment = State.ArrivalShipment(CorreoPackage.P2),
                P3ArrivalShipment = State.ArrivalShipment(CorreoPackage.P3),
                P1ArrivalTimeSeconds = arrivalTimes[(int)CorreoPackage.P1],
                P2ArrivalTimeSeconds = arrivalTimes[(int)CorreoPackage.P2],
                P3ArrivalTimeSeconds = arrivalTimes[(int)CorreoPackage.P3],
                PrioritySatisfied = State.PrioritySatisfied,
                TimeLimitSeconds = config.DurationSeconds,
                Completed = IsCompleted,
                Status = IsCompleted ? "completed" : (ended ? "incomplete" : "in_progress"),
                ElapsedSeconds = Round(begun ? (completionTime ?? now) - beginTime : 0),
                ShipmentSequence = shipments.Select(s => s.Label).ToList(),
                ActionSequence = new List<string>(actionSequence),
            };
            if (firstActionTime.HasValue) summary.FirstActionLatencySeconds = Round(firstActionTime.Value - beginTime);
            if (IsCompleted) summary.CompletionTimeSeconds = Round(completionTime.Value - beginTime);
            return summary;
        }

        void ResetSelection()
        {
            SelectedSource = null;
            SelectedDestination = null;
            selectedPackages.Clear();
        }

        void MarkCompleted()
        {
            IsCompleted = true;
            completionTime = clock.MonotonicSeconds;
        }

        void NoteFirstAction()
        {
            if (!firstActionTime.HasValue) firstActionTime = clock.MonotonicSeconds;
        }

        void LogSelection(string change, string target)
        {
            sink.Log(TrialEvent("selection_changed")
                .Add("change", change)
                .Add("target", target)
                .Add("source", SelectedSource?.ToString())
                .Add("destination", SelectedDestination?.ToString())
                .Add("selected_packages", SelectedPackages.Select(CorreoPackages.Id).ToList()));
        }

        void EmitCompletion()
        {
            sink.Log(NewEvent("trial_completed")
                .Add("trial_index", TrialIndex)
                .Add("completion_status", "completed")
                .Add("completion_time_s", Round(completionTime.Value - beginTime))
                .Add("shipment_count", State.ShipmentCount)
                .Add("priority_satisfied", State.PrioritySatisfied)
                .Add("package_locations", State.Snapshot()));

            var summary = NewEvent("trial_summary");
            BuildSummary().AddTo(summary);
            sink.Log(summary);
        }

        ResearchEvent NewEvent(string type) => new ResearchEvent(TaskName, type);

        ResearchEvent TrialEvent(string type)
        {
            return NewEvent(type)
                .Add("trial_index", TrialIndex)
                .Add("completion_status", IsCompleted ? "completed" : "in_progress")
                .Add("elapsed_s", Round(clock.MonotonicSeconds - beginTime));
        }

        static double Round(double value, int digits = 3) => Math.Round(value, digits);
    }
}
