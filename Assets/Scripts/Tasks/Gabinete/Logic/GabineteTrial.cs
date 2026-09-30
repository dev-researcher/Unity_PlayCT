using System;
using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Gabinete
{
    public enum GabinetePlacement
    {
        Available,
        Placed,
    }

    /// <summary>The logical state of one piece. Position on screen is the view's business; this is what the research needs.</summary>
    public sealed class GabinetePiece
    {
        public GabinetePieceSpec Spec { get; }
        public GabinetePlacement Placement { get; internal set; }
        public bool IsHeld { get; internal set; }
        public double CurrentYawDegrees { get; internal set; }
        public int AttemptCount { get; internal set; }

        public string Id => Spec.Id;
        public GabineteShapeType Shape => Spec.Shape;
        public int TargetOpening => Spec.OpeningIndex;
        public bool IsPlaced => Placement == GabinetePlacement.Placed;

        internal double GrabYawDegrees;

        internal GabinetePiece(GabinetePieceSpec spec)
        {
            Spec = spec;
            CurrentYawDegrees = spec.StartYawDegrees;
        }
    }

    public readonly struct GabineteOutcome
    {
        /// <summary>False when nothing was recorded (trial not running, unknown or already placed piece).</summary>
        public bool Accepted { get; }
        /// <summary>True when the release was judged against an opening (a placement attempt).</summary>
        public bool Attempt { get; }
        public bool Valid { get; }
        public bool Completed { get; }
        public string Reason { get; }
        public int OpeningIndex { get; }

        public GabineteOutcome(bool accepted, bool attempt, bool valid, bool completed, string reason, int openingIndex)
        {
            Accepted = accepted;
            Attempt = attempt;
            Valid = valid;
            Completed = completed;
            Reason = reason;
            OpeningIndex = openingIndex;
        }

        public static GabineteOutcome NotAccepted(string reason) => new GabineteOutcome(false, false, false, false, reason, -1);
    }

    /// <summary>
    /// One Gabinete de Formas trial. Pure logic: it holds which pieces are available or placed, decides each release
    /// against the openings (right piece, sufficiently aligned, orientation the opening accepts) and writes the log events
    /// and metrics. A rejected attempt never changes the placed pieces; the piece stays available. It knows nothing about
    /// Unity, XR or the visual cabinet, and it does not follow any order: pieces can be placed in any sequence.
    /// </summary>
    public sealed class GabineteTrial
    {
        public const string TaskName = "GabineteFormas";

        readonly GabineteTrialConfig config;
        readonly IResearchEventSink sink;
        readonly IClock clock;
        readonly List<GabinetePiece> pieces = new List<GabinetePiece>();
        readonly Dictionary<string, GabinetePiece> byId = new Dictionary<string, GabinetePiece>();
        readonly List<string> placementSequence = new List<string>();
        readonly List<string> actionSequence = new List<string>();
        readonly Dictionary<string, int> invalidByReason = new Dictionary<string, int>();

        double beginTime;
        double? firstActionTime;
        double? completionTime;
        int attempts;
        int validPlacements;
        int invalidAttempts;
        int offTargetReleases;
        int pickUps;
        int rotations;
        double totalRotationDegrees;
        bool begun;
        bool ended;

        public int TrialIndex { get; }
        public int ShapeCount => config.ShapeCount;
        public IReadOnlyList<GabinetePiece> Pieces => pieces;
        public int PlacedCount { get; private set; }
        public int AttemptCount => attempts;
        public int ValidPlacements => validPlacements;
        public int InvalidAttempts => invalidAttempts;
        public int OffTargetReleases => offTargetReleases;
        public int Rotations => rotations;
        public bool IsCompleted { get; private set; }
        public bool IsRunning => begun && !ended && !IsCompleted;
        public bool IsFinished => ended || IsCompleted;
        public IReadOnlyList<string> PlacementSequence => placementSequence;
        public IReadOnlyList<string> ActionSequence => actionSequence;

        public GabineteTrial(GabineteTrialConfig config, IResearchEventSink sink, IClock clock, int trialIndex = 1)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            config.Validate();
            TrialIndex = trialIndex;
            foreach (var spec in config.Pieces)
            {
                var piece = new GabinetePiece(spec);
                pieces.Add(piece);
                byId[spec.Id] = piece;
            }
        }

        public GabinetePiece Piece(string id) => byId.TryGetValue(id, out var piece) ? piece : null;

        /// <summary>Compact logical state: for each opening in order, the shape it takes and whether it is filled.</summary>
        public string Snapshot()
        {
            var ordered = new GabinetePiece[pieces.Count];
            foreach (var piece in pieces) ordered[piece.TargetOpening] = piece;
            var parts = new string[ordered.Length];
            for (var i = 0; i < ordered.Length; i++)
                parts[i] = GabineteShapes.Id(ordered[i].Shape) + ":" + (ordered[i].IsPlaced ? "placed" : "open");
            return string.Join(",", parts);
        }

        public void Begin()
        {
            if (begun) throw new InvalidOperationException("Trial already begun.");
            begun = true;
            beginTime = clock.MonotonicSeconds;

            var ids = new List<string>();
            var shapes = new List<string>();
            var required = new List<double>();
            foreach (var piece in pieces)
            {
                ids.Add(piece.Id);
                shapes.Add(GabineteShapes.Id(piece.Shape));
                required.Add(piece.Spec.RequiredRotationDegrees);
            }
            sink.Log(NewEvent("trial_started")
                .Add("trial_index", TrialIndex)
                .Add("shape_count", config.ShapeCount)
                .Add("pieces", ids)
                .Add("piece_types", shapes)
                .Add("required_rotation_deg", required)
                .Add("time_limit_s", config.DurationSeconds)
                .Add("position_tolerance_m", config.Rules.positionTolerance)
                .Add("orientation_tolerance_deg", config.Rules.orientationTolerance)
                .Add("resulting_state", Snapshot())
                .Add("completion_status", "in_progress"));
        }

        /// <summary>The participant picked a piece up. Placed pieces cannot be picked up again.</summary>
        public GabineteOutcome PickUp(string pieceId, double yawDegrees)
        {
            if (!IsRunning) return GabineteOutcome.NotAccepted("not_running");
            var piece = Piece(pieceId);
            if (piece == null) return GabineteOutcome.NotAccepted("unknown_piece");
            if (piece.IsPlaced) return GabineteOutcome.NotAccepted("already_placed");
            if (piece.IsHeld) return GabineteOutcome.NotAccepted("already_held");

            NoteFirstAction();
            piece.IsHeld = true;
            piece.GrabYawDegrees = GabineteGeometry.NormalizeDegrees(yawDegrees);
            pickUps++;
            actionSequence.Add($"{piece.Id}:pick_up");
            sink.Log(TrialEvent("piece_picked_up", piece)
                .Add("starting_orientation_deg", Round(piece.GrabYawDegrees, 1))
                .Add("resulting_state", Snapshot()));
            return new GabineteOutcome(true, false, false, false, "picked_up", -1);
        }

        /// <summary>
        /// The participant let go of a piece. If it is over an opening this is a placement attempt, which succeeds only
        /// for the right piece, close enough, upright and turned to an orientation the opening accepts. Otherwise the
        /// release is recorded as off target. Either way a rejected piece stays available and goes back to its tray slot.
        /// </summary>
        public GabineteOutcome Release(string pieceId, PiecePose pose)
        {
            if (!IsRunning) return GabineteOutcome.NotAccepted("not_running");
            var piece = Piece(pieceId);
            if (piece == null) return GabineteOutcome.NotAccepted("unknown_piece");
            if (piece.IsPlaced) return GabineteOutcome.NotAccepted("already_placed");
            if (!piece.IsHeld)
            {
                piece.IsHeld = true;
                piece.GrabYawDegrees = piece.CurrentYawDegrees;
            }

            NoteFirstAction();
            piece.IsHeld = false;
            var rules = config.Rules;
            var change = GabineteGeometry.AngleBetween(pose.YawDegrees, piece.GrabYawDegrees);
            var rotated = change >= rules.rotationThreshold;
            if (rotated)
            {
                rotations++;
                totalRotationDegrees += change;
            }

            double distance = 0;
            var opening = pose.Height <= rules.maxReleaseHeight
                ? GabineteGeometry.NearestOpening(pose, config.ShapeCount, rules.captureRadius, out distance)
                : -1;
            if (opening < 0)
            {
                offTargetReleases++;
                piece.CurrentYawDegrees = piece.Spec.StartYawDegrees;
                actionSequence.Add($"{piece.Id}:off_target");
                sink.Log(TrialEvent("piece_released", piece)
                    .Add("starting_orientation_deg", Round(piece.GrabYawDegrees, 1))
                    .Add("attempted_orientation_deg", Round(pose.YawDegrees, 1))
                    .Add("orientation_changed", rotated)
                    .Add("placement_result", "off_target")
                    .Add("valid", false)
                    .Add("resulting_state", Snapshot()));
                return new GabineteOutcome(true, false, false, false, "off_target", -1);
            }

            attempts++;
            piece.AttemptCount++;
            var target = FindByOpening(opening);
            var orientationError = GabineteGeometry.OrientationError(piece.Spec.ShapeSpec, pose.YawDegrees);

            string rejection = null;
            if (target.IsPlaced) rejection = "opening_occupied";
            else if (target.Shape != piece.Shape) rejection = "wrong_opening";
            else if (pose.TiltDegrees > rules.maxTilt) rejection = "not_upright";
            else if (distance > rules.positionTolerance) rejection = "misaligned";
            else if (orientationError > rules.orientationTolerance) rejection = "wrong_orientation";

            var valid = rejection == null;
            if (valid)
            {
                piece.Placement = GabinetePlacement.Placed;
                piece.CurrentYawDegrees = pose.YawDegrees;
                PlacedCount++;
                validPlacements++;
                placementSequence.Add(piece.Id);
                actionSequence.Add($"{piece.Id}>{opening}:placed");
            }
            else
            {
                invalidAttempts++;
                invalidByReason.TryGetValue(rejection, out var count);
                invalidByReason[rejection] = count + 1;
                piece.CurrentYawDegrees = piece.Spec.StartYawDegrees;
                actionSequence.Add($"{piece.Id}>{opening}:{rejection}");
            }

            var completed = valid && PlacedCount == pieces.Count;
            if (completed) MarkCompleted();

            sink.Log(TrialEvent("placement_attempt", piece)
                .Add("attempt_number", attempts)
                .Add("target_opening", piece.TargetOpening)
                .Add("attempted_opening", opening)
                .Add("attempted_opening_type", GabineteShapes.Id(target.Shape))
                .Add("starting_orientation_deg", Round(piece.GrabYawDegrees, 1))
                .Add("attempted_orientation_deg", Round(pose.YawDegrees, 1))
                .Add("orientation_error_deg", Round(orientationError, 1))
                .Add("orientation_changed", rotated)
                .Add("position_offset_m", Round(distance, 4))
                .Add("tilt_deg", Round(pose.TiltDegrees, 1))
                .Add("placement_result", valid ? "placed" : rejection)
                .Add("valid", valid)
                .Add("completed_pieces", PlacedCount)
                .Add("resulting_state", Snapshot()));
            if (completed) EmitCompletion();
            return new GabineteOutcome(true, true, valid, completed, valid ? "placed" : rejection, opening);
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
            foreach (var piece in pieces) piece.IsHeld = false;
            var e = NewEvent("trial_summary");
            BuildSummary().AddTo(e);
            e.Add("end_reason", reason);
            sink.Log(e);
        }

        public GabineteSummary BuildSummary()
        {
            var now = clock.MonotonicSeconds;
            var summary = new GabineteSummary
            {
                TrialIndex = TrialIndex,
                ShapeCount = config.ShapeCount,
                PlacementAttempts = attempts,
                ValidPlacements = validPlacements,
                InvalidAttempts = invalidAttempts,
                WrongOpeningAttempts = Count("wrong_opening"),
                WrongOrientationAttempts = Count("wrong_orientation"),
                MisalignedAttempts = Count("misaligned"),
                OtherInvalidAttempts = Count("opening_occupied") + Count("not_upright"),
                OffTargetReleases = offTargetReleases,
                PickUps = pickUps,
                RotationCount = rotations,
                TotalRotationDegrees = Round(totalRotationDegrees, 1),
                CompletedPieces = PlacedCount,
                TimeLimitSeconds = config.DurationSeconds,
                Completed = IsCompleted,
                Status = IsCompleted ? "completed" : (ended ? "incomplete" : "in_progress"),
                ElapsedSeconds = Round(begun ? (completionTime ?? now) - beginTime : 0),
                FinalState = Snapshot(),
                PlacementSequence = new List<string>(placementSequence),
                ActionSequence = new List<string>(actionSequence),
            };
            if (firstActionTime.HasValue) summary.FirstActionLatencySeconds = Round(firstActionTime.Value - beginTime);
            if (IsCompleted) summary.CompletionTimeSeconds = Round(completionTime.Value - beginTime);
            return summary;
        }

        int Count(string reason) => invalidByReason.TryGetValue(reason, out var count) ? count : 0;

        GabinetePiece FindByOpening(int opening)
        {
            foreach (var piece in pieces)
                if (piece.TargetOpening == opening) return piece;
            throw new InvalidOperationException($"No piece belongs to opening {opening}.");
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

        void EmitCompletion()
        {
            sink.Log(NewEvent("trial_completed")
                .Add("trial_index", TrialIndex)
                .Add("completion_status", "completed")
                .Add("completion_time_s", Round(completionTime.Value - beginTime))
                .Add("completed_pieces", PlacedCount)
                .Add("resulting_state", Snapshot()));

            var summary = NewEvent("trial_summary");
            BuildSummary().AddTo(summary);
            sink.Log(summary);
        }

        ResearchEvent NewEvent(string type) => new ResearchEvent(TaskName, type);

        ResearchEvent TrialEvent(string type, GabinetePiece piece)
        {
            return NewEvent(type)
                .Add("trial_index", TrialIndex)
                .Add("shape_count", config.ShapeCount)
                .Add("piece_id", piece.Id)
                .Add("piece_type", GabineteShapes.Id(piece.Shape))
                .Add("completion_status", IsCompleted ? "completed" : "in_progress")
                .Add("elapsed_s", Round(clock.MonotonicSeconds - beginTime));
        }

        static double Round(double value, int digits = 3) => Math.Round(value, digits);
    }
}
