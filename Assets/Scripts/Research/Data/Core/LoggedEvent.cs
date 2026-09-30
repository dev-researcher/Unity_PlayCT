using System;
using System.Collections.Generic;
using System.Globalization;

namespace PlayCT.Research
{
    /// <summary>One line of events.jsonl as read back: the shared envelope plus the task's own fields, in file order.</summary>
    public sealed class LoggedEvent
    {
        public static readonly IReadOnlyList<string> EnvelopeFields = new[]
        {
            "session_id", "participant_id", "condition", "task", "event", "timestamp_utc", "t_session_s",
        };

        readonly Dictionary<string, object> lookup = new Dictionary<string, object>();

        public IReadOnlyList<KeyValuePair<string, object>> Fields { get; }
        public int LineNumber { get; }

        public string SessionId => Text("session_id");
        public string ParticipantId => Text("participant_id");
        public string Condition => Text("condition");
        public string Task => Text("task");
        public string EventType => Text("event");
        public double? SessionSeconds => Number("t_session_s");

        public DateTime? TimestampUtc
        {
            get
            {
                var text = Text("timestamp_utc");
                if (text != null && DateTime.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value))
                    return value;
                return null;
            }
        }

        public LoggedEvent(IReadOnlyList<KeyValuePair<string, object>> fields, int lineNumber)
        {
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            LineNumber = lineNumber;
            foreach (var field in fields)
                if (!lookup.ContainsKey(field.Key)) lookup[field.Key] = field.Value;
        }

        public bool Has(string key) => lookup.ContainsKey(key);

        public object Get(string key) => lookup.TryGetValue(key, out var value) ? value : null;

        public string Text(string key) => Get(key) as string;

        public double? Number(string key)
        {
            switch (Get(key))
            {
                case long l: return l;
                case double d: return d;
                default: return null;
            }
        }

        public int? Integer(string key)
        {
            switch (Get(key))
            {
                case long l when l >= int.MinValue && l <= int.MaxValue: return (int)l;
                case double d when d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue: return (int)d;
                default: return null;
            }
        }

        public bool? Flag(string key) => Get(key) as bool?;

        public IReadOnlyList<string> TextList(string key)
        {
            if (!(Get(key) is List<object> items)) return null;
            var list = new List<string>(items.Count);
            foreach (var item in items) list.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
            return list;
        }
    }

    public static class EventLogReader
    {
        /// <summary>Reads events.jsonl lines without changing them. Blank or malformed lines are skipped and counted.</summary>
        public static List<LoggedEvent> Read(IEnumerable<string> lines, out int skippedLines)
        {
            var events = new List<LoggedEvent>();
            skippedLines = 0;
            var lineNumber = 0;
            foreach (var line in lines)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (JsonLineReader.TryParseObject(line, out var fields)) events.Add(new LoggedEvent(fields, lineNumber));
                else skippedLines++;
            }
            return events;
        }
    }
}
