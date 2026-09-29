using System;
using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Hanoi
{
    public enum ReleaseResult
    {
        MovedToNewPeg,
        ReturnedToSamePeg,
        IllegalLargerOnSmaller,
        NoPegTarget,
        NotAccepted,
    }

    public readonly struct ReleaseOutcome
    {
        public ReleaseResult Result { get; }
        public bool Legal { get; }
        public bool StateChanged { get; }
        public bool Completed { get; }
        public string SourcePeg { get; }
        public string DestinationPeg { get; }
        public int MoveNumber { get; }

        public ReleaseOutcome(ReleaseResult result, bool legal, bool stateChanged, bool completed, string sourcePeg, string destinationPeg, int moveNumber)
        {
            Result = result;
            Legal = legal;
            StateChanged = stateChanged;
            Completed = completed;
            SourcePeg = sourcePeg;
            DestinationPeg = destinationPeg;
            MoveNumber = moveNumber;
        }
    }

    /// <summary>
    /// One Tower of Hanoi trial. Pure logic: it knows nothing about Unity, XR or scene objects,
    /// so the same rules, metrics and log events are used in the headset and in automated tests.
    /// </summary>
    public sealed class HanoiTrial
    {
        public const string TaskName = "Hanoi";

        readonly HanoiState state;
        readonly IResearchEventSink sink;
        readonly IClock clock;
        readonly List<string> moveSequence = new List<string>();
        readonly List<string> attemptSequence = new List<string>();

        double beginTime;
        double? firstGrabTime;
        double? completionTime;
        int heldDisk;
        int attempts;
        int illegalPlacements;
        int offPegReleases;
        int notAccessibleGrabs;
        bool begun;
        bool ended;

        public int TrialIndex { get; }
        public int DiskCount => state.DiskCount;
        public int OptimalMoves => HanoiState.OptimalMoves(state.DiskCount);
        public int TotalMoves { get; private set; }
        public int InvalidAttempts => illegalPlacements + offPegReleases + notAccessibleGrabs;
        public bool IsCompleted { get; private set; }
        public bool IsRunning => begun && !ended;
        public int HeldDisk => heldDisk;
        public HanoiState State => state;

        public HanoiTrial(int diskCount, IResearchEventSink sink, IClock clock, int trialIndex = 1)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            state = new HanoiState(diskCount);
            TrialIndex = trialIndex;
        }

        public void Begin()
        {
            if (begun) throw new InvalidOperationException("Trial already begun.");
            begun = true;
            beginTime = clock.MonotonicSeconds;
            var e = NewEvent("trial_started")
                .Add("trial_index", TrialIndex)
                .Add("disk_count", DiskCount)
                .Add("optimal_moves", OptimalMoves)
                .Add("resulting_state", state.Snapshot())
                .Add("completion_status", "in_progress");
            sink.Log(e);
        }

        /// <summary>Registers that the participant picked up a disk. Returns false if the grab is not valid.</summary>
        public bool OnGrab(int disk)
        {
            if (!IsRunning || IsCompleted) return false;
            if (heldDisk != 0) return false;

            var source = state.PegOf(disk);
            var accessible = source != null && state.IsTop(disk);
            if (!firstGrabTime.HasValue) firstGrabTime = clock.MonotonicSeconds;

            if (!accessible)
            {
                notAccessibleGrabs++;
                sink.Log(DiskEvent("disk_grab", disk, source, "", false, "not_accessible", TotalMoves));
                return false;
            }

            heldDisk = disk;
            sink.Log(DiskEvent("disk_grab", disk, source, "", true, "picked_up", TotalMoves));
            return true;
        }

        /// <summary>
        /// Registers that the participant released a disk over <paramref name="targetPeg"/>
        /// (null when it was not released over any peg).
        /// </summary>
        public ReleaseOutcome OnRelease(int disk, string targetPeg)
        {
            var source = state.PegOf(disk);
            if (!IsRunning || IsCompleted || heldDisk != disk)
                return new ReleaseOutcome(ReleaseResult.NotAccepted, false, false, IsCompleted, source, targetPeg, TotalMoves);

            heldDisk = 0;
            attempts++;

            if (string.IsNullOrEmpty(targetPeg))
            {
                offPegReleases++;
                attemptSequence.Add($"D{disk}:{source}->none:off_peg");
                sink.Log(DiskEvent("disk_release", disk, source, "", false, "off_peg", TotalMoves));
                return new ReleaseOutcome(ReleaseResult.NoPegTarget, false, false, false, source, null, TotalMoves);
            }

            switch (state.Evaluate(disk, targetPeg))
            {
                case Placement.SamePeg:
                    attemptSequence.Add($"D{disk}:{source}->{targetPeg}:same_peg");
                    sink.Log(DiskEvent("disk_release", disk, source, targetPeg, true, "returned_same_peg", TotalMoves));
                    return new ReleaseOutcome(ReleaseResult.ReturnedToSamePeg, true, false, false, source, targetPeg, TotalMoves);

                case Placement.Legal:
                    state.Move(disk, targetPeg);
                    TotalMoves++;
                    moveSequence.Add($"D{disk}:{source}->{targetPeg}");
                    attemptSequence.Add($"D{disk}:{source}->{targetPeg}:legal");
                    var solved = state.IsSolved;
                    if (solved)
                    {
                        IsCompleted = true;
                        completionTime = clock.MonotonicSeconds;
                    }
                    sink.Log(DiskEvent("disk_release", disk, source, targetPeg, true, "moved", TotalMoves));
                    if (solved) EmitCompletion();
                    return new ReleaseOutcome(ReleaseResult.MovedToNewPeg, true, true, solved, source, targetPeg, TotalMoves);

                default:
                    illegalPlacements++;
                    attemptSequence.Add($"D{disk}:{source}->{targetPeg}:illegal_larger_on_smaller");
                    sink.Log(DiskEvent("disk_release", disk, source, targetPeg, false, "illegal_larger_on_smaller", TotalMoves));
                    return new ReleaseOutcome(ReleaseResult.IllegalLargerOnSmaller, false, false, false, source, targetPeg, TotalMoves);
            }
        }

        /// <summary>Ends a trial that was not solved (for example when the researcher stops it).</summary>
        public void End(string reason)
        {
            if (!begun || ended || IsCompleted) return;
            ended = true;
            heldDisk = 0;
            var e = NewEvent("trial_summary");
            BuildSummary().AddTo(e);
            e.Add("end_reason", reason);
            sink.Log(e);
        }

        public HanoiSummary BuildSummary()
        {
            var now = clock.MonotonicSeconds;
            var summary = new HanoiSummary
            {
                TrialIndex = TrialIndex,
                DiskCount = DiskCount,
                OptimalMoves = OptimalMoves,
                TotalMoves = TotalMoves,
                InvalidAttempts = InvalidAttempts,
                IllegalPlacements = illegalPlacements,
                OffPegReleases = offPegReleases,
                AttemptCount = attempts,
                Completed = IsCompleted,
                Status = IsCompleted ? "completed" : (ended ? "incomplete" : "in_progress"),
                FinalState = state.Snapshot(),
                ElapsedSeconds = Round(begun ? (completionTime ?? now) - beginTime : 0),
                MoveSequence = new List<string>(moveSequence),
                AttemptSequence = new List<string>(attemptSequence),
            };

            if (firstGrabTime.HasValue) summary.FirstGrabLatencySeconds = Round(firstGrabTime.Value - beginTime);
            if (IsCompleted)
            {
                summary.CompletionTimeSeconds = Round(completionTime.Value - beginTime);
                summary.TimeFromFirstGrabSeconds = firstGrabTime.HasValue ? Round(completionTime.Value - firstGrabTime.Value) : (double?)null;
                summary.Efficiency = TotalMoves > 0 ? Round((double)OptimalMoves / TotalMoves, 4) : (double?)null;
                summary.ExcessMoves = TotalMoves - OptimalMoves;
            }
            return summary;
        }

        void EmitCompletion()
        {
            var completed = NewEvent("trial_completed")
                .Add("trial_index", TrialIndex)
                .Add("completion_status", "completed")
                .Add("completion_time_s", Round(completionTime.Value - beginTime))
                .Add("total_moves", TotalMoves)
                .Add("resulting_state", state.Snapshot());
            sink.Log(completed);

            var summary = NewEvent("trial_summary");
            BuildSummary().AddTo(summary);
            sink.Log(summary);
        }

        ResearchEvent NewEvent(string type) => new ResearchEvent(TaskName, type);

        ResearchEvent DiskEvent(string type, int disk, string source, string destination, bool legal, string outcome, int moveNumber)
        {
            return NewEvent(type)
                .Add("trial_index", TrialIndex)
                .Add("disk_count", DiskCount)
                .Add("disk_id", $"D{disk}")
                .Add("source_peg", source ?? "")
                .Add("destination_peg", destination ?? "")
                .Add("legal", legal)
                .Add("outcome", outcome)
                .Add("move_number", moveNumber)
                .Add("attempt_number", attempts)
                .Add("resulting_state", state.Snapshot())
                .Add("completion_status", IsCompleted ? "completed" : "in_progress")
                .Add("elapsed_s", Round(clock.MonotonicSeconds - beginTime));
        }

        static double Round(double value, int digits = 3) => Math.Round(value, digits);
    }
}
