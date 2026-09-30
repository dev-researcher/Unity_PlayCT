using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;
using PlayCT.Tests;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PlayCT.OffEngine
{
    /// <summary>
    /// Builds the same object graph as the Laboratory scene (pegs, five disks with a grab interactable, session + logger)
    /// on the fake engine and drives the real HanoiTask/HanoiDisk/EventLogger scripts through hand grabs and releases.
    /// </summary>
    public class HanoiSceneHarness
    {
        const float BaseY = 0.78f, PegZ = 0.70f;
        static readonly (string name, float x)[] PegLayout = { ("Origen", -0.30f), ("Apoyo", 0f), ("Destino", 0.30f) };

        string logRoot;
        GameObject taskObject;
        HanoiTask task;
        HanoiDisk[] disks;
        XRGrabInteractable[] grabs;
        HanoiPeg[] pegs;
        EventLogger logger;
        SessionManager session;
        readonly FakeHand hand = new FakeHand();

        [SetUp]
        public void SetUp()
        {
            Engine.Reset();
            logRoot = Path.Combine(Path.GetTempPath(), "playct_off_" + Guid.NewGuid().ToString("N"));

            var sessionGo = new GameObject("SessionManager");
            session = sessionGo.AddComponent<SessionManager>(m =>
            {
                Engine.Set(m, "participantId", "P-77");
                Engine.Set(m, "condition", "condition-B");
            });

            var loggerGo = new GameObject("EventLogger");
            logger = loggerGo.AddComponent<EventLogger>();
            logger.Session = session;
            logger.RootDirectoryOverride = logRoot;

            pegs = new HanoiPeg[3];
            for (var i = 0; i < 3; i++)
            {
                var go = new GameObject("Peg_" + PegLayout[i].name);
                go.transform.position = new Vector3(PegLayout[i].x, BaseY, PegZ);
                pegs[i] = go.AddComponent<HanoiPeg>();
                pegs[i].Configure(PegLayout[i].name, 0.16f);
            }

            disks = new HanoiDisk[5];
            grabs = new XRGrabInteractable[5];
            for (var size = 1; size <= 5; size++)
            {
                var go = new GameObject("Disk_" + size);
                go.AddComponent<Rigidbody>();
                grabs[size - 1] = go.AddComponent<XRGrabInteractable>();
                var sz = size;
                disks[size - 1] = go.AddComponent<HanoiDisk>(d => Engine.Set(d, "size", sz));
            }

            taskObject = new GameObject("Hanoi_Task");
            task = taskObject.AddComponent<HanoiTask>(t =>
            {
                Engine.Set(t, "pegs", pegs);
                Engine.Set(t, "disks", disks);
                Engine.Set(t, "session", session);
                Engine.Set(t, "eventLogger", logger);
                Engine.Set(t, "diskCount", 3);
                Engine.Set(t, "useSessionDiskCount", false);
            });
            Engine.Call(task, "Start");
            Engine.Tick();
        }

        [TearDown]
        public void TearDown()
        {
            Engine.Call(logger, "OnDestroy");
            if (Directory.Exists(logRoot)) Directory.Delete(logRoot, true);
        }

        XRGrabInteractable Grab(int size) => grabs[size - 1];
        Vector3 SlotOf(string peg, int slot) => pegs.First(p => p.PegName == peg).SlotPosition(slot, 0.022f);
        Vector3 Above(string peg) => pegs.First(p => p.PegName == peg).TopPoint + Vector3.up * 0.05f;

        void HandMove(int size, string peg)
        {
            Assert.IsTrue(Grab(size).TryGrab(hand), $"hand could not grab D{size}");
            Grab(size).ReleaseAt(Above(peg));
            Engine.TickSeconds(1.0);
        }

        string[] Lines() => File.ReadAllLines(logger.CurrentLogPath);

        [Test]
        public void TrialStarts_WithDisksStackedOnOrigen_AndOnlyTopDiskGrabbable()
        {
            Assert.IsTrue(task.IsRunning);
            Assert.AreEqual(3, task.DiskCount);
            Assert.IsTrue(disks[0].gameObject.activeSelf && disks[2].gameObject.activeSelf);
            Assert.IsFalse(disks[3].gameObject.activeSelf, "disk 4 is hidden in a 3-disk trial");
            Assert.IsFalse(disks[4].gameObject.activeSelf);
            Assert.AreEqual(SlotOf("Origen", 2).y, disks[0].transform.position.y, 1e-4);
            Assert.AreEqual(SlotOf("Origen", 0).y, disks[2].transform.position.y, 1e-4);

            Assert.IsFalse(Grab(3).TryGrab(hand), "a buried disk must not be grabbable");
            Assert.IsFalse(Grab(2).TryGrab(hand));
            Assert.IsTrue(Grab(1).TryGrab(hand));
        }

        [Test]
        public void ValidMove_ThroughXrGrabEvents_SnapsDiskOntoThePeg()
        {
            HandMove(1, "Destino");

            Assert.AreEqual(1, task.Trial.TotalMoves);
            Assert.AreEqual(SlotOf("Destino", 0).x, disks[0].transform.position.x, 1e-4);
            Assert.AreEqual(SlotOf("Destino", 0).y, disks[0].transform.position.y, 1e-4);
            Assert.AreEqual(SlotOf("Destino", 0).z, disks[0].transform.position.z, 1e-4);
            Assert.IsTrue(disks[1].Accessible, "disk 2 becomes reachable once disk 1 has left");
        }

        [Test]
        public void IllegalPlacement_IsRejected_DiskTravelsBackToItsOriginalSlot()
        {
            HandMove(1, "Apoyo");
            var home = disks[1].transform.position;

            Assert.IsTrue(Grab(2).TryGrab(hand));
            Grab(2).ReleaseAt(Above("Apoyo"));
            Assert.IsTrue(disks[1].IsAnimating, "the disk is being returned, not left on the peg");
            Assert.IsFalse(Grab(2).TryGrab(hand), "cannot be re-grabbed mid-return");
            Engine.TickSeconds(1.0);

            Assert.IsFalse(disks[1].IsAnimating);
            Assert.AreEqual(0f, Vector3.Distance(home, disks[1].transform.position), 1e-4);
            Assert.AreEqual(1, task.Trial.TotalMoves);
            Assert.AreEqual(1, task.Trial.InvalidAttempts);
            Assert.AreEqual("Origen:[3,2];Apoyo:[1];Destino:[]", task.Trial.State.Snapshot());
            Assert.IsTrue(Grab(2).TryGrab(hand), "grabbable again once it has come home");
        }

        [Test]
        public void ReturnPath_LiftsAboveTheTopOfThePegBeforeTravellingSideways()
        {
            HandMove(1, "Apoyo");
            Assert.IsTrue(Grab(2).TryGrab(hand));
            Grab(2).ReleaseAt(Above("Apoyo"));

            var clearY = pegs[0].TopPoint.y + 0.022f;
            var start = disks[1].transform.position;
            var minY = float.MaxValue;
            for (var i = 0; i < 80; i++)
            {
                Engine.Tick();
                var p = disks[1].transform.position;
                if (Math.Abs(p.x - SlotOf("Origen", 1).x) > 0.02f && Math.Abs(p.x - start.x) > 0.02f) minY = Math.Min(minY, p.y);
            }
            Assert.Less(minY, float.MaxValue, "the disk was sampled while crossing between pegs");
            Assert.GreaterOrEqual(minY, clearY - 1e-3f, "disk cleared the peg tip while crossing between pegs");
        }

        [Test]
        public void ReleaseAwayFromPegs_ReturnsDiskAndCountsInvalidAttempt()
        {
            var home = disks[0].transform.position;
            Assert.IsTrue(Grab(1).TryGrab(hand));
            Grab(1).ReleaseAt(new Vector3(0f, 0.9f, 0.3f));
            Engine.TickSeconds(1.0);

            Assert.AreEqual(0f, Vector3.Distance(home, disks[0].transform.position), 1e-4);
            Assert.AreEqual(0, task.Trial.TotalMoves);
            Assert.AreEqual(1, task.Trial.InvalidAttempts);
        }

        [Test]
        public void OnlyOneDiskCanBeHeldAtATime()
        {
            HandMove(1, "Apoyo");

            Assert.IsTrue(Grab(2).TryGrab(hand));
            Assert.IsFalse(Grab(1).TryGrab(new FakeHand()), "the second hand cannot take another disk while one is held");
            Grab(2).ReleaseAt(Above("Origen"));
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void FullSolution_ViaHandEvents_CompletesAndLogsEverything(int count)
        {
            task.Configure(count, "condition-B");
            task.BeginTrial();
            Engine.Tick();
            Assert.AreEqual(count, task.DiskCount);
            for (var s = 1; s <= 5; s++) Assert.AreEqual(s <= count, disks[s - 1].gameObject.activeSelf);

            foreach (var (disk, _, to) in HanoiSolver.Solve(count))
                HandMove(disk, to);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual((1 << count) - 1, task.Trial.TotalMoves);
            Assert.IsNotNull(task.LastSummary);
            Assert.AreEqual(1.0, task.LastSummary.Efficiency.Value, 1e-9);
            Assert.AreEqual(0, task.LastSummary.InvalidAttempts);

            foreach (var d in disks.Take(count)) Assert.IsFalse(d.Accessible, "everything is locked after completion");
            Assert.IsFalse(Grab(1).TryGrab(hand));

            var lines = Lines();
            var moves = (1 << count) - 1;
            Assert.AreEqual(moves, lines.Count(l => l.Contains("\"event\":\"disk_release\"")));
            Assert.AreEqual(moves, lines.Count(l => l.Contains("\"event\":\"disk_grab\"")));
            Assert.AreEqual(1, lines.Count(l => l.Contains("\"event\":\"trial_completed\"")));
            Assert.IsTrue(lines.All(l => l.StartsWith("{\"session_id\":\"" + session.SessionId + "\",\"participant_id\":\"P-77\",\"condition\":\"condition-B\",\"task\":")));
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "hanoi_trial_*_summary.json").Length);
        }

        [Test]
        public void Session_FirstLine_IsSessionStarted_AndSharedLoggerServesOtherTasks()
        {
            HandMove(1, "Destino");
            var lines = Lines();
            StringAssert.Contains("\"task\":\"session\"", lines[0]);
            StringAssert.Contains("\"event\":\"session_started\"", lines[0]);

            logger.Log(new ResearchEvent("OtherTask", "custom_event").Add("value", 42));
            var last = Lines().Last();
            StringAssert.Contains("\"task\":\"OtherTask\"", last);
            StringAssert.Contains("\"value\":42", last);
            StringAssert.Contains("\"participant_id\":\"P-77\"", last);
        }

        [Test]
        public void NewSession_RotatesTheLogFile_WithNewIdentity()
        {
            HandMove(1, "Destino");
            var firstPath = logger.CurrentLogPath;
            session.BeginSession("P-88", "condition-C");
            task.BeginTrial();

            Assert.AreNotEqual(firstPath, logger.CurrentLogPath);
            StringAssert.Contains("\"participant_id\":\"P-88\"", Lines().Last());
            StringAssert.Contains("\"condition\":\"condition-C\"", Lines().Last());
        }

        [Test]
        public void Restarting_EndsTheRunningTrial_AndResetsTheBoard()
        {
            HandMove(1, "Destino");
            task.BeginTrial();
            Engine.Tick();

            Assert.AreEqual(0, task.Trial.TotalMoves);
            Assert.AreEqual(2, task.Trial.TrialIndex);
            Assert.AreEqual("Origen:[3,2,1];Apoyo:[];Destino:[]", task.Trial.State.Snapshot());
            Assert.AreEqual(SlotOf("Origen", 2).y, disks[0].transform.position.y, 1e-4);
            var summaries = Lines().Where(l => l.Contains("\"event\":\"trial_summary\"")).ToList();
            Assert.AreEqual(1, summaries.Count);
            StringAssert.Contains("\"end_reason\":\"restarted\"", summaries[0]);
            StringAssert.Contains("\"completion_status\":\"incomplete\"", summaries[0]);
        }

        [Test]
        public void NoErrorsWereLoggedToTheUnityConsole()
        {
            HandMove(1, "Destino");
            Assert.IsFalse(Debug.Messages.Any(m => m.StartsWith("E:") || m.StartsWith("W:")), string.Join("\n", Debug.Messages));
        }
    }
}
