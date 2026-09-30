using System;
using System.IO;
using UnityEngine;

namespace PlayCT.Research
{
    /// <summary>
    /// Owns the identity of the current research session (session, participant, condition).
    /// Every task and the <see cref="EventLogger"/> read it through <see cref="ISessionInfo"/>.
    /// Values come from the inspector, or from <c>session_config.json</c> in
    /// <see cref="Application.persistentDataPath"/> so the researcher can set them without rebuilding.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class SessionManager : MonoBehaviour, ISessionInfo
    {
        public const string ConfigFileName = "session_config.json";

        [Serializable]
        public class SessionConfig
        {
            public string participantId;
            public string condition;
            public int hanoiDiskCount;
        }

        [SerializeField] string participantId = "P000";
        [SerializeField] string condition = "baseline";
        [Tooltip("Number of Hanoi disks requested for the session (3, 4 or 5). 0 keeps the value set on the task.")]
        [SerializeField, Range(0, 5)] int hanoiDiskCount;
        [SerializeField] bool startSessionOnAwake = true;
        [SerializeField] bool loadConfigFile = true;

        public string SessionId { get; private set; } = string.Empty;
        public string ParticipantId => participantId;
        public string Condition => condition;
        public int HanoiDiskCount => hanoiDiskCount;
        public bool HasSession => !string.IsNullOrEmpty(SessionId);

        public event Action<ISessionInfo> SessionStarted;

        void Awake()
        {
            if (loadConfigFile) TryLoadConfig();
            if (startSessionOnAwake) BeginSession();
        }

        public void BeginSession(string newParticipantId = null, string newCondition = null)
        {
            if (!string.IsNullOrEmpty(newParticipantId)) participantId = newParticipantId;
            if (!string.IsNullOrEmpty(newCondition)) condition = newCondition;
            SessionId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
            SessionStarted?.Invoke(this);
        }

        public void SetCondition(string newCondition)
        {
            if (!string.IsNullOrEmpty(newCondition)) condition = newCondition;
        }

        public void SetHanoiDiskCount(int count) => hanoiDiskCount = Mathf.Clamp(count, 0, 5);

        void TryLoadConfig()
        {
            var path = Path.Combine(Application.persistentDataPath, ConfigFileName);
            if (!File.Exists(path)) return;
            try
            {
                var config = JsonUtility.FromJson<SessionConfig>(File.ReadAllText(path));
                if (config == null) return;
                if (!string.IsNullOrWhiteSpace(config.participantId)) participantId = config.participantId.Trim();
                if (!string.IsNullOrWhiteSpace(config.condition)) condition = config.condition.Trim();
                if (config.hanoiDiskCount > 0) hanoiDiskCount = Mathf.Clamp(config.hanoiDiskCount, 0, 5);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SessionManager] Could not read {path}: {ex.Message}");
            }
        }
    }
}
