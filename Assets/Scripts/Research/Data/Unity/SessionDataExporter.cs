using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PlayCT.Research
{
    /// <summary>
    /// Writes <c>session_summary.csv</c> (and optionally <c>events.csv</c>) into the session folder that the EventLogger already
    /// uses, by reading the session's events.jsonl back through <see cref="SessionAggregator"/>. It runs when the orchestrator
    /// finishes or aborts the experiment, or when <see cref="Export"/> is called. It only reads the JSONL and the task summary
    /// files stay as they are.
    /// </summary>
    [DisallowMultipleComponent]
    public class SessionDataExporter : MonoBehaviour
    {
        [SerializeField] TaskOrchestrator orchestrator;
        [SerializeField] EventLogger eventLogger;
        [SerializeField] bool exportWhenExperimentFinishes = true;
        [SerializeField] bool exportEventsCsv = true;

        public string LastSummaryPath { get; private set; }
        public string LastEventsPath { get; private set; }

        void OnEnable()
        {
            if (orchestrator == null) orchestrator = FindFirstObjectByType<TaskOrchestrator>();
            if (eventLogger == null) eventLogger = FindFirstObjectByType<EventLogger>();
            if (orchestrator != null) orchestrator.ExperimentFinished += OnExperimentFinished;
        }

        void OnDisable()
        {
            if (orchestrator != null) orchestrator.ExperimentFinished -= OnExperimentFinished;
        }

        void OnExperimentFinished(ExperimentSequencer sequencer)
        {
            if (exportWhenExperimentFinishes) Export();
        }

        /// <summary>Exports the current session's CSV files. Returns false if there is no event log to read.</summary>
        public bool Export()
        {
            if (eventLogger == null || string.IsNullOrEmpty(eventLogger.CurrentLogPath) || !File.Exists(eventLogger.CurrentLogPath)) return false;
            try
            {
                var events = EventLogReader.Read(ReadLines(eventLogger.CurrentLogPath), out var skipped);
                var summary = ResearchTaskCatalog.CreateAggregator().Aggregate(events, skipped);
                eventLogger.WriteJsonFile(SessionCsvExporter.SummaryFileName, SessionCsvExporter.BuildSessionSummaryCsv(summary));
                LastSummaryPath = Path.Combine(eventLogger.CurrentSessionDirectory, SessionCsvExporter.SummaryFileName);
                if (exportEventsCsv)
                {
                    eventLogger.WriteJsonFile(SessionCsvExporter.EventsFileName, SessionCsvExporter.BuildEventsCsv(events));
                    LastEventsPath = Path.Combine(eventLogger.CurrentSessionDirectory, SessionCsvExporter.EventsFileName);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionDataExporter] Could not export the session data: {ex.Message}");
                return false;
            }
        }

        static string[] ReadLines(string path)
        {
            // The logger keeps the file open for writing, so it has to be opened as shared.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                var lines = new System.Collections.Generic.List<string>();
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
                return lines.ToArray();
            }
        }
    }
}
