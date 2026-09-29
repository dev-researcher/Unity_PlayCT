using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Gabinete;

namespace PlayCT.Tests
{
    public class GabineteShapesAndConfigTests
    {
        static readonly GabineteShapeType[] RequiredSix =
        {
            GabineteShapeType.Prism, GabineteShapeType.Cylinder, GabineteShapeType.TruncatedCone,
            GabineteShapeType.Wedge, GabineteShapeType.LBlock, GabineteShapeType.Hexagon,
        };

        [Test]
        public void EveryRequiredShape_HasAFootprintThatFitsItsOwnOpeningCell()
        {
            foreach (var type in RequiredSix.Concat(new[] { GabineteShapeType.HalfCylinder, GabineteShapeType.TBlock }))
            {
                var shape = GabineteShapes.Get(type);
                Assert.Greater(Polygon2D.SignedArea(shape.Footprint), 0, type + " footprint is counter-clockwise");
                var c = Polygon2D.Centroid(shape.Footprint);
                Assert.AreEqual(0, c.X, 1e-4, type + " centred on x");
                Assert.AreEqual(0, c.Y, 1e-4, type + " centred on z");

                var (top, _) = GabineteShapes.OpeningScales(shape, GabineteLayout.PocketDepth);
                foreach (var p in shape.Footprint)
                {
                    Assert.Less(Math.Abs(p.X * top), GabineteLayout.Spacing * 0.5f - 0.002f, type + " opening stays inside its cell (x)");
                    Assert.Less(Math.Abs(p.Y * top), GabineteLayout.BoardDepth * 0.5f - 0.02f, type + " opening stays inside the board (z)");
                }
            }
        }

        [Test]
        public void ShapeIds_AreUniqueAndParseable()
        {
            var ids = Enum.GetValues(typeof(GabineteShapeType)).Cast<GabineteShapeType>().Select(GabineteShapes.Id).ToList();

            CollectionAssert.AllItemsAreUnique(ids);
            foreach (GabineteShapeType type in Enum.GetValues(typeof(GabineteShapeType)))
            {
                Assert.IsTrue(GabineteShapes.TryParse(GabineteShapes.Id(type), out var parsed));
                Assert.AreEqual(type, parsed);
            }
            Assert.IsFalse(GabineteShapes.TryParse("sphere", out _));
        }

        [Test]
        public void OrientationError_FoldsBySymmetry()
        {
            var prism = GabineteShapes.Get(GabineteShapeType.Prism);
            var hexagon = GabineteShapes.Get(GabineteShapeType.Hexagon);
            var wedge = GabineteShapes.Get(GabineteShapeType.Wedge);
            var cylinder = GabineteShapes.Get(GabineteShapeType.Cylinder);

            Assert.AreEqual(0, GabineteGeometry.OrientationError(prism, 0), 1e-9);
            Assert.AreEqual(90, GabineteGeometry.OrientationError(prism, 90), 1e-9);
            Assert.AreEqual(0, GabineteGeometry.OrientationError(prism, 180), 1e-9, "a rectangle fits again after a half turn");
            Assert.AreEqual(90, GabineteGeometry.OrientationError(prism, 270), 1e-9);
            Assert.AreEqual(0, GabineteGeometry.OrientationError(prism, -180), 1e-9);
            Assert.AreEqual(30, GabineteGeometry.OrientationError(hexagon, 30), 1e-9);
            Assert.AreEqual(0, GabineteGeometry.OrientationError(hexagon, 120), 1e-9);
            Assert.AreEqual(1, GabineteGeometry.OrientationError(wedge, 359), 1e-9);
            Assert.AreEqual(180, GabineteGeometry.OrientationError(wedge, 180), 1e-9, "a wedge has only one orientation");
            Assert.AreEqual(0, GabineteGeometry.OrientationError(cylinder, 47), 1e-9, "a circle fits at any yaw");
        }

        [Test]
        public void SomePiecesNeedAQuarterTurn_BeforeTheirOpeningAcceptsThem()
        {
            var six = GabineteProtocol.ForShapeCount(6);
            var byType = six.Pieces.ToDictionary(p => p.Shape, p => p.RequiredRotationDegrees);

            Assert.AreEqual(90, byType[GabineteShapeType.Prism], 1e-9);
            Assert.AreEqual(90, byType[GabineteShapeType.LBlock], 1e-9);
            Assert.AreEqual(30, byType[GabineteShapeType.Hexagon], 1e-9);
            Assert.AreEqual(0, byType[GabineteShapeType.Cylinder], 1e-9);
            Assert.AreEqual(0, byType[GabineteShapeType.Wedge], 1e-9);
        }

        [TestCase(4)]
        [TestCase(6)]
        [TestCase(8)]
        public void StandardConfigurations_HaveTheRightNumberOfDistinctShapes_AndAreDeterministic(int count)
        {
            var first = GabineteProtocol.ForShapeCount(count);
            var second = GabineteProtocol.ForShapeCount(count);

            Assert.AreEqual(count, first.ShapeCount);
            Assert.AreEqual(count, first.Pieces.Select(p => p.Shape).Distinct().Count());
            CollectionAssert.AreEqual(first.Pieces.Select(p => p.Id), second.Pieces.Select(p => p.Id));
            CollectionAssert.AreEqual(first.Pieces.Select(p => p.TraySlot), second.Pieces.Select(p => p.TraySlot));
            CollectionAssert.AreEqual(first.Pieces.Select(p => p.StartYawDegrees), second.Pieces.Select(p => p.StartYawDegrees));
            CollectionAssert.AreEquivalent(Enumerable.Range(0, count), first.Pieces.Select(p => p.TraySlot), "the tray uses every slot once");
            CollectionAssert.AreEquivalent(Enumerable.Range(0, count), first.Pieces.Select(p => p.OpeningIndex), "every opening takes one piece");
            Assert.IsTrue(first.Pieces.Any(p => p.TraySlot != p.OpeningIndex), "the tray order differs from the opening order");
            Assert.IsTrue(first.Pieces.Any(p => p.RequiredRotationDegrees >= 90), "at least one piece needs a quarter turn");
        }

        [Test]
        public void FourShapes_UseASubsetOfTheSixDefinedShapes_AndSixUseAllOfThem()
        {
            CollectionAssert.IsSubsetOf(GabineteProtocol.StandardShapes(4), RequiredSix);
            CollectionAssert.AreEquivalent(RequiredSix, GabineteProtocol.StandardShapes(6));
            CollectionAssert.IsSubsetOf(RequiredSix, GabineteProtocol.StandardShapes(8));
        }

        [Test]
        public void DefaultProtocol_HasFourSixAndEightShapeTrials()
        {
            CollectionAssert.AreEqual(new[] { 4, 6, 8 }, GabineteProtocol.Default().Select(c => c.ShapeCount));
        }

        [Test]
        public void ASpec_CanChooseItsOwnShapesTraySlotsAndYaws_WithoutTouchingTheTaskCode()
        {
            var config = new GabineteTrialSpec { shapes = "hexagon, prism, wedge", traySlots = "2,0,1", startYaws = "0, 180, 45", durationSeconds = 60 }.ToConfig();

            Assert.AreEqual(3, config.ShapeCount);
            Assert.AreEqual(GabineteShapeType.Hexagon, config.Pieces[0].Shape);
            Assert.AreEqual(2, config.Pieces[0].TraySlot);
            Assert.AreEqual(180, config.Pieces[1].StartYawDegrees, 1e-9);
            Assert.AreEqual(60, config.DurationSeconds);
        }

        [Test]
        public void BadSpecs_AreRejectedWithClearErrors()
        {
            Assert.Throws<FormatException>(() => new GabineteTrialSpec { shapes = "prism, sphere" }.ToConfig());
            Assert.Throws<FormatException>(() => new GabineteTrialSpec { shapes = "prism, wedge", traySlots = "0" }.ToConfig());
            Assert.Throws<InvalidOperationException>(() => new GabineteTrialSpec { shapes = "prism, wedge", traySlots = "1,1" }.ToConfig());
            Assert.Throws<ArgumentOutOfRangeException>(() => new GabineteTrialSpec { shapeCount = 5 }.ToConfig());
            Assert.Throws<InvalidOperationException>(() => new GabineteTrialSpec { shapes = "prism,prism,cylinder,wedge,l_block,hexagon,t_block,half_cylinder,truncated_cone" }.ToConfig());
        }

        [Test]
        public void Rules_MustBeCoherent()
        {
            Assert.DoesNotThrow(() => GabineteRules.Default.Validate());
            var rules = GabineteRules.Default;
            rules.positionTolerance = rules.captureRadius * 2;
            Assert.Throws<InvalidOperationException>(rules.Validate);
            rules = GabineteRules.Default;
            rules.orientationTolerance = 60;
            Assert.Throws<InvalidOperationException>(rules.Validate);
        }
    }

    public class GabineteTrialTests
    {
        FakeClock clock;
        MemorySink sink;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            sink = new MemorySink();
        }

        GabineteTrial Start(int count = 4, double durationSeconds = 0, int index = 1)
        {
            var config = GabineteProtocol.ForShapeCount(count);
            config.DurationSeconds = durationSeconds;
            var trial = new GabineteTrial(config, sink, clock, index);
            trial.Begin();
            return trial;
        }

        static GabinetePiece Of(GabineteTrial trial, GabineteShapeType type) => trial.Pieces.Single(p => p.Shape == type);

        /// <summary>The pose that puts a piece exactly in the given opening, at the yaw its own opening accepts.</summary>
        static PiecePose Fit(GabineteTrial trial, int opening, double yaw = 0) => PiecePose.At(opening, trial.ShapeCount, yaw);

        static GabineteOutcome Place(GabineteTrial trial, GabinetePiece piece)
        {
            trial.PickUp(piece.Id, piece.Spec.StartYawDegrees);
            return trial.Release(piece.Id, Fit(trial, piece.TargetOpening));
        }

        static object F(ResearchEvent e, string key) => MemorySink.Field(e, key);

        [Test]
        public void RightPiece_RightOpening_AcceptedOrientation_IsValid_AndUpdatesTheState()
        {
            var trial = Start();
            var prism = Of(trial, GabineteShapeType.Prism);

            trial.PickUp(prism.Id, 90);
            var outcome = trial.Release(prism.Id, Fit(trial, prism.TargetOpening, 180));

            Assert.IsTrue(outcome.Accepted);
            Assert.IsTrue(outcome.Attempt);
            Assert.IsTrue(outcome.Valid, "a prism fits after a half turn too");
            Assert.AreEqual("placed", outcome.Reason);
            Assert.IsTrue(prism.IsPlaced);
            Assert.AreEqual(GabinetePlacement.Placed, prism.Placement);
            Assert.AreEqual(180, prism.CurrentYawDegrees, 1e-9);
            Assert.AreEqual(1, trial.PlacedCount);
            Assert.AreEqual(1, trial.ValidPlacements);
            Assert.AreEqual("prism:placed,cylinder:open,wedge:open,l_block:open", trial.Snapshot());
            Assert.IsFalse(trial.IsCompleted);
        }

        [Test]
        public void RightPiece_ExactlyAlignedWithinTolerance_IsAccepted_ButJustOutsideItIsNot()
        {
            var trial = Start();
            var wedge = Of(trial, GabineteShapeType.Wedge);
            var rules = GabineteRules.Default;
            var centre = GabineteLayout.OpeningCenter(wedge.TargetOpening, 4);

            trial.PickUp(wedge.Id, 0);
            var outside = trial.Release(wedge.Id, new PiecePose(centre.X + rules.positionTolerance + 0.002f, centre.Y, 0));
            trial.PickUp(wedge.Id, 0);
            var inside = trial.Release(wedge.Id, new PiecePose(centre.X + rules.positionTolerance - 0.002f, centre.Y, rules.orientationTolerance - 1));

            Assert.IsFalse(outside.Valid);
            Assert.AreEqual("misaligned", outside.Reason);
            Assert.IsTrue(inside.Valid);
        }

        [Test]
        public void WrongPiece_OverAnotherPiecesOpening_IsRejected_AndStaysAvailable()
        {
            var trial = Start();
            var cylinder = Of(trial, GabineteShapeType.Cylinder);
            var wedgeOpening = Of(trial, GabineteShapeType.Wedge).TargetOpening;

            trial.PickUp(cylinder.Id, 0);
            var outcome = trial.Release(cylinder.Id, Fit(trial, wedgeOpening));

            Assert.IsTrue(outcome.Attempt);
            Assert.IsFalse(outcome.Valid);
            Assert.AreEqual("wrong_opening", outcome.Reason);
            Assert.AreEqual(wedgeOpening, outcome.OpeningIndex);
            Assert.IsFalse(cylinder.IsPlaced);
            Assert.AreEqual(0, trial.PlacedCount);
            Assert.AreEqual(1, trial.InvalidAttempts);
        }

        [Test]
        public void RightPiece_WrongOrientation_IsRejected_AndSucceedsAfterTurning()
        {
            var trial = Start();
            var prism = Of(trial, GabineteShapeType.Prism);

            trial.PickUp(prism.Id, prism.Spec.StartYawDegrees);
            var wrong = trial.Release(prism.Id, Fit(trial, prism.TargetOpening, prism.Spec.StartYawDegrees));
            trial.PickUp(prism.Id, prism.Spec.StartYawDegrees);
            var right = trial.Release(prism.Id, Fit(trial, prism.TargetOpening, 0));

            Assert.IsFalse(wrong.Valid);
            Assert.AreEqual("wrong_orientation", wrong.Reason);
            Assert.IsTrue(right.Valid);
            Assert.AreEqual(2, prism.AttemptCount);
        }

        [Test]
        public void TiltedPiece_OrOccupiedOpening_AreRejected()
        {
            var trial = Start();
            var wedge = Of(trial, GabineteShapeType.Wedge);
            var cylinder = Of(trial, GabineteShapeType.Cylinder);
            var centre = GabineteLayout.OpeningCenter(wedge.TargetOpening, 4);

            trial.PickUp(wedge.Id, 0);
            var tilted = trial.Release(wedge.Id, new PiecePose(centre.X, centre.Y, 0, tiltDegrees: 60));
            Place(trial, wedge);
            trial.PickUp(cylinder.Id, 0);
            var occupied = trial.Release(cylinder.Id, Fit(trial, wedge.TargetOpening));

            Assert.AreEqual("not_upright", tilted.Reason);
            Assert.AreEqual("opening_occupied", occupied.Reason);
            Assert.IsTrue(wedge.IsPlaced);
        }

        [Test]
        public void InvalidAttempts_NeverCorruptTheState()
        {
            var trial = Start();
            var prism = Of(trial, GabineteShapeType.Prism);
            var cylinder = Of(trial, GabineteShapeType.Cylinder);
            Place(trial, cylinder);
            var before = trial.Snapshot();

            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, Fit(trial, cylinder.TargetOpening));
            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, Fit(trial, prism.TargetOpening, 90));
            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, PiecePose.At(2, 4, 0));

            Assert.AreEqual(before, trial.Snapshot());
            Assert.AreEqual(1, trial.PlacedCount);
            Assert.IsTrue(cylinder.IsPlaced);
            Assert.IsFalse(prism.IsPlaced);
            Assert.AreEqual(prism.Spec.StartYawDegrees, prism.CurrentYawDegrees, 1e-9, "a rejected piece goes back to its starting orientation");
            Assert.IsTrue(trial.IsRunning);

            Assert.IsTrue(Place(trial, prism).Valid, "the trial carries on normally after rejected attempts");
            Assert.AreEqual(2, trial.PlacedCount);
        }

        [Test]
        public void ReleasingAwayFromTheOpenings_OrTooHigh_IsNotAnAttempt()
        {
            var trial = Start();
            var wedge = Of(trial, GabineteShapeType.Wedge);
            var centre = GabineteLayout.OpeningCenter(wedge.TargetOpening, 4);

            trial.PickUp(wedge.Id, 0);
            var onTray = trial.Release(wedge.Id, new PiecePose(0f, GabineteLayout.TrayZ, 0));
            trial.PickUp(wedge.Id, 0);
            var high = trial.Release(wedge.Id, new PiecePose(centre.X, centre.Y, 0, height: 0.5f));

            Assert.IsFalse(onTray.Attempt);
            Assert.AreEqual("off_target", onTray.Reason);
            Assert.IsFalse(high.Attempt);
            Assert.AreEqual(0, trial.AttemptCount);
            Assert.AreEqual(0, trial.InvalidAttempts);
            Assert.AreEqual(2, trial.OffTargetReleases);
            Assert.AreEqual(0, trial.PlacedCount);
            Assert.AreEqual(2, sink.OfType("piece_released").Count());
            Assert.AreEqual("off_target", F(sink.OfType("piece_released").First(), "placement_result"));
        }

        [Test]
        public void Completion_HappensOnlyWhenEveryConfiguredPieceIsPlaced_InAnyOrder()
        {
            foreach (var count in new[] { 4, 6, 8 })
            {
                sink.Events.Clear();
                var trial = Start(count);
                var order = trial.Pieces.OrderByDescending(p => p.Spec.TraySlot).ToList();

                for (var i = 0; i < order.Count - 1; i++)
                {
                    Assert.IsTrue(Place(trial, order[i]).Valid);
                    Assert.IsFalse(trial.IsCompleted, $"{count} shapes: not complete after {i + 1} pieces");
                    Assert.IsEmpty(sink.OfType("trial_completed"));
                }

                var last = Place(trial, order.Last());

                Assert.IsTrue(last.Completed, $"{count} shapes");
                Assert.IsTrue(trial.IsCompleted);
                Assert.IsFalse(trial.IsRunning);
                Assert.AreEqual(count, trial.PlacedCount);
                Assert.AreEqual(1, sink.OfType("trial_completed").Count());
                Assert.AreEqual(count, trial.PlacementSequence.Count);
            }
        }

        [Test]
        public void Completion_IsNotReachedByWrongPlacements()
        {
            var trial = Start();
            foreach (var piece in trial.Pieces)
            {
                trial.PickUp(piece.Id, piece.Spec.StartYawDegrees);
                trial.Release(piece.Id, Fit(trial, (piece.TargetOpening + 1) % 4));
            }

            Assert.IsFalse(trial.IsCompleted);
            Assert.AreEqual(0, trial.PlacedCount);
            Assert.AreEqual(4, trial.InvalidAttempts);
        }

        [Test]
        public void APlacedPiece_CannotBePickedUpAgain_AndNothingHappensAfterTheTrialEnds()
        {
            var trial = Start();
            var wedge = Of(trial, GabineteShapeType.Wedge);
            Place(trial, wedge);

            var again = trial.PickUp(wedge.Id, 0);
            trial.End("researcher_stop");
            var late = trial.PickUp(Of(trial, GabineteShapeType.Cylinder).Id, 0);

            Assert.IsFalse(again.Accepted);
            Assert.IsFalse(late.Accepted);
            Assert.IsFalse(trial.Release(Of(trial, GabineteShapeType.Cylinder).Id, Fit(trial, 1)).Accepted);
            Assert.IsFalse(trial.PickUp("nonexistent", 0).Accepted);
        }

        [Test]
        public void Initialisation_IsDeterministic_SameConfigSameStartStateAndStartEvent()
        {
            var first = Start(6);
            var firstStart = sink.OfType("trial_started").Single();
            sink.Events.Clear();
            var second = Start(6);
            var secondStart = sink.OfType("trial_started").Single();

            Assert.AreEqual(first.Snapshot(), second.Snapshot());
            Assert.AreEqual("prism:open,cylinder:open,truncated_cone:open,wedge:open,l_block:open,hexagon:open", first.Snapshot());
            CollectionAssert.AreEqual(firstStart.Fields.Select(f => f.Key), secondStart.Fields.Select(f => f.Key));
            CollectionAssert.AreEqual((IEnumerable<string>)F(firstStart, "pieces"), (IEnumerable<string>)F(secondStart, "pieces"));
            CollectionAssert.AreEqual((IEnumerable<double>)F(firstStart, "required_rotation_deg"), (IEnumerable<double>)F(secondStart, "required_rotation_deg"));
            Assert.AreEqual(0, second.PlacedCount);
            Assert.IsFalse(second.IsCompleted);
        }

        [Test]
        public void ANewTrial_StartsClean_NothingCarriesOverFromAnEarlierOne()
        {
            var first = Start(4);
            foreach (var piece in first.Pieces) Place(first, piece);
            var second = Start(4, index: 2);

            Assert.IsTrue(first.IsCompleted);
            Assert.AreEqual(0, second.PlacedCount);
            Assert.AreEqual(0, second.AttemptCount);
            Assert.IsTrue(second.IsRunning);
            Assert.IsTrue(second.Pieces.All(p => !p.IsPlaced && !p.IsHeld));
        }

        [Test]
        public void Rotations_CountOnlyGrabsWhereTheOrientationReallyChanged()
        {
            var trial = Start();
            var prism = Of(trial, GabineteShapeType.Prism);
            var centre = GabineteLayout.OpeningCenter(prism.TargetOpening, 4);

            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, new PiecePose(0f, GabineteLayout.TrayZ, 95));
            Assert.AreEqual(0, trial.Rotations, "5 degrees is hand tremor, not a rotation");

            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, new PiecePose(centre.X, centre.Y, 0));
            Assert.AreEqual(1, trial.Rotations);
            Assert.AreEqual(90, trial.BuildSummary().TotalRotationDegrees, 1e-9);
        }

        [Test]
        public void PlacementAttemptEvent_CarriesEverythingTheResearchNeeds()
        {
            var trial = Start(4, index: 3);
            var prism = Of(trial, GabineteShapeType.Prism);
            clock.Advance(2.5);

            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, Fit(trial, prism.TargetOpening, 90));
            var e = sink.OfType("placement_attempt").Single();

            Assert.AreEqual("GabineteFormas", e.Task);
            Assert.AreEqual(3, F(e, "trial_index"));
            Assert.AreEqual(prism.Id, F(e, "piece_id"));
            Assert.AreEqual("prism", F(e, "piece_type"));
            Assert.AreEqual(prism.TargetOpening, F(e, "target_opening"));
            Assert.AreEqual(prism.TargetOpening, F(e, "attempted_opening"));
            Assert.AreEqual("prism", F(e, "attempted_opening_type"));
            Assert.AreEqual(90d, F(e, "starting_orientation_deg"));
            Assert.AreEqual(90d, F(e, "attempted_orientation_deg"));
            Assert.AreEqual(90d, F(e, "orientation_error_deg"));
            Assert.AreEqual("wrong_orientation", F(e, "placement_result"));
            Assert.AreEqual(false, F(e, "valid"));
            Assert.AreEqual("prism:open,cylinder:open,wedge:open,l_block:open", F(e, "resulting_state"));
            Assert.AreEqual(2.5, (double)F(e, "elapsed_s"), 1e-9);
            Assert.AreEqual("in_progress", F(e, "completion_status"));
        }

        [Test]
        public void EventStream_HasTheExpectedOrder_AndEveryEventIsForGabinete()
        {
            var trial = Start(4);
            foreach (var piece in trial.Pieces) Place(trial, piece);

            var types = sink.Events.Select(e => e.EventType).ToList();
            Assert.AreEqual("trial_started", types.First());
            Assert.AreEqual(new[] { "trial_completed", "trial_summary" }, types.Skip(types.Count - 2).ToArray());
            Assert.AreEqual(4, types.Count(t => t == "piece_picked_up"));
            Assert.AreEqual(4, types.Count(t => t == "placement_attempt"));
            Assert.IsTrue(sink.Events.All(e => e.Task == "GabineteFormas"));
        }

        [Test]
        public void Summary_ReportsTheMetricsOfTheTrial()
        {
            var trial = Start(4);
            var prism = Of(trial, GabineteShapeType.Prism);
            var cylinder = Of(trial, GabineteShapeType.Cylinder);
            var wedge = Of(trial, GabineteShapeType.Wedge);
            var l = Of(trial, GabineteShapeType.LBlock);
            clock.Advance(3);

            trial.PickUp(prism.Id, 90);
            clock.Advance(4);
            trial.Release(prism.Id, Fit(trial, cylinder.TargetOpening, 0));
            Assert.AreEqual("wrong_opening", trial.ActionSequence.Last().Split(':').Last());
            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, Fit(trial, prism.TargetOpening, 90));
            trial.PickUp(prism.Id, 90);
            trial.Release(prism.Id, Fit(trial, prism.TargetOpening, 0));
            trial.PickUp(wedge.Id, 0);
            trial.Release(wedge.Id, new PiecePose(0f, GabineteLayout.TrayZ, 0));
            clock.Advance(5);
            Place(trial, cylinder);
            Place(trial, wedge);
            Place(trial, l);
            var summary = trial.BuildSummary();

            Assert.AreEqual(4, summary.ShapeCount);
            Assert.AreEqual(6, summary.PlacementAttempts);
            Assert.AreEqual(4, summary.ValidPlacements);
            Assert.AreEqual(2, summary.InvalidAttempts);
            Assert.AreEqual(1, summary.WrongOpeningAttempts);
            Assert.AreEqual(1, summary.WrongOrientationAttempts);
            Assert.AreEqual(1, summary.OffTargetReleases);
            Assert.AreEqual(4, summary.CompletedPieces);
            Assert.AreEqual("completed", summary.Status);
            Assert.IsTrue(summary.Completed);
            Assert.AreEqual(12, summary.CompletionTimeSeconds.Value, 1e-9);
            Assert.AreEqual(3, summary.FirstActionLatencySeconds.Value, 1e-9);
            Assert.AreEqual(3, summary.RotationCount, "the prism was turned twice and the l-block once; the cylinder and wedge were never turned");
            Assert.AreEqual(new[] { prism.Id, cylinder.Id, wedge.Id, l.Id }, summary.PlacementSequence.ToArray());
            CollectionAssert.Contains(summary.ActionSequence, $"{prism.Id}>{prism.TargetOpening}:placed");
            CollectionAssert.Contains(summary.ActionSequence, $"{wedge.Id}:off_target");
        }

        [Test]
        public void TimeLimit_EndsTheTrialAsIncomplete_AndWritesASummary()
        {
            var trial = Start(4, durationSeconds: 60);
            Place(trial, Of(trial, GabineteShapeType.Wedge));

            clock.Advance(59);
            Assert.IsFalse(trial.CheckTimeLimit());
            clock.Advance(2);
            Assert.IsTrue(trial.CheckTimeLimit());

            Assert.IsFalse(trial.IsRunning);
            Assert.IsFalse(trial.IsCompleted);
            var summary = sink.OfType("trial_summary").Single();
            Assert.AreEqual("incomplete", F(summary, "completion_status"));
            Assert.AreEqual("time_limit", F(summary, "end_reason"));
            Assert.AreEqual(1, F(summary, "completed_pieces"));
            Assert.IsNull(F(summary, "completion_time_s"));
            Assert.IsFalse(trial.CheckTimeLimit(), "ending twice does nothing");
        }

        [Test]
        public void SummaryJson_AddsTheSessionFields_AndUsesTheSameNamesAsTheEventLog()
        {
            var trial = Start(4);
            foreach (var piece in trial.Pieces) Place(trial, piece);

            var json = trial.BuildSummary().ToJson(new SessionInfo("S-1", "P-7", "Static"));

            StringAssert.StartsWith("{\"session_id\":\"S-1\",\"participant_id\":\"P-7\",\"condition\":\"Static\",\"task\":\"GabineteFormas\"", json);
            foreach (var key in new[] { "placement_attempts", "valid_placements", "invalid_attempts", "rotation_count", "completion_time_s", "completion_status", "placement_sequence", "action_sequence" })
                StringAssert.Contains($"\"{key}\":", json);
            StringAssert.Contains("\"completion_status\":\"completed\"", json);
        }
    }

    public class GabineteMeshTests
    {
        static double TriangleArea(IReadOnlyList<Vec2> pts, IList<int> tris)
        {
            double sum = 0;
            for (var t = 0; t < tris.Count; t += 3)
                sum += Cross(pts[tris[t]], pts[tris[t + 1]], pts[tris[t + 2]]) * 0.5;
            return sum;
        }

        static double Cross(Vec2 a, Vec2 b, Vec2 c) => ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);

        [Test]
        public void Triangulation_CoversEveryFootprintExactly_WithCounterClockwiseTriangles()
        {
            foreach (GabineteShapeType type in Enum.GetValues(typeof(GabineteShapeType)))
            {
                var footprint = GabineteShapes.Get(type).Footprint;
                var tris = PolygonTriangulator.Triangulate(footprint, out var pts);

                Assert.AreEqual(Polygon2D.SignedArea(footprint), TriangleArea(pts, tris), 1e-9, type + " area");
                for (var t = 0; t < tris.Count; t += 3)
                    Assert.Greater(Cross(pts[tris[t]], pts[tris[t + 1]], pts[tris[t + 2]]), 0, type + " triangle winding");
            }
        }

        [Test]
        public void Triangulation_WithAHole_CoversTheRingAndNothingInsideTheHole()
        {
            foreach (GabineteShapeType type in Enum.GetValues(typeof(GabineteShapeType)))
            {
                var hole = Polygon2D.Scaled(GabineteShapes.Get(type).Footprint, 1.04f, new Vec2(-0.004f, -0.005f));
                var cell = new List<Vec2> { new Vec2(-0.0525f, -0.075f), new Vec2(0.0525f, -0.075f), new Vec2(0.0525f, 0.075f), new Vec2(-0.0525f, 0.075f) };

                var tris = PolygonTriangulator.Triangulate(cell, new IReadOnlyList<Vec2>[] { hole }, out var pts);

                var expected = Polygon2D.SignedArea(cell) - Polygon2D.SignedArea(hole);
                Assert.AreEqual(expected, TriangleArea(pts, tris), 1e-9, type + " ring area");
                for (var t = 0; t < tris.Count; t += 3)
                {
                    Assert.Greater(Cross(pts[tris[t]], pts[tris[t + 1]], pts[tris[t + 2]]), 0, type + " winding");
                    var centroid = new Vec2((pts[tris[t]].X + pts[tris[t + 1]].X + pts[tris[t + 2]].X) / 3f, (pts[tris[t]].Y + pts[tris[t + 1]].Y + pts[tris[t + 2]].Y) / 3f);
                    Assert.IsFalse(Polygon2D.Contains(hole, centroid), type + " no triangle inside the opening");
                }
            }
        }

        static (double volume, double surface) Measure(PolygonMeshData mesh, int submesh)
        {
            double volume = 0, surface = 0;
            var tris = mesh.Submeshes[submesh];
            for (var t = 0; t < tris.Length; t += 3)
            {
                var a = Position(mesh, tris[t]);
                var b = Position(mesh, tris[t + 1]);
                var c = Position(mesh, tris[t + 2]);
                var n = Cross3(Sub(b, a), Sub(c, a));
                volume += Dot(a, Cross3(b, c)) / 6.0;
                surface += Math.Sqrt(Dot(n, n)) * 0.5;
            }
            return (volume, surface);
        }

        static double[] Position(PolygonMeshData m, int i) => new double[] { m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2] };
        static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static double[] Cross3(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        [Test]
        public void PieceMeshes_AreClosedSolids_WithOutwardFacingTriangles_AndTheExpectedVolume()
        {
            foreach (GabineteShapeType type in Enum.GetValues(typeof(GabineteShapeType)))
            {
                var shape = GabineteShapes.Get(type);
                var mesh = GabineteMeshBuilder.BuildPiece(shape);

                var (volume, _) = Measure(mesh, 0);
                var area = Polygon2D.SignedArea(shape.Footprint);
                var expected = area * shape.Height * (shape.BaseScale * shape.BaseScale + shape.BaseScale * shape.TopScale + shape.TopScale * shape.TopScale) / 3.0;

                Assert.AreEqual(expected, volume, expected * 1e-4, type + " volume (positive means the faces point outward)");
            }
        }

        [Test]
        public void PieceMeshes_HaveNormalsThatAgreeWithTheirTriangles_AndFitInsideTheHeight()
        {
            foreach (GabineteShapeType type in Enum.GetValues(typeof(GabineteShapeType)))
            {
                var shape = GabineteShapes.Get(type);
                var mesh = GabineteMeshBuilder.BuildPiece(shape);
                var tris = mesh.Submeshes[0];

                for (var t = 0; t < tris.Length; t += 3)
                {
                    var a = Position(mesh, tris[t]);
                    var geometric = Cross3(Sub(Position(mesh, tris[t + 1]), a), Sub(Position(mesh, tris[t + 2]), a));
                    var stored = new double[3];
                    for (var k = 0; k < 3; k++)
                        for (var v = 0; v < 3; v++) stored[k] += mesh.Normals[tris[t + v] * 3 + k];
                    Assert.Greater(Dot(geometric, stored), 0, type + " triangle " + t / 3);
                }
                for (var i = 0; i < mesh.VertexCount; i++)
                {
                    Assert.GreaterOrEqual(mesh.Vertices[i * 3 + 1], -1e-6f);
                    Assert.LessOrEqual(mesh.Vertices[i * 3 + 1], shape.Height + 1e-6f);
                }
                Assert.AreEqual(mesh.Vertices.Length / 3 * 2, mesh.Uvs.Length);
                Assert.AreEqual(mesh.Vertices.Length, mesh.Normals.Length);
            }
        }

        [Test]
        public void TaperedPiece_IsNarrowerAtTheBottom()
        {
            var cone = GabineteShapes.Get(GabineteShapeType.TruncatedCone);
            var mesh = GabineteMeshBuilder.BuildPiece(cone);

            float bottom = 0, top = 0;
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                var r = (float)Math.Sqrt(mesh.Vertices[i * 3] * mesh.Vertices[i * 3] + mesh.Vertices[i * 3 + 2] * mesh.Vertices[i * 3 + 2]);
                if (mesh.Vertices[i * 3 + 1] < 1e-6f) bottom = Math.Max(bottom, r);
                else if (mesh.Vertices[i * 3 + 1] > cone.Height - 1e-6f) top = Math.Max(top, r);
            }
            Assert.Less(bottom, top * 0.6f);
        }

        [TestCase(4)]
        [TestCase(6)]
        [TestCase(8)]
        public void Board_HasAPocketPerOpening_AndTheTopSurfaceIsThePlateMinusTheOpenings(int count)
        {
            var config = GabineteProtocol.ForShapeCount(count);
            var openings = config.Pieces.Select(p => new BoardOpening(p.Shape, GabineteLayout.OpeningCenter(p.OpeningIndex, count))).ToList();
            var width = GabineteLayout.BoardWidth(count);

            var mesh = GabineteMeshBuilder.BuildBoard(openings, width, GabineteLayout.BoardDepth, GabineteLayout.BoardThickness, GabineteLayout.PocketDepth);

            Assert.AreEqual(2, mesh.Submeshes.Length);
            double openingArea = 0;
            foreach (var piece in config.Pieces)
            {
                var shape = piece.ShapeSpec;
                var (top, _) = GabineteShapes.OpeningScales(shape, GabineteLayout.PocketDepth);
                openingArea += Polygon2D.SignedArea(shape.Footprint) * top * top;
            }
            double topArea = 0;
            var tris = mesh.Submeshes[0];
            for (var t = 0; t < tris.Length; t += 3)
            {
                var a = Position(mesh, tris[t]);
                var b = Position(mesh, tris[t + 1]);
                var c = Position(mesh, tris[t + 2]);
                if (Math.Abs(a[1]) < 1e-9 && Math.Abs(b[1]) < 1e-9 && Math.Abs(c[1]) < 1e-9)
                {
                    var n = Cross3(Sub(b, a), Sub(c, a));
                    Assert.Greater(n[1], 0, "top surface faces up");
                    topArea += n[1] * 0.5;
                }
            }
            Assert.AreEqual(width * GabineteLayout.BoardDepth - openingArea, topArea, 1e-6);
        }

        [Test]
        public void BoardPockets_FaceIntoTheOpenings_AndTheirFloorsFaceUp()
        {
            var openings = new[] { new BoardOpening(GabineteShapeType.LBlock, new Vec2(0, 0)) };
            var mesh = GabineteMeshBuilder.BuildBoard(openings, 0.2f, 0.15f, GabineteLayout.BoardThickness, GabineteLayout.PocketDepth);

            var tris = mesh.Submeshes[1];
            var floorArea = 0.0;
            for (var t = 0; t < tris.Length; t += 3)
            {
                var a = Position(mesh, tris[t]);
                var n = Cross3(Sub(Position(mesh, tris[t + 1]), a), Sub(Position(mesh, tris[t + 2]), a));
                var stored = new double[3];
                for (var k = 0; k < 3; k++)
                    for (var v = 0; v < 3; v++) stored[k] += mesh.Normals[tris[t + v] * 3 + k];
                Assert.Greater(Dot(n, stored), 0);
                var b = Position(mesh, tris[t + 1]);
                var c = Position(mesh, tris[t + 2]);
                var onFloor = Math.Abs(a[1] + GabineteLayout.PocketDepth) < 1e-9 && Math.Abs(b[1] + GabineteLayout.PocketDepth) < 1e-9 && Math.Abs(c[1] + GabineteLayout.PocketDepth) < 1e-9;
                if (onFloor)
                {
                    Assert.Greater(n[1], 0, "the pocket floor faces up");
                    floorArea += n[1] * 0.5;
                }
                else
                {
                    // A wall triangle faces the opening: a point just in front of its middle is inside the (vertical-walled) L opening.
                    var length = Math.Sqrt(Dot(n, n));
                    var mid = new[] { (a[0] + b[0] + c[0]) / 3 + 0.001 * n[0] / length, (a[2] + b[2] + c[2]) / 3 + 0.001 * n[2] / length };
                    var opening = Polygon2D.Scaled(GabineteShapes.Get(GabineteShapeType.LBlock).Footprint, GabineteShapes.OpeningClearance);
                    Assert.IsTrue(Polygon2D.Contains(opening, new Vec2((float)mid[0], (float)mid[1])), "pocket walls face into the opening");
                }
            }
            var shape = GabineteShapes.Get(GabineteShapeType.LBlock);
            var (_, bottom) = GabineteShapes.OpeningScales(shape, GabineteLayout.PocketDepth);
            Assert.AreEqual(Polygon2D.SignedArea(shape.Footprint) * bottom * bottom, floorArea, 1e-7);
        }
    }
}
