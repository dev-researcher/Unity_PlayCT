using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Correo;
using PlayCT.Tasks.Cubo;
using PlayCT.Tasks.Gabinete;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    public class JsonLineReaderTests
    {
        [Test]
        public void ReadsWhatJsonLineWrites_AndWritesItBackIdentically()
        {
            var fields = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("text", "comillas \" barra \\ salto \n tab \t año ¿qué?"),
                new KeyValuePair<string, object>("int", 42),
                new KeyValuePair<string, object>("long", 9007199254740993L),
                new KeyValuePair<string, object>("double", 12.5),
                new KeyValuePair<string, object>("small", 1E-05),
                new KeyValuePair<string, object>("flag", true),
                new KeyValuePair<string, object>("none", null),
                new KeyValuePair<string, object>("list", new List<string> { "A>C:P1", "x,y" }),
                new KeyValuePair<string, object>("empty", new List<string>()),
                new KeyValuePair<string, object>("time", new DateTime(2026, 1, 15, 9, 0, 1, 250, DateTimeKind.Utc)),
            };
            var line = JsonLine.Serialize(fields);

            Assert.IsTrue(JsonLineReader.TryParseObject(line, out var parsed));

            Assert.AreEqual("comillas \" barra \\ salto \n tab \t año ¿qué?", parsed[0].Value);
            Assert.AreEqual(42L, parsed[1].Value);
            Assert.AreEqual(9007199254740993L, parsed[2].Value);
            Assert.AreEqual(12.5, parsed[3].Value);
            Assert.AreEqual(1E-05, parsed[4].Value);
            Assert.AreEqual(true, parsed[5].Value);
            Assert.IsNull(parsed[6].Value);
            CollectionAssert.AreEqual(new object[] { "A>C:P1", "x,y" }, (List<object>)parsed[7].Value);
            Assert.AreEqual("2026-01-15T09:00:01.250Z", parsed[9].Value);
            Assert.AreEqual(line, JsonLine.Serialize(parsed), "reading loses nothing");
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase("{\"a\":1")]
        [TestCase("{\"a\":1} trailing")]
        [TestCase("{\"a\":\"unterminated}")]
        [TestCase("[1,2]")]
        public void MalformedLines_AreRejected(string line)
        {
            Assert.IsFalse(JsonLineReader.TryParseObject(line, out var fields));
            Assert.IsNull(fields);
        }

        [Test]
        public void EventLogReader_SkipsBlankAndBrokenLines_AndCountsTheBrokenOnes()
        {
            var lines = new[] { "{\"task\":\"Hanoi\",\"event\":\"a\"}", "", "{broken", "{\"task\":\"Hanoi\",\"event\":\"b\"}" };

            var events = EventLogReader.Read(lines, out var skipped);

            Assert.AreEqual(2, events.Count);
            Assert.AreEqual(1, skipped);
            Assert.AreEqual(4, events[1].LineNumber);
        }

        [Test]
        public void EveryLineOfARealSession_RoundTripsByteForByte()
        {
            var fixture = SessionFixture.FullSession();

            foreach (var line in fixture.Lines())
            {
                Assert.IsTrue(JsonLineReader.TryParseObject(line, out var fields), line);
                Assert.AreEqual(line, JsonLine.Serialize(fields));
            }
        }
    }

    public class SessionAggregationTests
    {
        static SessionSummary Aggregate(SessionFixture fixture, SessionAggregator aggregator = null)
        {
            var events = EventLogReader.Read(fixture.Lines(), out var skipped);
            return (aggregator ?? ResearchTaskCatalog.CreateAggregator()).Aggregate(events, skipped);
        }

        static TrialRecord Record(SessionSummary summary, string task, int? trial = 1) =>
            summary.Records.Single(r => r.TaskId == task && r.Trial == trial);

        [Test]
        public void ASessionWithFourTasks_AggregatesToOneRecordPerTaskTrial_InSessionOrder()
        {
            var summary = Aggregate(SessionFixture.FullSession());

            CollectionAssert.AreEqual(new[] { "Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo" }, summary.Records.Select(r => r.TaskId));
            Assert.IsTrue(summary.Records.All(r => r.SessionId == "S-1" && r.ParticipantId == "P-07" && r.Condition == "PreAdapted"));
            Assert.IsTrue(summary.Records.All(r => r.Trial == 1 && r.Completed == true && r.CompletionStatus == "completed"));
            Assert.IsTrue(summary.Records.All(r => r.Source == "trial_summary"));
        }

        [Test]
        public void SessionLevel_HasIdentityTaskSequenceOutcomeAndTimes()
        {
            var fixture = SessionFixture.FullSession();
            var summary = Aggregate(fixture);

            Assert.AreEqual("S-1", summary.SessionId);
            Assert.AreEqual("P-07", summary.ParticipantId);
            Assert.AreEqual("PreAdapted", summary.Condition);
            CollectionAssert.AreEqual(new[] { "Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo" }, summary.TaskSequence);
            Assert.AreEqual("completed", summary.ExperimentStatus);
            Assert.AreEqual(fixture.Lines().Length, summary.EventCount);
            Assert.AreEqual(new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc), summary.StartTimeUtc);
            Assert.AreEqual(fixture.Clock.Now, summary.EndTimeUtc);
            Assert.AreEqual((fixture.Clock.Now - summary.StartTimeUtc.Value).TotalSeconds, summary.DurationSeconds.Value, 1e-3);
        }

        [Test]
        public void Hanoi_MapsItsOwnNamesToTheCommonMetrics_AndKeepsTheRest()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("Hanoi");
            fixture.StartTask("Hanoi", 0);
            var trial = fixture.PlayHanoi(3);
            var real = trial.BuildSummary();
            var summary = Aggregate(fixture);

            var r = Record(summary, "Hanoi");

            Assert.AreEqual(real.AttemptCount, r.Attempts);
            Assert.AreEqual(7, r.ValidActions);
            Assert.AreEqual(7, r.Moves);
            Assert.AreEqual(real.InvalidAttempts, r.InvalidActions);
            Assert.AreEqual(real.Efficiency.Value, r.Efficiency.Value, 1e-9);
            Assert.AreEqual(real.AttemptSequence.Count, r.ActionSequence.Count);
            Assert.AreEqual(real.CompletionTimeSeconds.Value, r.CompletionTimeSeconds.Value, 1e-9);
            Assert.AreEqual(real.ElapsedSeconds, r.DurationSeconds.Value, 1e-9);
            var specific = r.TaskSpecific.ToDictionary(f => f.Key, f => f.Value);
            Assert.AreEqual(3L, specific["disk_count"]);
            Assert.AreEqual(7L, specific["optimal_moves"]);
            Assert.IsTrue(specific.ContainsKey("move_sequence"));
            Assert.IsFalse(specific.ContainsKey("total_moves"), "mapped fields are not repeated");
            Assert.IsFalse(specific.ContainsKey("session_id"));
        }

        [Test]
        public void EachTaskKeepsMetricsTheOthersDoNotHave()
        {
            var summary = Aggregate(SessionFixture.FullSession());

            var hanoi = Record(summary, "Hanoi");
            var cubo = Record(summary, "CuboRelaciones");
            var gabinete = Record(summary, "GabineteFormas");
            var correo = Record(summary, "ElCorreo");

            Assert.IsNotNull(hanoi.Efficiency);
            Assert.IsNull(cubo.Efficiency, "Cubo does not log an efficiency and none is invented");
            Assert.IsNull(gabinete.Efficiency);
            Assert.IsNull(correo.Efficiency);
            Assert.AreEqual(6, cubo.Attempts);
            Assert.AreEqual(4, cubo.ValidActions);
            Assert.AreEqual(1, cubo.InvalidActions);
            CollectionAssert.Contains(cubo.TaskSpecific.Select(f => f.Key), "reference_moves");
            Assert.AreEqual(7, gabinete.Attempts);
            Assert.AreEqual(4, gabinete.ValidActions);
            Assert.AreEqual(3, gabinete.InvalidActions);
            CollectionAssert.Contains(gabinete.TaskSpecific.Select(f => f.Key), "total_rotation_deg");
            Assert.AreEqual(6, correo.Attempts);
            Assert.AreEqual(5, correo.ValidActions);
            Assert.AreEqual(1, correo.InvalidActions);
            Assert.AreEqual(5, correo.Moves);
            var correoSpecific = correo.TaskSpecific.ToDictionary(f => f.Key, f => f.Value);
            Assert.AreEqual(2L, correoSpecific["p1_arrival_shipment"]);
            Assert.AreEqual(5L, correoSpecific["p2_arrival_shipment"]);
            Assert.AreEqual(true, correoSpecific["priority_satisfied"]);
            Assert.AreEqual("P1:E,P2:E,P3:E", correoSpecific["final_locations"]);
            Assert.AreEqual(5, correo.ActionSequence.Count(a => !a.Contains("rejected")) );
        }

        [Test]
        public void StartAndEndTimes_ComeFromTheTrialStartedAndTrialSummaryEvents()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("Hanoi", "ElCorreo");
            fixture.StartTask("Hanoi", 0);
            fixture.Clock.Advance(5);
            var startedAt = fixture.Clock.Now;
            fixture.PlayHanoi(3);
            var hanoiEnd = fixture.Clock.Now;
            fixture.EndTask("Hanoi", 0, "completed", 20);
            var summary = Aggregate(fixture);

            var r = Record(summary, "Hanoi");

            Assert.AreEqual(startedAt, r.StartTimeUtc);
            Assert.AreEqual(hanoiEnd, r.EndTimeUtc);
            Assert.AreEqual((hanoiEnd - startedAt).TotalSeconds, r.DurationSeconds.Value, 1e-3);
        }

        [Test]
        public void SeveralTrialsOfOneTask_GiveSeveralRecords()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("Hanoi");
            fixture.StartTask("Hanoi", 0);
            fixture.PlayHanoi(3, trialIndex: 1);
            fixture.Clock.Advance(10);
            fixture.PlayHanoi(4, trialIndex: 2);
            fixture.EndTask("Hanoi", 0, "completed", 90);
            var summary = Aggregate(fixture);

            Assert.AreEqual(2, summary.Records.Count);
            CollectionAssert.AreEqual(new int?[] { 1, 2 }, summary.Records.Select(r => r.Trial));
            Assert.Greater(summary.Records[1].StartTimeUtc, summary.Records[0].EndTimeUtc);
            Assert.AreEqual(4L, summary.Records[1].TaskSpecific.Single(f => f.Key == "disk_count").Value);
        }

        [Test]
        public void AnIncompleteTrial_IsRecordedAsNotCompleted_WithItsEndReason()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("Hanoi");
            fixture.StartTask("Hanoi", 0);
            var trial = new HanoiTrial(3, fixture.Writer, fixture.Clock);
            trial.Begin();
            fixture.Clock.Advance(4);
            trial.OnGrab(1);
            trial.OnRelease(1, HanoiPegs.Destino);
            trial.End("time_limit");
            fixture.EndTask("Hanoi", 0, "aborted", 4);
            var summary = Aggregate(fixture);

            var r = Record(summary, "Hanoi");

            Assert.AreEqual(false, r.Completed);
            Assert.AreEqual("incomplete", r.CompletionStatus);
            Assert.IsNull(r.CompletionTimeSeconds);
            Assert.AreEqual("time_limit", r.TaskSpecific.Single(f => f.Key == "end_reason").Value);
        }

        [Test]
        public void ATaskThatWroteNoTrialSummary_GetsOneRecordFromItsTaskEvents()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("Hanoi", "ElCorreo");
            fixture.StartTask("Hanoi", 0);
            fixture.Clock.Advance(8);
            fixture.EndTask("Hanoi", 0, "aborted", 8);
            fixture.StartTask("ElCorreo", 1);
            var summary = Aggregate(fixture);

            var hanoi = summary.Records.Single(r => r.TaskId == "Hanoi");
            var correo = summary.Records.Single(r => r.TaskId == "ElCorreo");

            Assert.AreEqual("task_events", hanoi.Source);
            Assert.IsNull(hanoi.Trial);
            Assert.AreEqual(false, hanoi.Completed);
            Assert.AreEqual("aborted", hanoi.CompletionStatus);
            Assert.AreEqual(8, hanoi.DurationSeconds.Value, 1e-9);
            Assert.IsNull(hanoi.Attempts, "no metric is made up");
            Assert.AreEqual("not_ended", correo.CompletionStatus);
            Assert.IsNull(correo.EndTimeUtc);
        }

        [Test]
        public void AnUnknownTask_IsStillAggregated_WithItsEnvelopeTimingStatusAndFieldsKept()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("NuevaTarea");
            fixture.StartTask("NuevaTarea", 0);
            fixture.Writer.Log(new ResearchEvent("NuevaTarea", "trial_started").Add("trial_index", 1));
            fixture.Clock.Advance(6);
            fixture.Writer.Log(new ResearchEvent("NuevaTarea", "trial_summary").Add("trial_index", 1).Add("completion_status", "completed")
                .Add("elapsed_s", 6.0).Add("puntos_de_control", 3).Add("pasos", new[] { "a", "b" }));
            var summary = Aggregate(fixture);

            var r = summary.Records.Single();

            Assert.AreEqual("NuevaTarea", r.TaskId);
            Assert.AreEqual(true, r.Completed);
            Assert.AreEqual(6.0, r.DurationSeconds.Value, 1e-9);
            Assert.IsNull(r.Attempts);
            CollectionAssert.AreEqual(new[] { "puntos_de_control", "pasos" }, r.TaskSpecific.Select(f => f.Key));
        }

        [Test]
        public void ANewTaskIsAddedByRegisteringAnAdapter()
        {
            var fixture = new SessionFixture();
            fixture.StartExperiment("NuevaTarea");
            fixture.StartTask("NuevaTarea", 0);
            fixture.Writer.Log(new ResearchEvent("NuevaTarea", "trial_summary").Add("trial_index", 1).Add("completion_status", "completed")
                .Add("elapsed_s", 6.0).Add("intentos", 9).Add("aciertos", 7).Add("fallos", 2).Add("otro", "x"));
            var aggregator = ResearchTaskCatalog.CreateAggregator();
            aggregator.Register(new MappedTaskMetricsAdapter("NuevaTarea", "intentos", "aciertos", "fallos", null, null, null, new[] { "otro" }));

            var r = Aggregate(fixture, aggregator).Records.Single();

            Assert.AreEqual(9, r.Attempts);
            Assert.AreEqual(7, r.ValidActions);
            Assert.AreEqual(2, r.InvalidActions);
            Assert.IsNull(r.Moves);
            CollectionAssert.AreEqual(new[] { "otro" }, r.TaskSpecific.Select(f => f.Key));
        }

        [Test]
        public void RegisteringTheSameTaskAgain_ReplacesTheEarlierAdapter()
        {
            var aggregator = ResearchTaskCatalog.CreateAggregator();
            var count = aggregator.Adapters.Count;

            aggregator.Register(new GenericTaskMetricsAdapter("Hanoi"));

            Assert.AreEqual(count, aggregator.Adapters.Count);
            Assert.IsInstanceOf<GenericTaskMetricsAdapter>(aggregator.Adapters.Single(a => a.TaskId == "Hanoi"));
        }

        [Test]
        public void GuideEvents_AreNotTrialRecords_AndOtherEventsDoNotBecomeSummaries()
        {
            var fixture = SessionFixture.FullSession();
            fixture.Writer.Log(new ResearchEvent("Hanoi", "guide_shown").Add("trial_index", 1).Add("guide_type", "help_request").Add("message_id", "x"));

            var summary = Aggregate(fixture);

            Assert.AreEqual(4, summary.Records.Count);
        }

        [Test]
        public void TheDeclaredExtraFieldsOfEveryTask_MatchTheFieldsItsRealSummaryWrites()
        {
            var samples = new Dictionary<string, ResearchEvent>();
            samples["Hanoi"] = Sample(HanoiTrial.TaskName, e => new HanoiSummary().AddTo(e), incomplete: true);
            samples["CuboRelaciones"] = Sample(CuboTrial.TaskName, e => new CuboSummary().AddTo(e), incomplete: true);
            samples["GabineteFormas"] = Sample(GabineteTrial.TaskName, e => new GabineteSummary().AddTo(e), incomplete: true);
            samples["ElCorreo"] = Sample(CorreoTrial.TaskName, e => new CorreoSummary().AddTo(e), incomplete: true);

            foreach (var adapter in ResearchTaskCatalog.MetricsAdapters())
            {
                var line = JsonLine.Serialize(new[] { new KeyValuePair<string, object>("task", adapter.TaskId) }.Concat(samples[adapter.TaskId].Fields));
                Assert.IsTrue(JsonLineReader.TryParseObject(line, out var fields));
                var logged = new LoggedEvent(fields, 1);
                var metrics = adapter.Extract(logged);
                var consumed = new HashSet<string>(LoggedEvent.EnvelopeFields.Concat(new[] { "trial_index", "completion_status", "completion_time_s", "elapsed_s" }).Concat(metrics.ConsumedFields));

                var specific = logged.Fields.Select(f => f.Key).Where(k => !consumed.Contains(k)).ToList();

                CollectionAssert.IsEmpty(specific.Except(adapter.ExtraFields), $"{adapter.TaskId}: fields the adapter does not declare");
                CollectionAssert.IsEmpty(adapter.ExtraFields.Except(specific.Concat(new[] { "end_reason" })), $"{adapter.TaskId}: declared fields the summary never writes");
                foreach (var mapped in metrics.ConsumedFields) Assert.IsTrue(logged.Has(mapped), $"{adapter.TaskId} maps '{mapped}' but its summary has no such field");
            }
        }

        static ResearchEvent Sample(string task, Action<ResearchEvent> fill, bool incomplete)
        {
            var e = new ResearchEvent(task, "trial_summary");
            fill(e);
            if (incomplete) e.Add("end_reason", "time_limit");
            return e;
        }

        [Test]
        public void Aggregation_DoesNotChangeTheRawEvents()
        {
            var fixture = SessionFixture.FullSession();
            var before = fixture.Lines();
            var events = EventLogReader.Read(before, out _);

            ResearchTaskCatalog.CreateAggregator().Aggregate(events);

            CollectionAssert.AreEqual(before, fixture.Lines());
            CollectionAssert.AreEqual(before, events.Select(e => JsonLine.Serialize(e.Fields)));
        }

        [Test]
        public void Aggregation_IsDeterministic()
        {
            var a = Aggregate(SessionFixture.FullSession());
            var b = Aggregate(SessionFixture.FullSession());

            Assert.AreEqual(SessionCsvExporter.BuildSessionSummaryCsv(a), SessionCsvExporter.BuildSessionSummaryCsv(b));
        }
    }
}
