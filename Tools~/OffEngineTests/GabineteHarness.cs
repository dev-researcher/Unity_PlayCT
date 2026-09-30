using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Gabinete;
using UnityEngine;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Runs the real GabineteTask, TaskOrchestrator, SessionManager and EventLogger scripts on the fake engine with a
    /// recording stand-in for the visual cabinet. It covers only what is specific to Gabinete (how it plugs into the
    /// orchestrator and drives its view); generic task and orchestrator behaviour is tested elsewhere. The board and piece
    /// meshes, the XR grab interactables and their release poses are not exercised here.
    /// </summary>
    public class GabineteHarness
    {
        sealed class FakeCabinetView : MonoBehaviour, IGabineteView
        {
            public bool Visible;
            public bool PiecesLocked;
            public bool AutoFinish = true;
            public GabineteTrialConfig Shown;
            public readonly List<string> Calls = new List<string>();
            Action pendingSeat;

            public void SetVisible(bool visible) { Visible = visible; Calls.Add("visible:" + visible); }
            public void Show(GabineteTrialConfig config) { Shown = config; PiecesLocked = false; Calls.Add("show:" + config.ShapeCount); }
            public void LockPieces() { PiecesLocked = true; Calls.Add("lock"); }
            public void ReturnPiece(string pieceId) { Calls.Add("return:" + pieceId); }

            public void SeatPiece(string pieceId, int openingIndex, Action done)
            {
                Calls.Add($"seat:{pieceId}:{openingIndex}");
                if (AutoFinish) { done?.Invoke(); return; }
                pendingSeat = done;
            }

            public void FinishSeating()
            {
                var done = pendingSeat;
                pendingSeat = null;
                done?.Invoke();
            }
        }

        sealed class EarlierTask : IExperimentTask
        {
            public string TaskId => "Earlier";
            public bool IsRunning { get; private set; }
            public bool IsCompleted { get; private set; }
            public event Action<IExperimentTask> Completed;
            public void StartTask(ExperimentCondition condition) { IsRunning = true; }
            public void EndTask(string reason) { IsRunning = false; }
            public void Finish() { IsCompleted = true; Completed?.Invoke(this); }
        }

        string logRoot;
        SessionManager session;
        EventLogger logger;
        ConditionManager conditions;
        TaskOrchestrator orchestrator;
        GabineteTask gabinete;
        FakeCabinetView view;
        GameObject otherTaskObjects;
        EarlierTask earlier;

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_gabinete_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (logger != null) Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        void Build(string condition = "PreAdapted", GabineteTrialSpec[] specs = null, string[] sequence = null, bool withEarlier = false)
        {
            session = new GameObject("SessionManager").AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", "P-11");
                Engine.Set(m, "condition", condition);
            });
            logger = new GameObject("EventLogger").AddComponent<EventLogger>();
            logger.Session = session;
            logger.RootDirectoryOverride = logRoot;
            conditions = new GameObject("ConditionManager").AddComponent<ConditionManager>(c => Engine.Set(c, "session", session));

            view = new GameObject("GabineteView").AddComponent<FakeCabinetView>();
            otherTaskObjects = new GameObject("OtherTaskObjects");
            gabinete = new GameObject("Gabinete_Task").AddComponent<GabineteTask>(t =>
            {
                Engine.Set(t, "session", session);
                Engine.Set(t, "eventLogger", logger);
                Engine.Set(t, "view", view);
                Engine.Set(t, "hideWhileActive", new[] { otherTaskObjects });
                if (specs != null) Engine.Set(t, "trials", specs);
            });

            orchestrator = new GameObject("Experiment").AddComponent<TaskOrchestrator>(o =>
            {
                Engine.Set(o, "session", session);
                Engine.Set(o, "eventLogger", logger);
                Engine.Set(o, "conditionManager", conditions);
                Engine.Set(o, "tasks", new MonoBehaviour[] { gabinete });
                Engine.Set(o, "beginOnStart", false);
                Engine.Set(o, "taskSequence", sequence ?? new[] { "GabineteFormas" });
            });
            if (withEarlier)
            {
                earlier = new EarlierTask();
                orchestrator.Register(earlier);
            }
        }

        static GabineteTrialSpec Spec(int count, float seconds = 0) => new GabineteTrialSpec { shapeCount = count, durationSeconds = seconds };

        void PlaceAll()
        {
            foreach (var piece in gabinete.Trial.Pieces.ToList())
            {
                gabinete.OnPieceGrabbed(piece.Id, piece.Spec.StartYawDegrees);
                gabinete.OnPieceReleased(piece.Id, PiecePose.At(piece.TargetOpening, gabinete.Trial.ShapeCount, 0));
            }
        }

        void NextTrial() => Engine.TickSeconds(1.5);

        string[] Lines() => File.ReadAllLines(logger.CurrentLogPath);

        string[] Types() => Lines().Select(l => Extract(l, "task") + ":" + Extract(l, "event")).ToArray();

        static string Extract(string line, string key)
        {
            var marker = "\"" + key + "\":\"";
            var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return line.Substring(start, line.IndexOf('"', start) - start);
        }

        [Test]
        public void Gabinete_IsACommonTask_WithAStableId_AndNothingShownBeforeItStarts()
        {
            Build();
            IExperimentTask task = gabinete;

            Assert.AreEqual("GabineteFormas", task.TaskId);
            Assert.IsFalse(task.IsRunning);
            Assert.IsFalse(task.IsCompleted);
            Assert.IsFalse(view.Visible);
        }

        [TestCase("Static", ExperimentCondition.Static)]
        [TestCase("PreAdapted", ExperimentCondition.PreAdapted)]
        public void Orchestrator_StartsGabinete_WithTheSessionCondition_ShowsTheFirstCabinet_AndClearsTheTable(string name, ExperimentCondition expected)
        {
            Build(condition: name);

            Assert.IsTrue(orchestrator.BeginExperiment());

            Assert.AreEqual("GabineteFormas", orchestrator.CurrentTaskId);
            Assert.IsTrue(gabinete.IsRunning);
            Assert.AreEqual(expected, gabinete.Condition);
            Assert.IsTrue(view.Visible);
            Assert.AreEqual(4, view.Shown.ShapeCount, "the default protocol starts with the 4-shape cabinet");
            Assert.IsFalse(otherTaskObjects.activeSelf, "other tasks' objects leave the table while Gabinete uses it");
        }

        [Test]
        public void Orchestrator_RunsGabineteAfterAnEarlierTask_OnlyWhenThatTaskIsDone()
        {
            Build(sequence: new[] { "Earlier", "GabineteFormas" }, withEarlier: true);
            orchestrator.BeginExperiment();

            Assert.IsFalse(gabinete.IsRunning);
            Assert.IsFalse(view.Visible);

            earlier.Finish();

            Assert.IsTrue(gabinete.IsRunning);
            Assert.IsTrue(view.Visible);
        }

        [Test]
        public void DefaultProtocol_RunsTheFourSixAndEightShapeCabinets_ThenTheOrchestratorEndsTheTask()
        {
            Build();
            orchestrator.BeginExperiment();
            var completedEvents = 0;
            gabinete.Completed += _ => completedEvents++;

            PlaceAll();
            Assert.IsTrue(gabinete.IsRunning, "the next cabinet follows after a pause");
            Assert.IsFalse(gabinete.Trial.IsRunning);
            NextTrial();
            Assert.AreEqual(6, view.Shown.ShapeCount);
            PlaceAll();
            NextTrial();
            Assert.AreEqual(8, view.Shown.ShapeCount);
            Assert.IsFalse(gabinete.IsCompleted);
            PlaceAll();

            Assert.AreEqual(1, completedEvents);
            Assert.IsTrue(gabinete.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
            Assert.IsFalse(view.Visible);
            Assert.IsTrue(view.PiecesLocked);
            Assert.IsTrue(otherTaskObjects.activeSelf, "the table is handed back");
            CollectionAssert.AreEqual(new[] { 4, 6, 8 }, gabinete.Summaries.Select(s => s.ShapeCount));
            Assert.IsTrue(gabinete.Summaries.All(s => s.Status == "completed" && s.InvalidAttempts == 0));
        }

        [Test]
        public void ValidRelease_SeatsThePiece_AndRejectedOrOffTargetReleasesSendItBack()
        {
            Build(specs: new[] { Spec(4) });
            orchestrator.BeginExperiment();
            var trial = gabinete.Trial;
            var prism = trial.Pieces.Single(p => p.Shape == GabineteShapeType.Prism);
            var cylinder = trial.Pieces.Single(p => p.Shape == GabineteShapeType.Cylinder);

            gabinete.OnPieceGrabbed(prism.Id, 90);
            gabinete.OnPieceReleased(prism.Id, PiecePose.At(prism.TargetOpening, 4, 90));
            gabinete.OnPieceGrabbed(cylinder.Id, 0);
            gabinete.OnPieceReleased(cylinder.Id, PiecePose.At(prism.TargetOpening, 4, 0));
            gabinete.OnPieceGrabbed(cylinder.Id, 0);
            gabinete.OnPieceReleased(cylinder.Id, new PiecePose(0f, GabineteLayout.TrayZ, 0));
            gabinete.OnPieceGrabbed(prism.Id, 90);
            gabinete.OnPieceReleased(prism.Id, PiecePose.At(prism.TargetOpening, 4, 0));

            CollectionAssert.AreEqual(new[] { $"return:{prism.Id}", $"return:{cylinder.Id}", $"return:{cylinder.Id}", $"seat:{prism.Id}:{prism.TargetOpening}" },
                view.Calls.Where(c => c.StartsWith("return") || c.StartsWith("seat")).ToArray());
            Assert.AreEqual(1, trial.PlacedCount);
        }

        [Test]
        public void CompletionIsReportedOnlyAfterTheLastPieceHasFinishedSeating()
        {
            Build(specs: new[] { Spec(4) });
            view.AutoFinish = false;
            orchestrator.BeginExperiment();
            var pieces = gabinete.Trial.Pieces.ToList();
            foreach (var piece in pieces)
            {
                gabinete.OnPieceGrabbed(piece.Id, piece.Spec.StartYawDegrees);
                gabinete.OnPieceReleased(piece.Id, PiecePose.At(piece.TargetOpening, 4, 0));
            }

            Assert.IsTrue(gabinete.Trial.IsCompleted, "the logic already knows");
            Assert.IsFalse(gabinete.IsCompleted);
            Assert.AreEqual(0, gabinete.Summaries.Count);

            view.FinishSeating();

            Assert.IsTrue(gabinete.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
        }

        [Test]
        public void EventsShareTheSessionLog_WithSessionParticipantAndCondition()
        {
            Build(condition: "Static");
            orchestrator.BeginExperiment();
            PlaceAll();

            var types = Types();
            Assert.AreEqual("session:session_started", types[0]);
            Assert.AreEqual("Experiment:experiment_started", types[1]);
            Assert.AreEqual("Experiment:task_started", types[2]);
            Assert.AreEqual("GabineteFormas:trial_started", types[3]);
            CollectionAssert.Contains(types, "GabineteFormas:placement_attempt");
            CollectionAssert.Contains(types, "GabineteFormas:trial_completed");
            Assert.IsTrue(Lines().All(l => l.Contains("\"participant_id\":\"P-11\"") && l.Contains("\"condition\":\"Static\"") && l.Contains("\"session_id\":\"" + session.SessionId + "\"")));
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "events.jsonl").Length);
        }

        [Test]
        public void PlacementAttemptLine_CarriesPieceOpeningOrientationsAndResult()
        {
            Build(specs: new[] { Spec(4) });
            orchestrator.BeginExperiment();
            var prism = gabinete.Trial.Pieces.Single(p => p.Shape == GabineteShapeType.Prism);
            gabinete.OnPieceGrabbed(prism.Id, 90);
            gabinete.OnPieceReleased(prism.Id, PiecePose.At(prism.TargetOpening, 4, 90));

            var line = Lines().Single(l => l.Contains("\"event\":\"placement_attempt\""));
            StringAssert.Contains("\"task\":\"GabineteFormas\"", line);
            StringAssert.Contains($"\"piece_id\":\"{prism.Id}\"", line);
            StringAssert.Contains("\"piece_type\":\"prism\"", line);
            StringAssert.Contains("\"target_opening\":0", line);
            StringAssert.Contains("\"starting_orientation_deg\":90", line);
            StringAssert.Contains("\"attempted_orientation_deg\":90", line);
            StringAssert.Contains("\"placement_result\":\"wrong_orientation\"", line);
            StringAssert.Contains("\"valid\":false", line);
            StringAssert.Contains("\"resulting_state\":\"prism:open", line);
            StringAssert.Contains("\"trial_index\":1", line);
            StringAssert.Contains("\"timestamp_utc\"", line);
        }

        [Test]
        public void EachTrial_WritesItsSummaryJson_WithTheSessionEnvelope()
        {
            Build();
            orchestrator.BeginExperiment();
            PlaceAll();

            var path = Path.Combine(logger.CurrentSessionDirectory, "gabinete_trial_01_summary.json");
            Assert.IsTrue(File.Exists(path));
            var json = File.ReadAllText(path);
            StringAssert.Contains("\"participant_id\":\"P-11\"", json);
            StringAssert.Contains("\"condition\":\"PreAdapted\"", json);
            StringAssert.Contains("\"task\":\"GabineteFormas\"", json);
            StringAssert.Contains("\"shape_count\":4", json);
            StringAssert.Contains("\"placement_attempts\":4", json);
            StringAssert.Contains("\"valid_placements\":4", json);
            StringAssert.Contains("\"invalid_attempts\":0", json);
            StringAssert.Contains("\"completion_status\":\"completed\"", json);
            StringAssert.Contains("\"completion_time_s\":", json);
        }

        [Test]
        public void TimeLimit_ClosesTheTrialAsIncomplete_LocksThePieces_AndTheNextTrialStartsAfterThePause()
        {
            Build(specs: new[] { Spec(4, 5), Spec(6) });
            orchestrator.BeginExperiment();

            Engine.TickSeconds(6);
            Engine.Call(gabinete, "Update");

            Assert.AreEqual("incomplete", gabinete.Summaries.Single().Status);
            Assert.IsTrue(view.PiecesLocked);
            Assert.IsTrue(File.Exists(Path.Combine(logger.CurrentSessionDirectory, "gabinete_trial_01_summary.json")));
            Assert.IsTrue(Lines().Any(l => l.Contains("\"end_reason\":\"time_limit\"")));

            NextTrial();
            Assert.AreEqual(6, view.Shown.ShapeCount);
            Assert.IsTrue(gabinete.Trial.IsRunning);
            Assert.IsFalse(view.PiecesLocked, "the new cabinet's pieces can be picked up");
        }

        [Test]
        public void EndingTheTaskEarly_ClosesTheOpenTrial_HidesTheCabinet_AndReturnsTheTable()
        {
            Build();
            orchestrator.BeginExperiment();
            var wedge = gabinete.Trial.Pieces.Single(p => p.Shape == GabineteShapeType.Wedge);
            gabinete.OnPieceGrabbed(wedge.Id, 0);
            gabinete.OnPieceReleased(wedge.Id, PiecePose.At(wedge.TargetOpening, 4, 0));

            orchestrator.Abort("operator_stop");

            Assert.IsFalse(gabinete.IsRunning);
            Assert.IsFalse(gabinete.IsCompleted);
            Assert.IsFalse(view.Visible);
            Assert.IsTrue(view.PiecesLocked);
            Assert.IsTrue(otherTaskObjects.activeSelf);
            Assert.IsTrue(Lines().Any(l => l.Contains("\"event\":\"trial_summary\"") && l.Contains("\"end_reason\":\"operator_stop\"")));
            Assert.AreEqual("incomplete", gabinete.Summaries.Single().Status);
            Assert.AreEqual(1, gabinete.Summaries.Single().CompletedPieces);

            var cylinder = gabinete.Trial.Pieces.Single(p => p.Shape == GabineteShapeType.Cylinder);
            gabinete.OnPieceGrabbed(cylinder.Id, 0);
            gabinete.OnPieceReleased(cylinder.Id, PiecePose.At(cylinder.TargetOpening, 4, 0));
            Engine.TickSeconds(3);
            Assert.AreEqual(1, gabinete.Summaries.Count, "no trial starts after the task ended");
            Assert.AreEqual(1, gabinete.Trial.PlacedCount, "input after the end changes nothing");
        }

        [Test]
        public void ProtocolIsConfigurable_FromTheTrialSpecs()
        {
            Build(specs: new[] { new GabineteTrialSpec { shapes = "hexagon, wedge", traySlots = "1,0" } });
            orchestrator.BeginExperiment();

            Assert.AreEqual(1, gabinete.TrialCount);
            Assert.AreEqual(2, view.Shown.ShapeCount);
            PlaceAll();

            Assert.IsTrue(gabinete.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
        }

        [Test]
        public void TheTaskNeverStartsASessionItself()
        {
            Build();
            var sessionId = session.SessionId;
            var sessionsStarted = 0;
            session.SessionStarted += _ => sessionsStarted++;

            gabinete.StartTask(ExperimentCondition.Static);
            gabinete.EndTask("test");

            Assert.AreEqual(0, sessionsStarted);
            Assert.AreEqual(sessionId, session.SessionId);
        }

        [Test]
        public void Restarting_BeginsTheProtocolAgainFromTheFirstTrial()
        {
            Build();
            orchestrator.BeginExperiment();
            var wedge = gabinete.Trial.Pieces.Single(p => p.Shape == GabineteShapeType.Wedge);
            gabinete.OnPieceGrabbed(wedge.Id, 0);
            gabinete.OnPieceReleased(wedge.Id, PiecePose.At(wedge.TargetOpening, 4, 0));

            gabinete.StartTask(ExperimentCondition.Static);

            Assert.AreEqual(1, gabinete.Trial.TrialIndex);
            Assert.AreEqual(0, gabinete.Trial.PlacedCount);
            Assert.AreEqual(4, view.Shown.ShapeCount);
        }
    }
}
