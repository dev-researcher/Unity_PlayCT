using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.App;
using PlayCT.Research;
using PlayCT.Tests;
using UnityEngine;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Runs the application flow on the real SessionManager, EventLogger, ConditionManager and TaskOrchestrator scripts (fake engine),
    /// with the task wrappers registered in the orchestrator the way ApplicationFlowController does it. Checks that free play leaves no
    /// data, that the experiment writes one session through the existing logger, and that nothing is duplicated.
    /// </summary>
    public class ApplicationHarness
    {
        sealed class Host : IAppHost, IExperimentTaskGate
        {
            public SessionManager Session;
            public EventLogger Logger;
            public TaskOrchestrator Orchestrator;
            public ApplicationFlow Flow;
            public readonly Dictionary<string, ScriptedTask> Tasks = new Dictionary<string, ScriptedTask>();
            public readonly Dictionary<string, GatedExperimentTask> Gated = new Dictionary<string, GatedExperimentTask>();

            public void StartFreePlayGame(string taskId)
            {
                Logger.Recording = false;
                Tasks[taskId].Completed += OnFreePlayCompleted;
                Tasks[taskId].StartTask(ExperimentCondition.Static);
                // A game logs while it runs; with recording off nothing may reach the file system.
                Logger.Log(new ResearchEvent(taskId, "free_play_probe"));
                Logger.WriteJsonFile("free_play_probe.json", "{}");
            }

            void OnFreePlayCompleted(IExperimentTask task) => Flow.NotifyGameCompleted(task.TaskId);

            public void StopFreePlayGame(string taskId, string reason)
            {
                Tasks[taskId].Completed -= OnFreePlayCompleted;
                Tasks[taskId].EndTask(reason);
            }

            public string PrepareSession(string participantId)
            {
                Session.BeginSession(participantId);
                return Session.SessionId;
            }

            public bool StartExperiment()
            {
                Logger.Recording = true;
                if (Orchestrator.BeginExperiment()) return true;
                Logger.Recording = false;
                return false;
            }

            public void StartExperimentTask(string taskId) => Gated[taskId].BeginInner();

            public void AcknowledgeExperimentTask(string taskId) => Gated[taskId].Release();

            public void EndExperiment()
            {
                Orchestrator.Abort("application_reset");
                Logger.Recording = false;
            }

            public void TaskOffered(string taskId) =>
                Flow.OfferExperimentTask(taskId, Orchestrator.Sequencer.CurrentIndex, Orchestrator.Sequencer.Sequence.Count);

            public void InnerStarting(string taskId) { }

            public void InnerCompleted(string taskId) => Flow.NotifyExperimentTaskCompleted(taskId);

            public void TaskClosed(string taskId) { }
        }

        string logRoot;
        Host host;
        SessionManager session;
        EventLogger logger;
        TaskOrchestrator orchestrator;

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_app_" + Guid.NewGuid().ToString("N"));
            session = new GameObject("SessionManager").AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", "P000");
                Engine.Set(m, "condition", "Static");
                Engine.Set(m, "startSessionOnAwake", false);
                Engine.Set(m, "loadConfigFile", false);
            });
            logger = new GameObject("EventLogger").AddComponent<EventLogger>(l => Engine.Set(l, "session", session));
            logger.RootDirectoryOverride = logRoot;
            var conditions = new GameObject("ConditionManager").AddComponent<ConditionManager>(c => Engine.Set(c, "session", session));
            orchestrator = new GameObject("Experiment").AddComponent<TaskOrchestrator>(o =>
            {
                Engine.Set(o, "session", session);
                Engine.Set(o, "eventLogger", logger);
                Engine.Set(o, "conditionManager", conditions);
                Engine.Set(o, "tasks", new MonoBehaviour[0]);
                Engine.Set(o, "beginOnStart", false);
                Engine.Set(o, "taskSequence", GameCatalog.Ids.ToArray());
            });

            logger.Recording = false;
            host = new Host { Session = session, Logger = logger, Orchestrator = orchestrator };
            foreach (var id in GameCatalog.Ids)
            {
                var task = new ScriptedTask(id);
                host.Tasks[id] = task;
                host.Gated[id] = new GatedExperimentTask(task, host);
                orchestrator.Register(host.Gated[id]);
            }
            host.Flow = new ApplicationFlow(host, logger);
            orchestrator.ExperimentFinished += s => host.Flow.NotifyExperimentFinished(s.State == ExperimentState.Completed);
        }

        [TearDown]
        public void TearDown()
        {
            if (logger != null) Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        void Press(string label)
        {
            var button = ScreenCatalog.Build(host.Flow).AllButtons().First(b => b.Label == label);
            Assert.IsTrue(button.Enabled, label);
            button.OnPress();
        }

        static string Extract(string line, string key)
        {
            var marker = "\"" + key + "\":\"";
            var start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            return line.Substring(start, line.IndexOf('"', start) - start);
        }

        [Test]
        public void TheLoggerStartsSilent_AndASessionIsNotCreatedUntilTheParticipantConfirms()
        {
            Assert.IsFalse(session.HasSession, "opening the app must not start a session");
            Assert.IsFalse(Directory.Exists(logRoot));
            Assert.IsFalse(logger.Recording);

            Press("EXPERIMENTO");
            Press("Continuar");
            Assert.IsFalse(session.HasSession);
            foreach (var c in "P01") Press(c.ToString());
            Press("Continuar");

            Assert.IsTrue(session.HasSession);
            Assert.AreEqual("P01", session.ParticipantId);
            Assert.AreEqual(session.SessionId, host.Flow.SessionId);
            Assert.AreEqual("Static", session.Condition, "the participant never changes the condition");
            Assert.IsFalse(Directory.Exists(logRoot), "a session alone writes nothing");
        }

        [Test]
        public void FreePlay_WritesNothingAndCreatesNoSession()
        {
            foreach (var game in GameCatalog.All)
            {
                Press("JUGAR");
                Press(game.Title);
                Press("Comenzar");
                host.Tasks[game.TaskId].Finish();
                Press("Volver al inicio");
            }

            Assert.IsFalse(session.HasSession);
            Assert.AreEqual(0, logger.EventCount);
            Assert.IsNull(logger.CurrentLogPath);
            Assert.IsFalse(Directory.Exists(logRoot), "free play must leave no folder, log or summary");
            Assert.AreEqual(ExperimentState.NotStarted, orchestrator.State);
        }

        [Test]
        public void FreePlayEventsAreNotRaisedToListeners()
        {
            var raised = 0;
            logger.EventLogged += _ => raised++;
            Press("JUGAR");
            Press("Torre de Hanói");
            Press("Comenzar");
            Assert.AreEqual(0, raised);
        }

        [Test]
        public void TheExperimentWritesOneSessionThroughTheExistingLogger()
        {
            Press("EXPERIMENTO");
            Press("Continuar");
            foreach (var c in "P01") Press(c.ToString());
            Press("Continuar");
            Press("Comenzar experimento");

            foreach (var id in GameCatalog.Ids)
            {
                Assert.AreEqual(AppState.ExperimentTaskInstructions, host.Flow.State);
                Assert.IsFalse(host.Tasks[id].IsRunning, "not started before Comenzar tarea");
                Press("Comenzar tarea");
                Assert.IsTrue(host.Tasks[id].IsRunning);
                host.Tasks[id].Finish();
                Assert.AreEqual(AppState.ExperimentTaskCompleted, host.Flow.State);
                Press("Continuar");
            }
            Assert.AreEqual(AppState.ExperimentCompleted, host.Flow.State);
            Press("Finalizar");
            Assert.AreEqual(AppState.Welcome, host.Flow.State);
            Assert.IsFalse(logger.Recording);

            var sessions = Directory.GetDirectories(Path.Combine(logRoot, "PlayCT"));
            Assert.AreEqual(1, sessions.Length, "exactly one session folder");
            var lines = File.ReadAllLines(logger.CurrentLogPath);
            Assert.IsTrue(lines.All(l => Extract(l, "participant_id") == "P01"));
            Assert.IsTrue(lines.All(l => Extract(l, "session_id") == session.SessionId));
            Assert.IsTrue(lines.All(l => Extract(l, "condition") == "Static"));
            Assert.IsFalse(lines.Any(l => l.Contains("free_play_probe")));

            var types = lines.Select(l => Extract(l, "task") + ":" + Extract(l, "event")).ToList();
            Assert.AreEqual(1, types.Count(t => t == "session:session_started"));
            Assert.AreEqual(1, types.Count(t => t == "Experiment:experiment_started"));
            Assert.AreEqual(1, types.Count(t => t == "Experiment:experiment_ended"));
            Assert.AreEqual(4, types.Count(t => t == "Experiment:task_started"));
            Assert.AreEqual(4, types.Count(t => t == "Experiment:task_ended"));
            Assert.AreEqual(4, types.Count(t => t == "Experiment:task_instructions_shown"));
            Assert.AreEqual(4, types.Count(t => t == "Experiment:task_begin_confirmed"));
            Assert.AreEqual(4, types.Count(t => t == "Experiment:task_completion_shown"));
            Assert.AreEqual(4, types.Count(t => t == "Experiment:task_continue_confirmed"));
            Assert.Less(types.IndexOf("session:session_started"), types.IndexOf("Experiment:experiment_started"));

            var order = lines.Where(l => Extract(l, "event") == "task_started").Select(l => Extract(l, "task_id")).ToArray();
            CollectionAssert.AreEqual(new[] { "Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo" }, order);
        }

        [Test]
        public void AfterTheExperimentNothingIsRecordedAnymore()
        {
            Press("EXPERIMENTO");
            Press("Continuar");
            foreach (var c in "P01") Press(c.ToString());
            Press("Continuar");
            Press("Comenzar experimento");
            foreach (var id in GameCatalog.Ids)
            {
                Press("Comenzar tarea");
                host.Tasks[id].Finish();
                Press("Continuar");
            }
            Press("Finalizar");
            var count = logger.EventCount;

            Press("JUGAR");
            Press("Torre de Hanói");
            Press("Comenzar");
            host.Tasks["Hanoi"].Finish();

            Assert.AreEqual(count, logger.EventCount);
            Assert.AreEqual(count, File.ReadAllLines(logger.CurrentLogPath).Length);
        }

        [Test]
        public void ASecondExperimentUsesANewSessionFolder()
        {
            for (var run = 0; run < 2; run++)
            {
                Press("EXPERIMENTO");
                Press("Continuar");
                foreach (var c in ("P0" + (run + 1))) Press(c.ToString());
                Press("Continuar");
                Press("Comenzar experimento");
                foreach (var id in GameCatalog.Ids)
                {
                    Press("Comenzar tarea");
                    host.Tasks[id].Finish();
                    Press("Continuar");
                }
                Press("Finalizar");
            }

            Assert.AreEqual(2, Directory.GetDirectories(Path.Combine(logRoot, "PlayCT")).Length);
        }

        [Test]
        public void AnInvalidConditionStopsTheExperimentBeforeAnyTaskStarts()
        {
            Engine.Set(session, "condition", "condition-B");
            Press("EXPERIMENTO");
            Press("Continuar");
            foreach (var c in "P01") Press(c.ToString());
            Press("Continuar");
            Press("Comenzar experimento");

            Assert.AreEqual(AppState.ExperimentReady, host.Flow.State);
            StringAssert.Contains("No se pudo iniciar", ScreenCatalog.Build(host.Flow).Notice);
            Assert.IsTrue(host.Tasks.Values.All(t => t.Starts == 0));
            Assert.IsFalse(logger.Recording);
        }

        [Test]
        public void TheResearcherConditionFromTheSessionConfigReachesEveryTask()
        {
            Engine.Set(session, "condition", "PreAdapted");
            Press("EXPERIMENTO");
            Press("Continuar");
            foreach (var c in "P09") Press(c.ToString());
            Press("Continuar");
            Press("Comenzar experimento");
            foreach (var id in GameCatalog.Ids)
            {
                Press("Comenzar tarea");
                Assert.AreEqual(ExperimentCondition.PreAdapted, host.Tasks[id].StartedWith);
                host.Tasks[id].Finish();
                Press("Continuar");
            }
        }
    }
}
