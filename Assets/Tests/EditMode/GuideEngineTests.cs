using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;
using PlayCT.Tasks.Hanoi;

namespace PlayCT.Tests
{
    public sealed class RecordingPresenter : IGuidePresenter
    {
        public readonly List<(GuideMessage message, string task, int? trial)> Shown = new List<(GuideMessage, string, int?)>();

        public void Present(GuideMessage message, string taskId, int? trial) => Shown.Add((message, taskId, trial));
    }

    public class GuideEngineTests
    {
        const string Task = "Hanoi";

        sealed class Rig
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly MemorySink Sink = new MemorySink();
            public readonly RecordingPresenter Presenter = new RecordingPresenter();
            public readonly GuideEngine Engine;

            public Rig(params GuideProfile[] profiles) => Engine = new GuideEngine(Sink, Clock, Presenter, profiles);

            public void TaskStarted(string task = Task) =>
                Engine.OnEvent(new ResearchEvent(GuideEngine.ExperimentTask, "task_started").Add("task_id", task));

            public void TaskEnded(string status, string task = Task) =>
                Engine.OnEvent(new ResearchEvent(GuideEngine.ExperimentTask, "task_ended").Add("task_id", task).Add("status", status));

            public void TrialStarted(int index, string task = Task) =>
                Engine.OnEvent(new ResearchEvent(task, "trial_started").Add("trial_index", index));

            public void TrialSummary(string status, string task = Task) =>
                Engine.OnEvent(new ResearchEvent(task, "trial_summary").Add("completion_status", status));

            public IEnumerable<string> ShownIds() => Presenter.Shown.Select(s => s.message.Id);
        }

        [Test]
        public void TaskStart_ShowsAStandardMessageAndLogsIt()
        {
            var rig = new Rig();
            rig.TaskStarted();

            Assert.AreEqual(1, rig.Presenter.Shown.Count);
            Assert.AreEqual(GuideMoment.TaskStart, rig.Presenter.Shown[0].message.Moment);
            var logged = rig.Sink.OfType(GuideEngine.ShownEvent).Single();
            Assert.AreEqual(Task, logged.Task);
            Assert.AreEqual("task_start", MemorySink.Field(logged, "guide_type"));
            Assert.AreEqual("common.task_start.1", MemorySink.Field(logged, "message_id"));
            Assert.AreEqual(rig.Presenter.Shown[0].message.Text, MemorySink.Field(logged, "text"));
        }

        [Test]
        public void TrialStart_ShowsAMessageForThatTrial()
        {
            var rig = new Rig();
            rig.TaskStarted();
            rig.TrialStarted(2);

            var (message, task, trial) = rig.Presenter.Shown.Last();
            Assert.AreEqual(GuideMoment.TrialStart, message.Moment);
            Assert.AreEqual(Task, task);
            Assert.AreEqual(2, trial);
            Assert.AreEqual(2, MemorySink.Field(rig.Sink.OfType(GuideEngine.ShownEvent).Last(), "trial_index"));
        }

        [Test]
        public void HelpRequest_UsesTheTaskMessagesInOrderAndCyclesDeterministically()
        {
            var profile = HanoiResearchProfile.Guide();
            profile.HelpCooldownSeconds = 0;
            var rig = new Rig(profile);
            rig.TaskStarted();
            rig.TrialStarted(1);

            for (var i = 0; i < 5; i++) Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());

            var help = rig.Presenter.Shown.Where(s => s.message.Moment == GuideMoment.HelpRequest).Select(s => s.message.Id).ToList();
            CollectionAssert.AreEqual(new[] { "hanoi.help.1", "hanoi.help.2", "hanoi.help.3", "hanoi.help.4", "hanoi.help.1" }, help);
        }

        [Test]
        public void TwoRunsWithTheSameEvents_ShowTheSameMessages()
        {
            List<string> Run()
            {
                var rig = new Rig(ResearchTaskCatalog.GuideProfiles().ToArray());
                rig.TaskStarted();
                rig.TrialStarted(1);
                rig.Engine.RequestHelp();
                rig.Clock.Advance(60);
                rig.Engine.RequestHelp();
                rig.TrialSummary("completed");
                rig.TaskEnded("completed");
                return rig.ShownIds().ToList();
            }

            CollectionAssert.AreEqual(Run(), Run());
        }

        [Test]
        public void TaskWithoutItsOwnMessages_FallsBackToTheStandardOnes()
        {
            var rig = new Rig(new GuideProfile(Task));
            rig.TaskStarted();
            rig.TrialStarted(1);
            rig.Engine.RequestHelp();

            Assert.IsTrue(rig.ShownIds().All(id => id.StartsWith("common.")));
        }

        [Test]
        public void HelpRequest_IsAnsweredOnlyWhileATrialIsRunning()
        {
            var rig = new Rig();
            Assert.AreEqual(HelpResult.NoActiveTrial, rig.Engine.RequestHelp(), "no task");
            rig.TaskStarted();
            Assert.AreEqual(HelpResult.NoActiveTrial, rig.Engine.RequestHelp(), "task but no trial yet");
            rig.TrialStarted(1);
            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
            rig.TrialSummary("completed");
            rig.Clock.Advance(100);
            Assert.AreEqual(HelpResult.NoActiveTrial, rig.Engine.RequestHelp(), "trial already ended");
        }

        [Test]
        public void HelpRequest_HonoursTheCooldown()
        {
            var rig = new Rig(new GuideProfile(Task) { HelpCooldownSeconds = 30 });
            rig.TaskStarted();
            rig.TrialStarted(1);

            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
            rig.Clock.Advance(10);
            Assert.AreEqual(HelpResult.Cooldown, rig.Engine.RequestHelp());
            rig.Clock.Advance(25);
            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
            Assert.AreEqual(2, rig.Presenter.Shown.Count(s => s.message.Moment == GuideMoment.HelpRequest));
        }

        [Test]
        public void HelpRequest_HonoursTheLimitPerTrialAndResetsForTheNextTrial()
        {
            var rig = new Rig(new GuideProfile(Task) { HelpCooldownSeconds = 0, MaxHelpPerTrial = 2 });
            rig.TaskStarted();
            rig.TrialStarted(1);

            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
            Assert.AreEqual(HelpResult.LimitReached, rig.Engine.RequestHelp());
            rig.TrialSummary("completed");
            rig.TrialStarted(2);
            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
        }

        [Test]
        public void HelpRequest_IsLoggedEvenWhenNoMessageIsShown()
        {
            var rig = new Rig(new GuideProfile(Task) { HelpRequestsEnabled = false });
            rig.TaskStarted();
            rig.TrialStarted(1);

            Assert.AreEqual(HelpResult.Disabled, rig.Engine.RequestHelp());

            var requested = rig.Sink.OfType(GuideEngine.HelpRequestedEvent).Single();
            Assert.AreEqual(false, MemorySink.Field(requested, "answered"));
            Assert.AreEqual("disabled", MemorySink.Field(requested, "result"));
            Assert.IsFalse(rig.Presenter.Shown.Any(s => s.message.Moment == GuideMoment.HelpRequest));
        }

        [Test]
        public void TrialEnd_MessageDependsOnTheOutcomeButNeverMentionsIt()
        {
            var rig = new Rig();
            rig.TaskStarted();
            rig.TrialStarted(1);
            rig.TrialSummary("completed");
            rig.TrialStarted(2);
            rig.TrialSummary("incomplete");

            var ends = rig.Presenter.Shown.Where(s => s.message.Moment == GuideMoment.TrialEnd).Select(s => s.message.Id).ToList();
            CollectionAssert.AreEqual(new[] { "common.trial_end.completed.1", "common.trial_end.incomplete.1" }, ends);
        }

        [Test]
        public void TrialSummaryWithoutAStartedTrial_ShowsNothing()
        {
            var rig = new Rig();
            rig.TaskStarted();
            rig.TrialSummary("completed");

            Assert.IsFalse(rig.Presenter.Shown.Any(s => s.message.Moment == GuideMoment.TrialEnd));
        }

        [Test]
        public void TaskEnd_IsShownOnlyWhenTheTaskWasCompleted()
        {
            var rig = new Rig();
            rig.TaskStarted();
            rig.TaskEnded("skipped");
            Assert.IsFalse(rig.Presenter.Shown.Any(s => s.message.Moment == GuideMoment.TaskEnd));

            rig.TaskStarted();
            rig.TaskEnded("completed");
            Assert.AreEqual(1, rig.Presenter.Shown.Count(s => s.message.Moment == GuideMoment.TaskEnd));
        }

        [Test]
        public void EventsOfAnotherTask_AreIgnored()
        {
            var rig = new Rig();
            rig.TaskStarted("Hanoi");
            var before = rig.Presenter.Shown.Count;
            rig.TrialStarted(1, "CuboRelaciones");

            Assert.AreEqual(before, rig.Presenter.Shown.Count);
            Assert.AreEqual(HelpResult.NoActiveTrial, rig.Engine.RequestHelp());
        }

        [Test]
        public void EachMomentCanBeDisabledPerTask()
        {
            var profile = new GuideProfile(Task).Disable(GuideMoment.TaskStart).Disable(GuideMoment.TrialStart)
                .Disable(GuideMoment.TrialEnd).Disable(GuideMoment.TaskEnd);
            var rig = new Rig(profile);
            rig.TaskStarted();
            rig.TrialStarted(1);
            rig.TrialSummary("completed");
            rig.TaskEnded("completed");

            Assert.AreEqual(0, rig.Presenter.Shown.Count);
            Assert.AreEqual(0, rig.Sink.OfType(GuideEngine.ShownEvent).Count());
        }

        [Test]
        public void DisabledEngine_ShowsNothingAndAnswersNoHelp()
        {
            var rig = new Rig();
            rig.Engine.Enabled = false;
            rig.TaskStarted();
            rig.TrialStarted(1);

            Assert.AreEqual(HelpResult.NoActiveTrial, rig.Engine.RequestHelp());
            Assert.AreEqual(0, rig.Presenter.Shown.Count);
        }

        [Test]
        public void TheGuideOwnEvents_AreNotFedBackIntoTheGuide()
        {
            var rig = new Rig();
            rig.TaskStarted();
            var before = rig.Presenter.Shown.Count;
            foreach (var e in rig.Sink.Events.ToList()) rig.Engine.OnEvent(e);

            Assert.AreEqual(before, rig.Presenter.Shown.Count);
        }

        [Test]
        public void Profiles_ConfigureEachTaskSeparately()
        {
            var a = new GuideProfile("A") { MaxHelpPerTrial = 1, HelpCooldownSeconds = 0 };
            var b = new GuideProfile("B") { HelpCooldownSeconds = 0 }.Disable(GuideMoment.TaskStart);
            var rig = new Rig(a, b);

            rig.TaskStarted("A");
            rig.TaskStarted("B");
            Assert.AreEqual(new[] { "A" }, rig.Presenter.Shown.Select(s => s.task).Distinct().ToArray());

            rig.TrialStarted(1, "B");
            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
            Assert.AreEqual(HelpResult.Shown, rig.Engine.RequestHelp());
        }

        [Test]
        public void GuideEvents_CarryTheFullEnvelopeThroughTheRealWriter()
        {
            var fixture = new SessionFixture("S-9", "P-03", "Control");
            var presenter = new RecordingPresenter();
            var engine = new GuideEngine(fixture.Writer, fixture.Clock, presenter, ResearchTaskCatalog.GuideProfiles());

            var start = new ResearchEvent(GuideEngine.ExperimentTask, "task_started").Add("task_id", Task);
            fixture.Writer.Log(start);
            engine.OnEvent(start);

            var shown = fixture.Events().Single(e => e.EventType == GuideEngine.ShownEvent);
            Assert.AreEqual("S-9", shown.SessionId);
            Assert.AreEqual("P-03", shown.ParticipantId);
            Assert.AreEqual("Control", shown.Condition);
            Assert.AreEqual(Task, shown.Task);
            Assert.AreEqual("task_start", shown.Text("guide_type"));
            Assert.IsNotEmpty(shown.Text("message_id"));
            Assert.IsNotNull(shown.TimestampUtc);
        }

        [Test]
        public void ARealHanoiTrial_DrivesTheGuideWithoutAnyChangeToTheTask()
        {
            var fixture = new SessionFixture();
            var presenter = new RecordingPresenter();
            var engine = new GuideEngine(fixture.Writer, fixture.Clock, presenter, ResearchTaskCatalog.GuideProfiles());
            var tap = new TapSink(fixture.Writer, engine);

            tap.Log(new ResearchEvent(GuideEngine.ExperimentTask, "task_started").Add("task_id", Task));
            var trial = new HanoiTrial(3, tap, fixture.Clock, 1);
            trial.Begin();
            fixture.Clock.Advance(20);
            engine.RequestHelp();
            HanoiSolver.Play(trial, 3);
            tap.Log(new ResearchEvent(GuideEngine.ExperimentTask, "task_ended").Add("task_id", Task).Add("status", "completed"));

            var moments = presenter.Shown.Select(s => s.message.Moment).ToList();
            CollectionAssert.AreEqual(new[]
            {
                GuideMoment.TaskStart, GuideMoment.TrialStart, GuideMoment.HelpRequest, GuideMoment.TrialEnd, GuideMoment.TaskEnd,
            }, moments);
            var guideEvents = fixture.Events().Where(e => e.EventType.StartsWith("guide_")).ToList();
            Assert.AreEqual(6, guideEvents.Count, "five messages and one help request");
        }

        sealed class TapSink : IResearchEventSink
        {
            readonly IResearchEventSink inner;
            readonly GuideEngine engine;

            public TapSink(IResearchEventSink inner, GuideEngine engine)
            {
                this.inner = inner;
                this.engine = engine;
            }

            public void Log(ResearchEvent researchEvent)
            {
                inner.Log(researchEvent);
                engine.OnEvent(researchEvent);
            }
        }

        static IEnumerable<GuideMessage> AllMessages() =>
            GuideCatalog.Common.Concat(ResearchTaskCatalog.GuideProfiles().SelectMany(p => p.Messages));

        [Test]
        public void EveryMessage_HasAUniqueId()
        {
            var ids = AllMessages().Select(m => m.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
        }

        [Test]
        public void EveryHelpMessage_IsAQuestion()
        {
            foreach (var m in AllMessages().Where(m => m.Moment == GuideMoment.HelpRequest || m.Moment == GuideMoment.TrialStart))
            {
                Assert.IsTrue(m.Text.StartsWith("¿") && m.Text.EndsWith("?"), m.Id + ": " + m.Text);
            }
        }

        [Test]
        public void EveryMessage_EndsAsAQuestion()
        {
            foreach (var m in AllMessages()) Assert.IsTrue(m.Text.EndsWith("?"), m.Id + ": " + m.Text);
        }

        [Test]
        public void NoMessage_GivesAnInstructionAJudgementOrARewardWord()
        {
            var forbidden = new[]
            {
                "mueve ", "mueva", "coloca", "pon ", "pon el", "gira ", "envía", "usa ", "debes", "tienes que", "tiene que", "hazlo", "haz ",
                "correcto", "incorrecto", "error", "bien hecho", "muy bien", "excelente", "felicidades", "enhorabuena", "respuesta", "solución",
                "solucion", "puntos", "punto ", "estrella", "premio", "nivel", "logro", "ganas", "ganaste", "perdiste", "fallaste", "mejor",
                "primero", "después", "siguiente", "ahora ", "algoritmo", "estrategia",
            };
            foreach (var m in AllMessages())
            {
                var text = m.Text.ToLowerInvariant();
                foreach (var word in forbidden)
                    Assert.IsFalse(text.Contains(word), $"{m.Id} contains '{word}': {m.Text}");
            }
        }

        [Test]
        public void MessagesAreInSpanish_AndShort()
        {
            foreach (var m in AllMessages())
            {
                Assert.LessOrEqual(m.Text.Length, 120, m.Id);
                Assert.IsFalse(m.Text.Contains("\n"), m.Id);
            }
        }

        [Test]
        public void EveryMoment_HasAStandardMessage_AndTrialEndCoversBothOutcomes()
        {
            foreach (GuideMoment moment in System.Enum.GetValues(typeof(GuideMoment)))
                Assert.IsNotEmpty(GuideCatalog.ForMoment(GuideCatalog.Common, moment, GuideOutcome.Completed), moment.ToString());
            Assert.IsNotEmpty(GuideCatalog.ForMoment(GuideCatalog.Common, GuideMoment.TrialEnd, GuideOutcome.Incomplete));
        }

        [Test]
        public void ProfilesExistForTheFourTasks_AndEachIsUsable()
        {
            var ids = ResearchTaskCatalog.GuideProfiles().Select(p => p.TaskId).ToList();
            CollectionAssert.AreEquivalent(new[] { "Hanoi", "CuboRelaciones", "GabineteFormas", "ElCorreo" }, ids);
            foreach (var profile in ResearchTaskCatalog.GuideProfiles())
                Assert.IsTrue(profile.IsEnabled(GuideMoment.HelpRequest), profile.TaskId);
        }

        [Test]
        public void DuplicateMessageIds_AreRejected()
        {
            var profile = new GuideProfile(Task).Add("x.1", GuideMoment.HelpRequest, "¿Qué observas?");
            Assert.Throws<System.ArgumentException>(() => profile.Add("x.1", GuideMoment.TrialStart, "¿Qué podrías probar?"));
        }
    }
}
