using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    public class HanoiTrialTests
    {
        FakeClock clock;
        MemorySink sink;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            sink = new MemorySink();
        }

        HanoiTrial NewTrial(int disks = 3)
        {
            var trial = new HanoiTrial(disks, sink, clock);
            trial.Begin();
            return trial;
        }

        [Test]
        public void ValidMove_UpdatesStateAndCountsOneMove()
        {
            var trial = NewTrial();

            Assert.IsTrue(trial.OnGrab(1));
            var outcome = trial.OnRelease(1, HanoiPegs.Destino);

            Assert.AreEqual(ReleaseResult.MovedToNewPeg, outcome.Result);
            Assert.IsTrue(outcome.Legal);
            Assert.IsTrue(outcome.StateChanged);
            Assert.AreEqual(1, outcome.MoveNumber);
            Assert.AreEqual(HanoiPegs.Origen, outcome.SourcePeg);
            Assert.AreEqual(HanoiPegs.Destino, outcome.DestinationPeg);
            Assert.AreEqual(1, trial.TotalMoves);
            Assert.AreEqual(0, trial.InvalidAttempts);
            Assert.AreEqual("Origen:[3,2];Apoyo:[];Destino:[1]", trial.State.Snapshot());
        }

        [Test]
        public void IllegalPlacement_LargerOnSmaller_IsRejectedAndCounted()
        {
            var trial = NewTrial();
            trial.OnGrab(1);
            trial.OnRelease(1, HanoiPegs.Apoyo);

            Assert.IsTrue(trial.OnGrab(2));
            var outcome = trial.OnRelease(2, HanoiPegs.Apoyo);

            Assert.AreEqual(ReleaseResult.IllegalLargerOnSmaller, outcome.Result);
            Assert.IsFalse(outcome.Legal);
            Assert.IsFalse(outcome.StateChanged);
            Assert.AreEqual(1, trial.TotalMoves, "an illegal placement is not a move");
            Assert.AreEqual(1, trial.InvalidAttempts);
            Assert.AreEqual("Origen:[3,2];Apoyo:[1];Destino:[]", trial.State.Snapshot());

            var release = sink.OfType("disk_release").Last();
            Assert.AreEqual(false, MemorySink.Field(release, "legal"));
            Assert.AreEqual("illegal_larger_on_smaller", MemorySink.Field(release, "outcome"));
            Assert.AreEqual("D2", MemorySink.Field(release, "disk_id"));
            Assert.AreEqual(HanoiPegs.Origen, MemorySink.Field(release, "source_peg"));
            Assert.AreEqual(HanoiPegs.Apoyo, MemorySink.Field(release, "destination_peg"));
        }

        [Test]
        public void AfterIllegalPlacement_DiskStaysOnSourceAndCanBeMovedLegally()
        {
            var trial = NewTrial();
            trial.OnGrab(1);
            trial.OnRelease(1, HanoiPegs.Apoyo);
            trial.OnGrab(2);
            trial.OnRelease(2, HanoiPegs.Apoyo);

            Assert.AreEqual(HanoiPegs.Origen, trial.State.PegOf(2));
            Assert.AreEqual(1, trial.State.SlotIndexOf(2), "disk 2 is back in its slot above disk 3");
            Assert.AreEqual(0, trial.HeldDisk, "nothing is held after the rejection");

            Assert.IsTrue(trial.OnGrab(2));
            var outcome = trial.OnRelease(2, HanoiPegs.Destino);
            Assert.AreEqual(ReleaseResult.MovedToNewPeg, outcome.Result);
            Assert.AreEqual(2, trial.TotalMoves);
            Assert.AreEqual(1, trial.InvalidAttempts);
        }

        [Test]
        public void ReleaseAwayFromAnyPeg_ReturnsDiskAndCountsInvalidAttempt()
        {
            var trial = NewTrial();
            trial.OnGrab(1);
            var outcome = trial.OnRelease(1, null);

            Assert.AreEqual(ReleaseResult.NoPegTarget, outcome.Result);
            Assert.IsFalse(outcome.Legal);
            Assert.AreEqual(0, trial.TotalMoves);
            Assert.AreEqual(1, trial.InvalidAttempts);
            Assert.AreEqual("Origen:[3,2,1];Apoyo:[];Destino:[]", trial.State.Snapshot());
        }

        [Test]
        public void ReleaseOnSamePeg_IsNotAMoveAndNotInvalid()
        {
            var trial = NewTrial();
            trial.OnGrab(1);
            var outcome = trial.OnRelease(1, HanoiPegs.Origen);

            Assert.AreEqual(ReleaseResult.ReturnedToSamePeg, outcome.Result);
            Assert.IsTrue(outcome.Legal);
            Assert.AreEqual(0, trial.TotalMoves);
            Assert.AreEqual(0, trial.InvalidAttempts);
        }

        [Test]
        public void BuriedDisk_CannotBePickedUp()
        {
            var trial = NewTrial();

            Assert.IsFalse(trial.OnGrab(3));
            Assert.AreEqual(1, trial.InvalidAttempts);
            Assert.AreEqual("not_accessible", MemorySink.Field(sink.OfType("disk_grab").Single(), "outcome"));
        }

        [TestCase(3, 7)]
        [TestCase(4, 15)]
        [TestCase(5, 31)]
        public void OptimalSolution_CompletesWithExpectedMovesAndFullEfficiency(int disks, int optimal)
        {
            var trial = NewTrial(disks);
            Assert.AreEqual(optimal, trial.OptimalMoves);

            clock.Advance(4.0);
            HanoiSolver.Play(trial, disks);

            Assert.IsTrue(trial.IsCompleted);
            Assert.AreEqual(optimal, trial.TotalMoves);
            var summary = trial.BuildSummary();
            Assert.IsTrue(summary.Completed);
            Assert.AreEqual("completed", summary.Status);
            Assert.AreEqual(1.0, summary.Efficiency.Value, 1e-9);
            Assert.AreEqual(0, summary.ExcessMoves.Value);
            Assert.AreEqual(optimal, summary.MoveSequence.Count);
            Assert.AreEqual(1, sink.OfType("trial_completed").Count(), "completion is reported exactly once");
            StringAssert.StartsWith("Origen:[];Apoyo:[];Destino:[", summary.FinalState);
        }

        [Test]
        public void Completion_IsDetectedOnlyWhenAllDisksAreOnDestino()
        {
            var trial = NewTrial(3);
            var solution = HanoiSolver.Solve(3);
            for (var i = 0; i < solution.Count - 1; i++)
            {
                trial.OnGrab(solution[i].disk);
                trial.OnRelease(solution[i].disk, solution[i].to);
                Assert.IsFalse(trial.IsCompleted, $"not complete after move {i + 1}");
            }

            var last = solution[solution.Count - 1];
            trial.OnGrab(last.disk);
            var outcome = trial.OnRelease(last.disk, last.to);

            Assert.IsTrue(outcome.Completed);
            Assert.IsTrue(trial.IsCompleted);
            Assert.AreEqual("Origen:[];Apoyo:[];Destino:[3,2,1]", trial.State.Snapshot());
        }

        [Test]
        public void AfterCompletion_FurtherInteractionIsIgnored()
        {
            var trial = NewTrial(3);
            HanoiSolver.Play(trial, 3);
            var events = sink.Events.Count;

            Assert.IsFalse(trial.OnGrab(1));
            Assert.AreEqual(events, sink.Events.Count);
        }

        [Test]
        public void CompletionTimeAndMoveCount_AreMeasuredFromTrialStart()
        {
            var trial = NewTrial(3);
            clock.Advance(2.5);
            var solution = HanoiSolver.Solve(3);
            foreach (var m in solution)
            {
                clock.Advance(1.0);
                trial.OnGrab(m.disk);
                clock.Advance(0.5);
                trial.OnRelease(m.disk, m.to);
            }

            var summary = trial.BuildSummary();
            Assert.AreEqual(2.5 + 7 * 1.5, summary.CompletionTimeSeconds.Value, 1e-6);
            Assert.AreEqual(3.5, summary.FirstGrabLatencySeconds.Value, 1e-6);
            Assert.AreEqual(7 * 1.5 - 1.0, summary.TimeFromFirstGrabSeconds.Value, 1e-6);
            Assert.AreEqual(7, summary.TotalMoves);

            var completed = sink.OfType("trial_completed").Single();
            Assert.AreEqual(2.5 + 7 * 1.5, (double)MemorySink.Field(completed, "completion_time_s"), 1e-6);
            Assert.AreEqual(7, MemorySink.Field(completed, "total_moves"));
        }

        [Test]
        public void SuboptimalSolution_HasEfficiencyBelowOne_AndCountsInvalidAttempts()
        {
            var trial = NewTrial(3);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Apoyo);
            trial.OnGrab(2); trial.OnRelease(2, HanoiPegs.Apoyo);         // illegal
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Origen);        // same peg (not a move)
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Destino);
            trial.OnGrab(2); trial.OnRelease(2, HanoiPegs.Apoyo);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Apoyo);
            trial.OnGrab(3); trial.OnRelease(3, HanoiPegs.Destino);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Origen);
            trial.OnGrab(2); trial.OnRelease(2, HanoiPegs.Destino);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Destino);

            var summary = trial.BuildSummary();
            Assert.IsTrue(summary.Completed);
            Assert.AreEqual(9, summary.TotalMoves);
            Assert.AreEqual(1, summary.InvalidAttempts);
            Assert.AreEqual(7.0 / 9.0, summary.Efficiency.Value, 1e-3);
            Assert.AreEqual(2, summary.ExcessMoves.Value);
        }

        [Test]
        public void ActionSequence_RecordsEveryAttemptInOrder()
        {
            var trial = NewTrial(3);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Apoyo);
            trial.OnGrab(2); trial.OnRelease(2, HanoiPegs.Apoyo);
            trial.OnGrab(2); trial.OnRelease(2, null);

            var summary = trial.BuildSummary();
            CollectionAssert.AreEqual(new[] { "D1:Origen->Apoyo" }, summary.MoveSequence);
            CollectionAssert.AreEqual(new[]
            {
                "D1:Origen->Apoyo:legal",
                "D2:Origen->Apoyo:illegal_larger_on_smaller",
                "D2:Origen->none:off_peg",
            }, summary.AttemptSequence);
        }

        [Test]
        public void EveryGrabAndReleaseIsLogged_WithAllRequiredFields()
        {
            var trial = NewTrial(3);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Destino);
            trial.OnGrab(2); trial.OnRelease(2, HanoiPegs.Destino);   // illegal
            trial.OnGrab(2); trial.OnRelease(2, HanoiPegs.Apoyo);

            Assert.AreEqual(1, sink.OfType("trial_started").Count());
            Assert.AreEqual(3, sink.OfType("disk_grab").Count());
            Assert.AreEqual(3, sink.OfType("disk_release").Count());

            foreach (var e in sink.OfType("disk_grab").Concat(sink.OfType("disk_release")))
            {
                Assert.AreEqual("Hanoi", e.Task);
                foreach (var key in new[] { "disk_id", "source_peg", "destination_peg", "legal", "move_number", "resulting_state", "completion_status" })
                    Assert.DoesNotThrow(() => MemorySink.Field(e, key), $"{e.EventType} lacks '{key}'");
            }

            var releases = sink.OfType("disk_release").ToList();
            Assert.AreEqual(1, MemorySink.Field(releases[0], "move_number"));
            Assert.AreEqual(1, MemorySink.Field(releases[1], "move_number"), "an illegal attempt keeps the current move number");
            Assert.AreEqual(2, MemorySink.Field(releases[2], "move_number"));
            Assert.AreEqual("Origen:[3];Apoyo:[2];Destino:[1]", MemorySink.Field(releases[2], "resulting_state"));
            Assert.AreEqual("in_progress", MemorySink.Field(releases[2], "completion_status"));
        }

        [Test]
        public void EndingIncompleteTrial_WritesSummaryWithIncompleteStatus()
        {
            var trial = NewTrial(4);
            trial.OnGrab(1); trial.OnRelease(1, HanoiPegs.Apoyo);
            clock.Advance(30);
            trial.End("test_end");

            var summary = sink.OfType("trial_summary").Single();
            Assert.AreEqual("incomplete", MemorySink.Field(summary, "completion_status"));
            Assert.AreEqual(1, MemorySink.Field(summary, "total_moves"));
            Assert.AreEqual(15, MemorySink.Field(summary, "optimal_moves"));
            Assert.AreEqual("test_end", MemorySink.Field(summary, "end_reason"));
            Assert.IsNull(MemorySink.Field(summary, "completion_time_s"));
        }

        [TestCase(2)]
        [TestCase(6)]
        public void UnsupportedDiskCounts_AreRejected(int disks)
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HanoiState(disks));
        }

        [Test]
        public void Release_WithoutGrab_IsNotAccepted()
        {
            var trial = NewTrial();
            var outcome = trial.OnRelease(1, HanoiPegs.Destino);

            Assert.AreEqual(ReleaseResult.NotAccepted, outcome.Result);
            Assert.AreEqual(0, trial.TotalMoves);
        }
    }
}
