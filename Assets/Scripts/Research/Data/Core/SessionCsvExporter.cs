using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PlayCT.Research
{
    /// <summary>
    /// Builds the CSV text for a session. <c>session_summary.csv</c> has one row per task trial with fixed common columns and,
    /// after them, one column per task-specific metric named <c>TaskId.field</c> (declared by the task's adapter, so the header
    /// does not depend on which tasks ran; fields no adapter declares are appended sorted). A metric a task does not have is
    /// an empty cell. <c>events.csv</c> has one row per JSONL event with the envelope and the remaining fields as one JSON cell.
    /// Text is returned as a string; the caller writes it as UTF-8 without a byte order mark.
    /// </summary>
    public static class SessionCsvExporter
    {
        public const string SummaryFileName = "session_summary.csv";
        public const string EventsFileName = "events.csv";

        public static readonly IReadOnlyList<string> CommonColumns = new[]
        {
            "session_id", "participant_id", "condition", "task_id", "trial", "start_time", "end_time", "duration", "completed",
            "completion_status", "completion_time_s", "attempts", "valid_actions", "invalid_actions", "moves", "efficiency",
            "action_sequence", "record_source",
        };

        public static readonly IReadOnlyList<string> EventColumns = new[]
        {
            "session_id", "participant_id", "condition", "task", "event", "timestamp_utc", "t_session_s", "trial_index", "data",
        };

        public static IReadOnlyList<string> SummaryColumns(SessionSummary summary)
        {
            var columns = new List<string>(CommonColumns);
            var declared = new HashSet<string>();
            foreach (var adapter in summary.Adapters)
                foreach (var field in adapter.ExtraFields)
                {
                    var name = ColumnName(adapter.TaskId, field);
                    if (declared.Add(name)) columns.Add(name);
                }

            var discovered = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var record in summary.Records)
                foreach (var field in record.TaskSpecific)
                {
                    var name = ColumnName(record.TaskId, field.Key);
                    if (!declared.Contains(name)) discovered.Add(name);
                }
            columns.AddRange(discovered);
            return columns;
        }

        public static string BuildSessionSummaryCsv(SessionSummary summary)
        {
            var columns = SummaryColumns(summary);
            var sb = new StringBuilder();
            CsvWriter.AppendRow(sb, columns);
            foreach (var record in summary.Records)
            {
                var specific = new Dictionary<string, object>();
                foreach (var field in record.TaskSpecific) specific[ColumnName(record.TaskId, field.Key)] = field.Value;

                var cells = new List<string>(columns.Count)
                {
                    record.SessionId,
                    record.ParticipantId,
                    record.Condition,
                    record.TaskId,
                    Format(record.Trial),
                    Format(record.StartTimeUtc),
                    Format(record.EndTimeUtc),
                    Format(record.DurationSeconds),
                    Format(record.Completed),
                    record.CompletionStatus,
                    Format(record.CompletionTimeSeconds),
                    Format(record.Attempts),
                    Format(record.ValidActions),
                    Format(record.InvalidActions),
                    Format(record.Moves),
                    Format(record.Efficiency),
                    Format(record.ActionSequence),
                    record.Source,
                };
                for (var i = CommonColumns.Count; i < columns.Count; i++)
                    cells.Add(specific.TryGetValue(columns[i], out var value) ? Format(value) : string.Empty);
                CsvWriter.AppendRow(sb, cells);
            }
            return sb.ToString();
        }

        public static string BuildEventsCsv(IEnumerable<LoggedEvent> events)
        {
            var sb = new StringBuilder();
            CsvWriter.AppendRow(sb, EventColumns);
            foreach (var e in events)
            {
                var rest = e.Fields.Where(f => !LoggedEvent.EnvelopeFields.Contains(f.Key) && f.Key != "trial_index").ToList();
                CsvWriter.AppendRow(sb, new[]
                {
                    e.SessionId, e.ParticipantId, e.Condition, e.Task, e.EventType,
                    Format(e.Get("timestamp_utc")), Format(e.Get("t_session_s")), Format(e.Get("trial_index")),
                    rest.Count == 0 ? string.Empty : JsonLine.Serialize(rest),
                });
            }
            return sb.ToString();
        }

        static string ColumnName(string taskId, string field) => taskId + "." + field;

        /// <summary>Empty for null, invariant numbers, true/false, ISO UTC times, and JSON for lists so nothing is lost.</summary>
        public static string Format(object value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case int i: return i.ToString(CultureInfo.InvariantCulture);
                case long l: return l.ToString(CultureInfo.InvariantCulture);
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? string.Empty : d.ToString("R", CultureInfo.InvariantCulture);
                case DateTime dt: return dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                case IEnumerable seq:
                    var sb = new StringBuilder();
                    JsonLine.AppendValue(sb, seq);
                    return sb.ToString();
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
