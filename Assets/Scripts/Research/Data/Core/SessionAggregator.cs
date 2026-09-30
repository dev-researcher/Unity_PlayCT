using System;
using System.Collections.Generic;

namespace PlayCT.Research
{
    /// <summary>
    /// One row of the session summary: one trial of one task (or, for a task that wrote no trial summary, the task as a whole).
    /// Built from the task's own <c>trial_summary</c> event, so nothing is measured twice.
    /// </summary>
    public sealed class TrialRecord
    {
        public const string SourceTrialSummary = "trial_summary";
        public const string SourceTaskEvents = "task_events";

        public string SessionId;
        public string ParticipantId;
        public string Condition;
        public string TaskId;
        public int? Trial;
        public DateTime? StartTimeUtc;
        public DateTime? EndTimeUtc;
        public double? DurationSeconds;
        public bool? Completed;
        public string CompletionStatus;
        public double? CompletionTimeSeconds;
        public int? Attempts;
        public int? ValidActions;
        public int? InvalidActions;
        public int? Moves;
        public double? Efficiency;
        public IReadOnlyList<string> ActionSequence;
        public string Source;

        /// <summary>Everything else in the task's summary, unchanged and in its original order.</summary>
        public List<KeyValuePair<string, object>> TaskSpecific = new List<KeyValuePair<string, object>>();
    }

    /// <summary>The session level: identity, experiment outcome and one <see cref="TrialRecord"/> per task trial.</summary>
    public sealed class SessionSummary
    {
        public string SessionId;
        public string ParticipantId;
        public string Condition;
        public DateTime? StartTimeUtc;
        public DateTime? EndTimeUtc;
        public double? DurationSeconds;
        public string ExperimentStatus;
        public List<string> TaskSequence = new List<string>();
        public int EventCount;
        public int SkippedLines;
        public List<TrialRecord> Records = new List<TrialRecord>();
        public IReadOnlyList<ITaskMetricsAdapter> Adapters = new ITaskMetricsAdapter[0];
    }

    /// <summary>
    /// Turns the raw events of a session (events.jsonl) into a <see cref="SessionSummary"/>: raw events, then the per-trial
    /// task summaries the tasks already log, then the session level. It only reads; the JSONL and the task summary files are
    /// never touched.
    /// </summary>
    public sealed class SessionAggregator
    {
        const string ExperimentTask = "Experiment";

        readonly List<ITaskMetricsAdapter> adapters = new List<ITaskMetricsAdapter>();

        public IReadOnlyList<ITaskMetricsAdapter> Adapters => adapters;

        public SessionAggregator(IEnumerable<ITaskMetricsAdapter> adapters = null)
        {
            if (adapters == null) return;
            foreach (var adapter in adapters) Register(adapter);
        }

        /// <summary>Registers the adapter for a task; registering the same task again replaces the earlier adapter.</summary>
        public void Register(ITaskMetricsAdapter adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            var index = adapters.FindIndex(a => a.TaskId == adapter.TaskId);
            if (index >= 0) adapters[index] = adapter;
            else adapters.Add(adapter);
        }

        public SessionSummary Aggregate(IReadOnlyList<LoggedEvent> events, int skippedLines = 0)
        {
            var summary = new SessionSummary { EventCount = events.Count, SkippedLines = skippedLines, Adapters = adapters.ToArray() };
            var trialStarts = new Dictionary<(string, int), DateTime>();
            var taskStarts = new Dictionary<string, DateTime>();
            var startOrder = new List<string>();
            var taskEnds = new Dictionary<string, LoggedEvent>();
            var tasksWithSummary = new HashSet<string>();

            foreach (var e in events)
            {
                summary.SessionId = summary.SessionId ?? e.SessionId;
                summary.ParticipantId = summary.ParticipantId ?? e.ParticipantId;
                summary.Condition = summary.Condition ?? e.Condition;
                var time = e.TimestampUtc;
                if (time.HasValue)
                {
                    if (!summary.StartTimeUtc.HasValue) summary.StartTimeUtc = time;
                    summary.EndTimeUtc = time;
                }

                if (e.Task == ExperimentTask)
                {
                    ReadExperimentEvent(e, summary, taskStarts, startOrder, taskEnds);
                    continue;
                }

                switch (e.EventType)
                {
                    case "trial_started":
                        if (e.Integer("trial_index") is int startedTrial && time.HasValue) trialStarts[(e.Task, startedTrial)] = time.Value;
                        break;
                    case "trial_summary":
                        tasksWithSummary.Add(e.Task);
                        summary.Records.Add(BuildRecord(e, trialStarts, taskStarts));
                        break;
                }
            }

            foreach (var taskId in startOrder)
            {
                if (tasksWithSummary.Contains(taskId)) continue;
                summary.Records.Add(BuildTaskRecord(taskId, summary, taskStarts, taskEnds));
            }

            if (summary.StartTimeUtc.HasValue && summary.EndTimeUtc.HasValue)
                summary.DurationSeconds = Math.Round((summary.EndTimeUtc.Value - summary.StartTimeUtc.Value).TotalSeconds, 3);
            return summary;
        }

        static void ReadExperimentEvent(LoggedEvent e, SessionSummary summary, Dictionary<string, DateTime> taskStarts,
            List<string> startOrder, Dictionary<string, LoggedEvent> taskEnds)
        {
            switch (e.EventType)
            {
                case "experiment_started":
                    var sequence = e.TextList("task_sequence");
                    if (sequence != null) summary.TaskSequence = new List<string>(sequence);
                    break;
                case "task_started":
                    var started = e.Text("task_id");
                    if (started == null) break;
                    if (e.TimestampUtc.HasValue) taskStarts[started] = e.TimestampUtc.Value;
                    if (!startOrder.Contains(started)) startOrder.Add(started);
                    break;
                case "task_ended":
                    var ended = e.Text("task_id");
                    if (ended != null) taskEnds[ended] = e;
                    break;
                case "experiment_ended":
                    summary.ExperimentStatus = e.Text("status");
                    break;
            }
        }

        TrialRecord BuildRecord(LoggedEvent e, Dictionary<(string, int), DateTime> trialStarts, Dictionary<string, DateTime> taskStarts)
        {
            var adapter = FindAdapter(e.Task);
            var metrics = adapter.Extract(e);
            var record = new TrialRecord
            {
                SessionId = e.SessionId,
                ParticipantId = e.ParticipantId,
                Condition = e.Condition,
                TaskId = e.Task,
                Trial = e.Integer("trial_index"),
                EndTimeUtc = e.TimestampUtc,
                CompletionStatus = e.Text("completion_status"),
                CompletionTimeSeconds = e.Number("completion_time_s"),
                DurationSeconds = e.Number("elapsed_s"),
                Attempts = metrics.Attempts,
                ValidActions = metrics.ValidActions,
                InvalidActions = metrics.InvalidActions,
                Moves = metrics.Moves,
                Efficiency = metrics.Efficiency,
                ActionSequence = metrics.ActionSequence,
                Source = TrialRecord.SourceTrialSummary,
            };
            record.Completed = record.CompletionStatus == null ? (bool?)null : record.CompletionStatus == "completed";

            if (record.Trial.HasValue && trialStarts.TryGetValue((e.Task, record.Trial.Value), out var start)) record.StartTimeUtc = start;
            else if (taskStarts.TryGetValue(e.Task, out var taskStart)) record.StartTimeUtc = taskStart;
            if (!record.DurationSeconds.HasValue && record.StartTimeUtc.HasValue && record.EndTimeUtc.HasValue)
                record.DurationSeconds = Math.Round((record.EndTimeUtc.Value - record.StartTimeUtc.Value).TotalSeconds, 3);

            var consumed = new HashSet<string>(LoggedEvent.EnvelopeFields);
            consumed.UnionWith(new[] { "trial_index", "completion_status", "completion_time_s", "elapsed_s" });
            foreach (var field in metrics.ConsumedFields) consumed.Add(field);
            foreach (var field in e.Fields)
                if (!consumed.Contains(field.Key)) record.TaskSpecific.Add(field);
            return record;
        }

        static TrialRecord BuildTaskRecord(string taskId, SessionSummary session, Dictionary<string, DateTime> taskStarts,
            Dictionary<string, LoggedEvent> taskEnds)
        {
            var record = new TrialRecord
            {
                SessionId = session.SessionId,
                ParticipantId = session.ParticipantId,
                Condition = session.Condition,
                TaskId = taskId,
                Source = TrialRecord.SourceTaskEvents,
            };
            if (taskStarts.TryGetValue(taskId, out var start)) record.StartTimeUtc = start;
            if (taskEnds.TryGetValue(taskId, out var ended))
            {
                record.EndTimeUtc = ended.TimestampUtc;
                record.DurationSeconds = ended.Number("duration_s");
                record.CompletionStatus = ended.Text("status");
                record.Completed = record.CompletionStatus == "completed";
            }
            else
            {
                record.CompletionStatus = "not_ended";
                record.Completed = false;
            }
            return record;
        }

        ITaskMetricsAdapter FindAdapter(string taskId)
        {
            foreach (var adapter in adapters)
                if (adapter.TaskId == taskId) return adapter;
            return new GenericTaskMetricsAdapter(taskId);
        }
    }
}
