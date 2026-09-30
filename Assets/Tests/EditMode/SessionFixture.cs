using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayCT.Research;
using PlayCT.Tasks.Correo;
using PlayCT.Tasks.Cubo;
using PlayCT.Tasks.Gabinete;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    /// <summary>
    /// Builds a realistic events.jsonl for a whole session through the real <see cref="EventLogWriter"/>: the orchestrator's
    /// lifecycle events, real Hanoi and El Correo trials, and Cubo and Gabinete trial events written with their real summary
    /// classes.
    /// </summary>
    public sealed class SessionFixture
    {
        public FakeClock Clock { get; } = new FakeClock();
        public StringWriter Text { get; } = new StringWriter();
        public EventLogWriter Writer { get; }
        public SessionInfo Session { get; }

        public SessionFixture(string sessionId = "S-1", string participantId = "P-07", string condition = "PreAdapted")
        {
            Session = new SessionInfo(sessionId, participantId, condition);
            Writer = new EventLogWriter(Session, Text, Clock);
            Writer.Log(new ResearchEvent("session", "session_started").Add("platform", "test"));
        }

        public string[] Lines() => Text.ToString().Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

        public List<LoggedEvent> Events() => EventLogReader.Read(Lines(), out _);

        public void StartExperiment(params string[] sequence) =>
            Writer.Log(new ResearchEvent("Experiment", "experiment_started").Add("condition", Session.Condition).Add("task_sequence", sequence).Add("task_count", sequence.Length));

        public void StartTask(string taskId, int index) =>
            Writer.Log(new ResearchEvent("Experiment", "task_started").Add("task_id", taskId).Add("task_index", index).Add("condition", Session.Condition));

        public void EndTask(string taskId, int index, string status, double duration) =>
            Writer.Log(new ResearchEvent("Experiment", "task_ended").Add("task_id", taskId).Add("task_index", index).Add("status", status)
                .Add("end_reason", status).Add("duration_s", duration));

        public void EndExperiment(string status, int completed) =>
            Writer.Log(new ResearchEvent("Experiment", "experiment_ended").Add("status", status).Add("end_reason", "all_tasks_completed")
                .Add("tasks_completed", completed).Add("tasks_skipped", 0).Add("duration_s", 1.0));

        public HanoiTrial PlayHanoi(int disks, int trialIndex = 1, bool withInvalidAttempt = true)
        {
            var trial = new HanoiTrial(disks, Writer, Clock, trialIndex);
            trial.Begin();
            Clock.Advance(1.5);
            if (withInvalidAttempt)
            {
                trial.OnGrab(1);
                trial.OnRelease(1, HanoiPegs.Origen);
            }
            foreach (var (disk, _, to) in HanoiSolver.Solve(disks))
            {
                Clock.Advance(2);
                trial.OnGrab(disk);
                trial.OnRelease(disk, to);
            }
            return trial;
        }

        public CorreoTrial PlayCorreo(int trialIndex = 1)
        {
            var trial = new CorreoTrial(new CorreoTrialConfig(), Writer, Clock, trialIndex);
            trial.Begin();
            Clock.Advance(3);
            trial.Ship(Settlement.A, Settlement.C, new[] { CorreoPackage.P2, CorreoPackage.P3 });
            Clock.Advance(2);
            trial.Ship(Settlement.A, Settlement.C, new[] { CorreoPackage.P1 });
            Clock.Advance(2);
            trial.Ship(Settlement.C, Settlement.E, new[] { CorreoPackage.P1 });
            Clock.Advance(2);
            trial.Ship(Settlement.A, Settlement.C, new[] { CorreoPackage.P2 });
            Clock.Advance(2);
            trial.Ship(Settlement.A, Settlement.C, new[] { CorreoPackage.P3 });
            Clock.Advance(2);
            trial.Ship(Settlement.C, Settlement.E, new[] { CorreoPackage.P2, CorreoPackage.P3 });
            return trial;
        }

        public void LogCuboTrial(int trialIndex, bool completed)
        {
            Writer.Log(new ResearchEvent(CuboTrial.TaskName, "trial_started").Add("trial_index", trialIndex).Add("mini_task", "cruz_clara"));
            Clock.Advance(20);
            var summary = new CuboSummary
            {
                TrialIndex = trialIndex,
                MiniTask = "cruz_clara",
                Goal = "cross",
                ValidRotations = 4,
                InvalidActions = 1,
                AttemptCount = 6,
                ReferenceMoves = 3,
                TimeLimitSeconds = 120,
                Completed = completed,
                Status = completed ? "completed" : "incomplete",
                CompletionTimeSeconds = completed ? 19.5 : (double?)null,
                FirstActionLatencySeconds = 2.25,
                ElapsedSeconds = 20,
                InitialState = "i",
                FinalState = "f",
                RotationSequence = new List<string> { "U+", "R-" },
                ActionSequence = new List<string> { "sel:U", "U+", "sel:R", "R-", "bad" },
            };
            var e = new ResearchEvent(CuboTrial.TaskName, "trial_summary");
            summary.AddTo(e);
            Writer.Log(e);
        }

        public void LogGabineteTrial(int trialIndex)
        {
            Writer.Log(new ResearchEvent(GabineteTrial.TaskName, "trial_started").Add("trial_index", trialIndex).Add("shape_count", 4));
            Clock.Advance(30);
            var summary = new GabineteSummary
            {
                TrialIndex = trialIndex,
                ShapeCount = 4,
                PlacementAttempts = 7,
                ValidPlacements = 4,
                InvalidAttempts = 3,
                WrongOpeningAttempts = 1,
                WrongOrientationAttempts = 2,
                PickUps = 8,
                RotationCount = 5,
                TotalRotationDegrees = 270.5,
                CompletedPieces = 4,
                TimeLimitSeconds = 300,
                Completed = true,
                Status = "completed",
                CompletionTimeSeconds = 29.75,
                FirstActionLatencySeconds = 1.5,
                ElapsedSeconds = 30,
                FinalState = "all",
                PlacementSequence = new List<string> { "prism", "cyl" },
                ActionSequence = new List<string> { "pick", "place", "pick", "place" },
            };
            var e = new ResearchEvent(GabineteTrial.TaskName, "trial_summary");
            summary.AddTo(e);
            Writer.Log(e);
        }

        /// <summary>Hanoi, Cubo, Gabinete and El Correo in order, as the orchestrator would run them.</summary>
        public static SessionFixture FullSession()
        {
            var f = new SessionFixture();
            f.StartExperiment("Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo");
            f.StartTask("Hanoi", 0);
            f.PlayHanoi(3);
            f.EndTask("Hanoi", 0, "completed", 30);
            f.StartTask("CuboRelaciones", 1);
            f.LogCuboTrial(1, completed: true);
            f.EndTask("CuboRelaciones", 1, "completed", 21);
            f.StartTask("GabineteFormas", 2);
            f.LogGabineteTrial(1);
            f.EndTask("GabineteFormas", 2, "completed", 31);
            f.StartTask("ElCorreo", 3);
            f.PlayCorreo();
            f.EndTask("ElCorreo", 3, "completed", 15);
            f.EndExperiment("completed", 4);
            return f;
        }
    }
}
