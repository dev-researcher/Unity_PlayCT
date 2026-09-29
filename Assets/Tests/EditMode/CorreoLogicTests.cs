using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Correo;

namespace PlayCT.Tests
{
    public class CorreoNetworkTests
    {
        static readonly CorreoNetwork Net = CorreoNetwork.Standard;

        [TestCase(Settlement.A, Settlement.B, 2)]
        [TestCase(Settlement.A, Settlement.C, 1)]
        [TestCase(Settlement.B, Settlement.D, 2)]
        [TestCase(Settlement.C, Settlement.D, 1)]
        [TestCase(Settlement.C, Settlement.E, 2)]
        [TestCase(Settlement.D, Settlement.E, 1)]
        public void TheSixPaths_ExistWithTheirCapacities(Settlement from, Settlement to, int capacity)
        {
            Assert.IsTrue(Net.TryGetPath(from, to, out var path));
            Assert.AreEqual(capacity, path.Capacity);
        }

        [Test]
        public void ThereAreExactlySixPaths_AndTheyAreDirected()
        {
            Assert.AreEqual(6, Net.Paths.Count);
            Assert.IsFalse(Net.HasPath(Settlement.B, Settlement.A));
            Assert.IsFalse(Net.HasPath(Settlement.E, Settlement.D));
            Assert.IsFalse(Net.HasPath(Settlement.D, Settlement.C));
        }

        [Test]
        public void BToC_DoesNotExist_AndAShipmentAlongItIsRejected()
        {
            Assert.IsFalse(Net.HasPath(Settlement.B, Settlement.C));

            var state = CorreoState.Initial.Apply(new CorreoShipment(Settlement.A, Settlement.B, new[] { CorreoPackage.P2 }));
            var verdict = CorreoRules.Evaluate(Net, state, new CorreoShipment(Settlement.B, Settlement.C, new[] { CorreoPackage.P2 }));

            Assert.IsFalse(verdict.Valid);
            Assert.AreEqual("no_path", verdict.Reason);
            Assert.AreEqual("path", verdict.Category);
            Assert.IsNull(verdict.PathCapacity);
        }

        [Test]
        public void ANetworkCannotDefineThePathTwice_OrALoop()
        {
            Assert.Throws<ArgumentException>(() => new CorreoNetwork(new[] { new CorreoPath(Settlement.A, Settlement.B, 1), new CorreoPath(Settlement.A, Settlement.B, 2) }));
            Assert.Throws<ArgumentException>(() => new CorreoPath(Settlement.A, Settlement.A, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CorreoPath(Settlement.A, Settlement.B, 0));
        }

        [Test]
        public void TheConfiguration_HasNoWayToChangeTheTopology()
        {
            var config = new CorreoTrialSpec { durationSeconds = 30 }.ToConfig();

            CollectionAssert.AreEqual(CorreoNetwork.Standard.Labels(), config.Network.Labels());
            CollectionAssert.AreEqual(new[] { "A>B:2", "A>C:1", "B>D:2", "C>D:1", "C>E:2", "D>E:1" }, config.Network.Labels());
        }
    }

    public class CorreoTrialTests
    {
        FakeClock clock;
        MemorySink sink;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            sink = new MemorySink();
        }

        CorreoTrial Start(double durationSeconds = 0, int index = 1)
        {
            var trial = new CorreoTrial(new CorreoTrialConfig { DurationSeconds = durationSeconds }, sink, clock, index);
            trial.Begin();
            return trial;
        }

        static CorreoOutcome Ship(CorreoTrial trial, Settlement from, Settlement to, params CorreoPackage[] packages) => trial.Ship(from, to, packages);

        static readonly CorreoPackage P1 = CorreoPackage.P1;
        static readonly CorreoPackage P2 = CorreoPackage.P2;
        static readonly CorreoPackage P3 = CorreoPackage.P3;

        /// <summary>A five-shipment solution: P1 by way of C, then P2 and P3 together from C.</summary>
        static readonly (Settlement, Settlement, CorreoPackage[])[] Solution =
        {
            (Settlement.A, Settlement.C, new[] { CorreoPackage.P1 }),
            (Settlement.C, Settlement.E, new[] { CorreoPackage.P1 }),
            (Settlement.A, Settlement.C, new[] { CorreoPackage.P2 }),
            (Settlement.A, Settlement.C, new[] { CorreoPackage.P3 }),
            (Settlement.C, Settlement.E, new[] { CorreoPackage.P2, CorreoPackage.P3 }),
        };

        static void Play(CorreoTrial trial, IEnumerable<(Settlement from, Settlement to, CorreoPackage[] packages)> moves)
        {
            foreach (var (from, to, packages) in moves) Assert.IsTrue(trial.Ship(from, to, packages).Valid, $"{from}>{to}");
        }

        static object F(ResearchEvent e, string key) => MemorySink.Field(e, key);

        [Test]
        public void InitialState_HasThreePackagesAtA_P1HighPriorityAndTheOthersLow()
        {
            var state = CorreoState.Initial;

            Assert.AreEqual("P1:A,P2:A,P3:A", state.Snapshot());
            foreach (var package in CorreoPackages.All) Assert.AreEqual(Settlement.A, state.Location(package));
            Assert.AreEqual(0, state.ShipmentCount);
            Assert.IsTrue(CorreoPackages.IsHighPriority(P1));
            Assert.IsFalse(CorreoPackages.IsHighPriority(P2));
            Assert.IsFalse(CorreoPackages.IsHighPriority(P3));
            Assert.IsFalse(state.AllDelivered);
            var trial = Start();
            var started = sink.OfType("trial_started").Single();
            CollectionAssert.AreEqual(new[] { "P1:high", "P2:low", "P3:low" }, (IEnumerable<string>)F(started, "packages"));
            Assert.AreEqual("P1:A,P2:A,P3:A", F(started, "package_locations"));
        }

        [TestCase(Settlement.B)]
        [TestCase(Settlement.C)]
        public void FromA_ASinglePackageCanGoToBOrC(Settlement to)
        {
            var trial = Start();

            var outcome = Ship(trial, Settlement.A, to, P2);

            Assert.IsTrue(outcome.Valid);
            Assert.AreEqual(to, trial.State.Location(P2));
        }

        [Test]
        public void EveryPathOfTheNetwork_CanBeUsedForAValidShipment()
        {
            var trial = Start();
            // P2 takes A>B, B>D; P3 waits at A while P1 goes C>E first so that low-priority packages may follow.
            Assert.IsTrue(Ship(trial, Settlement.A, Settlement.B, P2).Valid, "A>B");
            Assert.IsTrue(Ship(trial, Settlement.B, Settlement.D, P2).Valid, "B>D");
            Assert.IsTrue(Ship(trial, Settlement.A, Settlement.C, P1).Valid, "A>C");
            Assert.IsTrue(Ship(trial, Settlement.C, Settlement.E, P1).Valid, "C>E");
            Assert.IsTrue(Ship(trial, Settlement.D, Settlement.E, P2).Valid, "D>E");
            Assert.IsTrue(Ship(trial, Settlement.A, Settlement.C, P3).Valid);
            Assert.IsTrue(Ship(trial, Settlement.C, Settlement.D, P3).Valid, "C>D");
            Assert.IsTrue(Ship(trial, Settlement.D, Settlement.E, P3).Valid);
            Assert.IsTrue(trial.IsCompleted);
        }

        [Test]
        public void ShipmentWithinCapacity_Succeeds_AndMovesTheSelectedPackagesTogether()
        {
            var trial = Start();

            var outcome = Ship(trial, Settlement.A, Settlement.B, P2, P3);

            Assert.IsTrue(outcome.Valid);
            Assert.AreEqual("shipped", outcome.Reason);
            Assert.AreEqual(Settlement.B, trial.State.Location(P2));
            Assert.AreEqual(Settlement.B, trial.State.Location(P3));
            Assert.AreEqual(Settlement.A, trial.State.Location(P1));
            Assert.AreEqual(1, trial.State.ShipmentCount);
            Assert.AreEqual("P1:A,P2:B,P3:B", trial.State.Snapshot());
        }

        [Test]
        public void ShipmentExceedingCapacity_IsRejected_AndTheStateIsUnchanged()
        {
            var trial = Start();
            var before = trial.State;

            var outcome = Ship(trial, Settlement.A, Settlement.C, P2, P3);

            Assert.IsTrue(outcome.Accepted);
            Assert.IsFalse(outcome.Valid);
            Assert.AreEqual("capacity_exceeded", outcome.Reason);
            Assert.AreEqual("capacity", outcome.Category);
            Assert.AreSame(before, trial.State, "the previous valid state is kept, nothing moved or dropped");
            Assert.AreEqual("P1:A,P2:A,P3:A", trial.State.Snapshot());
            Assert.AreEqual(0, trial.ValidShipments);
            Assert.AreEqual(1, trial.InvalidShipments);

            var three = Ship(trial, Settlement.A, Settlement.B, P1, P2, P3);
            Assert.AreEqual("capacity_exceeded", three.Reason);
            Assert.AreEqual("P1:A,P2:A,P3:A", trial.State.Snapshot());
        }

        [Test]
        public void RejectedShipment_DoesNotAlterTheStateOrTheCounts_AndTheTrialGoesOn()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.B, P2);
            var before = trial.State;

            Ship(trial, Settlement.B, Settlement.C, P2);
            Ship(trial, Settlement.A, Settlement.C, P1, P2);
            Ship(trial, Settlement.A, Settlement.B, P1, P3);
            Ship(trial, Settlement.D, Settlement.E, P2);

            Assert.AreSame(before, trial.State);
            Assert.AreEqual(1, trial.State.ShipmentCount);
            Assert.AreEqual(1, trial.ValidShipments);
            Assert.AreEqual(4, trial.InvalidShipments);
            Assert.IsTrue(trial.IsRunning);
            Assert.IsTrue(Ship(trial, Settlement.B, Settlement.D, P2).Valid, "a valid shipment still works afterwards");
        }

        [Test]
        public void APackage_CanOnlyBeShippedFromWhereItIs()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.B, P2);

            var fromA = Ship(trial, Settlement.A, Settlement.B, P2);
            var fromB = Ship(trial, Settlement.B, Settlement.D, P3);
            var mixed = Ship(trial, Settlement.A, Settlement.B, P2, P3);

            Assert.AreEqual("package_not_at_source", fromA.Reason, "P2 is no longer at A");
            Assert.AreEqual("package_not_at_source", fromB.Reason, "P3 is still at A");
            Assert.AreEqual("package_not_at_source", mixed.Reason, "one absent package refuses the whole shipment");
            Assert.AreEqual("selection", fromA.Category);
            Assert.AreEqual("P1:A,P2:B,P3:A", trial.State.Snapshot());
        }

        [Test]
        public void ShipmentUpdatesLocations_AndPackagesCanWaitAtASettlement()
        {
            var trial = Start();

            Ship(trial, Settlement.A, Settlement.B, P2, P3);
            Assert.AreEqual("P1:A,P2:B,P3:B", trial.State.Snapshot());
            Ship(trial, Settlement.B, Settlement.D, P3);
            Assert.AreEqual("P1:A,P2:B,P3:D", trial.State.Snapshot(), "P2 waits at B, which is allowed");
            Assert.AreEqual(2, trial.State.ShipmentCount);
            CollectionAssert.AreEqual(new[] { CorreoPackage.P2 }, trial.State.PackagesAt(Settlement.B));
        }

        [Test]
        public void APackageAtE_HasNoPathOnward_AndAnEmptyShipmentIsRefused()
        {
            var trial = Start();
            Play(trial, Solution.Take(2));

            Assert.AreEqual("no_path", Ship(trial, Settlement.E, Settlement.D, P1).Reason);
            Assert.AreEqual("no_packages", Ship(trial, Settlement.A, Settlement.B).Reason);
            Assert.AreEqual("no_path", Ship(trial, Settlement.A, Settlement.A, P2).Reason);
        }

        [Test]
        public void Priority_ALowPriorityPackageCannotReachEBeforeP1()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.C, P2);

            var outcome = Ship(trial, Settlement.C, Settlement.E, P2);

            Assert.IsFalse(outcome.Valid);
            Assert.AreEqual("priority_violation", outcome.Reason);
            Assert.AreEqual("constraint", outcome.Category);
            Assert.AreEqual(Settlement.C, trial.State.Location(P2), "P2 stays at C");
            Assert.IsFalse(trial.State.IsDelivered(P2));
            Assert.IsTrue(trial.State.PrioritySatisfied);
        }

        [Test]
        public void Priority_P3AlsoCannotReachEBeforeP1_ThroughD()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.B, P3);
            Ship(trial, Settlement.B, Settlement.D, P3);

            var outcome = Ship(trial, Settlement.D, Settlement.E, P3);

            Assert.AreEqual("priority_violation", outcome.Reason);
            Assert.AreEqual(Settlement.D, trial.State.Location(P3));
        }

        [Test]
        public void Priority_P1ArrivingInTheSameShipmentAsALowPriorityPackage_IsAllowed()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.C, P2);
            Ship(trial, Settlement.A, Settlement.C, P1);

            var outcome = Ship(trial, Settlement.C, Settlement.E, P1, P2);

            Assert.IsTrue(outcome.Valid, "the shipment counts are equal, which the constraint allows");
            Assert.AreEqual(3, trial.State.ArrivalShipment(P1));
            Assert.AreEqual(3, trial.State.ArrivalShipment(P2));
            Assert.IsTrue(trial.State.PrioritySatisfied);
        }

        [Test]
        public void Priority_P1ReachingEEarlier_IsAllowed_AndTheArrivalsAreRecorded()
        {
            var trial = Start();
            Play(trial, Solution);

            Assert.AreEqual(2, trial.State.ArrivalShipment(P1));
            Assert.AreEqual(5, trial.State.ArrivalShipment(P2));
            Assert.AreEqual(5, trial.State.ArrivalShipment(P3));
            Assert.IsTrue(trial.State.PrioritySatisfied);
        }

        [Test]
        public void Completion_CannotHappenWithALowPriorityPackageReachingETooEarly()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.C, P2);
            Ship(trial, Settlement.C, Settlement.E, P2);
            Ship(trial, Settlement.A, Settlement.B, P3);
            Ship(trial, Settlement.B, Settlement.D, P3);
            Ship(trial, Settlement.D, Settlement.E, P3);
            Ship(trial, Settlement.A, Settlement.C, P1);
            var last = Ship(trial, Settlement.C, Settlement.E, P1);

            Assert.IsTrue(last.Valid, "P1 itself may still travel");
            Assert.IsFalse(trial.State.AllDelivered, "P2 and P3 never reached E early");
            Assert.IsFalse(trial.IsCompleted);
            Assert.IsEmpty(sink.OfType("trial_completed"));
        }

        [Test]
        public void P1CannotWaitInD_TheNextShipmentHasToTakeItOn()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.B, P1);
            Assert.IsTrue(Ship(trial, Settlement.B, Settlement.D, P1).Valid, "passing through D is fine");
            var before = trial.State;

            var leaveWaiting = Ship(trial, Settlement.A, Settlement.B, P2);

            Assert.IsFalse(leaveWaiting.Valid);
            Assert.AreEqual("p1_waiting_in_d", leaveWaiting.Reason);
            Assert.AreEqual("constraint", leaveWaiting.Category);
            Assert.AreSame(before, trial.State);
            Assert.AreEqual("P1:D,P2:A,P3:A", trial.State.Snapshot());

            Assert.AreEqual("p1_waiting_in_d", Ship(trial, Settlement.A, Settlement.C, P3).Reason);
            Assert.IsTrue(Ship(trial, Settlement.D, Settlement.E, P1).Valid, "moving P1 on is accepted");
            Assert.IsTrue(Ship(trial, Settlement.A, Settlement.B, P2).Valid, "and then the others can go");
        }

        [Test]
        public void P1AndP3_CannotShareAShipment()
        {
            var trial = Start();

            var outcome = Ship(trial, Settlement.A, Settlement.B, P1, P3);

            Assert.IsFalse(outcome.Valid);
            Assert.AreEqual("p1_p3_shared", outcome.Reason);
            Assert.AreEqual("constraint", outcome.Category);
            Assert.AreEqual("P1:A,P2:A,P3:A", trial.State.Snapshot());

            Ship(trial, Settlement.A, Settlement.B, P1, P2);
            Assert.IsTrue(trial.State.IsAt(P1, Settlement.B), "P1 with P2 is fine");
        }

        [Test]
        public void P1AndP3_TogetherOnALaterLeg_AreAlsoRefused()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.B, P1);
            Ship(trial, Settlement.A, Settlement.B, P3);

            var outcome = Ship(trial, Settlement.B, Settlement.D, P1, P3);

            Assert.AreEqual("p1_p3_shared", outcome.Reason);
            Assert.AreEqual("P1:B,P2:A,P3:B", trial.State.Snapshot());
        }

        [Test]
        public void IncompleteState_IsNotComplete()
        {
            var trial = Start();
            Assert.IsFalse(trial.IsCompleted);

            Play(trial, Solution.Take(4));

            Assert.IsFalse(trial.State.AllDelivered);
            Assert.IsFalse(trial.IsCompleted);
            Assert.IsTrue(trial.IsRunning);
            Assert.AreEqual("in_progress", trial.BuildSummary().Status);
        }

        [Test]
        public void AllThreePackagesAtE_WithAValidHistory_CompletesTheTask()
        {
            var trial = Start();

            Play(trial, Solution);

            Assert.IsTrue(trial.IsCompleted);
            Assert.IsFalse(trial.IsRunning);
            Assert.AreEqual("P1:E,P2:E,P3:E", trial.State.Snapshot());
            Assert.IsTrue(CorreoRules.TryReplay(CorreoNetwork.Standard, trial.Shipments, out var replayed), "the recorded history obeys every rule");
            Assert.AreEqual(trial.State, replayed);
            var completed = sink.OfType("trial_completed").Single();
            Assert.AreEqual(5, F(completed, "shipment_count"));
            Assert.AreEqual(true, F(completed, "priority_satisfied"));
            Assert.AreEqual(1, sink.OfType("trial_completed").Count());
            Assert.IsFalse(Ship(trial, Settlement.A, Settlement.B, P1).Accepted, "nothing is accepted after completion");
        }

        [Test]
        public void Completion_DependsOnTheLogicalState_NotOnTheOrderOfARouteThroughDOrC()
        {
            var trial = Start();
            Play(trial, new[]
            {
                (Settlement.A, Settlement.C, new[] { P1 }),
                (Settlement.C, Settlement.E, new[] { P1 }),
                (Settlement.A, Settlement.B, new[] { P2, P3 }),
                (Settlement.B, Settlement.D, new[] { P2, P3 }),
                (Settlement.D, Settlement.E, new[] { P2 }),
                (Settlement.D, Settlement.E, new[] { P3 }),
            });

            Assert.IsTrue(trial.IsCompleted);
            Assert.AreEqual(6, trial.State.ShipmentCount);
        }

        [Test]
        public void Determinism_TheSameActionsGiveTheSameStateEventsAndSummary()
        {
            var first = Start();
            Play(first, Solution);
            var firstEvents = sink.Events.Select(e => e.EventType + "|" + string.Join(";", e.Fields.Select(f => f.Key + "=" + Flatten(f.Value)))).ToList();
            sink.Events.Clear();
            clock = new FakeClock();
            var second = Start();
            Play(second, Solution);
            var secondEvents = sink.Events.Select(e => e.EventType + "|" + string.Join(";", e.Fields.Select(f => f.Key + "=" + Flatten(f.Value)))).ToList();

            Assert.AreEqual(first.State, second.State);
            Assert.AreEqual(first.State.Snapshot(), second.State.Snapshot());
            CollectionAssert.AreEqual(first.ActionSequence, second.ActionSequence);
            CollectionAssert.AreEqual(firstEvents, secondEvents);
            Assert.AreEqual(first.BuildSummary().ToJson(new SessionInfo("s", "p", "Static")), second.BuildSummary().ToJson(new SessionInfo("s", "p", "Static")));
        }

        static string Flatten(object value) => value is System.Collections.IEnumerable seq && !(value is string)
            ? "[" + string.Join(",", seq.Cast<object>()) + "]"
            : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void Determinism_TheOrderOfPackagesInASelectionDoesNotMatter()
        {
            var a = new CorreoShipment(Settlement.A, Settlement.B, new[] { P3, P2 });
            var b = new CorreoShipment(Settlement.A, Settlement.B, new[] { P2, P3, P2 });

            Assert.AreEqual(a.Label, b.Label);
            Assert.AreEqual("A>B:P2+P3", a.Label);
            Assert.AreEqual(CorreoState.Initial.Apply(a), CorreoState.Initial.Apply(b));
        }

        [Test]
        public void EveryReachableState_CanStillFinish_SoTheParticipantCanNeverGetStuck_AndFiveShipmentsIsTheFewest()
        {
            var network = CorreoNetwork.Standard;
            var seen = new Dictionary<CorreoState, int>();
            var queue = new Queue<CorreoState>();
            seen[CorreoState.Initial] = 0;
            queue.Enqueue(CorreoState.Initial);
            var all = ShipmentsUpToCapacity(network).ToList();
            var deadEnds = 0;
            var shortest = int.MaxValue;
            var graph = new Dictionary<CorreoState, List<CorreoState>>();

            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                var next = new List<CorreoState>();
                foreach (var shipment in all)
                {
                    if (!CorreoRules.Evaluate(network, state, shipment).Valid) continue;
                    var after = state.Apply(shipment);
                    next.Add(after);
                    if (seen.ContainsKey(after)) continue;
                    seen[after] = seen[state] + 1;
                    queue.Enqueue(after);
                }
                graph[state] = next;
                if (state.AllDelivered) shortest = Math.Min(shortest, seen[state]);
                else if (next.Count == 0) deadEnds++;
            }

            Assert.AreEqual(0, deadEnds, "every unfinished state has a valid shipment (packages only move forward)");
            Assert.AreEqual(5, shortest);
            Assert.IsTrue(seen.Keys.All(s => s.PrioritySatisfied), "no reachable state breaks the priority constraint");
        }

        static IEnumerable<CorreoShipment> ShipmentsUpToCapacity(CorreoNetwork network)
        {
            var subsets = new List<List<CorreoPackage>>();
            for (var mask = 1; mask < 8; mask++)
                subsets.Add(CorreoPackages.All.Where(p => (mask & (1 << (int)p)) != 0).ToList());
            foreach (var from in Settlements.All)
                foreach (var to in Settlements.All)
                    foreach (var subset in subsets)
                        yield return new CorreoShipment(from, to, subset);
        }

        [Test]
        public void ShipmentAttemptEvent_CarriesEverythingNeededToReconstructTheDecision()
        {
            var trial = Start(index: 2);
            Ship(trial, Settlement.A, Settlement.B, P2);
            clock.Advance(4.5);

            trial.Ship(Settlement.B, Settlement.D, new[] { P2, P3 });
            var e = sink.OfType("shipment_attempt").Last();

            Assert.AreEqual("ElCorreo", e.Task);
            Assert.AreEqual(2, F(e, "trial_index"));
            Assert.AreEqual(2, F(e, "attempt_number"));
            Assert.AreEqual("B", F(e, "source"));
            Assert.AreEqual("D", F(e, "destination"));
            CollectionAssert.AreEqual(new[] { "P2", "P3" }, (IEnumerable<string>)F(e, "selected_packages"));
            Assert.AreEqual(2, F(e, "selected_count"));
            Assert.AreEqual(2, F(e, "path_capacity"));
            Assert.AreEqual(false, F(e, "valid"));
            Assert.AreEqual("package_not_at_source", F(e, "outcome"));
            Assert.AreEqual("selection", F(e, "rejection_category"));
            Assert.AreEqual(1, F(e, "shipment_number"));
            Assert.AreEqual("P1:A,P2:B,P3:A", F(e, "locations_before"));
            Assert.AreEqual("P1:A,P2:B,P3:A", F(e, "locations_after"));
            Assert.AreEqual("in_progress", F(e, "completion_status"));
            Assert.AreEqual(4.5, (double)F(e, "elapsed_s"), 1e-9);
        }

        [Test]
        public void ValidShipmentEvent_HasBeforeAndAfterLocations_ShipmentNumberAndDeliveries()
        {
            var trial = Start();
            Ship(trial, Settlement.A, Settlement.C, P1);
            Ship(trial, Settlement.C, Settlement.E, P1);
            var e = sink.OfType("shipment_attempt").Last();

            Assert.AreEqual(true, F(e, "valid"));
            Assert.IsNull(F(e, "rejection_category"));
            Assert.AreEqual("shipped", F(e, "outcome"));
            Assert.AreEqual(2, F(e, "shipment_number"));
            Assert.AreEqual("P1:C,P2:A,P3:A", F(e, "locations_before"));
            Assert.AreEqual("P1:E,P2:A,P3:A", F(e, "locations_after"));
            CollectionAssert.AreEqual(new[] { "P1" }, (IEnumerable<string>)F(e, "delivered_packages"));
        }

        [Test]
        public void Selection_TapsChooseSourceDestinationAndPackages_WithoutCheckingAnything()
        {
            var trial = Start();

            trial.TapSettlement(Settlement.A);
            trial.TapSettlement(Settlement.C);
            trial.TogglePackage(P2);
            trial.TogglePackage(P3);
            trial.TogglePackage(P3);

            Assert.AreEqual(Settlement.A, trial.SelectedSource);
            Assert.AreEqual(Settlement.C, trial.SelectedDestination);
            CollectionAssert.AreEqual(new[] { P2 }, trial.SelectedPackages);
            CollectionAssert.AreEqual(new[] { "source_selected", "destination_selected", "package_added", "package_added", "package_removed" },
                sink.OfType("selection_changed").Select(e => (string)F(e, "change")).ToArray());

            trial.TapSettlement(Settlement.E);
            Assert.AreEqual(Settlement.E, trial.SelectedDestination, "a destination with no path can be chosen; the participant finds out on sending");
            trial.TapSettlement(Settlement.E);
            Assert.IsNull(trial.SelectedDestination);
            trial.TapSettlement(Settlement.A);
            Assert.IsNull(trial.SelectedSource);
            Assert.IsNull(trial.SelectedDestination);
        }

        [Test]
        public void Confirm_ShipsTheSelection_ClearsIt_AndAnInvalidOneLeavesTheStateAlone()
        {
            var trial = Start();
            trial.TapSettlement(Settlement.A);
            trial.TapSettlement(Settlement.C);
            trial.TogglePackage(P2);
            trial.TogglePackage(P3);
            var before = trial.State;

            var rejected = trial.Confirm();

            Assert.IsTrue(rejected.Accepted);
            Assert.IsFalse(rejected.Valid);
            Assert.AreEqual("capacity_exceeded", rejected.Reason);
            Assert.AreSame(before, trial.State);
            Assert.IsNull(trial.SelectedSource);
            Assert.AreEqual(0, trial.SelectedPackages.Count);

            trial.TapSettlement(Settlement.A);
            trial.TapSettlement(Settlement.C);
            trial.TogglePackage(P1);
            var accepted = trial.Confirm();

            Assert.IsTrue(accepted.Valid);
            Assert.AreEqual(Settlement.C, trial.State.Location(P1));
        }

        [Test]
        public void Confirm_WithAnIncompleteSelection_AttemptsNothing()
        {
            var trial = Start();
            trial.TapSettlement(Settlement.A);
            trial.TogglePackage(P2);

            var outcome = trial.Confirm();

            Assert.IsFalse(outcome.Accepted);
            Assert.AreEqual(0, trial.ShipmentAttempts);
            Assert.AreEqual(0, trial.InvalidShipments);
            Assert.AreEqual("incomplete_selection", F(sink.OfType("confirm_ignored").Single(), "reason"));
            Assert.AreEqual(Settlement.A, trial.SelectedSource, "the selection is kept so the participant can finish it");
        }

        [Test]
        public void Summary_ReportsEveryRequiredMetric()
        {
            var trial = Start();
            clock.Advance(2);
            Ship(trial, Settlement.A, Settlement.C, P2, P3);
            clock.Advance(3);
            Ship(trial, Settlement.B, Settlement.C, P2);
            Ship(trial, Settlement.A, Settlement.B, P1, P3);
            Ship(trial, Settlement.A, Settlement.C, P2);
            Ship(trial, Settlement.C, Settlement.E, P2);
            Ship(trial, Settlement.A, Settlement.C, P1);
            Ship(trial, Settlement.D, Settlement.E, P3);
            clock.Advance(5);
            Ship(trial, Settlement.C, Settlement.E, P1);
            clock.Advance(1);
            Ship(trial, Settlement.A, Settlement.C, P3);
            Ship(trial, Settlement.C, Settlement.E, P2, P3);
            Ship(trial, Settlement.A, Settlement.B, P3);
            var summary = trial.BuildSummary();

            Assert.AreEqual(10, summary.ShipmentAttempts);
            Assert.AreEqual(5, summary.ValidShipments);
            Assert.AreEqual(5, summary.InvalidShipments);
            Assert.AreEqual(5, summary.ShipmentCount);
            Assert.AreEqual(1, summary.RejectedCapacityAttempts);
            Assert.AreEqual(2, summary.RejectedConstraintAttempts, "P1 with P3, and P2 reaching E before P1");
            Assert.AreEqual(1, summary.RejectedPathAttempts);
            Assert.AreEqual(1, summary.RejectedSelectionAttempts);
            Assert.AreEqual("P1:E,P2:E,P3:E", summary.FinalLocations);
            Assert.AreEqual(3, summary.P1ArrivalShipment);
            Assert.AreEqual(5, summary.P2ArrivalShipment);
            Assert.AreEqual(5, summary.P3ArrivalShipment);
            Assert.AreEqual(10, summary.P1ArrivalTimeSeconds.Value, 1e-9);
            Assert.AreEqual(11, summary.P2ArrivalTimeSeconds.Value, 1e-9);
            Assert.AreEqual(11, summary.P3ArrivalTimeSeconds.Value, 1e-9);
            Assert.IsTrue(summary.PrioritySatisfied);
            Assert.AreEqual("completed", summary.Status);
            Assert.IsTrue(summary.Completed);
            Assert.AreEqual(11, summary.CompletionTimeSeconds.Value, 1e-9);
            Assert.AreEqual(2, summary.FirstActionLatencySeconds.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { "A>C:P2", "A>C:P1", "C>E:P1", "A>C:P3", "C>E:P2+P3" }, summary.ShipmentSequence);
            Assert.AreEqual(10, summary.ActionSequence.Count, "every recorded attempt, valid or refused; the one after completion is not accepted");
            Assert.AreEqual("A>B:P1+P3:rejected:p1_p3_shared", summary.ActionSequence[2]);
        }

        [Test]
        public void TimeLimit_EndsTheTrialAsIncomplete_AndWritesASummaryWithoutCompletion()
        {
            var trial = Start(durationSeconds: 60);
            Ship(trial, Settlement.A, Settlement.C, P1);

            clock.Advance(59);
            Assert.IsFalse(trial.CheckTimeLimit());
            clock.Advance(2);
            Assert.IsTrue(trial.CheckTimeLimit());

            Assert.IsFalse(trial.IsRunning);
            Assert.IsFalse(trial.IsCompleted);
            var summary = sink.OfType("trial_summary").Single();
            Assert.AreEqual("incomplete", F(summary, "completion_status"));
            Assert.AreEqual("time_limit", F(summary, "end_reason"));
            Assert.AreEqual("P1:C,P2:A,P3:A", F(summary, "final_locations"));
            Assert.IsNull(F(summary, "completion_time_s"));
            Assert.IsFalse(trial.Ship(Settlement.C, Settlement.E, new[] { P1 }).Accepted);
            Assert.IsFalse(trial.TapSettlement(Settlement.A));
        }

        [Test]
        public void SummaryJson_AddsTheSessionFields_AndUsesTheSameNamesAsTheEventLog()
        {
            var trial = Start();
            Play(trial, Solution);

            var json = trial.BuildSummary().ToJson(new SessionInfo("S-1", "P-7", "PreAdapted"));

            StringAssert.StartsWith("{\"session_id\":\"S-1\",\"participant_id\":\"P-7\",\"condition\":\"PreAdapted\",\"task\":\"ElCorreo\"", json);
            foreach (var key in new[]
            {
                "shipment_attempts", "valid_shipments", "invalid_shipments", "shipment_count", "rejected_capacity_attempts", "rejected_constraint_attempts",
                "final_locations", "p1_arrival_shipment", "p2_arrival_shipment", "p3_arrival_shipment", "priority_satisfied", "completion_time_s",
                "completion_status", "shipment_sequence", "action_sequence",
            })
                StringAssert.Contains($"\"{key}\":", json);
            StringAssert.Contains("\"final_locations\":\"P1:E,P2:E,P3:E\"", json);
            StringAssert.Contains("\"shipment_sequence\":[\"A>C:P1\",\"C>E:P1\",\"A>C:P2\",\"A>C:P3\",\"C>E:P2+P3\"]", json);
        }

        [Test]
        public void ANewTrial_StartsClean()
        {
            var first = Start();
            Play(first, Solution);
            var second = Start(index: 2);

            Assert.AreEqual(CorreoState.Initial, second.State);
            Assert.AreEqual(0, second.ShipmentAttempts);
            Assert.IsTrue(second.IsRunning);
        }
    }
}
