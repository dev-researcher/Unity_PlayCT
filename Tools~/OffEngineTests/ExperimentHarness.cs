using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;
using PlayCT.Tests;
using UnityEngine;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Wires ConditionManager + TaskOrchestrator to the real SessionManager, EventLogger and HanoiTask scripts on the
    /// fake engine (Hanoi is driven through the trial logic the disks would call), and checks the shared events.jsonl.
    /// </summary>
    public class ExperimentHarness
    {
        sealed class ExtraTask : IExperimentTask
        {
            public string TaskId { get; }
            public bool IsRunning { get; private set; }
            public bool IsCompleted { get; private set; }
            public ExperimentCondition? StartedWith;
            public event Action<IExperimentTask> Completed;
            public ExtraTask(string id) { TaskId = id; }
            public void StartTask(ExperimentCondition condition) { StartedWith = condition; IsRunning = true; }
            public void EndTask(string reason) { IsRunning = false; }
            public void Finish() { IsCompleted = true; Completed?.Invoke(this); }
        }

        string logRoot;
        SessionManager session;
        EventLogger logger;
        ConditionManager conditions;
        TaskOrchestrator orchestrator;
        HanoiTask hanoi;
        HanoiPeg[] pegs;
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable[] grabs;
        readonly UnityEngine.XR.Interaction.Toolkit.Interactors.FakeHand hand = new UnityEngine.XR.Interaction.Toolkit.Interactors.FakeHand();

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_exp_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (logger != null) Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        void Build(string condition, bool beginOnStart = false, string[] sequence = null)
        {
            session = new GameObject("SessionManager").AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", "P-01");
                Engine.Set(m, "condition", condition);
            });
            logger = new GameObject("EventLogger").AddComponent<EventLogger>();
            logger.Session = session;
            logger.RootDirectoryOverride = logRoot;
            conditions = new GameObject("ConditionManager").AddComponent<ConditionManager>(c => Engine.Set(c, "session", session));

            pegs = new HanoiPeg[3];
            var layout = new[] { ("Origen", -0.30f), ("Apoyo", 0f), ("Destino", 0.30f) };
            for (var i = 0; i < 3; i++)
            {
                var go = new GameObject("Peg_" + layout[i].Item1);
                go.transform.position = new Vector3(layout[i].Item2, 0.78f, 0.70f);
                pegs[i] = go.AddComponent<HanoiPeg>();
                pegs[i].Configure(layout[i].Item1, 0.16f);
            }
            var disks = new HanoiDisk[5];
            grabs = new UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable[5];
            for (var size = 1; size <= 5; size++)
            {
                var go = new GameObject("Disk_" + size);
                go.AddComponent<Rigidbody>();
                grabs[size - 1] = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                var sz = size;
                disks[size - 1] = go.AddComponent<HanoiDisk>(d => Engine.Set(d, "size", sz));
            }
            hanoi = new GameObject("Hanoi_Task").AddComponent<HanoiTask>(t =>
            {
                Engine.Set(t, "pegs", pegs);
                Engine.Set(t, "disks", disks);
                Engine.Set(t, "session", session);
                Engine.Set(t, "eventLogger", logger);
                Engine.Set(t, "diskCount", 3);
                Engine.Set(t, "useSessionDiskCount", false);
                Engine.Set(t, "beginOnStart", false);
            });

            orchestrator = new GameObject("Experiment").AddComponent<TaskOrchestrator>(o =>
            {
                Engine.Set(o, "session", session);
                Engine.Set(o, "eventLogger", logger);
                Engine.Set(o, "conditionManager", conditions);
                Engine.Set(o, "tasks", new MonoBehaviour[] { hanoi });
                Engine.Set(o, "beginOnStart", beginOnStart);
                if (sequence != null) Engine.Set(o, "taskSequence", sequence);
            });
        }

        void SolveHanoi()
        {
            foreach (var (disk, _, to) in HanoiSolver.Solve(hanoi.DiskCount))
            {
                Assert.IsTrue(grabs[disk - 1].TryGrab(hand), $"hand could not grab D{disk}");
                grabs[disk - 1].ReleaseAt(pegs.First(p => p.PegName == to).TopPoint + Vector3.up * 0.05f);
                Engine.TickSeconds(1.0);
            }
        }

        string[] EventTypes() => File.ReadAllLines(logger.CurrentLogPath)
            .Select(l => (task: Extract(l, "task"), type: Extract(l, "event")))
            .Select(x => x.task + ":" + x.type).ToArray();

        static string Extract(string line, string key)
        {
            var marker = "\"" + key + "\":\"";
            var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return line.Substring(start, line.IndexOf('"', start) - start);
        }

        [TestCase("Static", ExperimentCondition.Static)]
        [TestCase("PreAdapted", ExperimentCondition.PreAdapted)]
        [TestCase("pre-adapted", ExperimentCondition.PreAdapted)]
        public void ConditionManager_ReadsTheSessionCondition_AndNormalizesItsSpelling(string text, ExperimentCondition expected)
        {
            Build(text);

            Assert.IsTrue(conditions.IsValid);
            Assert.AreEqual(expected, conditions.Current);
            Assert.AreEqual(ExperimentConditions.ToLabel(expected), session.Condition, "one canonical spelling is used in logs");
        }

        [Test]
        public void ConditionManager_LegacyBaselineBecomesStatic()
        {
            Build("baseline");
            Assert.AreEqual(ExperimentCondition.Static, conditions.Current);
            Assert.AreEqual("Static", session.Condition);
        }

        [Test]
        public void ConditionManager_Set_UpdatesTheSessionManager_AndRaisesChanged()
        {
            Build("Static");
            ExperimentCondition? seen = null;
            conditions.Changed += c => seen = c;

            conditions.Set(ExperimentCondition.PreAdapted);

            Assert.AreEqual("PreAdapted", session.Condition);
            Assert.AreEqual(ExperimentCondition.PreAdapted, seen);
        }

        [Test]
        public void ConditionManager_UnsupportedText_IsInvalidAndCurrentThrows()
        {
            Build("condition-B");

            Assert.IsFalse(conditions.IsValid);
            Assert.AreEqual("condition-B", conditions.RawCondition, "the text is left untouched");
            Assert.Throws<InvalidOperationException>(() => { var _ = conditions.Current; });
        }

        [Test]
        public void Hanoi_ImplementsTheCommonTask_AndDoesNotAutoStartWhenBeginOnStartIsOff()
        {
            Build("Static");
            Engine.Call(hanoi, "Start");

            IExperimentTask task = hanoi;
            Assert.AreEqual("Hanoi", task.TaskId);
            Assert.IsFalse(task.IsRunning);
            Assert.IsFalse(task.IsCompleted);
        }

        [Test]
        public void Orchestrator_RunsHanoiThroughTheCommonAbstraction_ToSessionCompletion()
        {
            Build("PreAdapted");
            Engine.Call(hanoi, "Start");

            Assert.IsTrue(orchestrator.BeginExperiment());

            Assert.AreEqual(ExperimentState.Running, orchestrator.State);
            Assert.AreEqual("Hanoi", orchestrator.CurrentTaskId);
            Assert.IsTrue(hanoi.IsRunning);

            SolveHanoi();

            Assert.IsTrue(hanoi.IsCompleted);
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
            Assert.IsNull(orchestrator.CurrentTaskId);
            Assert.AreEqual(new[] { "Hanoi" }, orchestrator.Sequencer.CompletedTaskIds);
        }

        [Test]
        public void Orchestrator_WritesLifecycleAndHanoiEventsToTheSameEventsJsonl()
        {
            Build("PreAdapted");
            Engine.Call(hanoi, "Start");
            orchestrator.BeginExperiment();
            SolveHanoi();

            var types = EventTypes();
            Assert.AreEqual("session:session_started", types[0]);
            Assert.AreEqual("Experiment:experiment_started", types[1]);
            Assert.AreEqual("Experiment:task_started", types[2]);
            Assert.AreEqual("Hanoi:trial_started", types[3]);
            Assert.Less(Array.IndexOf(types, "Hanoi:trial_summary"), Array.IndexOf(types, "Experiment:task_ended"));
            Assert.AreEqual(new[] { "Experiment:task_ended", "Experiment:experiment_ended" }, types.Skip(types.Length - 2).ToArray());

            var lines = File.ReadAllLines(logger.CurrentLogPath);
            Assert.IsTrue(lines.All(l => l.Contains("\"participant_id\":\"P-01\"") && l.Contains("\"condition\":\"PreAdapted\"")),
                "every event, from every task, carries the same session and condition");
            StringAssert.Contains("\"task_sequence\":[\"Hanoi\"]", lines[1]);
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "events.jsonl").Length);
        }

        [Test]
        public void Orchestrator_PassesTheConditionToTasks_AndRunsRegisteredTasksInConfiguredOrder()
        {
            Build("PreAdapted", sequence: new[] { "Hanoi", "Future", "Unregistered" });
            Engine.Call(hanoi, "Start");
            var future = new ExtraTask("Future");
            orchestrator.Register(future);

            orchestrator.BeginExperiment();
            Assert.IsNull(future.StartedWith, "the second task does not start before Hanoi is done");
            SolveHanoi();

            Assert.AreEqual("Future", orchestrator.CurrentTaskId);
            Assert.AreEqual(ExperimentCondition.PreAdapted, future.StartedWith);

            future.Finish();
            Assert.AreEqual(ExperimentState.Completed, orchestrator.State);
            Assert.AreEqual(new[] { "Unregistered" }, orchestrator.Sequencer.SkippedTaskIds);
        }

        [Test]
        public void Orchestrator_InvalidCondition_DoesNotStartAnyTask_AndLogsRejection()
        {
            Build("condition-B");
            Engine.Call(hanoi, "Start");

            Assert.IsFalse(orchestrator.BeginExperiment());

            Assert.AreEqual(ExperimentState.NotStarted, orchestrator.State);
            Assert.IsFalse(hanoi.IsRunning);
            CollectionAssert.Contains(EventTypes(), "Experiment:experiment_rejected");
            Assert.IsTrue(Debug.Messages.Any(m => m.StartsWith("E:")));
        }

        [Test]
        public void Orchestrator_BeginOnStart_StartsTheExperimentInStart()
        {
            Build("Static", beginOnStart: true);
            Engine.Call(hanoi, "Start");
            Engine.Call(orchestrator, "Start");

            Assert.AreEqual(ExperimentState.Running, orchestrator.State);
            Assert.IsTrue(hanoi.IsRunning);
        }

        [Test]
        public void Orchestrator_Abort_EndsAnUnfinishedHanoiTrialWithTheReason()
        {
            Build("Static");
            Engine.Call(hanoi, "Start");
            orchestrator.BeginExperiment();
            Assert.IsTrue(grabs[0].TryGrab(hand));

            orchestrator.Abort("operator_stop");

            Assert.AreEqual(ExperimentState.Aborted, orchestrator.State);
            Assert.IsFalse(hanoi.IsRunning);
            var lines = File.ReadAllLines(logger.CurrentLogPath);
            Assert.IsTrue(lines.Any(l => l.Contains("\"event\":\"trial_summary\"") && l.Contains("\"end_reason\":\"operator_stop\"")));
            Assert.IsTrue(lines.Last().Contains("\"event\":\"experiment_ended\"") && lines.Last().Contains("\"status\":\"aborted\""));
        }

        [Test]
        public void Orchestrator_RejectsTwoDifferentTasksWithTheSameId()
        {
            Build("Static");
            Assert.Throws<InvalidOperationException>(() => orchestrator.Register(new ExtraTask("Hanoi")));
        }
    }
}
