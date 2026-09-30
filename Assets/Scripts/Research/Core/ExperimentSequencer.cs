using System;
using System.Collections.Generic;

namespace PlayCT.Research
{
    public enum ExperimentState
    {
        NotStarted,
        Running,
        Completed,
        Aborted,
    }

    /// <summary>
    /// Runs an ordered list of task IDs one after another: start the task, wait for its completion, end it, move on.
    /// It contains no task-specific logic; tasks are looked up by ID. Lifecycle events go to the shared
    /// research event sink under the task name "Experiment".
    /// </summary>
    public sealed class ExperimentSequencer
    {
        public const string TaskName = "Experiment";

        readonly List<string> sequence;
        readonly Func<string, IExperimentTask> resolve;
        readonly IResearchEventSink sink;
        readonly IClock clock;
        readonly List<string> completed = new List<string>();
        readonly List<string> skipped = new List<string>();

        IExperimentTask current;
        double experimentStart;
        double taskStart;

        public ExperimentCondition Condition { get; }
        public ExperimentState State { get; private set; } = ExperimentState.NotStarted;
        public int CurrentIndex { get; private set; } = -1;
        public string CurrentTaskId => current?.TaskId;
        public IReadOnlyList<string> Sequence => sequence;
        public IReadOnlyList<string> CompletedTaskIds => completed;
        public IReadOnlyList<string> SkippedTaskIds => skipped;

        public event Action<ExperimentSequencer> Finished;

        public ExperimentSequencer(IEnumerable<string> sequence, Func<string, IExperimentTask> resolve, ExperimentCondition condition,
            IResearchEventSink sink, IClock clock)
        {
            this.sequence = new List<string>(sequence ?? throw new ArgumentNullException(nameof(sequence)));
            this.resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Condition = condition;
        }

        public void Start()
        {
            if (State != ExperimentState.NotStarted) throw new InvalidOperationException("The experiment was already started.");
            State = ExperimentState.Running;
            experimentStart = clock.MonotonicSeconds;
            sink.Log(NewEvent("experiment_started")
                .Add("condition", ExperimentConditions.ToLabel(Condition))
                .Add("task_sequence", new List<string>(sequence))
                .Add("task_count", sequence.Count));
            AdvanceTo(0);
        }

        public void Abort(string reason)
        {
            if (State != ExperimentState.Running) return;
            if (current != null)
            {
                var task = current;
                current = null;
                task.Completed -= OnTaskCompleted;
                task.EndTask(reason);
                LogTaskEnded(task, "aborted", reason);
            }
            Finish(ExperimentState.Aborted, reason);
        }

        void AdvanceTo(int next)
        {
            while (State == ExperimentState.Running)
            {
                if (next >= sequence.Count)
                {
                    Finish(ExperimentState.Completed, "all_tasks_completed");
                    return;
                }

                var id = sequence[next];
                var task = resolve(id);
                if (task == null)
                {
                    skipped.Add(id);
                    sink.Log(NewEvent("task_skipped").Add("task_id", id).Add("task_index", next).Add("reason", "not_registered"));
                    next++;
                    continue;
                }

                current = task;
                CurrentIndex = next;
                taskStart = clock.MonotonicSeconds;
                task.Completed += OnTaskCompleted;
                sink.Log(NewEvent("task_started")
                    .Add("task_id", task.TaskId)
                    .Add("task_index", next)
                    .Add("condition", ExperimentConditions.ToLabel(Condition)));
                task.StartTask(Condition);

                // A task may complete inside StartTask (which advances on its own) or already be complete afterwards.
                if (current == task && task.IsCompleted) CompleteCurrent();
                return;
            }
        }

        void OnTaskCompleted(IExperimentTask task)
        {
            if (task != current) return;
            CompleteCurrent();
        }

        void CompleteCurrent()
        {
            var task = current;
            var index = CurrentIndex;
            current = null;
            task.Completed -= OnTaskCompleted;
            task.EndTask("completed");
            LogTaskEnded(task, "completed", "completed");
            completed.Add(task.TaskId);
            AdvanceTo(index + 1);
        }

        void LogTaskEnded(IExperimentTask task, string status, string reason)
        {
            sink.Log(NewEvent("task_ended")
                .Add("task_id", task.TaskId)
                .Add("task_index", CurrentIndex)
                .Add("status", status)
                .Add("end_reason", reason)
                .Add("duration_s", Math.Round(clock.MonotonicSeconds - taskStart, 3)));
        }

        void Finish(ExperimentState state, string reason)
        {
            State = state;
            sink.Log(NewEvent("experiment_ended")
                .Add("status", state == ExperimentState.Completed ? "completed" : "aborted")
                .Add("end_reason", reason)
                .Add("tasks_completed", completed.Count)
                .Add("tasks_skipped", skipped.Count)
                .Add("duration_s", Math.Round(clock.MonotonicSeconds - experimentStart, 3)));
            Finished?.Invoke(this);
        }

        static ResearchEvent NewEvent(string type) => new ResearchEvent(TaskName, type);
    }
}
