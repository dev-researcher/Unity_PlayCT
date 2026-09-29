using System;
using System.Collections.Generic;

namespace PlayCT.Research
{
    /// <summary>The common metrics of one trial summary. A value is null when the task does not have that metric.</summary>
    public sealed class TaskMetrics
    {
        public int? Attempts;
        public int? ValidActions;
        public int? InvalidActions;
        public int? Moves;
        public double? Efficiency;
        public IReadOnlyList<string> ActionSequence;

        /// <summary>Summary fields that were used for the common metrics and so are not repeated as task-specific data.</summary>
        public IReadOnlyCollection<string> ConsumedFields = new string[0];
    }

    /// <summary>
    /// Tells the aggregation how one task names its common metrics inside its own <c>trial_summary</c> event. Everything the
    /// adapter does not consume is kept as task-specific data, so a task can have metrics no other task has. A new task is
    /// aggregated by registering an adapter (or none: unknown tasks still get the envelope, timing and status).
    /// </summary>
    public interface ITaskMetricsAdapter
    {
        string TaskId { get; }

        /// <summary>The task-specific summary fields, so the CSV columns are the same whether or not the task appears in a session.</summary>
        IReadOnlyList<string> ExtraFields { get; }

        TaskMetrics Extract(LoggedEvent summary);
    }

    /// <summary>Adapter driven by field names; covers every task whose summary has plain counters.</summary>
    public sealed class MappedTaskMetricsAdapter : ITaskMetricsAdapter
    {
        readonly string attempts;
        readonly string valid;
        readonly string invalid;
        readonly string moves;
        readonly string efficiency;
        readonly string actionSequence;
        readonly List<string> extraFields;

        public string TaskId { get; }
        public IReadOnlyList<string> ExtraFields => extraFields;

        public MappedTaskMetricsAdapter(string taskId, string attemptsField, string validActionsField, string invalidActionsField,
            string movesField, string efficiencyField, string actionSequenceField, IEnumerable<string> extraFields)
        {
            TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
            attempts = attemptsField;
            valid = validActionsField;
            invalid = invalidActionsField;
            moves = movesField;
            efficiency = efficiencyField;
            actionSequence = actionSequenceField;
            this.extraFields = new List<string>(extraFields ?? new string[0]);
        }

        public TaskMetrics Extract(LoggedEvent summary)
        {
            var consumed = new List<string>();
            int? Count(string field)
            {
                if (field == null) return null;
                consumed.Add(field);
                return summary.Integer(field);
            }

            var metrics = new TaskMetrics
            {
                Attempts = Count(attempts),
                ValidActions = Count(valid),
                InvalidActions = Count(invalid),
                Moves = Count(moves),
            };
            if (efficiency != null)
            {
                consumed.Add(efficiency);
                metrics.Efficiency = summary.Number(efficiency);
            }
            if (actionSequence != null)
            {
                consumed.Add(actionSequence);
                metrics.ActionSequence = summary.TextList(actionSequence);
            }
            metrics.ConsumedFields = consumed;
            return metrics;
        }
    }

    /// <summary>Used for a task with no registered adapter: only envelope, timing and status are common; every other field is task-specific.</summary>
    public sealed class GenericTaskMetricsAdapter : ITaskMetricsAdapter
    {
        public string TaskId { get; }
        public IReadOnlyList<string> ExtraFields => new string[0];

        public GenericTaskMetricsAdapter(string taskId) { TaskId = taskId; }

        public TaskMetrics Extract(LoggedEvent summary) => new TaskMetrics();
    }
}
