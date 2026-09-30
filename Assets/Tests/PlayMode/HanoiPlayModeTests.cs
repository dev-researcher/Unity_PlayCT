using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PlayCT.Tests.PlayMode
{
    /// <summary>
    /// Runs the Laboratory scene and drives the Hanoi task through the same grab/release path the XR
    /// interactables use, then inspects the JSON Lines log written by the shared EventLogger.
    /// </summary>
    public class HanoiPlayModeTests
    {
        const string SceneName = "Laboratory";

        HanoiTask task;
        EventLogger logger;
        SessionManager session;
        string tempRoot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "playct_tests_" + System.Guid.NewGuid().ToString("N"));
            var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            yield return null;

            task = Object.FindFirstObjectByType<HanoiTask>();
            logger = Object.FindFirstObjectByType<EventLogger>();
            session = Object.FindFirstObjectByType<SessionManager>();
            Assert.IsNotNull(task, "HanoiTask missing from the scene");
            Assert.IsNotNull(logger, "EventLogger missing from the scene");
            Assert.IsNotNull(session, "SessionManager missing from the scene");

            logger.RootDirectoryOverride = tempRoot;
            session.BeginSession("P-TEST", "unit-test");
            task.Configure(3, "unit-test");
            task.BeginTrial();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }

        IEnumerator WaitForDisksToSettle()
        {
            var timeout = Time.realtimeSinceStartup + 5f;
            while (task.Disks.Any(d => d.isActiveAndEnabled && d.IsAnimating) && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.IsFalse(task.Disks.Any(d => d.isActiveAndEnabled && d.IsAnimating), "a disk never settled");
        }

        string[] ReadLog() => File.ReadAllLines(logger.CurrentLogPath);

        [UnityTest]
        public IEnumerator ValidMove_MovesTheDiskToTheDestinationPeg()
        {
            Assert.IsTrue(task.SimulateMove(1, HanoiPegs.Destino));
            yield return WaitForDisksToSettle();

            var disk = task.FindDisk(1);
            var expected = task.Pegs.First(p => p.PegName == HanoiPegs.Destino).SlotPosition(0, disk.Thickness);
            Assert.Less(Vector3.Distance(disk.transform.position, expected), 1e-3f);
            Assert.AreEqual(1, task.Trial.TotalMoves);
        }

        [UnityTest]
        public IEnumerator IllegalPlacement_IsRejected_AndTheDiskReturnsToItsSlot()
        {
            task.SimulateMove(1, HanoiPegs.Apoyo);
            yield return WaitForDisksToSettle();

            var disk2 = task.FindDisk(2);
            var home = disk2.transform.position;
            Assert.IsTrue(task.SimulateMove(2, HanoiPegs.Apoyo));
            yield return WaitForDisksToSettle();

            Assert.Less(Vector3.Distance(disk2.transform.position, home), 1e-3f, "disk 2 must be back on Origen");
            Assert.AreEqual(1, task.Trial.TotalMoves);
            Assert.AreEqual(1, task.Trial.InvalidAttempts);
            Assert.AreEqual(HanoiPegs.Origen, task.Trial.State.PegOf(2));
        }

        [UnityTest]
        public IEnumerator ReleasingAwayFromPegs_ReturnsTheDisk()
        {
            var disk = task.FindDisk(1);
            var home = disk.transform.position;
            Assert.IsTrue(task.SimulateGrab(1));
            task.SimulateRelease(1, home + new Vector3(0f, 0.3f, -0.25f));
            yield return WaitForDisksToSettle();

            Assert.Less(Vector3.Distance(disk.transform.position, home), 1e-3f);
            Assert.AreEqual(0, task.Trial.TotalMoves);
            Assert.AreEqual(1, task.Trial.InvalidAttempts);
        }

        [UnityTest]
        public IEnumerator OptimalSolution_3Disks_CompletesAndLogsEveryInteraction() => RunOptimalSolution(3);

        [UnityTest]
        public IEnumerator OptimalSolution_4Disks_CompletesAndLogsEveryInteraction() => RunOptimalSolution(4);

        [UnityTest]
        public IEnumerator OptimalSolution_5Disks_CompletesAndLogsEveryInteraction() => RunOptimalSolution(5);

        IEnumerator RunOptimalSolution(int disks)
        {
            task.Configure(disks, "unit-test");
            task.BeginTrial();
            yield return null;

            var moves = Solve(disks);
            foreach (var (disk, to) in moves)
            {
                Assert.IsTrue(task.SimulateMove(disk, to), $"move D{disk}->{to} refused");
                yield return WaitForDisksToSettle();
            }

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual((1 << disks) - 1, task.Trial.TotalMoves);
            Assert.NotNull(task.LastSummary);
            Assert.AreEqual(1.0, task.LastSummary.Efficiency.Value, 1e-9);

            var lines = ReadLog();
            Assert.AreEqual(moves.Count, lines.Count(l => l.Contains("\"event\":\"disk_release\"")));
            Assert.AreEqual(moves.Count, lines.Count(l => l.Contains("\"event\":\"disk_grab\"")));
            Assert.AreEqual(1, lines.Count(l => l.Contains("\"event\":\"trial_completed\"")));
            Assert.IsTrue(lines.All(l => l.Contains("\"session_id\":\"" + session.SessionId + "\"") && l.Contains("\"participant_id\":\"P-TEST\"") && l.Contains("\"condition\":\"unit-test\"")));
            Assert.AreEqual(1, Directory.GetFiles(logger.CurrentSessionDirectory, "hanoi_trial_*_summary.json").Length);
        }

        [UnityTest]
        public IEnumerator DiskCount_IsConfigurable_AndUnusedDisksAreHidden()
        {
            task.Configure(4, "unit-test");
            task.BeginTrial();
            yield return null;

            Assert.AreEqual(4, task.DiskCount);
            Assert.IsTrue(task.FindDisk(4).gameObject.activeSelf);
            Assert.IsFalse(task.FindDisk(5).gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator BuriedDisks_AreNotGrabbable()
        {
            Assert.IsFalse(task.FindDisk(3).Accessible);
            Assert.IsTrue(task.FindDisk(1).Accessible);
            Assert.IsFalse(task.SimulateGrab(3));
            yield return null;
        }

        static System.Collections.Generic.List<(int disk, string to)> Solve(int n)
        {
            var moves = new System.Collections.Generic.List<(int disk, string to)>();
            Recurse(n, HanoiPegs.Origen, HanoiPegs.Destino, HanoiPegs.Apoyo, moves);
            return moves;
        }

        static void Recurse(int n, string from, string to, string via, System.Collections.Generic.List<(int disk, string to)> moves)
        {
            if (n == 0) return;
            Recurse(n - 1, from, via, to, moves);
            moves.Add((n, to));
            Recurse(n - 1, via, to, from, moves);
        }
    }
}
