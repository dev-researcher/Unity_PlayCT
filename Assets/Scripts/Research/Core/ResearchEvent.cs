using System.Collections.Generic;

namespace PlayCT.Research
{
    public sealed class ResearchEvent
    {
        public string Task { get; }
        public string EventType { get; }
        public List<KeyValuePair<string, object>> Fields { get; } = new List<KeyValuePair<string, object>>();

        public ResearchEvent(string task, string eventType)
        {
            Task = task;
            EventType = eventType;
        }

        public ResearchEvent Add(string key, object value)
        {
            Fields.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }
    }

    public interface IResearchEventSink
    {
        void Log(ResearchEvent researchEvent);
    }
}
