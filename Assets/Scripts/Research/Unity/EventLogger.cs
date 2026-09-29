using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PlayCT.Research
{
    /// <summary>
    /// Shared research event log. Tasks send <see cref="ResearchEvent"/>s here and the logger adds the
    /// session envelope (session, participant, condition, task, timestamps) and appends one JSON line per event to
    /// <c>persistentDataPath/PlayCT/&lt;sessionId&gt;/events.jsonl</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class EventLogger : MonoBehaviour, IResearchEventSink
    {
        public const string EventsFileName = "events.jsonl";

        [SerializeField] SessionManager session;
        [SerializeField] string rootFolderName = "PlayCT";
        [Tooltip("Optional absolute folder that replaces persistentDataPath (used by automated tests).")]
        [SerializeField] string rootDirectoryOverride = "";

        readonly IClock clock = new UnityClock();
        EventLogWriter writer;
        string openSessionId;
        int closedWriterEvents;

        public string CurrentLogPath { get; private set; }
        public string CurrentSessionDirectory { get; private set; }
        public int EventCount => closedWriterEvents + (writer != null ? writer.EventCount : 0);

        public event Action<ResearchEvent> EventLogged;

        public string RootDirectoryOverride
        {
            get => rootDirectoryOverride;
            set => rootDirectoryOverride = value ?? string.Empty;
        }

        public SessionManager Session
        {
            get => session;
            set => session = value;
        }

        void Awake()
        {
            ResolveSession();
        }

        public void Log(ResearchEvent researchEvent)
        {
            if (researchEvent == null) return;
            if (!EnsureWriter()) return;
            writer.Log(researchEvent);
            EventLogged?.Invoke(researchEvent);
        }

        public void WriteJsonFile(string fileName, string json)
        {
            if (!EnsureWriter()) return;
            File.WriteAllText(Path.Combine(CurrentSessionDirectory, fileName), json, new UTF8Encoding(false));
        }

        void ResolveSession()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (session == null)
            {
                session = new GameObject("SessionManager").AddComponent<SessionManager>();
            }
            if (!session.HasSession) session.BeginSession();
        }

        bool EnsureWriter()
        {
            ResolveSession();
            if (writer != null && openSessionId == session.SessionId) return true;
            CloseWriter();

            try
            {
                var root = string.IsNullOrEmpty(rootDirectoryOverride) ? Application.persistentDataPath : rootDirectoryOverride;
                CurrentSessionDirectory = Path.Combine(root, rootFolderName, session.SessionId);
                Directory.CreateDirectory(CurrentSessionDirectory);
                CurrentLogPath = Path.Combine(CurrentSessionDirectory, EventsFileName);
                var stream = new FileStream(CurrentLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                writer = new EventLogWriter(session, new StreamWriter(stream, new UTF8Encoding(false)), clock);
                openSessionId = session.SessionId;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EventLogger] Cannot open the event log: {ex.Message}");
                writer = null;
                return false;
            }

            var started = new ResearchEvent("session", "session_started")
                .Add("app_version", Application.version)
                .Add("unity_version", Application.unityVersion)
                .Add("platform", Application.platform.ToString())
                .Add("device_model", SystemInfo.deviceModel)
                .Add("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            writer.Log(started);
            EventLogged?.Invoke(started);
            return true;
        }

        void CloseWriter()
        {
            if (writer == null) return;
            closedWriterEvents += writer.EventCount;
            writer.Dispose();
            writer = null;
            openSessionId = null;
        }

        void OnDestroy()
        {
            CloseWriter();
        }
    }
}
