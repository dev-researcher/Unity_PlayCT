using System;
using System.Collections.Generic;
using PlayCT.Research;

namespace PlayCT.Tasks.Cubo
{
    public readonly struct CuboOutcome
    {
        /// <summary>False when the trial was not running, so nothing was recorded.</summary>
        public bool Accepted { get; }
        public bool Legal { get; }
        public bool StateChanged { get; }
        public bool Completed { get; }
        public string Reason { get; }

        public CuboOutcome(bool accepted, bool legal, bool stateChanged, bool completed, string reason)
        {
            Accepted = accepted;
            Legal = legal;
            StateChanged = stateChanged;
            Completed = completed;
            Reason = reason;
        }

        public static CuboOutcome NotAccepted => new CuboOutcome(false, false, false, false, "not_running");
    }

    /// <summary>
    /// One Cubo de Relaciones trial. Pure logic: it holds the cube state, checks each action against the trial
    /// configuration, and writes the log events and metrics. It knows nothing about Unity, XR or the visual cube.
    /// An invalid action is refused and recorded; the state is never changed by it and the trial goes on.
    /// </summary>
    public sealed class CuboTrial
    {
        public const string TaskName = "CuboRelaciones";

        readonly CuboTrialConfig config;
        readonly IResearchEventSink sink;
        readonly IClock clock;
        readonly List<string> rotationSequence = new List<string>();
        readonly List<string> actionSequence = new List<string>();

        double beginTime;
        double? firstActionTime;
        double? completionTime;
        int attempts;
        int invalidActions;
        bool stabilized;
        bool begun;
        bool ended;
        CubeFace? answerFace;

        public int TrialIndex { get; }
        public CuboMiniTask MiniTask => config.MiniTask;
        public CubeState State { get; private set; }
        public CubeState InitialState => config.InitialState;
        public CubeFace? SelectedFace { get; private set; }
        public bool Stabilized => stabilized;
        public int ValidRotations { get; private set; }
        public int InvalidActions => invalidActions;
        public bool IsCompleted { get; private set; }
        public bool IsRunning => begun && !ended && !IsCompleted;
        public bool IsFinished => ended || IsCompleted;
        public IReadOnlyList<string> RotationSequence => rotationSequence;
        public IReadOnlyList<string> ActionSequence => actionSequence;

        public CuboTrial(CuboTrialConfig config, IResearchEventSink sink, IClock clock, int trialIndex = 1)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            config.Validate();
            TrialIndex = trialIndex;
            State = config.InitialState;
        }

        public void Begin()
        {
            if (begun) throw new InvalidOperationException("Trial already begun.");
            begun = true;
            beginTime = clock.MonotonicSeconds;
            var allowed = new List<string>();
            foreach (var move in config.AllowedMoves) allowed.Add(move.Notation);
            sink.Log(NewEvent("trial_started")
                .Add("trial_index", TrialIndex)
                .Add("mini_task", CuboMiniTasks.Id(config.MiniTask))
                .Add("goal", GoalLabel())
                .Add("allowed_rotations", allowed)
                .Add("time_limit_s", config.DurationSeconds)
                .Add("reference_moves", config.ReferenceMoves)
                .Add("resulting_state", State.Snapshot())
                .Add("completion_status", "in_progress"));
        }

        /// <summary>Records whether the stabilizing hand is holding the cube. Face actions need it to be held.</summary>
        public void SetStabilized(bool held)
        {
            if (stabilized == held) return;
            stabilized = held;
            if (!IsRunning) return;
            sink.Log(TrialEvent("stabilizer_changed")
                .Add("stabilized", held)
                .Add("resulting_state", State.Snapshot()));
        }

        /// <summary>
        /// The participant touched a face without twisting. In the invariant mini-task this is the answer;
        /// in the others it only selects the face.
        /// </summary>
        public CuboOutcome SelectFace(CubeFace face)
        {
            if (!IsRunning) return CuboOutcome.NotAccepted;
            SelectedFace = face;
            NoteFirstAction();
            return config.Goal.Kind == CuboGoalKind.UnchangedFace ? Answer(face) : Select(face);
        }

        /// <summary>Asks for one quarter turn of a face layer.</summary>
        public CuboOutcome TryRotate(CubeMove move)
        {
            if (!IsRunning) return CuboOutcome.NotAccepted;
            SelectedFace = move.Face;
            NoteFirstAction();
            attempts++;

            var previous = State;
            string rejection = null;
            if (!stabilized) rejection = "not_stabilized";
            else if (!config.IsAllowed(move)) rejection = "rotation_not_allowed";

            if (rejection != null)
            {
                invalidActions++;
                actionSequence.Add($"{move.Notation}:rejected:{rejection}");
                sink.Log(RotationEvent(move, false, rejection, previous));
                return new CuboOutcome(true, false, false, false, rejection);
            }

            State = previous.Apply(move);
            ValidRotations++;
            rotationSequence.Add(move.Notation);
            actionSequence.Add(move.Notation);

            var solved = config.Goal.Kind != CuboGoalKind.UnchangedFace && config.Goal.IsSatisfiedBy(State);
            if (solved) MarkCompleted();
            sink.Log(RotationEvent(move, true, "applied", previous));
            if (solved) EmitCompletion();
            return new CuboOutcome(true, true, true, solved, "applied");
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
            var e = NewEvent("trial_summary");
            BuildSummary().AddTo(e);
            e.Add("end_reason", reason);
            sink.Log(e);
        }

        public CuboSummary BuildSummary()
        {
            var now = clock.MonotonicSeconds;
            var summary = new CuboSummary
            {
                TrialIndex = TrialIndex,
                MiniTask = CuboMiniTasks.Id(config.MiniTask),
                Goal = GoalLabel(),
                ValidRotations = ValidRotations,
                InvalidActions = invalidActions,
                AttemptCount = attempts,
                ReferenceMoves = config.ReferenceMoves,
                TimeLimitSeconds = config.DurationSeconds,
                Completed = IsCompleted,
                Status = IsCompleted ? "completed" : (ended ? "incomplete" : "in_progress"),
                ElapsedSeconds = Round(begun ? (completionTime ?? now) - beginTime : 0),
                AnswerFace = answerFace.HasValue ? CubeFaces.Letter(answerFace.Value).ToString() : null,
                InitialState = config.InitialState.Snapshot(),
                FinalState = State.Snapshot(),
                RotationSequence = new List<string>(rotationSequence),
                ActionSequence = new List<string>(actionSequence),
            };
            if (firstActionTime.HasValue) summary.FirstActionLatencySeconds = Round(firstActionTime.Value - beginTime);
            if (IsCompleted) summary.CompletionTimeSeconds = Round(completionTime.Value - beginTime);
            return summary;
        }

        CuboOutcome Select(CubeFace face)
        {
            var letter = CubeFaces.Letter(face);
            if (!stabilized)
            {
                invalidActions++;
                actionSequence.Add($"select:{letter}:rejected:not_stabilized");
                sink.Log(TrialEvent("face_selected")
                    .Add("selected_face", letter.ToString())
                    .Add("legal", false)
                    .Add("outcome", "not_stabilized")
                    .Add("resulting_state", State.Snapshot()));
                return new CuboOutcome(true, false, false, false, "not_stabilized");
            }

            actionSequence.Add($"select:{letter}");
            sink.Log(TrialEvent("face_selected")
                .Add("selected_face", letter.ToString())
                .Add("legal", true)
                .Add("outcome", "selected")
                .Add("resulting_state", State.Snapshot()));
            return new CuboOutcome(true, true, false, false, "selected");
        }

        CuboOutcome Answer(CubeFace face)
        {
            var letter = CubeFaces.Letter(face);
            attempts++;

            string rejection = null;
            if (!stabilized) rejection = "not_stabilized";
            else if (State.Equals(config.InitialState)) rejection = "nothing_changed";
            else if (ValidRotations < config.MinRotations) rejection = "too_few_rotations";
            else if (!State.FaceEquals(config.InitialState, face)) rejection = "face_changed";

            if (rejection != null)
            {
                invalidActions++;
                actionSequence.Add($"answer:{letter}:rejected:{rejection}");
                sink.Log(AnswerEvent(letter, false, rejection));
                return new CuboOutcome(true, false, false, false, rejection);
            }

            answerFace = face;
            actionSequence.Add($"answer:{letter}");
            MarkCompleted();
            sink.Log(AnswerEvent(letter, true, "unchanged_face"));
            EmitCompletion();
            return new CuboOutcome(true, true, false, true, "unchanged_face");
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
            sink.Log(TrialEvent("trial_completed")
                .Add("completion_status", "completed")
                .Add("completion_time_s", Round(completionTime.Value - beginTime))
                .Add("valid_rotations", ValidRotations)
                .Add("resulting_state", State.Snapshot()));

            var summary = NewEvent("trial_summary");
            BuildSummary().AddTo(summary);
            sink.Log(summary);
        }

        string GoalLabel()
        {
            switch (config.Goal.Kind)
            {
                case CuboGoalKind.CrossOnFace: return "cross:" + CubeFaces.Letter(config.Goal.Face);
                case CuboGoalKind.ExactState: return "exact_state";
                default: return "unchanged_face";
            }
        }

        ResearchEvent NewEvent(string type) => new ResearchEvent(TaskName, type);

        ResearchEvent TrialEvent(string type)
        {
            return NewEvent(type)
                .Add("trial_index", TrialIndex)
                .Add("mini_task", CuboMiniTasks.Id(config.MiniTask))
                .Add("stabilized", stabilized)
                .Add("attempt_number", attempts)
                .Add("completion_status", IsCompleted ? "completed" : "in_progress")
                .Add("elapsed_s", Round(clock.MonotonicSeconds - beginTime));
        }

        ResearchEvent RotationEvent(CubeMove move, bool legal, string outcome, CubeState previous)
        {
            return TrialEvent("rotation")
                .Add("selected_face", CubeFaces.Letter(move.Face).ToString())
                .Add("rotation_axis", move.Axis.ToString())
                .Add("rotation_direction", move.DirectionLabel)
                .Add("rotation_amount_deg", CubeMove.AmountDegrees)
                .Add("legal", legal)
                .Add("outcome", outcome)
                .Add("move_number", ValidRotations)
                .Add("previous_state", previous.Snapshot())
                .Add("resulting_state", State.Snapshot());
        }

        ResearchEvent AnswerEvent(char letter, bool legal, string outcome)
        {
            return TrialEvent("face_answer")
                .Add("selected_face", letter.ToString())
                .Add("legal", legal)
                .Add("outcome", outcome)
                .Add("move_number", ValidRotations)
                .Add("previous_state", config.InitialState.Snapshot())
                .Add("resulting_state", State.Snapshot());
        }

        static double Round(double value, int digits = 3) => Math.Round(value, digits);
    }
}
