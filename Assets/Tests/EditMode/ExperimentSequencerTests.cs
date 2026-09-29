using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PlayCT.Research;

namespace PlayCT.Tests
{
    public class ExperimentSequencerTests
    {
        sealed class StubTask : IExperimentTask
        {
            public string TaskId { get; }
            public bool IsRunning { get; private set; }
            public bool IsCompleted { get; private set; }
            public readonly List<string> Calls = new List<string>();
            public ExperimentCondition? StartedWith;
            public bool CompleteInsideStart;
            public bool AlreadyCompleteAfterStart;

            public event Action<IExperimentTask> Completed;

            public StubTask(string id) { TaskId = id; }

            public void StartTask(ExperimentCondition condition)
            {
                Calls.Add("start");
                StartedWith = condition;
                IsRunning = true;
                if (CompleteInsideStart) Finish();
                else if (AlreadyCompleteAfterStart) IsCompleted = true;
            }

            public void EndTask(string reason)
            {
                Calls.Add("end:" + reason);
                IsRunning = false;
            }

            public void Finish()
            {
                IsCompleted = true;
                Completed?.Invoke(this);
            }
        }

        FakeClock clock;
        MemorySink sink;
        Dictionary<string, IExperimentTask> tasks;

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock();
            sink = new MemorySink();
            tasks = new Dictionary<string, IExperimentTask>();
        }

        StubTask Add(string id)
        {
            var task = new StubTask(id);
            tasks[id] = task;
            return task;
        }

        ExperimentSequencer Create(ExperimentCondition condition, params string[] sequence) =>
            new ExperimentSequencer(sequence, id => tasks.TryGetValue(id, out var t) ? t : null, condition, sink, clock);

        string[] Types() => sink.Events.Select(e => e.EventType).ToArray();

        [Test]
        public void Start_LogsExperimentStartedWithConditionAndSequence_ThenStartsFirstTask()
        {
            var a = Add("A");
            var b = Add("B");
            var run = Create(ExperimentCondition.PreAdapted, "A", "B");

            run.Start();

            Assert.AreEqual(ExperimentState.Running, run.State);
            Assert.AreEqual("A", run.CurrentTaskId);
            Assert.AreEqual(new[] { "start" }, a.Calls);
            Assert.IsEmpty(b.Calls, "the second task waits for the first");
            Assert.AreEqual(ExperimentCondition.PreAdapted, a.StartedWith);
            Assert.AreEqual(new[] { "experiment_started", "task_started" }, Types());
            var started = sink.Events[0];
            Assert.AreEqual(ExperimentSequencer.TaskName, started.Task);
            Assert.AreEqual("PreAdapted", MemorySink.Field(started, "condition"));
            Assert.AreEqual(new[] { "A", "B" }, (List<string>)MemorySink.Field(started, "task_sequence"));
        }

        [Test]
        public void CompletingATask_EndsIt_AndStartsTheNext_InSequenceOrder()
        {
            var a = Add("A");
            var b = Add("B");
            var run = Create(ExperimentCondition.Static, "A", "B");
            run.Start();

            clock.Advance(12.5);
            a.Finish();

            Assert.AreEqual(new[] { "start", "end:completed" }, a.Calls);
            Assert.AreEqual(new[] { "start" }, b.Calls);
            Assert.AreEqual("B", run.CurrentTaskId);
            Assert.AreEqual(1, run.CurrentIndex);
            Assert.AreEqual(new[] { "experiment_started", "task_started", "task_ended", "task_started" }, Types());
            var ended = sink.OfType("task_ended").Single();
            Assert.AreEqual("A", MemorySink.Field(ended, "task_id"));
            Assert.AreEqual("completed", MemorySink.Field(ended, "status"));
            Assert.AreEqual(12.5, (double)MemorySink.Field(ended, "duration_s"), 1e-6);
        }

        [Test]
        public void LastTaskCompleting_FinishesTheExperiment()
        {
            var a = Add("A");
            var b = Add("B");
            var run = Create(ExperimentCondition.Static, "A", "B");
            var finished = 0;
            run.Finished += _ => finished++;
            run.Start();

            a.Finish();
            clock.Advance(30);
            b.Finish();

            Assert.AreEqual(ExperimentState.Completed, run.State);
            Assert.IsNull(run.CurrentTaskId);
            Assert.AreEqual(new[] { "A", "B" }, run.CompletedTaskIds);
            Assert.AreEqual(1, finished);
            Assert.AreEqual("experiment_ended", Types().Last());
            var end = sink.Events.Last();
            Assert.AreEqual("completed", MemorySink.Field(end, "status"));
            Assert.AreEqual(2, MemorySink.Field(end, "tasks_completed"));
            Assert.AreEqual(30, (double)MemorySink.Field(end, "duration_s"), 1e-6);
        }

        [Test]
        public void EmptySequence_CompletesImmediately()
        {
            var run = Create(ExperimentCondition.Static);
            run.Start();

            Assert.AreEqual(ExperimentState.Completed, run.State);
            Assert.AreEqual(new[] { "experiment_started", "experiment_ended" }, Types());
        }

        [Test]
        public void UnregisteredTaskIds_AreSkippedAndLogged_NotFaked()
        {
            var a = Add("A");
            var d = Add("D");
            var run = Create(ExperimentCondition.Static, "A", "Missing", "D");
            run.Start();

            a.Finish();

            Assert.AreEqual("D", run.CurrentTaskId);
            Assert.AreEqual(2, run.CurrentIndex, "the index refers to the position in the configured sequence");
            Assert.AreEqual(new[] { "Missing" }, run.SkippedTaskIds);
            var skipped = sink.OfType("task_skipped").Single();
            Assert.AreEqual("Missing", MemorySink.Field(skipped, "task_id"));
            Assert.AreEqual(1, MemorySink.Field(skipped, "task_index"));

            d.Finish();
            Assert.AreEqual(1, MemorySink.Field(sink.Events.Last(), "tasks_skipped"));
        }

        [Test]
        public void AllTasksMissing_StillCompletes()
        {
            var run = Create(ExperimentCondition.Static, "X", "Y");
            run.Start();

            Assert.AreEqual(ExperimentState.Completed, run.State);
            Assert.AreEqual(2, sink.OfType("task_skipped").Count());
        }

        [Test]
        public void TaskThatCompletesInsideStart_AdvancesExactlyOnce()
        {
            var a = Add("A");
            a.CompleteInsideStart = true;
            var b = Add("B");
            var run = Create(ExperimentCondition.Static, "A", "B");

            run.Start();

            Assert.AreEqual(new[] { "start", "end:completed" }, a.Calls);
            Assert.AreEqual(new[] { "start" }, b.Calls);
            Assert.AreEqual("B", run.CurrentTaskId);
            Assert.AreEqual(1, sink.OfType("task_ended").Count());
        }

        [Test]
        public void TaskAlreadyCompleteAfterStart_WithoutEvent_IsStillDetected()
        {
            var a = Add("A");
            a.AlreadyCompleteAfterStart = true;
            var run = Create(ExperimentCondition.Static, "A");

            run.Start();

            Assert.AreEqual(ExperimentState.Completed, run.State);
            Assert.AreEqual(new[] { "start", "end:completed" }, a.Calls);
        }

        [Test]
        public void CompletionRaisedAgainAfterTheTaskEnded_IsIgnored()
        {
            var a = Add("A");
            var b = Add("B");
            var run = Create(ExperimentCondition.Static, "A", "B");
            run.Start();
            a.Finish();

            a.Finish();

            Assert.AreEqual("B", run.CurrentTaskId);
            Assert.AreEqual(1, sink.OfType("task_ended").Count());
            Assert.AreEqual(new[] { "start" }, b.Calls);
        }

        [Test]
        public void CompletionFromANonCurrentTask_IsIgnored()
        {
            var a = Add("A");
            var b = Add("B");
            var run = Create(ExperimentCondition.Static, "A", "B");
            run.Start();

            b.Finish();

            Assert.AreEqual("A", run.CurrentTaskId);
            Assert.IsEmpty(b.Calls);
        }

        [Test]
        public void Abort_EndsTheCurrentTaskWithTheReason_AndStopsTheExperiment()
        {
            var a = Add("A");
            var b = Add("B");
            var run = Create(ExperimentCondition.Static, "A", "B");
            run.Start();
            clock.Advance(4);

            run.Abort("operator_stop");

            Assert.AreEqual(ExperimentState.Aborted, run.State);
            Assert.AreEqual(new[] { "start", "end:operator_stop" }, a.Calls);
            Assert.IsEmpty(b.Calls);
            Assert.AreEqual("aborted", MemorySink.Field(sink.OfType("task_ended").Single(), "status"));
            var end = sink.Events.Last();
            Assert.AreEqual("aborted", MemorySink.Field(end, "status"));
            Assert.AreEqual("operator_stop", MemorySink.Field(end, "end_reason"));

            a.Finish();
            Assert.AreEqual(1, sink.OfType("experiment_ended").Count(), "a late completion changes nothing");
        }

        [Test]
        public void Abort_WhenNotRunning_DoesNothing()
        {
            var run = Create(ExperimentCondition.Static, "A");
            run.Abort("early");
            Assert.AreEqual(ExperimentState.NotStarted, run.State);
            Assert.IsEmpty(sink.Events);
        }

        [Test]
        public void Start_CannotBeCalledTwice()
        {
            Add("A");
            var run = Create(ExperimentCondition.Static, "A");
            run.Start();
            Assert.Throws<InvalidOperationException>(() => run.Start());
        }

        [Test]
        public void EventsFromAllTasksShareTheSameSink()
        {
            var a = Add("A");
            var run = Create(ExperimentCondition.Static, "A");
            run.Start();
            sink.Log(new ResearchEvent("A", "custom_task_event"));
            a.Finish();

            Assert.AreEqual(new[] { "experiment_started", "task_started", "custom_task_event", "task_ended", "experiment_ended" }, Types());
        }
    }
}
