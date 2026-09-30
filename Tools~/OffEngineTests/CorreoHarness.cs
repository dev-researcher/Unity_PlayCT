using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Correo;
using UnityEngine;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Runs the real CorreoTask, TaskOrchestrator, SessionManager and EventLogger scripts on the fake engine with a recording
    /// stand-in for the visual network. It covers only what is specific to El Correo (how it plugs into the orchestrator and
    /// drives its view); generic task and orchestrator behaviour is tested elsewhere, and the rules are covered by the logic
    /// tests. The network meshes, lettering, XR interactables and their selection are not exercised here.
    /// </summary>
    public class CorreoHarness
    {
        sealed class FakeMapView : MonoBehaviour, ICorreoView
        {
            public bool Visible;
            public bool AutoFinish = true;
            public string Selection = "";
            public readonly List<string> Calls = new List<string>();
            Action pendingDone;
            CorreoState pendingResult;
            public CorreoState Shown;

            public bool IsAnimating => pendingDone != null;

            public void SetVisible(bool visible) { Visible = visible; Calls.Add("visible:" + visible); }
            public void Show(CorreoTrialConfig config, CorreoState state) { Shown = state; Calls.Add("show:" + state.Snapshot()); }

            public void ShowSelection(Settlement? source, Settlement? destination, IReadOnlyList<CorreoPackage> packages) =>
                Selection = $"{source}>{destination}:{string.Join("+", packages)}";

            public void ShowNotShipped(CorreoShipment shipment) { Calls.Add("not_shipped:" + shipment.Label); }

            public void AnimateShipment(CorreoShipment shipment, CorreoState result, Action done)
            {
                Calls.Add("carry:" + shipment.Label);
                if (AutoFinish) { Shown = result; done?.Invoke(); return; }
                pendingDone = done;
                pendingResult = result;
            }

            public void FinishCarrying()
            {
                var done = pendingDone;
                pendingDone = null;
                Shown = pendingResult;
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

        const CorreoPackage P1 = CorreoPackage.P1;
        const CorreoPackage P2 = CorreoPackage.P2;
        const CorreoPackage P3 = CorreoPackage.P3;

        string logRoot;
        SessionManager session;
        EventLogger logger;
        ConditionManager conditions;
        TaskOrchestrator orchestrator;
        CorreoTask correo;
        FakeMapView view;
        GameObject otherTaskObjects;
        EarlierTask earlier;

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_correo_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (logger != null) Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        void Build(string condition = "PreAdapted", CorreoTrialSpec[] specs = null, string[] sequence = null, bool withEarlier = false)
        {
            session = new GameObject("SessionManager").AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", "P-21");
                Engine.Set(m, "condition", condition);
            });
            logger = new GameObject("EventLogger").AddComponent<EventLogger>();
            logger.Session = session;
            logger.RootDirectoryOverride = logRoot;
            conditions = new GameObject("ConditionManager").AddComponent<ConditionManager>(c => Engine.Set(c, "session", session));

            view = new GameObject("CorreoView").AddComponent<FakeMapView>();
            otherTaskObjects = new GameObject("OtherTaskObjects");
            correo = new GameObject("Correo_Task").AddComponent<CorreoTask>(t =>
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
                Engine.Set(o, "tasks", new MonoBehaviour[] { correo });
                Engine.Set(o, "beginOnStart", false);
                Engine.Set(o, "taskSequence", sequence ?? new[] { "ElCorreo" });
            });
            if (withEarlier)
            {
                earlier = new EarlierTask();
                orchestrator.Register(earlier);
            }
        }

        void Send(Settlement from, Settlement to, params CorreoPackage[] packages)
        {
            correo.TapSettlement(from);
            correo.TapSettlement(to);
            foreach (var package in packages) correo.TogglePackage(package);
            correo.Confirm();
        }

        void Solve()
        {
            Send(Settlement.A, Settlement.C, P1);
            Send(Settlement.C, Settlement.E, P1);
            Send(Settlement.A, Settlement.C, P2);
            Send(Settlement.A, Settlement.C, P3);
            Send(Settlement.C, Settlement.E, P2, P3);
        }

        string[] Lines() => File.ReadAllLines(logger.CurrentLogPath);

        string[] Types() => Lines().Select(l => Extract(l, "task") + ":" + Extract(l, "event")).ToArray();

        static string Extract(string line, string key)
        {
            var marker = "\"" + key + "\":\"";
            var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return line.Substring(start, line.IndexOf('"', start) - start);
        }

        [Test]
        public void Correo_IsACommonTask_WithAStableId_AndNothingShownBeforeItStarts()
        {
            Build();
            IExperimentTask task = correo;

            Assert.AreEqual("ElCorreo", task.TaskId);
            Assert.IsFalse(task.IsRunning);
            Assert.IsFalse(task.IsCompleted);
            Assert.IsFalse(view.Visible);
        }

        [TestCase("Static", ExperimentCondition.Static)]
        [TestCase("PreAdapted", ExperimentCondition.PreAdapted)]
        public void Orchestrator_StartsCorreo_WithTheSessionCondition_ShowsTheNetworkWithEveryPackageAtA_AndClearsTheTable(string name, ExperimentCondition expected)
        {
            Build(condition: name);

            Assert.IsTrue(orchestrator.BeginExperiment());

            Assert.AreEqual("ElCorreo", orchestrator.CurrentTaskId);
            Assert.IsTrue(correo.IsRunning);
            Assert.AreEqual(expected, correo.Condition);
            Assert.IsTrue(view.Visible);
            Assert.AreEqual("P1:A,P2:A,P3:A", view.Shown.Snapshot());
            Assert.IsFalse(otherTaskObjects.activeSelf, "other tasks' objects leave the table while El Correo uses it");
            Assert.AreEqual(1, correo.TrialCount, "the default protocol is a single trial");
        }

        [Test]
        public void Orchestrator_RunsCorreoAfterAnEarlierTask_OnlyWhenThatTaskIsDone()
        {
            Build(sequence: new[] { "Earlier", "ElCorreo" }, withEarlier: true);
            orchestrator.BeginExperiment();

            Assert.IsFalse(correo.IsRunning);
            Assert.IsFalse(view.Visible);

            earlier.Finish();

            Assert.IsTrue(correo.IsRunning);
            Assert.IsTrue(view.Visible);
        }

        [Test]
        public void SolvingTheTask_CompletesIt_AndTheOrchestratorHandsTheTableBack()
        {
            Build();
            orchestrator.BeginExperiment();
            var completedEvents = 0;
            correo.Completed += _ => completedEvents++;

            Solve();

            Assert.AreEqual(1, completedEvents);
            Assert.IsTrue(correo.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
            Assert.IsFalse(view.Visible);
            Assert.IsTrue(otherTaskObjects.activeSelf);
            var summary = correo.Summaries.Single();
            Assert.AreEqual("completed", summary.Status);
            Assert.AreEqual(5, summary.ValidShipments);
            Assert.AreEqual(0, summary.InvalidShipments);
        }

        [Test]
        public void Confirm_HandsTheShipmentToTheView_AndARefusedOneOnlyGetsTheNeutralCue()
        {
            Build();
            orchestrator.BeginExperiment();

            Send(Settlement.A, Settlement.C, P2, P3);
            Send(Settlement.A, Settlement.C, P2);

            CollectionAssert.AreEqual(new[] { "not_shipped:A>C:P2+P3", "carry:A>C:P2" }, view.Calls.Where(c => c.StartsWith("not_shipped") || c.StartsWith("carry")).ToArray());
            Assert.AreEqual("P1:A,P2:C,P3:A", correo.Trial.State.Snapshot());
            Assert.AreEqual("P1:A,P2:C,P3:A", view.Shown.Snapshot());
            Assert.AreEqual(1, correo.Trial.InvalidShipments);
        }

        [Test]
        public void SelectionIsShownAsMade_AndClearedAfterConfirmation()
        {
            Build();
            orchestrator.BeginExperiment();

            correo.TapSettlement(Settlement.A);
            Assert.AreEqual("A>:", view.Selection);
            correo.TapSettlement(Settlement.B);
            correo.TogglePackage(P3);
            correo.TogglePackage(P2);
            Assert.AreEqual("A>B:P2+P3", view.Selection);

            correo.Confirm();

            Assert.AreEqual(">:", view.Selection);
            correo.TapSettlement(Settlement.C);
            correo.ClearSelection();
            Assert.AreEqual(">:", view.Selection);
        }

        [Test]
        public void WhileAShipmentIsBeingCarried_FurtherInputIsIgnored_AndCompletionWaitsForTheCarry()
        {
            Build();
            view.AutoFinish = false;
            orchestrator.BeginExperiment();
            Send(Settlement.A, Settlement.C, P1);
            Assert.IsTrue(view.IsAnimating);

            correo.TapSettlement(Settlement.C);

            Assert.IsNull(correo.Trial.SelectedSource, "input during the carry does nothing");
            view.FinishCarrying();
            view.AutoFinish = true;
            Send(Settlement.C, Settlement.E, P1);
            Send(Settlement.A, Settlement.C, P2);
            Send(Settlement.A, Settlement.C, P3);
            view.AutoFinish = false;
            Send(Settlement.C, Settlement.E, P2, P3);

            Assert.IsTrue(correo.Trial.IsCompleted, "the logic already knows");
            Assert.IsFalse(correo.IsCompleted);
            Assert.AreEqual(0, correo.Summaries.Count);

            view.FinishCarrying();

            Assert.IsTrue(correo.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
        }

        [Test]
        public void EventsShareTheSessionLog_WithSessionParticipantAndCondition()
        {
            Build(condition: "Static");
            orchestrator.BeginExperiment();
            Solve();

            var types = Types();
            Assert.AreEqual("session:session_started", types[0]);
            Assert.AreEqual("Experiment:experiment_started", types[1]);
            Assert.AreEqual("Experiment:task_started", types[2]);
            Assert.AreEqual("ElCorreo:trial_started", types[3]);
            CollectionAssert.Contains(types, "ElCorreo:selection_changed");
            CollectionAssert.Contains(types, "ElCorreo:shipment_attempt");
            CollectionAssert.Contains(types, "ElCorreo:trial_completed");
            Assert.IsTrue(Lines().All(l => l.Contains("\"participant_id\":\"P-21\"") && l.Contains("\"condition\":\"Static\"") && l.Contains("\"session_id\":\"" + session.SessionId + "\"")));
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "events.jsonl").Length);
        }

        [Test]
        public void ShipmentAttemptLine_CarriesTheDecisionAndItsResult()
        {
            Build();
            orchestrator.BeginExperiment();
            Send(Settlement.A, Settlement.C, P2, P3);

            var line = Lines().Single(l => l.Contains("\"event\":\"shipment_attempt\""));
            StringAssert.Contains("\"task\":\"ElCorreo\"", line);
            StringAssert.Contains("\"trial_index\":1", line);
            StringAssert.Contains("\"source\":\"A\"", line);
            StringAssert.Contains("\"destination\":\"C\"", line);
            StringAssert.Contains("\"selected_packages\":[\"P2\",\"P3\"]", line);
            StringAssert.Contains("\"selected_count\":2", line);
            StringAssert.Contains("\"path_capacity\":1", line);
            StringAssert.Contains("\"legal\":false", line);
            StringAssert.Contains("\"outcome\":\"capacity_exceeded\"", line);
            StringAssert.Contains("\"rejection_category\":\"capacity\"", line);
            StringAssert.Contains("\"locations_before\":\"P1:A,P2:A,P3:A\"", line);
            StringAssert.Contains("\"locations_after\":\"P1:A,P2:A,P3:A\"", line);
            StringAssert.Contains("\"shipment_number\":0", line);
            StringAssert.Contains("\"completion_status\":\"in_progress\"", line);
            StringAssert.Contains("\"timestamp_utc\"", line);
        }

        [Test]
        public void TheTrial_WritesItsSummaryJson_WithTheSessionEnvelope()
        {
            Build();
            orchestrator.BeginExperiment();
            Send(Settlement.C, Settlement.E, P2);
            Solve();

            var path = Path.Combine(logger.CurrentSessionDirectory, "correo_trial_01_summary.json");
            Assert.IsTrue(File.Exists(path));
            var json = File.ReadAllText(path);
            StringAssert.Contains("\"participant_id\":\"P-21\"", json);
            StringAssert.Contains("\"condition\":\"PreAdapted\"", json);
            StringAssert.Contains("\"task\":\"ElCorreo\"", json);
            StringAssert.Contains("\"shipment_attempts\":6", json);
            StringAssert.Contains("\"valid_shipments\":5", json);
            StringAssert.Contains("\"invalid_shipments\":1", json);
            StringAssert.Contains("\"rejected_path_attempts\":0", json);
            StringAssert.Contains("\"rejected_selection_attempts\":1", json);
            StringAssert.Contains("\"final_locations\":\"P1:E,P2:E,P3:E\"", json);
            StringAssert.Contains("\"priority_satisfied\":true", json);
            StringAssert.Contains("\"completion_status\":\"completed\"", json);
            StringAssert.Contains("\"completion_time_s\":", json);
        }

        [Test]
        public void TimeLimit_ClosesTheTrialAsIncomplete_AndTheOrchestratorMovesOn()
        {
            Build(specs: new[] { new CorreoTrialSpec { durationSeconds = 5 } });
            orchestrator.BeginExperiment();
            Send(Settlement.A, Settlement.C, P1);

            Engine.TickSeconds(6);
            Engine.Call(correo, "Update");

            var summary = correo.Summaries.Single();
            Assert.AreEqual("incomplete", summary.Status);
            Assert.AreEqual("P1:C,P2:A,P3:A", summary.FinalLocations);
            Assert.IsTrue(File.Exists(Path.Combine(logger.CurrentSessionDirectory, "correo_trial_01_summary.json")));
            Assert.IsTrue(Lines().Any(l => l.Contains("\"end_reason\":\"time_limit\"")));
            Assert.IsTrue(correo.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
        }

        [Test]
        public void EndingTheTaskEarly_ClosesTheOpenTrial_HidesTheNetwork_AndReturnsTheTable()
        {
            Build();
            orchestrator.BeginExperiment();
            Send(Settlement.A, Settlement.C, P1);

            orchestrator.Abort("operator_stop");

            Assert.IsFalse(correo.IsRunning);
            Assert.IsFalse(correo.IsCompleted);
            Assert.IsFalse(view.Visible);
            Assert.IsTrue(otherTaskObjects.activeSelf);
            Assert.IsTrue(Lines().Any(l => l.Contains("\"event\":\"trial_summary\"") && l.Contains("\"end_reason\":\"operator_stop\"")));
            Assert.AreEqual("incomplete", correo.Summaries.Single().Status);

            Send(Settlement.C, Settlement.E, P1);
            Assert.AreEqual("P1:C,P2:A,P3:A", correo.Trial.State.Snapshot(), "input after the end changes nothing");
        }

        [Test]
        public void ProtocolIsConfigurable_WithSeveralTrials_EachStartingFromTheInitialState()
        {
            Build(specs: new[] { new CorreoTrialSpec(), new CorreoTrialSpec() });
            orchestrator.BeginExperiment();

            Solve();
            Assert.IsTrue(correo.IsRunning, "the second trial follows after a pause");
            Engine.TickSeconds(1.5);

            Assert.AreEqual(2, correo.Trial.TrialIndex);
            Assert.AreEqual("P1:A,P2:A,P3:A", correo.Trial.State.Snapshot());
            Assert.AreEqual("P1:A,P2:A,P3:A", view.Shown.Snapshot());
            Solve();
            Assert.IsTrue(correo.IsCompleted);
            Assert.IsTrue(File.Exists(Path.Combine(logger.CurrentSessionDirectory, "correo_trial_02_summary.json")));
        }

        [Test]
        public void Restarting_BeginsTheProtocolAgainFromTheInitialState()
        {
            Build();
            orchestrator.BeginExperiment();
            Send(Settlement.A, Settlement.C, P1);

            correo.StartTask(ExperimentCondition.Static);

            Assert.AreEqual(1, correo.Trial.TrialIndex);
            Assert.AreEqual("P1:A,P2:A,P3:A", correo.Trial.State.Snapshot());
            Assert.AreEqual(0, correo.Trial.ShipmentAttempts);
            Assert.AreEqual("P1:A,P2:A,P3:A", view.Shown.Snapshot());
        }

        [Test]
        public void TheTaskNeverStartsASessionItself()
        {
            Build();
            var sessionId = session.SessionId;
            var sessionsStarted = 0;
            session.SessionStarted += _ => sessionsStarted++;

            correo.StartTask(ExperimentCondition.Static);
            correo.EndTask("test");

            Assert.AreEqual(0, sessionsStarted);
            Assert.AreEqual(sessionId, session.SessionId);
        }
    }
}
