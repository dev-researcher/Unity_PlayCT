using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Cubo;
using UnityEngine;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Runs the real CuboTask, TaskOrchestrator, SessionManager and EventLogger scripts on the fake engine with a
    /// recording stand-in for the visual cube. The visual cube and the XR handles are not exercised here.
    /// </summary>
    public class CuboHarness
    {
        sealed class FakeCuboView : MonoBehaviour, ICuboView
        {
            public bool Visible;
            public bool Animating;
            public readonly List<string> Calls = new List<string>();
            public CubeState Shown;
            public bool AutoFinish = true;
            Action pending;

            public bool IsAnimating => Animating;
            public void SetVisible(bool visible) { Visible = visible; Calls.Add("visible:" + visible); }
            public void Show(CubeState state) { Shown = state; Calls.Add("show"); }

            public void AnimateTurn(CubeMove move, CubeState result, Action done)
            {
                Calls.Add("turn:" + move.Notation);
                Shown = result;
                if (AutoFinish) { done?.Invoke(); return; }
                Animating = true;
                pending = done;
            }

            public void FinishAnimation()
            {
                Animating = false;
                var done = pending;
                pending = null;
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
        CuboTask cubo;
        FakeCuboView view;
        GameObject otherTaskObjects;
        EarlierTask earlier;

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_cubo_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (logger != null) Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        void Build(string condition = "PreAdapted", CuboTrialSpec[] specs = null, string[] sequence = null, bool withEarlier = false)
        {
            session = new GameObject("SessionManager").AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", "P-09");
                Engine.Set(m, "condition", condition);
            });
            logger = new GameObject("EventLogger").AddComponent<EventLogger>();
            logger.Session = session;
            logger.RootDirectoryOverride = logRoot;
            conditions = new GameObject("ConditionManager").AddComponent<ConditionManager>(c => Engine.Set(c, "session", session));

            view = new GameObject("CuboView").AddComponent<FakeCuboView>();
            otherTaskObjects = new GameObject("OtherTaskObjects");
            cubo = new GameObject("Cubo_Task").AddComponent<CuboTask>(t =>
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
                Engine.Set(o, "tasks", new MonoBehaviour[] { cubo });
                Engine.Set(o, "beginOnStart", false);
                Engine.Set(o, "taskSequence", sequence ?? new[] { "CuboRelaciones" });
            });
            if (withEarlier)
            {
                earlier = new EarlierTask();
                orchestrator.Register(earlier);
            }
        }

        static CubeMove M(string text) => CubeNotation.Parse(text).Single();

        void Play(params string[] moves)
        {
            cubo.SetStabilized(true);
            foreach (var move in moves) cubo.Rotate(M(move));
        }

        void FinishTrial1() => Play("R", "F'");
        void FinishTrial2() => Play("F");
        void FinishTrial3() => Play("F'", "U'", "R'");

        void FinishTrial4()
        {
            Play("R");
            cubo.SelectFace(CubeFace.Left);
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
        public void Cubo_IsACommonTask_WithAStableId()
        {
            Build();
            IExperimentTask task = cubo;

            Assert.AreEqual("CuboRelaciones", task.TaskId);
            Assert.IsFalse(task.IsRunning);
            Assert.IsFalse(task.IsCompleted);
            Assert.IsFalse(view.Visible, "nothing is shown before the orchestrator starts the task");
        }

        [Test]
        public void Orchestrator_StartsCubo_ShowsTheFirstTrial_AndClearsTheTable()
        {
            Build();

            Assert.IsTrue(orchestrator.BeginExperiment());

            Assert.AreEqual("CuboRelaciones", orchestrator.CurrentTaskId);
            Assert.IsTrue(cubo.IsRunning);
            Assert.IsTrue(view.Visible);
            Assert.AreEqual(cubo.Trial.InitialState, view.Shown);
            Assert.AreEqual("cruz_clara", CuboMiniTasks.Id(cubo.Trial.MiniTask));
            Assert.IsFalse(otherTaskObjects.activeSelf, "other tasks' objects leave the table while Cubo uses it");
            Assert.AreEqual(ExperimentCondition.PreAdapted, cubo.Condition);
        }

        [Test]
        public void Orchestrator_RunsCuboAfterAnEarlierTask_OnlyWhenThatTaskIsDone()
        {
            Build(sequence: new[] { "Earlier", "CuboRelaciones" }, withEarlier: true);
            orchestrator.BeginExperiment();

            Assert.IsFalse(cubo.IsRunning);
            Assert.IsFalse(view.Visible);

            earlier.Finish();

            Assert.IsTrue(cubo.IsRunning);
            Assert.IsTrue(view.Visible);
        }

        [Test]
        public void FullProtocol_RunsFourTrialsInOrder_ThenTheOrchestratorEndsTheTask()
        {
            Build();
            orchestrator.BeginExperiment();
            var completedEvents = 0;
            cubo.Completed += _ => completedEvents++;

            FinishTrial1();
            Assert.IsTrue(cubo.IsRunning, "the next trial follows after a pause");
            Assert.IsFalse(cubo.Trial.IsRunning);
            NextTrial();
            Assert.AreEqual("corregir_una_pieza", CuboMiniTasks.Id(cubo.Trial.MiniTask));
            Assert.AreEqual(cubo.Trial.InitialState, view.Shown);

            FinishTrial2();
            NextTrial();
            FinishTrial3();
            NextTrial();
            Assert.AreEqual("que_permanece", CuboMiniTasks.Id(cubo.Trial.MiniTask));
            Assert.IsFalse(cubo.IsCompleted);
            FinishTrial4();

            Assert.AreEqual(1, completedEvents);
            Assert.IsTrue(cubo.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
            Assert.IsFalse(view.Visible, "the orchestrator ended the task, which hides the cube");
            Assert.IsTrue(otherTaskObjects.activeSelf, "the table is handed back");
            CollectionAssert.AreEqual(new[] { "cruz_clara", "corregir_una_pieza", "elegir_una_secuencia", "que_permanece" }, cubo.Summaries.Select(s => s.MiniTask));
            Assert.IsTrue(cubo.Summaries.All(s => s.Status == "completed"));
        }

        [Test]
        public void EventsShareTheSessionLog_WithSessionParticipantAndCondition()
        {
            Build();
            orchestrator.BeginExperiment();
            FinishTrial1();

            var types = Types();
            Assert.AreEqual("session:session_started", types[0]);
            Assert.AreEqual("Experiment:experiment_started", types[1]);
            Assert.AreEqual("Experiment:task_started", types[2]);
            Assert.AreEqual("CuboRelaciones:trial_started", types[3]);
            CollectionAssert.Contains(types, "CuboRelaciones:rotation");
            CollectionAssert.Contains(types, "CuboRelaciones:trial_completed");
            Assert.IsTrue(Lines().All(l => l.Contains("\"participant_id\":\"P-09\"") && l.Contains("\"condition\":\"PreAdapted\"") && l.Contains("\"session_id\":\"" + session.SessionId + "\"")));
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "events.jsonl").Length);
        }

        [Test]
        public void RotationEvents_CarryFaceAxisDirectionAmountAndStates()
        {
            Build();
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);
            var before = cubo.Trial.State.Snapshot();
            cubo.Rotate(M("R'"));

            var line = Lines().Single(l => l.Contains("\"event\":\"rotation\""));
            StringAssert.Contains("\"task\":\"CuboRelaciones\"", line);
            StringAssert.Contains("\"mini_task\":\"cruz_clara\"", line);
            StringAssert.Contains("\"selected_face\":\"R\"", line);
            StringAssert.Contains("\"rotation_axis\":\"x\"", line);
            StringAssert.Contains("\"rotation_direction\":\"counterclockwise\"", line);
            StringAssert.Contains("\"rotation_amount_deg\":90", line);
            StringAssert.Contains("\"legal\":true", line);
            StringAssert.Contains("\"previous_state\":\"" + before + "\"", line);
            StringAssert.Contains("\"resulting_state\":\"" + cubo.Trial.State.Snapshot() + "\"", line);
            StringAssert.Contains("\"trial_index\":1", line);
            StringAssert.Contains("\"timestamp_utc\"", line);
        }

        [Test]
        public void Rotations_NeedTheStabilizingHand_AndInvalidOnesLeaveTheCubeUnchanged()
        {
            Build();
            orchestrator.BeginExperiment();
            cubo.SetStabilized(false);
            var before = cubo.Trial.State;

            cubo.Rotate(M("R"));

            Assert.AreEqual(before, cubo.Trial.State);
            Assert.IsFalse(view.Calls.Any(c => c.StartsWith("turn:")), "the visual cube did not move");
            Assert.IsTrue(Lines().Any(l => l.Contains("\"event\":\"rotation\"") && l.Contains("\"legal\":false") && l.Contains("\"outcome\":\"not_stabilized\"")));
            Assert.IsTrue(cubo.Trial.IsRunning);

            cubo.SetStabilized(true);
            cubo.Rotate(M("R"));
            Assert.IsTrue(view.Calls.Contains("turn:R"));
        }

        [Test]
        public void StabilizerHeldBeforeTheTrialBegins_CarriesOverToTheNextTrial()
        {
            Build();
            cubo.SetStabilized(true);
            orchestrator.BeginExperiment();

            Assert.IsTrue(cubo.Trial.Stabilized);
            cubo.Rotate(M("R"));
            Assert.AreEqual(1, cubo.Trial.ValidRotations);
        }

        [Test]
        public void RotationNotInTheAllowedSet_IsRefusedWithoutMovingTheCube()
        {
            Build(specs: new[] { new CuboTrialSpec { miniTask = "cruz_clara", initialMoves = "F R'", allowedMoves = "R R'" } });
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);

            cubo.Rotate(M("U"));

            Assert.AreEqual(0, cubo.Trial.ValidRotations);
            Assert.AreEqual(1, cubo.Trial.InvalidActions);
            Assert.IsFalse(view.Calls.Any(c => c.StartsWith("turn:")));
        }

        [Test]
        public void Gestures_MapTapAndTwistToSelectionAndQuarterTurns()
        {
            Build();
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);

            cubo.ReleaseFace(CubeFace.Right, 0f, 0.01f, 0.08f);
            Assert.AreEqual(CubeFace.Right, cubo.Trial.SelectedFace);
            Assert.AreEqual(0, cubo.Trial.ValidRotations);

            cubo.ReleaseFace(CubeFace.Right, 60f, 0.09f, 0.10f);
            Assert.AreEqual(new[] { "R" }, cubo.Trial.RotationSequence);

            cubo.ReleaseFace(CubeFace.Right, -60f, 0.09f, 0.10f);
            Assert.AreEqual(new[] { "R", "R'" }, cubo.Trial.RotationSequence);

            var events = Lines().Length;
            cubo.ReleaseFace(CubeFace.Right, 8f, 0.09f, 0.10f);
            Assert.AreEqual(events, Lines().Length, "an unclear movement does nothing and logs nothing");
        }

        [Test]
        public void InputIsIgnoredWhileTheCubeIsTurning()
        {
            Build();
            view.AutoFinish = false;
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);

            cubo.Rotate(M("R"));
            cubo.Rotate(M("U"));
            Assert.AreEqual(new[] { "R" }, cubo.Trial.RotationSequence);

            view.FinishAnimation();
            cubo.Rotate(M("U"));
            Assert.AreEqual(new[] { "R", "U" }, cubo.Trial.RotationSequence);
        }

        [Test]
        public void CompletionIsReportedOnlyAfterTheLastTurnHasFinishedAnimating()
        {
            Build(specs: new[] { new CuboTrialSpec { miniTask = "corregir_una_pieza", initialMoves = "F'" } });
            view.AutoFinish = false;
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);

            cubo.Rotate(M("F"));
            Assert.IsFalse(cubo.IsCompleted);
            Assert.AreEqual(0, cubo.Summaries.Count);

            view.FinishAnimation();
            Assert.IsTrue(cubo.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
        }

        [Test]
        public void EachTrial_WritesItsSummaryJson_WithTheSessionEnvelope()
        {
            Build();
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);
            FinishTrial1();

            var path = Path.Combine(logger.CurrentSessionDirectory, "cubo_trial_01_summary.json");
            Assert.IsTrue(File.Exists(path));
            var json = File.ReadAllText(path);
            StringAssert.Contains("\"participant_id\":\"P-09\"", json);
            StringAssert.Contains("\"condition\":\"PreAdapted\"", json);
            StringAssert.Contains("\"task\":\"CuboRelaciones\"", json);
            StringAssert.Contains("\"mini_task\":\"cruz_clara\"", json);
            StringAssert.Contains("\"valid_rotations\":2", json);
            StringAssert.Contains("\"invalid_actions\":0", json);
            StringAssert.Contains("\"completion_status\":\"completed\"", json);
            StringAssert.Contains("\"rotation_sequence\":[\"R\",\"F'\"]", json);
            StringAssert.Contains("\"completion_time_s\":", json);
        }

        [Test]
        public void TimeLimit_ClosesTheTrialAsIncomplete_AndTheNextTrialStartsAfterThePause()
        {
            Build(specs: new[]
            {
                new CuboTrialSpec { miniTask = "cruz_clara", initialMoves = "F R'", durationSeconds = 5 },
                new CuboTrialSpec { miniTask = "corregir_una_pieza", initialMoves = "F'" },
            });
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);

            Engine.TickSeconds(6);
            Engine.Call(cubo, "Update");

            Assert.AreEqual("incomplete", cubo.Summaries.Single().Status);
            Assert.IsTrue(File.Exists(Path.Combine(logger.CurrentSessionDirectory, "cubo_trial_01_summary.json")));
            Assert.IsTrue(Lines().Any(l => l.Contains("\"end_reason\":\"time_limit\"")));

            NextTrial();
            Assert.AreEqual("corregir_una_pieza", CuboMiniTasks.Id(cubo.Trial.MiniTask));
            Assert.IsTrue(cubo.Trial.IsRunning);
        }

        [Test]
        public void EndingTheTaskEarly_ClosesTheOpenTrial_HidesTheCube_AndReturnsTheTable()
        {
            Build();
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);
            cubo.Rotate(M("R"));

            orchestrator.Abort("operator_stop");

            Assert.IsFalse(cubo.IsRunning);
            Assert.IsFalse(cubo.IsCompleted);
            Assert.IsFalse(view.Visible);
            Assert.IsTrue(otherTaskObjects.activeSelf);
            Assert.IsTrue(Lines().Any(l => l.Contains("\"event\":\"trial_summary\"") && l.Contains("\"end_reason\":\"operator_stop\"")));
            Assert.AreEqual("incomplete", cubo.Summaries.Single().Status);

            cubo.Rotate(M("U"));
            Engine.TickSeconds(3);
            Assert.AreEqual(1, cubo.Summaries.Count, "no trial starts after the task ended");
        }

        [Test]
        public void ProtocolIsConfigurable_FromTheTrialSpecs()
        {
            Build(specs: new[] { new CuboTrialSpec { miniTask = "corregir_una_pieza", initialMoves = "U'", allowedMoves = "U U'" } });
            orchestrator.BeginExperiment();

            Assert.AreEqual(1, cubo.TrialCount);
            cubo.SetStabilized(true);
            cubo.Rotate(M("R"));
            Assert.AreEqual(1, cubo.Trial.InvalidActions);
            cubo.Rotate(M("U"));

            Assert.IsTrue(cubo.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
        }

        [Test]
        public void Restarting_BeginsTheProtocolAgainFromTheFirstTrial()
        {
            Build();
            orchestrator.BeginExperiment();
            cubo.SetStabilized(true);
            cubo.Rotate(M("R"));

            cubo.StartTask(ExperimentCondition.Static);

            Assert.AreEqual(1, cubo.Trial.TrialIndex);
            Assert.AreEqual(0, cubo.Trial.ValidRotations);
            Assert.AreEqual(cubo.Trial.InitialState, view.Shown);
        }
    }
}
