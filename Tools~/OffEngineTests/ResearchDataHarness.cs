using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;
using PlayCT.Tests;
using UnityEngine;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Runs the guide and the CSV export through the real SessionManager, EventLogger, ConditionManager, TaskOrchestrator and
    /// HanoiTask scripts on the fake engine, and checks what ends up in the session folder.
    /// </summary>
    public class ResearchDataHarness
    {
        sealed class CardPresenter : MonoBehaviour, IGuidePresenter
        {
            public readonly System.Collections.Generic.List<string> Shown = new System.Collections.Generic.List<string>();

            public void Present(GuideMessage message, string taskId, int? trial) => Shown.Add(message.Id);
        }

        string logRoot;
        SessionManager session;
        EventLogger logger;
        TaskOrchestrator orchestrator;
        HanoiTask hanoi;
        HanoiPeg[] pegs;
        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable[] grabs;
        readonly UnityEngine.XR.Interaction.Toolkit.Interactors.FakeHand hand = new UnityEngine.XR.Interaction.Toolkit.Interactors.FakeHand();
        GuideDirector guide;
        CardPresenter presenter;
        SessionDataExporter exporter;

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_data_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (logger != null) Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        void Build(string participant = "P-01", bool withGuide = true, bool withExporter = true)
        {
            session = new GameObject("SessionManager").AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", participant);
                Engine.Set(m, "condition", "PreAdapted");
            });
            logger = new GameObject("EventLogger").AddComponent<EventLogger>();
            logger.Session = session;
            logger.RootDirectoryOverride = logRoot;
            var conditions = new GameObject("ConditionManager").AddComponent<ConditionManager>(c => Engine.Set(c, "session", session));

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
                Engine.Set(o, "beginOnStart", false);
            });

            if (withGuide)
            {
                presenter = new GameObject("GuideCard").AddComponent<CardPresenter>();
                guide = new GameObject("Guide").AddComponent<GuideDirector>(g =>
                {
                    Engine.Set(g, "eventLogger", logger);
                    Engine.Set(g, "presenter", presenter);
                });
            }
            if (withExporter)
            {
                exporter = new GameObject("Exporter").AddComponent<SessionDataExporter>(x =>
                {
                    Engine.Set(x, "orchestrator", orchestrator);
                    Engine.Set(x, "eventLogger", logger);
                });
            }
            Engine.Call(hanoi, "Start");
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

        [Test]
        public void Guide_FollowsTheOrchestratorAndHanoi_AndLogsToTheSameEventsJsonl()
        {
            Build();
            orchestrator.BeginExperiment();
            Engine.TickSeconds(30);
            Assert.AreEqual(HelpResult.Shown, guide.RequestHelp());
            SolveHanoi();

            var moments = guide.Engine.ShownCount;
            Assert.AreEqual(5, moments, "task start, trial start, help, trial end, task end");
            Assert.AreEqual(5, presenter.Shown.Count);

            var lines = File.ReadAllLines(logger.CurrentLogPath);
            var shown = lines.Where(l => l.Contains("\"event\":\"guide_shown\"")).ToArray();
            Assert.AreEqual(5, shown.Length);
            Assert.IsTrue(shown.All(l => l.Contains("\"session_id\":\"" + session.SessionId + "\"") && l.Contains("\"participant_id\":\"P-01\"") &&
                l.Contains("\"condition\":\"PreAdapted\"") && l.Contains("\"task\":\"Hanoi\"") && l.Contains("\"timestamp_utc\":") &&
                l.Contains("\"guide_type\":") && l.Contains("\"message_id\":")));
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "events.jsonl").Length);
        }

        [Test]
        public void Guide_HelpBeforeAnyTrial_IsNotAnswered()
        {
            Build();
            Assert.AreEqual(HelpResult.NoActiveTrial, guide.RequestHelp());
            Assert.AreEqual(0, presenter.Shown.Count);
        }

        [Test]
        public void Guide_DoesNotChangeWhatHanoiLogs()
        {
            Build(withGuide: false, withExporter: false);
            orchestrator.BeginExperiment();
            SolveHanoi();
            var without = File.ReadAllLines(logger.CurrentLogPath).Select(l => Extract(l, "event")).ToArray();

            TearDown();
            SetUp();
            Build(withGuide: true, withExporter: false);
            orchestrator.BeginExperiment();
            SolveHanoi();
            var with = File.ReadAllLines(logger.CurrentLogPath).Select(l => Extract(l, "event")).Where(e => !e.StartsWith("guide_")).ToArray();

            CollectionAssert.AreEqual(without, with);
        }

        [Test]
        public void Export_WritesSessionSummaryCsvIntoTheSessionFolder_WhenTheExperimentFinishes()
        {
            Build();
            orchestrator.BeginExperiment();
            SolveHanoi();

            var path = Path.Combine(logger.CurrentSessionDirectory, "session_summary.csv");
            Assert.IsTrue(File.Exists(path), path);
            Assert.AreEqual(path, exporter.LastSummaryPath);
            Assert.IsTrue(File.Exists(Path.Combine(logger.CurrentSessionDirectory, "events.csv")));

            var bytes = File.ReadAllBytes(path);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 without BOM");
            var rows = File.ReadAllText(path, new UTF8Encoding(false)).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, rows.Length, "header and the one Hanoi trial");
            StringAssert.StartsWith("session_id,participant_id,condition,task_id,trial,", rows[0]);
            StringAssert.Contains(",P-01,PreAdapted,Hanoi,1,", rows[1]);
        }

        [Test]
        public void Export_LeavesEventsJsonlAsItWas()
        {
            Build();
            orchestrator.BeginExperiment();
            SolveHanoi();
            var before = File.ReadAllBytes(logger.CurrentLogPath);

            Assert.IsTrue(exporter.Export());

            CollectionAssert.AreEqual(before, File.ReadAllBytes(logger.CurrentLogPath));
        }

        [Test]
        public void Export_IsDeterministic_ForTheSameEvents()
        {
            Build();
            orchestrator.BeginExperiment();
            SolveHanoi();

            var first = File.ReadAllBytes(exporter.LastSummaryPath);
            exporter.Export();
            CollectionAssert.AreEqual(first, File.ReadAllBytes(exporter.LastSummaryPath));
        }

        [Test]
        public void Export_WithoutAnEventLog_ReturnsFalse()
        {
            Build();
            Assert.IsFalse(exporter.Export());
        }

        static string Extract(string line, string key)
        {
            var marker = "\"" + key + "\":\"";
            var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            return line.Substring(start, line.IndexOf('"', start) - start);
        }
    }
}
