using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.Tasks.Cubo;

namespace PlayCT.Tests
{
    public class CubeStateTests
    {
        static CubeMove M(string text) => CubeNotation.Parse(text).Single();

        [Test]
        public void Solved_HasNineStickersOfEachFaceColour_AndIsSolved()
        {
            var snapshot = CubeState.Solved.Snapshot();

            Assert.AreEqual(54, snapshot.Length);
            Assert.AreEqual("UUUUUUUUUDDDDDDDDDFFFFFFFFFBBBBBBBBBLLLLLLLLLRRRRRRRRR", snapshot);
            Assert.IsTrue(CubeState.Solved.IsSolved);
        }

        [Test]
        public void ClockwiseRightTurn_MovesTheFrontColumnUp_AndKeepsTheOppositeFace()
        {
            var after = CubeState.Solved.Apply(M("R"));

            Assert.AreEqual("UUFUUFUUF", after.FaceSnapshot(CubeFace.Up), "front stickers go up on a clockwise right turn");
            Assert.IsTrue(after.FaceEquals(CubeState.Solved, CubeFace.Left), "the face opposite the turned layer is untouched");
            Assert.IsTrue(after.FaceEquals(CubeState.Solved, CubeFace.Right), "a solved face looks the same when turned in place");
            Assert.AreEqual(12, Enumerable.Range(0, 54).Count(i => after.ColorAt(i) != CubeState.Solved.ColorAt(i)));
        }

        [Test]
        public void ClockwiseUpTurn_MovesTheRightStripeToTheFront()
        {
            var after = CubeState.Solved.Apply(M("U"));

            Assert.AreEqual("RRR", after.FaceSnapshot(CubeFace.Front).Substring(0, 3));
            Assert.AreEqual("FFFFFFFFF".Substring(0, 3), after.FaceSnapshot(CubeFace.Left).Substring(0, 3), "the front stripe goes to the left");
        }

        [Test]
        public void CounterClockwise_IsTheExactInverseOfClockwise_ForEveryFace()
        {
            var scrambled = CubeState.FromMoves(CubeNotation.Parse("F R' U L D' B"));
            foreach (var move in CubeMove.AllMoves())
                Assert.AreEqual(scrambled, scrambled.Apply(move).Apply(move.Inverse), move.Notation);
        }

        [Test]
        public void FourQuarterTurns_ReturnToTheStart_ButOneTwoOrThreeDoNot()
        {
            foreach (var move in CubeMove.AllMoves())
            {
                var state = CubeState.Solved.Apply(new[] { M("F"), M("U'") });
                var current = state;
                for (var i = 1; i <= 3; i++)
                {
                    current = current.Apply(move);
                    Assert.AreNotEqual(state, current, $"{move.Notation} x{i}");
                }
                Assert.AreEqual(state, current.Apply(move), $"{move.Notation} x4");
            }
        }

        [Test]
        public void Apply_DoesNotChangeTheStateItIsCalledOn()
        {
            var before = CubeState.Solved.Snapshot();
            CubeState.Solved.Apply(M("R"));
            Assert.AreEqual(before, CubeState.Solved.Snapshot());
        }

        [Test]
        public void SameMoves_GiveTheSameState_EveryTime()
        {
            var moves = CubeNotation.Parse("R U F' L D B' R'");
            Assert.AreEqual(CubeState.FromMoves(moves).Snapshot(), CubeState.FromMoves(moves).Snapshot());
        }

        [Test]
        public void CentresNeverMove_AndEveryColourKeepsNineStickers()
        {
            var state = CubeState.FromMoves(CubeNotation.Parse("R U F' L D B' R' U' F D L'"));

            foreach (var face in CubeFaces.All)
                Assert.AreEqual((int)face, state.ColorAt(CubeLayout.IndexOf(face, 1, 1)), $"{face} centre");
            var counts = new int[6];
            for (var i = 0; i < 54; i++) counts[state.ColorAt(i)]++;
            CollectionAssert.AreEqual(new[] { 9, 9, 9, 9, 9, 9 }, counts);
        }

        [Test]
        public void Snapshot_RoundTripsThroughFromSnapshot()
        {
            var state = CubeState.FromMoves(CubeNotation.Parse("F R' U"));
            Assert.AreEqual(state, CubeState.FromSnapshot(state.Snapshot()));
        }

        [Test]
        public void FromSnapshot_RejectsWrongLengthUnknownLettersAndUnevenColours()
        {
            Assert.Throws<FormatException>(() => CubeState.FromSnapshot("UUU"));
            Assert.Throws<FormatException>(() => CubeState.FromSnapshot(new string('X', 54)));
            Assert.Throws<FormatException>(() => CubeState.FromSnapshot(new string('U', 54)));
        }

        [Test]
        public void Cross_IsDetectedOnlyWhenTheCentreAndFourEdgesMatch()
        {
            Assert.IsTrue(CubeState.Solved.IsCrossOn(CubeFace.Up));
            Assert.IsFalse(CubeState.Solved.Apply(M("F")).IsCrossOn(CubeFace.Up));
            Assert.IsTrue(CubeState.Solved.Apply(M("U")).IsCrossOn(CubeFace.Up), "turning the face itself keeps its cross");
        }

        [Test]
        public void Notation_ParsesAndFormatsQuarterTurns_AndRejectsEverythingElse()
        {
            var moves = CubeNotation.Parse("R U' F");
            Assert.AreEqual("R U' F", CubeNotation.Format(moves));
            Assert.AreEqual(CubeTurn.CounterClockwise, moves[1].Turn);
            Assert.Throws<FormatException>(() => CubeNotation.Parse("R2"));
            Assert.Throws<FormatException>(() => CubeNotation.Parse("X"));
            Assert.IsEmpty(CubeNotation.Parse(""));
        }

        [TestCase(CubeFace.Up, 'y', "clockwise")]
        [TestCase(CubeFace.Down, 'y', "clockwise")]
        [TestCase(CubeFace.Left, 'x', "clockwise")]
        [TestCase(CubeFace.Right, 'x', "clockwise")]
        [TestCase(CubeFace.Front, 'z', "clockwise")]
        [TestCase(CubeFace.Back, 'z', "clockwise")]
        public void Move_ReportsTheAxisOfItsFace_AndExactly90Degrees(CubeFace face, char axis, string direction)
        {
            var move = new CubeMove(face, CubeTurn.Clockwise);
            Assert.AreEqual(axis, move.Axis);
            Assert.AreEqual(direction, move.DirectionLabel);
            Assert.AreEqual(90, CubeMove.AmountDegrees);
            Assert.AreEqual("counterclockwise", move.Inverse.DirectionLabel);
        }
    }

    public class TwistGestureTests
    {
        static readonly TwistGestureSettings Settings = TwistGestureSettings.Default;

        [Test]
        public void ShortMovement_IsATap_WhateverTheAngle()
        {
            Assert.AreEqual(GestureKind.Tap, TwistGesture.Classify(0, 0.01, 0.08, Settings));
            Assert.AreEqual(GestureKind.Tap, TwistGesture.Classify(80, 0.02, 0.08, Settings));
        }

        [Test]
        public void ClearSweep_IsATurnInThatDirection()
        {
            Assert.AreEqual(GestureKind.Clockwise, TwistGesture.Classify(40, 0.08, 0.10, Settings));
            Assert.AreEqual(GestureKind.CounterClockwise, TwistGesture.Classify(-40, 0.08, 0.10, Settings));
        }

        [Test]
        public void SmallSweepOrStartAtTheCentre_IsIgnored()
        {
            Assert.AreEqual(GestureKind.Ignored, TwistGesture.Classify(10, 0.08, 0.10, Settings));
            Assert.AreEqual(GestureKind.Ignored, TwistGesture.Classify(90, 0.08, 0.005, Settings));
        }
    }

    public class CuboTrialTests
    {
        FakeClock clock;
        MemorySink sink;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            sink = new MemorySink();
        }

        static CubeMove M(string text) => CubeNotation.Parse(text).Single();

        CuboTrialConfig Config(string mini)
        {
            var spec = CuboProtocol.DefaultSpecs().First(s => s.miniTask == mini);
            return spec.ToConfig();
        }

        CuboTrial Begin(string mini, bool stabilized = true, int index = 1)
        {
            var trial = new CuboTrial(Config(mini), sink, clock, index);
            trial.Begin();
            trial.SetStabilized(stabilized);
            return trial;
        }

        object Last(string type, string key) => MemorySink.Field(sink.OfType(type).Last(), key);

        [Test]
        public void Begin_LogsTheTrialConfigurationAndStartingState()
        {
            var trial = Begin("corregir_una_pieza");

            var e = sink.OfType("trial_started").Single();
            Assert.AreEqual(CuboTrial.TaskName, e.Task);
            Assert.AreEqual("corregir_una_pieza", MemorySink.Field(e, "mini_task"));
            Assert.AreEqual(1, MemorySink.Field(e, "trial_index"));
            Assert.AreEqual("exact_state", MemorySink.Field(e, "goal"));
            Assert.AreEqual(new[] { "U", "U'", "R", "R'", "F", "F'" }, (List<string>)MemorySink.Field(e, "allowed_rotations"));
            Assert.AreEqual(CubeState.FromMoves(new[] { M("F'") }).Snapshot(), MemorySink.Field(e, "resulting_state"));
            Assert.IsTrue(trial.IsRunning);
            Assert.Throws<InvalidOperationException>(() => trial.Begin());
        }

        [Test]
        public void ValidRotation_ChangesTheState_AndLogsEverythingNeededToReconstructIt()
        {
            var trial = Begin("cruz_clara");
            var before = trial.State;

            var outcome = trial.TryRotate(M("R"));

            Assert.IsTrue(outcome.Accepted && outcome.Legal && outcome.StateChanged);
            Assert.AreEqual(before.Apply(M("R")), trial.State);
            var e = sink.OfType("rotation").Single();
            Assert.AreEqual("R", MemorySink.Field(e, "selected_face"));
            Assert.AreEqual("x", MemorySink.Field(e, "rotation_axis"));
            Assert.AreEqual("clockwise", MemorySink.Field(e, "rotation_direction"));
            Assert.AreEqual(90, MemorySink.Field(e, "rotation_amount_deg"));
            Assert.AreEqual(true, MemorySink.Field(e, "legal"));
            Assert.AreEqual(1, MemorySink.Field(e, "move_number"));
            Assert.AreEqual(1, MemorySink.Field(e, "attempt_number"));
            Assert.AreEqual(1, MemorySink.Field(e, "trial_index"));
            Assert.AreEqual("cruz_clara", MemorySink.Field(e, "mini_task"));
            Assert.AreEqual(before.Snapshot(), MemorySink.Field(e, "previous_state"));
            Assert.AreEqual(trial.State.Snapshot(), MemorySink.Field(e, "resulting_state"));
            Assert.AreEqual("in_progress", MemorySink.Field(e, "completion_status"));
            Assert.AreEqual(CubeFace.Right, trial.SelectedFace);
        }

        [Test]
        public void RotationOutsideTheAllowedSet_IsRefused_AndTheStateIsKept()
        {
            var trial = Begin("que_permanece");
            var before = trial.State;

            var outcome = trial.TryRotate(M("U"));

            Assert.IsTrue(outcome.Accepted);
            Assert.IsFalse(outcome.Legal);
            Assert.AreEqual("rotation_not_allowed", outcome.Reason);
            Assert.AreEqual(before, trial.State);
            var e = sink.OfType("rotation").Single();
            Assert.AreEqual(false, MemorySink.Field(e, "legal"));
            Assert.AreEqual("rotation_not_allowed", MemorySink.Field(e, "outcome"));
            Assert.AreEqual(MemorySink.Field(e, "previous_state"), MemorySink.Field(e, "resulting_state"));
            Assert.AreEqual(0, trial.ValidRotations);
            Assert.AreEqual(1, trial.InvalidActions);
            Assert.IsTrue(trial.IsRunning, "the participant can carry on");
        }

        [Test]
        public void RotationWithoutTheStabilizingHand_IsRefused()
        {
            var trial = Begin("cruz_clara", stabilized: false);
            var before = trial.State;

            var outcome = trial.TryRotate(M("R"));

            Assert.AreEqual("not_stabilized", outcome.Reason);
            Assert.AreEqual(before, trial.State);
            Assert.AreEqual(1, trial.InvalidActions);

            trial.SetStabilized(true);
            Assert.IsTrue(trial.TryRotate(M("R")).Legal);
        }

        [Test]
        public void StabilizerChanges_AreLoggedOnlyWhenTheyChange()
        {
            var trial = Begin("cruz_clara", stabilized: false);
            trial.SetStabilized(true);
            trial.SetStabilized(true);
            trial.SetStabilized(false);

            var values = sink.OfType("stabilizer_changed").Select(e => (bool)MemorySink.Field(e, "stabilized")).ToArray();
            Assert.AreEqual(new[] { true, false }, values);
        }

        [Test]
        public void CruzClara_CompletesWhenTheCrossIsFormed_AndSummarisesTheTrial()
        {
            var trial = Begin("cruz_clara");
            Assert.IsFalse(trial.State.IsCrossOn(CubeFace.Up), "the cross starts broken");

            clock.Advance(2);
            trial.TryRotate(M("U"));
            clock.Advance(1);
            trial.TryRotate(M("U'"));
            clock.Advance(3);
            trial.TryRotate(M("R"));
            clock.Advance(4.5);
            var outcome = trial.TryRotate(M("F'"));

            Assert.IsTrue(outcome.Completed);
            Assert.IsTrue(trial.IsCompleted);
            Assert.IsFalse(trial.IsRunning);
            var summary = trial.BuildSummary();
            Assert.AreEqual("completed", summary.Status);
            Assert.AreEqual(10.5, summary.CompletionTimeSeconds.Value, 1e-9);
            Assert.AreEqual(4, summary.ValidRotations);
            Assert.AreEqual(0, summary.InvalidActions);
            Assert.AreEqual(4, summary.AttemptCount);
            Assert.AreEqual(new[] { "U", "U'", "R", "F'" }, summary.RotationSequence);
            Assert.AreEqual(2.0, summary.FirstActionLatencySeconds.Value, 1e-9);
            Assert.AreEqual("cross:U", summary.Goal);
            Assert.AreEqual(1, sink.OfType("trial_completed").Count());
            Assert.AreEqual(1, sink.OfType("trial_summary").Count());
            Assert.AreEqual("completed", Last("trial_completed", "completion_status"));
            Assert.AreEqual("completed", Last("rotation", "completion_status"));
        }

        [Test]
        public void CorregirUnaPieza_IsSolvedByTurningTheDisplacedLayerBack()
        {
            var trial = Begin("corregir_una_pieza");

            Assert.IsFalse(trial.TryRotate(M("F'")).Completed, "turning the same way again does not fix it");
            Assert.IsFalse(trial.TryRotate(M("F")).Completed);
            Assert.IsTrue(trial.TryRotate(M("F")).Completed);
            Assert.IsTrue(trial.State.IsSolved);
        }

        [Test]
        public void ElegirUnaSecuencia_AcceptsAnyAllowedSequenceThatReachesTheTarget()
        {
            var trial = Begin("elegir_una_secuencia");
            foreach (var move in "F' U' R'".Split(' ')) trial.TryRotate(M(move));

            Assert.IsTrue(trial.IsCompleted);
            Assert.IsTrue(trial.State.IsSolved);
            Assert.AreEqual(3, trial.BuildSummary().ReferenceMoves);
        }

        [Test]
        public void InvalidActions_AreCountedSeparately_AndDoNotEndTheTrial()
        {
            var trial = Begin("elegir_una_secuencia");
            trial.TryRotate(M("L"));
            trial.TryRotate(M("D'"));
            trial.SetStabilized(false);
            trial.TryRotate(M("F'"));
            trial.SetStabilized(true);
            trial.TryRotate(M("F'"));

            var summary = trial.BuildSummary();
            Assert.AreEqual(1, summary.ValidRotations);
            Assert.AreEqual(3, summary.InvalidActions);
            Assert.AreEqual(4, summary.AttemptCount);
            Assert.AreEqual(new[] { "L:rejected:rotation_not_allowed", "D':rejected:rotation_not_allowed", "F':rejected:not_stabilized", "F'" }, summary.ActionSequence);
            Assert.IsTrue(trial.IsRunning);
        }

        [Test]
        public void QuePermanece_NeedsARotationFirst_ThenAcceptsOnlyAnUnchangedFace()
        {
            var trial = Begin("que_permanece");

            Assert.AreEqual("nothing_changed", trial.SelectFace(CubeFace.Left).Reason, "nothing has been rotated yet");
            Assert.IsTrue(trial.TryRotate(M("R")).Legal);
            Assert.AreEqual("face_changed", trial.SelectFace(CubeFace.Up).Reason);
            Assert.AreEqual("face_changed", trial.SelectFace(CubeFace.Right).Reason);
            Assert.IsTrue(trial.IsRunning);

            var outcome = trial.SelectFace(CubeFace.Left);

            Assert.IsTrue(outcome.Legal && outcome.Completed);
            var summary = trial.BuildSummary();
            Assert.AreEqual("L", summary.AnswerFace);
            Assert.AreEqual(5, summary.AttemptCount, "one early answer, one rotation, two wrong answers and the right one");
            Assert.AreEqual(3, summary.InvalidActions);
            Assert.AreEqual("unchanged_face", Last("face_answer", "outcome"));
        }

        [TestCase("R")]
        [TestCase("R'")]
        public void QuePermanece_DefaultStart_HasExactlyOneUnchangedFaceAfterEitherAllowedTurn(string move)
        {
            var config = Config("que_permanece");
            var after = config.InitialState.Apply(M(move));

            var unchanged = CubeFaces.All.Where(f => after.FaceEquals(config.InitialState, f)).ToArray();

            CollectionAssert.AreEqual(new[] { CubeFace.Left }, unchanged);
        }

        [Test]
        public void FaceTap_InTheOtherMiniTasks_OnlySelectsTheFace()
        {
            var trial = Begin("cruz_clara");
            var before = trial.State;

            var outcome = trial.SelectFace(CubeFace.Front);

            Assert.IsTrue(outcome.Legal);
            Assert.IsFalse(outcome.StateChanged);
            Assert.AreEqual(before, trial.State);
            Assert.AreEqual(CubeFace.Front, trial.SelectedFace);
            Assert.AreEqual("F", Last("face_selected", "selected_face"));
            Assert.AreEqual(0, trial.BuildSummary().AttemptCount, "a selection alone is not an attempt");
        }

        [Test]
        public void FaceTap_WithoutTheStabilizingHand_IsRecordedAsInvalid()
        {
            var trial = Begin("cruz_clara", stabilized: false);

            var outcome = trial.SelectFace(CubeFace.Front);

            Assert.IsFalse(outcome.Legal);
            Assert.AreEqual(1, trial.InvalidActions);
            Assert.AreEqual(false, Last("face_selected", "legal"));
        }

        [Test]
        public void AfterCompletion_FurtherActionsAreNotAccepted_AndNothingMoreIsLogged()
        {
            var trial = Begin("corregir_una_pieza");
            trial.TryRotate(M("F"));
            var events = sink.Events.Count;

            Assert.IsFalse(trial.TryRotate(M("R")).Accepted);
            Assert.IsFalse(trial.SelectFace(CubeFace.Up).Accepted);
            trial.End("late");

            Assert.AreEqual(events, sink.Events.Count);
            Assert.IsTrue(trial.State.IsSolved);
        }

        [Test]
        public void TimeLimit_EndsAnUnfinishedTrialAsIncomplete()
        {
            var trial = Begin("cruz_clara");
            trial.TryRotate(M("R"));
            clock.Advance(239);
            Assert.IsFalse(trial.CheckTimeLimit());

            clock.Advance(2);

            Assert.IsTrue(trial.CheckTimeLimit());
            Assert.IsFalse(trial.IsRunning);
            Assert.IsFalse(trial.IsCompleted);
            var e = sink.OfType("trial_summary").Single();
            Assert.AreEqual("incomplete", MemorySink.Field(e, "completion_status"));
            Assert.AreEqual("time_limit", MemorySink.Field(e, "end_reason"));
            Assert.IsFalse(trial.TryRotate(M("R")).Accepted);
            Assert.IsFalse(trial.CheckTimeLimit());
        }

        [Test]
        public void ZeroDuration_MeansNoTimeLimit()
        {
            var config = Config("cruz_clara");
            config.DurationSeconds = 0;
            var trial = new CuboTrial(config, sink, clock);
            trial.Begin();

            clock.Advance(100000);

            Assert.IsFalse(trial.CheckTimeLimit());
            Assert.IsTrue(trial.IsRunning);
        }

        [Test]
        public void End_WritesAnIncompleteSummaryOnce()
        {
            var trial = Begin("cruz_clara");
            trial.End("operator_stop");
            trial.End("again");

            var e = sink.OfType("trial_summary").Single();
            Assert.AreEqual("operator_stop", MemorySink.Field(e, "end_reason"));
            Assert.AreEqual("incomplete", trial.BuildSummary().Status);
        }

        [Test]
        public void NewTrial_StartsFromItsConfiguredInitialState_WithCleanCounters()
        {
            var first = Begin("cruz_clara");
            first.TryRotate(M("R"));
            first.TryRotate(M("D"));
            first.End("restart");

            var second = new CuboTrial(Config("cruz_clara"), sink, clock, 2);
            second.Begin();

            Assert.AreEqual(second.InitialState, second.State);
            Assert.AreEqual(0, second.ValidRotations);
            Assert.AreEqual(0, second.InvalidActions);
            Assert.IsEmpty(second.ActionSequence);
            Assert.IsNull(second.SelectedFace);
            Assert.AreEqual(2, second.TrialIndex);
        }

        [Test]
        public void SameActions_ProduceIdenticalEventStreams()
        {
            var a = new MemorySink();
            var b = new MemorySink();
            foreach (var target in new[] { a, b })
            {
                var localClock = new FakeClock();
                var trial = new CuboTrial(Config("elegir_una_secuencia"), target, localClock);
                trial.Begin();
                trial.SetStabilized(true);
                foreach (var move in "L F' U' R'".Split(' '))
                {
                    localClock.Advance(1.5);
                    trial.TryRotate(M(move));
                }
            }

            Assert.AreEqual(a.Events.Count, b.Events.Count);
            for (var i = 0; i < a.Events.Count; i++)
            {
                Assert.AreEqual(a.Events[i].EventType, b.Events[i].EventType);
                CollectionAssert.AreEqual(a.Events[i].Fields.Select(f => f.Key + "=" + Format(f.Value)), b.Events[i].Fields.Select(f => f.Key + "=" + Format(f.Value)));
            }
        }

        static string Format(object value) => value is System.Collections.IEnumerable list && !(value is string)
            ? string.Join(",", list.Cast<object>())
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public class CuboConfigTests
    {
        [Test]
        public void DefaultProtocol_HasTheFourMiniTasksInOrder_AndEveryStartIsNotYetSolved()
        {
            var configs = CuboProtocol.Default();

            CollectionAssert.AreEqual(
                new[] { CuboMiniTask.CruzClara, CuboMiniTask.CorregirUnaPieza, CuboMiniTask.ElegirUnaSecuencia, CuboMiniTask.QuePermanece },
                configs.Select(c => c.MiniTask));
            foreach (var config in configs)
                if (config.Goal.Kind != CuboGoalKind.UnchangedFace) Assert.IsFalse(config.Goal.IsSatisfiedBy(config.InitialState), CuboMiniTasks.Id(config.MiniTask));
        }

        [Test]
        public void DefaultProtocol_NeverAsksForAFullSolve()
        {
            foreach (var spec in CuboProtocol.DefaultSpecs())
                Assert.LessOrEqual(CubeNotation.Parse(spec.initialMoves).Count, 3, spec.miniTask);
        }

        [Test]
        public void Spec_BuildsTheConfiguredTrial()
        {
            var spec = new CuboTrialSpec
            {
                miniTask = "Cruz clara",
                initialMoves = "R",
                goalFace = "D",
                allowedMoves = "R R'",
                durationSeconds = 30,
                referenceMoves = 1,
            };
            var config = spec.ToConfig();

            Assert.AreEqual(CuboMiniTask.CruzClara, config.MiniTask);
            Assert.AreEqual(CuboGoalKind.CrossOnFace, config.Goal.Kind);
            Assert.AreEqual(CubeFace.Down, config.Goal.Face);
            Assert.AreEqual(30, config.DurationSeconds);
            Assert.IsTrue(config.IsAllowed(new CubeMove(CubeFace.Right, CubeTurn.Clockwise)));
            Assert.IsFalse(config.IsAllowed(new CubeMove(CubeFace.Up, CubeTurn.Clockwise)));
        }

        [Test]
        public void Spec_ExplicitStateOverridesMoves()
        {
            var state = CubeState.FromMoves(CubeNotation.Parse("U"));
            var config = new CuboTrialSpec { miniTask = "corregir_una_pieza", initialMoves = "R", initialState = state.Snapshot() }.ToConfig();
            Assert.AreEqual(state, config.InitialState);
        }

        [Test]
        public void Spec_EmptyAllowedMoves_MeansAllTwelve()
        {
            var config = new CuboTrialSpec { miniTask = "cruz_clara", initialMoves = "F" }.ToConfig();
            foreach (var move in CubeMove.AllMoves()) Assert.IsTrue(config.IsAllowed(move));
        }

        [Test]
        public void Spec_RejectsUnknownMiniTaskBadMovesAndAnAlreadyMetGoal()
        {
            Assert.Throws<FormatException>(() => new CuboTrialSpec { miniTask = "rubik" }.ToConfig());
            Assert.Throws<FormatException>(() => new CuboTrialSpec { miniTask = "cruz_clara", initialMoves = "R2" }.ToConfig());
            Assert.Throws<InvalidOperationException>(() => new CuboTrialSpec { miniTask = "cruz_clara", initialMoves = "" }.ToConfig());
            Assert.Throws<InvalidOperationException>(() => new CuboTrialSpec { miniTask = "corregir_una_pieza", initialMoves = "" }.ToConfig());
        }

        [TestCase("cruz_clara", CuboMiniTask.CruzClara)]
        [TestCase("Corregir una pieza", CuboMiniTask.CorregirUnaPieza)]
        [TestCase("ElegirUnaSecuencia", CuboMiniTask.ElegirUnaSecuencia)]
        [TestCase("que-permanece", CuboMiniTask.QuePermanece)]
        public void MiniTaskNames_ParseFromTextAndRoundTrip(string text, CuboMiniTask expected)
        {
            Assert.IsTrue(CuboMiniTasks.TryParse(text, out var task));
            Assert.AreEqual(expected, task);
            Assert.IsTrue(CuboMiniTasks.TryParse(CuboMiniTasks.Id(task), out var again));
            Assert.AreEqual(expected, again);
        }
    }
}
