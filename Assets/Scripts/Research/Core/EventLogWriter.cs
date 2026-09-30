using System;
using System.Collections.Generic;
using System.IO;

namespace PlayCT.Research
{
    /// <summary>
    /// Writes research events as JSON Lines. Every task shares the same envelope
    /// (session_id, participant_id, condition, task, event, timestamp_utc, t_session_s)
    /// followed by the task-specific fields, flattened into the same object.
    /// </summary>
    public sealed class EventLogWriter : IResearchEventSink, IDisposable
    {
        readonly ISessionInfo session;
        readonly IClock clock;
        readonly TextWriter writer;
        readonly double startMonotonic;
        readonly object gate = new object();

        public int EventCount { get; private set; }

        public EventLogWriter(ISessionInfo session, TextWriter writer, IClock clock)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            startMonotonic = clock.MonotonicSeconds;
        }

        public void Log(ResearchEvent researchEvent)
        {
            var fields = new List<KeyValuePair<string, object>>(8 + researchEvent.Fields.Count)
            {
                new KeyValuePair<string, object>("session_id", session.SessionId),
                new KeyValuePair<string, object>("participant_id", session.ParticipantId),
                new KeyValuePair<string, object>("condition", session.Condition),
                new KeyValuePair<string, object>("task", researchEvent.Task),
                new KeyValuePair<string, object>("event", researchEvent.EventType),
                new KeyValuePair<string, object>("timestamp_utc", clock.UtcNow),
                new KeyValuePair<string, object>("t_session_s", Math.Round(clock.MonotonicSeconds - startMonotonic, 3)),
            };
            fields.AddRange(researchEvent.Fields);

            var line = JsonLine.Serialize(fields);
            lock (gate)
            {
                writer.WriteLine(line);
                writer.Flush();
                EventCount++;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                writer.Flush();
                writer.Dispose();
            }
        }
    }
}
